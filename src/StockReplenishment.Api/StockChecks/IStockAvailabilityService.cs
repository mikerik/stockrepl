namespace StockReplenishment.Api.StockChecks;

/// <summary>Client for the (slow) external warehouse stock availability system.</summary>
public interface IStockAvailabilityService
{
    Task<IReadOnlyList<StockAvailability>> GetAvailabilityAsync(
        IReadOnlyCollection<string> articleNumbers,
        CancellationToken cancellationToken);
}

public sealed record StockAvailability(string ArticleNumber, decimal AvailableQuantity);

public sealed class StockServiceUnavailableException(string message) : Exception(message);
