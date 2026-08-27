using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.Access;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Access;

/// <summary>
/// The path between ADR 0010's steps: the only place the onboarding marker
/// moves, and the only place a skip is recorded.
/// </summary>
/// <remarks>
/// <para>
/// The four connection forms know nothing about any of this. Each of them
/// stores what it checked and stops there, which is what ADR 0020 needs when
/// the same four forms turn up behind the settings routes — a form that moved
/// the marker as a side effect would be doing onboarding to an installation
/// that finished it months ago.
/// </para>
/// <para>
/// So a step is answered in two writes: the form's, and then this one. That is
/// deliberate rather than tolerated. The one thing ADR 0010 asks of a step is
/// that closing the tab costs nothing, and it does not: the connection is
/// stored and checked, and the step it belongs to is offered again with what
/// was stored already there.
/// </para>
/// </remarks>
public sealed class Onboarding(FabDbContext context, ILogger<Onboarding> logger)
{
    /// <summary>
    /// The step is answered: the marker moves past it.
    /// </summary>
    /// <remarks>
    /// Refused unless the step actually holds what it was asking for, which is
    /// what keeps ADR 0010's two mandatory steps mandatory — the loop stands
    /// still until they are done, and nothing reaches
    /// <see cref="OnboardingStep.Complete"/> without them.
    /// </remarks>
    public async Task<OnboardingAct> TakeAsync(
        OnboardingStep step,
        CancellationToken cancellationToken = default)
    {
        var installation = await context.Installation.SingleAsync(cancellationToken);

        if (installation.OnboardingStep != step)
        {
            return new OnboardingAct(OnboardingOutcome.NotTheCurrentStep, installation.OnboardingStep);
        }

        if (!await IsAnsweredAsync(installation, step, cancellationToken))
        {
            return new OnboardingAct(OnboardingOutcome.NotConfigured, installation.OnboardingStep);
        }

        return await MoveOnAsync(installation, OnboardingOutcome.Taken, cancellationToken);
    }

    /// <summary>
    /// The step is passed by deliberately. ADR 0010: the consequence is spelled
    /// out at the moment it is taken — which the browser side does, because it
    /// is what asks — and what is left behind is a Gap on the connection.
    /// </summary>
    public async Task<OnboardingAct> SkipAsync(
        OnboardingStep step,
        CancellationToken cancellationToken = default)
    {
        var installation = await context.Installation.SingleAsync(cancellationToken);

        if (installation.OnboardingStep != step)
        {
            return new OnboardingAct(OnboardingOutcome.NotTheCurrentStep, installation.OnboardingStep);
        }

        if (!OnboardingPath.IsSkippable(step))
        {
            return new OnboardingAct(OnboardingOutcome.NotSkippable, installation.OnboardingStep);
        }

        // The Gap, recorded where the status slice will read it. It is not
        // inferred from an empty credential later on, because "nobody has been
        // asked yet" and "somebody was asked and said no" are the same empty
        // column and a different sentence.
        if (step is OnboardingStep.Sabnzbd)
        {
            installation.SabnzbdSkipped = true;
        }
        else
        {
            installation.IndexersSkipped = true;
        }

        logger.LogWarning(
            "The {Step} step was skipped during setting up. What it configures is missing until it "
            + "is filled in from the settings.",
            step);

        return await MoveOnAsync(installation, OnboardingOutcome.Skipped, cancellationToken);
    }

    private async Task<OnboardingAct> MoveOnAsync(
        InstallationRow installation,
        OnboardingOutcome outcome,
        CancellationToken cancellationToken)
    {
        installation.OnboardingStep = OnboardingPath.After(installation.OnboardingStep);

        context.Installation.Update(installation);
        await context.SaveChangesAsync(cancellationToken);

        if (installation.OnboardingStep is OnboardingStep.Complete)
        {
            logger.LogInformation("Setting up is finished. This installation is ready.");
        }

        return new OnboardingAct(outcome, installation.OnboardingStep);
    }

    /// <summary>
    /// Whether the form in front of this step stored anything. Asked of the
    /// database rather than trusted from the browser, because it is the whole
    /// of what stops a caller walking the path without answering it.
    /// </summary>
    private Task<bool> IsAnsweredAsync(
        InstallationRow installation,
        OnboardingStep step,
        CancellationToken cancellationToken) => step switch
    {
        OnboardingStep.Password => Task.FromResult(installation.PasswordHash is not null),
        OnboardingStep.PrdbKey => Task.FromResult(installation.PrdbApiKey is { Length: > 0 }),
        OnboardingStep.Sabnzbd => Task.FromResult(installation.SabnzbdApiKey is { Length: > 0 }),
        OnboardingStep.Indexers => context.Indexers.AnyAsync(cancellationToken),
        OnboardingStep.LibraryRoot => Task.FromResult(installation.LibraryRoot is { Length: > 0 }),
        _ => Task.FromResult(false),
    };
}

/// <summary>What happened to the step, and where the path stands afterwards.</summary>
public sealed record OnboardingAct(OnboardingOutcome Outcome, OnboardingStep NextStep);
