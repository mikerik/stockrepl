using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using StockReplenishment.Client;
using StockReplenishment.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddMudServices();
builder.Services.AddSingleton<UserSession>();
builder.Services.AddSingleton<RequestUpdatesListener>();
builder.Services.AddTransient<UserHeaderHandler>();
builder.Services
    .AddHttpClient<ReplenishmentApiClient>(client => client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress))
    .AddHttpMessageHandler<UserHeaderHandler>();

await builder.Build().RunAsync();
