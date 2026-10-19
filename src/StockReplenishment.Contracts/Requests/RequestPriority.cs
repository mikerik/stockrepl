using System.Text.Json.Serialization;

namespace StockReplenishment.Contracts.Requests;

[JsonConverter(typeof(JsonStringEnumConverter<RequestPriority>))]
public enum RequestPriority
{
    Low,
    Normal,
    Urgent
}
