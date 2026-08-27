using Prdb.Fab.Core.Connections;

using Xunit;

namespace Prdb.Fab.Core.Tests.Connections;

public sealed class PrdbConnectionTests
{
    /// <summary>
    /// ADR 0010 asks for four distinct verdicts rather than one message, and
    /// the reason is that two of them ask for a correction and two of them ask
    /// for patience. A shared sentence would lose exactly that.
    /// </summary>
    [Fact]
    public void No_two_verdicts_say_the_same_thing()
    {
        var sentences = Enum.GetValues<PrdbConnectionOutcome>()
            .Select(PrdbConnection.Sentence)
            .ToArray();

        Assert.Equal(sentences.Length, sentences.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(sentences, sentence => sentence.Length == 0);
    }
}
