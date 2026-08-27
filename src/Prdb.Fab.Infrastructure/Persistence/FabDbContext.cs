using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using Prdb.Fab.Core.Access;
using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Core.Scheduling;

namespace Prdb.Fab.Infrastructure.Persistence;

/// <summary>
/// What is built so far of ADR 0033's twenty-four tables: ADR 0014's two, the
/// installation and its sessions, the catalogue half of the schema, and the
/// scaffolding row the one routine works through. The rest arrive with the
/// features that need them.
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
    /// ADR 0013's catalogue: the part of prdb this installation has looked at.
    /// None of it is exported — every row refetches itself by running — and
    /// nothing in it identifies anything.
    /// </summary>
    public DbSet<CatalogueVideoRow> CatalogueVideos => Set<CatalogueVideoRow>();

    public DbSet<CatalogueVideoPreNameRow> CatalogueVideoPreNames => Set<CatalogueVideoPreNameRow>();

    public DbSet<CatalogueVideoActorRow> CatalogueVideoActors => Set<CatalogueVideoActorRow>();

    public DbSet<CatalogueSiteRow> CatalogueSites => Set<CatalogueSiteRow>();

    public DbSet<CatalogueActorRow> CatalogueActors => Set<CatalogueActorRow>();

    public DbSet<CatalogueImageRow> CatalogueImages => Set<CatalogueImageRow>();

    /// <summary>One row per feed. See <see cref="FeedCursorRow"/>.</summary>
    public DbSet<FeedCursorRow> FeedCursors => Set<FeedCursorRow>();

    /// <summary>The user's half, and the three tables a key change drops.</summary>
    public DbSet<WantedVideoRow> WantedVideos => Set<WantedVideoRow>();

    public DbSet<FavouriteSiteRow> FavouriteSites => Set<FavouriteSiteRow>();

    public DbSet<FavouriteActorRow> FavouriteActors => Set<FavouriteActorRow>();

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
            routine.Declares(AccountClass.AccountFree);

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
            run.Declares(AccountClass.AccountFree);

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

            // The prdb key is on this row and the row is not dropped with it:
            // what a key from another account takes is the three tables below
            // and three of the cursors, never the installation itself.
            installation.Declares(AccountClass.AccountFree);

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

            // A session is the browser's, not the prdb account's.
            session.Declares(AccountClass.AccountFree);

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
            indexer.Declares(AccountClass.AccountFree);

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
            item.Declares(AccountClass.AccountFree);
            item.Property(row => row.Label).IsRequired();

            // The work set: rows the sweep has not been past. Indexed because
            // asking "is there anything to do" is what the lane does all day.
            item.HasIndex(row => row.SweptAt);
        });

        // The catalogue. Integer surrogates throughout, because ADR 0033 spends
        // a UUIDv7 only where a row crosses the export boundary and none of
        // these do; prdb's own ids are the natural keys, unique because that is
        // what an upsert writes against and what everything outside the cache
        // names a row by.
        builder.Entity<CatalogueVideoRow>(video =>
        {
            video.ToTable("catalogue_video");
            video.HasKey(row => row.Id);
            video.Declares(AccountClass.AccountFree);

            video.HasIndex(row => row.PrdbId).IsUnique();

            video.Property(row => row.Title).IsRequired();
            video.Property(row => row.NormalisedTitle).IsRequired();

            // ADR 0032's work set for the backwards search, and deliberately
            // not indexed here: its reader arrives with the indexer cache, and
            // ADR 0033 asks for the index where the COUNT is, which is with the
            // routine that makes it every tick. The column exists now because a
            // row written before it would otherwise sit unsearched with no
            // error and no Gap.
            video.Property(row => row.TitleSearchedBackwards).HasDefaultValue(false);

            video.HasOne(row => row.Site)
                .WithMany()
                .HasForeignKey(row => row.SiteId)
                // ADR 0013 never deletes a site row, so this is what should
                // happen if something ever tried: refuse, rather than take the
                // videos with it.
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<CatalogueVideoPreNameRow>(preName =>
        {
            preName.ToTable("catalogue_video_pre_name");
            preName.HasKey(row => row.Id);
            preName.Declares(AccountClass.AccountFree);

            preName.Property(row => row.PreName).IsRequired();
            preName.Property(row => row.NormalisedPreName).IsRequired();
            preName.Property(row => row.SearchedBackwards).HasDefaultValue(false);

            // The natural key. A video's detail read brings its pre-names whole
            // every time, so without this an upsert would append the same title
            // again on every repair pass — and each copy would be a needle of
            // its own for ADR 0025's pass.
            preName.HasIndex(row => new { row.VideoId, row.PreName }).IsUnique();

            preName.HasOne(row => row.Video)
                .WithMany()
                .HasForeignKey(row => row.VideoId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<CatalogueVideoActorRow>(credit =>
        {
            credit.ToTable("catalogue_video_actor");
            credit.HasKey(row => new { row.VideoId, row.ActorId });
            credit.Declares(AccountClass.AccountFree);

            credit.HasOne(row => row.Video)
                .WithMany()
                .HasForeignKey(row => row.VideoId)
                .OnDelete(DeleteBehavior.Cascade);

            credit.HasOne(row => row.Actor)
                .WithMany()
                .HasForeignKey(row => row.ActorId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<CatalogueSiteRow>(site =>
        {
            site.ToTable("catalogue_site");
            site.HasKey(row => row.Id);
            site.Declares(AccountClass.AccountFree);

            site.HasIndex(row => row.PrdbId).IsUnique();

            site.Property(row => row.Title).IsRequired();
            site.Property(row => row.StillOffered).HasDefaultValue(true);
        });

        builder.Entity<CatalogueActorRow>(actor =>
        {
            actor.ToTable("catalogue_actor");
            actor.HasKey(row => row.Id);
            actor.Declares(AccountClass.AccountFree);

            actor.HasIndex(row => row.PrdbId).IsUnique();

            actor.Property(row => row.Name).IsRequired();
        });

        builder.Entity<CatalogueImageRow>(image =>
        {
            image.ToTable("catalogue_image");
            image.HasKey(row => row.Id);
            image.Declares(AccountClass.AccountFree);

            image.HasIndex(row => row.PrdbId).IsUnique();

            image.Property(row => row.Url).IsRequired();
            image.Property(row => row.Position).HasDefaultValue(0);
            image.Property(row => row.Cached).HasDefaultValue(false);
            image.Property(row => row.FoundDead).HasDefaultValue(false);

            // ADR 0027's choice, as the order it is made in: the first entry of
            // the video's images[] carrying a URL. Indexed with the video
            // because that is how it is always asked — one video, its lowest
            // position — and the id is in the key so that the tie prdb breaks
            // by image id is broken the same way here.
            image.HasIndex(row => new { row.VideoId, row.Position, row.PrdbId });

            // ADR 0033 asks for this one filtered to pinned videos, which no
            // index can express — pinning is a query (ADR 0033) and SQLite has
            // no index over another table. What it can hold is the other half
            // of the same clause, and that is the selective half: an installed
            // cache has almost nothing uncached in it, so the routine's work
            // set is found in the index rather than by reading the table.
            image.HasIndex(row => row.Cached).HasFilter("\"Cached\" = 0");

            // ADR 0030's eviction order: least recently served first, over the
            // unpinned part only. One of the two indexes ADR 0033 asks for by
            // name.
            image.HasIndex(row => row.LastServedAt);

            image.HasOne(row => row.Video)
                .WithMany()
                .HasForeignKey(row => row.VideoId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<FeedCursorRow>(cursor =>
        {
            cursor.ToTable("feed_cursor");

            // The feed is the key: a second row for one feed would be two
            // positions over one stream with nothing to say which is behind.
            cursor.HasKey(row => row.Feed);
            cursor.Property(row => row.Feed).HasConversion<string>();

            // The one table whose account class is a property of the row. What
            // makes that a declaration rather than a shrug is Feeds.AccountClassOf,
            // which answers for every feed there is.
            cursor.Declares(AccountClass.PerRow);
        });

        // The user's half. Each of these is keyed by what it points at, which
        // is both the shape of the thing — one row per wanted video, per
        // followed site, per followed actor — and the index ADR 0033's pinning
        // anti-join reads.
        builder.Entity<WantedVideoRow>(wanted =>
        {
            wanted.ToTable("wanted_video");
            wanted.HasKey(row => row.VideoId);
            wanted.Declares(AccountClass.AccountScoped);

            wanted.HasOne(row => row.Video)
                .WithMany()
                .HasForeignKey(row => row.VideoId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<FavouriteSiteRow>(favourite =>
        {
            favourite.ToTable("favourite_site");
            favourite.HasKey(row => row.SiteId);
            favourite.Declares(AccountClass.AccountScoped);

            favourite.HasOne(row => row.Site)
                .WithMany()
                .HasForeignKey(row => row.SiteId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<FavouriteActorRow>(favourite =>
        {
            favourite.ToTable("favourite_actor");
            favourite.HasKey(row => row.ActorId);
            favourite.Declares(AccountClass.AccountScoped);

            favourite.HasOne(row => row.Actor)
                .WithMany()
                .HasForeignKey(row => row.ActorId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
