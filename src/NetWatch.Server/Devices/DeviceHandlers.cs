using NetWatch.Core.Devices;
using NetWatch.Server.Common;

namespace NetWatch.Server.Devices;

internal static class DeviceHandlers
{
    public static async Task<IResult> ListAsync(DeviceService service, CancellationToken cancellationToken) =>
        Map(await service.ListAsync(cancellationToken));

    public static async Task<IResult> GetAsync(
        string id,
        DeviceService service,
        CancellationToken cancellationToken) =>
        Map(await service.GetAsync(id, cancellationToken));

    public static async Task<IResult> RenameAsync(
        string id,
        RenameDeviceRequest request,
        DeviceService service,
        CancellationToken cancellationToken) =>
        Map(await service.RenameAsync(id, request, cancellationToken));

    public static async Task<IResult> CreateManualAsync(
        CreateManualDeviceRequest request,
        DeviceService service,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateManualAsync(request, cancellationToken);
        return result is DeviceDetailsResult success ?
            Results.Created($"/api/devices/{success.Value.Id}", success.Value) :
            Map(result);
    }

    private static IResult Map(ServiceResult result) => result switch
    {
        DeviceListResult success => Results.Ok(success.Value),
        DeviceDetailsResult success => Results.Ok(success.Value),
        InvalidDeviceServiceErrorResult => Results.Problem(
            statusCode: StatusCodes.Status400BadRequest),
        DeviceNotFoundServiceErrorResult => Results.Problem(
            statusCode: StatusCodes.Status404NotFound),
        DeviceConflictServiceErrorResult => Results.Problem(
            statusCode: StatusCodes.Status409Conflict),
        DeviceUnavailableServiceErrorResult => Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable),
        ErrorServiceResult => Results.Problem(
            statusCode: StatusCodes.Status500InternalServerError),
        _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
    };
}
