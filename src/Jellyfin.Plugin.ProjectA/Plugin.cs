// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 Fr4ctalNoise and Project A contributors.

using System;
using System.Collections.Generic;
using Jellyfin.Plugin.ProjectA.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.ProjectA;

/// <summary>
/// Project A: an AniList-style social feed for one Jellyfin server.
/// This class only identifies the plugin and its settings page; the work is done by
/// the services registered in <see cref="PluginServiceRegistrator"/>.
/// </summary>
public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>The display name, also used in the injected script tag.</summary>
    public const string PluginName = "Project A";

    /// <summary>The plugin's permanent id. Never change it: Jellyfin keys installs and settings on it.</summary>
    public static readonly Guid PluginId = new("0fe4dc69-b52d-40bc-ad8d-ff518bad55a3");

    /// <summary>Initializes a new instance of the <see cref="Plugin"/> class.</summary>
    /// <param name="applicationPaths">Jellyfin's paths.</param>
    /// <param name="xmlSerializer">Jellyfin's XML serializer, used for the settings file.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <summary>Gets the running instance, or null before Jellyfin has created it.</summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public override string Name => PluginName;

    /// <inheritdoc />
    public override Guid Id => PluginId;

    /// <inheritdoc />
    public override string Description => "A social feed for this server: watch activity, posts, comments and likes.";

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = "ProjectA",
                EmbeddedResourcePath = "Jellyfin.Plugin.ProjectA.Configuration.configPage.html",
            },
        ];
    }
}
