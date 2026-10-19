using Microsoft.EntityFrameworkCore;
using StockReplenishment.Api.Data;
using StockReplenishment.Contracts.Catalog;

namespace StockReplenishment.Api.Application;

public interface ICatalogService
{
    Task<IReadOnlyList<LocationDto>> GetLocationsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<MaterialDto>> SearchMaterialsAsync(string? search, int limit, CancellationToken cancellationToken);
}

public sealed class CatalogService(AppDbContext db) : ICatalogService
{
    public async Task<IReadOnlyList<LocationDto>> GetLocationsAsync(CancellationToken cancellationToken) =>
        await db.Locations
            .AsNoTracking()
            .OrderBy(l => l.Code)
            .Select(l => new LocationDto(l.Id, l.Code, l.Name, l.Area))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<MaterialDto>> SearchMaterialsAsync(string? search, int limit, CancellationToken cancellationToken)
    {
        var query = db.Materials.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = LikePattern.Contains(search);
            query = query.Where(m =>
                EF.Functions.Like(m.ArticleNumber, pattern, LikePattern.EscapeCharacter)
                || EF.Functions.Like(m.Description, pattern, LikePattern.EscapeCharacter));
        }

        return await query
            .OrderBy(m => m.ArticleNumber)
            .Take(limit)
            .Select(m => new MaterialDto(m.Id, m.ArticleNumber, m.Description, m.UnitOfMeasure))
            .ToListAsync(cancellationToken);
    }
}
