namespace NetWatch.Server.Live;

internal static class LiveTrafficHandler
{
    public static IResult Get(LiveTrafficTracker tracker) => Results.Ok(tracker.GetSnapshot());
}
