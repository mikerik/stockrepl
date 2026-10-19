using Microsoft.EntityFrameworkCore;
using StockReplenishment.Api.Domain;
using StockReplenishment.Api.Tests.Support;
using StockReplenishment.Contracts.Requests;

namespace StockReplenishment.Api.Tests.Data;

[TestFixture]
public class AppDbContextTests
{
    private TestDatabase _database = null!;

    [SetUp]
    public void SetUp() => _database = new TestDatabase();

    [TearDown]
    public void TearDown() => _database.Dispose();

    [Test]
    public async Task SaveChanges_ConcurrentUpdatesOfSameRequest_SecondWriterGetsConcurrencyException()
    {
        int id;
        await using (var db = _database.CreateContext())
        {
            var request = ReplenishmentRequest.CreateDraft(
                await db.Locations.FirstAsync(), RequestPriority.Normal, null,
                [new RequestLineSpec(await db.Materials.FirstAsync(), 5)], TestData.Worker, TestData.Now);
            request.Submit(TestData.Worker, TestData.Now);
            db.Requests.Add(request);
            await db.SaveChangesAsync();
            id = request.Id;
        }

        await using var first = _database.CreateContext();
        await using var second = _database.CreateContext();
        var firstCopy = await first.Requests.Include(r => r.Lines).ThenInclude(l => l.Material).SingleAsync(r => r.Id == id);
        var secondCopy = await second.Requests.SingleAsync(r => r.Id == id);

        firstCopy.CompleteStockCheck(new Dictionary<string, decimal>(), TestData.Now);
        await first.SaveChangesAsync();
        secondCopy.Reject(TestData.Reviewer, "Not needed", TestData.Now);

        Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        Assert.That(firstCopy.Version, Is.EqualTo(1));
    }

    [Test]
    public async Task Timestamps_AreReadBackAsUtc()
    {
        await using (var db = _database.CreateContext())
        {
            db.Requests.Add(ReplenishmentRequest.CreateDraft(
                await db.Locations.FirstAsync(), RequestPriority.Normal, null,
                [new RequestLineSpec(await db.Materials.FirstAsync(), 5)], TestData.Worker, TestData.Now));
            await db.SaveChangesAsync();
        }

        await using var reader = _database.CreateContext();
        var stored = await reader.Requests.SingleAsync();

        Assert.That(stored.CreatedAt.Kind, Is.EqualTo(DateTimeKind.Utc));
        Assert.That(stored.CreatedAt, Is.EqualTo(TestData.Now));
    }
}
