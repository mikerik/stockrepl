using StockReplenishment.Contracts.Requests;

namespace StockReplenishment.Api.Domain;

public sealed record RequestLineSpec(Material Material, decimal RequestedQuantity);

/// <summary>
/// Aggregate root for a stock replenishment request. All state transitions go through methods on this
/// class so the workflow rules live in exactly one place.
/// </summary>
public class ReplenishmentRequest
{
    private readonly List<ReplenishmentRequestLine> _lines = [];
    private readonly List<RequestStatusChange> _history = [];

    private ReplenishmentRequest()
    {
    }

    public int Id { get; private set; }

    public string RequestNumber => FormatRequestNumber(Id);

    public int LocationId { get; private set; }

    public StockLocation Location { get; private set; } = null!;

    public RequestPriority Priority { get; private set; }

    public RequestStatus Status { get; private set; }

    public string? Notes { get; private set; }

    public string RequestedBy { get; private set; } = string.Empty;

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    public DateTime? SubmittedAt { get; private set; }

    public StockCheckStatus StockCheckStatus { get; private set; }

    public DateTime? StockCheckCompletedAt { get; private set; }

    public string? StockCheckMessage { get; private set; }

    public string? ReviewedBy { get; private set; }

    public DateTime? ReviewedAt { get; private set; }

    public string? RejectionReason { get; private set; }

    public string? FulfilledBy { get; private set; }

    public DateTime? FulfilledAt { get; private set; }

    /// <summary>Optimistic concurrency token, incremented on every save.</summary>
    public int Version { get; private set; }

    public IReadOnlyList<ReplenishmentRequestLine> Lines => _lines;

    public IReadOnlyList<RequestStatusChange> History => _history;

    /// <summary>Outcome of the completed stock check; null while no result is known. Persisted so lists can show it cheaply.</summary>
    public bool? AllItemsAvailable { get; private set; }

    public static string FormatRequestNumber(int id) => $"RR-{id:D6}";

    public static ReplenishmentRequest CreateDraft(
        StockLocation location,
        RequestPriority priority,
        string? notes,
        IReadOnlyCollection<RequestLineSpec> lines,
        string requestedBy,
        DateTime now)
    {
        var request = new ReplenishmentRequest
        {
            Status = RequestStatus.Draft,
            StockCheckStatus = StockCheckStatus.NotStarted,
            RequestedBy = requestedBy,
            CreatedAt = now,
        };

        request.ApplyDraftContent(location, priority, notes, lines, now);
        request._history.Add(new RequestStatusChange(null, RequestStatus.Draft, requestedBy, now, null));
        return request;
    }

    public void UpdateDraft(
        StockLocation location,
        RequestPriority priority,
        string? notes,
        IReadOnlyCollection<RequestLineSpec> lines,
        DateTime now)
    {
        EnsureStatus(RequestStatus.Draft, "edited");
        ApplyDraftContent(location, priority, notes, lines, now);
    }

    public void EnsureCanBeDeleted() => EnsureStatus(RequestStatus.Draft, "deleted");

    public void Submit(string submittedBy, DateTime now)
    {
        EnsureStatus(RequestStatus.Draft, "submitted");
        if (_lines.Count == 0)
        {
            throw new BusinessRuleException(nameof(Lines), "A request must contain at least one line item before it can be submitted.");
        }

        SubmittedAt = now;
        StartStockCheck(now);
        TransitionTo(RequestStatus.Submitted, submittedBy, now, null);
    }

    /// <summary>Re-queues the stock check after the external service failed.</summary>
    public void RetryStockCheck(DateTime now)
    {
        EnsureStatus(RequestStatus.Submitted, "re-checked");
        if (StockCheckStatus != StockCheckStatus.Failed)
        {
            throw new InvalidStateException($"Only a failed stock check can be retried (current state: {StockCheckStatus}).");
        }

        StartStockCheck(now);
    }

    /// <summary>
    /// Returns true when the request is still waiting for a stock check result. A request can be rejected
    /// while the check is running, in which case the late result must be discarded.
    /// </summary>
    public bool IsAwaitingStockCheck =>
        Status == RequestStatus.Submitted && StockCheckStatus == StockCheckStatus.Pending;

    public void CompleteStockCheck(IReadOnlyDictionary<string, decimal> availableByArticle, DateTime now)
    {
        EnsureAwaitingStockCheck();

        foreach (var line in _lines)
        {
            // An article the external system does not know about has, by definition, no available stock.
            line.SetAvailableQuantity(availableByArticle.GetValueOrDefault(line.Material.ArticleNumber));
        }

        var shortCount = _lines.Count(l => !l.IsAvailable);
        AllItemsAvailable = shortCount == 0;
        StockCheckStatus = StockCheckStatus.Completed;
        StockCheckCompletedAt = now;
        StockCheckMessage = shortCount == 0
            ? $"All {_lines.Count} item(s) are available."
            : $"{shortCount} of {_lines.Count} item(s) have insufficient stock.";
        UpdatedAt = now;
    }

    public void FailStockCheck(string reason, DateTime now)
    {
        EnsureAwaitingStockCheck();

        StockCheckStatus = StockCheckStatus.Failed;
        StockCheckCompletedAt = now;
        StockCheckMessage = reason;
        UpdatedAt = now;
    }

