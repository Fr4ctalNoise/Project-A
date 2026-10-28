// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 Fr4ctalNoise and Project A contributors.

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Jellyfin.Plugin.ProjectA.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.ProjectA.Tests;

public sealed class SocialStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "projecta-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_dir))
            {
                foreach (var f in Directory.EnumerateFileSystemEntries(_dir, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(f, FileAttributes.Normal);
                }

                Directory.Delete(_dir, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private SocialStore Store(string? path = null) =>
        new(path ?? Path.Combine(_dir, "ProjectA", SocialStore.FileName), NullLogger<SocialStore>.Instance);

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    [Fact]
    public void Fresh_install_creates_the_folder_and_database_and_reports_ok()
    {
        var store = Store();
        var health = store.CheckHealth();
        Assert.Equal(new StoreHealth(true, null, SocialStore.CurrentSchemaVersion), health);
        Assert.True(File.Exists(store.DatabasePath));
    }

    [Fact]
    public void Database_uses_wal_journal_mode()
    {
        var store = Store();
        store.CheckHealth();
        using var c = new SqliteConnection($"Data Source={store.DatabasePath}");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode;";
        Assert.Equal("wal", (string)cmd.ExecuteScalar()!);
    }

    [Fact]
    public void Restart_reuses_the_existing_file_without_resetting_it()
    {
        var path = Path.Combine(_dir, "ProjectA", SocialStore.FileName);
        Assert.True(Store(path).CheckHealth().Ok);
        using (var c = new SqliteConnection($"Data Source={path}"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "CREATE TABLE keep_me (x INTEGER); INSERT INTO keep_me VALUES (42);";
            cmd.ExecuteNonQuery();
        }

        Assert.True(Store(path).CheckHealth().Ok);
        using var check = new SqliteConnection($"Data Source={path}");
        check.Open();
        using var read = check.CreateCommand();
        read.CommandText = "SELECT x FROM keep_me;";
        Assert.Equal(42L, read.ExecuteScalar());
    }

    [Fact]
    public void Corrupt_file_is_reported_and_left_byte_for_byte_untouched()
    {
        var path = Path.Combine(_dir, "ProjectA", SocialStore.FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var junk = new byte[8192];
        new Random(7).NextBytes(junk);
        File.WriteAllBytes(path, junk);
        var before = Hash(path);

        var health = Store(path).CheckHealth();

        Assert.False(health.Ok);
        Assert.Equal("corrupt", health.Problem);
        Assert.Equal(before, Hash(path));
    }

    [Fact]
    public void Truncated_database_is_reported_not_repaired()
    {
        var path = Path.Combine(_dir, "ProjectA", SocialStore.FileName);
        Assert.True(Store(path).CheckHealth().Ok);
        SqliteConnection.ClearAllPools();
        foreach (var side in new[] { "-wal", "-shm" })
        {
            if (File.Exists(path + side))
            {
                File.Delete(path + side);
            }
        }

        var bytes = File.ReadAllBytes(path);
        File.WriteAllBytes(path, bytes.Take(100).ToArray()); // header survives, pages gone
        var health = Store(path).CheckHealth();
        Assert.False(health.Ok);
        Assert.Contains(health.Problem, new[] { "corrupt", "error" });
    }

    [Fact]
    public void Database_from_a_newer_project_a_is_refused_and_not_modified()
    {
        var path = Path.Combine(_dir, "ProjectA", SocialStore.FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var c = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "CREATE TABLE schema_version (version INTEGER NOT NULL); INSERT INTO schema_version VALUES (99);";
            cmd.ExecuteNonQuery();
        }

        var before = Hash(path);
        var health = Store(path).CheckHealth();

        Assert.Equal(new StoreHealth(false, "newer_schema", 99), health);
        Assert.Equal(before, Hash(path));
    }

    [Fact]
    public void A_lock_held_past_the_timeout_is_reported_as_locked()
    {
        var path = Path.Combine(_dir, "ProjectA", SocialStore.FileName);
        Assert.True(Store(path).CheckHealth().Ok);

        using var holder = new SqliteConnection($"Data Source={path};Pooling=False");
        holder.Open();
        using (var lockCmd = holder.CreateCommand())
        {
            lockCmd.CommandText = "PRAGMA locking_mode = EXCLUSIVE; BEGIN EXCLUSIVE;";
            lockCmd.ExecuteNonQuery();
        }

        var started = DateTime.UtcNow;
        var health = Store(path).CheckHealth();
        Assert.False(health.Ok);
        Assert.Equal("locked", health.Problem);
        Assert.InRange((DateTime.UtcNow - started).TotalSeconds, 4, 30); // waited for the busy timeout first
    }

    [Fact]
    public void Read_only_folder_is_reported_as_not_writable()
    {
        if (!OperatingSystem.IsLinux() || Environment.UserName == "root")
        {
            return; // root ignores permissions; CI runs as a normal user
        }

        var folder = Path.Combine(_dir, "ro");
        Directory.CreateDirectory(folder);
        File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            var health = Store(Path.Combine(folder, "ProjectA", SocialStore.FileName)).CheckHealth();
            Assert.False(health.Ok);
            Assert.Equal("not_writable", health.Problem);
        }
        finally
        {
            File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public void Path_with_spaces_and_non_ascii_characters_works()
    {
        var store = Store(Path.Combine(_dir, "Jellyfin config ç日本", "ProjectA", SocialStore.FileName));
        Assert.True(store.CheckHealth().Ok);
    }

    /// <summary>
    /// Regression: concurrent checks (the notifier bot and an admin at once) overwrote each
    /// other's probe between write and read-back and reported a false "readback_mismatch".
    /// Several rounds of many parallel checks make the race reproduce reliably.
    /// </summary>
    [Fact]
    public async Task Concurrent_health_checks_all_succeed()
    {
        var store = Store();
        for (var round = 0; round < 5; round++)
        {
            var results = await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => Task.Run(store.CheckHealth)));
            Assert.All(results, r => Assert.True(r.Ok, r.Problem));
        }
    }

    [Theory]
    [InlineData(5, "locked")]
    [InlineData(6, "locked")]
    [InlineData(8, "not_writable")]
    [InlineData(13, "not_writable")]
    [InlineData(14, "not_writable")]
    [InlineData(11, "corrupt")]
    [InlineData(26, "corrupt")]
    [InlineData(1, "error")]
    public void Sqlite_errors_map_to_fixed_codes(int code, string expected) =>
        Assert.Equal(expected, SocialStore.Classify(new SqliteException("x", code)));

    [Fact]
    public void Problem_codes_never_carry_exception_text()
    {
        var code = SocialStore.Classify(new SqliteException("secret path /config/users/alice", 1));
        Assert.Equal("error", code);
    }
}
