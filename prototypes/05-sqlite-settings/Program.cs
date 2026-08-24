// PROTOTYPE — throwaway. Answers ticket 05: how SQLite is opened and written.
// Nothing here is production code. The scratch databases are named "PROTOTYPE-wipe-me-*.db".
//
//   dotnet run                 -- everything, in order
//   dotnet run -- seed         -- rebuild the reference database only
//
// Sizes come from the decisions: 300 000 releases (three indexers at ADR 0015's
// 100 000 ceiling), 500 needles (ADR 0025), six indexed counts per tick (ADR 0032),
// a status page read every five seconds (ADR 0018).

using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Text;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

const int Releases = 300_000;
const int Needles = 500;
const int CatalogueVideos = 20_000;
const int Downloads = 10_000;

var dir = AppContext.BaseDirectory;
var reference = Path.Combine(dir, "PROTOTYPE-wipe-me-reference.db");

var mode = args.Length > 0 ? args[0] : "all";

if (mode is "seed" or "all" && !File.Exists(reference))
{
    Seed(reference);
}

if (mode == "seed")
{
    return;
}

Console.WriteLine();
Console.WriteLine($"SQLite {SqliteVersion()} via Microsoft.Data.Sqlite, {Releases:N0} releases, "
    + $"{new FileInfo(reference).Length / 1024 / 1024} MiB on disk");
Console.WriteLine();

IdleTickCost();
ReaderLatencyUnderWrite();
LongTransaction();
TwoWriters();
PragmaPersistence();
EfCoreBehaviour();
BackwardsPassCost();
WhoSerialises();
WalGrowth();

Console.WriteLine();
Console.WriteLine("Done. Scratch databases left in " + dir);

// ─────────────────────────────────────────────────────────────────────────────
// A. What a tick costs when nothing is happening.
// ─────────────────────────────────────────────────────────────────────────────
void IdleTickCost()
{
    Header("A. Idle cost of one tick (ADR 0032's six indexed counts)");

    foreach (var (journal, sync) in new[] { ("delete", "full"), ("wal", "normal") })
    {
        var db = Fresh($"idle-{journal}");
        using var c = Open(db, journal, sync, busyMs: 5000);

        // warm
        for (var i = 0; i < 20; i++) SixCounts(c);

        var samples = new List<double>();
        for (var i = 0; i < 200; i++)
        {
            var t = Stopwatch.GetTimestamp();
            SixCounts(c);
            samples.Add(Ms(t));
        }

        Row($"journal={journal} synchronous={sync}", samples);
    }

    Console.WriteLine();
    Console.WriteLine("  Read as: the whole standing idle cost of the work-set family, per tick.");
}

// ─────────────────────────────────────────────────────────────────────────────
// B. Does a reader wait for a writer? The status page against the sync lane.
// ─────────────────────────────────────────────────────────────────────────────
void ReaderLatencyUnderWrite()
{
    Header("B. Status page latency while the sync lane writes and the bulk lane scans");

    foreach (var (journal, sync) in new[] { ("delete", "full"), ("wal", "full"), ("wal", "normal") })
    {
        var db = Fresh($"contend-{journal}-{sync}");
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var token = stop.Token;
        var busyWrites = 0;
        var pages = 0;

        // Writer: indexer walk pages, 100 rows a batch, as fast as a walk ever goes.
        var writer = Task.Run(() =>
        {
            using var c = Open(db, journal, sync, busyMs: 5000);
            var n = 0;
            while (!token.IsCancellationRequested)
            {
                try { WalkPage(c, ref n); }
                catch (SqliteException e) when (e.SqliteErrorCode == 5) { Interlocked.Increment(ref busyWrites); }
                Thread.Sleep(50);
            }
        }, token);

        // Bulk lane: ADR 0025's one pass over the cache with every needle at once.
        var scanner = Task.Run(() =>
        {
            using var c = Open(db, journal, sync, busyMs: 5000);
            while (!token.IsCancellationRequested)
            {
                try { BackwardsPass(c); } catch (SqliteException) { }
            }
        }, token);

        // Reader: the status page. Polled far faster than five seconds so there
        // are enough samples; per-read latency does not depend on the interval.
        var samples = new List<double>();
        using (var c = Open(db, journal, sync, busyMs: 5000))
        {
            while (!token.IsCancellationRequested)
            {
                var t = Stopwatch.GetTimestamp();
                try { StatusPage(c); pages++; } catch (SqliteException) { }
                samples.Add(Ms(t));
                Thread.Sleep(25);
            }
        }

        try { Task.WaitAll([writer, scanner], TimeSpan.FromSeconds(5)); } catch { }

        Row($"journal={journal} synchronous={sync}", samples, extra: $"busy writes: {busyWrites}");
    }

    Console.WriteLine();
    Console.WriteLine("  Read as: what ADR 0018's five-second poll costs a person while the loop runs.");
}

