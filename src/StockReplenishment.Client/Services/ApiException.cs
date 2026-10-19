using System.Net;

namespace StockReplenishment.Client.Services;

/// <summary>An API call failed; carries the server's problem details in a user-presentable form.</summary>
public sealed class ApiException(HttpStatusCode statusCode, string message, IReadOnlyDictionary<string, string[]> errors)
    : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;

    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;

    public string DisplayMessage =>
        Errors.Count == 0
            ? Message
            : string.Join(" ", Errors.Values.SelectMany(e => e).Distinct());
}
