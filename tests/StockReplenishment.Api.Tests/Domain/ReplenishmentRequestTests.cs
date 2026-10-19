using StockReplenishment.Api.Domain;
using StockReplenishment.Api.Tests.Support;
using StockReplenishment.Contracts.Requests;
using static StockReplenishment.Api.Tests.Support.TestData;

namespace StockReplenishment.Api.Tests.Domain;

[TestFixture]
public class ReplenishmentRequestTests
{
    [Test]
    public void CreateDraft_WithValidInput_StartsAsDraftWithHistoryEntry()
    {
        var request = Draft(("100-0001", 10), ("100-0002", 5));

        Assert.Multiple(() =>
        {
            Assert.That(request.Status, Is.EqualTo(RequestStatus.Draft));
            Assert.That(request.StockCheckStatus, Is.EqualTo(StockCheckStatus.NotStarted));
            Assert.That(request.Lines, Has.Count.EqualTo(2));
            Assert.That(request.RequestedBy, Is.EqualTo(Worker));
            Assert.That(request.History.Single().ToStatus, Is.EqualTo(RequestStatus.Draft));
        });
    }

    [Test]
    public void CreateDraft_WithoutLines_Throws()
    {
        Assert.Throws<BusinessRuleException>(() =>
            ReplenishmentRequest.CreateDraft(Location(), RequestPriority.Low, null, [], Worker, Now));
    }

    [Test]
    public void CreateDraft_WithDuplicateArticles_Throws()
    {
        var material = Material(1, "100-0001");

        var ex = Assert.Throws<BusinessRuleException>(() => ReplenishmentRequest.CreateDraft(
            Location(), RequestPriority.Low, null, [new(material, 1), new(material, 2)], Worker, Now));

        Assert.That(ex!.Message, Does.Contain("100-0001"));
    }

    [TestCase(0)]
    [TestCase(-5)]
    public void CreateDraft_WithNonPositiveQuantity_Throws(decimal quantity)
    {
        Assert.Throws<BusinessRuleException>(() => Draft(("100-0001", quantity)));
    }

    [Test]
    public void CreateDraft_TrimsNotesAndTreatsWhitespaceAsEmpty()
    {
        var request = ReplenishmentRequest.CreateDraft(
            Location(), RequestPriority.Low, "   ", [new(Material(1, "100-0001"), 1)], Worker, Now);

        Assert.That(request.Notes, Is.Null);
    }

    [Test]
    public void Submit_FromDraft_MovesToSubmittedAndStartsStockCheck()
    {
        var request = Draft();

        request.Submit(Worker, Now);

        Assert.Multiple(() =>
        {
            Assert.That(request.Status, Is.EqualTo(RequestStatus.Submitted));
            Assert.That(request.StockCheckStatus, Is.EqualTo(StockCheckStatus.Pending));
            Assert.That(request.SubmittedAt, Is.EqualTo(Now));
            Assert.That(request.IsAwaitingStockCheck, Is.True);
            Assert.That(request.History.Last().FromStatus, Is.EqualTo(RequestStatus.Draft));
        });
    }

    [Test]
    public void Submit_WhenAlreadySubmitted_ThrowsInvalidState()
    {
        var request = Draft();
        request.Submit(Worker, Now);

        Assert.Throws<InvalidStateException>(() => request.Submit(Worker, Now));
    }

    [Test]
    public void UpdateDraft_AfterSubmission_ThrowsInvalidState()
    {
        var request = Draft();
        request.Submit(Worker, Now);

        Assert.Throws<InvalidStateException>(() =>
            request.UpdateDraft(Location(), RequestPriority.Urgent, null, [new(Material(1, "100-0001"), 1)], Now));
    }

    [Test]
    public void EnsureCanBeDeleted_WhenSubmitted_ThrowsInvalidState()
    {
        var request = Draft();
        request.Submit(Worker, Now);

        Assert.Throws<InvalidStateException>(request.EnsureCanBeDeleted);
    }

    [Test]
    public void CompleteStockCheck_RecordsAvailabilityPerLine()
    {
        var request = Draft(("100-0001", 10), ("100-0002", 50));
        request.Submit(Worker, Now);

        request.CompleteStockCheck(new Dictionary<string, decimal> { ["100-0001"] = 10, ["100-0002"] = 20 }, Now);

        Assert.Multiple(() =>
        {
            Assert.That(request.StockCheckStatus, Is.EqualTo(StockCheckStatus.Completed));
            Assert.That(request.AllItemsAvailable, Is.False);
            Assert.That(request.Lines[0].IsAvailable, Is.True);
            Assert.That(request.Lines[1].AvailableQuantity, Is.EqualTo(20));
            Assert.That(request.Lines[1].IsAvailable, Is.False);
            Assert.That(request.StockCheckMessage, Is.EqualTo("1 of 2 item(s) have insufficient stock."));
        });
    }

