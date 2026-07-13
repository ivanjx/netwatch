using System.Globalization;
using System.Net;

namespace NetWatch.Server.Configuration;

internal sealed record NetWatchOptions(
    int NetFlowListenPort,
    IReadOnlyList<IPAddress> NetFlowAllowedExporters,
    Uri? MikroTikBaseUrl,
    string? MikroTikUsername,
    string? MikroTikPassword,
    bool MikroTikAllowInvalidCertificate,
    string MikroTikInterface,
    IReadOnlyList<string> WanInterfaces,
    IReadOnlyList<IPNetwork> IgnoredNetworks,
    TimeZoneInfo ReportingTimeZone,
    TimeSpan DhcpSyncInterval,
    TimeSpan UsageFlushInterval,
    TimeSpan LiveSampleInterval,
    TimeSpan LiveIdleTimeout,
    int HourlyRetentionMonths,
    int UnresolvedRetentionDays,
    int DiagnosticRetentionDays)
{
    public static NetWatchOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var netFlowListenPort = ParseInteger(configuration, "NETFLOW_LISTEN_PORT", 2055, 1, 65_535);
        var baseUrl = ParseBaseUrl(configuration["MIKROTIK_BASE_URL"]);
        var reportingTimeZone = ParseTimeZone(configuration["TZ"] ?? TimeZoneInfo.Utc.Id);

        return new NetWatchOptions(
            netFlowListenPort,
            ParseAddresses(configuration["NETFLOW_ALLOWED_EXPORTERS"], "NETFLOW_ALLOWED_EXPORTERS"),
            baseUrl,
            NullIfWhiteSpace(configuration["MIKROTIK_USERNAME"]),
            configuration["MIKROTIK_PASSWORD"],
            ParseBoolean(configuration, "MIKROTIK_ALLOW_INVALID_CERTIFICATE", false),
            NullIfWhiteSpace(configuration["MIKROTIK_INTERFACE"]) ?? "all",
            ParseValues(configuration["WAN_INTERFACES"]),
            ParseNetworks(configuration["IGNORED_NETWORKS"], "IGNORED_NETWORKS"),
            reportingTimeZone,
            TimeSpan.FromSeconds(ParseInteger(configuration, "DHCP_SYNC_INTERVAL_SECONDS", 30, 1, 86_400)),
            TimeSpan.FromSeconds(ParseInteger(configuration, "USAGE_FLUSH_INTERVAL_SECONDS", 5, 1, 3_600)),
            TimeSpan.FromSeconds(ParseInteger(configuration, "LIVE_SAMPLE_INTERVAL_SECONDS", 2, 1, 60)),
            TimeSpan.FromSeconds(ParseInteger(configuration, "LIVE_IDLE_TIMEOUT_SECONDS", 15, 1, 3_600)),
            ParseInteger(configuration, "HOURLY_RETENTION_MONTHS", 24, 1, 1_200),
            ParseInteger(configuration, "UNRESOLVED_RETENTION_DAYS", 30, 1, 36_500),
            ParseInteger(configuration, "DIAGNOSTIC_RETENTION_DAYS", 30, 1, 36_500));
    }

    private static Uri? ParseBaseUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("MIKROTIK_BASE_URL must be an absolute HTTP or HTTPS URL.");
        }

        return uri;
    }

    private static TimeZoneInfo ParseTimeZone(string value)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(value);
        }
        catch (TimeZoneNotFoundException exception)
        {
            throw new InvalidOperationException($"TZ '{value}' was not found.", exception);
        }
        catch (InvalidTimeZoneException exception)
        {
            throw new InvalidOperationException($"TZ '{value}' is invalid.", exception);
        }
    }

    private static int ParseInteger(
        IConfiguration configuration,
        string key,
        int defaultValue,
        int minimum,
        int maximum)
    {
        var text = configuration[key];
        if (text is null)
        {
            return defaultValue;
        }

        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ||
            value < minimum || value > maximum)
        {
            throw new InvalidOperationException($"{key} must be an integer from {minimum} through {maximum}.");
        }

        return value;
    }

    private static bool ParseBoolean(IConfiguration configuration, string key, bool defaultValue)
    {
        var text = configuration[key];
        if (text is null)
        {
            return defaultValue;
        }

        if (!bool.TryParse(text, out var value))
        {
            throw new InvalidOperationException($"{key} must be 'true' or 'false'.");
        }

        return value;
    }

    private static IReadOnlyList<IPAddress> ParseAddresses(string? value, string key) =>
        ParseValues(value).Select(item => ParseAddress(item, key)).ToArray();

    private static IPAddress ParseAddress(string value, string key)
    {
        if (!IPAddress.TryParse(value, out var address))
        {
            throw new InvalidOperationException($"{key} contains invalid IP address '{value}'.");
        }

        return address;
    }

    private static IReadOnlyList<IPNetwork> ParseNetworks(string? value, string key)
    {
        var networks = new List<IPNetwork>();
        foreach (var item in ParseValues(value))
        {
            if (!IPNetwork.TryParse(item, out var network))
            {
                throw new InvalidOperationException($"{key} contains invalid subnet '{item}'.");
            }

            networks.Add(network);
        }

        return networks;
    }

    private static IReadOnlyList<string> ParseValues(string? value) =>
        string.IsNullOrWhiteSpace(value) ?
            [] :
            value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
