// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 Fr4ctalNoise and Project A contributors.

using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.ProjectA.Auth;
using Jellyfin.Plugin.ProjectA.Data;
using Jellyfin.Plugin.ProjectA.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Plugin.ProjectA.Api;

/// <summary>
/// Text posts and the feed (build step 2). All under /Social, all behind
/// <c>[Authorize]</c> + <c>[SocialUser]</c> (signed-in, existing, enabled users; no API keys).
/// </summary>
/// <remarks>
/// <para>JSON uses Jellyfin's own settings (PascalCase names, Guids as 32 hex digits).</para>
/// <para>Who may do what: anyone signed in may post and read; a post can be deleted by its
/// author or an administrator. Which authors appear is decided by <see cref="IUserDirectory"/>.</para>
/// </remarks>
[ApiController]
[Route("Social")]
[TypeFilter(typeof(DatabaseUnavailableFilter))]
public sealed class PostsController : ControllerBase
{
    /// <summary>Feed page size when the page doesn't ask for one.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>Largest request body accepted for a post: 4,000 characters is at most 12 KB of UTF-8, plus JSON escaping.</summary>
    public const int MaxRequestBytes = 64 * 1024;

    private readonly PostStore _posts;
    private readonly IUserDirectory _users;
    private readonly TimeProvider _clock;

    /// <summary>Initializes a new instance of the <see cref="PostsController"/> class.</summary>
    /// <param name="posts">Post storage.</param>
    /// <param name="users">Which authors a viewer may see.</param>
    /// <param name="clock">Time source (tests use a fixed clock).</param>
    public PostsController(PostStore posts, IUserDirectory users, TimeProvider clock)
    {
        _posts = posts;
        _users = users;
        _clock = clock;
    }

    /// <summary>The feed: newest posts first, only from authors this viewer may see.</summary>
    /// <param name="before">Cursor: only posts older than this id (the previous page's <c>NextBefore</c>).</param>
    /// <param name="limit">Page size, 1 to 50 (default 20).</param>
    /// <returns>A page of posts and the cursor for the next one (null at the end).</returns>
    [HttpGet("Feed")]
    [Authorize]
    [SocialUser]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<FeedResponse> GetFeed([FromQuery] long? before, [FromQuery] int? limit)
    {
        if (!TryGetCaller(out var userId, out var isAdmin))
        {
            return Unauthorized();
        }

        var size = limit ?? DefaultPageSize;
        if (size < 1 || size > PostStore.MaxPageSize)
        {
            return BadRequest(new ErrorResponse("limit_invalid"));
        }

        if (before is <= 0)
        {
            return BadRequest(new ErrorResponse("before_invalid"));
        }

        var authors = _users.VisibleAuthors(isAdmin);
        var rows = _posts.ListFeed(authors.Keys.ToList(), before, size);
        var posts = rows.Select(r => View(r, authors[r.AuthorId], userId, isAdmin)).ToList();

        Response.Headers[HeaderNames.CacheControl] = "no-store";
        return new FeedResponse(posts, rows.Count == size ? rows[^1].Id : null);
    }