// ─────────────────────────────────────────────────────────────────────────────
// C. The rule ADR 0004 states, priced: a transaction held open across a call.
// ─────────────────────────────────────────────────────────────────────────────
void LongTransaction()
{
    Header("C. A write transaction held open for 3 s (the thing ADR 0004 forbids)");

    foreach (var (journal, sync) in new[] { ("delete", "full"), ("wal", "normal") })
    {
        var db = Fresh($"longtx-{journal}");
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        var token = stop.Token;

        var holder = Task.Run(() =>
        {
            using var c = Open(db, journal, sync, busyMs: 5000);
            using var tx = c.BeginTransaction();
            var n = 900_000;
            WalkPage(c, ref n, tx);
            Thread.Sleep(3000);          // stands in for an HTTP call inside the transaction
            tx.Commit();
        }, token);

        Thread.Sleep(300);

        var reads = new List<double>();
        var writes = new List<double>();
        var busy = 0;
        using (var c = Open(db, journal, sync, busyMs: 5000))
        {
            var deadline = DateTime.UtcNow.AddSeconds(3);
            var n = 500_000;
            while (DateTime.UtcNow < deadline)
            {
                var t = Stopwatch.GetTimestamp();
                StatusPage(c);
                reads.Add(Ms(t));

                t = Stopwatch.GetTimestamp();
                try { WalkPage(c, ref n); writes.Add(Ms(t)); }
                catch (SqliteException e) when (e.SqliteErrorCode == 5) { busy++; writes.Add(Ms(t)); }
                Thread.Sleep(25);
            }
        }

        try { holder.Wait(TimeSpan.FromSeconds(10)); } catch { }

        Row($"journal={journal}  reader", reads);
        Row($"journal={journal}  other writer", writes, extra: $"busy: {busy}");
    }

    Console.WriteLine();
    Console.WriteLine("  Read as: the cost of breaking the no-transaction-across-a-call rule,");
    Console.WriteLine("  separately for somebody reading a page and for another lane writing.");
}

// ─────────────────────────────────────────────────────────────────────────────
// D. Two lanes writing at once. Does busy_timeout do the serialising?
// ─────────────────────────────────────────────────────────────────────────────
void TwoWriters()
{
    Header("D. Two lanes writing at once, by busy_timeout");

    foreach (var busyMs in new[] { 0, 100, 5000 })
    {
        var db = Fresh($"writers-{busyMs}");
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var token = stop.Token;
        var busy = 0;
        var done = 0;
        var latencies = new List<double>();
        var gate = new Lock();

        var tasks = Enumerable.Range(0, 2).Select(lane => Task.Run(() =>
        {
            using var c = Open(db, "wal", "normal", busyMs);
            var n = lane * 1_000_000;
            while (!token.IsCancellationRequested)
            {
                var t = Stopwatch.GetTimestamp();
                try
                {
                    WalkPage(c, ref n);
                    Interlocked.Increment(ref done);
                }
                catch (SqliteException e) when (e.SqliteErrorCode is 5 or 6)
                {
                    Interlocked.Increment(ref busy);
                }
                lock (gate) latencies.Add(Ms(t));
            }
        }, token)).ToArray();

        try { Task.WaitAll(tasks, TimeSpan.FromSeconds(15)); } catch { }

        Row($"busy_timeout={busyMs} ms", latencies, extra: $"committed: {done}, SQLITE_BUSY: {busy}");
    }

    Console.WriteLine();
    Console.WriteLine("  Read as: whether 'one writer at a time' needs enforcing in the application,");
    Console.WriteLine("  and what a lock timeout would hand ADR 0014's backoff as a routine failure.");
}

