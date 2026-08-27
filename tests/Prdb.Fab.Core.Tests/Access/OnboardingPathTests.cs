using Prdb.Fab.Core.Access;

using Xunit;

namespace Prdb.Fab.Core.Tests.Access;

/// <summary>
/// ADR 0010's path, which is written down in exactly one place so that nothing
/// has to agree with it from memory.
/// </summary>
public sealed class OnboardingPathTests
{
    [Fact]
    public void The_path_is_the_one_ADR_0010_describes()
    {
        Assert.Equal(
            [
                OnboardingStep.Password,
                OnboardingStep.PrdbKey,
                OnboardingStep.Sabnzbd,
                OnboardingStep.Indexers,
                OnboardingStep.LibraryRoot,
                OnboardingStep.Complete,
            ],
            OnboardingPath.Steps);
    }

    /// <summary>
    /// A step that is added to the enum and forgotten here would otherwise be
    /// unreachable, which is the kind of thing that looks like a working wizard
    /// until somebody takes the step before it.
    /// </summary>
    [Fact]
    public void Every_step_there_is_is_on_the_path_once()
    {
        Assert.Equal(Enum.GetValues<OnboardingStep>().Order(), OnboardingPath.Steps.Order());
        Assert.Equal(OnboardingPath.Steps.Count, OnboardingPath.Steps.Distinct().Count());
    }

    [Fact]
    public void Each_step_leads_to_the_next_one()
    {
        Assert.Equal(OnboardingStep.PrdbKey, OnboardingPath.After(OnboardingStep.Password));
        Assert.Equal(OnboardingStep.Sabnzbd, OnboardingPath.After(OnboardingStep.PrdbKey));
        Assert.Equal(OnboardingStep.Indexers, OnboardingPath.After(OnboardingStep.Sabnzbd));
        Assert.Equal(OnboardingStep.LibraryRoot, OnboardingPath.After(OnboardingStep.Indexers));
        Assert.Equal(OnboardingStep.Complete, OnboardingPath.After(OnboardingStep.LibraryRoot));
    }

    /// <summary>ADR 0010: the wizard is finished, and does not return.</summary>
    [Fact]
    public void The_end_of_the_path_leads_to_itself()
    {
        Assert.Equal(OnboardingStep.Complete, OnboardingPath.After(OnboardingStep.Complete));
    }

    /// <summary>
    /// ADR 0010 names two, and the argument for each is that the tool is still
    /// a tool without it. Neither mandatory step has such an argument.
    /// </summary>
    [Fact]
    public void Only_the_downloader_and_the_indexers_may_be_skipped()
    {
        Assert.Equal(
            [OnboardingStep.Sabnzbd, OnboardingStep.Indexers],
            OnboardingPath.Steps.Where(OnboardingPath.IsSkippable));
    }

    [Fact]
    public void No_two_outcomes_say_the_same_thing()
    {
        var sentences = Enum.GetValues<OnboardingOutcome>()
            .Select(OnboardingPath.Sentence)
            .ToArray();

        Assert.Equal(sentences.Length, sentences.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(sentences, sentence => sentence.Length == 0);
    }
}
