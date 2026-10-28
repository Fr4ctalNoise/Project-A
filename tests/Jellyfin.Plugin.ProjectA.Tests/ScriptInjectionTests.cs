// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 Fr4ctalNoise and Project A contributors.

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Jellyfin.Plugin.ProjectA.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.ProjectA.Tests;

public class ScriptInjectionTests
{
    // jellyfin-web v12.4 (commit 0a580ce) src/index.html, the template the served page is built from.
    private static readonly string Upstream = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "jellyfin-web-12.4-index.template.html"));

    // Jellyfin Enhanced 12.14's tag as BuildScriptTag() in JellyfinEnhanced.cs writes it.
    private const string JeTag = "<script plugin=\"Jellyfin Enhanced\" version=\"12.14.0.0\" dev=\"false\" src=\"../JellyfinEnhanced/script?v=12.14.0.0\" defer></script>";

    private static readonly string Tag = ScriptInjectionStartupFilter.BuildScriptTag("0.1.0.0");

    private static int Count(string haystack, string needle) =>
        (haystack.Length - haystack.Replace(needle, string.Empty, StringComparison.Ordinal).Length) / needle.Length;

    // ------------------------------------------------------------ pure functions

    [Theory]
    [InlineData("/web", true)]
    [InlineData("/web/", true)]
    [InlineData("/web/index.html", true)]
    [InlineData("/WEB/INDEX.HTML", true)]
    [InlineData("/jellyfin/web/", true)]
    [InlineData("/jellyfin/web/index.html", true)]
    [InlineData("/web/main.jellyfin.bundle.js", false)]
    [InlineData("/web/index.html.bak", false)]
    [InlineData("/Social/script", false)]
    [InlineData("/webx", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_the_web_app_shell_is_handled(string? path, bool expected) =>
        Assert.Equal(expected, ScriptInjectionStartupFilter.IsIndexRequest(path));

    [Fact]
    public void Tag_is_relative_deferred_and_versioned()
    {
        Assert.Equal("<script plugin=\"Project A\" version=\"0.1.0.0\" src=\"../Social/script?v=0.1.0.0\" defer></script>", Tag);
    }

    [Fact]
    public void A_hostile_version_string_cannot_break_out_of_the_tag()
    {
        var tag = ScriptInjectionStartupFilter.BuildScriptTag("1\"><img src=x onerror=alert(1)>");
        Assert.DoesNotContain("\"><img", tag, StringComparison.Ordinal);
        Assert.Single(tag.Split("<script").Skip(1));
        Assert.Equal(2, Count(tag, "<")); // only "<script" and "</script>"
    }

    [Fact]
    public void Upstream_page_gets_the_tag_once_just_before_body_close()
    {
        var html = ScriptInjectionStartupFilter.InjectInto(Upstream, Tag, out var injected);
        Assert.True(injected);
        Assert.Equal(1, Count(html, Tag));
        Assert.EndsWith(Tag + "\n</body>", html[..(html.LastIndexOf("</body>", StringComparison.Ordinal) + 7)], StringComparison.Ordinal);
        Assert.Equal(Upstream, html.Replace(Tag + "\n", string.Empty, StringComparison.Ordinal));
    }

    [Fact]
    public void Running_twice_does_not_inject_twice()
    {
        var once = ScriptInjectionStartupFilter.InjectInto(Upstream, Tag, out _);
        var twice = ScriptInjectionStartupFilter.InjectInto(once, Tag, out var injected);
        Assert.False(injected);
        Assert.Equal(once, twice);
    }

    [Fact]
    public void Coexists_with_jellyfin_enhanced_in_either_order()
    {
        // JE first, then us (filters registered in that order).
        var withJe = Upstream.Replace("</body>", JeTag + "\n</body>", StringComparison.Ordinal);
        var both = ScriptInjectionStartupFilter.InjectInto(withJe, Tag, out var injected);
        Assert.True(injected);
        Assert.Equal(1, Count(both, Tag));
        Assert.Equal(1, Count(both, JeTag));

        // Us first, then JE's own check (it looks for /JellyfinEnhanced/script) still adds JE.
        var oursFirst = ScriptInjectionStartupFilter.InjectInto(Upstream, Tag, out _);
        Assert.DoesNotContain("/JellyfinEnhanced/script", oursFirst, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_leftover_on_disk_tag_counts_as_already_injected()
    {
        var legacy = Upstream.Replace("</body>", "<script src=\"../Social/script?v=0.0.1\"></script></body>", StringComparison.Ordinal);
        var html = ScriptInjectionStartupFilter.InjectInto(legacy, Tag, out var injected);
        Assert.False(injected);
        Assert.Equal(legacy, html);
    }

    [Fact]
    public void Page_without_body_close_is_left_alone()
    {
        var html = ScriptInjectionStartupFilter.InjectInto("<html><body>unfinished", Tag, out var injected);
        Assert.False(injected);
        Assert.Equal("<html><body>unfinished", html);
    }

    [Fact]
    public void Uses_the_last_body_close_and_ignores_case()
    {
        var page = "<html><body><script>var s='</body>';</script></BODY></html>";
        var html = ScriptInjectionStartupFilter.InjectInto(page, Tag, out _);
        Assert.Equal("<html><body><script>var s='</body>';</script>" + Tag + "\n</BODY></html>", html);
    }

    [Fact]
    public void Empty_page_is_left_alone()
    {
        Assert.Equal(string.Empty, ScriptInjectionStartupFilter.InjectInto(string.Empty, Tag, out var injected));
        Assert.False(injected);
    }

    // ---------------------------------------------------------- full middleware

    /// <summary>Runs the filter in front of a fake static-file handler, as in Jellyfin's pipeline.</summary>
    private static async Task<(TestServer Server, InjectionStatus Status, Func<HttpContext, string?> LastSeen)> StartAsync(Func<HttpContext, Task> terminal)
    {
        var status = new InjectionStatus();
        string? lastAcceptEncoding = null;
        var host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(s =>
                {
                    s.AddSingleton(status);
                    s.AddSingleton<IStartupFilter>(new ScriptInjectionStartupFilter(NullLogger<ScriptInjectionStartupFilter>.Instance, status));
                })
                .Configure(app => app.Run(async ctx =>
                {
                    lastAcceptEncoding = ctx.Request.Headers.AcceptEncoding.ToString();
                    await terminal(ctx);
                })))
            .StartAsync();
        return (host.GetTestServer(), status, _ => lastAcceptEncoding);
    }

    private static Func<HttpContext, Task> ServeUpstream(int status = 200, string contentType = "text/html", string? body = null) => async ctx =>
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = contentType;
        ctx.Response.Headers.ETag = "\"static\"";
        ctx.Response.Headers.LastModified = "Mon, 26 Oct 2026 00:00:00 GMT";
        ctx.Response.Headers.AcceptRanges = "bytes";
        await ctx.Response.WriteAsync(body ?? Upstream);
    };

    [Fact]
    public async Task Index_is_served_with_the_tag_correct_length_and_no_stale_validators()
    {
        var (server, status, _) = await StartAsync(ServeUpstream());
        using var client = server.CreateClient();
        using var response = await client.GetAsync("/web/");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("../Social/script?v=", body, StringComparison.Ordinal);
        Assert.Equal(Encoding.UTF8.GetByteCount(body), response.Content.Headers.ContentLength);
        Assert.Null(response.Headers.ETag);
        Assert.Null(response.Content.Headers.LastModified);
        Assert.False(response.Headers.Contains("Accept-Ranges"));
        Assert.NotNull(status.LastInjected);
    }

    [Fact]
    public async Task Compression_and_range_requests_are_neutralised_before_the_static_handler()
    {
        var (server, _, lastSeen) = await StartAsync(ServeUpstream());
        using var client = server.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/web/index.html");
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, br");
        request.Headers.TryAddWithoutValidation("Range", "bytes=0-10");
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(string.Empty, lastSeen(null!));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("../Social/script", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(304, "text/html")]
    [InlineData(302, "text/html")]
    [InlineData(500, "text/html")]
    [InlineData(200, "application/json")]
    public async Task Non_html_and_non_200_responses_pass_through_unchanged(int code, string type)
    {
        var (server, status, _) = await StartAsync(ServeUpstream(code, type, "<html><body>x</body></html>"));
        using var client = server.CreateClient();
        using var response = await client.GetAsync("/web/");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(code, (int)response.StatusCode);
        Assert.DoesNotContain("/Social/script", body, StringComparison.Ordinal);
        Assert.Null(status.LastInjected);
    }

    [Fact]
    public async Task Head_requests_pass_straight_through()
    {
        var reached = false;
        var (server, status, _) = await StartAsync(ctx => { reached = true; ctx.Response.ContentType = "text/html"; return Task.CompletedTask; });
        using var client = server.CreateClient();
        using var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/web/"));
        Assert.True(reached);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(status.LastInjected);
    }

    [Fact]
    public async Task Other_paths_are_never_buffered_or_touched()
    {
        var (server, _, _) = await StartAsync(ServeUpstream());
        using var client = server.CreateClient();
        var body = await client.GetStringAsync("/web/other.html");
        Assert.Equal(Upstream, body);
    }

    [Fact]
    public async Task A_failure_in_the_static_handler_is_rethrown_not_served_half_written()
    {
        var (server, _, _) = await StartAsync(async ctx =>
        {
            ctx.Response.ContentType = "text/html";
            await ctx.Response.WriteAsync("<html><body>partial");
            throw new InvalidOperationException("disk error");
        });
        using var client = server.CreateClient();
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetAsync("/web/"));
    }

    [Fact]
    public async Task Huge_page_is_handled()
    {
        var big = "<html><body>" + new string('x', 5_000_000) + "</body></html>";
        var (server, _, _) = await StartAsync(ServeUpstream(body: big));
        using var client = server.CreateClient();
        var body = await client.GetStringAsync("/web/");
        var served = ScriptInjectionStartupFilter.BuildScriptTag(typeof(Plugin).Assembly.GetName().Version!.ToString());
        Assert.Equal(big.Length + served.Length + 1, body.Length);
    }

    [Fact]
    public async Task Concurrent_requests_each_get_exactly_one_tag()
    {
        var (server, _, _) = await StartAsync(ServeUpstream());
        using var client = server.CreateClient();
        var bodies = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => client.GetStringAsync("/web/")));
        Assert.All(bodies, b => Assert.Equal(1, Count(b, "../Social/script?v=")));
    }
}
