// SPDX-License-Identifier: GPL-3.0-only
// Adapted from Jellyfin Enhanced (GPL-3.0), commit d822e14:
//   Jellyfin.Plugin.JellyfinEnhanced/Services/ScriptInjectionStartupFilter.cs
// Copyright (c) n00bcodr and Jellyfin Enhanced contributors.
// Copyright (c) 2026 Fr4ctalNoise and Project A contributors.
// Changes: injects ../Social/script instead of ../JellyfinEnhanced/script; the
// tag is built here (escaped) rather than by the plugin class; records that an
// injection happened for the health check; uses ILogger; the request-path test
// and the HTML rewrite are separate static methods so they can be unit tested.

using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ProjectA.Web;

/// <summary>
/// Adds Project A's script tag to jellyfin-web's index.html as it is served.
/// Nothing is written to disk, so jellyfin-web updates can't remove it and no
/// writable web folder is needed.
/// </summary>
/// <remarks>
/// Defensive in the same ways as Jellyfin Enhanced's filter:
/// it only touches a 200 text/html response for the web app shell; it never
/// injects twice (another filter, or a leftover on-disk tag, may already have);
/// if anything goes wrong it serves the original page; and it can be switched
/// off in the plugin settings (<c>DisableScriptInjection</c>).
/// </remarks>
public sealed class ScriptInjectionStartupFilter : IStartupFilter
{
    /// <summary>The path fragment that identifies our tag, used for the "already injected" check.</summary>
    public const string ScriptPathMarker = "/Social/script";

    private readonly ILogger<ScriptInjectionStartupFilter> _logger;
    private readonly InjectionStatus _status;
    private int _loggedOnce;

    /// <summary>Initializes a new instance of the <see cref="ScriptInjectionStartupFilter"/> class.</summary>
    /// <param name="logger">Logger.</param>
    /// <param name="status">Shared record of whether injection has happened, read by the health check.</param>
    public ScriptInjectionStartupFilter(ILogger<ScriptInjectionStartupFilter> logger, InjectionStatus status)
    {
        _logger = logger;
        _status = status;
    }

    /// <inheritdoc />
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            // Registered before the rest of the pipeline so it runs outermost: removing
            // Accept-Encoding below then reliably yields an uncompressed body to rewrite.
            app.Use(InvokeAsync);
            next(app);
        };
    }

    /// <summary>
    /// True when the request is for the web app shell: "/web", "/web/" or "/web/index.html",
    /// also under a base-URL prefix such as "/jellyfin/web/".
    /// </summary>
    /// <param name="path">The request path.</param>
    /// <returns>Whether this filter should handle the request.</returns>
    public static bool IsIndexRequest(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        return path.EndsWith("/web/index.html", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/web/", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/web", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Builds the script tag. The version is HTML-encoded although it only ever holds digits and dots.</summary>
    /// <param name="version">The plugin version, used as a cache-buster.</param>
    /// <returns>The tag to insert before &lt;/body&gt;.</returns>
    public static string BuildScriptTag(string version)
    {
        var v = WebUtility.HtmlEncode(version);
        return $"<script plugin=\"{Plugin.PluginName}\" version=\"{v}\" src=\"../Social/script?v={WebUtility.UrlEncode(version)}\" defer></script>";
    }

    /// <summary>
    /// Inserts the tag before the last &lt;/body&gt;, unless our script is already referenced
    /// or the page has no &lt;/body&gt;.
    /// </summary>
    /// <param name="html">The page.</param>
    /// <param name="tag">The tag from <see cref="BuildScriptTag"/>.</param>
    /// <param name="injected">Whether the tag was inserted.</param>
    /// <returns>The page, changed or not.</returns>
    public static string InjectInto(string html, string tag, out bool injected)
    {
        injected = false;
        if (html.Contains(ScriptPathMarker, StringComparison.OrdinalIgnoreCase))
        {
            return html;
        }

        var bodyClose = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        if (bodyClose < 0)
        {
            return html;
        }

        injected = true;
        return string.Concat(html.AsSpan(0, bodyClose), tag, "\n", html.AsSpan(bodyClose));
    }

    private async Task InvokeAsync(HttpContext context, Func<Task> next)
    {
        if (!IsIndexRequest(context.Request.Path.Value) || !HttpMethods.IsGet(context.Request.Method))
        {
            // HEAD/OPTIONS must pass straight through so the host computes correct headers.
            await next().ConfigureAwait(false);
            return;
        }

        if (Plugin.Instance?.Configuration.DisableScriptInjection == true)
        {
            await next().ConfigureAwait(false);
            return;
        }

        // Ask for a complete, uncompressed 200 that can be rewritten: no compression,
        // and no Range (a 206 partial response would pass through un-injected).
        context.Request.Headers.Remove("Accept-Encoding");
        context.Request.Headers.Remove("Range");
        context.Request.Headers.Remove("If-Range");

        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await next().ConfigureAwait(false);
        }
        catch
        {
            // A downstream failure is not ours to swallow. The real response hasn't
            // started, so the host's error handler can still produce a clean error.
            context.Response.Body = originalBody;
            throw;
        }

        context.Response.Body = originalBody;
        buffer.Seek(0, SeekOrigin.Begin);

        var isHtml = context.Response.StatusCode == StatusCodes.Status200OK
            && (context.Response.ContentType?.Contains("text/html", StringComparison.OrdinalIgnoreCase) ?? false);
        if (!isHtml)
        {
            // 304, redirects, errors, anything not HTML: unchanged.
            await buffer.CopyToAsync(originalBody, context.RequestAborted).ConfigureAwait(false);
            return;
        }

        string html;
        using (var reader = new StreamReader(buffer, Encoding.UTF8, true, 1024, leaveOpen: true))
        {
            html = await reader.ReadToEndAsync(context.RequestAborted).ConfigureAwait(false);
        }

        try
        {
            var version = typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "0";
            html = InjectInto(html, BuildScriptTag(version), out var injected);
            if (injected)
            {
                _status.MarkInjected();
                if (Interlocked.Exchange(ref _loggedOnce, 1) == 0)
                {
                    _logger.LogInformation("Project A: added its script to the web page (request-time injection).");
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Never break the web page: serve what we have.
            _logger.LogWarning(ex, "Project A: script injection failed; serving the page unchanged.");
        }

        var bytes = Encoding.UTF8.GetBytes(html);
        context.Response.ContentType = "text/html;charset=utf-8";
        context.Response.ContentLength = bytes.Length;

        // The body changed, so the static-file validators no longer match it.
        context.Response.Headers.Remove("ETag");
        context.Response.Headers.Remove("Last-Modified");
        context.Response.Headers.Remove("Accept-Ranges");
        await originalBody.WriteAsync(bytes, context.RequestAborted).ConfigureAwait(false);
    }
}
