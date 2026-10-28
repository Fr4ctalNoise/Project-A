// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 Fr4ctalNoise and Project A contributors.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Text.Json;
using Jellyfin.Plugin.ProjectA.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace Jellyfin.Plugin.ProjectA.Tests;

/// <summary>
/// Jellyfin has no "sign-in required unless marked otherwise" default, so one missing
/// [Authorize] would make an endpoint public. These tests find every endpoint by
/// reflection, so a new endpoint can't be forgotten. They also write the endpoint list
/// that the live-server test (verification 1) calls with no token, a disabled user's
/// token and an API key.
/// </summary>
public class EndpointAccessTests
{
    /// <summary>The only endpoints allowed without sign-in. Adding one needs a security review.</summary>
    private static readonly HashSet<string> AnonymousAllowList = ["SocialController.GetScript"];

    /// <summary>The only endpoints that accept API keys. Adding one needs a security review.</summary>
    private static readonly HashSet<string> ApiKeyAllowList = ["SocialController.GetHealth"];

    /// <summary>One endpoint as the access tests call it.</summary>
    /// <param name="Path">A concrete path: route values are replaced by a sample that never matches
    /// real data (id 0, the empty Guid), so the sign-in tests read, create and delete nothing.</param>
    /// <param name="AnyStatusWhenAllowed">For a caller the gate lets through, any answer except
    /// 401, 403 or a server error counts as "allowed": a sample id gives 404 and an empty POST body
    /// 400 or 415. False means exactly 200 (a GET with no route values).</param>
    public sealed record Endpoint(string Name, string Method, string Path, bool Anonymous, bool AllowApiKey, bool AdminOnly, bool AnyStatusWhenAllowed);

    /// <summary>Replaces each {name} or {name:constraint} with a sample value that can't exist.</summary>
    public static string SamplePath(string template) =>
        Regex.Replace(template, @"\{(?<name>[A-Za-z]+)(:(?<c>[a-z]+))?\}", m => m.Groups["c"].Value == "guid" ? Guid.Empty.ToString() : "0");

    public static IReadOnlyList<Endpoint> Discover()
    {
        var assembly = typeof(Plugin).Assembly;
        var result = new List<Endpoint>();
        foreach (var controller in assembly.GetTypes().Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract))
        {
            var prefix = controller.GetCustomAttribute<RouteAttribute>()?.Template ?? string.Empty;
            foreach (var action in controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                var verbs = action.GetCustomAttributes<HttpMethodAttribute>().ToList();
                if (verbs.Count == 0)
                {
                    Assert.Fail($"{controller.Name}.{action.Name} is public but has no HTTP verb attribute; make it non-public or add one.");
                }

                var social = action.GetCustomAttribute<SocialUserAttribute>();
                foreach (var verb in verbs)
                {
                    foreach (var method in verb.HttpMethods)
                    {
                        var template = "/" + string.Join('/', new[] { prefix, verb.Template }.Where(s => !string.IsNullOrEmpty(s)));
                        var path = SamplePath(template);
                        result.Add(new Endpoint(
                            $"{controller.Name}.{action.Name}",
                            method,
                            path,
                            action.GetCustomAttribute<AllowAnonymousAttribute>() is not null,
                            social?.AllowApiKey ?? false,
                            social?.RequireAdministrator ?? false,
                            AnyStatusWhenAllowed: method != "GET" || path != template));
                    }
                }
            }
        }

