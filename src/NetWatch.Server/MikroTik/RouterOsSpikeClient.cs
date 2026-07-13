using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using NetWatch.Server.Configuration;

namespace NetWatch.Server.MikroTik;

internal sealed class RouterOsSpikeClient : IDisposable
{
    private readonly SpikeOptions options;
    private readonly ILogger<RouterOsSpikeClient> logger;
    private readonly HttpClient httpClient;

    public RouterOsSpikeClient(SpikeOptions options, ILogger<RouterOsSpikeClient> logger)
    {
        this.options = options;
        this.logger = logger;

        var handler = new HttpClientHandler();
        if (options.MikroTikAllowInvalidCertificate)
        {
            handler.ServerCertificateCustomValidationCallback = static (_, _, _, _) => true;
        }

        httpClient = new HttpClient(handler)
        {
            BaseAddress = options.MikroTikBaseUrl,
            Timeout = TimeSpan.FromSeconds(15)
        };

        if (!string.IsNullOrEmpty(options.MikroTikUsername))
        {
            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{options.MikroTikUsername}:{options.MikroTikPassword}"));
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }
    }

    public async Task<RouterOsSpikeResult> GetSupportedVersionAsync(CancellationToken cancellationToken)
    {
        if (options.MikroTikBaseUrl is null)
        {
            return RouterOsSpikeResult.Failure(503, "MIKROTIK_BASE_URL is not configured.");
        }

        try
        {
            using var response = await httpClient.GetAsync("rest/system/resource", cancellationToken);
            var payload = await ReadPayloadAsync(response, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return RouterOsSpikeResult.Failure(
                    (int)response.StatusCode,
                    $"RouterOS version request failed with HTTP {(int)response.StatusCode}.",
                    payload);
            }

            if (!TryFindStringProperty(payload, "version", out var version))
            {
                return RouterOsSpikeResult.Failure(502, "RouterOS did not return a version value.", payload);
            }

            if (!RouterOsVersion.TryParse(version, out var parsedVersion))
            {
                return RouterOsSpikeResult.Failure(502, $"RouterOS returned an unrecognized version '{version}'.", payload);
            }

            if (!parsedVersion.IsSupported)
            {
                return RouterOsSpikeResult.Failure(
                    409,
                    $"RouterOS {version} is unsupported. NetWatch requires RouterOS v7 or later.",
                    payload,
                    version);
            }

            logger.LogInformation("Connected to supported RouterOS version {RouterOsVersion}", version);
            return RouterOsSpikeResult.Success(payload, version);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "RouterOS version probe failed");
            return RouterOsSpikeResult.Failure(503, $"RouterOS version probe failed: {exception.Message}");
        }
    }

    public async Task<RouterOsSpikeResult> GetDhcpLeasesAsync(CancellationToken cancellationToken)
    {
        var versionResult = await GetSupportedVersionAsync(cancellationToken);
        if (!versionResult.IsSuccess)
        {
            return versionResult;
        }

        return await GetAsync("rest/ip/dhcp-server/lease", versionResult.RouterOsVersion, cancellationToken);
    }

    public async Task<RouterOsSpikeResult> ProbeTorchAsync(CancellationToken cancellationToken)
    {
        var versionResult = await GetSupportedVersionAsync(cancellationToken);
        if (!versionResult.IsSuccess)
        {
            return versionResult;
        }

        var requestPayload = JsonSerializer.Serialize(
            new TorchRequest(options.TorchInterface, "1s", string.Empty),
            NetWatch.Server.Serialization.NetWatchJsonContext.Default.TorchRequest);
        using var content = new StringContent(requestPayload, Encoding.UTF8, "application/json");

        try
        {
            using var response = await httpClient.PostAsync("rest/tool/torch", content, cancellationToken);
            var payload = await ReadPayloadAsync(response, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return RouterOsSpikeResult.Failure(
                    (int)response.StatusCode,
                    $"RouterOS Torch request failed with HTTP {(int)response.StatusCode}. Verify the 'sniff' permission and interface name.",
                    payload,
                    versionResult.RouterOsVersion);
            }

            return RouterOsSpikeResult.Success(payload, versionResult.RouterOsVersion);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "RouterOS Torch probe failed");
            return RouterOsSpikeResult.Failure(503, $"RouterOS Torch probe failed: {exception.Message}");
        }
    }

    public void Dispose()
    {
        httpClient.Dispose();
    }

    private async Task<RouterOsSpikeResult> GetAsync(
        string path,
        string? version,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(path, cancellationToken);
            var payload = await ReadPayloadAsync(response, cancellationToken);
            return response.IsSuccessStatusCode ?
                RouterOsSpikeResult.Success(payload, version) :
                RouterOsSpikeResult.Failure(
                    (int)response.StatusCode,
                    $"RouterOS request '{path}' failed with HTTP {(int)response.StatusCode}.",
                    payload,
                    version);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "RouterOS request {Path} failed", path);
            return RouterOsSpikeResult.Failure(503, $"RouterOS request '{path}' failed: {exception.Message}");
        }
    }

    private static async Task<JsonElement?> ReadPayloadAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength == 0)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return document.RootElement.Clone();
    }

    private static bool TryFindStringProperty(JsonElement? payload, string propertyName, out string value)
    {
        value = string.Empty;
        if (payload is null)
        {
            return false;
        }

        var element = payload.Value;
        if (element.ValueKind == JsonValueKind.Array && element.GetArrayLength() > 0)
        {
            element = element[0];
        }

        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return value.Length > 0;
    }
}

internal readonly record struct RouterOsVersion(int Major)
{
    public bool IsSupported => Major >= 7;

    public static bool TryParse(string value, out RouterOsVersion version)
    {
        version = default;
        var separator = value.IndexOfAny(['.', ' ', '-']);
        var majorText = separator < 0 ? value : value[..separator];
        if (!int.TryParse(majorText, out var major) || major < 0)
        {
            return false;
        }

        version = new RouterOsVersion(major);
        return true;
    }
}

internal sealed record RouterOsSpikeResult(
    bool IsSuccess,
    int StatusCode,
    string? Error,
    string? RouterOsVersion,
    JsonElement? Payload)
{
    public static RouterOsSpikeResult Success(JsonElement? payload, string? version) =>
        new(true, 200, null, version, payload);

    public static RouterOsSpikeResult Failure(
        int statusCode,
        string error,
        JsonElement? payload = null,
        string? version = null) =>
        new(false, statusCode, error, version, payload);
}

internal sealed record TorchRequest(
    [property: System.Text.Json.Serialization.JsonPropertyName("interface")] string Interface,
    [property: System.Text.Json.Serialization.JsonPropertyName("duration")] string Duration,
    [property: System.Text.Json.Serialization.JsonPropertyName("once")] string Once);
