namespace StockReplenishment.Api.Domain;

public class Material
{
    public int Id { get; set; }

    public required string ArticleNumber { get; set; }

    public required string Description { get; set; }

    public required string UnitOfMeasure { get; set; }
}
