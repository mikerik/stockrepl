using Microsoft.AspNetCore.SignalR;
using StockReplenishment.Api.Application;
using StockReplenishment.Api.Domain;
using StockReplenishment.Contracts.Common;
using StockReplenishment.Contracts.Requests;

namespace StockReplenishment.Api.Infrastructure;

public sealed class SignalRRequestNotifier(
    IHubContext<RequestsHub> hubContext,
    ILogger<SignalRRequestNotifier> logger) : IRequestNotifier
{
    public async Task RequestUpdatedAsync(ReplenishmentRequest request, CancellationToken cancellationToken = default)
    {
        var notification = new RequestUpdatedNotification(request.Id, request.Status, request.StockCheckStatus);
        try
        {
            await hubContext.Clients.All.SendAsync(ApiConventions.RequestUpdatedEvent, notification, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Clients can always re-read the request; a lost push is not worth failing the operation for.
            logger.LogWarning(ex, "Failed to push update for request {RequestId}", request.Id);
        }
    }
}
