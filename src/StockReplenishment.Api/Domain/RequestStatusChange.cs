using StockReplenishment.Contracts.Requests;

namespace StockReplenishment.Api.Domain;

/// <summary>Audit trail entry, one per lifecycle transition.</summary>
public class RequestStatusChange
{
    private RequestStatusChange()
    {
    }

    internal RequestStatusChange(RequestStatus? fromStatus, RequestStatus toStatus, string changedBy, DateTime changedAt, string? comment)
    {
        FromStatus = fromStatus;
        ToStatus = toStatus;
        ChangedBy = changedBy;
        ChangedAt = changedAt;
        Comment = comment;
    }

    public int Id { get; private set; }

    public int RequestId { get; private set; }

    public RequestStatus? FromStatus { get; private set; }

    public RequestStatus ToStatus { get; private set; }

    public string ChangedBy { get; private set; } = string.Empty;

    public DateTime ChangedAt { get; private set; }

    public string? Comment { get; private set; }
}
