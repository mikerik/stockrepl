using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StockReplenishment.Api.StockChecks;
using StockReplenishment.Contracts.Common;

namespace StockReplenishment.Api.Tests.Api;

/// <summary>Runs the real application with its own database and an instant, always-available stock service.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public IStockAvailabilityService StockService { get; } = Substitute.For<IStockAvailabilityService>();

    public ApiFactory()
    {
        StockService.GetAvailabilityAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IReadOnlyCollection<string>>().Select(a => new StockAvailability(a, 1_000_000)).ToList());
    }

    public HttpClient CreateClientAs(string userName)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(ApiConventions.UserHeader, userName);
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Database:Name", $"ApiTests-{Guid.NewGuid():N}");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IStockAvailabilityService>();
            services.AddSingleton(StockService);
        });
    }
}
