// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 Fr4ctalNoise and Project A contributors.

using System;
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
    public const int CurrentSchemaVersion = 1;

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
            using (var write = connection.CreateCommand())
            {
                write.CommandText = "INSERT INTO health_probe (id, checked_at) VALUES (1, $stamp) "
                    + "ON CONFLICT(id) DO UPDATE SET checked_at = excluded.checked_at;";
                write.Parameters.AddWithValue("$stamp", stamp);
                write.ExecuteNonQuery();
            }

            string? readBack;
            using (var read = connection.CreateCommand())
            {
                read.CommandText = "SELECT checked_at FROM health_probe WHERE id = 1;";
                readBack = read.ExecuteScalar() as string;
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

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        // busy_timeout: wait for a lock rather than failing at once. foreign_keys: enforced
        // relationships for the tables that arrive in later steps.
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

                if (found == 0)
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
