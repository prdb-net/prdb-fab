using Prdb.Fab.Core.Access;
using Prdb.Fab.Infrastructure.Access;

namespace Prdb.Fab.Host.Access;

/// <summary>
/// ADR 0010's path, seen from the API: the two acts that move the marker.
/// </summary>
/// <remarks>
/// Which step is next is answered by <c>GET /api/access/state</c>, because the
/// one page decides everything it shows from that one read. What is here is
/// only what moves — ADR 0040: named actions, and a refusal is a 200 with a
/// name in it, because a browser that is a step behind is something the tool
/// checked and can answer.
/// </remarks>
public static class OnboardingEndpoints
{
    public static void MapOnboarding(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/onboarding").WithTags("Onboarding");

        group.MapPost("/take", async (
            OnboardingStepRequest request,
            Onboarding onboarding,
            CancellationToken cancellationToken) =>
        {
            var act = await onboarding.TakeAsync(request.Step, cancellationToken);

            return TypedResults.Ok(new OnboardingVerdict(
                act.Outcome,
                OnboardingPath.Sentence(act.Outcome),
                act.NextStep));
        });

        group.MapPost("/skip", async (
            OnboardingStepRequest request,
            Onboarding onboarding,
            CancellationToken cancellationToken) =>
        {
            var act = await onboarding.SkipAsync(request.Step, cancellationToken);

            return TypedResults.Ok(new OnboardingVerdict(
                act.Outcome,
                OnboardingPath.Sentence(act.Outcome),
                act.NextStep));
        });
    }
}

/// <summary>
/// Which step is being acted on. Named rather than implied, so that a browser
/// left open across a restart is told it is a step behind instead of quietly
/// moving whichever step the installation happens to be on now.
/// </summary>
public sealed record OnboardingStepRequest(OnboardingStep Step);

/// <summary>ADR 0040: a verdict is a success with a typed body saying what happened.</summary>
/// <param name="NextStep">
/// Where the path stands after this, whether or not it moved. The browser side
/// navigates by it rather than by assuming the act did what it asked.
/// </param>
public sealed record OnboardingVerdict(
    OnboardingOutcome Outcome,
    string Detail,
    OnboardingStep NextStep);
