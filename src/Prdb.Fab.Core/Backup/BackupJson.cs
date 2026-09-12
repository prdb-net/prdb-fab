using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Prdb.Fab.Core.Backup;

/// <summary>
/// The one way a <see cref="BackupDocument"/> becomes text and text becomes a
/// document.
/// </summary>
/// <remarks>
/// ADR 0009 asks for JSON, UTF-8 and indented, and gives the reason: the failure
/// mode is somebody standing in front of a Restore that will not complete, and
/// a file they can open answers "what is actually in here" without us. So the
/// settings here are all in service of being read — one place, so the document
/// written and the document read cannot disagree.
/// </remarks>
public static class BackupJson
{
    /// <summary>
    /// Indented, camel-cased, enumerations by name, and non-ASCII left alone.
    /// </summary>
    /// <remarks>
    /// The relaxed encoder is the one choice here worth arguing. The strict
    /// default escapes every non-ASCII character, and a library full of titles
    /// and paths that are not ASCII would arrive as a wall of <c>ä</c> —
    /// unreadable, which is the property this document exists to have. What the
    /// strict encoder protects against is a document interpolated into HTML or
    /// a script, and this one is a file the user saves.
    /// </remarks>
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The document as the file holds it.</summary>
    public static string Write(BackupDocument document) =>
        JsonSerializer.Serialize(document, Options);

    /// <summary>
    /// The document a file holds, or null where the text is not one.
    /// </summary>
    /// <remarks>
    /// Null rather than an exception because the caller is a person handing the
    /// tool a file: what follows is a sentence about the file, not a stack
    /// trace. What the document then <em>says</em> — its format version, its
    /// tool version — is read from the document rather than guarded here.
    /// </remarks>
    public static BackupDocument? Read(string text)
    {
        try
        {
            return JsonSerializer.Deserialize<BackupDocument>(text, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
