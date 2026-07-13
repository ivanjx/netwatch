using Microsoft.AspNetCore.Http.Json;

using NetWatch.Server.Configuration;
using NetWatch.Server.Data;
using NetWatch.Server.FlowCollection;
using NetWatch.Server.MikroTik;
using NetWatch.Server.Serialization;

var builder = WebApplication.CreateSlimBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.ListenAnyIP(8080));

builder.Services.Configure<JsonOptions>(options =>
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, NetWatchJsonContext.Default));

var spikeOptions = SpikeOptions.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(spikeOptions);
builder.Services.AddSingleton<SqliteSpikeStore>();
builder.Services.AddSingleton<RouterOsSpikeClient>();
builder.Services.AddHostedService<NetFlowUdpListener>();

var app = builder.Build();

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.MapGet("/api/spikes/status", (SpikeOptions options) => new SpikeStatusResponse(
    "NetWatch",
    options.DatabasePath,
    $"{options.NetFlowListenAddress}:{options.NetFlowListenPort}",
    options.MikroTikBaseUrl is not null));

app.MapPost("/api/spikes/sqlite", async (SqliteSpikeStore store, CancellationToken cancellationToken) =>
{
    var result = await store.WriteAndReadAsync(cancellationToken);
    return Results.Ok(result);
});

app.MapGet("/api/spikes/mikrotik/version", async (RouterOsSpikeClient client, CancellationToken cancellationToken) =>
{
    var result = await client.GetSupportedVersionAsync(cancellationToken);
    return result.IsSuccess ?
        Results.Ok(result) :
        Results.Problem(result.Error, statusCode: result.StatusCode);
});

app.MapGet("/api/spikes/mikrotik/dhcp-leases", async (RouterOsSpikeClient client, CancellationToken cancellationToken) =>
{
    var result = await client.GetDhcpLeasesAsync(cancellationToken);
    return result.IsSuccess ?
        Results.Ok(result) :
        Results.Problem(result.Error, statusCode: result.StatusCode);
});

app.MapPost("/api/spikes/mikrotik/torch", async (RouterOsSpikeClient client, CancellationToken cancellationToken) =>
{
    var result = await client.ProbeTorchAsync(cancellationToken);
    return result.IsSuccess ?
        Results.Ok(result) :
        Results.Problem(result.Error, statusCode: result.StatusCode);
});

app.MapFallbackToFile("index.html");

app.Run();
