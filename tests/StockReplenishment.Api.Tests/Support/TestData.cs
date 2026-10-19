using StockReplenishment.Api.Domain;
using StockReplenishment.Contracts.Requests;

namespace StockReplenishment.Api.Tests.Support;

internal static class TestData
{
    public const string Worker = "Anna Lindqvist";
    public const string Reviewer = "Erik Johansson";

    public static readonly DateTime Now = new(2026, 10, 16, 8, 0, 0, DateTimeKind.Utc);

    public static StockLocation Location(int id = 1) =>
        new() { Id = id, Code = $"LOC-{id}", Name = $"Location {id}", Area = "Assembly" };

    public static Material Material(int id, string articleNumber) =>
        new() { Id = id, ArticleNumber = articleNumber, Description = $"Material {articleNumber}", UnitOfMeasure = "pcs" };

    public static ReplenishmentRequest Draft(params (string Article, decimal Quantity)[] lines)
    {
        var specs = lines.Length == 0
            ? [new RequestLineSpec(Material(1, "100-0001"), 10)]
            : lines.Select((l, i) => new RequestLineSpec(Material(i + 1, l.Article), l.Quantity)).ToList();

        return ReplenishmentRequest.CreateDraft(Location(), RequestPriority.Normal, null, specs, Worker, Now);
    }

    public static ReplenishmentRequest SubmittedWithCompletedCheck()
    {
        var request = Draft();
        request.Submit(Worker, Now);
        request.CompleteStockCheck(new Dictionary<string, decimal> { ["100-0001"] = 100 }, Now);
        return request;
    }
}
