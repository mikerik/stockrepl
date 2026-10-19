using System.ComponentModel.DataAnnotations;

namespace StockReplenishment.Contracts.Requests;

/// <summary>Filter, sorting and paging parameters for the request list (bound from the query string).</summary>
public sealed class RequestQuery
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    /// <summary>Upper bound that keeps (Page - 1) * PageSize far from integer overflow.</summary>
    public const int MaxPage = 1_000_000;

    public RequestStatus? Status { get; set; }

    public RequestPriority? Priority { get; set; }

    public int? LocationId { get; set; }

    /// <summary>Matches request number, requester or article number.</summary>
    [MaxLength(100)]
    public string? Search { get; set; }

    public RequestSortOrder Sort { get; set; } = RequestSortOrder.NewestFirst;

    [Range(1, MaxPage)]
    public int Page { get; set; } = 1;

    [Range(1, MaxPageSize)]
    public int PageSize { get; set; } = DefaultPageSize;
}
