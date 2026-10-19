namespace StockReplenishment.Contracts.Requests;

public sealed record ReplenishmentRequestSummaryDto(
    int Id,
    string RequestNumber,
    int LocationId,
    string LocationCode,
    string LocationName,
    RequestPriority Priority,
    RequestStatus Status,
    StockCheckStatus StockCheckStatus,
    bool? AllItemsAvailable,
    int LineCount,
    string RequestedBy,
    DateTime CreatedAt,
    DateTime? SubmittedAt);

public sealed record ReplenishmentRequestDto(
    int Id,
    string RequestNumber,
    int LocationId,
    string LocationCode,
    string LocationName,
    RequestPriority Priority,
    RequestStatus Status,
    string? Notes,
    string RequestedBy,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? SubmittedAt,
    StockCheckStatus StockCheckStatus,
    DateTime? StockCheckCompletedAt,
    string? StockCheckMessage,
    bool? AllItemsAvailable,
    string? ReviewedBy,
    DateTime? ReviewedAt,
    string? RejectionReason,
    string? FulfilledBy,
    DateTime? FulfilledAt,
    int Version,
    IReadOnlyList<RequestLineDto> Lines,
    IReadOnlyList<StatusChangeDto> History);

public sealed record RequestLineDto(
    int Id,
    int MaterialId,
    string ArticleNumber,
    string Description,
    string UnitOfMeasure,
    decimal RequestedQuantity,
    decimal? AvailableQuantity,
    decimal? FulfilledQuantity)
{
    /// <summary>Null until the stock check has completed.</summary>
    public bool? IsAvailable => AvailableQuantity is null ? null : AvailableQuantity >= RequestedQuantity;
}

public sealed record StatusChangeDto(
    RequestStatus? FromStatus,
    RequestStatus ToStatus,
    string ChangedBy,
    DateTime ChangedAt,
    string? Comment);
