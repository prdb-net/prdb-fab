using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Skeleton;

namespace Prdb.Fab.Infrastructure.Scheduling;

public static class SchedulingServiceCollectionExtensions
{
    /// <summary>
    /// The schedule: the row store, the registrar that gives every routine a
    /// row, and the routines themselves.
    /// </summary>
    /// <remarks>
    /// A routine is registered as <see cref="IRoutine"/> and found by its name.
    /// That is the answer ADR 0038 left to the skeleton for how a row finds its
    /// code: the name binds, once, here; the target the row carries is an
    /// argument rather than a second registration, so twenty indexer rows share
    /// one implementation and a row created at runtime needs nothing added.
    /// </remarks>
    public static IServiceCollection AddFabScheduling(this IServiceCollection services)
    {
        services.AddScoped<IRoutineStore, RoutineStore>();
        services.AddScoped<RoutineRegistrar>();
        services.AddScoped<RunLog>();

        // The skeleton's one routine. Scaffolding, and the only thing the
        // schedule has to turn until a feature arrives.
        services.AddScoped<IRoutine, SkeletonSweepRoutine>();
        services.AddScoped<SkeletonItems>();

        return services;
    }

    /// <summary>
    /// Creates the rows for the routines this build knows about. Runs after the
    /// migrations and before the lanes.
    /// </summary>
    public static async Task PrepareFabScheduleAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();

        await scope.ServiceProvider
            .GetRequiredService<RoutineRegistrar>()
            .EnsureRowsExistAsync(cancellationToken);
    }
}
