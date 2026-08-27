using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using Prdb.Fab.Core.Scheduling;

namespace Prdb.Fab.Infrastructure.Persistence;

/// <summary>
/// The skeleton's schema: ADR 0014's two tables, plus the scaffolding row the
/// one routine works through. Not ADR 0033's twenty-four — that arrives with
/// the features that need it.
/// </summary>
public sealed class FabDbContext(DbContextOptions<FabDbContext> options) : DbContext(options)
{
    public DbSet<RoutineRow> Routines => Set<RoutineRow>();

    public DbSet<RoutineRunRow> RoutineRuns => Set<RoutineRunRow>();

    public DbSet<SkeletonItemRow> SkeletonItems => Set<SkeletonItemRow>();

    /// <summary>
    /// Stored as plain UTC rather than as an offset. SQLite has no date type,
    /// and the provider refuses a <see cref="DateTimeOffset"/> on either side of
    /// a comparison or in an ORDER BY — which is exactly what the schedule does
    /// with these: <em>what is due</em> is one indexed query per tick, not a
    /// table read into memory. Everything here is UTC anyway, so the offset
    /// carried no information. <c>prdb-ordeno</c> reached the same conclusion
    /// for the same reason.
    /// </summary>
    private static readonly ValueConverter<DateTimeOffset, DateTime> UtcTimestamp = new(
        value => value.UtcDateTime,
        stored => new DateTimeOffset(stored, TimeSpan.Zero));

    private static readonly ValueConverter<DateTimeOffset?, DateTime?> OptionalUtcTimestamp = new(
        value => value!.Value.UtcDateTime,
        stored => new DateTimeOffset(stored!.Value, TimeSpan.Zero));

    protected override void OnModelCreating(ModelBuilder builder)
    {
        foreach (var property in builder.Model.GetEntityTypes().SelectMany(entity => entity.GetProperties()))
        {
            if (property.ClrType == typeof(DateTimeOffset))
            {
                property.SetValueConverter(UtcTimestamp);
            }
            else if (property.ClrType == typeof(DateTimeOffset?))
            {
                property.SetValueConverter(OptionalUtcTimestamp);
            }
        }

        builder.Entity<RoutineRow>(routine =>
        {
            routine.ToTable("routine");
            routine.HasKey(row => row.Id);

            // A routine is one row per (name, target): twenty indexer rows share
            // one implementation, and nothing may create a second row for the
            // same pair, because then two lanes would both think it was theirs.
            routine.HasIndex(row => new { row.Name, row.Target }).IsUnique();

            // What every tick asks. ADR 0038 has the lane read this on every
            // tick rather than cache it, so it is worth an index from the start.
            routine.HasIndex(row => new { row.Lane, row.DueAt });

            routine.Property(row => row.Name).IsRequired();
            routine.Property(row => row.Lane).HasConversion<string>();
        });

        builder.Entity<RoutineRunRow>(run =>
        {
            run.ToTable("routine_run");
            run.HasKey(row => row.Id);

            run.HasOne(row => row.Routine)
                .WithMany()
                .HasForeignKey(row => row.RoutineId)
                .OnDelete(DeleteBehavior.Cascade);

            // Read newest-first per routine, and trimmed to fifty the same way.
            run.HasIndex(row => new { row.RoutineId, row.StartedAt });

            run.Property(row => row.Outcome).HasConversion<string>();
        });

        builder.Entity<SkeletonItemRow>(item =>
        {
            item.ToTable("skeleton_item");
            item.HasKey(row => row.Id);
            item.Property(row => row.Label).IsRequired();

            // The work set: rows the sweep has not been past. Indexed because
            // asking "is there anything to do" is what the lane does all day.
            item.HasIndex(row => row.SweptAt);
        });
    }
}
