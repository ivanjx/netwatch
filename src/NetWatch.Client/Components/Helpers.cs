using NetWatch.Core.Usage;

namespace NetWatch.Client.Components;

public static class Helpers
{
    private static readonly string[] _units = ["B", "KB", "MB", "GB", "TB", "PB"];
    private static readonly string[] _rateUnits = ["bps", "Kbps", "Mbps", "Gbps", "Tbps"];

    public static string Bytes(long bytes)
    {
        var value = Math.Max(0, bytes);
        var unit = 0;
        var scaled = (double)value;
        while (scaled >= 1024 && unit < _units.Length - 1)
        {
            scaled /= 1024;
            unit++;
        }

        return $"{scaled:0.#} {_units[unit]}";
    }

    public static string Total(UsageTotalsResponse totals) =>
        Bytes(totals.DownloadBytes + totals.UploadBytes);

    public static string Number(long value) => value.ToString("N0");

    public static string BitsPerSecond(long bitsPerSecond)
    {
        var value = Math.Max(0, bitsPerSecond);
        var unit = 0;
        var scaled = (double)value;
        while (scaled >= 1000 && unit < _rateUnits.Length - 1)
        {
            scaled /= 1000;
            unit++;
        }

        return $"{scaled:0.#} {_rateUnits[unit]}";
    }
}
