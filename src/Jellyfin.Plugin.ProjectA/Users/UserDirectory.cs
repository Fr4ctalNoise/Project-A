// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 Fr4ctalNoise and Project A contributors.

using System;
using System.Collections.Generic;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.ProjectA.Users;

/// <summary>What the feed shows about a post's author. Nothing else about the account leaves the server.</summary>
/// <param name="Id">Jellyfin user id.</param>
/// <param name="Name">User name as Jellyfin shows it.</param>
/// <param name="HasImage">The user has a profile picture (so the page asks Jellyfin for it instead of drawing an initial).</param>
public sealed record AuthorInfo(Guid Id, string Name, bool HasImage);

/// <summary>
/// Which users' posts a viewer may see. One place for the rule, so every feature that lists
/// posts (feed, title page, Recommended tab) applies it the same way.
/// </summary>
public interface IUserDirectory
{
    /// <summary>
    /// The authors whose posts this viewer may see, by <see cref="AuthorVisibility.CanSee"/>.
    /// Deleted accounts are never included.
    /// </summary>
    /// <param name="viewerId">The viewer.</param>
    /// <param name="viewerIsAdministrator">The viewer is an administrator.</param>
    /// <returns>Visible authors by id.</returns>
    IReadOnlyDictionary<Guid, AuthorInfo> VisibleAuthors(Guid viewerId, bool viewerIsAdministrator);

    /// <summary>Looks up one existing account, whatever its state (used to name a new post's author).</summary>
    /// <param name="userId">User id.</param>
    /// <returns>The author, or null if the account doesn't exist.</returns>
    AuthorInfo? Find(Guid userId);
}

/// <summary>
/// The one rule for whose posts a viewer sees. Shared by every feature that lists posts, and
/// tested directly in AuthorVisibilityTests.
/// </summary>
public static class AuthorVisibility
{
    /// <summary>
    /// Administrators see everyone. Everyone sees their own posts. Otherwise disabled authors
    /// are left out, and so are authors hidden from the login screen when the admin setting
    /// <c>HidePostsFromHiddenUsers</c> is on (it is off by default). The same order as Jellyfin
    /// Enhanced's ShouldHideAuthor.
    /// </summary>
    /// <param name="authorId">Author.</param>
    /// <param name="authorDisabled">The author's account is disabled.</param>
    /// <param name="authorHidden">The author is hidden from the login screen.</param>
    /// <param name="viewerId">Viewer.</param>
    /// <param name="viewerIsAdministrator">The viewer is an administrator.</param>
    /// <param name="hidePostsFromHiddenUsers">The admin setting.</param>
    /// <returns>True if the viewer sees the author's posts.</returns>
    public static bool CanSee(Guid authorId, bool authorDisabled, bool authorHidden, Guid viewerId, bool viewerIsAdministrator, bool hidePostsFromHiddenUsers)
    {
        if (viewerIsAdministrator || authorId == viewerId)
        {
            return true;
        }

        if (authorDisabled)
        {
            return false;
        }

        return !(hidePostsFromHiddenUsers && authorHidden);
    }
}

/// <summary>
/// <see cref="IUserDirectory"/> backed by Jellyfin's user manager and Project A's settings.
/// Read fresh on every call: a server has a handful of users, and a just-disabled account (or a
/// changed setting) must take effect at once.
/// </summary>
public sealed class JellyfinUserDirectory : IUserDirectory
{
    private readonly IUserManager _userManager;

    /// <summary>Initializes a new instance of the <see cref="JellyfinUserDirectory"/> class.</summary>
    /// <param name="userManager">Jellyfin's users.</param>
    public JellyfinUserDirectory(IUserManager userManager)
    {
        _userManager = userManager;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<Guid, AuthorInfo> VisibleAuthors(Guid viewerId, bool viewerIsAdministrator)
    {
        var hideHidden = Plugin.Instance?.Configuration.HidePostsFromHiddenUsers ?? false;
        var result = new Dictionary<Guid, AuthorInfo>();
        foreach (var user in _userManager.GetUsers())
        {
            if (AuthorVisibility.CanSee(
                user.Id,
                user.HasPermission(PermissionKind.IsDisabled),
                user.HasPermission(PermissionKind.IsHidden),
                viewerId,
                viewerIsAdministrator,
                hideHidden))
            {
                result[user.Id] = new AuthorInfo(user.Id, user.Username, user.ProfileImage is not null);
            }
        }

        return result;
    }

    /// <inheritdoc />
    public AuthorInfo? Find(Guid userId)
    {
        var user = userId == Guid.Empty ? null : _userManager.GetUserById(userId);
        return user is null ? null : new AuthorInfo(user.Id, user.Username, user.ProfileImage is not null);
    }
}
