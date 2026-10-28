// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 Fr4ctalNoise and Project A contributors.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Jellyfin.Plugin.ProjectA.Data;

/// <summary>One post as stored. The body is the author's markdown, never HTML.</summary>
/// <param name="Id">Post id (never reused).</param>
/// <param name="AuthorId">Jellyfin user id of the author.</param>
/// <param name="Kind">status, watched, review or recommend (only status is written so far).</param>
/// <param name="Body">Markdown source, up to <see cref="PostStore.MaxBodyLength"/> characters.</param>
/// <param name="CreatedAt">When it was posted (UTC).</param>
public sealed record PostRow(long Id, Guid AuthorId, string Kind, string Body, DateTimeOffset CreatedAt);

/// <summary>Outcome of <see cref="PostStore.CreateStatus"/>.</summary>
/// <param name="Id">The new post's id, or 0 when <paramref name="RateLimited"/>.</param>
/// <param name="RateLimited">The author already posted the maximum in the last minute; nothing was written.</param>
public readonly record struct CreateResult(long Id, bool RateLimited);

/// <summary>
/// Reads and writes posts in social.db (table <c>social_posts</c>, schema 2).
/// </summary>
/// <remarks>
/// <para>Knows nothing about HTTP or who may do what: callers check permissions and validate
/// input first (see <c>Api/PostsController</c> and <c>Api/PostInput</c>). This class still
/// refuses values the table can't hold, as a second line of defence.</para>
/// <para>Deletes are soft (<c>deleted = 1</c>): the row stays so comments and likes (step 3)
/// disappear with their post and nothing is orphaned, and deleted posts still count toward
/// the rate limit so delete-and-repost can't be used to flood the feed.</para>
/// <para>Times are stored as Unix milliseconds (UTC); user ids as 32 lowercase hex digits.</para>
/// </remarks>
public sealed class PostStore
{
    /// <summary>Longest post body, in UTF-16 code units (what the browser's <c>String.length</c> counts).</summary>
    public const int MaxBodyLength = 4000;

    /// <summary>Most posts one user may create in any 60-second window (design doc: 10 a minute).</summary>
    public const int MaxPostsPerMinute = 10;

    /// <summary>Largest page the feed returns.</summary>
    public const int MaxPageSize = 50;

    private readonly SocialStore _store;

