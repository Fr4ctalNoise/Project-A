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
}
