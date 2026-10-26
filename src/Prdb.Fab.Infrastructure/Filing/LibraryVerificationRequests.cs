using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Filing;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Filing;

/// <summary>Explicitly invalidates disposable verification evidence, never held content.</summary>
public sealed class LibraryVerificationRequests(FabDbContext context)
{
    public async Task<LibraryVerificationProgress> ReadAsync(CancellationToken cancellationToken)
    {
        var total = await context.VideoFiles.CountAsync(cancellationToken);
        var answers = await context.LibraryVerifications.AsNoTracking()
            .GroupBy(row => row.Outcome)
            .Select(group => new { Outcome = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var checkedCount = answers.Sum(row => row.Count);
        return new(total, Math.Max(0, total - checkedCount),
            answers.Where(row => row.Outcome != LibraryVerification.Confirmed).Sum(row => row.Count));
    }

    public async Task<LibraryVerificationProgress> RequestAsync(bool onlyUnconfirmed, CancellationToken cancellationToken)
    {
        // One atomic statement. An already pending file remains pending;
        // repeated requests cannot duplicate work. The ordinary Bulk routine
        // resumes this durable work set after a restart.
        await context.LibraryVerifications
            .Where(row => !onlyUnconfirmed || row.Outcome != LibraryVerification.Confirmed)
            .ExecuteDeleteAsync(cancellationToken);
        return await ReadAsync(cancellationToken);
    }
}

public sealed record LibraryVerificationProgress(int Total, int Pending, int Unconfirmed);
