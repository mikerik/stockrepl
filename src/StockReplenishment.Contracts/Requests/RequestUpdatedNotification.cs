namespace StockReplenishment.Contracts.Requests;

/// <summary>Pushed to connected clients whenever a request changes (including when a stock check finishes).</summary>
public sealed record RequestUpdatedNotification(int RequestId, RequestStatus Status, StockCheckStatus StockCheckStatus);
