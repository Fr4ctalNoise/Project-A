// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 Fr4ctalNoise and Project A contributors.

using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.ProjectA.Configuration;

/// <summary>
/// Project A's settings, stored by Jellyfin in plugins/configurations/Jellyfin.Plugin.ProjectA.xml.
/// </summary>
public sealed class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets a value indicating whether the page script injection is turned off.
    /// An emergency switch: with it on, Jellyfin's web page is served untouched and
    /// Project A's button and page disappear, while the server side keeps running.
    /// </summary>
    public bool DisableScriptInjection { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether posts by users hidden from the login screen are
    /// left out of the feed for everyone except administrators and the author.
    /// Off by default: Jellyfin hides every new user from the login screen unless told
    /// otherwise, so "hidden" usually says nothing about whether someone wants to be seen
    /// (decided 2026-10-28; Jellyfin Enhanced's equivalent setting is on by default).
    /// </summary>
    public bool HidePostsFromHiddenUsers { get; set; }
}
