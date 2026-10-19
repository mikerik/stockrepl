using Microsoft.EntityFrameworkCore;
using StockReplenishment.Api.Application;
using StockReplenishment.Api.Data;
using StockReplenishment.Api.Domain;
using StockReplenishment.Api.Tests.Support;
using StockReplenishment.Contracts.Requests;

namespace StockReplenishment.Api.Tests.Application;

[TestFixture]
public class ReplenishmentRequestServiceTests
{
    private TestDatabase _database = null!;
    private ICurrentUser _currentUser = null!;
    private IStockCheckQueue _queue = null!;
    private IRequestNotifier _notifier = null!;

    [SetUp]
    public void SetUp()
    {
        _database = new TestDatabase();
        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.UserName.Returns(TestData.Worker);
        _queue = Substitute.For<IStockCheckQueue>();
        _notifier = Substitute.For<IRequestNotifier>();
    }

    [TearDown]
    public void TearDown() => _database.Dispose();

    [Test]
    public async Task CreateAsync_ValidInput_PersistsDraftForCurrentUserAndNotifies()
    {
        var created = await RunAsync(s => s.CreateAsync(ValidInput(), CancellationToken.None));

        await using var db = _database.CreateContext();
        var stored = await db.Requests.Include(r => r.Lines).SingleAsync(r => r.Id == created.Id);
        Assert.Multiple(() =>
        {
            Assert.That(created.Status, Is.EqualTo(RequestStatus.Draft));
            Assert.That(created.RequestNumber, Is.EqualTo($"RR-{created.Id:D6}"));
            Assert.That(stored.RequestedBy, Is.EqualTo(TestData.Worker));
            Assert.That(stored.Lines, Has.Count.EqualTo(2));
        });
        await _notifier.Received(1).RequestUpdatedAsync(Arg.Is<ReplenishmentRequest>(r => r.Id == created.Id), Arg.Any<CancellationToken>());
    }

    [Test]
    public void CreateAsync_UnknownLocationAndArticle_ReportsBothErrors()
    {
        var input = ValidInput();
        input.LocationId = 999;
        input.Lines[0].ArticleNumber = "999-9999";

        var ex = Assert.ThrowsAsync<BusinessRuleException>(() => RunAsync(s => s.CreateAsync(input, CancellationToken.None)));

        Assert.That(ex!.Errors.Keys, Is.EquivalentTo(new[] { nameof(SaveRequestInput.LocationId), nameof(SaveRequestInput.Lines) }));
    }

    [Test]
    public async Task UpdateAsync_Draft_ReplacesLines()
    {
        var created = await RunAsync(s => s.CreateAsync(ValidInput(), CancellationToken.None));
        var update = ValidInput();
        update.Priority = RequestPriority.Urgent;
        update.Lines = [new RequestLineInput { ArticleNumber = "310-0007", RequestedQuantity = 2.5m }];

        var updated = await RunAsync(s => s.UpdateAsync(created.Id, update, CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(updated.Priority, Is.EqualTo(RequestPriority.Urgent));
            Assert.That(updated.Lines.Select(l => l.ArticleNumber), Is.EqualTo(new[] { "310-0007" }));
            Assert.That(updated.Version, Is.GreaterThan(created.Version));
        });
    }