// ─────────────────────────────────────────────────────────────────────────────
// E. Which pragmas survive a new connection, and which have to be set every time.
// ─────────────────────────────────────────────────────────────────────────────
void PragmaPersistence()
{
    Header("E. What a pragma set on one connection means for the next");

    var db = Fresh("pragma");

    using (var c = Open(db, "wal", "normal", busyMs: 4321)) { }

    using var plain = new SqliteConnection($"Data Source={db}");
    plain.Open();
    Console.WriteLine($"  a connection opened with no pragmas at all sees:");
    Console.WriteLine($"    journal_mode  = {Scalar(plain, "PRAGMA journal_mode")}   (stored in the file)");
    Console.WriteLine($"    synchronous   = {Scalar(plain, "PRAGMA synchronous")}   (2 = FULL, per connection)");
    Console.WriteLine($"    busy_timeout  = {Scalar(plain, "PRAGMA busy_timeout")}   (per connection)");
    Console.WriteLine($"    foreign_keys  = {Scalar(plain, "PRAGMA foreign_keys")}   (per connection)");

    using var pooled1 = new SqliteConnection($"Data Source={db};Pooling=True");
    pooled1.Open();
    Exec(pooled1, "PRAGMA busy_timeout = 9999");
    pooled1.Close();

    using var pooled2 = new SqliteConnection($"Data Source={db};Pooling=True");
    pooled2.Open();
    var leaked = Scalar(pooled2, "PRAGMA busy_timeout");
    Console.WriteLine($"  pooled: set busy_timeout=9999, closed, reopened -> {leaked}");
    Console.WriteLine($"    {(leaked!.ToString() == "9999" ? "the pragma SURVIVED: a pooled connection is reused as it was left" : "reset on return to the pool")}");

    using var unpooled = new SqliteConnection($"Data Source={db};Pooling=False");
    unpooled.Open();
    Console.WriteLine($"  unpooled, fresh connection: busy_timeout = {Scalar(unpooled, "PRAGMA busy_timeout")}");
    Console.WriteLine();
    Console.WriteLine("  Read as: which pragmas an application has to set, and when.");
}