    [Test]
    public void CompleteStockCheck_ArticleMissingFromResult_IsTreatedAsUnavailable()
    {
        var request = Draft(("100-0001", 10));
        request.Submit(Worker, Now);

        request.CompleteStockCheck(new Dictionary<string, decimal>(), Now);

        Assert.That(request.Lines[0].AvailableQuantity, Is.Zero);
        Assert.That(request.AllItemsAvailable, Is.False);
    }

    [Test]
    public void CompleteStockCheck_WhenNotAwaitingCheck_ThrowsInvalidState()
    {
        var request = Draft();

        Assert.Throws<InvalidStateException>(() => request.CompleteStockCheck(new Dictionary<string, decimal>(), Now));
    }

    [Test]
    public void RetryStockCheck_AfterFailure_ReturnsToPending()
    {
        var request = Draft();
        request.Submit(Worker, Now);
        request.FailStockCheck("Service unavailable", Now);

        request.RetryStockCheck(Now);

        Assert.That(request.StockCheckStatus, Is.EqualTo(StockCheckStatus.Pending));
        Assert.That(request.StockCheckMessage, Is.Null);
    }

    [Test]
    public void RetryStockCheck_WhenCheckDidNotFail_ThrowsInvalidState()
    {
        var request = SubmittedWithCompletedCheck();

        Assert.Throws<InvalidStateException>(() => request.RetryStockCheck(Now));
    }

    [Test]
    public void Approve_WhileStockCheckPending_ThrowsInvalidState()
    {
        var request = Draft();
        request.Submit(Worker, Now);

        Assert.Throws<InvalidStateException>(() => request.Approve(Reviewer, null, Now));
    }

    [Test]
    public void Approve_ByRequester_ThrowsInvalidState()
    {
        var request = SubmittedWithCompletedCheck();

        Assert.Throws<InvalidStateException>(() => request.Approve(Worker.ToUpperInvariant(), null, Now));
    }

    [Test]
    public void Approve_AfterCompletedCheck_RecordsReviewer()
    {
        var request = SubmittedWithCompletedCheck();

        request.Approve(Reviewer, "  Looks good ", Now);

        Assert.Multiple(() =>
        {
            Assert.That(request.Status, Is.EqualTo(RequestStatus.Approved));
            Assert.That(request.ReviewedBy, Is.EqualTo(Reviewer));
            Assert.That(request.History.Last().Comment, Is.EqualTo("Looks good"));
        });
    }

    [TestCase("")]
    [TestCase("   ")]
    public void Reject_WithoutReason_Throws(string reason)
    {
        var request = SubmittedWithCompletedCheck();

        Assert.Throws<BusinessRuleException>(() => request.Reject(Reviewer, reason, Now));
        Assert.That(request.Status, Is.EqualTo(RequestStatus.Submitted));
    }

    [Test]
    public void Reject_WhileStockCheckPending_CancelsTheCheck()
    {
        var request = Draft();
        request.Submit(Worker, Now);

        request.Reject(Reviewer, "Duplicate", Now);

        Assert.Multiple(() =>
        {
            Assert.That(request.Status, Is.EqualTo(RequestStatus.Rejected));
            Assert.That(request.RejectionReason, Is.EqualTo("Duplicate"));
            Assert.That(request.StockCheckStatus, Is.EqualTo(StockCheckStatus.Cancelled));
            Assert.That(request.IsAwaitingStockCheck, Is.False);
        });
    }

    [Test]
    public void Reject_ApprovedRequest_ThrowsInvalidState()
    {
        var request = SubmittedWithCompletedCheck();
        request.Approve(Reviewer, null, Now);

        Assert.Throws<InvalidStateException>(() => request.Reject(Reviewer, "Too late", Now));
    }

    [Test]
    public void Fulfill_WhenNotApproved_ThrowsInvalidState()
    {
        var request = SubmittedWithCompletedCheck();

        Assert.Throws<InvalidStateException>(() => request.Fulfill("Warehouse", new Dictionary<int, decimal>(), Now));
    }
}
