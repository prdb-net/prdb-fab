using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>
/// <c>GET /video-user-images/changes</c>: what moderation has done to the user
/// previews since this installation last looked.
/// </summary>
/// <remarks>
/// <para>
/// The sixth feed, and the first one that carries a state as well as a row. The
/// other five report what prdb now says about an entity; this one also reports
/// that prdb has stopped saying it — which is the only way a withdrawal is ever
/// heard about, and the reason a Preview gallery is allowed to show a picture at
/// all.
/// </para>
/// <para>
/// <strong>It starts at prdb's clock, not at the beginning.</strong> The
/// argument is <see cref="VideoImageFeed"/>'s unchanged: the feed is global, the
/// population is larger than this Catalogue, and history holds nothing this
/// installation can place — every row it will ever care about arrives from a
/// snapshot it asked for. Unlike that feed, the clock is not taken lazily on the
/// first run: <see cref="UserPreviewReadRoutine"/> takes it before the first
/// snapshot, because the other order leaves a gap.
/// </para>
/// <para>
/// <strong>It discards what it cannot place.</strong> A page names rows for
/// Videos nothing here is interested in, and those are dropped without a row and
/// without a byte. See <see cref="UserPreviewWrites.ChangesAsync"/>.
/// </para>
/// </remarks>
public sealed class UserPreviewFeed(
    FabDbContext context,
    PrdbGateway prdb,
    CatalogueRows catalogue,
    UserPreviewWrites writes) : ChangeFeed(context, prdb, catalogue)
{
    public override Feed Feed => Feed.VideoUserImages;

    public override bool StartsAtTheBeginning => false;

    public override PrdbWork Work => PrdbWork.UserPreviews;

    public override async Task<FeedPage> ReadAsync(
        string apiKey,
        FeedPosition from,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var page = await Prdb.AskAsync(
            apiKey,
            Work,
            (client, token) => client.VideoUserImages.Changes.GetAsync(
                request =>
                {
                    request.QueryParameters.PageSize = pageSize;
                    request.QueryParameters.Since = from.Since;
                    request.QueryParameters.SinceId = from.SinceId;
                },
                token),
            cancellationToken);

        if (page is null)
        {
            return FeedPage.Nothing;
        }

        return new FeedPage(
            await writes.ChangesAsync(page.Items ?? [], cancellationToken),
            page.HasMore ?? false,
            page.NextCursor?.UpdatedAtUtc,
            page.NextCursor?.Id,
            page.ServerTimeUtc);
    }
}

/// <summary>
/// ADR 0061's cadence for the user previews feed: one hour, and only while
/// something is interested.
/// </summary>
/// <remarks>
/// <para>
/// An hour because of what being late costs: a withdrawn picture shown for up
/// to an hour longer, and a restored one missing for up to an hour. Neither is
/// worth one request the Catalogue's own feeds would have spent.
/// </para>
/// <para>
/// <strong>It is a routine with a work set (ADR 0032)</strong>, which is why
/// <see cref="IdleProfile.RequestsAnHour"/> does not move: an installation
/// whose owner has never opened a Preview and filed nothing makes no request
/// here at all, and the idle profile is what the schedule costs with nothing to
/// do.
/// </para>
/// <para>
/// The expiry runs here rather than in a routine of its own, because it is the
/// same work set read from the other end — what this feed follows is exactly
/// what the expiry stops following.
/// </para>
/// </remarks>
public sealed class UserPreviewFeedRoutine(
    UserPreviewFeed feed,
    UserPreviews previews,
    FeedCursors cursors,
    FabDbContext context,
    ILogger<UserPreviewFeedRoutine> logger)
    : ChangeFeedRoutine(feed, cursors, context, logger), ISpendsPrdbBudget, IWorkSetPaced
{
    public const string RoutineName = "prdb.user-previews.changes";

    public override string Name => RoutineName;

    public override TimeSpan Cadence => UserPreviewContract.FeedCadence;

    public PrdbWork Spends => Source.Work;

    protected override Task<bool> AnythingToFollowAsync(CancellationToken cancellationToken) =>
        previews.AnythingFollowedAsync(cancellationToken);

    protected override async Task ReachedAsync(FeedPosition position, CancellationToken cancellationToken)
    {
        var expired = await previews.ExpireAsync(cancellationToken);

        if (expired > 0)
        {
            logger.LogInformation(
                "{Count} user preview interest(s) and row(s) expired and were dropped.",
                expired);
        }
    }
}
