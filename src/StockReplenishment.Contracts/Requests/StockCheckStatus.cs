using System.Text.Json.Serialization;

namespace StockReplenishment.Contracts.Requests;

/// <summary>State of the external stock availability check that runs after submission.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<StockCheckStatus>))]
public enum StockCheckStatus
{
    /// <summary>The request has not been submitted yet.</summary>
    NotStarted,

    /// <summary>The check is queued or in progress.</summary>
    Pending,

    /// <summary>The external service answered; per-line availability is known.</summary>
    Completed,

    /// <summary>The external service could not be reached after all retries. The check can be retried.</summary>
    Failed,

    /// <summary>The request was rejected before the check finished; any late result is discarded.</summary>
    Cancelled
}
