using System.Text.Json;
using System.Text.Json.Nodes;

namespace Prdb.Fab.Core.Backup;

/// <summary>
/// What happened when a file was handed to the tool and asked to be a Backup.
/// </summary>
public enum BackupIntakeOutcome
{
    /// <summary>It is a document of a format this build understands.</summary>
    Read,

    /// <summary>
    /// It is not one. Not JSON at all, or JSON with no envelope — the same
    /// answer either way, because the person is holding a file rather than a
    /// parse tree.
    /// </summary>
    NotABackup,

    /// <summary>
    /// Written by a newer tool than this one, and therefore possibly in a shape
    /// this build would read the recognisable parts of and silently drop the
    /// rest. ADR 0009 refuses it and names the version instead.
    /// </summary>
    FromANewerTool,
}

/// <param name="FormatVersion">
/// What the envelope said, whether or not this build can read it — so that a
/// refusal can quote the file rather than describe it.
/// </param>
/// <param name="ToolVersion">
/// The build that wrote it, which is the half of the refusal a person can act
/// on: it names the image to pull.
/// </param>
public sealed record BackupIntake(
    BackupIntakeOutcome Outcome,
    BackupDocument? Document,
    int? FormatVersion,
    string? ToolVersion);

/// <summary>
/// Turning the text of a file into a document this build understands, or into
/// the reason it will not.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0009 asks for two things here and this is the only place either happens:
/// a file from a newer tool is refused by name rather than partially read, and
/// an older supported format is migrated forward. The migration is deliberately
/// in DTO space — over the JSON, before anything touches the database — so that
/// a restore is either of the current shape or is not attempted.
/// </para>
/// <para>
/// <see cref="Upgrades"/> carries one step, and it is the shape every later one
/// should have: a document that predates a section gets an empty one, because
/// <em>the section did not exist</em> and <em>the section is empty</em> are the
/// same installation. The seam was built before there was a step to put in it,
/// which is why adding the step was three lines rather than a decision.
/// </para>
/// </remarks>
public static class BackupReader
{
    /// <summary>
    /// One entry per format that is not the current one: given a document of
    /// version <c>n</c> as a tree, leave it as version <c>n + 1</c>.
    /// </summary>
    /// <remarks>
    /// A tree rather than a record, because the record is the <em>current</em>
    /// shape and an old document is by definition not it. Editing the tree is
    /// what lets a field be renamed, split or defaulted without a second copy
    /// of every DTO being kept alive for the sake of one migration.
    /// </remarks>
    private static readonly IReadOnlyDictionary<int, Action<JsonObject>> Upgrades =
        new Dictionary<int, Action<JsonObject>>
        {
            // 1 -> 2: ADR 0062's flag on a filed Video File whose evidence prdb
            // has withdrawn. A document written before it existed describes an
            // installation that has flagged nothing.
            [1] = envelope =>
            {
                envelope["identificationFlags"] = new JsonArray();
                envelope["formatVersion"] = BackupFormat.Version;
            },
        };

    public static BackupIntake Read(string text)
    {
        JsonObject? envelope;

        try
        {
            envelope = JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException)
        {
            return NotABackup;
        }

        if (envelope is null) return NotABackup;

        // Read straight off the tree rather than from a parsed document. The
        // version is what decides whether the document can be parsed at all, so
        // asking the parser first would be asking the question backwards.
        if (!envelope.TryGetPropertyValue("formatVersion", out var stated)
            || stated is null
            || stated.GetValueKind() != JsonValueKind.Number)
        {
            return NotABackup;
        }

        var version = stated.GetValue<int>();
        var tool = envelope.TryGetPropertyValue("toolVersion", out var wrote)
            && wrote?.GetValueKind() == JsonValueKind.String
                ? wrote.GetValue<string>()
                : null;

        if (version > BackupFormat.Version)
        {
            return new BackupIntake(BackupIntakeOutcome.FromANewerTool, null, version, tool);
        }

        // An unknown older version is not a backup this build can make sense of
        // either. It is the same answer as unreadable, and for the honest
        // reason: there is no step to run.
        for (var at = version; at < BackupFormat.Version; at++)
        {
            if (!Upgrades.TryGetValue(at, out var upgrade))
            {
                return new BackupIntake(BackupIntakeOutcome.NotABackup, null, version, tool);
            }

            upgrade(envelope);
        }

        BackupDocument? document;

        try
        {
            document = envelope.Deserialize<BackupDocument>(BackupJson.Options);
        }
        catch (JsonException)
        {
            return new BackupIntake(BackupIntakeOutcome.NotABackup, null, version, tool);
        }

        return document is null
            ? new BackupIntake(BackupIntakeOutcome.NotABackup, null, version, tool)
            : new BackupIntake(BackupIntakeOutcome.Read, document, version, tool);
    }

    private static BackupIntake NotABackup { get; } =
        new(BackupIntakeOutcome.NotABackup, null, null, null);
}
