using NetWatch.Server.Common;

namespace NetWatch.Server.Usage;

internal static class UsageHandlers
{
    public static async Task<IResult> GetSummaryAsync(
        HttpRequest request,
        UsageService service,
        CancellationToken cancellationToken) =>
        Map(await service.GetSummaryAsync(
            request.Query["timezone"],
            request.Query["category"],
            request.Query["wanInterface"],
            cancellationToken));

    public static async Task<IResult> GetDevicesAsync(
        HttpRequest request,
        UsageService service,
        CancellationToken cancellationToken) =>
        Map(await service.GetDevicesAsync(
            request.Query["from"],
            request.Query["to"],
            request.Query["timezone"],
            request.Query["category"],
            request.Query["wanInterface"],
            cancellationToken));

    public static async Task<IResult> GetHistoryAsync(
        HttpRequest request,
        UsageService service,
        CancellationToken cancellationToken) =>
        Map(await service.GetHistoryAsync(
            request.Query["from"],
            request.Query["to"],
            request.Query["timezone"],
            request.Query["grouping"],
            request.Query["deviceId"],
            request.Query["category"],
            request.Query["wanInterface"],
            cancellationToken));

    public static async Task<IResult> ClearDeviceAsync(
        string id,
        UsageClearService service,
        CancellationToken cancellationToken) =>
        Map(await service.ClearDeviceAsync(id, cancellationToken));

    public static async Task<IResult> ClearAllAsync(
        UsageClearService service,
        CancellationToken cancellationToken) =>
        Map(await service.ClearAllAsync(cancellationToken));

    private static IResult Map(ServiceResult result) => result switch
    {
        UsageSummaryResult success => Results.Ok(success.Value),
        DeviceUsageListResult success => Results.Ok(success.Value),
        UsageHistoryResult success => Results.Ok(success.Value),
        UsageClearedResult => Results.NoContent(),
        InvalidUsageQueryServiceErrorResult => Results.Problem(
            statusCode: StatusCodes.Status400BadRequest),
        UsageUnavailableServiceErrorResult => Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable),
        ErrorServiceResult => Results.Problem(
            statusCode: StatusCodes.Status500InternalServerError),
        _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
    };
}
