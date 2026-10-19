using System.Threading.Channels;
using StockReplenishment.Api.Application;

namespace StockReplenishment.Api.StockChecks;

/// <summary>
/// In-process queue. The request's persisted Pending state is the source of truth, this channel only
/// signals the worker; a production deployment would use a durable broker or an outbox table instead.
/// </summary>
public sealed class StockCheckQueue : IStockCheckQueue
{
    private readonly Channel<int> _channel = Channel.CreateUnbounded<int>(new UnboundedChannelOptions { SingleReader = true });

    public ValueTask EnqueueAsync(int requestId, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(requestId, cancellationToken);

    public IAsyncEnumerable<int> DequeueAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
