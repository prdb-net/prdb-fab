using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using Prdb.Fab.Core.Access;
using Prdb.Fab.Core.Scheduling;

namespace Prdb.Fab.Infrastructure.Persistence;

/// <summary>
/// What is built so far of ADR 0033's twenty-four tables: ADR 0014's two, the
/// installation and its sessions, and the scaffolding row the one routine works
/// through. The rest arrive with the features that need them.
/// </summary>
public sealed class FabDbContext(DbContextOptions<FabDbContext> options) : DbContext(options)
{
    public DbSet<RoutineRow> Routines => Set<RoutineRow>();

    public DbSet<RoutineRunRow> RoutineRuns => Set<RoutineRunRow>();

    public DbSet<SkeletonItemRow> SkeletonItems => Set<SkeletonItemRow>();

    /// <summary>The one row. See <see cref="InstallationRow"/>.</summary>
    public DbSet<InstallationRow> Installation => Set<InstallationRow>();

    public DbSet<SessionRow> Sessions => Set<SessionRow>();

    /// <summary>
    /// ADR 0033's exported half of an indexer. See <see cref="IndexerRow"/> for
    /// where the other half goes.
    /// </summary>
    public DbSet<IndexerRow> Indexers => Set<IndexerRow>();

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

        builder.Entity<InstallationRow>(installation =>
        {
            installation.ToTable("installation", table => table.HasCheckConstraint(
                "CK_installation_one_row",
                $"\"Id\" = {InstallationRow.TheOnlyRow}"));

            installation.HasKey(row => row.Id);

            // Never generated: the key is the constant, so a second insert is
            // refused by the check constraint rather than quietly numbered 2.
            installation.Property(row => row.Id).ValueGeneratedNever();

            installation.Property(row => row.OnboardingStep).HasConversion<string>();

            // The row exists from the first migration rather than being created
            // on demand, so nothing anywhere has to ask whether it is there.
            installation.HasData(new InstallationRow { Id = InstallationRow.TheOnlyRow });
        });

        builder.Entity<SessionRow>(session =>
        {
            session.ToTable("session");
            session.HasKey(row => row.Id);

            // Every authenticated request is this lookup.
            session.HasIndex(row => row.TokenHash).IsUnique();

            // ADR 0010: expiry is a property of the row, and sweeping the dead
            // ones is one delete over this.
            session.HasIndex(row => row.ExpiresAt);
        });

        builder.Entity<IndexerRow>(indexer =>
        {
            indexer.ToTable("indexer");
            indexer.HasKey(row => row.Id);

            indexer.Property(row => row.Name).IsRequired();
            indexer.Property(row => row.Url).IsRequired();
            indexer.Property(row => row.ApiKey).IsRequired();
            indexer.Property(row => row.LastVerdict).HasConversion<string>();

            // The same indexer added twice is a mistake rather than a
            // configuration: two rows would walk it twice, spend ADR 0024's
            // budget twice, and give the same package two release identities.
            indexer.HasIndex(row => row.Url).IsUnique();
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
