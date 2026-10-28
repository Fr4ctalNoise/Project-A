// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 Fr4ctalNoise and Project A contributors.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.ProjectA.Api;
using Jellyfin.Plugin.ProjectA.Auth;
using Jellyfin.Plugin.ProjectA.Data;
using Jellyfin.Plugin.ProjectA.Users;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.ProjectA.Tests;

/// <summary>
/// The post and feed endpoints with a real database and a fake user list. The sign-in gate
/// itself is tested in SocialAccessTests and, against a real server, by the live tests; here
/// the caller is what the gate would have recorded.
/// </summary>
public sealed class PostsControllerTests : IDisposable
{
    private static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Admin = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Hidden = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid Disabled = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid Deleted = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "projecta-tests", Guid.NewGuid().ToString("N"));
    private readonly PostStore _posts;
    private readonly FakeUsers _users = new();
    private readonly FixedClock _clock = new();

    public PostsControllerTests()
    {
        _posts = new PostStore(new SocialStore(Path.Combine(_dir, SocialStore.FileName), NullLogger<SocialStore>.Instance));
        _users.Add(Alice, "alice");
        _users.Add(Bob, "bob", hasImage: true);
        _users.Add(Admin, "admin");
        _users.Add(Hidden, "hidden", hidden: true);
        _users.Add(Disabled, "disabled", disabled: true);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private PostsController As(Guid? user, bool admin = false)
    {
        var http = new DefaultHttpContext();
        if (user is not null)
        {
            http.Items[SocialUserFilter.UserIdItemKey] = user.Value;
            http.Items[SocialUserFilter.UserIsAdministratorItemKey] = admin;
        }

        return new PostsController(_posts, _users, _clock) { ControllerContext = new ControllerContext { HttpContext = http } };
    }

    private long Create(Guid author, string body)
    {
        var result = As(author).CreatePost(new CreatePostRequest(body));
        var created = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(201, created.StatusCode);
        _clock.Now = _clock.Now.AddSeconds(7); // stay well under the rate limit
        return Assert.IsType<PostView>(created.Value).Id;
    }

    private FeedResponse Feed(Guid viewer, bool admin = false, long? before = null, int? limit = null) =>
        As(viewer, admin).GetFeed(before, limit).Value!;

    private static string? ErrorCode(IActionResult? result) =>
        Assert.IsType<ErrorResponse>(Assert.IsAssignableFrom<ObjectResult>(result).Value).Error;

    [Fact]
    public void A_post_is_created_and_returned_with_its_author()
    {
        var result = As(Bob).CreatePost(new CreatePostRequest("Watching **Frieren** tonight"));
        var created = Assert.IsType<ObjectResult>(result.Result);
        var view = Assert.IsType<PostView>(created.Value);

        Assert.Equal(201, created.StatusCode);
        Assert.Equal(Bob, view.AuthorId);
        Assert.Equal("bob", view.AuthorName);
        Assert.True(view.AuthorHasImage);
        Assert.Equal("status", view.Kind);
        Assert.Equal("Watching **Frieren** tonight", view.Body);
        Assert.Equal(_clock.Now, view.CreatedAt);
        Assert.True(view.CanDelete);
    }

    [Fact]
    public void Line_endings_are_normalised_and_whitespace_trimmed()
    {
        var id = Create(Alice, "  line one\r\nline two\rline three \n\n ");
        Assert.Equal("line one\nline two\nline three", _posts.Get(id)!.Body);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \r\n\t  ")]
    public void Empty_bodies_are_refused_and_nothing_is_stored(string? body)
    {
        var bad = Assert.IsType<BadRequestObjectResult>(As(Alice).CreatePost(new CreatePostRequest(body)).Result);
        Assert.Equal(PostInput.BodyRequired, ErrorCode(bad));
        Assert.Empty(Feed(Alice).Posts);
    }

    /// <summary>
    /// Control characters and broken UTF-16. Kept out of [InlineData] on purpose: test runners
    /// put case values into XML result files, where these characters are illegal.
    /// </summary>
    [Fact]
    public void Control_characters_and_broken_unicode_are_refused_and_nothing_is_stored()
    {
        var cases = new[]
        {
            "nul\0inside", "bell\u0007", "escape \u001b[31m red", "c1 \u0085 control", "del \u007f",
            "lone high \ud800 surrogate", "lone low \udc00 surrogate", "ends with high \ud83c",
            "reversed pair \udc00\ud800", "vertical\u000btab", "form\u000cfeed",
        };

        for (var i = 0; i < cases.Length; i++)
        {
            var result = As(Alice).CreatePost(new CreatePostRequest(cases[i])).Result;
            Assert.True(result is BadRequestObjectResult, $"case {i} was not refused");
            Assert.Equal(PostInput.BodyInvalid, ErrorCode(result));
        }

        // At the very ends, vertical tab and form feed are whitespace and simply trimmed away.
        Assert.Equal("edge", _posts.Get(Create(Alice, "\u000bedge\u000c"))!.Body);

        Assert.Single(Feed(Alice).Posts); // only the trimmed "edge" post
    }

    [Fact]
    public void A_missing_request_body_is_refused()
    {
        var bad = Assert.IsType<BadRequestObjectResult>(As(Alice).CreatePost(null!).Result);
        Assert.Equal(PostInput.BodyRequired, ErrorCode(bad));
    }

    [Fact]
    public void Exactly_4000_characters_is_accepted_and_4001_refused()
    {
        Create(Alice, new string('a', 4000));
        var tooLong = As(Alice).CreatePost(new CreatePostRequest(new string('b', 4001)));
        Assert.Equal(PostInput.BodyTooLong, ErrorCode(Assert.IsType<BadRequestObjectResult>(tooLong.Result)));

        // A huge body is refused before any copying work.
        var huge = As(Alice).CreatePost(new CreatePostRequest(new string('c', 1_000_000)));
        Assert.Equal(PostInput.BodyTooLong, ErrorCode(Assert.IsType<BadRequestObjectResult>(huge.Result)));
    }

    [Fact]
    public void Tabs_emoji_and_other_scripts_are_fine()
    {
        var body = "tab\there 🎃🧛 日本語 שלום\nnext line";
        Assert.Equal(body, _posts.Get(Create(Alice, body))!.Body);
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("[click](javascript:alert(1))")]
    public void Html_and_script_in_markdown_are_stored_as_plain_text_for_the_page_to_render_safely(string body)
    {
        var id = Create(Alice, body);
        Assert.Equal(body, Feed(Bob).Posts.Single(p => p.Id == id).Body);
    }

    [Fact]
    public void Posting_too_fast_gets_429_with_retry_after_and_stores_nothing()
    {
        for (var i = 0; i < PostStore.MaxPostsPerMinute; i++)
        {
            Assert.Equal(201, ((ObjectResult)As(Alice).CreatePost(new CreatePostRequest($"p{i}")).Result!).StatusCode);
        }

        var controller = As(Alice);
        var refused = Assert.IsType<ObjectResult>(controller.CreatePost(new CreatePostRequest("flood")).Result);
        Assert.Equal(429, refused.StatusCode);
        Assert.Equal("rate_limited", ((ErrorResponse)refused.Value!).Error);
        Assert.Equal("60", controller.Response.Headers.RetryAfter.ToString());
        Assert.DoesNotContain(Feed(Alice, limit: 50).Posts, p => p.Body == "flood");
    }

    [Fact]
    public void Feed_pages_with_the_cursor_and_ends_with_null()
    {
        var ids = Enumerable.Range(0, 7).Select(i => Create(Alice, $"p{i}")).ToList();

        var first = Feed(Bob, limit: 3);
        var second = Feed(Bob, before: first.NextBefore, limit: 3);
        var third = Feed(Bob, before: second.NextBefore, limit: 3);

        Assert.Equal(ids.AsEnumerable().Reverse(), first.Posts.Concat(second.Posts).Concat(third.Posts).Select(p => p.Id));
        Assert.NotNull(first.NextBefore);
        Assert.Null(third.NextBefore);
    }

    [Fact]
    public void Feed_default_page_is_20()
    {
        for (var i = 0; i < 25; i++)
        {
            Create(Alice, $"p{i}");
        }

        Assert.Equal(PostsController.DefaultPageSize, Feed(Bob).Posts.Count);
    }

    [Theory]
    [InlineData(null, 0, "limit_invalid")]
    [InlineData(null, -1, "limit_invalid")]
    [InlineData(null, 51, "limit_invalid")]
    [InlineData(null, int.MaxValue, "limit_invalid")]
    [InlineData(0L, null, "before_invalid")]
    [InlineData(-5L, null, "before_invalid")]
    [InlineData(long.MinValue, null, "before_invalid")]
    public void Bad_paging_values_are_refused(long? before, int? limit, string code)
    {
        var result = As(Bob).GetFeed(before, limit);
        Assert.Equal(code, ErrorCode(Assert.IsType<BadRequestObjectResult>(result.Result)));
    }

    [Fact]
    public void Posts_by_hidden_and_disabled_users_show_only_to_admins_and_deleted_users_to_no_one()
    {
        var visible = Create(Alice, "visible");
        var hidden = Create(Hidden, "hidden");
        var disabled = _posts.CreateStatus(Disabled, "posted before being disabled", _clock.Now).Id;
        _users.Add(Deleted, "deleted");
        var deleted = Create(Deleted, "posted before being deleted");
        _users.Remove(Deleted);

        Assert.Equal(new[] { visible }, Feed(Bob).Posts.Select(p => p.Id));
        Assert.Equal(new[] { visible }, Feed(Hidden).Posts.Select(p => p.Id)); // hidden users don't see their own either
        Assert.Equal(new[] { disabled, hidden, visible }, Feed(Admin, admin: true).Posts.Select(p => p.Id));
        Assert.DoesNotContain(Feed(Admin, admin: true).Posts, p => p.Id == deleted);
    }

    [Fact]
    public void Can_delete_is_true_only_for_the_author_and_admins()
    {
        Create(Alice, "mine");
        Assert.True(Feed(Alice).Posts.Single().CanDelete);
        Assert.False(Feed(Bob).Posts.Single().CanDelete);
        Assert.True(Feed(Admin, admin: true).Posts.Single().CanDelete);
    }

    [Fact]
    public void Author_can_delete_their_own_post()
    {
        var id = Create(Alice, "oops");
        Assert.IsType<NoContentResult>(As(Alice).DeletePost(id));
        Assert.Empty(Feed(Alice).Posts);
        Assert.IsType<NotFoundResult>(As(Alice).DeletePost(id)); // already gone
    }

    [Fact]
    public void Another_user_cannot_delete_a_post_and_it_stays()
    {
        var id = Create(Alice, "keep");
        var refused = Assert.IsType<StatusCodeResult>(As(Bob).DeletePost(id));
        Assert.Equal(403, refused.StatusCode);
        Assert.NotNull(_posts.Get(id));
    }

    [Fact]
    public void An_admin_can_delete_anyones_post()
    {
        var id = Create(Alice, "moderated");
        Assert.IsType<NoContentResult>(As(Admin, admin: true).DeletePost(id));
        Assert.Null(_posts.Get(id));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(999_999L)]
    [InlineData(long.MaxValue)]
    public void Deleting_a_post_that_does_not_exist_is_404(long id) =>
        Assert.IsType<NotFoundResult>(As(Admin, admin: true).DeletePost(id));

    [Fact]
    public void Without_a_caller_recorded_by_the_gate_every_endpoint_refuses()
    {
        var id = Create(Alice, "x");
        Assert.IsType<UnauthorizedResult>(As(null).GetFeed(null, null).Result);
        Assert.IsType<UnauthorizedResult>(As(null).CreatePost(new CreatePostRequest("y")).Result);
        Assert.IsType<UnauthorizedResult>(As(null).DeletePost(id));
        Assert.NotNull(_posts.Get(id));
    }

    [Fact]
    public void A_user_deleted_between_the_gate_and_the_post_is_refused()
    {
        var ghost = Guid.NewGuid();
        Assert.IsType<UnauthorizedResult>(As(ghost).CreatePost(new CreatePostRequest("boo")).Result);
        Assert.Empty(Feed(Admin, admin: true).Posts);
    }

    [Fact]
    public void Responses_are_not_cached()
    {
        var controller = As(Bob);
        controller.GetFeed(null, null);
        Assert.Equal("no-store", controller.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public void Database_errors_become_a_fixed_503_without_exception_text()
    {
        var filter = new DatabaseUnavailableFilter(NullLogger<DatabaseUnavailableFilter>.Instance);
        var context = new ExceptionContext(
            new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>())
        {
            Exception = new SqliteException("database is locked at /config/plugins/configurations/ProjectA/social.db", 5),
        };

        filter.OnException(context);

        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(503, result.StatusCode);
        Assert.Equal(new ErrorResponse("database_unavailable", "locked"), result.Value);
        Assert.True(context.ExceptionHandled);
    }

    [Fact]
    public void Store_unavailable_becomes_503_with_its_problem_code()
    {
        var context = FilterContext(new StoreUnavailableException("not_writable", new IOException("/config/... is read-only")));
        new DatabaseUnavailableFilter(NullLogger<DatabaseUnavailableFilter>.Instance).OnException(context);
        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(503, result.StatusCode);
        Assert.Equal(new ErrorResponse("database_unavailable", "not_writable"), result.Value);
    }

    /// <summary>
    /// ASP.NET reports an oversized request body as BadHttpRequestException, which is an
    /// IOException. It must come back as 413, not as a database fault or a 500.
    /// </summary>
    [Fact]
    public void An_oversized_request_is_413_not_a_database_fault()
    {
        var context = FilterContext(new BadHttpRequestException("Request body too large.", 413));
        new DatabaseUnavailableFilter(NullLogger<DatabaseUnavailableFilter>.Instance).OnException(context);
        Assert.Equal(413, Assert.IsType<StatusCodeResult>(context.Result).StatusCode);
    }

    [Fact]
    public void Other_exceptions_are_left_to_jellyfin()
    {
        foreach (var ex in new Exception[]
        {
            new InvalidOperationException("boom"),
            new IOException("client disconnected"),
        })
        {
            var context = FilterContext(ex);
            new DatabaseUnavailableFilter(NullLogger<DatabaseUnavailableFilter>.Instance).OnException(context);
            Assert.Null(context.Result);
            Assert.False(context.ExceptionHandled);
        }
    }

    [Fact]
    public void A_database_folder_that_cannot_be_created_is_reported_as_unavailable_not_as_an_io_error()
    {
        var blocker = Path.Combine(_dir, "blocker");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(blocker, "a file where the folder should be");
        var store = new PostStore(new SocialStore(Path.Combine(blocker, "sub", SocialStore.FileName), NullLogger<SocialStore>.Instance));

        var ex = Assert.Throws<StoreUnavailableException>(() => store.Get(1));
        Assert.Equal("not_writable", ex.Problem);
    }

    private static ExceptionContext FilterContext(Exception ex) => new(
        new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()),
        new List<IFilterMetadata>())
    {
        Exception = ex,
    };

    private sealed class FixedClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeUsers : IUserDirectory
    {
        private readonly Dictionary<Guid, (AuthorInfo Info, bool Hidden, bool Disabled)> _all = [];

        public void Add(Guid id, string name, bool hasImage = false, bool hidden = false, bool disabled = false) =>
            _all[id] = (new AuthorInfo(id, name, hasImage), hidden, disabled);

        public void Remove(Guid id) => _all.Remove(id);

        public IReadOnlyDictionary<Guid, AuthorInfo> VisibleAuthors(bool viewerIsAdministrator) =>
            _all.Values
                .Where(u => viewerIsAdministrator || (!u.Hidden && !u.Disabled))
                .ToDictionary(u => u.Info.Id, u => u.Info);
    }
}
