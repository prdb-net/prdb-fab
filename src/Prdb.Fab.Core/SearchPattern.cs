namespace Prdb.Fab.Core;

/// <summary>
/// The <c>LIKE</c> pattern a typed search box becomes, and the escape character
/// it is read with.
/// </summary>
/// <remarks>
/// <para>
/// In no area's namespace for the same reason as <see cref="Paging"/>: the
/// catalogue grids, the library and the operation log all search the same way,
/// and each of them carried its own copy of these three <c>Replace</c> calls.
/// All three copies were correct — the risk was the fourth one, written
/// slightly differently.
/// </para>
/// <para>
/// The escaping is not decoration. Without it a search for <c>50%</c> matches
/// everything and a search for <c>a_b</c> matches <c>axb</c>, because both
/// characters are <c>LIKE</c>'s own wildcards. It is not an injection defence —
/// EF parameterises the value either way — it is the difference between a
/// search box that searches for what was typed and one that does not.
/// </para>
/// </remarks>
public static class SearchPattern
{
    /// <summary>
    /// The escape character every one of these searches is read with. A
    /// constant, so that it reaches an expression tree as one.
    /// </summary>
    public const string Escape = "\\";

    /// <summary>
    /// The pattern matching every row that contains <paramref name="value"/>,
    /// with the wildcards in it taken literally.
    /// </summary>
    public static string Containing(string value)
    {
        // The escape character first: doing it after the wildcards would escape
        // the backslashes this method has just put in.
        var literal = value.Trim()
            .Replace(Escape, Escape + Escape, StringComparison.Ordinal)
            .Replace("%", Escape + "%", StringComparison.Ordinal)
            .Replace("_", Escape + "_", StringComparison.Ordinal);

        return $"%{literal}%";
    }
}
