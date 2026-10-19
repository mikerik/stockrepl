namespace StockReplenishment.Contracts.Catalog;

public sealed record LocationDto(int Id, string Code, string Name, string Area);

public sealed record MaterialDto(int Id, string ArticleNumber, string Description, string UnitOfMeasure);
