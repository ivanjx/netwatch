using NetWatch.Server.Common;

namespace NetWatch.Server.FlowCollection;

internal static class CollectorDiagnosticsHandler
{
    public static async Task<IResult> GetAsync(
        CollectorDiagnosticsService service,
        CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(cancellationToken);
        return result switch
        {
            CollectorDiagnosticsResult success => Results.Ok(success.Value),
            CollectorDiagnosticsUnavailableServiceErrorResult => Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable),
            ErrorServiceResult => Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
        };
    }
}

