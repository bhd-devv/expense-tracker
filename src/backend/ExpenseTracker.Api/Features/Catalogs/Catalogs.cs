using ExpenseTracker.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Features.Catalogs;

public sealed record CatalogItemDto(Guid Id, string Name, string? Code, bool Archived, int Version);
public sealed record CatalogRequest(string Name, bool Archived, int Version);
public sealed record CatalogDto(IReadOnlyList<CatalogItemDto> Categories);

public sealed class CatalogService(Database db, ILogger<CatalogService>? logger = null)
{
    public async Task<CatalogDto> List(User user, CancellationToken ct) => new(
        (await db.Categories.AsNoTracking().Where(x => x.UserId == user.Id).OrderBy(x => x.Name).ToArrayAsync(ct))
            .Select(x => new CatalogItemDto(x.Id, x.Name, x.Code, x.Archived, x.Version)).ToArray());

    public async Task<CatalogItemDto> Category(User user, Guid? id, CatalogRequest input, CancellationToken ct)
    {
        await using var transaction = await db.LockUser(user, ct);
        if (id is null && await db.Categories.CountAsync(x => x.UserId == user.Id, ct) >= 50) throw new ApiError("catalog_limit");
        var category = id is null ? new Category { UserId = user.Id } : await db.Categories.SingleOrDefaultAsync(x => x.Id == id && x.UserId == user.Id, ct)
            ?? throw new ApiError("not_found", 404);
        if (id is not null && category.Version != input.Version) throw new ApiError("conflict", 409);
        var name = Rules.Text(input.Name, 60);
        if (category.Name != name) category.Code = null;
        category.Name = name;
        category.Archived = input.Archived;
        if (id is null) db.Add(category); else category.Version++;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        logger?.LogInformation("Category saved categoryId={CategoryId} userId={UserId} created={Created} archived={Archived} version={Version}",
            category.Id, user.Id, id is null, category.Archived, category.Version);
        return new(category.Id, category.Name, category.Code, category.Archived, category.Version);
    }
}
