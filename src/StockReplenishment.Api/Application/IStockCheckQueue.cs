namespace StockReplenishment.Api.Application;

/// <summary>Hands request ids over to the background stock check worker.</summary>
public interface IStockCheckQueue
{
    ValueTask EnqueueAsync(int requestId, CancellationToken cancellationToken = default);

    IAsyncEnumerable<int> DequeueAllAsync(CancellationToken cancellationToken);
}