// ─────────────────────────────────────────────────────────────────────────────
// F. What EF Core does with all of it.
// ─────────────────────────────────────────────────────────────────────────────
void EfCoreBehaviour()
{
    Header("F. EF Core on top");

    var db = Fresh("efcore");

    var cs = new SqliteConnectionStringBuilder
    {
        DataSource = db,
        Pooling = true,
        DefaultTimeout = 30,
    }.ToString();

    using (var ctx = new Ctx(cs))
    {
        Console.WriteLine($"  EF Core {typeof(DbContext).Assembly.GetName().Version}");
        Console.WriteLine($"  connection string: {cs}");

        var conn = (SqliteConnection)ctx.Database.GetDbConnection();
        conn.Open();
        Console.WriteLine($"  what EF Core's own connection reports before anything is configured:");
        Console.WriteLine($"    journal_mode = {Scalar(conn, "PRAGMA journal_mode")}");
        Console.WriteLine($"    synchronous  = {Scalar(conn, "PRAGMA synchronous")}");
        Console.WriteLine($"    busy_timeout = {Scalar(conn, "PRAGMA busy_timeout")}  <- 'Default Timeout' is NOT this");
        Console.WriteLine($"    foreign_keys = {Scalar(conn, "PRAGMA foreign_keys")}");
        conn.Close();
    }

    // The same six counts, through EF Core, tracking on and off.
    using (var ctx = new Ctx(cs))
    {
        // warm: the first query of a kind pays for compiling it
        for (var i = 0; i < 20; i++) _ = ctx.Releases.Count(r => r.State == 0);
        _ = ctx.Releases.AsNoTracking().Where(r => r.State == 2).Take(200).ToList();
        _ = ctx.Releases.Where(r => r.State == 2).Take(200).ToList();
        ctx.ChangeTracker.Clear();

        var t = Stopwatch.GetTimestamp();
        for (var i = 0; i < 100; i++) _ = ctx.Releases.Count(r => r.State == 0);
        Console.WriteLine($"  one count through EF Core, warm: {Ms(t) / 100:F3} ms  (raw ADO.NET: ~0.05 ms)");

        var samples = new List<double>();
        for (var i = 0; i < 20; i++)
        {
            var t2 = Stopwatch.GetTimestamp();
            _ = ctx.Releases.AsNoTracking().Where(r => r.State == 2).Take(200).ToList();
            samples.Add(Ms(t2));
        }
        Row("  200 rows, AsNoTracking", samples);

        samples.Clear();
        for (var i = 0; i < 20; i++)
        {
            ctx.ChangeTracker.Clear();
            var t2 = Stopwatch.GetTimestamp();
            _ = ctx.Releases.Where(r => r.State == 2).Take(200).ToList();
            samples.Add(Ms(t2));
        }
        Row("  200 rows, tracked", samples);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// G. What ADR 0025's one pass actually costs on this schema.
// ─────────────────────────────────────────────────────────────────────────────
void BackwardsPassCost()
{
    Header("G. ADR 0025's backwards pass, on its own (that ADR measured 1.7 s at 300k)");

    foreach (var (journal, sync) in new[] { ("delete", "full"), ("wal", "normal") })
    {
        var db = Fresh($"pass-{journal}");
        using var c = Open(db, journal, sync, busyMs: 5000);
        BackwardsPass(c);   // warm the page cache

        var samples = new List<double>();
        for (var i = 0; i < 5; i++)
        {
            var t = Stopwatch.GetTimestamp();
            BackwardsPass(c);
            samples.Add(Ms(t));
        }
        Row($"journal={journal}", samples);
    }

    Console.WriteLine();
    Console.WriteLine("  Read as: how long the bulk lane holds a read open, which is what section B");
    Console.WriteLine("  makes the status page contend with.");
}

// ─────────────────────────────────────────────────────────────────────────────
// H. busy_timeout or Default Timeout — which one actually does the waiting?
// ─────────────────────────────────────────────────────────────────────────────
void WhoSerialises()
{
    Header("H. Which knob serialises two writers: busy_timeout, or Default Timeout");

    foreach (var (busyMs, cmdTimeout, label) in new[]
    {
        (0, 30, "busy_timeout=0   Default Timeout=30"),
        (0, 1,  "busy_timeout=0   Default Timeout=1"),
        (5000, 1, "busy_timeout=5000 Default Timeout=1"),
    })
    {
        var db = Fresh($"knob-{busyMs}-{cmdTimeout}");
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        var token = stop.Token;
        var busy = 0;
        var done = 0;

        var tasks = Enumerable.Range(0, 2).Select(lane => Task.Run(() =>
        {
            var c = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = db,
                Pooling = true,
                DefaultTimeout = cmdTimeout,
            }.ToString());
            c.Open();
            Exec(c, "PRAGMA journal_mode = wal");
            Exec(c, "PRAGMA synchronous = normal");
            Exec(c, $"PRAGMA busy_timeout = {busyMs}");

            var n = lane * 2_000_000;
            while (!token.IsCancellationRequested)
            {
                try { LongWrite(c, ref n); Interlocked.Increment(ref done); }
                catch (SqliteException e) when (e.SqliteErrorCode is 5 or 6) { Interlocked.Increment(ref busy); }
            }
            c.Dispose();
        }, token)).ToArray();

        try { Task.WaitAll(tasks, TimeSpan.FromSeconds(12)); } catch { }
        Console.WriteLine($"  {label,-40}  committed: {done,6}   SQLITE_BUSY: {busy}");
    }

    Console.WriteLine();
    Console.WriteLine("  Read as: whether a routine can be handed a lock error that ADR 0014's");
    Console.WriteLine("  backoff would then count as the routine's own failure.");
}

