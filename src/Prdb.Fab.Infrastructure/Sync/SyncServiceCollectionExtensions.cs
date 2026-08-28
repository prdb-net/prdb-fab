using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Prdb.Fab.Core.Scheduling;

namespace Prdb.Fab.Infrastructure.Sync;

public static class SyncServiceCollectionExtensions
{
    /// <summary>
    /// ADR 0013's sync: the five change feeds, What's New in both directions,
    /// the site list, the repair pass, the two bootstraps that retire, and what
    /// they share — including ADR 0033's pinning, which is a query the repair
    /// pass and eviction both ask rather than a column either of them writes.
    /// </summary>
    /// <remarks>
    /// Registered apart from <c>AddFabScheduling</c> rather than inside it,
    /// because that method is the schedule — the row store, the registrar, the
    /// runner — and this is a feature that has routines. Every routine here is
    /// found by its name like any other, which is what ADR 0038 asked of a row
    /// that binds to code.
    /// </remarks>
    public static IServiceCollection AddFabSync(this IServiceCollection services)
    {
        services.AddScoped<FeedCursors>();
        services.TryAddScoped<CatalogueRows>();

        // ADR 0033's pinning, as the query it is. One source today and one
        // clause each for the five tables that arrive later, which is what
        // keeps adding one from being a rewrite.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ICataloguePin, WantedVideoPin>());
        services.TryAddScoped<CataloguePins>();
        services.AddScoped<CatalogueEviction>();

        // ADR 0030's cache: the files, the fetch that fills them and the sweep
        // that bounds them. The display path takes ArtworkCache directly, which
        // is the half of the cache no routine turns.
        services.TryAddScoped<ArtworkStore>();
        services.AddScoped<ArtworkCache>();
        services.AddScoped<ArtworkEviction>();

        // What the browse surfaces read. Registered here because what it reads
        // is what the routines above write, and there is nothing else to it —
        // it holds no state and reaches nothing.
        services.AddScoped<CatalogueBrowse>();

        services.AddScoped<ActorFeed>();
        services.AddScoped<VideoImageFeed>();
        services.AddScoped<WantedVideoFeed>();
        services.AddScoped<FavouriteSiteFeed>();
        services.AddScoped<FavouriteActorFeed>();

        // The one entity with no change feed, and the write every route into
        // the catalogue goes through.
        services.TryAddScoped<VideoDetails>();
        services.AddScoped<WhatsNew>();

        // The concrete type as well as the interface, so that a routine can be
        // asked for by name in a test without going through the whole set. The
        // schedule only ever sees IRoutine.
        Routine<ActorFeedRoutine>(services);
        Routine<ActorDrainRoutine>(services);
        Routine<VideoImageFeedRoutine>(services);
        Routine<WantedVideoFeedRoutine>(services);
        Routine<FavouriteSiteFeedRoutine>(services);
        Routine<FavouriteActorFeedRoutine>(services);
        Routine<WhatsNewRoutine>(services);
        Routine<WhatsNewBackfillRoutine>(services);
        Routine<SiteListRoutine>(services);
        Routine<CatalogueRepairRoutine>(services);
        Routine<ArtworkRoutine>(services);

        return services;
    }

    private static void Routine<TRoutine>(IServiceCollection services)
        where TRoutine : class, IRoutine
    {
        services.AddScoped<TRoutine>();
        services.AddScoped<IRoutine>(provider => provider.GetRequiredService<TRoutine>());
    }
}
