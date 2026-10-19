using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StockReplenishment.Api.Application;
using StockReplenishment.Api.Data;
using StockReplenishment.Contracts.Requests;

namespace StockReplenishment.Api.StockChecks;

/// <summary>
/// Consumes the stock check queue in the background so the submit endpoint can return 202 Accepted
/// immediately. Up to <see cref="StockCheckOptions.MaxConcurrentChecks"/> checks run in parallel.
/// </summary>
public sealed class StockCheckWorker(
    IStockCheckQueue queue,
    IServiceScopeFactory scopeFactory,
    IOptions<StockCheckOptions> options,
    ILogger<StockCheckWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RequeuePendingChecksAsync(stoppingToken);

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = options.Value.MaxConcurrentChecks,
                CancellationToken = stoppingToken,
            };
            await Parallel.ForEachAsync(queue.DequeueAllAsync(stoppingToken), parallelOptions, ProcessAsync);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down; unfinished checks stay Pending and are re-queued on the next start.
        }
    }

    private async ValueTask ProcessAsync(int requestId, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var processor = scope.ServiceProvider.GetRequiredService<StockCheckProcessor>();
            await processor.ProcessAsync(requestId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Never let one bad request stop the worker.
            logger.LogError(ex, "Unexpected error while processing the stock check for request {RequestId}", requestId);
        }
    }

    private async Task RequeuePendingChecksAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var pendingIds = await db.Requests
            .Where(r => r.Status == RequestStatus.Submitted && r.StockCheckStatus == StockCheckStatus.Pending)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        foreach (var id in pendingIds)
        {
            await queue.EnqueueAsync(id, cancellationToken);
        }

        if (pendingIds.Count > 0)
        {
            logger.LogInformation("Re-queued {Count} pending stock check(s)", pendingIds.Count);
        }
    }
}