// ─────────────────────────────────────────────────────────────────────────────
// I. What the WAL does on the data volume while a reader is always present.
// ─────────────────────────────────────────────────────────────────────────────
void WalGrowth()
{
    Header("I. WAL size under continuous writing with a status page always open");

    var db = Fresh("wal-growth");
    using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    var token = stop.Token;

    var writer = Task.Run(() =>
    {
        using var c = Open(db, "wal", "normal", busyMs: 5000);
        var n = 3_000_000;
        while (!token.IsCancellationRequested) { WalkPage(c, ref n); Thread.Sleep(20); }
    }, token);

    var reader = Task.Run(() =>
    {
        using var c = Open(db, "wal", "normal", busyMs: 5000);
        while (!token.IsCancellationRequested) { StatusPage(c); Thread.Sleep(25); }
    }, token);

    var peak = 0L;
    while (!token.IsCancellationRequested)
    {
        var wal = new FileInfo(db + "-wal");
        if (wal.Exists) peak = Math.Max(peak, wal.Length);
        Thread.Sleep(200);
    }
    try { Task.WaitAll([writer, reader], TimeSpan.FromSeconds(5)); } catch { }

    var final = new FileInfo(db + "-wal");
    Console.WriteLine($"  peak WAL during 15 s of writing with a reader present: {peak / 1024:N0} KiB");
    Console.WriteLine($"  WAL after the writers stop:                            {(final.Exists ? final.Length / 1024 : 0):N0} KiB");

    using (var c = Open(db, "wal", "normal", busyMs: 5000))
    {
        Exec(c, "PRAGMA wal_checkpoint(TRUNCATE)");
    }
    final.Refresh();
    Console.WriteLine($"  after an explicit TRUNCATE checkpoint:                 {(final.Exists ? final.Length / 1024 : 0):N0} KiB");
    Console.WriteLine();
    Console.WriteLine("  Read as: whether the WAL is something ADR 0034's data-volume figures must carry.");
}

// A write big enough that two of them genuinely collide.
void LongWrite(SqliteConnection c, ref int seed)
{
    using var tx = c.BeginTransaction();
    using var cmd = c.CreateCommand();
    cmd.Transaction = tx;
    cmd.CommandText = "UPDATE release SET state = state WHERE id > $a AND id < $b";
    cmd.Parameters.AddWithValue("$a", seed % 200_000);
    cmd.Parameters.AddWithValue("$b", (seed % 200_000) + 20_000);
    cmd.ExecuteNonQuery();
    seed += 7919;
    tx.Commit();
}

// ─────────────────────────────────────────────────────────────────────────────
// Workloads
// ─────────────────────────────────────────────────────────────────────────────

void SixCounts(SqliteConnection c)
{
    Scalar(c, "SELECT COUNT(*) FROM release WHERE state = 0");                              // screening
    Scalar(c, "SELECT COUNT(*) FROM needle WHERE searched = 0");                            // backwards search
    Scalar(c, "SELECT COUNT(*) FROM release WHERE state = 2");                              // release identification
    Scalar(c, "SELECT COUNT(*) FROM arriving_file WHERE state = 0");                        // identify arriving files
    Scalar(c, "SELECT COUNT(*) FROM arriving_file WHERE state = 1");                        // file
    Scalar(c, "SELECT COUNT(*) FROM catalogue_video WHERE pinned = 1 AND image_cached = 0"); // artwork
}

void StatusPage(SqliteConnection c)
{
    SixCounts(c);
    Scalar(c, "SELECT COUNT(*) FROM arriving_file WHERE state = 4");                 // review queue, in every header
    Scalar(c, "SELECT COUNT(*) FROM download WHERE state IN (0,1)");                 // outstanding
    Query(c, "SELECT name, lane, due_at, last_success_at, consecutive_failures FROM routine");
    Query(c, "SELECT id, path, intended_path FROM arriving_file WHERE state = 2");   // what is being filed, and since when
}

