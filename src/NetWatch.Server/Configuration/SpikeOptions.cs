using System.Globalization;
using System.Net;

namespace NetWatch.Server.Configuration;

internal sealed record SpikeOptions(
    string DatabasePath,
    IPAddress NetFlowListenAddress,
    int NetFlowListenPort,
    Uri? MikroTikBaseUrl,
    string? MikroTikUsername,
    string? MikroTikPassword,
    bool MikroTikAllowInvalidCertificate,
    string TorchInterface)
{
    private const string FixedDatabasePath = "/data/netwatch.db";
    private static readonly IPAddress FixedNetFlowListenAddress = IPAddress.Any;

    public static SpikeOptions FromConfiguration(IConfiguration configuration)
    {
        var listenPortText = configuration["NETFLOW_LISTEN_PORT"] ?? "2055";
        var baseUrlText = configuration["MIKROTIK_BASE_URL"];

        if (!int.TryParse(listenPortText, NumberStyles.None, CultureInfo.InvariantCulture, out var listenPort) ||
            listenPort is < 1 or > 65535)
        {
            throw new InvalidOperationException($"NETFLOW_LISTEN_PORT '{listenPortText}' is invalid.");
        }

        Uri? baseUrl = null;
        if (!string.IsNullOrWhiteSpace(baseUrlText) &&
            (!Uri.TryCreate(baseUrlText, UriKind.Absolute, out baseUrl) ||
             (baseUrl.Scheme != Uri.UriSchemeHttp && baseUrl.Scheme != Uri.UriSchemeHttps)))
        {
            throw new InvalidOperationException("MIKROTIK_BASE_URL must be an absolute HTTP or HTTPS URL.");
        }

        return new SpikeOptions(
            FixedDatabasePath,
            FixedNetFlowListenAddress,
            listenPort,
            baseUrl,
            configuration["MIKROTIK_USERNAME"],
            configuration["MIKROTIK_PASSWORD"],
            bool.TryParse(configuration["MIKROTIK_ALLOW_INVALID_CERTIFICATE"], out var allowInvalid) && allowInvalid,
            configuration["MIKROTIK_TORCH_INTERFACE"] ?? "all");
    }
}
