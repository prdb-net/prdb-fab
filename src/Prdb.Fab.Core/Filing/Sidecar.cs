using System.Globalization;
using System.Text;
using System.Xml;

namespace Prdb.Fab.Core.Filing;

/// <summary>What the sidecar says about one video, and the whole of it.</summary>
public sealed record SidecarMetadata(
    Guid VideoId,
    string Title,
    string? Studio,
    DateOnly? ReleaseDate,
    IReadOnlyList<string> Actors);

/// <summary>
/// The sidecar the media server reads: <c>movie.nfo</c>, root element
/// <c>&lt;movie&gt;</c>, in the exact shapes `prdb-ordeno` measured against a
/// real server.
/// </summary>
/// <remarks>
/// <para>
/// Three of those shapes fail silently rather than loudly, which is why they are
/// tested rather than commented. <c>&lt;premiered&gt;</c> is parsed against
/// exactly <c>yyyy-MM-dd</c>, and an ISO timestamp is discarded without a word,
/// taking the production year with it. A performer becomes a person only as an
/// <c>&lt;actor&gt;</c> with a <c>&lt;name&gt;</c> child and a <c>&lt;type&gt;</c>
/// the server knows. And one unescaped <c>&amp;</c>, <c>&lt;</c> or <c>&gt;</c>
/// makes the document unparseable, after which the server uses none of it and
/// falls back to the file name — which looks exactly like a metadata lookup that
/// returned nothing.
/// </para>
/// <para>
/// Five elements and no others. No plot, genre or tag, because prdb has none and
/// a field invented here is a field the media server believes. No runtime: the
/// server reads the streams out of the file itself, and prdb's `durationMs` is a
/// median across the files prdb holds — a different file's answer, written next
/// to this one. And no second <c>&lt;studio&gt;</c> for the network, which would
/// collapse two levels of the catalogue into one flat browsable list.
/// </para>
/// <para>
/// Nothing here touches a filesystem: the same metadata always produces the same
/// document, which is what lets the writing be tested apart from the replacing.
/// </para>
/// </remarks>
public static class Sidecar
{
    /// <summary>
    /// The one format <c>&lt;premiered&gt;</c> is parsed against by a default
    /// installation.
    /// </summary>
    public const string ReleaseDateFormat = "yyyy-MM-dd";

    /// <summary>The document for one video, ready to be written as UTF-8.</summary>
    public static string For(SidecarMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var document = new Utf8StringWriter();

        using (var writer = XmlWriter.Create(document, Settings))
        {
            writer.WriteStartElement("movie");
            writer.WriteElementString("title", Text(metadata.Title));

            // Bare, and never a timestamp. Absent rather than approximate where
            // prdb knows no date: a wrong date is believed, and an absent one is
            // simply absent.
            if (metadata.ReleaseDate is { } date)
            {
                writer.WriteElementString(
                    "premiered",
                    date.ToString(ReleaseDateFormat, CultureInfo.InvariantCulture));
            }

            if (Text(metadata.Studio) is { Length: > 0 } studio)
            {
                writer.WriteElementString("studio", studio);
            }

            foreach (var actor in metadata.Actors)
            {
                if (Text(actor) is not { Length: > 0 } name)
                {
                    // An empty name is dropped by the server anyway, and a person
                    // with no name is not something to have written. Skipping it
                    // here keeps what was written and what is displayed in step.
                    continue;
                }

                writer.WriteStartElement("actor");
                writer.WriteElementString("name", name);

                // Whatever prdb calls the role. A type the server does not know
                // produces a person filed under nothing rather than an actor.
                writer.WriteElementString("type", "Actor");
                writer.WriteEndElement();
            }

            // The receipt on the whole document: it comes back as a provider id
            // under the key `prdb`, which is what says the entry in the library
            // and the video in prdb are the same thing.
            writer.WriteStartElement("uniqueid");
            writer.WriteAttributeString("type", "prdb");
            writer.WriteString(metadata.VideoId.ToString("d", CultureInfo.InvariantCulture));
            writer.WriteEndElement();

            writer.WriteEndElement();
        }

        return document.ToString() + '\n';
    }

    private static XmlWriterSettings Settings => new()
    {
        Indent = true,
        IndentChars = "  ",

        // Written on a NAS and read on whatever the user's desktop is. One line
        // ending, chosen here rather than taken from the machine that happens to
        // be running the container.
        NewLineChars = "\n",
        Encoding = new UTF8Encoding(false),
    };

    /// <summary>
    /// Text as an XML document may carry it. The writer escapes the three
    /// characters that matter; what it cannot carry at all is a control
    /// character, which is taken out here rather than left to throw.
    /// </summary>
    private static string Text(string? value) =>
        new(
            (value ?? string.Empty)
                .Where(character => !char.IsControl(character) || character is '\t')
                .ToArray());

    private sealed class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
    }
}
