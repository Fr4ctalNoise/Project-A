// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 Fr4ctalNoise and Project A contributors.

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.ProjectA.Api;
using Xunit;

namespace Jellyfin.Plugin.ProjectA.Tests;

/// <summary>Checks on the repository itself: licensing, versions and the embedded script.</summary>
public class RepositoryTests
{
    private static readonly string Root = FindRoot();

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root (Directory.Build.props) not found.");
    }

    [Fact]
    public void Every_source_file_declares_its_licence()
    {
        var extensions = new[] { ".cs", ".js", ".mjs", ".html", ".csproj", ".sh", ".py", ".yml" };
        var skip = new[] { "/bin/", "/obj/", "/node_modules/", "/fixtures/", "/Fixtures/", "/vendor/" };
        var files = Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
            .Select(f => f.Replace('\\', '/'))
            .Where(f => extensions.Contains(Path.GetExtension(f)) && !f.Contains("/.git/", StringComparison.Ordinal))
            .Where(f => (f.Contains("/src/", StringComparison.Ordinal) || f.Contains("/tests/", StringComparison.Ordinal) || f.Contains("/build/", StringComparison.Ordinal) || f.Contains("/.github/", StringComparison.Ordinal))
                && !skip.Any(s => f.Contains(s, StringComparison.Ordinal)))
            .ToList();

        Assert.NotEmpty(files);
        var missing = files.Where(f => !File.ReadLines(f).Take(5).Any(l => l.Contains("SPDX-License-Identifier: GPL-3.0-only", StringComparison.Ordinal))).ToList();
        Assert.True(missing.Count == 0, "Missing SPDX line in: " + string.Join(", ", missing.Select(m => Path.GetRelativePath(Root, m))));
    }

    [Fact]
    public void Adapted_files_name_their_source_and_licence()
    {
        var adapted = new[]
        {
            "src/Jellyfin.Plugin.ProjectA/Web/ScriptInjectionStartupFilter.cs",
            "src/Jellyfin.Plugin.ProjectA/Web/client/projectA.js",
        };
        foreach (var rel in adapted)
        {
            var head = string.Join('\n', File.ReadLines(Path.Combine(Root, rel)).Take(8));
            Assert.Contains("Jellyfin Enhanced (GPL-3.0)", head, StringComparison.Ordinal);
            Assert.Contains("d822e14", head, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Licence_files_listed_in_the_notices_exist()
    {
        var notices = File.ReadAllText(Path.Combine(Root, "THIRD_PARTY_NOTICES.md"));
        var linked = Regex.Matches(notices, @"\(licenses/([^)]+)\)").Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.NotEmpty(linked);
        Assert.All(linked, f => Assert.True(File.Exists(Path.Combine(Root, "licenses", f)), f));
        Assert.True(File.Exists(Path.Combine(Root, "LICENSE")));
    }

    [Fact]
    public void Changelog_top_release_matches_the_build_version()
    {
        var version = typeof(Plugin).Assembly.GetName().Version!.ToString();
        var first = File.ReadLines(Path.Combine(Root, "CHANGELOG.md")).First(l => Regex.IsMatch(l, @"^## \d"));
        Assert.StartsWith($"## {version} ", first, StringComparison.Ordinal);
    }

    [Fact]
    public void Embedded_script_is_the_source_file()
    {
        using var stream = typeof(Plugin).Assembly.GetManifestResourceStream(SocialController.ScriptResourceName);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        var embedded = reader.ReadToEnd();
        var source = File.ReadAllText(Path.Combine(Root, "src/Jellyfin.Plugin.ProjectA/Web/client/projectA.js"));
        Assert.Equal(source, embedded);
    }

    [Fact]
    public void Embedded_script_never_uses_innerHTML_or_puts_tokens_in_urls()
    {
        var source = File.ReadAllText(Path.Combine(Root, "src/Jellyfin.Plugin.ProjectA/Web/client/projectA.js"));
        var code = string.Join('\n', source.Split('\n').Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
        Assert.DoesNotContain("innerHTML", code, StringComparison.Ordinal);
        Assert.DoesNotContain("outerHTML", code, StringComparison.Ordinal);
        Assert.DoesNotContain("insertAdjacentHTML", code, StringComparison.Ordinal);
        Assert.DoesNotContain("api_key", code, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("accessToken", code, StringComparison.Ordinal);
        Assert.DoesNotContain("eval(", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Plugin_id_is_the_same_everywhere()
    {
        var id = Plugin.PluginId.ToString();
        Assert.Contains(id, File.ReadAllText(Path.Combine(Root, "src/Jellyfin.Plugin.ProjectA/Configuration/configPage.html")), StringComparison.Ordinal);
        Assert.Contains(id, File.ReadAllText(Path.Combine(Root, "build.yaml")), StringComparison.Ordinal);
    }
}
