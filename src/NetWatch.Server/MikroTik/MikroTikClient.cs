using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using NetWatch.Server.Common;
using NetWatch.Server.Configuration;
using NetWatch.Server.Serialization;

namespace NetWatch.Server.MikroTik;

internal sealed class MikroTikClient : IMikroTikClient, IDisposable
{
    private readonly NetWatchOptions _options;
    private readonly ILogger<MikroTikClient> _logger;
    private readonly HttpClient _httpClient;

    public MikroTikClient(NetWatchOptions options, ILogger<MikroTikClient> logger)
    {
        _options = options;
        _logger = logger;

        var handler = new HttpClientHandler();
        if (options.MikroTikAllowInvalidCertificate)
        {
            handler.ServerCertificateCustomValidationCallback = static (_, _, _, _) => true;
        }

        _httpClient = new HttpClient(handler)
        {
            BaseAddress = options.MikroTikBaseUrl,
            Timeout = TimeSpan.FromSeconds(15)
        };

        if (!string.IsNullOrEmpty(options.MikroTikUsername))
        {
            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{options.MikroTikUsername}:{options.MikroTikPassword}"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }
    }

    public async Task<ServiceResult> GetRouterVersionAsync(CancellationToken cancellationToken)
    {
        var result = await GetPayloadAsync("rest/system/resource", cancellationToken);
        if (result is not RouterPayloadResult response)
        {
            return result;
        }

        var element = response.Value;
        if (element.ValueKind == JsonValueKind.Array && element.GetArrayLength() > 0)
        {
            element = element[0];
        }

        var resource = element.ValueKind == JsonValueKind.Object ?
            element.Deserialize(NetWatchJsonContext.Default.RouterSystemResourceDto) :
            null;
        var version = resource?.Version;
        if (string.IsNullOrWhiteSpace(version) || !TryParseMajorVersion(version, out var major))
        {
            _logger.LogWarning("RouterOS response contained a missing or unrecognized version");
            return new MikroTikInvalidResponseServiceErrorResult();
        }

        return new RouterVersionResult(version, major, major >= 7);
    }

    public async Task<ServiceResult> GetDhcpLeasesAsync(CancellationToken cancellationToken)
    {
        var result = await GetAsync(
            "rest/ip/dhcp-server/lease",
            NetWatchJsonContext.Default.IReadOnlyListRouterDhcpLeaseDto,
            cancellationToken);
        if (result is not RouterResponseResult<IReadOnlyList<RouterDhcpLeaseDto>> response)
        {
            return result;
        }

        var leases = new List<DhcpLease>(response.Value.Count);
        foreach (var item in response.Value)
        {
            var macAddress = item.ActiveMacAddress ?? item.MacAddress;
            var ipAddress = item.ActiveAddress ?? item.Address;
            if (string.IsNullOrWhiteSpace(macAddress) || string.IsNullOrWhiteSpace(ipAddress))
            {
                continue;
            }

            var isActive = string.Equals(item.Status, "bound", StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrWhiteSpace(item.ActiveAddress);
            leases.Add(new DhcpLease(
                macAddress,
                ipAddress,
                item.HostName,
                item.Status,
                ParseRouterOsBoolean(item.Dynamic),
                item.LastSeen,
                item.Server,
                item.Comment,
                isActive));
        }

        return new DhcpLeasesResult(leases);
    }

    public void Dispose() => _httpClient.Dispose();

    private async Task<ServiceResult> GetAsync<T>(
        string path,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken)
    {
        var result = await GetPayloadAsync(path, cancellationToken);
        if (result is not RouterPayloadResult response)
        {
            return result;
        }

        try
        {
            var value = response.Value.Deserialize(jsonTypeInfo);
            if (value is null)
            {
                _logger.LogWarning("RouterOS response for {Path} was empty", path);
                return new MikroTikInvalidResponseServiceErrorResult();
            }

            return new RouterResponseResult<T>(value);
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "RouterOS response for {Path} could not be read", path);
            return new MikroTikInvalidResponseServiceErrorResult();
        }
    }

    private async Task<ServiceResult> GetPayloadAsync(string path, CancellationToken cancellationToken)
    {
        if (_options.MikroTikBaseUrl is null)
        {
            return new MikroTikNotConfiguredServiceErrorResult();
        }

        try
        {
            using var response = await _httpClient.GetAsync(path, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "RouterOS request {Path} failed with HTTP {StatusCode}",
                    path,
                    (int)response.StatusCode);
                return new MikroTikUnavailableServiceErrorResult();
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return new RouterPayloadResult(document.RootElement.Clone());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new CanceledServiceErrorResult();
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "RouterOS response for {Path} was malformed", path);
            return new MikroTikInvalidResponseServiceErrorResult();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "RouterOS request {Path} failed", path);
            return new MikroTikUnavailableServiceErrorResult();
        }
    }

    private static bool ParseRouterOsBoolean(string? value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);

    private static bool TryParseMajorVersion(string value, out int major)
    {
        var separator = value.IndexOfAny(['.', ' ', '-']);
        var majorText = separator < 0 ? value : value[..separator];
        return int.TryParse(majorText, out major) && major >= 0;
    }

    private sealed record RouterResponseResult<T>(T Value) : SuccessServiceResult;

    private sealed record RouterPayloadResult(JsonElement Value) : SuccessServiceResult;
}