    public void Approve(string reviewer, string? comment, DateTime now)
    {
        EnsureStatus(RequestStatus.Submitted, "approved");
        if (StockCheckStatus != StockCheckStatus.Completed)
        {
            throw new InvalidStateException(
                $"The request cannot be approved until the stock availability check has completed (current state: {StockCheckStatus}).");
        }

        EnsureNotRequester(reviewer, "approve");

        ReviewedBy = reviewer;
        ReviewedAt = now;
        TransitionTo(RequestStatus.Approved, reviewer, now, NormalizeText(comment));
    }

    public void Reject(string reviewer, string reason, DateTime now)
    {
        EnsureStatus(RequestStatus.Submitted, "rejected");
        var normalizedReason = NormalizeText(reason)
            ?? throw new BusinessRuleException(nameof(RejectionReason), "A rejection reason is required.");

        EnsureNotRequester(reviewer, "reject");

        if (StockCheckStatus == StockCheckStatus.Pending)
        {
            StockCheckStatus = StockCheckStatus.Cancelled;
            StockCheckCompletedAt = now;
            StockCheckMessage = "Cancelled because the request was rejected.";
        }

        ReviewedBy = reviewer;
        ReviewedAt = now;
        RejectionReason = normalizedReason;
        TransitionTo(RequestStatus.Rejected, reviewer, now, normalizedReason);
    }

    public void Fulfill(string fulfilledBy, IReadOnlyDictionary<int, decimal> fulfilledByLineId, DateTime now)
    {
        EnsureStatus(RequestStatus.Approved, "fulfilled");

        var unknownLineIds = fulfilledByLineId.Keys.Except(_lines.Select(l => l.Id)).ToList();
        if (unknownLineIds.Count > 0)
        {
            throw new BusinessRuleException("Lines", $"Unknown line id(s): {string.Join(", ", unknownLineIds)}.");
        }

        var missing = _lines.Where(l => !fulfilledByLineId.ContainsKey(l.Id)).ToList();
        if (missing.Count > 0)
        {
            throw new BusinessRuleException(
                "Lines",
                $"A fulfilled quantity must be given for every line. Missing: {string.Join(", ", missing.Select(l => l.Material.ArticleNumber))}.");
        }

        foreach (var line in _lines)
        {
            var quantity = fulfilledByLineId[line.Id];
            if (quantity < 0 || quantity > line.RequestedQuantity)
            {
                throw new BusinessRuleException(
                    "Lines",
                    $"Fulfilled quantity for {line.Material.ArticleNumber} must be between 0 and the requested quantity ({line.RequestedQuantity:0.###}).");
            }
        }

        foreach (var line in _lines)
        {
            line.SetFulfilledQuantity(fulfilledByLineId[line.Id]);
        }

        FulfilledBy = fulfilledBy;
        FulfilledAt = now;
        TransitionTo(RequestStatus.Fulfilled, fulfilledBy, now, null);
    }

    internal void IncrementVersion() => Version++;

    private void ApplyDraftContent(
        StockLocation location,
        RequestPriority priority,
        string? notes,
        IReadOnlyCollection<RequestLineSpec> lines,
        DateTime now)
    {
        if (lines.Count == 0)
        {
            throw new BusinessRuleException(nameof(Lines), "A request must contain at least one line item.");
        }

        var duplicates = lines
            .GroupBy(l => l.Material.ArticleNumber, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicates.Count > 0)
        {
            throw new BusinessRuleException(nameof(Lines), $"Each article may only appear once. Duplicated: {string.Join(", ", duplicates)}.");
        }

        if (lines.Any(l => l.RequestedQuantity <= 0))
        {
            throw new BusinessRuleException(nameof(Lines), "Requested quantities must be greater than zero.");
        }

        Location = location;
        LocationId = location.Id;
        Priority = priority;
        Notes = NormalizeText(notes);
        UpdatedAt = now;

        _lines.Clear();
        _lines.AddRange(lines.Select(l => new ReplenishmentRequestLine(l.Material, l.RequestedQuantity)));
    }

    private void StartStockCheck(DateTime now)
    {
        StockCheckStatus = StockCheckStatus.Pending;
        AllItemsAvailable = null;
        StockCheckCompletedAt = null;
        StockCheckMessage = null;
        UpdatedAt = now;
        foreach (var line in _lines)
        {
            line.SetAvailableQuantity(null);
        }
    }

    private void TransitionTo(RequestStatus newStatus, string changedBy, DateTime now, string? comment)
    {
        _history.Add(new RequestStatusChange(Status, newStatus, changedBy, now, comment));
        Status = newStatus;
        UpdatedAt = now;
    }

    private void EnsureStatus(RequestStatus required, string action)
    {
        if (Status != required)
        {
            throw new InvalidStateException($"Request {RequestNumber} is {Status}; only {required} requests can be {action}.");
        }
    }

    private void EnsureAwaitingStockCheck()
    {
        if (!IsAwaitingStockCheck)
        {
            throw new InvalidStateException($"Request {RequestNumber} is not awaiting a stock check.");
        }
    }

    private void EnsureNotRequester(string reviewer, string action)
    {
        if (string.Equals(reviewer, RequestedBy, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidStateException($"You cannot {action} your own request; it must be reviewed by someone else.");
        }
    }

    private static string? NormalizeText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
