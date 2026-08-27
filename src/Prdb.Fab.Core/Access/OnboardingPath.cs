namespace Prdb.Fab.Core.Access;

/// <summary>
/// ADR 0010's path, as a rule rather than as an order that is repeated wherever
/// a step ends: the sequence of <see cref="OnboardingStep"/>, which of them may
/// be skipped, and what may be answered when a step is taken or skipped.
/// </summary>
/// <remarks>
/// The marker moves in exactly one place (<c>Onboarding</c> in Infrastructure),
/// and this is the rule it moves by. Onboarding is a path with a beginning and
/// an end rather than a set of addresses that happen to be numbered, and the
/// four forms know nothing about it — which is what lets ADR 0020 put the same
/// forms behind the settings routes without an onboarding marker anywhere near
/// them.
/// </remarks>
public static class OnboardingPath
{
    private static readonly OnboardingStep[] InOrder =
    [
        OnboardingStep.Password,
        OnboardingStep.PrdbKey,
        OnboardingStep.Sabnzbd,
        OnboardingStep.Indexers,
        OnboardingStep.LibraryRoot,
        OnboardingStep.Complete,
    ];

    /// <summary>The path, in order, ending on the state that never leads anywhere.</summary>
    public static IReadOnlyList<OnboardingStep> Steps => InOrder;

    /// <summary>
    /// Where the path goes once this step is behind it. <see cref="OnboardingStep.Complete"/>
    /// answers itself: ADR 0010 finishes the wizard and does not return to it.
    /// </summary>
    public static OnboardingStep After(OnboardingStep step)
    {
        var position = Array.IndexOf(InOrder, step);

        return position < 0 || position == InOrder.Length - 1
            ? OnboardingStep.Complete
            : InOrder[position + 1];
    }

    /// <summary>
    /// Whether ADR 0010 allows this step to be passed by without being answered.
    /// SABnzbd and the indexers, and nothing else: a tool that cannot download
    /// is still a tool that holds a library, and one that has no library root
    /// has nowhere to put anything.
    /// </summary>
    public static bool IsSkippable(OnboardingStep step) =>
        step is OnboardingStep.Sabnzbd or OnboardingStep.Indexers;

    /// <summary>
    /// ADR 0043: every sentence a person reads is a value returned from here, so
    /// that a test can read them and hold that no two of them say the same thing.
    /// </summary>
    public static string Sentence(OnboardingOutcome outcome) => outcome switch
    {
        OnboardingOutcome.Taken => "That step is done, and setting up continues at the next one.",
        OnboardingOutcome.Skipped =>
            "That step was skipped. What it would have configured is missing until it is filled in "
            + "from the settings.",
        OnboardingOutcome.NotTheCurrentStep =>
            "Setting up is not on that step, so nothing was changed. This window was showing a "
            + "different one, and it has been brought back to where the path actually is.",
        OnboardingOutcome.NotConfigured =>
            "That step has nothing stored yet, so there is nothing to continue past.",
        OnboardingOutcome.NotSkippable =>
            "That step cannot be skipped: nothing works without it.",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
    };
}

/// <summary>
/// What happened to a step that was taken or skipped. ADR 0040: a refusal is
/// something the tool checked and can answer, so all five of these are a 200
/// with a name in them.
/// </summary>
public enum OnboardingOutcome
{
    /// <summary>The step was answered, and the marker has moved past it.</summary>
    Taken,

    /// <summary>
    /// The step was passed by deliberately, and the marker has moved past it.
    /// What is left behind is a Gap on the connection it would have configured.
    /// </summary>
    Skipped,

    /// <summary>
    /// The path is somewhere else — a second window, or a browser that was left
    /// open while the path moved on. Nothing was written.
    /// </summary>
    NotTheCurrentStep,

    /// <summary>
    /// Nothing was stored by the form in front of this step, so it has not been
    /// answered. This is what keeps the two mandatory steps mandatory.
    /// </summary>
    NotConfigured,

    /// <summary>The step is one of ADR 0010's two mandatory ones.</summary>
    NotSkippable,
}
