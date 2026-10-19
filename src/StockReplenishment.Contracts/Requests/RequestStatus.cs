using System.Text.Json.Serialization;

namespace StockReplenishment.Contracts.Requests;

/// <summary>Lifecycle: Draft → Submitted → Approved → Fulfilled, or Submitted → Rejected.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RequestStatus>))]
public enum RequestStatus
{
    Draft,
    Submitted,
    Approved,
    Rejected,
    Fulfilled
}
