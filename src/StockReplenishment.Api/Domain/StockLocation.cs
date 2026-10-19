namespace StockReplenishment.Api.Domain;

/// <summary>A station / line-side storage location that can request replenishment.</summary>
public class StockLocation
{
    public int Id { get; set; }

    public required string Code { get; set; }

    public required string Name { get; set; }

    public required string Area { get; set; }
}
