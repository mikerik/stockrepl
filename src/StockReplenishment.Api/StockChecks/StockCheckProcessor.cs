using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StockReplenishment.Api.Application;
using StockReplenishment.Api.Data;
using StockReplenishment.Contracts.Requests;

namespace StockReplenishment.Api.StockChecks;

/// <summary>Runs the external stock check for one request and records the outcome.</summary>
public sealed class StockCheckProcessor(
    AppDbContext db,
    IStockAvailabilityService stockService,
    IRequestNotifier notifier,
    IOptions<StockCheckOptions> options,
    TimeProvider timeProvider,
    ILogger<StockCheckProcessor> logger)
{
    private const int MaxSaveAttempts = 3;

    public async Task ProcessAsync(int requestId, CancellationToken cancellationToken)
    {
        // Read only what the external call needs; nothing is tracked while the slow call is in flight.
        var articleNumbers = await db.Requests
            .AsNoTracking()
            .Where(r => r.Id == requestId
                        && r.Status == RequestStatus.Submitted
                        && r.StockCheckStatus == StockCheckStatus.Pending)
            .SelectMany(r => r.Lines.Select(l => l.Material.ArticleNumber))
            .ToListAsync(cancellationToken);

        if (articleNumbers.Count == 0)
        {
            logger.LogInformation("Request {RequestId} is no longer awaiting a stock check; skipping", requestId);
            return;
        }

        var outcome = await QueryWithRetriesAsync(requestId, articleNumbers, cancellationToken);
        await RecordOutcomeAsync(requestId, outcome, cancellationToken);
    }

    private async Task<CheckOutcome> QueryWithRetriesAsync(int requestId, IReadOnlyCollection<string> articleNumbers, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        string? lastError = null;

        for (var attempt = 1; attempt <= settings.MaxAttempts; attempt++)
        {
            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptCts.CancelAfter(settings.AttemptTimeout);

            try
            {
                var result = await stockService.GetAvailabilityAsync(articleNumbers, attemptCts.Token);
                return CheckOutcome.Success(result);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                lastError = $"The stock service did not respond within {settings.AttemptTimeout.TotalSeconds:0} seconds.";
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex.Message;
            }

            logger.LogWarning("Stock check for request {RequestId} failed (attempt {Attempt}/{MaxAttempts}): {Error}",
                requestId, attempt, settings.MaxAttempts, lastError);

            if (attempt < settings.MaxAttempts)
            {
                var delay = settings.RetryBaseDelay * Math.Pow(2, attempt - 1);
                await Task.Delay(delay, timeProvider, cancellationToken);
            }
        }

        return CheckOutcome.Failure($"Stock check failed after {settings.MaxAttempts} attempt(s): {lastError}");
    }

    private async Task RecordOutcomeAsync(int requestId, CheckOutcome outcome, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxSaveAttempts; attempt++)
        {
            db.ChangeTracker.Clear();
            var request = await db.Requests
                .Include(r => r.Location)
                .Include(r => r.Lines).ThenInclude(l => l.Material)
                .SingleOrDefaultAsync(r => r.Id == requestId, cancellationToken);

            // The request may have been rejected or deleted while the external call was running.
            if (request is null || !request.IsAwaitingStockCheck)
            {
                logger.LogInformation("Request {RequestId} changed during the stock check; discarding the result", requestId);
                return;
            }

            var now = timeProvider.GetUtcNow().UtcDateTime;
            if (outcome.Availability is { } availability)
            {
                request.CompleteStockCheck(availability, now);
            }
            else
            {
                request.FailStockCheck(outcome.Error!, now);
            }

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxSaveAttempts)
            {
                logger.LogInformation("Concurrent update of request {RequestId}; reloading and retrying", requestId);
                continue;
            }

            logger.LogInformation("Stock check for request {RequestId} finished: {Status} ({Message})",
                requestId, request.StockCheckStatus, request.StockCheckMessage);
            await notifier.RequestUpdatedAsync(request, cancellationToken);
            return;
        }
    }

    private sealed record CheckOutcome(IReadOnlyDictionary<string, decimal>? Availability, string? Error)
    {
        public static CheckOutcome Success(IEnumerable<StockAvailability> result) =>
            new(result
                    .GroupBy(r => r.ArticleNumber, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.Sum(r => r.AvailableQuantity), StringComparer.OrdinalIgnoreCase),
                null);

        public static CheckOutcome Failure(string error) => new(null, error);
    }
}
