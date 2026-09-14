namespace Prdb.Fab.Core.Sync;

/// <summary>
/// Every number ADR 0064 fixes about publishing a generated preview to prdb, in
/// the one place the code reads them from.
/// </summary>
/// <remarks>
/// <para>
/// The companion to <see cref="UserPreviewContract"/> and written on the same
/// rule: a contract written twice is a contract that will disagree with itself,
/// so the ADR's table and this class are the same figures and the ADR is the
/// argument for each of them. What is <em>not</em> here is anything ADR 0061
/// already fixed — the sheet and WebVTT ceilings and the tile count are read
/// from that contract rather than restated, because producing something this
/// tool would itself refuse to display is the clearest possible sign a number
/// is wrong.
/// </para>
/// <para>
/// None of it is a setting. ADR 0020 admits a control where the answer lives
/// outside anything the tool can observe; the one value on this channel that
/// qualifies is whether to publish at all, and that is the switch.
/// </para>
/// </remarks>
public static class PreviewPublicationContract
{
    /// <summary>
    /// Which generation this tool's output is, carried on every publication
    /// row.
    /// </summary>
    /// <remarks>
    /// One output per eligible file <em>per version</em>, so that a later
    /// release may choose a better geometry without the rows it already sent
    /// becoming unreadable. Raising it does not resubmit anything: ADR 0064
    /// makes republication an explicit request, and a version is what such a
    /// request would be scoped by.
    /// </remarks>
    public const int OutputVersion = 1;

    /// <summary>The width of one tile of a generated Sprite Sheet.</summary>
    /// <remarks>
    /// The Review contact sheet's size (ADR 0053), and for the same reason: it
    /// is the size a scrubbing strip is actually read at, and it is what
    /// Jellyfin's own trickplay uses. Sharing the figure is not sharing the
    /// code — the two are produced by different passes for different surfaces.
    /// </remarks>
    public const int TileWidth = 320;

    /// <summary>The height of one tile of a generated Sprite Sheet.</summary>
    public const int TileHeight = 180;

    /// <summary>
    /// How much of a Video File one tile is meant to stand for.
    /// </summary>
    /// <remarks>
    /// Ten seconds, which is the interval an ordinary scene gets: a
    /// twenty-minute scene becomes 120 tiles. It is a target rather than a rule
    /// — <see cref="TilesFor"/> stretches it for a long file and shortens it for
    /// a short one, because the bounds either side of it matter more than the
    /// interval does.
    /// </remarks>
    public static readonly TimeSpan ATile = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The fewest tiles a generated Sprite Sheet may be cut into.
    /// </summary>
    /// <remarks>
    /// Twenty-four. Below it the sheet stops being something anybody can scrub
    /// against — six pictures of a three-minute scene is a contact sheet, which
    /// the Review Queue already has — and prdb's population is better served by
    /// nothing than by that.
    /// </remarks>
    public const int FewestTiles = 24;

    /// <summary>
    /// The most tiles a generated Sprite Sheet may be cut into, which is the
    /// most this tool will accept from anybody else.
    /// </summary>
    public const int MostTiles = UserPreviewContract.Tiles;

    /// <summary>The most a generated sheet may weigh.</summary>
    public const long ASheet = UserPreviewContract.ASprite;

    /// <summary>The most a generated WebVTT may weigh.</summary>
    public const long AVtt = UserPreviewContract.AVtt;

    /// <summary>
    /// How long one generation may run before it is abandoned.
    /// </summary>
    /// <remarks>
    /// Twenty minutes, and it is measured rather than guessed. A twenty-minute
    /// 1080p h264 file decoded end to end on a single thread produces its sheet
    /// in about eighty seconds, so a two-hour file on hardware several times
    /// slower than a desktop is the case this has to leave room for. It is
    /// deliberately generous: nothing waits on this pass, and a timeout that
    /// fires on a long film would make exactly the files with the most to show
    /// the ones that never publish.
    /// </remarks>
    public static readonly TimeSpan Generation = TimeSpan.FromMinutes(20);

    /// <summary>How often the generating routine looks for work.</summary>
    /// <remarks>
    /// Five minutes, in the <c>Bulk</c> lane, one file per run. It is a routine
    /// with a work set (ADR 0032), so an installation with nothing eligible
    /// spends nothing on it; the cadence says how often to look rather than how
    /// often to act.
    /// </remarks>
    public static readonly TimeSpan GenerationCadence = TimeSpan.FromMinutes(5);

