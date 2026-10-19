using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using StockReplenishment.Api.Data;
using StockReplenishment.Api.Domain;
using StockReplenishment.Contracts.Common;
using StockReplenishment.Contracts.Requests;

namespace StockReplenishment.Api.Application;

public interface IReplenishmentRequestService
{
    Task<PagedResult<ReplenishmentRequestSummaryDto>> SearchAsync(RequestQuery query, CancellationToken cancellationToken);

    Task<ReplenishmentRequestDto> GetAsync(int id, CancellationToken cancellationToken);

    Task<ReplenishmentRequestDto> CreateAsync(SaveRequestInput input, CancellationToken cancellationToken);

    Task<ReplenishmentRequestDto> UpdateAsync(int id, SaveRequestInput input, CancellationToken cancellationToken);

    Task DeleteAsync(int id, CancellationToken cancellationToken);

    Task<ReplenishmentRequestDto> SubmitAsync(int id, CancellationToken cancellationToken);

    Task<ReplenishmentRequestDto> RetryStockCheckAsync(int id, CancellationToken cancellationToken);

    Task<ReplenishmentRequestDto> ApproveAsync(int id, ApproveRequestInput input, CancellationToken cancellationToken);

    Task<ReplenishmentRequestDto> RejectAsync(int id, RejectRequestInput input, CancellationToken cancellationToken);

    Task<ReplenishmentRequestDto> FulfillAsync(int id, FulfillRequestInput input, CancellationToken cancellationToken);
}

