using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ExpenseTracker.Api.Features.Catalogs;
using ExpenseTracker.Api.Features.Users;
using ExpenseTracker.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Features.Incomes;

public sealed record IncomeInput(string Amount, Guid CategoryId, string Description, DateOnly Date, int Version);
public sealed record CreateIncomeRequest(Guid RequestId, string Amount, Guid CategoryId, string Description, DateOnly? Date);
public sealed record IncomeDto(Guid Id, string Amount, string Currency, Guid CategoryId, string Description, DateOnly Date, int Version);
public sealed record IncomePage(IReadOnlyList<IncomeDto> Items, int Page, bool HasMore, string Total, string Currency);
public sealed record IncomeCategoryRequest(Guid RequestId, string Name, string Icon, int Version);

public sealed class IncomeService(Database db, TimeProvider clock)
{
    private static string Hash(params object[] values) => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(values))));
    private static IncomeDto View(Income x, User user) => new(x.Id, Rules.Money(x.Amount), user.Currency, x.CategoryId, x.Description, x.Date, x.Version);
    private static CatalogItemDto View(IncomeCategory x) => new(x.Id, x.Name, null, false, x.Version, x.Icon);

    public async Task<CatalogDto> Categories(User user, CancellationToken ct) => new(
        (await db.IncomeCategories.AsNoTracking().Where(x => x.UserId == user.Id && !x.Deleted)
            .OrderBy(x => x.Name).ThenBy(x => x.Id).ToArrayAsync(ct)).Select(View).ToArray());

    public async Task<CatalogItemDto> SaveCategory(User user, Guid? id, IncomeCategoryRequest input, CancellationToken ct)
    {
        var name = Rules.Text(input.Name, 60);
        if (!CategoryIcons.Contains(input.Icon)) throw new ApiError("invalid_icon");
        var hash = Hash(name, input.Icon);
        await using var transaction = await db.LockUser(user, ct);
        IncomeCategory category;
        if (id is null)
        {
            if (input.RequestId == Guid.Empty) throw new ApiError("invalid_request_id");
            var previous = await db.IncomeCategories.SingleOrDefaultAsync(x => x.Id == input.RequestId, ct);
            if (previous is not null)
            {
                if (previous.UserId != user.Id || previous.Deleted || previous.CreateHash != hash) throw new ApiError("conflict", 409);
                return View(previous);
            }
            if (await db.IncomeCategories.CountAsync(x => x.UserId == user.Id && !x.Deleted, ct) >= 50) throw new ApiError("catalog_limit");
            category = new() { Id = input.RequestId, UserId = user.Id, CreateHash = hash };
            db.Add(category);
        }
        else
        {
            category = await db.IncomeCategories.SingleOrDefaultAsync(x => x.UserId == user.Id && x.Id == id && !x.Deleted, ct)
                ?? throw new ApiError("not_found", 404);
            if (category.Version != input.Version)
            {
                if (category.Version == input.Version + 1 && category.Name == name && category.Icon == input.Icon) return View(category);
                throw new ApiError("conflict", 409);
            }
            category.Version++;
        }
        category.Name = name;
        category.Icon = input.Icon;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return View(category);
    }

    public async Task DeleteCategory(User user, Guid id, int version, CancellationToken ct)
    {
        await using var transaction = await db.LockUser(user, ct);
        var category = await db.IncomeCategories.SingleOrDefaultAsync(x => x.UserId == user.Id && x.Id == id, ct)
            ?? throw new ApiError("not_found", 404);
        if (category.Deleted && category.Version == version + 1) return;
        if (category.Deleted || category.Version != version) throw new ApiError("conflict", 409);
        if (await db.Incomes.AnyAsync(x => x.UserId == user.Id && x.CategoryId == id && !x.Deleted, ct)) throw new ApiError("income_category_in_use");
        category.Deleted = true;
        category.Version++;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<IncomePage> List(User user, DateOnly? from, DateOnly? to, Guid? categoryId, int page, CancellationToken ct)
    {
        if (page is < 0 or > 100000 || from > to) throw new ApiError("invalid_filter");
        await using var transaction = await db.LockUser(user, ct);
        var query = db.Incomes.AsNoTracking().Where(x => x.UserId == user.Id && !x.Deleted
            && (from == null || x.Date >= from) && (to == null || x.Date <= to) && (categoryId == null || x.CategoryId == categoryId));
        var total = await query.SumAsync(x => x.Amount, ct);
        var items = await query.OrderByDescending(x => x.Date).ThenByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
            .Skip(page * 20).Take(21).ToArrayAsync(ct);
        await transaction.CommitAsync(ct);
        return new(items.Take(20).Select(x => View(x, user)).ToArray(), page, items.Length > 20, Rules.Money(total), user.Currency);
    }

    public async Task<IncomeDto> Create(User user, CreateIncomeRequest input, CancellationToken ct)
    {
        if (input.RequestId == Guid.Empty) throw new ApiError("invalid_request_id");
        var amount = Rules.Amount(input.Amount, user.Currency);
        var description = Rules.Text(input.Description);
        if (input.Date == DateOnly.MinValue) throw new ApiError("invalid_date");
        // Hash the requested date (including null), so retrying after midnight preserves the first resolved date.
        var hash = Hash(amount, input.CategoryId, description, input.Date?.ToString("yyyy-MM-dd") ?? "today");
        await using var transaction = await db.LockUser(user, ct);
        var previous = await db.Incomes.SingleOrDefaultAsync(x => x.Id == input.RequestId, ct);
        if (previous is not null)
        {
            if (previous.UserId != user.Id || previous.Deleted || previous.CreateHash != hash) throw new ApiError("conflict", 409);
            return View(previous, user);
        }
        await RequireCategory(user, input.CategoryId, ct);
        var income = new Income
        {
            Id = input.RequestId,
            UserId = user.Id,
            Amount = amount,
            CategoryId = input.CategoryId,
            Description = description,
            Date = input.Date ?? Rules.Today(user, clock),
            CreatedAt = clock.GetUtcNow(),
            CreateHash = hash
        };
        db.Add(income);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return View(income, user);
    }

    public async Task<IncomeDto> Edit(User user, Guid id, IncomeInput input, CancellationToken ct)
    {
        var amount = Rules.Amount(input.Amount, user.Currency);
        var description = Rules.Text(input.Description);
        if (input.Date == DateOnly.MinValue) throw new ApiError("invalid_date");
        await using var transaction = await db.LockUser(user, ct);
        var income = await db.Incomes.SingleOrDefaultAsync(x => x.UserId == user.Id && x.Id == id && !x.Deleted, ct)
            ?? throw new ApiError("not_found", 404);
        if (income.Version != input.Version)
        {
            if (income.Version == input.Version + 1 && income.Amount == amount && income.CategoryId == input.CategoryId
                && income.Description == description && income.Date == input.Date) return View(income, user);
            throw new ApiError("conflict", 409);
        }
        await RequireCategory(user, input.CategoryId, ct);
        income.Amount = amount;
        income.Description = description;
        income.CategoryId = input.CategoryId;
        income.Date = input.Date;
        income.Version++;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return View(income, user);
    }

    public async Task Delete(User user, Guid id, int version, CancellationToken ct)
    {
        await using var transaction = await db.LockUser(user, ct);
        var income = await db.Incomes.SingleOrDefaultAsync(x => x.UserId == user.Id && x.Id == id, ct) ?? throw new ApiError("not_found", 404);
        if (income.Deleted && income.Version == version + 1) return;
        if (income.Deleted || income.Version != version) throw new ApiError("conflict", 409);
        income.Deleted = true;
        income.Version++;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private async Task RequireCategory(User user, Guid id, CancellationToken ct)
    {
        if (!await db.IncomeCategories.AnyAsync(x => x.UserId == user.Id && x.Id == id && !x.Deleted, ct)) throw new ApiError("invalid_category");
    }
}

public static class IncomeEndpoints
{
    public static void MapIncomes(this RouteGroupBuilder group)
    {
        group.MapGet("/income-categories", async (HttpContext context, UserService users, IncomeService incomes, CancellationToken ct) =>
            await incomes.Categories(await users.Current(context, ct), ct));
        group.MapPost("/income-categories", async (IncomeCategoryRequest input, HttpContext context, UserService users, IncomeService incomes, CancellationToken ct) =>
            await incomes.SaveCategory(await users.Current(context, ct), null, input, ct));
        group.MapPut("/income-categories/{id:guid}", async (Guid id, IncomeCategoryRequest input, HttpContext context, UserService users, IncomeService incomes, CancellationToken ct) =>
            await incomes.SaveCategory(await users.Current(context, ct), id, input, ct));
        group.MapDelete("/income-categories/{id:guid}", async (Guid id, int version, HttpContext context, UserService users, IncomeService incomes, CancellationToken ct) =>
        {
            await incomes.DeleteCategory(await users.Current(context, ct), id, version, ct);
            return Results.NoContent();
        });
        group.MapGet("/incomes", async (DateOnly? from, DateOnly? to, Guid? categoryId, int? page, HttpContext context, UserService users, IncomeService incomes, CancellationToken ct) =>
            await incomes.List(await users.Current(context, ct), from, to, categoryId, page ?? 0, ct));
        group.MapPost("/incomes", async (CreateIncomeRequest input, HttpContext context, UserService users, IncomeService incomes, CancellationToken ct) =>
            await incomes.Create(await users.Current(context, ct), input, ct));
        group.MapPut("/incomes/{id:guid}", async (Guid id, IncomeInput input, HttpContext context, UserService users, IncomeService incomes, CancellationToken ct) =>
            await incomes.Edit(await users.Current(context, ct), id, input, ct));
        group.MapDelete("/incomes/{id:guid}", async (Guid id, int version, HttpContext context, UserService users, IncomeService incomes, CancellationToken ct) =>
        {
            await incomes.Delete(await users.Current(context, ct), id, version, ct);
            return Results.NoContent();
        });
    }
}