    [Test]
    public async Task SubmitAsync_Draft_PersistsPendingCheckAndQueuesIt()
    {
        var created = await RunAsync(s => s.CreateAsync(ValidInput(), CancellationToken.None));

        var submitted = await RunAsync(s => s.SubmitAsync(created.Id, CancellationToken.None));

        Assert.That(submitted.Status, Is.EqualTo(RequestStatus.Submitted));
        Assert.That(submitted.StockCheckStatus, Is.EqualTo(StockCheckStatus.Pending));
        await _queue.Received(1).EnqueueAsync(created.Id, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SubmitAsync_Twice_ThrowsAndQueuesOnlyOnce()
    {
        var created = await RunAsync(s => s.CreateAsync(ValidInput(), CancellationToken.None));
        await RunAsync(s => s.SubmitAsync(created.Id, CancellationToken.None));

        Assert.ThrowsAsync<InvalidStateException>(() => RunAsync(s => s.SubmitAsync(created.Id, CancellationToken.None)));
        await _queue.Received(1).EnqueueAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public void SubmitAsync_UnknownRequest_ThrowsNotFound()
    {
        Assert.ThrowsAsync<NotFoundException>(() => RunAsync(s => s.SubmitAsync(12345, CancellationToken.None)));
        _queue.DidNotReceiveWithAnyArgs().EnqueueAsync(default, default);
    }

    [Test]
    public async Task DeleteAsync_Draft_RemovesRequestAndLines()
    {
        var created = await RunAsync(s => s.CreateAsync(ValidInput(), CancellationToken.None));

        await RunAsync(async s =>
        {
            await s.DeleteAsync(created.Id, CancellationToken.None);
            return 0;
        });

        await using var db = _database.CreateContext();
        Assert.That(await db.Requests.AnyAsync(), Is.False);
        Assert.That(await db.Set<ReplenishmentRequestLine>().AnyAsync(), Is.False);
    }

    [Test]
    public async Task FulfillAsync_ApprovedRequest_RecordsQuantitiesPerLine()
    {
        var approved = await CreateApprovedRequestAsync();
        var input = new FulfillRequestInput
        {
            Lines = approved.Lines.Select(l => new FulfilledLineInput { LineId = l.Id, FulfilledQuantity = l.RequestedQuantity - 1 }).ToList(),
        };

        var fulfilled = await RunAsync(s => s.FulfillAsync(approved.Id, input, CancellationToken.None));

        Assert.That(fulfilled.Status, Is.EqualTo(RequestStatus.Fulfilled));
        Assert.That(fulfilled.Lines.Select(l => l.FulfilledQuantity), Is.EqualTo(approved.Lines.Select(l => (decimal?)(l.RequestedQuantity - 1))));
        Assert.That(fulfilled.History.Select(h => h.ToStatus), Is.EqualTo(new[]
        {
            RequestStatus.Draft, RequestStatus.Submitted, RequestStatus.Approved, RequestStatus.Fulfilled,
        }));
    }

    [Test]
    public async Task FulfillAsync_MissingLine_ThrowsAndLeavesRequestApproved()
    {
        var approved = await CreateApprovedRequestAsync();
        var input = new FulfillRequestInput
        {
            Lines = [new FulfilledLineInput { LineId = approved.Lines[0].Id, FulfilledQuantity = 1 }],
        };

        Assert.ThrowsAsync<BusinessRuleException>(() => RunAsync(s => s.FulfillAsync(approved.Id, input, CancellationToken.None)));

        var reloaded = await RunAsync(s => s.GetAsync(approved.Id, CancellationToken.None));
        Assert.That(reloaded.Status, Is.EqualTo(RequestStatus.Approved));
    }

    [Test]
    public async Task FulfillAsync_MoreThanRequested_Throws()
    {
        var approved = await CreateApprovedRequestAsync();
        var input = new FulfillRequestInput
        {
            Lines = approved.Lines.Select(l => new FulfilledLineInput { LineId = l.Id, FulfilledQuantity = l.RequestedQuantity + 1 }).ToList(),
        };

        Assert.ThrowsAsync<BusinessRuleException>(() => RunAsync(s => s.FulfillAsync(approved.Id, input, CancellationToken.None)));
    }

    [Test]
    public async Task SearchAsync_FiltersByStatusPriorityAndLocation()
    {
        await CreateAsync(locationId: 1, RequestPriority.Urgent);
        var submittedUrgent = await CreateAsync(locationId: 1, RequestPriority.Urgent);
        await CreateAsync(locationId: 2, RequestPriority.Urgent);
        await CreateAsync(locationId: 1, RequestPriority.Low);
        await RunAsync(s => s.SubmitAsync(submittedUrgent.Id, CancellationToken.None));

        var byPriorityAndLocation = await SearchAsync(new RequestQuery { Priority = RequestPriority.Urgent, LocationId = 1 });
        var byStatus = await SearchAsync(new RequestQuery { Status = RequestStatus.Submitted });

        Assert.That(byPriorityAndLocation.TotalCount, Is.EqualTo(2));
        Assert.That(byStatus.Items.Select(i => i.Id), Is.EqualTo(new[] { submittedUrgent.Id }));
    }

    [Test]
    public async Task SearchAsync_Pages_ReturnTotalCountAndRequestedSlice()
    {
        var ids = new List<int>();
        for (var i = 0; i < 5; i++)
        {
            ids.Add((await CreateAsync(locationId: 1, RequestPriority.Normal)).Id);
        }

        var page2 = await SearchAsync(new RequestQuery { Page = 2, PageSize = 2, Sort = RequestSortOrder.OldestFirst });
        var page3 = await SearchAsync(new RequestQuery { Page = 3, PageSize = 2, Sort = RequestSortOrder.OldestFirst });

        Assert.Multiple(() =>
        {
            Assert.That(page2.TotalCount, Is.EqualTo(5));
            Assert.That(page2.TotalPages, Is.EqualTo(3));
            Assert.That(page2.Items.Select(i => i.Id), Is.EqualTo(ids.Skip(2).Take(2)));
            Assert.That(page3.Items.Select(i => i.Id), Is.EqualTo(ids.Skip(4)));
        });
    }

    [Test]
    public async Task SearchAsync_SortByPriority_PutsUrgentFirst()
    {
        await CreateAsync(locationId: 1, RequestPriority.Low);
        await CreateAsync(locationId: 1, RequestPriority.Urgent);
        await CreateAsync(locationId: 1, RequestPriority.Normal);

        var result = await SearchAsync(new RequestQuery { Sort = RequestSortOrder.PriorityThenNewest });

        Assert.That(result.Items.Select(i => i.Priority), Is.EqualTo(new[] { RequestPriority.Urgent, RequestPriority.Normal, RequestPriority.Low }));
    }

    [TestCase("RR-{0}")]
    [TestCase("rr-{0}")]
    public async Task SearchAsync_ByRequestNumber_FindsExactRequest(string format)
    {
        await CreateAsync(locationId: 1, RequestPriority.Low);
        var target = await CreateAsync(locationId: 1, RequestPriority.Low);

        var result = await SearchAsync(new RequestQuery { Search = string.Format(format, target.Id.ToString("D6")) });

        Assert.That(result.Items.Select(i => i.Id), Is.EqualTo(new[] { target.Id }));
    }

    [Test]
    public async Task SearchAsync_ByArticleText_MatchesLineMaterial()
    {
        var input = ValidInput();
        input.Lines = [new RequestLineInput { ArticleNumber = "310-0007", RequestedQuantity = 1 }];
        var withWire = await RunAsync(s => s.CreateAsync(input, CancellationToken.None));
        await CreateAsync(locationId: 1, RequestPriority.Low);

        var result = await SearchAsync(new RequestQuery { Search = "wire" });

        Assert.That(result.Items.Select(i => i.Id), Is.EqualTo(new[] { withWire.Id }));
    }

    private async Task<ReplenishmentRequestDto> CreateApprovedRequestAsync()
    {
        var created = await RunAsync(s => s.CreateAsync(ValidInput(), CancellationToken.None));
        await RunAsync(s => s.SubmitAsync(created.Id, CancellationToken.None));

        // Stand-in for the background worker.
        await using (var db = _database.CreateContext())
        {
            var request = await db.Requests.Include(r => r.Lines).ThenInclude(l => l.Material).SingleAsync(r => r.Id == created.Id);
            request.CompleteStockCheck(request.Lines.ToDictionary(l => l.Material.ArticleNumber, _ => 1000m), TestData.Now);
            await db.SaveChangesAsync();
        }

        _currentUser.UserName.Returns(TestData.Reviewer);
        return await RunAsync(s => s.ApproveAsync(created.Id, new ApproveRequestInput(), CancellationToken.None));
    }

    private Task<ReplenishmentRequestDto> CreateAsync(int locationId, RequestPriority priority)
    {
        var input = ValidInput();
        input.LocationId = locationId;
        input.Priority = priority;
        return RunAsync(s => s.CreateAsync(input, CancellationToken.None));
    }

    private Task<Contracts.Common.PagedResult<ReplenishmentRequestSummaryDto>> SearchAsync(RequestQuery query) =>
        RunAsync(s => s.SearchAsync(query, CancellationToken.None));

    /// <summary>Each call gets a fresh DbContext, like an HTTP request would.</summary>
    private async Task<T> RunAsync<T>(Func<ReplenishmentRequestService, Task<T>> action)
    {
        await using var db = _database.CreateContext();
        var service = new ReplenishmentRequestService(db, _currentUser, _queue, _notifier, TimeProvider.System);
        return await action(service);
    }

    private static SaveRequestInput ValidInput() => new()
    {
        LocationId = 1,
        Priority = RequestPriority.Normal,
        Notes = "Running low",
        Lines =
        [
            new RequestLineInput { ArticleNumber = "100-2045", RequestedQuantity = 100 },
            new RequestLineInput { ArticleNumber = "100-2046", RequestedQuantity = 100 },
        ],
    };
}
