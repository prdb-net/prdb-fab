namespace Prdb.Fab.Core.Catalogue;

/// <summary>
/// What happens to a table's rows when the prdb key entered is one belonging to
/// a different account. ADR 0033 makes this a property each table declares
/// rather than prose, so that changing the key is a list of deletes that can be
/// read off the schema instead of a procedure somebody has to keep in step with
/// new tables.
/// </summary>
public enum AccountClass
{
    /// <summary>
    /// Belongs to no account and survives the change — the whole catalogue of
    /// videos, sites and actors, and everything the tool holds about itself.
    /// ADR 0013 keeps the library pinned across it for the same reason.
    /// </summary>
    AccountFree,

    /// <summary>
    /// Deleted when the key belongs to a different account, because the rows
    /// are the other account's answers: the wanted list, the favourites, and
    /// the cursors that walked them.
    /// </summary>
    AccountScoped,

    /// <summary>
    /// Carries the account it was made under and is never deleted. ADR 0019 and
    /// ADR 0022 need this so that what one account submitted is not counted as
    /// sent by an account prdb never heard it from.
    /// </summary>
    AccountStamped,

    /// <summary>
    /// The class is decided per row rather than per table, which in this schema
    /// is <c>FeedCursor</c> alone: three of its rows follow the account and the
    /// rest do not. A table that says this has to name the classifier that
    /// covers every row it can hold — see <see cref="Feeds.AccountClassOf"/> —
    /// or the declaration says nothing.
    /// </summary>
    PerRow,
}
