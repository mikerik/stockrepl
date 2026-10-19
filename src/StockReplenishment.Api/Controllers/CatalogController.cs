using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using StockReplenishment.Api.Application;
using StockReplenishment.Contracts.Catalog;

namespace StockReplenishment.Api.Controllers;

[ApiController]
[Route("api")]
[Produces("application/json")]
public sealed class CatalogController(ICatalogService catalogService) : ControllerBase
{
    [HttpGet("locations")]
    [ProducesResponseType<IReadOnlyList<LocationDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<LocationDto>>> GetLocations(CancellationToken cancellationToken) =>
        Ok(await catalogService.GetLocationsAsync(cancellationToken));

    [HttpGet("materials")]
    [ProducesResponseType<IReadOnlyList<MaterialDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<MaterialDto>>> SearchMaterials(
        [FromQuery, MaxLength(100)] string? search,
        [FromQuery, Range(1, 100)] int limit = 20,
        CancellationToken cancellationToken = default) =>
        Ok(await catalogService.SearchMaterialsAsync(search, limit, cancellationToken));
}
