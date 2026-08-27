using Prdb.Fab.Core.Sync;

using Xunit;

namespace Prdb.Fab.Core.Tests.Sync;

/// <summary>
/// ADR 0014's named condition as a rule: what a plan has to be to carry the
/// schedule, and what is given up first when it does not.
/// </summary>
public sealed class IdleProfileTests
{
    /// <summary>
    /// The sentence ADR 0014 uses, as a number: about nine requests an hour.
    /// The routines that make them are added up where they are registered; this
    /// is the constant they are added up against.
    /// </summary>
    [Fact]
    public void The_idle_profile_is_about_nine_requests_an_hour()
    {
        Assert.InRange(IdleProfile.RequestsAnHour, 9, 10);
    }

    /// <summary>
    /// The half is not a new number. ADR 0014 gives the repair pass whatever
    /// holds hourly usage under half the limit, which makes the half the line
    /// all background work stays under — and the other half is what a person or
    /// an arrived file is waiting on.
    /// </summary>
    [Theory]
    [InlineData(6, false)]
    [InlineData(18, false)]
    [InlineData(19, true)]
    [InlineData(1000, true)]
    public void A_plan_carries_the_schedule_when_half_of_it_covers_the_profile(int limit, bool carries)
    {
        Assert.Equal(carries, IdleProfile.CarriedBy(limit));
    }

    /// <summary>
    /// Nothing read yet is not a plan that is too small. An installation that
    /// has asked prdb nothing must not start out degraded, and the first answer
    /// is what settles it.
    /// </summary>
    [Fact]
    public void A_limit_that_has_not_been_read_carries_the_schedule()
    {
        Assert.True(IdleProfile.CarriedBy(null));
    }

    /// <summary>
    /// ADR 0014's order, quoted: actors to 24 h, images and What's New to
    /// 60 min. Asserted as a table because that is what it is — a degradation
    /// the user can be told about, not a scheduler reasoning about itself.
    /// </summary>
    [Theory]
    [InlineData(PrdbWork.Actors, 6, 24)]
    [InlineData(PrdbWork.Images, 0.5, 1)]
    [InlineData(PrdbWork.WhatsNew, 0.25, 1)]
    public void A_plan_too_small_sheds_in_the_documented_order(
        PrdbWork work,
        double cadenceHours,
        double shedToHours)
    {
        var shed = IdleProfile.CadenceFor(work, TimeSpan.FromHours(cadenceHours), shedding: true);

        Assert.Equal(TimeSpan.FromHours(shedToHours), shed);
    }

    /// <summary>
    /// What is not shed, and the reason it is not an omission: the wanted list
    /// is ADR 0007's only source of intent, so a tool that stops reading it
    /// stops being able to want anything. What is given up is knowing about
    /// videos nobody has asked for.
    /// </summary>
    [Theory]
    [InlineData(PrdbWork.UserFeeds)]
    [InlineData(PrdbWork.Sites)]
    [InlineData(PrdbWork.Identification)]
    [InlineData(PrdbWork.Writes)]
    public void The_work_the_user_is_waiting_on_is_never_shed(PrdbWork work)
    {
        var cadence = TimeSpan.FromHours(1);

        Assert.Equal(cadence, IdleProfile.CadenceFor(work, cadence, shedding: true));
    }

    /// <summary>
    /// A plan that carries the schedule changes nothing at all, which is what
    /// makes the condition worth recording when it does arrive.
    /// </summary>
    [Fact]
    public void A_plan_that_carries_the_schedule_changes_no_cadence()
    {
        var cadence = TimeSpan.FromHours(6);

        Assert.Equal(cadence, IdleProfile.CadenceFor(PrdbWork.Actors, cadence, shedding: false));
    }

    /// <summary>
    /// Shedding only ever asks for less. A shed cadence shorter than the one in
    /// the code would be this table quietly speeding something up, which is not
    /// a degradation and not what any of it is for.
    /// </summary>
    [Fact]
    public void Shedding_never_makes_a_routine_run_more_often()
    {
        // The site list already runs daily, which is what actors are shed to.
        var daily = TimeSpan.FromHours(24);

        Assert.Equal(daily, IdleProfile.CadenceFor(PrdbWork.Actors, daily, shedding: true));

        // And a routine already slower than the shed cadence keeps its own.
        var weekly = TimeSpan.FromDays(7);

        Assert.Equal(weekly, IdleProfile.CadenceFor(PrdbWork.Images, weekly, shedding: true));
    }
}
