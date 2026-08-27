using Prdb.Fab.Core.Access;

using Xunit;

namespace Prdb.Fab.Core.Tests.Access;

public sealed class PasswordRuleTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    public void What_is_too_short_is_refused_with_a_reason(string? password)
    {
        // ADR 0043: a rule in Core cannot log, so it returns its reason.
        Assert.NotNull(PasswordRule.Refuse(password));
    }

    [Fact]
    public void A_password_at_the_floor_is_accepted()
    {
        Assert.Null(PasswordRule.Refuse(new string('x', PasswordRule.MinimumLength)));
    }

    [Fact]
    public void Nothing_is_trimmed_away_before_it_is_measured()
    {
        // A password is a secret rather than a name: spaces are characters of
        // it, and a rule that trims them would quietly accept "       x".
        Assert.Null(PasswordRule.Refuse("        "));
    }

    [Fact]
    public void A_body_too_large_to_hash_is_refused_rather_than_hashed()
    {
        Assert.NotNull(PasswordRule.Refuse(new string('x', PasswordRule.MaximumLength + 1)));
    }
}
