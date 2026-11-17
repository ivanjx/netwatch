using NetWatch.Client.Components;
using NetWatch.Core.Live;
using NetWatch.Core.Usage;
using Wick.Core;

namespace NetWatch.Client.State;

[ReactiveState]
public partial class DashboardState
{
    public partial UsageSummaryResponse? Summary { get; set; }

    public partial UsageHistoryResponse? History { get; set; }

    public partial LiveTrafficSnapshotResponse? LiveTraffic { get; set; }

    public ReactiveList<DeviceUsageResponse> DeviceUsage { get; } = [];

    public partial string SelectedPeriodKey { get; set; } = "today";

    public partial string? Error { get; set; }

    public partial bool Loading { get; set; } = true;

    public partial bool PeriodLoading { get; set; }

    public UsagePeriodResponse SelectedPeriod => SelectedPeriodKey switch
    {
        "week" => Summary!.Week,
        "month" => Summary!.Month,
        _ => Summary!.Today
    };

    public string PeriodLabel => SelectedPeriodKey switch
    {
        "week" => "Since Monday",
        "month" => "Since the first of the month",
        _ => "Since local midnight"
    };

    public string ChartCaption => SelectedPeriodKey == "today" ? "Hourly totals" : "Daily totals";
    public string Grouping => SelectedPeriodKey == "today" ? "hour" : "day";
    public long PeriodTotal => SelectedPeriod.Totals.DownloadBytes + SelectedPeriod.Totals.UploadBytes;
    public long CurrentNetworkSpeed =>
        (LiveTraffic?.Network.DownloadBitsPerSecond ?? 0) +
        (LiveTraffic?.Network.UploadBitsPerSecond ?? 0);
    public string CurrentNetworkSpeedDetail =>
        $"{Helpers.BitsPerSecond(LiveTraffic?.Network.DownloadBitsPerSecond ?? 0)} down · " +
        $"{Helpers.BitsPerSecond(LiveTraffic?.Network.UploadBitsPerSecond ?? 0)} up";

    public string PeriodClass(string period) => SelectedPeriodKey == period ? "active" : string.Empty;

    public string Share(long total) => PeriodTotal == 0 ? "0%" : $"{total * 100d / PeriodTotal:0.#}%";

    public long CurrentSpeed(string deviceId)
    {
        var rate = LiveTraffic?.Devices.FirstOrDefault(device => device.DeviceId == deviceId)?.Rate;
        return (rate?.DownloadBitsPerSecond ?? 0) + (rate?.UploadBitsPerSecond ?? 0);
    }
}
