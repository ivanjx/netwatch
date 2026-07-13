using NetWatch.Server.Common;

namespace NetWatch.Server.Health;

internal static class SystemStatusHandler
{
    public static async Task<IResult> GetAsync(
        SystemStatusService service,
        CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(cancellationToken);
        return result switch
        {
            SystemStatusResult success => Results.Ok(success.Value),
            SystemStatusUnavailableServiceErrorResult => Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable),
            ErrorServiceResult => Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
        };
    }
}