void WalkPage(SqliteConnection c, ref int seed, SqliteTransaction? tx = null)
{
    var own = tx is null;
    using var t = own ? c.BeginTransaction() : null;
    using var cmd = c.CreateCommand();
    cmd.Transaction = (SqliteTransaction?)t ?? tx;
    cmd.CommandText = """
        INSERT INTO release (indexer_id, guid, title, normalised_title, posted_at, size, download_url, state)
        VALUES ($i, $g, $t, $n, $p, $s, $u, 0)
        """;
    var pi = cmd.Parameters.Add("$i", SqliteType.Integer);
    var pg = cmd.Parameters.Add("$g", SqliteType.Text);
    var pt = cmd.Parameters.Add("$t", SqliteType.Text);
    var pn = cmd.Parameters.Add("$n", SqliteType.Text);
    var pp = cmd.Parameters.Add("$p", SqliteType.Integer);
    var ps = cmd.Parameters.Add("$s", SqliteType.Integer);
    var pu = cmd.Parameters.Add("$u", SqliteType.Text);

    for (var k = 0; k < 100; k++)
    {
        var title = SceneTitle(seed++);
        pi.Value = seed % 3;
        pg.Value = $"guid-{seed}";
        pt.Value = title;
        pn.Value = Normalise(title);
        pp.Value = 1_700_000_000 + seed;
        ps.Value = 4_000_000_000L + seed;
        pu.Value = $"https://indexer.example/getnzb?id={seed}&apikey=deadbeef";
        cmd.ExecuteNonQuery();
    }

    t?.Commit();
}

// ADR 0025: every needle accumulated since the last run, in ONE pass over the cache.
void BackwardsPass(SqliteConnection c)
{
    using var cmd = c.CreateCommand();
    cmd.CommandText = """
        SELECT COUNT(*)
        FROM release r
        JOIN needle n ON r.normalised_title LIKE '%' || n.normalised || '%'
        WHERE n.searched = 0
        """;
    cmd.ExecuteScalar();
}