public sealed partial class ReplenishmentRequestService(
    AppDbContext db,
    ICurrentUser currentUser,
    IStockCheckQueue stockCheckQueue,
    IRequestNotifier notifier,
    TimeProvider timeProvider) : IReplenishmentRequestService
{
    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    public async Task<PagedResult<ReplenishmentRequestSummaryDto>> SearchAsync(RequestQuery query, CancellationToken cancellationToken)
    {
        var requests = db.Requests.AsNoTracking();

        if (query.Status is { } status)
        {
            requests = requests.Where(r => r.Status == status);
        }

        if (query.Priority is { } priority)
        {
            requests = requests.Where(r => r.Priority == priority);
        }

        if (query.LocationId is { } locationId)
        {
            requests = requests.Where(r => r.LocationId == locationId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            requests = ApplySearch(requests, query.Search);
        }

        requests = query.Sort switch
        {
            RequestSortOrder.OldestFirst => requests.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id),
            RequestSortOrder.PriorityThenNewest => requests.OrderByDescending(r => r.Priority).ThenByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id),
            _ => requests.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id),
        };

        var totalCount = await requests.CountAsync(cancellationToken);
        var items = await requests
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(r => new ReplenishmentRequestSummaryDto(
                r.Id,
                ReplenishmentRequest.FormatRequestNumber(r.Id),
                r.LocationId,
                r.Location.Code,
                r.Location.Name,
                r.Priority,
                r.Status,
                r.StockCheckStatus,
                r.AllItemsAvailable,
                r.Lines.Count,
                r.RequestedBy,
                r.CreatedAt,
                r.SubmittedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<ReplenishmentRequestSummaryDto>(items, query.Page, query.PageSize, totalCount);
    }

    public async Task<ReplenishmentRequestDto> GetAsync(int id, CancellationToken cancellationToken) =>
        (await LoadAsync(id, cancellationToken)).ToDto();

    public async Task<ReplenishmentRequestDto> CreateAsync(SaveRequestInput input, CancellationToken cancellationToken)
    {
        var (location, lines) = await ResolveInputAsync(input, cancellationToken);
        var request = ReplenishmentRequest.CreateDraft(location, input.Priority, input.Notes, lines, currentUser.UserName, Now);

        db.Requests.Add(request);
        await db.SaveChangesAsync(cancellationToken);
        await notifier.RequestUpdatedAsync(request, cancellationToken);
        return request.ToDto();
    }

    public async Task<ReplenishmentRequestDto> UpdateAsync(int id, SaveRequestInput input, CancellationToken cancellationToken)
    {
        var request = await LoadAsync(id, cancellationToken);
        var (location, lines) = await ResolveInputAsync(input, cancellationToken);
        request.UpdateDraft(location, input.Priority, input.Notes, lines, Now);

        await db.SaveChangesAsync(cancellationToken);
        await notifier.RequestUpdatedAsync(request, cancellationToken);
        return request.ToDto();
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var request = await LoadAsync(id, cancellationToken);
        request.EnsureCanBeDeleted();

        db.Requests.Remove(request);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<ReplenishmentRequestDto> SubmitAsync(int id, CancellationToken cancellationToken) =>
        StartStockCheckAsync(id, request => request.Submit(currentUser.UserName, Now), cancellationToken);

    public Task<ReplenishmentRequestDto> RetryStockCheckAsync(int id, CancellationToken cancellationToken) =>
        StartStockCheckAsync(id, request => request.RetryStockCheck(Now), cancellationToken);

    public Task<ReplenishmentRequestDto> ApproveAsync(int id, ApproveRequestInput input, CancellationToken cancellationToken) =>
        ExecuteAsync(id, request => request.Approve(currentUser.UserName, input.Comment, Now), cancellationToken);

    public Task<ReplenishmentRequestDto> RejectAsync(int id, RejectRequestInput input, CancellationToken cancellationToken) =>
        ExecuteAsync(id, request => request.Reject(currentUser.UserName, input.Reason, Now), cancellationToken);

    public Task<ReplenishmentRequestDto> FulfillAsync(int id, FulfillRequestInput input, CancellationToken cancellationToken)
    {
        var duplicateLineIds = input.Lines.GroupBy(l => l.LineId).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicateLineIds.Count > 0)
        {
            throw new BusinessRuleException(nameof(input.Lines), $"Duplicate line id(s): {string.Join(", ", duplicateLineIds)}.");
        }

        var quantities = input.Lines.ToDictionary(l => l.LineId, l => l.FulfilledQuantity);
        return ExecuteAsync(id, request => request.Fulfill(currentUser.UserName, quantities, Now), cancellationToken);
    }

    private async Task<ReplenishmentRequestDto> ExecuteAsync(
        int id,
        Action<ReplenishmentRequest> operation,
        CancellationToken cancellationToken)
    {
        var request = await LoadAsync(id, cancellationToken);
        operation(request);

        await db.SaveChangesAsync(cancellationToken);
        await notifier.RequestUpdatedAsync(request, cancellationToken);
        return request.ToDto();
    }

    private async Task<ReplenishmentRequestDto> StartStockCheckAsync(
        int id,
        Action<ReplenishmentRequest> operation,
        CancellationToken cancellationToken)
    {
        // The Pending state is persisted before the work is queued, so a check that is lost
        // (e.g. on restart) is picked up again by the worker's recovery on startup.
        var result = await ExecuteAsync(id, operation, cancellationToken);
        await stockCheckQueue.EnqueueAsync(id, CancellationToken.None);
        return result;
    }

    private async Task<ReplenishmentRequest> LoadAsync(int id, CancellationToken cancellationToken) =>
        await db.Requests
            .Include(r => r.Location)
            .Include(r => r.Lines).ThenInclude(l => l.Material)
            .Include(r => r.History)
            .AsSplitQuery()
            .SingleOrDefaultAsync(r => r.Id == id, cancellationToken)
        ?? throw new NotFoundException("Replenishment request", id);

    private async Task<(StockLocation Location, List<RequestLineSpec> Lines)> ResolveInputAsync(
        SaveRequestInput input,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();

        var location = await db.Locations.SingleOrDefaultAsync(l => l.Id == input.LocationId, cancellationToken);
        if (location is null)
        {
            errors[nameof(input.LocationId)] = [$"Stock location {input.LocationId} does not exist."];
        }

        var articleNumbers = input.Lines.Select(l => l.ArticleNumber.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var materials = await db.Materials
            .Where(m => articleNumbers.Contains(m.ArticleNumber))
            .ToDictionaryAsync(m => m.ArticleNumber, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var unknown = articleNumbers.Where(a => !materials.ContainsKey(a)).ToList();
        if (unknown.Count > 0)
        {
            errors[nameof(input.Lines)] = [$"Unknown article number(s): {string.Join(", ", unknown)}."];
        }

        if (errors.Count > 0)
        {
            throw new BusinessRuleException(errors);
        }

        var lines = input.Lines
            .Select(l => new RequestLineSpec(materials[l.ArticleNumber.Trim()], l.RequestedQuantity))
            .ToList();
        return (location!, lines);
    }

    private static IQueryable<ReplenishmentRequest> ApplySearch(IQueryable<ReplenishmentRequest> requests, string search)
    {
        var match = RequestNumberPattern().Match(search.Trim());
        if (match.Success && int.TryParse(match.Groups["id"].Value, out var requestId))
        {
            return requests.Where(r => r.Id == requestId);
        }

        var pattern = LikePattern.Contains(search);
        return requests.Where(r =>
            EF.Functions.Like(r.RequestedBy, pattern, LikePattern.EscapeCharacter)
            || EF.Functions.Like(r.Location.Name, pattern, LikePattern.EscapeCharacter)
            || r.Lines.Any(l =>
                EF.Functions.Like(l.Material.ArticleNumber, pattern, LikePattern.EscapeCharacter)
                || EF.Functions.Like(l.Material.Description, pattern, LikePattern.EscapeCharacter)));
    }

    [GeneratedRegex(@"^RR-0*(?<id>\d{1,9})$", RegexOptions.IgnoreCase)]
    private static partial Regex RequestNumberPattern();
}
