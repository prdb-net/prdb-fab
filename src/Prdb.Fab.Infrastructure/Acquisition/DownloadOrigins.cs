using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Acquisition;

/// <summary>Resolves the durable person-or-rules explanation of Downloads.</summary>
public sealed class DownloadOrigins(FabDbContext context)
{
    public async Task<IReadOnlyDictionary<Guid, DownloadOriginView>> ForAsync(
        IReadOnlyCollection<Guid> downloadIds,
        CancellationToken cancellationToken = default)
    {
        var ids = downloadIds.Distinct().ToArray();
        if (ids.Length == 0) return new Dictionary<Guid, DownloadOriginView>();

        var downloads = await context.Downloads
            .Where(row => ids.Contains(row.Id))
            .Select(row => new { row.Id, row.OriginIsPerson })
            .ToListAsync(cancellationToken);
        var byDownload = await context.DownloadOriginRules
            .Where(row => ids.Contains(row.DownloadId))
            .OrderBy(row => row.RuleName)
            .ThenBy(row => row.Id)
            .Select(row => new { row.DownloadId, row.AutomationRuleId, row.RuleName })
            .ToListAsync(cancellationToken);

        return downloads.ToDictionary(
            download => download.Id,
            download => new DownloadOriginView(
                download.OriginIsPerson ? DownloadOrigin.Person : DownloadOrigin.Automation,
                [.. byDownload.Where(member => member.DownloadId == download.Id)
                    .Select(member => new DownloadOriginRuleView(member.AutomationRuleId, member.RuleName))]));
    }
}

public enum DownloadOrigin { Person, Automation }
public sealed record DownloadOriginView(DownloadOrigin Kind, IReadOnlyList<DownloadOriginRuleView> Rules);
public sealed record DownloadOriginRuleView(Guid? RuleId, string Name);
