using ExpenseTracker.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Features.Catalogs;

public sealed record CatalogItemDto(Guid Id, string Name, string? Currency, string? Code, bool Archived, int Version);
public sealed record CatalogRequest(string Name, string? Currency, bool Archived, int Version);
public sealed record CatalogDto(IReadOnlyList<CatalogItemDto> Accounts, IReadOnlyList<CatalogItemDto> Categories);

public sealed class CatalogService(Database db, ILogger<CatalogService>? logger = null)
{
    public async Task<CatalogDto> List(User user, CancellationToken ct) => new(
        (await db.Accounts.AsNoTracking().Where(x => x.UserId == user.Id).OrderBy(x => x.Name).ToArrayAsync(ct))
            .Select(x => new CatalogItemDto(x.Id, x.Name, x.Currency, null, x.Archived, x.Version)).ToArray(),
        (await db.Categories.AsNoTracking().Where(x => x.UserId == user.Id).OrderBy(x => x.Name).ToArrayAsync(ct))
            .Select(x => new CatalogItemDto(x.Id, x.Name, null, x.Code, x.Archived, x.Version)).ToArray());

    public async Task<CatalogItemDto> Account(User user, Guid? id, CatalogRequest input, CancellationToken ct)
    {
        await using var transaction = await db.LockUser(user, ct);
        if (id is null && await db.Accounts.CountAsync(x => x.UserId == user.Id, ct) >= 50) throw new ApiError("catalog_limit");
        var account = id is null ? new Account { UserId = user.Id } : await db.Accounts.SingleOrDefaultAsync(x => x.Id == id && x.UserId == user.Id, ct)
            ?? throw new ApiError("not_found", 404);
        if (id is not null && account.Version != input.Version) throw new ApiError("conflict", 409);
        var currency = input.Currency ?? user.Currency;
        if (!Rules.Currencies.Contains(currency)) throw new ApiError("invalid_currency");
        if (id is not null && account.Currency != currency && (await db.Expenses.AnyAsync(x => x.UserId == user.Id && x.AccountId == id, ct)
            || await db.Drafts.AnyAsync(x => x.UserId == user.Id && x.AccountId == id, ct))) throw new ApiError("currency_locked", 409);
        account.Name = Rules.Text(input.Name, 60);
        account.Currency = currency;
        account.Archived = input.Archived;
        if (id is null) db.Add(account); else account.Version++;
        if (account.Archived && user.DefaultAccountId == account.Id)
        { user.DefaultAccountId = null; user.Version++; }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        logger?.LogInformation("Account saved accountId={AccountId} userId={UserId} created={Created} archived={Archived} version={Version}",
            account.Id, user.Id, id is null, account.Archived, account.Version);
        return new(account.Id, account.Name, account.Currency, null, account.Archived, account.Version);
    }

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
        return new(category.Id, category.Name, null, category.Code, category.Archived, category.Version);
    }
}