        return result;
    }

    [Fact]
    public void Endpoints_are_found() => Assert.NotEmpty(Discover());

    [Theory]
    [InlineData("/Social/Posts/{id:long}", "/Social/Posts/0")]
    [InlineData("/Social/Admin/Mute/{userId:guid}", "/Social/Admin/Mute/00000000-0000-0000-0000-000000000000")]
    [InlineData("/Social/Likes/{kind}/{id}", "/Social/Likes/0/0")]
    [InlineData("/Social/Feed", "/Social/Feed")]
    public void Route_values_become_samples_that_match_no_real_data(string template, string expected) =>
        Assert.Equal(expected, SamplePath(template));

    [Fact]
    public void Every_sample_path_is_concrete() =>
        Assert.All(Discover(), e => Assert.DoesNotContain("{", e.Path, StringComparison.Ordinal));

    [Fact]
    public void Every_endpoint_requires_sign_in_and_the_social_check_unless_on_the_anonymous_allow_list()
    {
        var assembly = typeof(Plugin).Assembly;
        foreach (var controller in assembly.GetTypes().Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract))
        {
            // Class-level [AllowAnonymous] would silently open every action.
            Assert.Null(controller.GetCustomAttribute<AllowAnonymousAttribute>());

            foreach (var action in controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                var name = $"{controller.Name}.{action.Name}";
                var anonymous = action.GetCustomAttribute<AllowAnonymousAttribute>() is not null;
                var authorize = action.GetCustomAttribute<AuthorizeAttribute>() is not null || controller.GetCustomAttribute<AuthorizeAttribute>() is not null;
                var social = action.GetCustomAttribute<SocialUserAttribute>() is not null;

                if (anonymous)
                {
                    Assert.True(AnonymousAllowList.Contains(name), $"{name} is [AllowAnonymous] but not on the allow list.");
                    Assert.False(authorize, $"{name} mixes [AllowAnonymous] and [Authorize].");
                }
                else
                {
                    Assert.True(authorize, $"{name} lacks [Authorize].");
                    Assert.True(social, $"{name} lacks [SocialUser].");
                }
            }
        }
    }

    [Fact]
    public void Only_allow_listed_endpoints_accept_api_keys()
    {
        foreach (var e in Discover().Where(e => e.AllowApiKey))
        {
            Assert.Contains(e.Name, ApiKeyAllowList);
        }
    }

    [Fact]
    public void Allow_lists_name_real_endpoints()
    {
        var names = Discover().Select(e => e.Name).ToHashSet();
        Assert.All(AnonymousAllowList.Concat(ApiKeyAllowList), n => Assert.Contains(n, names));
    }

    /// <summary>
    /// The list verify-auth.sh uses on your server must match the code, or a new endpoint
    /// would go untested there. To update: copy the "expected" lines from the failure message.
    /// </summary>
    [Fact]
    public void Server_test_endpoint_list_matches_the_code()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        static string YesNo(bool b) => b ? "yes" : "no";
        var expected = Discover()
            .Select(e => $"{e.Method}\t{e.Path}\t{YesNo(e.Anonymous)}\t{YesNo(e.AllowApiKey)}\t{YesNo(e.AdminOnly)}\t{(e.AnyStatusWhenAllowed ? "any" : "200")}")
            .OrderBy(l => l, StringComparer.Ordinal)
            .ToList();
        var actual = File.ReadAllLines(Path.Combine(dir.FullName, "tests", "live", "endpoints.tsv"))
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .OrderBy(l => l, StringComparer.Ordinal)
            .ToList();
        Assert.True(expected.SequenceEqual(actual), "tests/live/endpoints.tsv is out of date. Expected lines:\n" + string.Join('\n', expected));
    }

    /// <summary>
    /// Writes the endpoint list for the live-server test when PROJECTA_ENDPOINTS_OUT is set
    /// (CI sets it). Always runs, so the list can't drift from the code.
    /// </summary>
    [Fact]
    public void Endpoint_list_is_written_for_the_live_server_test()
    {
        var json = JsonSerializer.Serialize(Discover(), new JsonSerializerOptions { WriteIndented = true });
        Assert.Contains("/Social/Health", json, StringComparison.Ordinal);
        var target = Environment.GetEnvironmentVariable("PROJECTA_ENDPOINTS_OUT");
        if (!string.IsNullOrEmpty(target))
        {
            File.WriteAllText(target, json);
        }
    }
}
