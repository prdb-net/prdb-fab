namespace Prdb.Fab.Infrastructure.Filing;

/// <summary>Serializes Library content changes with the person's delete actions.</summary>
public sealed class ContentMutationLock : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public async Task<IDisposable> EnterAsync(CancellationToken token)
    {
        await gate.WaitAsync(token);
        return new Lease(gate);
    }
    private sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
    public void Dispose() => gate.Dispose();
}
