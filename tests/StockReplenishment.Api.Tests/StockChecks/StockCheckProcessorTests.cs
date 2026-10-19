using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StockReplenishment.Api.Application;
using StockReplenishment.Api.Domain;
using StockReplenishment.Api.StockChecks;
using StockReplenishment.Api.Tests.Support;
using StockReplenishment.Contracts.Requests;

namespace StockReplenishment.Api.Tests.StockChecks;

[TestFixture]
public class StockCheckProcessorTests
{
    private TestDatabase _database = null!;
    private IStockAvailabilityService _stockService = null!;
    private IRequestNotifier _notifier = null!;
    private StockCheckOptions _options = null!;

    [SetUp]
    public void SetUp()
    {
        _database = new TestDatabase();
        _stockService = Substitute.For<IStockAvailabilityService>();
        _notifier = Substitute.For<IRequestNotifier>();
        _options = new StockCheckOptions
        {
            MaxAttempts = 3,
            AttemptTimeout = TimeSpan.FromSeconds(5),
            RetryBaseDelay = TimeSpan.Zero,
        };
    }

    [TearDown]
    public void TearDown() => _database.Dispose();

    [Test]
    public async Task ProcessAsync_ServiceAnswers_CompletesCheckAndNotifies()
    {
        var requestId = await CreateSubmittedRequestAsync(("100-2045", 100), ("100-2046", 100));
        _stockService.GetAvailabilityAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns([new StockAvailability("100-2045", 500), new StockAvailability("100-2046", 40)]);

        await ProcessAsync(requestId);

        var request = await LoadAsync(requestId);
        Assert.Multiple(() =>
        {
            Assert.That(request.StockCheckStatus, Is.EqualTo(StockCheckStatus.Completed));
            Assert.That(request.AllItemsAvailable, Is.False);
            Assert.That(request.Lines.Single(l => l.Material.ArticleNumber == "100-2046").AvailableQuantity, Is.EqualTo(40));
        });
        await _stockService.Received(1).GetAvailabilityAsync(
            Arg.Is<IReadOnlyCollection<string>>(a => a.Order().SequenceEqual(new[] { "100-2045", "100-2046" })),
            Arg.Any<CancellationToken>());
        await _notifier.Received(1).RequestUpdatedAsync(Arg.Is<ReplenishmentRequest>(r => r.Id == requestId), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProcessAsync_TransientFailure_RetriesAndCompletes()
    {
        var requestId = await CreateSubmittedRequestAsync(("100-2045", 10));
        _stockService.GetAvailabilityAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => throw new StockServiceUnavailableException("503"),
                _ => Task.FromResult<IReadOnlyList<StockAvailability>>([new StockAvailability("100-2045", 10)]));

        await ProcessAsync(requestId);

        var request = await LoadAsync(requestId);
        Assert.That(request.StockCheckStatus, Is.EqualTo(StockCheckStatus.Completed));
        Assert.That(request.AllItemsAvailable, Is.True);
        await _stockService.ReceivedWithAnyArgs(2).GetAvailabilityAsync(default!, default);
    }

    [Test]
    public async Task ProcessAsync_ServiceKeepsFailing_MarksCheckFailedAfterMaxAttempts()
    {
        var requestId = await CreateSubmittedRequestAsync(("100-2045", 10));
        _stockService.GetAvailabilityAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<StockAvailability>>(_ => throw new StockServiceUnavailableException("Service unavailable"));

        await ProcessAsync(requestId);

        var request = await LoadAsync(requestId);
        Assert.That(request.StockCheckStatus, Is.EqualTo(StockCheckStatus.Failed));
        Assert.That(request.StockCheckMessage, Does.Contain("3 attempt(s)").And.Contain("Service unavailable"));
        await _stockService.ReceivedWithAnyArgs(3).GetAvailabilityAsync(default!, default);
        await _notifier.ReceivedWithAnyArgs(1).RequestUpdatedAsync(default!, default);
    }

    [Test]
    public async Task ProcessAsync_ServiceDoesNotRespond_TimesOutAndFails()
    {
        _options.MaxAttempts = 2;
        _options.AttemptTimeout = TimeSpan.FromMilliseconds(50);
        var requestId = await CreateSubmittedRequestAsync(("100-2045", 10));
        _stockService.GetAvailabilityAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                await Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>());
                return (IReadOnlyList<StockAvailability>)[];
            });

        await ProcessAsync(requestId);

        var request = await LoadAsync(requestId);
        Assert.That(request.StockCheckStatus, Is.EqualTo(StockCheckStatus.Failed));
        Assert.That(request.StockCheckMessage, Does.Contain("did not respond"));
    }

    [Test]
    public async Task ProcessAsync_RequestRejectedWhileCheckRuns_DiscardsResult()
    {
        var requestId = await CreateSubmittedRequestAsync(("100-2045", 10));
        _stockService.GetAvailabilityAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                // A reviewer rejects the request while the slow external call is in flight.
                await using var db = _database.CreateContext();
                var request = await db.Requests.SingleAsync(r => r.Id == requestId);
                request.Reject(TestData.Reviewer, "Not needed anymore", TestData.Now);
                await db.SaveChangesAsync();
                return (IReadOnlyList<StockAvailability>)[new StockAvailability("100-2045", 10)];
            });

        await ProcessAsync(requestId);

        var reloaded = await LoadAsync(requestId);
        Assert.That(reloaded.Status, Is.EqualTo(RequestStatus.Rejected));
        Assert.That(reloaded.StockCheckStatus, Is.EqualTo(StockCheckStatus.Cancelled));
        Assert.That(reloaded.Lines.Single().AvailableQuantity, Is.Null);
        await _notifier.DidNotReceiveWithAnyArgs().RequestUpdatedAsync(default!, default);
    }

    [Test]
    public async Task ProcessAsync_RequestNotAwaitingCheck_DoesNotCallService()
    {
        int draftId;
        await using (var db = _database.CreateContext())
        {
            var draft = ReplenishmentRequest.CreateDraft(
                await db.Locations.FirstAsync(), RequestPriority.Normal, null,
                [new RequestLineSpec(await db.Materials.FirstAsync(), 1)], TestData.Worker, TestData.Now);
            db.Requests.Add(draft);
            await db.SaveChangesAsync();
            draftId = draft.Id;
        }

        await ProcessAsync(draftId);

        await _stockService.DidNotReceiveWithAnyArgs().GetAvailabilityAsync(default!, default);
    }

    private async Task ProcessAsync(int requestId)
    {
        await using var db = _database.CreateContext();
        var processor = new StockCheckProcessor(
            db, _stockService, _notifier, Options.Create(_options), TimeProvider.System, NullLogger<StockCheckProcessor>.Instance);
        await processor.ProcessAsync(requestId, CancellationToken.None);
    }

    private async Task<int> CreateSubmittedRequestAsync(params (string Article, decimal Quantity)[] lines)
    {
        await using var db = _database.CreateContext();
        var materials = await db.Materials.ToDictionaryAsync(m => m.ArticleNumber);
        var request = ReplenishmentRequest.CreateDraft(
            await db.Locations.FirstAsync(),
            RequestPriority.Normal,
            null,
            lines.Select(l => new RequestLineSpec(materials[l.Article], l.Quantity)).ToList(),
            TestData.Worker,
            TestData.Now);
        request.Submit(TestData.Worker, TestData.Now);
        db.Requests.Add(request);
        await db.SaveChangesAsync();
        return request.Id;
    }

    private async Task<ReplenishmentRequest> LoadAsync(int id)
    {
        await using var db = _database.CreateContext();
        return await db.Requests.Include(r => r.Lines).ThenInclude(l => l.Material).SingleAsync(r => r.Id == id);
    }
}
