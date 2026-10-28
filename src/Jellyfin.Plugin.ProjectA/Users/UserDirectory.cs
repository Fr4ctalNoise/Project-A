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
    /// The authors whose posts this viewer may see. Deleted accounts are never included.
    /// Disabled and hidden accounts are included only for administrators (design doc:
    /// "posts by hidden or disabled users are hidden from everyone but admins").
    /// </summary>
    /// <param name="viewerIsAdministrator">The viewer is an administrator.</param>
    /// <returns>Visible authors by id.</returns>
    IReadOnlyDictionary<Guid, AuthorInfo> VisibleAuthors(bool viewerIsAdministrator);
}

/// <summary>
/// <see cref="IUserDirectory"/> backed by Jellyfin's user manager. Read fresh on every call:
/// a server has a handful of users, and a just-disabled account must vanish from the feed at once.
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
    public IReadOnlyDictionary<Guid, AuthorInfo> VisibleAuthors(bool viewerIsAdministrator)
    {
        var result = new Dictionary<Guid, AuthorInfo>();
        foreach (var user in _userManager.GetUsers())
        {
            if (!viewerIsAdministrator
                && (user.HasPermission(PermissionKind.IsDisabled) || user.HasPermission(PermissionKind.IsHidden)))
            {
                continue;
            }

            result[user.Id] = new AuthorInfo(user.Id, user.Username, user.ProfileImage is not null);
        }

        return result;
    }
}