// ─────────────────────────────────────────────────────────────────────────────
// Seed
// ─────────────────────────────────────────────────────────────────────────────
void Seed(string path)
{
    Console.WriteLine($"Seeding {Releases:N0} releases into {Path.GetFileName(path)} — once, then reused.");
    File.Delete(path);

    using var c = new SqliteConnection($"Data Source={path}");
    c.Open();
    Exec(c, "PRAGMA journal_mode = wal");
    Exec(c, "PRAGMA synchronous = normal");

    Exec(c, """
        CREATE TABLE release (
            id INTEGER PRIMARY KEY,
            indexer_id INTEGER NOT NULL,
            guid TEXT NOT NULL,
            title TEXT NOT NULL,
            normalised_title TEXT NOT NULL,
            posted_at INTEGER NOT NULL,
            size INTEGER NOT NULL,
            download_url TEXT NOT NULL,
            state INTEGER NOT NULL,
            video_id INTEGER NULL
        );
        CREATE INDEX ix_release_state ON release(state);
        CREATE INDEX ix_release_video ON release(video_id);

        CREATE TABLE arriving_file (
            id INTEGER PRIMARY KEY,
            path TEXT NOT NULL,
            state INTEGER NOT NULL,
            last_attempt_at INTEGER NULL,
            intended_path TEXT NULL
        );
        CREATE INDEX ix_arriving_state ON arriving_file(state);

        CREATE TABLE needle (
            id INTEGER PRIMARY KEY,
            normalised TEXT NOT NULL,
            searched INTEGER NOT NULL
        );
        CREATE INDEX ix_needle_searched ON needle(searched);

        CREATE TABLE catalogue_video (
            id INTEGER PRIMARY KEY,
            pinned INTEGER NOT NULL,
            image_cached INTEGER NOT NULL
        );
        CREATE INDEX ix_catalogue_pinned ON catalogue_video(pinned, image_cached);

        CREATE TABLE download (
            id INTEGER PRIMARY KEY,
            state INTEGER NOT NULL,
            stage_log TEXT
        );
        CREATE INDEX ix_download_state ON download(state);

        CREATE TABLE routine (
            id INTEGER PRIMARY KEY,
            name TEXT NOT NULL,
            lane INTEGER NOT NULL,
            due_at INTEGER NOT NULL,
            last_success_at INTEGER NULL,
            consecutive_failures INTEGER NOT NULL
        );
        """);

    var sw = Stopwatch.StartNew();
    using (var tx = c.BeginTransaction())
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO release (indexer_id, guid, title, normalised_title, posted_at, size, download_url, state)
            VALUES ($i, $g, $t, $n, $p, $s, $u, $st)
            """;
        var pi = cmd.Parameters.Add("$i", SqliteType.Integer);
        var pg = cmd.Parameters.Add("$g", SqliteType.Text);
        var pt = cmd.Parameters.Add("$t", SqliteType.Text);
        var pn = cmd.Parameters.Add("$n", SqliteType.Text);
        var pp = cmd.Parameters.Add("$p", SqliteType.Integer);
        var ps = cmd.Parameters.Add("$s", SqliteType.Integer);
        var pu = cmd.Parameters.Add("$u", SqliteType.Text);
        var pst = cmd.Parameters.Add("$st", SqliteType.Integer);

        for (var i = 0; i < Releases; i++)
        {
            var title = SceneTitle(i);
            pi.Value = i % 3;
            pg.Value = $"guid-{i}";
            pt.Value = title;
            pn.Value = Normalise(title);
            pp.Value = 1_700_000_000 + i;
            ps.Value = 4_000_000_000L + i;
            pu.Value = $"https://indexer.example/getnzb?id={i}&apikey=deadbeef";
            // Most of the cache is screened and unidentified; ADR 0018 draws that share.
            pst.Value = i % 100 < 3 ? 0 : i % 100 < 5 ? 2 : 1;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    using (var tx = c.BeginTransaction())
    {
        for (var i = 0; i < Needles; i++)
        {
            Exec(c, $"INSERT INTO needle (normalised, searched) VALUES ('{Site(i % 40)} {i}', {(i < 120 ? 0 : 1)})", tx);
        }
        for (var i = 0; i < CatalogueVideos; i++)
        {
            Exec(c, $"INSERT INTO catalogue_video (pinned, image_cached) VALUES ({(i % 4 == 0 ? 1 : 0)}, {(i % 7 == 0 ? 0 : 1)})", tx);
        }
        for (var i = 0; i < Downloads; i++)
        {
            Exec(c, $"INSERT INTO download (state, stage_log) VALUES ({i % 5}, 'queued\nfetching\nunpacking')", tx);
        }
        for (var i = 0; i < 400; i++)
        {
            Exec(c, $"INSERT INTO arriving_file (path, state) VALUES ('/media/downloads/x{i}/file.mkv', {i % 5})", tx);
        }
        for (var i = 0; i < 22; i++)
        {
            Exec(c, $"INSERT INTO routine (name, lane, due_at, last_success_at, consecutive_failures) "
                + $"VALUES ('routine-{i}', {i % 4}, {1_700_000_000 + i}, {1_700_000_000}, 0)", tx);
        }
        tx.Commit();
    }

    Exec(c, "PRAGMA wal_checkpoint(TRUNCATE)");
    Exec(c, "VACUUM");
    Console.WriteLine($"Seeded in {sw.Elapsed.TotalSeconds:F1} s.");
}

// ─────────────────────────────────────────────────────────────────────────────
// Plumbing
// ─────────────────────────────────────────────────────────────────────────────

string Fresh(string name)
{
    var path = Path.Combine(dir, $"PROTOTYPE-wipe-me-{name}.db");
    foreach (var suffix in new[] { "", "-wal", "-shm" })
    {
        if (File.Exists(path + suffix)) File.Delete(path + suffix);
    }
    File.Copy(reference, path);

    // Every copy starts from the same journal mode, whatever the reference was left in.
    using var c = new SqliteConnection($"Data Source={path}");
    c.Open();
    Exec(c, "PRAGMA journal_mode = delete");
    return path;
}

SqliteConnection Open(string path, string journal, string synchronous, int busyMs)
{
    var c = new SqliteConnection(new SqliteConnectionStringBuilder
    {
        DataSource = path,
        Pooling = true,
    }.ToString());
    c.Open();
    Exec(c, $"PRAGMA journal_mode = {journal}");
    Exec(c, $"PRAGMA synchronous = {synchronous}");
    Exec(c, $"PRAGMA busy_timeout = {busyMs}");
    Exec(c, "PRAGMA foreign_keys = on");
    return c;
}

static void Exec(SqliteConnection c, string sql, SqliteTransaction? tx = null)
{
    using var cmd = c.CreateCommand();
    cmd.CommandText = sql;
    cmd.Transaction = tx;
    cmd.ExecuteNonQuery();
}

static object? Scalar(SqliteConnection c, string sql)
{
    using var cmd = c.CreateCommand();
    cmd.CommandText = sql;
    return cmd.ExecuteScalar();
}

static void Query(SqliteConnection c, string sql)
{
    using var cmd = c.CreateCommand();
    cmd.CommandText = sql;
    using var r = cmd.ExecuteReader();
    while (r.Read()) { }
}

static double Ms(long from) => Stopwatch.GetElapsedTime(from).TotalMilliseconds;

static string SqliteVersion()
{
    using var c = new SqliteConnection("Data Source=:memory:");
    c.Open();
    return (string)Scalar(c, "SELECT sqlite_version()")!;
}

static string Site(int i) => new[]
{
    "BrazzersExxtra", "TeamSkeetAllstars", "SisLovesMe", "RealityKings", "BangBros",
    "MomIsHorny", "DigitalPlayground", "EvilAngel", "NubileFilms", "TushyRaw",
}[i % 10] + (i / 10 == 0 ? "" : $"Vol{i / 10}");

static string SceneTitle(int i) => string.Create(CultureInfo.InvariantCulture,
    $"{Site(i % 40)}.24.{(i % 12) + 1:00}.{(i % 28) + 1:00}.Firstname.Lastname{i % 900}.Some.Scene.Title.Here.XXX.1080p.MP4-WRB[rarbg]");

static string Normalise(string title)
{
    var sb = new StringBuilder(title.Length);
    var lastWasSeparator = false;
    foreach (var ch in title.AsSpan())
    {
        if (char.IsLetterOrDigit(ch))
        {
            sb.Append(char.ToLowerInvariant(ch));
            lastWasSeparator = false;
        }
        else if (!lastWasSeparator)
        {
            sb.Append(' ');
            lastWasSeparator = true;
        }
    }
    return sb.ToString().Trim();
}

static void Header(string title)
{
    Console.WriteLine();
    Console.WriteLine(new string('─', 78));
    Console.WriteLine(title);
    Console.WriteLine(new string('─', 78));
}

static void Row(string label, List<double> samples, string? extra = null)
{
    if (samples.Count == 0) { Console.WriteLine($"  {label,-34}  (no samples)"); return; }
    var s = samples.Order().ToArray();
    string P(double q) => $"{s[Math.Min(s.Length - 1, (int)(s.Length * q))]:F2}";
    Console.WriteLine($"  {label,-34}  n={s.Length,-5} p50={P(0.5),8} ms  p95={P(0.95),8} ms  max={s[^1],8:F2} ms"
        + (extra is null ? "" : $"   {extra}"));
}

// ─────────────────────────────────────────────────────────────────────────────
// EF Core model, minimal
// ─────────────────────────────────────────────────────────────────────────────
internal sealed class Ctx(string connectionString) : DbContext
{
    public DbSet<Release> Releases => Set<Release>();

    protected override void OnConfiguring(DbContextOptionsBuilder options) =>
        options.UseSqlite(connectionString).EnableSensitiveDataLogging(false);

    protected override void OnModelCreating(ModelBuilder model)
    {
        var r = model.Entity<Release>();
        r.ToTable("release");
        r.HasKey(x => x.Id);
        r.Property(x => x.Id).HasColumnName("id");
        r.Property(x => x.State).HasColumnName("state");
        r.Property(x => x.Title).HasColumnName("title");
        r.Property(x => x.NormalisedTitle).HasColumnName("normalised_title");
        r.Property(x => x.Guid).HasColumnName("guid");
        r.Property(x => x.IndexerId).HasColumnName("indexer_id");
        r.Property(x => x.PostedAt).HasColumnName("posted_at");
        r.Property(x => x.Size).HasColumnName("size");
        r.Property(x => x.DownloadUrl).HasColumnName("download_url");
    }
}

internal sealed class Release
{
    public long Id { get; set; }
    public int IndexerId { get; set; }
    public string Guid { get; set; } = "";
    public string Title { get; set; } = "";
    public string NormalisedTitle { get; set; } = "";
    public long PostedAt { get; set; }
    public long Size { get; set; }
    public string DownloadUrl { get; set; } = "";
    public int State { get; set; }
}
