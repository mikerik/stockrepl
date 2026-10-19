using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using StockReplenishment.Api.Application;
using StockReplenishment.Contracts.Common;
using StockReplenishment.Contracts.Requests;

namespace StockReplenishment.Api.Controllers;

[ApiController]
[Route("api/replenishment-requests")]
[Produces("application/json")]
public sealed class ReplenishmentRequestsController(IReplenishmentRequestService service) : ControllerBase
{
    /// <summary>Lists requests, filtered by status, priority, location and free text, one page at a time.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<ReplenishmentRequestSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<ReplenishmentRequestSummaryDto>>> Search(
        [FromQuery] RequestQuery query,
        CancellationToken cancellationToken) =>
        Ok(await service.SearchAsync(query, cancellationToken));

    [HttpGet("{id:int}")]
    [ProducesResponseType<ReplenishmentRequestDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReplenishmentRequestDto>> Get(int id, CancellationToken cancellationToken) =>
        Ok(await service.GetAsync(id, cancellationToken));

    /// <summary>Creates a new request in Draft status.</summary>
    [HttpPost]
    [ProducesResponseType<ReplenishmentRequestDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ReplenishmentRequestDto>> Create(SaveRequestInput input, CancellationToken cancellationToken)
    {
        var created = await service.CreateAsync(input, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    /// <summary>Replaces the content of a draft request.</summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType<ReplenishmentRequestDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReplenishmentRequestDto>> Update(int id, SaveRequestInput input, CancellationToken cancellationToken) =>
        Ok(await service.UpdateAsync(id, input, cancellationToken));

    /// <summary>Deletes a draft request.</summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Submits a draft for approval. The external stock check runs in the background, so this returns
    /// 202 Accepted immediately; the result is pushed over SignalR and can also be polled via the Location URL.
    /// </summary>
    [HttpPost("{id:int}/submit")]
    [ProducesResponseType<ReplenishmentRequestDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReplenishmentRequestDto>> Submit(int id, CancellationToken cancellationToken)
    {
        var request = await service.SubmitAsync(id, cancellationToken);
        return AcceptedAtAction(nameof(Get), new { id }, request);
    }

    /// <summary>Starts a new stock check after the previous one failed.</summary>
    [HttpPost("{id:int}/stock-check")]
    [ProducesResponseType<ReplenishmentRequestDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReplenishmentRequestDto>> RetryStockCheck(int id, CancellationToken cancellationToken)
    {
        var request = await service.RetryStockCheckAsync(id, cancellationToken);
        return AcceptedAtAction(nameof(Get), new { id }, request);
    }

    [HttpPost("{id:int}/approve")]
    [ProducesResponseType<ReplenishmentRequestDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReplenishmentRequestDto>> Approve(
        int id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ApproveRequestInput? input,
        CancellationToken cancellationToken) =>
        Ok(await service.ApproveAsync(id, input ?? new ApproveRequestInput(), cancellationToken));

    [HttpPost("{id:int}/reject")]
    [ProducesResponseType<ReplenishmentRequestDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReplenishmentRequestDto>> Reject(int id, RejectRequestInput input, CancellationToken cancellationToken) =>
        Ok(await service.RejectAsync(id, input, cancellationToken));

    /// <summary>Marks an approved request as fulfilled, recording the delivered quantity for every line.</summary>
    [HttpPost("{id:int}/fulfill")]
    [ProducesResponseType<ReplenishmentRequestDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReplenishmentRequestDto>> Fulfill(int id, FulfillRequestInput input, CancellationToken cancellationToken) =>
        Ok(await service.FulfillAsync(id, input, cancellationToken));
}
