using System.Text.Json.Serialization;

namespace StockReplenishment.Contracts.Requests;

[JsonConverter(typeof(JsonStringEnumConverter<RequestSortOrder>))]
public enum RequestSortOrder
{
    NewestFirst,
    OldestFirst,
    PriorityThenNewest
}