    /// <summary>Initializes a new instance of the <see cref="PostStore"/> class.</summary>
    /// <param name="store">The database.</param>
    public PostStore(SocialStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Creates a status post unless the author has already created
    /// <see cref="MaxPostsPerMinute"/> posts in the last 60 seconds. The count and the insert
    /// happen in one write transaction, so two requests at the same moment can't both slip in.
    /// </summary>
    /// <param name="authorId">The signed-in author.</param>
    /// <param name="body">Validated markdown (see <c>PostInput</c>).</param>
    /// <param name="now">Current time.</param>
    /// <returns>The new id, or a rate-limited result with nothing written.</returns>
    public CreateResult CreateStatus(Guid authorId, string body, DateTimeOffset now)
    {
        if (authorId == Guid.Empty)
        {
            throw new ArgumentException("An author is required.", nameof(authorId));
        }

        ArgumentNullException.ThrowIfNull(body);
        if (body.Length == 0 || body.Length > MaxBodyLength)
        {
            throw new ArgumentOutOfRangeException(nameof(body), body.Length, "Body must be 1 to 4000 characters.");
        }

        var nowMs = now.ToUnixTimeMilliseconds();
        using var connection = _store.OpenReady();
        using var transaction = connection.BeginTransaction(); // BEGIN IMMEDIATE: takes the write lock now

        using (var count = connection.CreateCommand())
        {
            count.Transaction = transaction;
            count.CommandText = "SELECT count(*) FROM social_posts WHERE author_id = $author AND created_at > $since;";
            count.Parameters.AddWithValue("$author", Key(authorId));
            count.Parameters.AddWithValue("$since", nowMs - 60_000);
            if (Convert.ToInt64(count.ExecuteScalar(), CultureInfo.InvariantCulture) >= MaxPostsPerMinute)
            {
                return new CreateResult(0, true); // disposing rolls back; nothing was written
            }
        }

        long id;
        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO social_posts (author_id, kind, body_md, created_at, updated_at) "
                + "VALUES ($author, 'status', $body, $now, $now) RETURNING id;";
            insert.Parameters.AddWithValue("$author", Key(authorId));
            insert.Parameters.AddWithValue("$body", body);
            insert.Parameters.AddWithValue("$now", nowMs);
            id = Convert.ToInt64(insert.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        transaction.Commit();
        return new CreateResult(id, false);
    }

    /// <summary>
    /// The newest posts that aren't deleted, written by one of <paramref name="authors"/>,
    /// older than <paramref name="before"/> when given. Filtering by author happens in SQL, so
    /// a page is always full when enough posts exist (no short pages from filtering after).
    /// </summary>
    /// <param name="authors">Authors the viewer may see (from <c>IUserDirectory</c>). Empty gives an empty page.</param>
    /// <param name="before">Only posts with a smaller id (the previous page's cursor), or null for the newest.</param>
    /// <param name="limit">1 to <see cref="MaxPageSize"/>.</param>
    /// <returns>Posts, newest first.</returns>
    public IReadOnlyList<PostRow> ListFeed(IReadOnlyCollection<Guid> authors, long? before, int limit)
    {
        ArgumentNullException.ThrowIfNull(authors);
        if (limit < 1 || limit > MaxPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "Limit must be 1 to 50.");
        }

        if (before is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(before), before, "Cursor must be a positive id.");
        }

        if (authors.Count == 0)
        {
            return [];
        }

        using var connection = _store.OpenReady();
        using var query = connection.CreateCommand();
        // The author list travels as one JSON parameter read by json_each, so it is never
        // spliced into the SQL and has no parameter-count limit however many users exist.
        query.CommandText =
            "SELECT id, author_id, kind, body_md, created_at FROM social_posts " +
            "WHERE deleted = 0 AND ($before IS NULL OR id < $before) " +
            "AND author_id IN (SELECT value FROM json_each($authors)) " +
            "ORDER BY id DESC LIMIT $limit;";
        query.Parameters.AddWithValue("$before", (object?)before ?? DBNull.Value);
        query.Parameters.AddWithValue("$authors", JsonSerializer.Serialize(authors.Select(Key)));
        query.Parameters.AddWithValue("$limit", limit);

        var result = new List<PostRow>(limit);
        using var reader = query.ExecuteReader();
        while (reader.Read())
        {
            result.Add(Read(reader));
        }

        return result;
    }

    /// <summary>A post that isn't deleted, or null.</summary>
    /// <param name="id">Post id.</param>
    /// <returns>The post or null.</returns>
    public PostRow? Get(long id)
    {
        if (id <= 0)
        {
            return null;
        }

        using var connection = _store.OpenReady();
        using var query = connection.CreateCommand();
        query.CommandText = "SELECT id, author_id, kind, body_md, created_at FROM social_posts WHERE id = $id AND deleted = 0;";
        query.Parameters.AddWithValue("$id", id);
        using var reader = query.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    /// <summary>Marks a post deleted. Permission is the caller's job.</summary>
    /// <param name="id">Post id.</param>
    /// <param name="now">Current time.</param>
    /// <returns>True if a post was deleted now; false if it didn't exist or was already deleted.</returns>
    public bool SoftDelete(long id, DateTimeOffset now)
    {
        if (id <= 0)
        {
            return false;
        }

        using var connection = _store.OpenReady();
        using var update = connection.CreateCommand();
        update.CommandText = "UPDATE social_posts SET deleted = 1, updated_at = $now WHERE id = $id AND deleted = 0;";
        update.Parameters.AddWithValue("$id", id);
        update.Parameters.AddWithValue("$now", now.ToUnixTimeMilliseconds());
        return update.ExecuteNonQuery() == 1;
    }

    /// <summary>The stored form of a user id: 32 lowercase hex digits.</summary>
    /// <param name="userId">User id.</param>
    /// <returns>The key.</returns>
    internal static string Key(Guid userId) => userId.ToString("N", CultureInfo.InvariantCulture);

    private static PostRow Read(SqliteDataReader reader) => new(
        reader.GetInt64(0),
        Guid.ParseExact(reader.GetString(1), "N"),
        reader.GetString(2),
        reader.GetString(3),
        DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(4)));
}
