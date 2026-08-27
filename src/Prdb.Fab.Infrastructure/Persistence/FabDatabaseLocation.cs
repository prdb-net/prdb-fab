using Microsoft.Data.Sqlite;

namespace Prdb.Fab.Infrastructure.Persistence;

/// <summary>
/// Where the database is, and the connection string that reaches it. ADR 0034
/// mounts the directory; nothing here creates the mount.
/// </summary>
public sealed class FabDatabaseLocation
{
    public FabDatabaseLocation(string dataDirectory)
    {
        DataDirectory = dataDirectory;
        FilePath = Path.Combine(dataDirectory, "prdb-fab.db");

        // ADR 0039: only journal_mode lives in the file. Everything else is a
        // property of a connection, and pooling makes a connection's state
        // unknowable — so the pragmas are set on every open, by the interceptor,
        // rather than negotiated here.
        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = FilePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Default,
        }.ToString();
    }

    public string DataDirectory { get; }

    public string FilePath { get; }

    public string ConnectionString { get; }

    public void EnsureDirectoryExists() => Directory.CreateDirectory(DataDirectory);
}
