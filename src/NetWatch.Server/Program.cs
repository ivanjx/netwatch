using Microsoft.AspNetCore.Http.Json;

using NetWatch.Server.Configuration;
using NetWatch.Server.Data;
using NetWatch.Server.FlowCollection;
using NetWatch.Server.Health;
using NetWatch.Server.MikroTik;
using NetWatch.Server.Serialization;

var builder = WebApplication.CreateSlimBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.ListenAnyIP(8080));

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();

builder.Services.Configure<JsonOptions>(options =>
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, NetWatchJsonContext.Default));

var netWatchOptions = NetWatchOptions.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(netWatchOptions);
builder.Services.AddSingleton(new ApplicationState(DateTimeOffset.UtcNow));
builder.Services.AddSingleton<SqliteConnectionFactory>();
builder.Services.AddSingleton<MigrationRunner>();
builder.Services.AddSingleton<DatabaseInitializer>();
builder.Services.AddSingleton<SystemStatusRepository>();
builder.Services.AddSingleton<SystemStatusService>();
builder.Services.AddSingleton<RouterOsSpikeClient>();
builder.Services.AddHostedService<NetFlowUdpListener>();
builder.Services.AddHealthChecks().AddCheck<SqliteHealthCheck>("sqlite");

var app = builder.Build();

await app.Services.GetRequiredService<DatabaseInitializer>().InitializeAsync(app.Lifetime.ApplicationStopping);

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.MapHealthChecks("/api/health");
app.MapGet("/api/status", SystemStatusHandler.GetAsync);

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
