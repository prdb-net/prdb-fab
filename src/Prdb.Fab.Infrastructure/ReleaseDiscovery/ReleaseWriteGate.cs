namespace Prdb.Fab.Infrastructure.ReleaseDiscovery;

/// <summary>
/// Serialises the read-then-write part of Release cache updates across scopes.
/// SQLite is a single writer, and every Release source can discover the same
/// unique Indexer identity concurrently.
/// </summary>
public sealed class ReleaseWriteGate
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async ValueTask<IAsyncDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        return new Lease(gate);
    }

    private sealed class Lease(SemaphoreSlim gate) : IAsyncDisposable
    {
        private int disposed;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0) gate.Release();
            return ValueTask.CompletedTask;
        }
    }
}
