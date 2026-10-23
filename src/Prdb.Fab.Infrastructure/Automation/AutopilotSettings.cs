using Microsoft.EntityFrameworkCore;
using Prdb.Fab.Core.Access;
using Prdb.Fab.Core.Acquisition;
using Prdb.Fab.Infrastructure.Filing;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Automation;

public sealed class AutopilotSettings(FabDbContext context, WantedAutopilot autopilot, CollectionPreparation preparation)
{
    public async Task<CollectionAct> StartAsync(bool enabled, IReadOnlyCollection<string> directories, CancellationToken token = default)
    {
        if (await context.AutomationRules.AnyAsync(row => row.SuppliedWanted, token))
            return new(true, "The supplied policy already exists. Its current choice and initial selection were preserved.");
        if (!await context.CollectionPreparations.AnyAsync(token))
        {
            var started = await preparation.StartAsync(directories, token);
            if (!started.Accepted) return started;
        }
        await autopilot.SetEnabledAsync(enabled, token);
        return new(true, enabled
            ? "Recent Wanted autopilot is configured. It waits for setup, Connections and the reviewed collection import."
            : "Recent Wanted autopilot is off. Collection preparation continues without automatic Downloads from this policy.");
    }

    public async Task<CollectionAct> SetEnabledAsync(bool enabled, CancellationToken token = default)
    {
        if (!await context.AutomationRules.AnyAsync(row => row.SuppliedWanted, token))
            return new(false, "Prepare the existing collection before adopting Recent Wanted autopilot.");
        await autopilot.SetEnabledAsync(enabled, token);
        return new(true, enabled ? "Recent Wanted autopilot is on. Its original selection is preserved."
            : "Recent Wanted autopilot is off. Independent custom rules may still permit Downloads.");
    }

    public async Task<AutopilotSelection> SelectionAsync(int page, CancellationToken token = default)
    {
        var account = await context.Installation.Select(row => row.PrdbUserHash).SingleAsync(token);
        var members = context.AutopilotMemberships.Where(row => row.UserHash == account && (row.InitialBatch || row.Admitted));
        page = Core.Paging.Wanted(page);
        var rows = await members.OrderByDescending(row => row.InitialBatch).ThenBy(row => row.VideoId)
            .Skip(Core.Paging.Skip(page, 25)).Take(25).Select(row => new AutopilotSelectedVideo(row.VideoId,
                context.CatalogueVideos.Where(video => video.PrdbId == row.VideoId).Select(video => video.Title).FirstOrDefault(),
                row.InitialBatch,
                context.LibraryEntries.Any(entry => entry.VideoId == row.VideoId) ? "In Library"
                : !context.WantedVideos.Any(wanted => wanted.Video!.PrdbId == row.VideoId) ? "No longer Wanted"
                : context.Downloads.Any(download => download.VideoId == row.VideoId && (download.State == DownloadState.Outstanding || download.State == DownloadState.Completed)) ? "Download in progress"
                : context.ArrivingFileCandidates.Any(candidate => candidate.VideoId == row.VideoId)
                    || context.ArrivingFiles.Any(arrival => arrival.VideoId == row.VideoId && arrival.Reason != null) ? "Needs review"
                : "Waiting for an eligible Release; current date, Confidence and retry limits still apply"))
            .ToListAsync(token);
        return new(page, 25, await members.CountAsync(token), rows);
    }

