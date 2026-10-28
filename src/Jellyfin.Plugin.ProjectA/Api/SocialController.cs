// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 Fr4ctalNoise and Project A contributors.

using System;
using System.IO;
using System.Security.Cryptography;
using Jellyfin.Plugin.ProjectA.Auth;
using Jellyfin.Plugin.ProjectA.Data;
using Jellyfin.Plugin.ProjectA.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Plugin.ProjectA.Api;

/// <summary>
/// Project A's HTTP endpoints, all under /Social.
/// Every endpoint carries [Authorize] + [SocialUser] except <see cref="GetScript"/>;
/// the EndpointAccessTests build test enforces that for every endpoint added later.
/// </summary>
[ApiController]
[Route("Social")]
public sealed class SocialController : ControllerBase
{
    /// <summary>Embedded name of the browser script.</summary>
    public const string ScriptResourceName = "Jellyfin.Plugin.ProjectA.Web.client.projectA.js";

    private static readonly Lazy<(byte[] Bytes, string ETag)> Script = new(LoadScript);

    private readonly SocialStore _store;
    private readonly InjectionStatus _injection;

    /// <summary>Initializes a new instance of the <see cref="SocialController"/> class.</summary>
    /// <param name="store">The database.</param>
    /// <param name="injection">Whether the page script has been injected.</param>
    public SocialController(SocialStore store, InjectionStatus injection)
    {
        _store = store;
        _injection = injection;
    }

    /// <summary>
    /// The browser script. The only anonymous endpoint: it is code with no data,
    /// it does nothing until someone is signed in, and the web page must be able
    /// to load it on the sign-in screen.
    /// </summary>
    /// <returns>The script, or 304 when the browser's copy is current.</returns>
    [HttpGet("script")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    public IActionResult GetScript()
    {
        var (bytes, etag) = Script.Value;
        Response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";
        Response.Headers[HeaderNames.CacheControl] = "no-cache";
        Response.Headers[HeaderNames.ETag] = etag;

        if (Request.Headers.IfNoneMatch.ToString() == etag)
        {
            return StatusCode(StatusCodes.Status304NotModified);
        }

        return File(bytes, "application/javascript; charset=utf-8");
    }

    /// <summary>
    /// Health of the plugin for administrators and for je-review-notifier's health
    /// watch (which calls with an API key). Status only: no users, posts or names.
    /// </summary>
    /// <returns>Plugin version, database state and injection state.</returns>
    [HttpGet("Health")]
    [Authorize]
    [SocialUser(AllowApiKey = true, RequireAdministrator = true)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public ActionResult<HealthResponse> GetHealth()
    {
        var database = _store.CheckHealth();
        Response.Headers[HeaderNames.CacheControl] = "no-store";
        return new HealthResponse(
            Version: typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "unknown",
            DatabaseOk: database.Ok,
            DatabaseProblem: database.Problem,
            SchemaVersion: database.SchemaVersion,
            ScriptInjectedSinceStart: _injection.LastInjected is not null,
            ScriptInjectionDisabled: Plugin.Instance?.Configuration.DisableScriptInjection == true);
    }

    private static (byte[] Bytes, string ETag) LoadScript()
    {
        using var stream = typeof(SocialController).Assembly.GetManifestResourceStream(ScriptResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {ScriptResourceName} is missing from the build.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var bytes = memory.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).Substring(0, 16).ToLowerInvariant();
        return (bytes, $"\"{hash}\"");
    }
}

/// <summary>The health check's answer.</summary>
/// <param name="Version">Installed Project A version.</param>
/// <param name="DatabaseOk">The database could be written and read back.</param>
/// <param name="DatabaseProblem">A fixed problem code when not ok (see <see cref="SocialStore"/>).</param>
/// <param name="SchemaVersion">Database schema version.</param>
/// <param name="ScriptInjectedSinceStart">The web page has been served with Project A's script since Jellyfin started.</param>
/// <param name="ScriptInjectionDisabled">The emergency switch in the plugin settings is on.</param>
public sealed record HealthResponse(
    string Version,
    bool DatabaseOk,
    string? DatabaseProblem,
    int SchemaVersion,
    bool ScriptInjectedSinceStart,
    bool ScriptInjectionDisabled);
