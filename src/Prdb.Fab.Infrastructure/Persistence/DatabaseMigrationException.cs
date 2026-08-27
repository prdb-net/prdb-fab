namespace Prdb.Fab.Infrastructure.Persistence;

/// <summary>
/// The database could not be brought to the schema this build expects. ADR 0004
/// stops the tool on this rather than running against a schema it does not
/// understand.
/// </summary>
public sealed class DatabaseMigrationException(string message, Exception inner)
    : Exception(message, inner);
