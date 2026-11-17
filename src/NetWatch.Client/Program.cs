using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

using NetWatch.Client;
using NetWatch.Client.State;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// Page state lives as long as its page component; system status is shared for the app's lifetime.
builder.Services.AddTransient<DashboardState>();
builder.Services.AddTransient<DevicesState>();
builder.Services.AddTransient<DeviceDetailsState>();
builder.Services.AddScoped<SystemStatusState>();

await builder.Build().RunAsync();
