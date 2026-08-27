using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Access;
using Prdb.Fab.Infrastructure.Access;
using Prdb.Fab.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Access;

/// <summary>
/// The only place ADR 0010's marker moves, and the only place a skip is
/// recorded.
/// </summary>
public sealed class OnboardingTests
{
    [Fact]
    public async Task A_step_that_was_answered_moves_the_marker_on()
    {
        await using var database = await TestDatabase.CreateAsync();

        await StandingAt(database, OnboardingStep.PrdbKey, row => row.PrdbApiKey = "a key");

        var act = await Acting(database, onboarding =>
            onboarding.TakeAsync(OnboardingStep.PrdbKey, TestContext.Current.CancellationToken));

        Assert.Equal(OnboardingOutcome.Taken, act.Outcome);
        Assert.Equal(OnboardingStep.Sabnzbd, act.NextStep);
        Assert.Equal(OnboardingStep.Sabnzbd, await MarkerIn(database));
    }

    /// <summary>
    /// ADR 0010: the loop stands still until the mandatory steps are done. What
    /// makes them mandatory is this — the marker moves on what is stored rather
    /// than on the browser having asked.
    /// </summary>
    [Fact]
    public async Task A_step_with_nothing_stored_cannot_be_taken()
    {
        await using var database = await TestDatabase.CreateAsync();

        await StandingAt(database, OnboardingStep.LibraryRoot);

        var act = await Acting(database, onboarding =>
            onboarding.TakeAsync(OnboardingStep.LibraryRoot, TestContext.Current.CancellationToken));

        Assert.Equal(OnboardingOutcome.NotConfigured, act.Outcome);
        Assert.Equal(OnboardingStep.LibraryRoot, await MarkerIn(database));
    }

    /// <summary>
    /// A second tab, or a window left open across a restart. It is told where
    /// the path actually is rather than moving whichever step it found.
    /// </summary>
    [Fact]
    public async Task A_step_the_path_has_left_behind_moves_nothing()
    {
        await using var database = await TestDatabase.CreateAsync();

        await StandingAt(database, OnboardingStep.Indexers, row => row.PrdbApiKey = "a key");

        var act = await Acting(database, onboarding =>
            onboarding.TakeAsync(OnboardingStep.PrdbKey, TestContext.Current.CancellationToken));

        Assert.Equal(OnboardingOutcome.NotTheCurrentStep, act.Outcome);
        Assert.Equal(OnboardingStep.Indexers, act.NextStep);
        Assert.Equal(OnboardingStep.Indexers, await MarkerIn(database));
    }

    [Fact]
    public async Task Skipping_the_downloader_records_the_gap_and_moves_on()
    {
        await using var database = await TestDatabase.CreateAsync();

        await StandingAt(database, OnboardingStep.Sabnzbd);

        var act = await Acting(database, onboarding =>
            onboarding.SkipAsync(OnboardingStep.Sabnzbd, TestContext.Current.CancellationToken));

        var installation = await Reading(database);

        Assert.Equal(OnboardingOutcome.Skipped, act.Outcome);
        Assert.Equal(OnboardingStep.Indexers, act.NextStep);
        Assert.True(installation.SabnzbdSkipped);
        Assert.False(installation.IndexersSkipped);
    }

    [Fact]
    public async Task Skipping_the_indexers_records_its_own_gap()
    {
        await using var database = await TestDatabase.CreateAsync();

        await StandingAt(database, OnboardingStep.Indexers);

        var act = await Acting(database, onboarding =>
            onboarding.SkipAsync(OnboardingStep.Indexers, TestContext.Current.CancellationToken));

        var installation = await Reading(database);

        Assert.Equal(OnboardingOutcome.Skipped, act.Outcome);
        Assert.Equal(OnboardingStep.LibraryRoot, act.NextStep);
        Assert.True(installation.IndexersSkipped);
    }

    /// <summary>
    /// ADR 0010 names two skippable steps and gives each an argument. The other
    /// two have none, and a caller asking anyway is refused rather than obeyed.
    /// </summary>
    [Theory]
    [InlineData(OnboardingStep.PrdbKey)]
    [InlineData(OnboardingStep.LibraryRoot)]
    public async Task A_mandatory_step_cannot_be_skipped(OnboardingStep step)
    {
        await using var database = await TestDatabase.CreateAsync();

        await StandingAt(database, step);

        var act = await Acting(database, onboarding =>
            onboarding.SkipAsync(step, TestContext.Current.CancellationToken));

        Assert.Equal(OnboardingOutcome.NotSkippable, act.Outcome);
        Assert.Equal(step, await MarkerIn(database));
    }

    /// <summary>
    /// The end of the path is the end of it. Nothing re-enters the wizard, and
    /// an act that arrives afterwards is told so.
    /// </summary>
    [Fact]
    public async Task Nothing_moves_once_the_path_is_finished()
    {
        await using var database = await TestDatabase.CreateAsync();

        await StandingAt(database, OnboardingStep.Complete, row => row.LibraryRoot = "/library");

        var taken = await Acting(database, onboarding =>
            onboarding.TakeAsync(OnboardingStep.LibraryRoot, TestContext.Current.CancellationToken));
        var skipped = await Acting(database, onboarding =>
            onboarding.SkipAsync(OnboardingStep.Sabnzbd, TestContext.Current.CancellationToken));

        Assert.Equal(OnboardingOutcome.NotTheCurrentStep, taken.Outcome);
        Assert.Equal(OnboardingOutcome.NotTheCurrentStep, skipped.Outcome);
        Assert.Equal(OnboardingStep.Complete, await MarkerIn(database));
    }

    /// <summary>
    /// An installation that is past the password and standing on
    /// <paramref name="step"/>, holding whatever the caller says it holds.
    /// </summary>
    /// <remarks>
    /// Its own scope, like everything else here: ADR 0039 keeps contexts short
    /// and untracked, so a scope per act is what the application does and a
    /// shared one would be a test arrangement nothing else has.
    /// </remarks>
    private static async Task StandingAt(
        TestDatabase database,
        OnboardingStep step,
        Action<InstallationRow>? holding = null)
    {
        await using var scope = database.Scope();

        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        var installation = await context.Installation.SingleAsync(TestContext.Current.CancellationToken);

        installation.PasswordHash = "hashed";
        installation.OnboardingStep = step;
        holding?.Invoke(installation);

        context.Installation.Update(installation);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<OnboardingAct> Acting(
        TestDatabase database,
        Func<Onboarding, Task<OnboardingAct>> act)
    {
        await using var scope = database.Scope();

        return await act(scope.ServiceProvider.GetRequiredService<Onboarding>());
    }

    private static async Task<InstallationRow> Reading(TestDatabase database)
    {
        await using var scope = database.Scope();

        return await scope.ServiceProvider.GetRequiredService<Installations>()
            .ReadAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<OnboardingStep> MarkerIn(TestDatabase database)
    {
        await using var scope = database.Scope();

        return await scope.ServiceProvider.GetRequiredService<Installations>()
            .NextStepAsync(TestContext.Current.CancellationToken);
    }
}
