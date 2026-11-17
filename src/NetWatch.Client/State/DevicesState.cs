using NetWatch.Core.Devices;
using Wick.Core;

namespace NetWatch.Client.State;

[ReactiveState]
public partial class DevicesState
{
    public ReactiveList<DeviceSummaryResponse> Devices { get; } = [];

    public partial bool IsLoaded { get; set; }

    public partial string? Error { get; set; }

    public partial string? Message { get; set; }

    public partial string? EditingId { get; set; }

    public partial string? EditName { get; set; }

    public partial string ManualName { get; set; } = string.Empty;

    public partial string ManualAddress { get; set; } = string.Empty;

    public partial string ManualMac { get; set; } = string.Empty;

    public partial string ManualType { get; set; } = "Client";

    public partial string ManualNotes { get; set; } = string.Empty;

    public partial bool Saving { get; set; }

    public partial bool ClearingUsage { get; set; }
}
