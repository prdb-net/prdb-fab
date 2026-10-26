using Microsoft.Extensions.DependencyInjection;

namespace Prdb.Fab.Infrastructure.Status;

/// <summary>Shares one local read across overlapping polling clients for at most five seconds.</summary>
public sealed class StatusSnapshot(IServiceScopeFactory scopes, TimeProvider time) : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private StatusState? snapshot;
    private DateTimeOffset expires;

    public async Task<StatusState> ReadAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (snapshot is not null && time.GetUtcNow() < expires) return snapshot;
            await using var scope = scopes.CreateAsyncScope();
            var fresh = await scope.ServiceProvider.GetRequiredService<StatusService>().ReadAsync(cancellationToken);
            snapshot = fresh;
            expires = time.GetUtcNow().AddSeconds(5);
            return fresh;
        }
        finally { gate.Release(); }
    }

    public async Task InvalidateAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try { snapshot = null; }
        finally { gate.Release(); }
    }

    public void Dispose() => gate.Dispose();
}
