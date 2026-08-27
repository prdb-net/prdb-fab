namespace Prdb.Fab.Core.Connections;

/// <summary>
/// What happened when an indexer was checked and added.
/// </summary>
public enum IndexerConnectionOutcome
{
    /// <summary>A real search came back, and the row is there.</summary>
    Saved,

    /// <summary>
    /// The indexer refused the key. ADR 0010 requires a real search for exactly
    /// this: <c>t=caps</c> answers without a key on three of the four
    /// implementations surveyed, so it confirms nothing.
    /// </summary>
    WrongKey,

    /// <summary>
    /// The key is fine and the indexer has had enough for today. Worth
    /// retrying, and not a reason to change anything.
    /// </summary>
    LimitReached,

    /// <summary>
    /// Something answered and it was not a Newznab API — most often an HTML
    /// page, which is what a blocked or misconfigured address looks like.
    /// </summary>
    NotAnIndexer,

    /// <summary>
    /// The indexer answered with a refusal of its own that is neither the key
    /// nor a limit. Its own wording is carried along, because the five
    /// implementations surveyed disagree about everything except the shape of
    /// the document that says so.
    /// </summary>
    Refused,

    /// <summary>Nothing answered: a timeout, a refused connection, a bad address.</summary>
    NotRightNow,

    /// <summary>
    /// This indexer is already configured. Two rows for one address would walk
    /// it twice, spend its budget twice, and give one package two release
    /// identities.
    /// </summary>
    AlreadyAdded,

    /// <summary>
    /// The indexer works and has nothing this tool can search. Refused rather
    /// than stored, because an indexer that cannot answer the only question
    /// this tool asks is a Gap that would only be discovered by the first sweep
    /// finding nothing.
    /// </summary>
    NoCategories,
}

/// <summary>One node of an indexer's own category tree, as <c>t=caps</c> reports it.</summary>
/// <remarks>
/// The ids are site-specific by design, which is why nothing here keys off one.
/// </remarks>
public sealed record CapsCategory(int Id, string Name, IReadOnlyList<CapsCategory> Children)
{
    public CapsCategory(int id, string name)
        : this(id, name, [])
    {
    }
}

/// <summary>
/// The rules behind the indexer step: what an error document means, and which
/// of an indexer's categories this tool is going to search.
/// </summary>
public static class IndexerConnection
{
    /// <summary>
    /// The names a top-level category has to carry for this tool to search it.
    /// </summary>
    /// <remarks>
    /// ADR 0002 and the Newznab research both land in the same place: the
    /// numbers are worthless. <c>6070</c> is <em>Packs</em> in the spec and in
    /// nZEDb and <em>Other</em> in Prowlarr's canonical table, and <c>6999</c>
    /// exists in both PHP servers and in neither client. The name is the only
    /// part of a caps document worth reading, so the tree is matched on it —
    /// and both spellings that appear in the wild are accepted, since the tree
    /// is the indexer's own and nobody agreed on one word.
    /// </remarks>
    private static readonly string[] AdultCategoryNames = ["xxx", "adult"];

    /// <summary>The separator between a category and its parent in a stored name.</summary>
    private const char NameSeparator = '/';

    public static string Sentence(IndexerConnectionOutcome outcome, string? refusal = null) => outcome switch
    {
        IndexerConnectionOutcome.Saved =>
            "The indexer answered a real search, and its categories were read "
            + "from its own list.",

        IndexerConnectionOutcome.WrongKey =>
            "The indexer refused that key. It is the API key from your account "
            + "page there, and this tool checks it with a real search rather "
            + "than with a capabilities call — most indexers answer that "
            + "one to anybody.",

        IndexerConnectionOutcome.LimitReached =>
            "The key is right and the indexer has had enough requests from it "
            + "for now. Nothing to correct; try again later.",

        IndexerConnectionOutcome.NotAnIndexer =>
            "Something answered at that address and it was not a Newznab API. "
            + "The address is the API one, which usually ends in /api — and "
            + "an HTML page coming back is what a blocked or mistyped one looks "
            + "like.",

        IndexerConnectionOutcome.Refused =>
            $"The indexer refused: {refusal ?? "no reason given"}.",

        IndexerConnectionOutcome.NotRightNow =>
            "Nothing answered at that address. That is the indexer or the "
            + "network rather than the key.",

        IndexerConnectionOutcome.AlreadyAdded =>
            "That address is already configured as an indexer. Correct the one "
            + "that is there rather than adding it twice.",

        IndexerConnectionOutcome.NoCategories =>
            "The indexer answered, and its category list holds nothing this "
            + "tool searches for. It has not been added, because an indexer "
            + "with nothing to search is a gap that would otherwise be found by "
            + "the first sweep that came back empty.",

        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };

    /// <summary>
    /// What an error document means, given the status it arrived with.
    /// </summary>
    /// <remarks>
    /// The research surveyed five implementations and found them disagreeing on
    /// every part of this: a wrong key is code <c>100</c> at HTTP 200 in the
    /// spec and in newznab classic, code <c>403</c> at HTTP 403 in nZEDb, and
    /// code <c>100</c> at HTTP 401 in NNTmux. So all four signals are read
    /// together rather than any one of them being trusted, which is what both
    /// Sonarr and Prowlarr ended up doing too.
    /// </remarks>
    public static IndexerConnectionOutcome ForError(int httpStatus, int? errorCode, string? description)
    {
        if (errorCode is >= 100 and <= 199 or 401 or 403
            || httpStatus is 401 or 403
            || Mentions(description, "credentials")
            || Mentions(description, "api key")
            || Mentions(description, "apikey"))
        {
            return IndexerConnectionOutcome.WrongKey;
        }

        if (errorCode is 429 or 500 or 501 || httpStatus == 429 || Mentions(description, "limit reached"))
        {
            return IndexerConnectionOutcome.LimitReached;
        }

        return IndexerConnectionOutcome.Refused;
    }

    /// <summary>
    /// The categories this tool will search at this indexer, named rather than
    /// numbered.
    /// </summary>
    /// <remarks>
    /// ADR 0033 puts <em>the category names matched by name</em> on the
    /// exported row, and this is why the stored value is names: the ids are the
    /// indexer's own, they are re-read from caps whenever the walk needs them,
    /// and a backup restored against an indexer that renumbered its tree still
    /// says what was meant. A child is qualified by its parent, since a
    /// subcategory called <c>DVD</c> means nothing on its own.
    /// </remarks>
    public static IReadOnlyList<string> MatchedByName(IEnumerable<CapsCategory> tree)
    {
        var matched = new List<string>();

        foreach (var category in tree)
        {
            if (!AdultCategoryNames.Contains(category.Name.Trim().ToLowerInvariant()))
            {
                continue;
            }

            var parent = category.Name.Trim();
            matched.Add(parent);
            matched.AddRange(category.Children.Select(child => $"{parent}{NameSeparator}{child.Name.Trim()}"));
        }

        return matched;
    }

    private static bool Mentions(string? description, string word) =>
        description?.Contains(word, StringComparison.OrdinalIgnoreCase) == true;
}
