namespace Prdb.Fab.Core.Sync;

/// <summary>Automatic polling and background demand derived from observed account limits.</summary>
public static class IdleProfile
{
    // The ten recurring routines registered by AddFabSync. User previews are
    // interest-driven, but their possible hourly poll is budgeted below.
    public const double RequestsAnHour = 4 + 2 + 2 + 2 + 1 + 1 + 1 + (1.0 / 6) + 1 + (1d / 24);
    public const double RequestsInThirtyDays = RequestsAnHour * 24 * 30;
    public static bool CarriedByMonthly(int? limit) => limit is null || CadenceMultiplier(null, limit) == 1;

    /// <summary>The API's monthly window is 31 rolling days.</summary>
    public const int WindowDays = 31;
    // Reserve room for the interest-driven preview feed and a daily account read,
    // even on an installation whose current idle set does not need previews.
    public const double ScheduledRequestsAnHour = RequestsAnHour + 1;
    public const double ScheduledRequestsPerWindow = ScheduledRequestsAnHour * 24 * WindowDays;
    public const double RecurringShare = 0.40;

    public static TimeSpan WantedCadenceFor(IEnumerable<string?> packages) =>
        packages.Select(package => package?.ToLowerInvariant() switch
        {
            "ultra" => TimeSpan.FromMinutes(5),
            "premium" => TimeSpan.FromMinutes(15),
            _ => TimeSpan.FromMinutes(30),
        }).DefaultIfEmpty(TimeSpan.FromMinutes(30)).Min();

    public static double RequestsAnHourFor(TimeSpan? wantedCadence) =>
        RequestsAnHour - 2 + 1 / (wantedCadence ?? TimeSpan.FromMinutes(30)).TotalHours;

    public static double ScheduledRequestsAnHourFor(TimeSpan? wantedCadence) => RequestsAnHourFor(wantedCadence) + 1;

    public static double ScheduledRequestsPerWindowFor(TimeSpan? wantedCadence) =>
        ScheduledRequestsAnHourFor(wantedCadence) * 24 * WindowDays;

    /// <summary>Fit recurring polls into at most half the usable account budget.</summary>
    public static double CadenceMultiplier(int? hourly, int? monthly, TimeSpan? wantedCadence = null) => Math.Max(1,
        Math.Max(hourly is > 0 ? ScheduledRequestsAnHourFor(wantedCadence) / (hourly.Value * RecurringShare) : 1,
            monthly is > 0 ? ScheduledRequestsPerWindowFor(wantedCadence) / (monthly.Value * RecurringShare) : 1));

    public static double ReservedPerWindow(int hourly, int monthly, TimeSpan? wantedCadence = null) =>
        ScheduledRequestsPerWindowFor(wantedCadence) / CadenceMultiplier(hourly, monthly, wantedCadence);

    public static TimeSpan Adapt(TimeSpan cadence, int? hourly, int? monthly, TimeSpan? wantedCadence = null) =>
        TimeSpan.FromTicks((long)Math.Ceiling(cadence.Ticks * CadenceMultiplier(hourly, monthly, wantedCadence)));

}
