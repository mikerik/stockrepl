using StockReplenishment.Api.Domain;

namespace StockReplenishment.Api.Application;

/// <summary>Pushes request changes to connected clients.</summary>
public interface IRequestNotifier
{
    /// <summary>Best effort: implementations must not throw, a failed notification must never fail the operation.</summary>
    Task RequestUpdatedAsync(ReplenishmentRequest request, CancellationToken cancellationToken = default);
}
