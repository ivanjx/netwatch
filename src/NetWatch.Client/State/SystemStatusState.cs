using System.Globalization;
using Wick.Core;

namespace NetWatch.Client.State;

[ReactiveState]
public partial class SystemStatusState
{
    public partial DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

    public partial DateTimeOffset? ServerTime { get; set; }

    public partial DateTimeOffset ServerTimeReceivedAtUtc { get; set; }

    public partial DateTimeOffset? LastDataAtUtc { get; set; }

    public partial string? ReportingTimezone { get; set; }

    public partial bool IsOnline { get; set; }

    public string ServerTimeText => ServerTime is not { } serverTime ?
        "Server time unavailable" :
        $"{serverTime.Add(Now - ServerTimeReceivedAtUtc).ToString("d MMM yyyy HH:mm:ss", CultureInfo.InvariantCulture)} · {ReportingTimezone}";

    public string SystemStatusClass => IsOnline ? "online" : "offline";

    public string SystemStatusText => IsOnline ?
        $"Online · {DataFreshnessText}" :
        $"Offline · {DataFreshnessText}";

    public string SystemStatusTitle => IsOnline ?
        "NetWatch is reachable. Freshness is based on the latest collected data." :
        "NetWatch could not be reached during the latest status check.";

    public string DataFreshnessText
    {
        get
        {
            if (LastDataAtUtc is not { } lastDataAtUtc)
            {
                return "Waiting for data";
            }

            var age = Now - lastDataAtUtc;
            if (age.TotalMinutes < 1)
            {
                return "Updated just now";
            }

            if (age.TotalHours < 1)
            {
                return $"Updated {Math.Max(1, (int)age.TotalMinutes)} min ago";
            }

            if (age.TotalDays < 1)
            {
                return $"Updated {Math.Max(1, (int)age.TotalHours)} hr ago";
            }

            return $"Updated {Math.Max(1, (int)age.TotalDays)} d ago";
        }
    }
}
