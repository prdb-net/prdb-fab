using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Scheduling;

namespace Prdb.Fab.Infrastructure.Sync;

public static class SyncServiceCollectionExtensions
{
    /// <summary>
    /// ADR 0013's sync: the five change feeds, What's New in both directions,
    /// the two bootstraps that retire, and what they share.
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
        services.AddScoped<CatalogueRows>();

        services.AddScoped<ActorFeed>();
        services.AddScoped<VideoImageFeed>();
        services.AddScoped<WantedVideoFeed>();
        services.AddScoped<FavouriteSiteFeed>();
        services.AddScoped<FavouriteActorFeed>();

        // The one entity with no change feed, and the write every route into
        // the catalogue goes through.
        services.AddScoped<VideoDetails>();
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

        return services;
    }

    private static void Routine<TRoutine>(IServiceCollection services)
        where TRoutine : class, IRoutine
    {
        services.AddScoped<TRoutine>();
        services.AddScoped<IRoutine>(provider => provider.GetRequiredService<TRoutine>());
    }
}
