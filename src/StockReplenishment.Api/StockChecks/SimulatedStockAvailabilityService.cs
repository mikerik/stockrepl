using Microsoft.Extensions.Options;

namespace StockReplenishment.Api.StockChecks;

/// <summary>
/// Stand-in for the external warehouse system: answers after a random delay of several seconds and
/// occasionally fails. Availability is derived from the article number so results are repeatable.
/// </summary>
public sealed class SimulatedStockAvailabilityService(
    IOptions<SimulatedStockServiceOptions> options,
    TimeProvider timeProvider,
    ILogger<SimulatedStockAvailabilityService> logger) : IStockAvailabilityService
{
    public async Task<IReadOnlyList<StockAvailability>> GetAvailabilityAsync(
        IReadOnlyCollection<string> articleNumbers,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var latency = TimeSpan.FromMilliseconds(
            Random.Shared.Next((int)settings.MinLatency.TotalMilliseconds, (int)settings.MaxLatency.TotalMilliseconds + 1));

        logger.LogInformation("Simulated stock service answering {Count} article(s) in {Latency}", articleNumbers.Count, latency);
        await Task.Delay(latency, timeProvider, cancellationToken);

        if (Random.Shared.NextDouble() < settings.FailureRate)
        {
            throw new StockServiceUnavailableException("The warehouse stock service returned 503 Service Unavailable.");
        }

        return articleNumbers.Select(a => new StockAvailability(a, SimulatedQuantity(a))).ToList();
    }

    private static decimal SimulatedQuantity(string articleNumber)
    {
        // Stable across runs (string.GetHashCode is randomized per process).
        var hash = articleNumber.Aggregate(17, (acc, c) => unchecked((acc * 31) + c));
        var bucket = Math.Abs(hash % 100);

        // Roughly every fifth article is scarce, the rest are well stocked.
        return bucket < 20 ? bucket : 100 + (bucket * 25);
    }
}