    /// <summary>How often the uploading routine looks for work.</summary>
    /// <remarks>
    /// A minute, in the <c>Bulk</c> lane, one upload per run. One at a time
    /// because an upload is megabytes on a connection somebody else is also
    /// using, and because <see cref="PrdbWork.Publications"/>'s reserve is a
    /// share of an hourly budget rather than a concurrency limit.
    /// </remarks>
    public static readonly TimeSpan UploadCadence = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How many generated publications may be waiting to be sent before
    /// generation stops.
    /// </summary>
    /// <remarks>
    /// Fifty, which is around a hundred megabytes of sheets. A count rather
    /// than a byte ceiling because every waiting file is waiting for its own
    /// upload: evicting the oldest would mean re-deciding which obligation to
    /// abandon, and stopping means the backlog drains first. The stop is a
    /// Brake — nothing is broken, and what it holds back is what was asked for.
    /// </remarks>
    public const int MostWaiting = 50;

    /// <summary>
    /// How long an unsent publication intent survives nothing happening to it.
    /// </summary>
    /// <remarks>
    /// Thirty days: shorter than the Recent Window's ninety and longer than any
    /// plausible outage. A month-old unsent publication belongs to an
    /// installation that was switched off, and the file it was made from has
    /// had a month to become a different file.
    /// </remarks>
    public static readonly TimeSpan IntentExpiry = TimeSpan.FromDays(30);

    /// <summary>
    /// The <c>previewImageType</c> every publication carries.
    /// </summary>
    /// <remarks>
    /// Matched case-insensitively by the POST's own pattern, and sent in the
    /// casing the read endpoints answer with so that a row read back compares
    /// equal without anything normalising it.
    /// </remarks>
    public const string PreviewImageType = "SpriteSheet";

    /// <summary>
    /// The <c>displayOrder</c> every publication carries.
    /// </summary>
    /// <remarks>
    /// Zero, because there is exactly one publication per file and per output
    /// version. The field orders one submitter's pictures of one Video against
    /// each other, and this tool never has two to order.
    /// </remarks>
    public const int DisplayOrder = 0;

    /// <summary>The multipart filename the generated sheet is sent under.</summary>
    /// <remarks>
    /// A fixed string, and that is the point rather than a detail. The filename
    /// on a multipart part is the one field of the body that carries a local
    /// name by accident, and ADR 0064 says the source Video File never leaves —
    /// including its name.
    /// </remarks>
    public const string SheetFilename = "preview.jpg";

    /// <summary>The multipart filename the generated WebVTT is sent under.</summary>
    public const string VttFilename = "preview.vtt";

    /// <summary>
    /// How many tiles a file of this Runtime is cut into.
    /// </summary>
    /// <remarks>
    /// <c>clamp(ceil(runtime / 10 s), 24, 400)</c>, computed from the Runtime
    /// the Probe already recorded. ADR 0021's rule holds and ADR 0061's
    /// amendment to it is the one that applies: the stored value is read again,
    /// the file is not.
    /// </remarks>
    public static int TilesFor(TimeSpan runtime)
    {
        if (runtime <= TimeSpan.Zero)
        {
            return FewestTiles;
        }

        var wanted = (int)Math.Ceiling(runtime / ATile);

        return Math.Clamp(wanted, FewestTiles, MostTiles);
    }

    /// <summary>
    /// How many columns a sheet of this many tiles is laid out in.
    /// </summary>
    /// <remarks>
    /// <c>ceil(sqrt(tiles))</c>, so the sheet stays near-square. At the tile
    /// ceiling that is 20 columns and 6400x3600 pixels rather than ten columns
    /// and 3200x7200 — the same bytes in a shape more decoders are happy with.
    /// </remarks>
    public static int ColumnsFor(int tiles) =>
        tiles <= 0 ? 1 : (int)Math.Ceiling(Math.Sqrt(tiles));

    /// <summary>How many rows a sheet of this many tiles is laid out in.</summary>
    public static int RowsFor(int tiles) =>
        tiles <= 0 ? 1 : (int)Math.Ceiling((double)tiles / ColumnsFor(tiles));

    /// <summary>
    /// Where in the file the tile at <paramref name="index"/> is taken from.
    /// </summary>
    /// <remarks>
    /// Evenly spaced across the whole Runtime, which is what makes the output
    /// deterministic: the same file at the same output version produces the same
    /// sheet, so a rerun after a crash is the same bytes rather than a second
    /// picture.
    /// </remarks>
    public static TimeSpan TileAt(int index, int tiles, TimeSpan runtime) =>
        tiles <= 0 ? TimeSpan.Zero : runtime * ((index + 0.5) / tiles);
}
