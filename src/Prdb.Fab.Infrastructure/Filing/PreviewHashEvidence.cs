using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.Filing;
using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Sync;

namespace Prdb.Fab.Infrastructure.Filing;

/// <summary>
/// ADR 0062's evidence: what the user previews this installation already holds
/// say about an Arriving File's hash, and what may be done about it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>No prdb request, ever.</strong> The evidence is rows put here because
/// somebody opened a Preview or filed a file, so the whole of this is one
/// indexed query — nothing to pace, nothing to deduplicate, no cadence, and no
/// new failure when prdb is unreachable. That is also why it is safe to run on
/// every turn of the arrival routine, which is what makes evidence arriving
/// <em>later</em> count: a file that has sat in the Review Queue for a week is
/// identified on the next tick after somebody opens its Video's Preview.
/// </para>
/// <para>
/// <strong>It is consulted only where prdb named nothing.</strong> The authority
/// is <c>POST /videos/identify</c> and stays it. That makes the design correct
/// whether or not prdb's own ladder already reads these bindings: if it does, it
/// answers first and none of this fires.
/// </para>
/// <para>
/// <strong>The stored hash is read and no file is touched.</strong> ADR 0021 has
/// a Video File read once; the sixteen characters that single reading produced
/// are what this matches on.
/// </para>
/// </remarks>
public sealed class PreviewHashEvidence(
    FabDbContext context,
    CatalogueRows catalogue,
    TimeProvider time,
    ILogger<PreviewHashEvidence> logger)
{
    /// <summary>
    /// How many Arriving Files one pass looks at.
    /// </summary>
    /// <remarks>
    /// The same bound the identification batch has, and for ADR 0032's reason
    /// rather than for a request's: a pass that walked an arbitrarily long queue
    /// would hold its lane for as long as the queue was.
    /// </remarks>
    public const int ABatch = 200;

    /// <summary>
    /// Looks at every Arriving File the contract makes eligible and applies
    /// what the evidence says.
    /// </summary>
    /// <returns>How many rows the pass changed.</returns>
    public async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        var eligible = await context.ArrivingFiles
            .AsTracking()
            .Where(row => row.OsHash != null
                && ((row.State == ArrivingFileState.AwaitingIdentification
                        && row.Reason == ArrivingFileReason.Unidentified
                        && row.VideoId == null)
                    // Already assigned from evidence and not yet moved: the
                    // withdrawal check, which is the only moment an assignment
                    // can still be taken back without touching a disk.
                    || (row.MatchedBy == IdentificationRung.PreviewHash
                        && row.State != ArrivingFileState.Filed)
                    // Already filed from evidence: nothing on disk moves, but
                    // the person is told.
                    || (row.MatchedBy == IdentificationRung.PreviewHash
                        && row.State == ArrivingFileState.Filed)))
            .OrderBy(row => row.Id)
            .Take(ABatch)
            .ToListAsync(cancellationToken);

        if (eligible.Count == 0)
        {
            return 0;
        }

        var evidence = await EvidenceForAsync(
            eligible.Select(row => row.OsHash).ToList(),
            cancellationToken);

        var admitted = await context.GateAdmissions
            .Where(row => row.Gate == AfterDownloadGate.Name)
            .Select(row => row.Confidence)
            .ToHashSetAsync(cancellationToken);

        var now = time.GetUtcNow();
        var changed = 0;

        foreach (var arrival in eligible)
        {
            var named = evidence.TryGetValue(
                UserPreviewHash.Normalise(arrival.OsHash) ?? string.Empty,
                out var videos)
                ? videos
                : [];

            changed += arrival.State == ArrivingFileState.Filed
                ? await FlagAsync(arrival, named, now, cancellationToken)
                : await DecideAsync(arrival, named, admitted, now, cancellationToken);
        }

        if (changed > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        return changed;
    }

    /// <summary>
    /// Which Videos the linked, currently shown user previews carrying each of
    /// these hashes name.
    /// </summary>
    /// <remarks>
    /// One query for the whole batch rather than one per file. Both sides are
    /// normalised — the local hash by <see cref="UserPreviewHash"/> on the way
    /// in and the published one on the way into the table — so this is an
    /// index seek and never a case-insensitive scan.
    /// </remarks>
    public async Task<Dictionary<string, IReadOnlyList<Guid>>> EvidenceForAsync(
        IReadOnlyList<string?> hashes,
        CancellationToken cancellationToken)
    {
        var wanted = hashes
            .Select(UserPreviewHash.Normalise)
            .OfType<string>()
            .Distinct()
            .ToList();

        if (wanted.Count == 0)
        {
            return [];
        }

        var rows = await context.UserPreviews
            .AsNoTracking()
            .Where(row => row.Shown && row.VideoPrdbId != null && wanted.Contains(row.OsHash))
            .Select(row => new { row.OsHash, VideoId = row.VideoPrdbId!.Value })
            .Distinct()
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.OsHash, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<Guid>)[.. group.Select(row => row.VideoId).Order()],
                StringComparer.Ordinal);
    }

    /// <summary>
    /// What the evidence made of one Arriving File, without changing anything.
    /// </summary>
    /// <remarks>
    /// The Review Queue's question. It is asked of the same rows the sweep
    /// reads, so a person is shown the reason the sweep had rather than a second
    /// account of it.
    /// </remarks>
    public static PreviewHashOutcome OutcomeOf(
        IReadOnlyList<Guid> named,
        IReadOnlyList<Guid> prdbCandidates,
        Guid? assigned,
        IdentificationRung? matchedBy) => (named.Count, assigned, matchedBy) switch
    {
        (_, not null, IdentificationRung.PreviewHash) => PreviewHashOutcome.Assigned,
        (_, not null, _) => PreviewHashOutcome.NotNeeded,
        (0, _, _) => PreviewHashOutcome.NoEvidence,
        (> 1, _, _) => PreviewHashOutcome.Conflicting,
        _ when prdbCandidates.Count > 0 && !prdbCandidates.Contains(named[0]) =>
            PreviewHashOutcome.OutsideTheCandidates,
        _ => PreviewHashOutcome.Assigned,
    };

    /// <summary>
    /// One Arriving File that has not been filed: assign, take an assignment
    /// back, or leave it where it is.
    /// </summary>
    private async Task<int> DecideAsync(
        ArrivingFileRow arrival,
        IReadOnlyList<Guid> named,
        IReadOnlySet<IdentificationConfidence> admitted,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (arrival.MatchedBy == IdentificationRung.PreviewHash)
        {
            // The withdrawal check. Still exactly one Video and still the same
            // one is the ordinary case and costs nothing.
            if (named.Count == 1 && named[0] == arrival.VideoId)
            {
                return 0;
            }

            logger.LogInformation(
                "The preview-hash evidence for an Arriving File no longer holds; it goes back to the "
                + "Review Queue.");

            arrival.VideoId = null;
            arrival.Confidence = null;
            arrival.MatchedBy = null;
            arrival.State = ArrivingFileState.AwaitingIdentification;
            arrival.Reason = ArrivingFileReason.Unidentified;

            return 1;
        }

        if (named.Count == 0)
        {
            return 0;
        }

        var prdbCandidates = await context.ArrivingFileCandidates
            .Where(row => row.ArrivingFileId == arrival.Id)
            .Select(row => row.VideoId)
            .ToListAsync(cancellationToken);

        var outcome = OutcomeOf(named, prdbCandidates, arrival.VideoId, arrival.MatchedBy);

        if (outcome != PreviewHashOutcome.Assigned)
        {
            // Conflicting, or outside what prdb itself listed. Nothing is
            // chosen, and what the evidence named is recorded as candidates so
            // that the person deciding sees what the tool saw — and so that the
            // Catalogue keeps those Videos (ADR 0033 pins a candidate).
            return await RememberAsync(arrival, named, prdbCandidates, cancellationToken);
        }

        var video = named[0];

        if (!await FilableAsync(video, cancellationToken))
        {
            // The Catalogue does not hold this Video in enough detail to file
            // against — no Site, so the entry directory could not be named.
            // Recording it as a candidate pins it, which is what makes the
            // repair pass read it first; the next pass then makes the
            // assignment. Assigning now and failing at the move would be a
            // routine failing repeatedly for a reason nothing states.
            logger.LogInformation(
                "Preview-hash evidence names a Video the Catalogue has no detail for yet; the "
                + "Arriving File waits for it.");

            return await RememberAsync(arrival, named, prdbCandidates, cancellationToken);
        }

        arrival.VideoId = video;
        arrival.Confidence = IdentificationConfidence.Strong;
        arrival.MatchedBy = IdentificationRung.PreviewHash;

        var mayFile = admitted.Contains(IdentificationConfidence.Strong);

        arrival.State = mayFile ? ArrivingFileState.AwaitingFiling : ArrivingFileState.AwaitingIdentification;
        arrival.Reason = mayFile ? null : ArrivingFileReason.Unidentified;

        context.IdentificationOutcomes.Add(new IdentificationOutcomeRow
        {
            At = now,
            Gate = AfterDownloadGate.Name,
            Outcome = nameof(IdentificationRung.PreviewHash),
        });

        logger.LogInformation(
            "An Arriving File was identified from preview-hash evidence and recorded as {Confidence}.",
            IdentificationConfidence.Strong);

        return 1;
    }

    /// <summary>
    /// One Arriving File that has been filed and whose evidence has gone or
    /// changed.
    /// </summary>
    /// <remarks>
    /// ADR 0062's absolute line: nothing on disk moves. No rename, no delete, no
    /// reassignment, no second Library entry. What happens is a record and a
    /// person — and the record goes when the evidence comes back, because a
    /// restored preview is not something anybody needs to be told about twice.
    /// </remarks>
    private async Task<int> FlagAsync(
        ArrivingFileRow arrival,
        IReadOnlyList<Guid> named,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var holds = named.Count == 1 && named[0] == arrival.VideoId;

        var file = await context.VideoFiles
            .Where(row => row.LibraryEntryVideoId == arrival.VideoId && row.OsHash == arrival.OsHash)
            .Select(row => row.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (file == Guid.Empty)
        {
            // The file has been replaced or deleted since. There is nothing in
            // the Library this evidence is about any more.
            return 0;
        }

        var flag = await context.IdentificationFlags
            .AsTracking()
            .SingleOrDefaultAsync(row => row.VideoFileId == file, cancellationToken);

        if (holds)
        {
            if (flag is null)
            {
                return 0;
            }

            context.IdentificationFlags.Remove(flag);

            return 1;
        }

        var reason = named.Count switch
        {
            0 => "prdb no longer shows the user preview this file's Video was named from.",
            1 => "The user preview this file's Video was named from now names a different Video.",
            _ => "The user previews carrying this file's hash now name more than one Video.",
        };

        var nowNames = named.Count == 1 ? named[0] : (Guid?)null;

        if (flag is not null)
        {
            if (string.Equals(flag.Reason, reason, StringComparison.Ordinal)
                && flag.NowNamesVideoId == nowNames)
            {
                return 0;
            }

            flag.Reason = reason;
            flag.NowNamesVideoId = nowNames;
            flag.At = now;

            return 1;
        }

        logger.LogInformation(
            "A filed Video File was identified from preview-hash evidence that no longer holds. "
            + "Nothing has been moved; it is flagged for a person.");

        context.IdentificationFlags.Add(new IdentificationFlagRow
        {
            VideoFileId = file,
            VideoId = arrival.VideoId!.Value,
            NowNamesVideoId = nowNames,
            Reason = reason,
            At = now,
        });

        return 1;
    }

    /// <summary>
    /// Records what the evidence named as candidates, without choosing one.
    /// </summary>
    private async Task<int> RememberAsync(
        ArrivingFileRow arrival,
        IReadOnlyList<Guid> named,
        IReadOnlyList<Guid> already,
        CancellationToken cancellationToken)
    {
        var added = 0;

        foreach (var video in named.Where(video => !already.Contains(video)))
        {
            await catalogue.VideoAsync(video, title: null, releaseDate: null, cancellationToken);

            context.ArrivingFileCandidates.Add(new ArrivingFileCandidateRow
            {
                ArrivingFileId = arrival.Id,
                VideoId = video,
            });

            added++;
        }

        return added;
    }

    /// <summary>
    /// Whether the Catalogue holds this Video in enough detail for Filing to
    /// name an entry directory from it.
    /// </summary>
    /// <remarks>
    /// The Site, which ADR 0005's layout puts in the path. A Video written by a
    /// list feed has one; a stub written because something referred to it does
    /// not, and Filing refuses such a row rather than guessing a directory.
    /// </remarks>
    private Task<bool> FilableAsync(Guid video, CancellationToken cancellationToken) =>
        context.CatalogueVideos.AnyAsync(
            row => row.PrdbId == video && row.SiteId != null,
            cancellationToken);
}
