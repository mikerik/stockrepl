using StockReplenishment.Api.Domain;
using StockReplenishment.Contracts.Catalog;
using StockReplenishment.Contracts.Requests;

namespace StockReplenishment.Api.Application;

internal static class ReplenishmentRequestMappings
{
    public static ReplenishmentRequestDto ToDto(this ReplenishmentRequest request) => new(
        request.Id,
        request.RequestNumber,
        request.LocationId,
        request.Location.Code,
        request.Location.Name,
        request.Priority,
        request.Status,
        request.Notes,
        request.RequestedBy,
        request.CreatedAt,
        request.UpdatedAt,
        request.SubmittedAt,
        request.StockCheckStatus,
        request.StockCheckCompletedAt,
        request.StockCheckMessage,
        request.AllItemsAvailable,
        request.ReviewedBy,
        request.ReviewedAt,
        request.RejectionReason,
        request.FulfilledBy,
        request.FulfilledAt,
        request.Version,
        request.Lines
            .OrderBy(l => l.Id)
            .Select(l => new RequestLineDto(
                l.Id,
                l.MaterialId,
                l.Material.ArticleNumber,
                l.Material.Description,
                l.Material.UnitOfMeasure,
                l.RequestedQuantity,
                l.AvailableQuantity,
                l.FulfilledQuantity))
            .ToList(),
        request.History
            .OrderBy(h => h.ChangedAt)
            .ThenBy(h => h.Id)
            .Select(h => new StatusChangeDto(h.FromStatus, h.ToStatus, h.ChangedBy, h.ChangedAt, h.Comment))
            .ToList());

    public static LocationDto ToDto(this StockLocation location) =>
        new(location.Id, location.Code, location.Name, location.Area);

    public static MaterialDto ToDto(this Material material) =>
        new(material.Id, material.ArticleNumber, material.Description, material.UnitOfMeasure);
}
