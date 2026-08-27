using System.Data.Common;

namespace Prdb.Fab.Infrastructure.Persistence;

/// <summary>
/// The four settings ADR 0039 measured, and the one place they are written.
/// </summary>
public static class SqlitePragmas
{
    /// <summary>
    /// Applied to a connection that has just been opened.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every one of them, every time. ADR 0039's finding is that
    /// <c>Microsoft.Data.Sqlite</c> pools connections and hands one back in
    /// whatever state it was left in, so "set it once at startup" is a sentence
    /// about a connection nobody can point at afterwards. Only
    /// <c>journal_mode</c> is a property of the file and survives; the other
    /// three cost a few microseconds and remove the question.
    /// </para>
    /// <para>
    /// <c>busy_timeout</c> is what makes ADR 0004's one-writer rule enough on
    /// its own: a second writer waits rather than being told the database is
    /// locked.
    /// </para>
    /// </remarks>
    public static void Apply(DbConnection connection)
    {
        Execute(connection, "PRAGMA journal_mode=WAL;");
        Execute(connection, "PRAGMA synchronous=NORMAL;");
        Execute(connection, "PRAGMA busy_timeout=5000;");
        Execute(connection, "PRAGMA foreign_keys=ON;");
    }

    private static void Execute(DbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
