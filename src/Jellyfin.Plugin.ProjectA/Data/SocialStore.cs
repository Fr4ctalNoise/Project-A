// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 Fr4ctalNoise and Project A contributors.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using MediaBrowser.Common.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ProjectA.Data;

/// <summary>Result of a database health check. Holds no user data.</summary>
/// <param name="Ok">The database could be written and read back.</param>
/// <param name="Problem">When not ok: a short, fixed code (see <see cref="SocialStore"/>); never an exception message.</param>
/// <param name="SchemaVersion">The schema version found, or 0 if unknown.</param>
public sealed record StoreHealth(bool Ok, string? Problem, int SchemaVersion);

/// <summary>
/// Project A's SQLite database: one file, social.db, in Jellyfin's plugin
/// configurations folder (plugins/configurations/ProjectA/). That folder survives
/// plugin updates; the plugin's own folder is replaced by each new version.
/// </summary>
/// <remarks>
/// <para>Uses the server's own Microsoft.Data.Sqlite (see the project file). Every
/// operation opens its own short-lived connection, so the class is safe to share.</para>
/// <para>Feature stores (for example <see cref="PostStore"/>) get connections from
/// <see cref="OpenReady"/>, so the schema is always current before any query runs.</para>
/// <para>SQL is parameterised only. A damaged or unreadable file is never deleted or
/// rewritten: the health check reports it and the file is left for inspection.</para>
/// <para>Problem codes: <c>not_writable</c> (folder or file read-only, or disk full),
/// <c>locked</c> (another process holds a lock past the 5 s busy timeout),
/// <c>corrupt</c> (not a database, or damaged), <c>newer_schema</c> (written by a
/// newer Project A), <c>readback_mismatch</c>, <c>error</c> (anything else).</para>
/// </remarks>
public sealed class SocialStore
{
    /// <summary>The schema version this build creates and understands.</summary>
    /// <remarks>History: 1 = the Spike (health probe only); 2 = text posts (social_posts).</remarks>
    public const int CurrentSchemaVersion = 2;

    /// <summary>
    /// Upgrades, keyed by the version each one produces. Each runs once, in order, inside the
    /// same transaction that records the new version, so a failed upgrade leaves the file as it
    /// was. Never edit a released entry: add a new version instead.
    /// </summary>
    /// <remarks>
    /// <para>Version 2: <c>social_posts</c>. <c>kind</c> already allows all four kinds the design
    /// doc plans (status, watched, review, recommend), because SQLite can't change a CHECK
    /// constraint without rebuilding the table. The columns those later kinds need (item,
    /// episodes, images, review key) arrive with their own build steps as
    /// <c>ALTER TABLE ... ADD COLUMN</c>, which is cheap.</para>
    /// <para><c>AUTOINCREMENT</c> means a post id is never reused, so an old link or cursor can
    /// never point at someone else's later post. <c>ix_social_posts_feed</c> serves the feed
    /// (newest first, not deleted); <c>ix_social_posts_author_time</c> serves the per-user rate
    /// limit.</para>
    /// </remarks>
    internal static readonly IReadOnlyList<(int Version, string Sql)> Migrations =
    [
        (2,
            "CREATE TABLE social_posts (" +
            " id INTEGER PRIMARY KEY AUTOINCREMENT," +
            " author_id TEXT NOT NULL CHECK (length(author_id) = 32)," +
            " kind TEXT NOT NULL CHECK (kind IN ('status', 'watched', 'review', 'recommend'))," +
            " body_md TEXT NOT NULL DEFAULT '' CHECK (length(body_md) <= 4000)," +
            " created_at INTEGER NOT NULL," +
            " updated_at INTEGER NOT NULL," +
            " deleted INTEGER NOT NULL DEFAULT 0 CHECK (deleted IN (0, 1)));" +
            "CREATE INDEX ix_social_posts_feed ON social_posts (deleted, id DESC);" +
            "CREATE INDEX ix_social_posts_author_time ON social_posts (author_id, created_at);"),
    ];

    /// <summary>The folder name under Jellyfin's plugin configurations path.</summary>
    public const string FolderName = "ProjectA";

    /// <summary>The database file name.</summary>
    public const string FileName = "social.db";

    private readonly ILogger<SocialStore> _logger;
    private readonly string _connectionString;
    private readonly object _initLock = new();
    private volatile bool _initialized;

