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
            SystemStatusUnavailableServiceErrorResult error => Results.Problem(
                title: error.Message,
                detail: error.Detail,
                statusCode: StatusCodes.Status503ServiceUnavailable,
                extensions: new Dictionary<string, object?> { ["code"] = error.Code }),
            ErrorServiceResult error => Results.Problem(
                title: error.Message,
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?> { ["code"] = error.Code }),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
        };
    }
}
