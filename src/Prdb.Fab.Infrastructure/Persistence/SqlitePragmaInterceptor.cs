using System.Data.Common;

using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Prdb.Fab.Infrastructure.Persistence;

/// <summary>
/// Applies <see cref="SqlitePragmas"/> to every connection EF Core opens.
/// </summary>
/// <remarks>
/// An interceptor rather than a call at startup, for ADR 0039's reason: the
/// pool decides which physical connection a request gets, so the only moment
/// that is reliably "this connection, now" is the one it hands over here.
/// </remarks>
public sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        SqlitePragmas.Apply(connection);
        base.ConnectionOpened(connection, eventData);
    }

    public override Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        SqlitePragmas.Apply(connection);
        return base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }
}
