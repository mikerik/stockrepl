using System.Net;
using System.Net.Http.Json;
using System.Text;
using StockReplenishment.Contracts.Catalog;
using StockReplenishment.Contracts.Common;
using StockReplenishment.Contracts.Requests;

namespace StockReplenishment.Client.Services;

/// <summary>Typed client for the stock replenishment REST API.</summary>
public sealed class ReplenishmentApiClient(HttpClient http)
{
    private const string RequestsPath = "api/replenishment-requests";

    public Task<List<LocationDto>> GetLocationsAsync(CancellationToken cancellationToken = default) =>
        SendAsync<List<LocationDto>>(HttpMethod.Get, "api/locations", null, cancellationToken);

    public Task<List<MaterialDto>> SearchMaterialsAsync(string? search, CancellationToken cancellationToken = default) =>
        SendAsync<List<MaterialDto>>(HttpMethod.Get, $"api/materials?limit=25&search={Uri.EscapeDataString(search ?? string.Empty)}", null, cancellationToken);

    public Task<PagedResult<ReplenishmentRequestSummaryDto>> SearchRequestsAsync(RequestQuery query, CancellationToken cancellationToken = default) =>
        SendAsync<PagedResult<ReplenishmentRequestSummaryDto>>(HttpMethod.Get, $"{RequestsPath}?{ToQueryString(query)}", null, cancellationToken);

    public Task<ReplenishmentRequestDto> GetRequestAsync(int id, CancellationToken cancellationToken = default) =>
        SendAsync<ReplenishmentRequestDto>(HttpMethod.Get, $"{RequestsPath}/{id}", null, cancellationToken);

    public Task<ReplenishmentRequestDto> CreateRequestAsync(SaveRequestInput input, CancellationToken cancellationToken = default) =>
        SendAsync<ReplenishmentRequestDto>(HttpMethod.Post, RequestsPath, input, cancellationToken);

    public Task<ReplenishmentRequestDto> UpdateRequestAsync(int id, SaveRequestInput input, CancellationToken cancellationToken = default) =>
        SendAsync<ReplenishmentRequestDto>(HttpMethod.Put, $"{RequestsPath}/{id}", input, cancellationToken);

    public async Task DeleteRequestAsync(int id, CancellationToken cancellationToken = default)
    {
        using var response = await http.DeleteAsync($"{RequestsPath}/{id}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<ReplenishmentRequestDto> SubmitRequestAsync(int id, CancellationToken cancellationToken = default) =>
        SendAsync<ReplenishmentRequestDto>(HttpMethod.Post, $"{RequestsPath}/{id}/submit", null, cancellationToken);

    public Task<ReplenishmentRequestDto> RetryStockCheckAsync(int id, CancellationToken cancellationToken = default) =>
        SendAsync<ReplenishmentRequestDto>(HttpMethod.Post, $"{RequestsPath}/{id}/stock-check", null, cancellationToken);

    public Task<ReplenishmentRequestDto> ApproveRequestAsync(int id, string? comment, CancellationToken cancellationToken = default) =>
        SendAsync<ReplenishmentRequestDto>(HttpMethod.Post, $"{RequestsPath}/{id}/approve", new ApproveRequestInput { Comment = comment }, cancellationToken);

    public Task<ReplenishmentRequestDto> RejectRequestAsync(int id, string reason, CancellationToken cancellationToken = default) =>
        SendAsync<ReplenishmentRequestDto>(HttpMethod.Post, $"{RequestsPath}/{id}/reject", new RejectRequestInput { Reason = reason }, cancellationToken);

    public Task<ReplenishmentRequestDto> FulfillRequestAsync(int id, FulfillRequestInput input, CancellationToken cancellationToken = default) =>
        SendAsync<ReplenishmentRequestDto>(HttpMethod.Post, $"{RequestsPath}/{id}/fulfill", input, cancellationToken);

    private async Task<T> SendAsync<T>(HttpMethod method, string uri, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, uri);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType());
        }

        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
            ?? throw new ApiException(response.StatusCode, "The server returned an empty response.", new Dictionary<string, string[]>());
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        ProblemResponse? problem = null;
        if (response.Content.Headers.ContentType?.MediaType is "application/problem+json" or "application/json")
        {
            try
            {
                problem = await response.Content.ReadFromJsonAsync<ProblemResponse>(cancellationToken);
            }
            catch (System.Text.Json.JsonException)
            {
                // Fall through to the generic message.
            }
        }

        var message = problem?.Detail ?? problem?.Title ?? DefaultMessage(response.StatusCode);
        throw new ApiException(response.StatusCode, message, problem?.Errors ?? new Dictionary<string, string[]>());
    }

    private static string DefaultMessage(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.NotFound => "The requested item was not found.",
        HttpStatusCode.Conflict => "The operation conflicts with the current state of the request.",
        >= HttpStatusCode.InternalServerError => "The server encountered an error. Please try again.",
        _ => $"The request failed ({(int)statusCode}).",
    };

    private static string ToQueryString(RequestQuery query)
    {
        var parameters = new List<KeyValuePair<string, string>>
        {
            new("page", query.Page.ToString()),
            new("pageSize", query.PageSize.ToString()),
            new("sort", query.Sort.ToString()),
        };
        if (query.Status is { } status)
        {
            parameters.Add(new("status", status.ToString()));
        }

        if (query.Priority is { } priority)
        {
            parameters.Add(new("priority", priority.ToString()));
        }

        if (query.LocationId is { } locationId)
        {
            parameters.Add(new("locationId", locationId.ToString()));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            parameters.Add(new("search", query.Search.Trim()));
        }

        var builder = new StringBuilder();
        foreach (var (key, value) in parameters)
        {
            builder.Append(builder.Length == 0 ? string.Empty : "&")
                .Append(key).Append('=').Append(Uri.EscapeDataString(value));
        }

        return builder.ToString();
    }

    private sealed record ProblemResponse(string? Title, string? Detail, int? Status, Dictionary<string, string[]>? Errors);
}
