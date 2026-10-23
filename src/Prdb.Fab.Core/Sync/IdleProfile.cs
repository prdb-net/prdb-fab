namespace Prdb.Fab.Core.Sync;

/// <summary>Automatic polling and background demand derived from observed account limits.</summary>
public static class IdleProfile
{
    // The ten recurring routines registered by AddFabSync. User previews are
    // interest-driven, but their possible hourly poll is budgeted below.
    public const double RequestsAnHour = 4 + 2 + 2 + 1 + 1 + 1 + 1 + (1.0 / 6) + 1 + (1d / 24);
    public const double RequestsInThirtyDays = RequestsAnHour * 24 * 30;
    public static bool CarriedByMonthly(int? limit) => limit is null || CadenceMultiplier(null, limit) == 1;

    /// <summary>The API's monthly window is 31 rolling days.</summary>
    public const int WindowDays = 31;
    // Reserve room for the interest-driven preview feed and a daily account read,
    // even on an installation whose current idle set does not need previews.
    public const double ScheduledRequestsAnHour = RequestsAnHour + 1;
    public const double ScheduledRequestsPerWindow = ScheduledRequestsAnHour * 24 * WindowDays;
    public const double RecurringShare = 0.40;

    /// <summary>Fit recurring polls into at most half the usable account budget.</summary>
    public static double CadenceMultiplier(int? hourly, int? monthly) => Math.Max(1,
        Math.Max(hourly is > 0 ? ScheduledRequestsAnHour / (hourly.Value * RecurringShare) : 1,
            monthly is > 0 ? ScheduledRequestsPerWindow / (monthly.Value * RecurringShare) : 1));

    public static double ReservedPerWindow(int hourly, int monthly) =>
        ScheduledRequestsPerWindow / CadenceMultiplier(hourly, monthly);

    public static TimeSpan Adapt(TimeSpan cadence, int? hourly, int? monthly) =>
        TimeSpan.FromTicks((long)Math.Ceiling(cadence.Ticks * CadenceMultiplier(hourly, monthly)));

}
