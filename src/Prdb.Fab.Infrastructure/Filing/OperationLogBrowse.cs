using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Filing;

/// <summary>The read-only Operation Log. No control-flow code depends on it.</summary>
public sealed class OperationLogBrowse(FabDbContext context)
{
    public const int APage = 50;

    public async Task<OperationLogPage> ReadAsync(
        string? act,
        string? search,
        Guid? videoId,
        int page,
        CancellationToken cancellationToken = default)
    {
        var wanted = Paging.Wanted(page);
        var query = context.OperationLogEntries.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(act)) query = query.Where(row => row.Act == act);
        if (videoId is not null) query = query.Where(row => row.VideoId == videoId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = SearchPattern.Containing(search);
            query = query.Where(row =>
                (row.PathBefore != null && EF.Functions.Like(row.PathBefore, pattern, SearchPattern.Escape))
                || (row.PathAfter != null && EF.Functions.Like(row.PathAfter, pattern, SearchPattern.Escape))
                || (row.DisplacedPath != null && EF.Functions.Like(row.DisplacedPath, pattern, SearchPattern.Escape)));
        }

        var total = await query.CountAsync(cancellationToken);
        var entries = await query
            .OrderByDescending(row => row.At)
            .ThenByDescending(row => row.Id)
            .Skip(Paging.Skip(wanted, APage))
            .Take(APage)
            .Select(row => new OperationLogEntry(
                row.Id,
                row.Act,
                row.VideoId,
                row.DownloadId,
                row.PathBefore,
                row.PathAfter,
                row.DisplacedPath,
                row.LeftoverNamesJson,
                row.Actor,
                row.Reason,
                row.At))
            .ToListAsync(cancellationToken);

        return new OperationLogPage(entries, wanted, APage, total);
    }
}

public sealed record OperationLogEntry(
    Guid Id,
    string Act,
    Guid? VideoId,
    Guid? DownloadId,
    string? PathBefore,
    string? PathAfter,
    string? DisplacedPath,
    string? LeftoverNamesJson,
    string Actor,
    string Reason,
    DateTimeOffset At);

public sealed record OperationLogPage(
    IReadOnlyList<OperationLogEntry> Entries,
    int Page,
    int PageSize,
    int Total);
