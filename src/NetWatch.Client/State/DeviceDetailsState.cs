using NetWatch.Client.Components;
using NetWatch.Core.Devices;
using NetWatch.Core.Diagnostics;
using NetWatch.Core.Live;
using NetWatch.Core.Usage;
using Wick.Core;

namespace NetWatch.Client.State;

[ReactiveState]
public partial class DeviceDetailsState
{
    public static UsageTotalsResponse EmptyTotals => new(0, 0, 0, 0);

    public partial DeviceDetailsResponse? Device { get; set; }

    public partial SystemStatusResponse? Status { get; set; }

    public partial UsageSummaryResponse? Summary { get; set; }

    public partial UsageTotalsResponse Today { get; set; } = EmptyTotals;

    public partial UsageTotalsResponse Week { get; set; } = EmptyTotals;

    public partial UsageTotalsResponse Month { get; set; } = EmptyTotals;

    public partial UsageHistoryResponse? History { get; set; }

    public partial LiveTrafficRateResponse? LiveTraffic { get; set; }

    public partial DateTime FromDate { get; set; }

    public partial DateTime ToDate { get; set; }

    public partial string? Error { get; set; }

    public partial string? RangeError { get; set; }

    public partial string? UsageError { get; set; }

    public partial bool Loading { get; set; } = true;

    public partial bool HistoryLoading { get; set; }

    public partial bool ClearingUsage { get; set; }

    public string DeviceStatusClass => Device switch
    {
        { IsManual: true } => "static",
        { IsOnline: true } => "online",
        _ => "offline"
    };

    public string DeviceStatusLabel => Device switch
    {
        { IsManual: true } => "Static",
        { IsOnline: true } => "Online",
        _ => "Offline"
    };

    public long HistoryTotal => History?.Points.Sum(point => point.Totals.DownloadBytes + point.Totals.UploadBytes) ?? 0;

    public long CurrentSpeed =>
        (LiveTraffic?.DownloadBitsPerSecond ?? 0) + (LiveTraffic?.UploadBitsPerSecond ?? 0);

    public string CurrentSpeedDetail =>
        $"{Helpers.BitsPerSecond(LiveTraffic?.DownloadBitsPerSecond ?? 0)} down · " +
        $"{Helpers.BitsPerSecond(LiveTraffic?.UploadBitsPerSecond ?? 0)} up";
}