    public async Task<AutopilotState> ReadAsync(CancellationToken token = default)
    {
        var policy = await context.AutomationRules.SingleOrDefaultAsync(row => row.SuppliedWanted, token);
        var install = await context.Installation.SingleAsync(token);
        var collection = await context.CollectionPreparations.SingleOrDefaultAsync(token);
        var baseline = await context.AutopilotBaselines.AnyAsync(row => row.UserHash == install.PrdbUserHash, token);
        var members = context.AutopilotMemberships.Where(row => row.UserHash == install.PrdbUserHash);
        var admitted = await members.CountAsync(row => row.Admitted, token);
        var initial = await members.CountAsync(row => row.InitialBatch, token);
        var initialHeld = await members.CountAsync(row => row.InitialBatch && context.LibraryEntries.Any(entry => entry.VideoId == row.VideoId), token);
        var newAdmitted = await members.CountAsync(row => row.Admitted && !row.InitialBatch, token);
        var collectionTotal = await context.CollectionFiles.CountAsync(token);
        var collectionFiled = await context.CollectionFiles.CountAsync(file => context.ArrivingFiles.Any(arrival => arrival.Id == file.Id && arrival.State == Core.Filing.ArrivingFileState.Filed), token);
        var collectionLeft = await context.CollectionFiles.CountAsync(file => file.LeftAt != null, token);
        var excluded = await members.CountAsync(row => !row.Admitted, token);
        var custom = await context.AutomationRules.CountAsync(row => !row.SuppliedWanted && row.Enabled, token);
        var outstanding = await context.Downloads.CountAsync(row => !row.OriginIsPerson && row.State == DownloadState.Outstanding, token);
        var phase = "Active";
        var detail = $"{initialHeld} of {initial} initially selected Videos are in the Library; {newAdmitted} later admissions. {outstanding} unfinished automatic Downloads. The initial selection is never refilled.";
        var route = "/settings/automation";
        if (policy is null) { phase = "NotAdopted"; detail = "Use Recent Wanted autopilot to start without writing rules. Your existing automation choices are preserved."; }
        else if (!policy.Enabled) { phase = "Off"; detail = "Recent Wanted autopilot is off. Its saved selection is preserved."; }
        else if (install.OnboardingStep != OnboardingStep.Complete) { phase = "Setup"; detail = "Finish setup before automatic Downloads start."; route = "/"; }
        else if (collection?.Phase != "Ready")
        {
            phase = collection?.Phase ?? "PreparationRequired";
            detail = collection?.Error ?? phase switch
            {
                "Preview" => "Review the existing collection and approve its moves or explicitly leave files in place.",
                "Importing" => "Approved files are being moved into the Library. Automatic Downloads wait until import finishes.",
                "Identifying" => "prdb is identifying the existing files. Automatic Downloads wait for collection preparation.",
                "Validating" => "Approved sources and destinations are being checked before import.",
                _ => "The existing collection is being prepared. Automatic Downloads wait for the inventory and approved import.",
            };
            route = "/settings/library/collection";
        }
        else if (string.IsNullOrWhiteSpace(install.PrdbApiKey)) { phase = "Connections"; detail = "Connect prdb to synchronize Wanted and identify files."; route = "/settings/connections/prdb"; }
        else if (string.IsNullOrWhiteSpace(install.SabnzbdApiKey)) { phase = "Connections"; detail = "Connect SABnzbd before automatic Downloads can start."; route = "/settings/connections/sabnzbd"; }
        else if (!await context.Indexers.AnyAsync(row => row.Enabled, token)) { phase = "Connections"; detail = "Add or enable an Indexer. All enabled Indexers participate automatically."; route = "/settings/connections"; }
        else if (!baseline) { phase = "WantedBaseline"; detail = "Waiting for the complete Wanted list and Video details before selecting the newest 100 eligible missing Videos."; route = "/status"; }
        if (collection is not null && collection.Phase != "Ready")
            detail += $" {collectionTotal} collection files: {collectionFiled} filed, {collectionLeft} explicitly left in place.";
        if (custom > 0) detail += $" {custom} enabled custom rule(s) can independently permit older or additional Videos.";
        return new(policy != null, policy?.Enabled ?? false, phase, detail, route, initial, admitted, excluded, custom,
            install.AutomaticDownloadCap, install.RetryBudget, initialHeld, newAdmitted, collectionTotal, collectionFiled, collectionLeft);
    }
}

public sealed record AutopilotState(bool Present, bool Enabled, string Phase, string Detail, string ActionRoute,
    int InitialAdmitted, int Admitted, int Excluded, int EnabledCustomRules, int DownloadCap, int RetryBudget, int InitialHeld, int NewAdmitted, int CollectionTotal, int CollectionFiled, int CollectionLeft);

public sealed record AutopilotSelection(int Page, int PageSize, int Total, IReadOnlyList<AutopilotSelectedVideo> Videos);
public sealed record AutopilotSelectedVideo(Guid VideoId, string? Title, bool Initial, string State);