    /// <summary>Initializes a new instance of the <see cref="SocialStore"/> class for the server's configuration folder.</summary>
    /// <param name="applicationPaths">Jellyfin's paths.</param>
    /// <param name="logger">Logger.</param>
    public SocialStore(IApplicationPaths applicationPaths, ILogger<SocialStore> logger)
        : this(Path.Combine(applicationPaths.PluginConfigurationsPath, FolderName, FileName), logger)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="SocialStore"/> class for a given file (tests).</summary>
    /// <param name="databasePath">Full path of the database file.</param>
    /// <param name="logger">Logger.</param>
    public SocialStore(string databasePath, ILogger<SocialStore> logger)
    {
        DatabasePath = databasePath;
        _logger = logger;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            DefaultTimeout = 5,
        }.ToString();
    }

    /// <summary>Gets the database file's full path.</summary>
    public string DatabasePath { get; }

    /// <summary>
    /// Writes a timestamp to a probe row and reads it back. Called by the health check.
    /// Never throws for database problems; they come back as a <see cref="StoreHealth"/>.
    /// </summary>
    /// <returns>The result.</returns>
    public StoreHealth CheckHealth()
    {
        try
        {
            EnsureInitialized();
            var stamp = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

            using var connection = Open();

            // Write and read back inside one transaction, so a concurrent check (the notifier
            // bot and an admin at the same moment) can't overwrite the probe in between.
            string? readBack;
            using (var transaction = connection.BeginTransaction())
            {
                using (var write = connection.CreateCommand())
                {
                    write.Transaction = transaction;
                    write.CommandText = "INSERT INTO health_probe (id, checked_at) VALUES (1, $stamp) "
                        + "ON CONFLICT(id) DO UPDATE SET checked_at = excluded.checked_at;";
                    write.Parameters.AddWithValue("$stamp", stamp);
                    write.ExecuteNonQuery();
                }

                using (var read = connection.CreateCommand())
                {
                    read.Transaction = transaction;
                    read.CommandText = "SELECT checked_at FROM health_probe WHERE id = 1;";
                    readBack = read.ExecuteScalar() as string;
                }

                transaction.Commit();
            }

            var version = ReadSchemaVersion(connection);
            return readBack == stamp
                ? new StoreHealth(true, null, version)
                : new StoreHealth(false, "readback_mismatch", version);
        }
        catch (NewerSchemaException ex)
        {
            return new StoreHealth(false, "newer_schema", ex.FoundVersion);
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            var problem = Classify(ex);
            _logger.LogWarning(ex, "Project A: database check failed ({Problem}) for {Path}", problem, DatabasePath);
            return new StoreHealth(false, problem, 0);
        }
    }

    /// <summary>Maps a failure to a fixed problem code. Public for tests.</summary>
    /// <param name="ex">The exception.</param>
    /// <returns>A code from the list in the remarks.</returns>
    public static string Classify(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "not_writable",
        IOException => "not_writable",
        SqliteException s => s.SqliteErrorCode switch
        {
            5 or 6 => "locked",                 // SQLITE_BUSY, SQLITE_LOCKED
            8 or 13 or 14 or 23 => "not_writable", // SQLITE_READONLY, SQLITE_FULL, SQLITE_CANTOPEN, SQLITE_AUTH
            11 or 26 => "corrupt",              // SQLITE_CORRUPT, SQLITE_NOTADB
            _ => "error",
        },
        _ => "error",
    };

    /// <summary>
    /// Opens a connection to an up-to-date database (creating or upgrading it first if
    /// needed). The caller disposes it. Database problems surface as
    /// <see cref="SqliteException"/> or <see cref="StoreUnavailableException"/>, which
    /// endpoints turn into a fixed 503 answer.
    /// </summary>
    /// <remarks>
    /// File-system errors are wrapped rather than passed on as <see cref="IOException"/>,
    /// because ASP.NET also uses IOException for request problems (an oversized body, a
    /// dropped connection) that must not be reported as a database fault.
    /// </remarks>
    /// <returns>An open connection.</returns>
    internal SqliteConnection OpenReady()
    {
        try
        {
            EnsureInitialized();
            return Open();
        }
        catch (NewerSchemaException ex)
        {
            throw new StoreUnavailableException("newer_schema", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new StoreUnavailableException(Classify(ex), ex);
        }
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        // busy_timeout: wait for a lock rather than failing at once. foreign_keys: enforced
        // relationships for tables that reference each other (comments, likes).
        pragma.CommandText = "PRAGMA busy_timeout = 5000; PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    private void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        lock (_initLock)
        {
            if (_initialized)
            {
                return;
            }

            var folder = Path.GetDirectoryName(DatabasePath);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            using var connection = Open();

            // Check the schema before changing anything: a file from a newer Project A must
            // be left exactly as it is (switching to WAL below rewrites the file header).
            var found = ReadSchemaVersion(connection);
            if (found > CurrentSchemaVersion)
            {
                throw new NewerSchemaException(found);
            }

            using (var wal = connection.CreateCommand())
            {
                // WAL: readers don't block the writer. Persistent per file, so set once.
                wal.CommandText = "PRAGMA journal_mode = WAL;";
                wal.ExecuteNonQuery();
            }

            using (var transaction = connection.BeginTransaction())
            {
                using var create = connection.CreateCommand();
                create.Transaction = transaction;
                create.CommandText =
                    "CREATE TABLE IF NOT EXISTS schema_version (version INTEGER NOT NULL);" +
                    "CREATE TABLE IF NOT EXISTS health_probe (id INTEGER PRIMARY KEY CHECK (id = 1), checked_at TEXT NOT NULL);";
                create.ExecuteNonQuery();

                // A new file starts at version 1 (the tables above), then takes every upgrade.
                var from = Math.Max(found, 1);
                foreach (var (version, sql) in Migrations)
                {
                    if (version <= from)
                    {
                        continue;
                    }

                    using var migrate = connection.CreateCommand();
                    migrate.Transaction = transaction;
                    migrate.CommandText = sql;
                    migrate.ExecuteNonQuery();
                }

                if (found != CurrentSchemaVersion)
                {
                    using var setVersion = connection.CreateCommand();
                    setVersion.Transaction = transaction;
                    setVersion.CommandText = "DELETE FROM schema_version; INSERT INTO schema_version (version) VALUES ($v);";
                    setVersion.Parameters.AddWithValue("$v", CurrentSchemaVersion);
                    setVersion.ExecuteNonQuery();
                }

                transaction.Commit();
            }

            _initialized = true;
            if (found != 0 && found != CurrentSchemaVersion)
            {
                _logger.LogInformation("Project A: database upgraded from schema {From} to {To}", found, CurrentSchemaVersion);
            }

            _logger.LogInformation("Project A: database ready at {Path} (schema {Version})", DatabasePath, CurrentSchemaVersion);
        }
    }

    private static int ReadSchemaVersion(SqliteConnection connection)
    {
        using var exists = connection.CreateCommand();
        exists.CommandText = "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = 'schema_version';";
        if (Convert.ToInt64(exists.ExecuteScalar(), CultureInfo.InvariantCulture) == 0)
        {
            return 0;
        }

        using var read = connection.CreateCommand();
        read.CommandText = "SELECT max(version) FROM schema_version;";
        var value = read.ExecuteScalar();
        return value is null or DBNull ? 0 : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    /// <summary>Thrown when the file was created by a newer Project A; it is then left untouched.</summary>
    /// <remarks>Endpoints see it as <see cref="StoreUnavailableException"/> with problem <c>newer_schema</c>.</remarks>
    private sealed class NewerSchemaException : Exception
    {
        public NewerSchemaException(int foundVersion)
            : base($"Database schema {foundVersion} is newer than this build understands ({CurrentSchemaVersion}).")
        {
            FoundVersion = foundVersion;
        }

        public int FoundVersion { get; }
    }
}

/// <summary>
/// The database can't be used right now (folder not writable, disk full, file from a newer
/// Project A). Carries a fixed problem code from <see cref="SocialStore.Classify"/>, never a path.
/// </summary>
public sealed class StoreUnavailableException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="StoreUnavailableException"/> class.</summary>
    /// <param name="problem">Fixed problem code.</param>
    /// <param name="inner">The underlying error (logged, never sent to the browser).</param>
    public StoreUnavailableException(string problem, Exception inner)
        : base("Project A's database is unavailable: " + problem, inner)
    {
        Problem = problem;
    }

    /// <summary>Gets the fixed problem code.</summary>
    public string Problem { get; }
}