    /// <summary>Creates a status post (markdown text).</summary>
    /// <param name="request">The post.</param>
    /// <returns>201 with the new post; 400 with an error code; 429 when posting too fast.</returns>
    [HttpPost("Posts")]
    [Authorize]
    [SocialUser]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxRequestBytes)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public ActionResult<PostView> CreatePost([FromBody] CreatePostRequest request)
    {
        if (!TryGetCaller(out var userId, out var isAdmin))
        {
            return Unauthorized();
        }

        var error = PostInput.ValidatePost(request?.Body, out var body);
        if (error is not null)
        {
            return BadRequest(new ErrorResponse(error));
        }

        // The author must still be visible to themselves (a hidden account can post, but its
        // posts only show to admins). Look the name up the same way the feed does.
        var everyone = _users.VisibleAuthors(viewerIsAdministrator: true);
        if (!everyone.TryGetValue(userId, out var author))
        {
            return Unauthorized(); // the account vanished between the gate and here
        }

        var result = _posts.CreateStatus(userId, body, _clock.GetUtcNow());
        if (result.RateLimited)
        {
            Response.Headers[HeaderNames.RetryAfter] = "60";
            return StatusCode(StatusCodes.Status429TooManyRequests, new ErrorResponse("rate_limited"));
        }

        var row = _posts.Get(result.Id)!;
        Response.Headers[HeaderNames.CacheControl] = "no-store";
        return StatusCode(StatusCodes.Status201Created, View(row, author, userId, isAdmin));
    }

    /// <summary>Deletes a post. Only its author or an administrator may.</summary>
    /// <param name="id">Post id.</param>
    /// <returns>204; 403 if not allowed; 404 if there is no such post.</returns>
    [HttpDelete("Posts/{id:long}")]
    [Authorize]
    [SocialUser]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult DeletePost([FromRoute] long id)
    {
        if (!TryGetCaller(out var userId, out var isAdmin))
        {
            return Unauthorized();
        }

        var post = _posts.Get(id);
        if (post is null)
        {
            return NotFound();
        }

        if (post.AuthorId != userId && !isAdmin)
        {
            return Forbid403();
        }

        return _posts.SoftDelete(id, _clock.GetUtcNow()) ? NoContent() : NotFound();
    }

    /// <summary>
    /// The caller checked by <see cref="SocialUserFilter"/>. False only if that filter didn't
    /// run, which the endpoint build test prevents; refusing is the safe answer anyway.
    /// </summary>
    private bool TryGetCaller(out Guid userId, out bool isAdministrator)
    {
        var items = HttpContext?.Items;
        userId = items?[SocialUserFilter.UserIdItemKey] is Guid id ? id : Guid.Empty;
        isAdministrator = items?[SocialUserFilter.UserIsAdministratorItemKey] is true;
        return userId != Guid.Empty;
    }

    // Forbid() would go through the authentication handler's challenge; a plain 403 is what we mean.
    private StatusCodeResult Forbid403() => StatusCode(StatusCodes.Status403Forbidden);

    private static PostView View(PostRow row, AuthorInfo author, Guid viewerId, bool viewerIsAdmin) => new(
        row.Id,
        author.Id,
        author.Name,
        author.HasImage,
        row.Kind,
        row.Body,
        row.CreatedAt,
        CanDelete: row.AuthorId == viewerId || viewerIsAdmin);
}

/// <summary>Body of <c>POST /Social/Posts</c>.</summary>
/// <param name="Body">Markdown text, up to 4,000 characters.</param>
public sealed record CreatePostRequest(string? Body);

/// <summary>One post as the page receives it. <c>Body</c> is markdown, to be rendered safely by the page.</summary>
/// <param name="Id">Post id.</param>
/// <param name="AuthorId">Author's Jellyfin user id.</param>
/// <param name="AuthorName">Author's user name.</param>
/// <param name="AuthorHasImage">The author has a profile picture.</param>
/// <param name="Kind">Post kind (status for now).</param>
/// <param name="Body">Markdown source.</param>
/// <param name="CreatedAt">When it was posted (UTC).</param>
/// <param name="CanDelete">The viewer may delete it (author or admin), so the page shows the delete action.</param>
public sealed record PostView(
    long Id,
    Guid AuthorId,
    string AuthorName,
    bool AuthorHasImage,
    string Kind,
    string Body,
    DateTimeOffset CreatedAt,
    bool CanDelete);

/// <summary>A page of the feed.</summary>
/// <param name="Posts">Posts, newest first.</param>
/// <param name="NextBefore">Pass as <c>before</c> to get the next page; null when there are no more.</param>
public sealed record FeedResponse(IReadOnlyList<PostView> Posts, long? NextBefore);
