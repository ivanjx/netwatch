using Microsoft.AspNetCore.Http.Json;

using NetWatch.Server.Configuration;
using NetWatch.Server.Data;
using NetWatch.Server.Devices;
using NetWatch.Server.FlowCollection;
using NetWatch.Server.Health;
using NetWatch.Server.Live;
using NetWatch.Server.MikroTik;
using NetWatch.Server.Serialization;
using NetWatch.Server.Usage;

var builder = WebApplication.CreateSlimBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.ListenAnyIP(8080));

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();

builder.Services.Configure<JsonOptions>(options =>
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, NetWatchJsonContext.Default));

var netWatchOptions = NetWatchOptions.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(netWatchOptions);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new ApplicationState(DateTimeOffset.UtcNow));
builder.Services.AddSingleton<SqliteConnectionFactory>();
builder.Services.AddSingleton<MigrationRunner>();
builder.Services.AddSingleton<DatabaseInitializer>();
builder.Services.AddSingleton<SystemStatusRepository>();
builder.Services.AddSingleton<SystemStatusService>();
builder.Services.AddSingleton<MikroTikClient>();
builder.Services.AddSingleton<IMikroTikClient>(services => services.GetRequiredService<MikroTikClient>());
builder.Services.AddSingleton<DeviceRepository>();
builder.Services.AddSingleton<DeviceService>();
builder.Services.AddSingleton<DeviceSynchronizationService>();
builder.Services.AddSingleton<NetFlowChannel>();
builder.Services.AddSingleton<NetFlowDiagnostics>();
builder.Services.AddSingleton<NetFlowPacketProcessor>();
builder.Services.AddSingleton<UsageRepository>();
builder.Services.AddSingleton<DeviceAttributionService>();
builder.Services.AddSingleton<UsageAccumulator>();
builder.Services.AddSingleton<LiveTrafficTracker>();
builder.Services.AddSingleton<UsageMutationLock>();
builder.Services.AddSingleton<UnresolvedUsageReconciler>();
builder.Services.AddSingleton<UsageService>();
builder.Services.AddSingleton<UsageClearService>();
builder.Services.AddSingleton<CollectorDiagnosticsService>();
builder.Services.AddHostedService<DhcpSynchronizationWorker>();
builder.Services.AddHostedService<NetFlowUdpListener>();
builder.Services.AddHostedService<NetFlowProcessingWorker>();
builder.Services.AddHealthChecks().AddCheck<SqliteHealthCheck>("sqlite");

var app = builder.Build();

await app.Services.GetRequiredService<DatabaseInitializer>().InitializeAsync(app.Lifetime.ApplicationStopping);

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.MapHealthChecks("/api/health");
app.MapGet("/api/status", SystemStatusHandler.GetAsync);
app.MapGet("/api/diagnostics", CollectorDiagnosticsHandler.GetAsync);
app.MapGet("/api/devices", DeviceHandlers.ListAsync);
app.MapGet("/api/devices/{id}", DeviceHandlers.GetAsync);
app.MapPatch("/api/devices/{id}", DeviceHandlers.RenameAsync);
app.MapPost("/api/devices/manual", DeviceHandlers.CreateManualAsync);
app.MapGet("/api/usage/summary", UsageHandlers.GetSummaryAsync);
app.MapGet("/api/usage/devices", UsageHandlers.GetDevicesAsync);
app.MapGet("/api/usage/history", UsageHandlers.GetHistoryAsync);
app.MapGet("/api/live", LiveTrafficHandler.Get);
app.MapDelete("/api/usage/devices/{id}", UsageHandlers.ClearDeviceAsync);
app.MapDelete("/api/usage", UsageHandlers.ClearAllAsync);

app.MapFallbackToFile("index.html");

app.Run();
