namespace StockReplenishment.Api.Domain;

public class ReplenishmentRequestLine
{
    private ReplenishmentRequestLine()
    {
    }

    internal ReplenishmentRequestLine(Material material, decimal requestedQuantity)
    {
        Material = material;
        MaterialId = material.Id;
        RequestedQuantity = requestedQuantity;
    }

    public int Id { get; private set; }

    public int RequestId { get; private set; }

    public int MaterialId { get; private set; }

    public Material Material { get; private set; } = null!;

    public decimal RequestedQuantity { get; private set; }

    /// <summary>Quantity reported available by the external stock check; null until the check completes.</summary>
    public decimal? AvailableQuantity { get; private set; }

    public decimal? FulfilledQuantity { get; private set; }

    public bool IsAvailable => AvailableQuantity >= RequestedQuantity;

    internal void SetAvailableQuantity(decimal? quantity) => AvailableQuantity = quantity;

    internal void SetFulfilledQuantity(decimal quantity) => FulfilledQuantity = quantity;
}
