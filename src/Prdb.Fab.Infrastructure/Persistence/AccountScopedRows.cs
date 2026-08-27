using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.Catalogue;

namespace Prdb.Fab.Infrastructure.Persistence;

/// <summary>
/// The half of the local data that belongs to the prdb account, and the deletes
/// that drop it when the key entered belongs to a different one.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0013 decides what goes: the wanted list, the favourites and their three
/// cursors are deleted; the catalogue belongs to no account and is kept, and so
/// is everything the tool holds about itself. ADR 0033 makes that
/// <strong>a list of deletes that can be read off the schema</strong> rather
/// than a procedure somebody keeps in step with new tables — which is why this
/// class walks the model instead of naming three tables.
/// </para>
/// <para>
/// What makes that safe rather than clever is the guard beside it: every entity
/// type declares an <see cref="AccountClass"/> and a test walks the model and
/// fails over one that says nothing. So a table added later either answers or
/// the build stops, and a table that answers <see cref="AccountClass.AccountStamped"/>
/// — ADR 0019's record of what was reported, which survives scoped to the
/// account it was made under — is never swept up here, because nothing selects
/// it.
/// </para>
/// <para>
/// It does not block and it is not undoable. ADR 0010 settled that: people do
/// move accounts, and what the tool owes them is that the consequence is named
/// before it happens rather than discovered afterwards as a wanted list that
/// emptied itself.
/// </para>
/// </remarks>
public sealed class AccountScopedRows(FabDbContext context, ILogger<AccountScopedRows> logger)
{
    /// <summary>
    /// The tables a key change empties, in the order they are emptied.
    /// </summary>
    public IReadOnlyList<string> Tables => [.. InDeletionOrder().Select(NameOf)];

    /// <summary>
    /// The feeds whose position is another account's answer, and therefore
    /// worthless against this one. A cursor from somebody else's wanted list
    /// would silently start the new account's list in the middle.
    /// </summary>
    public static IReadOnlyList<Feed> Cursors { get; } =
        [.. Feeds.All.Where(feed => feed.AccountClassOf() == AccountClass.AccountScoped)];

    /// <summary>
    /// Drops the user's half. Returns how many rows went, for the log line the
    /// caller writes.
    /// </summary>
    public async Task<int> DropAsync(CancellationToken cancellationToken = default)
    {
        var removed = 0;

        foreach (var entity in InDeletionOrder())
        {
            // The name comes from the model rather than from anywhere a person
            // can reach, which is what makes the quoting below the whole of the
            // question. There is no typed set to call ExecuteDelete on without
            // naming each table in code, which is the thing this must not do.
            var delete = "DELETE FROM \"" + NameOf(entity) + "\";";

            removed += await context.Database.ExecuteSqlRawAsync(delete, cancellationToken);
        }

        removed += await context.FeedCursors
            .Where(row => Cursors.Contains(row.Feed))
            .ExecuteDeleteAsync(cancellationToken);

        logger.LogWarning(
            "The prdb key belongs to a different account, so {Count} row(s) of the previous "
            + "account's wanted list, favourites and feed positions were deleted. The catalogue "
            + "belongs to no account and was kept.",
            removed);

        return removed;
    }

    /// <summary>
    /// The account-scoped tables, dependents before the rows they point at.
    /// </summary>
    /// <remarks>
    /// None of the three point at each other today, so the order is nominal —
    /// and it is written anyway, because the alternative is a delete that
    /// starts failing on a foreign key the day somebody adds the fourth table.
    /// The dependency is read off the model like everything else here.
    /// </remarks>
    private IReadOnlyList<IEntityType> InDeletionOrder()
    {
        var pending = context.Model.GetEntityTypes()
            .Where(entity => AccountClasses.DeclaredBy(entity) == AccountClass.AccountScoped)
            .OrderBy(NameOf, StringComparer.Ordinal)
            .ToList();

        Refuse(context.Model);

        var ordered = new List<IEntityType>(pending.Count);

        while (pending.Count > 0)
        {
            var free = pending
                .Where(entity => !pending.Any(other =>
                    other != entity
                    && other.GetForeignKeys().Any(key => key.PrincipalEntityType == entity)))
                .ToList();

            if (free.Count == 0)
            {
                // Tables that point at each other in a circle. SQLite's own
                // cascades are what answer for that, and refusing to run would
                // leave the key changed with the previous account's list still
                // in place — which is the failure this whole class prevents.
                ordered.AddRange(pending);
                break;
            }

            ordered.AddRange(free);
            pending.RemoveAll(free.Contains);
        }

        return ordered;
    }

    /// <summary>
    /// The one table whose account class is decided per row is
    /// <c>FeedCursor</c>, and <see cref="Feeds.AccountClassOf"/> is where that
    /// answer lives. A second such table would need a classifier of its own,
    /// and a silent pass over it would leave another account's position behind.
    /// </summary>
    private static void Refuse(IModel model)
    {
        var unread = model.GetEntityTypes()
            .Where(entity => AccountClasses.DeclaredBy(entity) == AccountClass.PerRow)
            .Where(entity => entity.ClrType != typeof(FeedCursorRow))
            .Select(NameOf)
            .ToList();

        if (unread.Count > 0)
        {
            throw new InvalidOperationException(
                $"{string.Join(", ", unread)} says its account class is decided per row, and "
                + "nothing here knows how to read it (ADR 0033).");
        }
    }

    private static string NameOf(IReadOnlyEntityType entity) =>
        entity.GetTableName() ?? entity.ClrType.Name;
}
