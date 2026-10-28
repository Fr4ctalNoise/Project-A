// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 Fr4ctalNoise and Project A contributors.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.ProjectA.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.ProjectA.Tests;

/// <summary>The post store against a real SQLite file: storage, paging, filtering, limits, upgrades.</summary>
public sealed class PostStoreTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "projecta-tests", Guid.NewGuid().ToString("N"));
    private readonly SocialStore _social;
    private readonly PostStore _posts;

    public PostStoreTests()
    {
        _social = new SocialStore(Path.Combine(_dir, SocialStore.FileName), NullLogger<SocialStore>.Instance);
        _posts = new PostStore(_social);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private long Post(Guid author, string body, int secondsAfterT0)
    {
        var r = _posts.CreateStatus(author, body, T0.AddSeconds(secondsAfterT0));
        Assert.False(r.RateLimited);
        return r.Id;
    }

    [Fact]
    public void Created_post_reads_back_exactly()
    {
        var id = Post(Alice, "Hello **feed**", 0);
        var row = _posts.Get(id);
        Assert.Equal(new PostRow(id, Alice, "status", "Hello **feed**", T0), row);
    }

    [Fact]
    public void Feed_is_newest_first_and_pages_by_cursor_without_gaps_or_repeats()
    {
        var ids = Enumerable.Range(0, 25).Select(i => Post(i % 2 == 0 ? Alice : Bob, $"post {i}", i * 61)).ToList();

        var page1 = _posts.ListFeed([Alice, Bob], null, 10);
        var page2 = _posts.ListFeed([Alice, Bob], page1[^1].Id, 10);
        var page3 = _posts.ListFeed([Alice, Bob], page2[^1].Id, 10);

        var all = page1.Concat(page2).Concat(page3).Select(p => p.Id).ToList();
        Assert.Equal(ids.AsEnumerable().Reverse(), all);
        Assert.Equal(5, page3.Count);
    }

    [Fact]
    public void Feed_only_contains_allowed_authors_and_pages_stay_full()
    {
        for (var i = 0; i < 30; i++)
        {
            Post(i % 3 == 0 ? Alice : Bob, $"p{i}", i * 61);
        }

        var page = _posts.ListFeed([Alice], null, 5);
        Assert.Equal(5, page.Count);
        Assert.All(page, p => Assert.Equal(Alice, p.AuthorId));
    }

    [Fact]
    public void Feed_with_no_allowed_authors_is_empty()
    {
        Post(Alice, "x", 0);
        Assert.Empty(_posts.ListFeed([], null, 20));
    }

    [Fact]
    public void Deleted_posts_leave_the_feed_and_cannot_be_deleted_twice()
    {
        var keep = Post(Alice, "keep", 0);
        var gone = Post(Alice, "gone", 61);

        Assert.True(_posts.SoftDelete(gone, T0.AddMinutes(5)));
        Assert.False(_posts.SoftDelete(gone, T0.AddMinutes(6)));
        Assert.Null(_posts.Get(gone));
        Assert.Equal(new[] { keep }, _posts.ListFeed([Alice], null, 20).Select(p => p.Id));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    [InlineData(long.MaxValue)]
    public void Missing_or_invalid_ids_are_not_found(long id)
    {
        Assert.Null(_posts.Get(id));
        Assert.False(_posts.SoftDelete(id, T0));
    }

    [Fact]
    public void Eleventh_post_within_a_minute_is_refused_and_not_written()
    {
        for (var i = 0; i < PostStore.MaxPostsPerMinute; i++)
        {
            Post(Alice, $"p{i}", i);
        }

        var refused = _posts.CreateStatus(Alice, "one too many", T0.AddSeconds(30));
        Assert.True(refused.RateLimited);
        Assert.Equal(0, refused.Id);
        Assert.DoesNotContain(_posts.ListFeed([Alice], null, 50), p => p.Body == "one too many");

        // Other users are unaffected, and the window slides: 61 s after the first post, one more fits.
        Assert.False(_posts.CreateStatus(Bob, "bob", T0.AddSeconds(30)).RateLimited);
        Assert.False(_posts.CreateStatus(Alice, "later", T0.AddSeconds(61)).RateLimited);
    }

    [Fact]
    public void Deleting_posts_does_not_reset_the_rate_limit()
    {
        for (var i = 0; i < PostStore.MaxPostsPerMinute; i++)
        {
            _posts.SoftDelete(Post(Alice, $"p{i}", i), T0.AddSeconds(i));
        }

        Assert.True(_posts.CreateStatus(Alice, "flood", T0.AddSeconds(20)).RateLimited);
    }

    [Fact]
    public async Task Concurrent_posts_never_exceed_the_rate_limit()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 30)
            .Select(i => Task.Run(() => _posts.CreateStatus(Alice, $"c{i}", T0.AddMilliseconds(i)))));

        Assert.Equal(PostStore.MaxPostsPerMinute, results.Count(r => !r.RateLimited));
        Assert.Equal(PostStore.MaxPostsPerMinute, _posts.ListFeed([Alice], null, 50).Count);
    }

    [Theory]
    [InlineData("'; DROP TABLE social_posts; --")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("\" OR 1=1 --")]
    [InlineData("emoji 🎃 and RTL שלום and nul-free unicode ✓")]
    public void Hostile_or_unusual_text_is_stored_verbatim_as_data(string body)
    {
        var id = Post(Alice, body, 0);
        Assert.Equal(body, _posts.Get(id)!.Body);
        Assert.Single(_posts.ListFeed([Alice], null, 20));
    }

    [Fact]
    public void Body_limits_are_enforced_by_the_store_too()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _posts.CreateStatus(Alice, string.Empty, T0));
        Assert.Throws<ArgumentOutOfRangeException>(() => _posts.CreateStatus(Alice, new string('a', PostStore.MaxBodyLength + 1), T0));
        Assert.Throws<ArgumentException>(() => _posts.CreateStatus(Guid.Empty, "x", T0));
        Assert.False(_posts.CreateStatus(Alice, new string('a', PostStore.MaxBodyLength), T0).RateLimited);
    }

    [Fact]
    public void Table_constraints_refuse_bad_rows_written_around_the_store()
    {
        _social.CheckHealth();
        using var c = new SqliteConnection($"Data Source={_social.DatabasePath};Pooling=False");
        c.Open();
        foreach (var sql in new[]
        {
            "INSERT INTO social_posts (author_id, kind, body_md, created_at, updated_at) VALUES ('short', 'status', 'x', 0, 0);",
            "INSERT INTO social_posts (author_id, kind, body_md, created_at, updated_at) VALUES ('11111111111111111111111111111111', 'html', 'x', 0, 0);",
            "INSERT INTO social_posts (author_id, kind, body_md, created_at, updated_at, deleted) VALUES ('11111111111111111111111111111111', 'status', 'x', 0, 0, 2);",
            "INSERT INTO social_posts (author_id, kind, body_md, created_at, updated_at) VALUES ('11111111111111111111111111111111', 'status', printf('%.4001c', 'a'), 0, 0);",
        })
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            var ex = Assert.Throws<SqliteException>(() => cmd.ExecuteNonQuery());
            Assert.Equal(19, ex.SqliteErrorCode); // SQLITE_CONSTRAINT
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(PostStore.MaxPageSize + 1)]
    public void Out_of_range_page_sizes_are_refused(int limit) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => _posts.ListFeed([Alice], null, limit));

    [Fact]
    public void Non_positive_cursor_is_refused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => _posts.ListFeed([Alice], 0, 10));

    [Fact]
    public void Ids_are_never_reused_after_a_delete()
    {
        var first = Post(Alice, "a", 0);
        _posts.SoftDelete(first, T0);
        var second = Post(Alice, "b", 61);
        Assert.True(second > first);
    }

    [Fact]
    public void A_spike_database_is_upgraded_in_place_keeping_its_data()
    {
        // Build exactly what 0.1.0.0 left on disk: schema 1 with a health probe row.
        var path = Path.Combine(_dir, "old", SocialStore.FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var c = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText =
                "PRAGMA journal_mode = WAL;" +
                "CREATE TABLE schema_version (version INTEGER NOT NULL); INSERT INTO schema_version VALUES (1);" +
                "CREATE TABLE health_probe (id INTEGER PRIMARY KEY CHECK (id = 1), checked_at TEXT NOT NULL);" +
                "INSERT INTO health_probe VALUES (1, 'from-0.1.0.0');";
            cmd.ExecuteNonQuery();
        }

        var store = new SocialStore(path, NullLogger<SocialStore>.Instance);
        var posts = new PostStore(store);
        var id = posts.CreateStatus(Alice, "after upgrade", T0).Id;

        Assert.Equal(new StoreHealth(true, null, 2), store.CheckHealth());
        Assert.Equal("after upgrade", posts.Get(id)!.Body);
        using var check = new SqliteConnection($"Data Source={path};Pooling=False");
        check.Open();
        using var versions = check.CreateCommand();
        versions.CommandText = "SELECT count(*) FROM schema_version;";
        Assert.Equal(1L, (long)versions.ExecuteScalar()!);
    }

    [Fact]
    public void A_second_store_on_an_upgraded_file_does_not_run_the_upgrade_again()
    {
        Post(Alice, "x", 0);
        var again = new PostStore(new SocialStore(_social.DatabasePath, NullLogger<SocialStore>.Instance));
        Assert.Single(again.ListFeed([Alice], null, 20)); // a second CREATE TABLE would have thrown
    }
}
