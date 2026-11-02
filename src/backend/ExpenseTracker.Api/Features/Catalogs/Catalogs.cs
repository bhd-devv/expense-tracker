using ExpenseTracker.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Features.Catalogs;

public sealed record CategoryLimitDto(string Amount, string Currency, string Spent, decimal UsedPercent, bool Exceeded, DateOnly From, DateOnly To);
public sealed record CategoryLimitRequest(string? Amount, string? Currency, int Version);
public sealed record CatalogItemDto(Guid Id, string Name, string? Code, bool Archived, int Version, string? Icon = null, CategoryLimitDto? Limit = null);
public sealed record CatalogRequest(string Name, bool Archived, int Version, string? Icon = null);
public sealed record CatalogDto(IReadOnlyList<CatalogItemDto> Categories);

public sealed class CatalogService(Database db, TimeProvider clock, ILogger<CatalogService>? logger = null)
{
    public async Task<CatalogDto> List(User user, CancellationToken ct)
    {
        var categories = await db.Categories.AsNoTracking().Where(x => x.UserId == user.Id).OrderBy(x => x.Name).ToArrayAsync(ct);
        var (from, to) = Month(user);
        if (!categories.Any(x => x.MonthlyLimit is not null))
            return new(categories.Select(x => View(x, from, to)).ToArray());
        var totals = await db.Expenses.AsNoTracking().Where(x => x.UserId == user.Id && x.Date >= from && x.Date <= to)
            .GroupBy(x => new { x.CategoryId, x.Currency })
            .Select(g => new { g.Key.CategoryId, g.Key.Currency, Spent = g.Sum(x => x.Amount) })
            .ToDictionaryAsync(x => (x.CategoryId, x.Currency), x => x.Spent, ct);
        return new(categories.Select(x => View(x, from, to, totals.GetValueOrDefault((x.Id, x.LimitCurrency ?? "")))).ToArray());
    }

    private (DateOnly From, DateOnly To) Month(User user)
    {
        var today = Rules.Today(user, clock);
        var from = new DateOnly(today.Year, today.Month, 1);
        return (from, from.AddMonths(1).AddDays(-1));
    }

    private static CatalogItemDto View(Category category, DateOnly from, DateOnly to, decimal spent = 0) => new(
        category.Id, category.Name, category.Code, category.Archived, category.Version, category.Icon ?? CategoryIcons.Default(category.Code),
        category.MonthlyLimit is { } amount && category.LimitCurrency is { } currency
            ? new(Rules.Money(amount), currency, Rules.Money(spent), decimal.Round(spent / amount * 100, 1, MidpointRounding.AwayFromZero), spent > amount, from, to)
            : null);

    private async Task<CatalogItemDto> View(User user, Category category, CancellationToken ct)
    {
        var (from, to) = Month(user);
        var spent = category.MonthlyLimit is null ? 0 : await db.Expenses.AsNoTracking()
            .Where(x => x.UserId == user.Id && x.CategoryId == category.Id && x.Currency == category.LimitCurrency
                && x.Date >= from && x.Date <= to).SumAsync(x => x.Amount, ct);
        return View(category, from, to, spent);
    }

    public async Task<CatalogItemDto> Category(User user, Guid? id, CatalogRequest input, CancellationToken ct)
    {
        await using var transaction = await db.LockUser(user, ct);
        if (id is null && await db.Categories.CountAsync(x => x.UserId == user.Id, ct) >= 50) throw new ApiError("catalog_limit");
        var category = id is null ? new Category { UserId = user.Id } : await db.Categories.SingleOrDefaultAsync(x => x.Id == id && x.UserId == user.Id, ct)
            ?? throw new ApiError("not_found", 404);
        if (id is not null && category.Version != input.Version) throw new ApiError("conflict", 409);
        if (input.Icon is not null && !CategoryIcons.Contains(input.Icon)) throw new ApiError("invalid_icon");
        category.Icon = input.Icon ?? category.Icon ?? CategoryIcons.Default(category.Code);
        var name = Rules.Text(input.Name, 60);
        if (category.Name != name) category.Code = null;
        category.Name = name;
        category.Archived = input.Archived;
        if (id is null) db.Add(category); else category.Version++;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        logger?.LogInformation("Category saved categoryId={CategoryId} userId={UserId} created={Created} archived={Archived} version={Version}",
            category.Id, user.Id, id is null, category.Archived, category.Version);
        return await View(user, category, ct);
    }

    public async Task<CatalogItemDto> Limit(User user, Guid id, CategoryLimitRequest input, CancellationToken ct)
    {
        await using var transaction = await db.LockUser(user, ct);
        var category = await db.Categories.SingleOrDefaultAsync(x => x.Id == id && x.UserId == user.Id, ct)
            ?? throw new ApiError("not_found", 404);
        if (category.Version != input.Version) throw new ApiError("conflict", 409);
        if (input.Amount is null)
        {
            if (input.Currency is not null) throw new ApiError("invalid_limit");
            category.MonthlyLimit = null;
            category.LimitCurrency = null;
        }
        else
        {
            if (input.Currency is null || !Rules.Currencies.Contains(input.Currency)) throw new ApiError("invalid_currency");
            category.MonthlyLimit = Rules.Amount(input.Amount, input.Currency);
            category.LimitCurrency = input.Currency;
        }
        category.Version++;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        logger?.LogInformation("Category limit saved categoryId={CategoryId} userId={UserId} version={Version}", id, user.Id, category.Version);
        return await View(user, category, ct);
    }

    public async Task Delete(User user, Guid id, int version, CancellationToken ct)
    {
        await using var transaction = await db.LockUser(user, ct);
        var category = await db.Categories.SingleOrDefaultAsync(x => x.Id == id && x.UserId == user.Id, ct)
            ?? throw new ApiError("not_found", 404);
        if (category.Version != version) throw new ApiError("conflict", 409);
        if (await db.Expenses.AnyAsync(x => x.UserId == user.Id && x.CategoryId == id, ct)
            || await db.Drafts.AnyAsync(x => x.UserId == user.Id && x.CategoryId == id
                && x.Status != "saved" && x.Status != "cancelled" && x.ExpiresAt > clock.GetUtcNow(), ct))
            throw new ApiError("category_in_use");
        db.Remove(category);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        logger?.LogInformation("Category deleted categoryId={CategoryId} userId={UserId}", id, user.Id);
    }
}
