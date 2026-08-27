using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>
/// ADR 0014's cadence for the actors feed: six hours.
/// </summary>
/// <remarks>
/// The slowest of the five, and it can be: after the drain beside it has
/// finished, what this catches is a corpus of names being corrected, which is
/// nothing anything downstream waits on.
/// </remarks>
public sealed class ActorFeedRoutine(
    ActorFeed feed,
    FeedCursors cursors,
    FabDbContext context,
    ILogger<ActorFeedRoutine> logger) : ChangeFeedRoutine(feed, cursors, context, logger), ISpendsPrdbBudget
{
    public const string RoutineName = "prdb.actors";

    public override string Name => RoutineName;

    public override TimeSpan Cadence => TimeSpan.FromHours(6);

    public PrdbWork Spends => Source.Work;
}

/// <summary>
/// ADR 0014's cadence for the video images feed: thirty minutes.
/// </summary>
/// <remarks>
/// Twice as often as the actors feed and half as often as What's New, which is
/// where the artwork of a video read half an hour ago catches up with it.
/// </remarks>
public sealed class VideoImageFeedRoutine(
    VideoImageFeed feed,
    FeedCursors cursors,
    FabDbContext context,
    ILogger<VideoImageFeedRoutine> logger) : ChangeFeedRoutine(feed, cursors, context, logger), ISpendsPrdbBudget
{
    public const string RoutineName = "prdb.video-images";

    public override string Name => RoutineName;

    public override TimeSpan Cadence => TimeSpan.FromMinutes(30);

    public PrdbWork Spends => Source.Work;
}

/// <summary>
/// ADR 0014's cadence for the wanted list: one hour.
/// </summary>
/// <remarks>
/// The one of the three user feeds a person notices, because ADR 0010 ends
/// onboarding on the list this fills. An hour is what ADR 0014 fixed and not a
/// setting: ADR 0020 admits a control only where the tool cannot know the
/// answer, and this number follows from a budget the tool reads for itself.
/// </remarks>
public sealed class WantedVideoFeedRoutine(
    WantedVideoFeed feed,
    FeedCursors cursors,
    FabDbContext context,
    ILogger<WantedVideoFeedRoutine> logger) : ChangeFeedRoutine(feed, cursors, context, logger), ISpendsPrdbBudget
{
    public const string RoutineName = "prdb.wanted-videos";

    public override string Name => RoutineName;

    public override TimeSpan Cadence => TimeSpan.FromHours(1);

    public PrdbWork Spends => Source.Work;
}

/// <summary>ADR 0014's cadence for the favourite sites feed: one hour.</summary>
public sealed class FavouriteSiteFeedRoutine(
    FavouriteSiteFeed feed,
    FeedCursors cursors,
    FabDbContext context,
    ILogger<FavouriteSiteFeedRoutine> logger) : ChangeFeedRoutine(feed, cursors, context, logger), ISpendsPrdbBudget
{
    public const string RoutineName = "prdb.favourite-sites";

    public override string Name => RoutineName;

    public override TimeSpan Cadence => TimeSpan.FromHours(1);

    public PrdbWork Spends => Source.Work;
}

/// <summary>ADR 0014's cadence for the favourite actors feed: one hour.</summary>
public sealed class FavouriteActorFeedRoutine(
    FavouriteActorFeed feed,
    FeedCursors cursors,
    FabDbContext context,
    ILogger<FavouriteActorFeedRoutine> logger) : ChangeFeedRoutine(feed, cursors, context, logger), ISpendsPrdbBudget
{
    public const string RoutineName = "prdb.favourite-actors";

    public override string Name => RoutineName;

    public override TimeSpan Cadence => TimeSpan.FromHours(1);

    public PrdbWork Spends => Source.Work;
}
