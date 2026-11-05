using ExpenseTracker.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Features.Savings;

public sealed record CreateSavingsRequest(Guid RequestId, string Name, string Currency);
public sealed record SavingsEntryRequest(Guid RequestId, string Kind, string Amount, DateOnly Date, string Description);
public sealed record SavingsAccountDto(Guid Id, string Name, string Currency, string Balance, string Added, string Spent, int Version);
public sealed record SavingsAccountsDto(IReadOnlyList<SavingsAccountDto> Items);
public sealed record SavingsEntryDto(Guid Id, string Kind, string Amount, DateOnly Date, string Description);
public sealed record SavingsEntryPage(SavingsAccountDto Account, IReadOnlyList<SavingsEntryDto> Items, int Page, bool HasMore);

public sealed class SavingsService(Database db, TimeProvider clock)
{
    public async Task<SavingsAccountsDto> List(User user, CancellationToken ct)
    {
        var accounts = await db.SavingsAccounts.AsNoTracking().Where(x => x.UserId == user.Id && !x.Deleted)
            .OrderBy(x => x.Name).ThenBy(x => x.Id).ToArrayAsync(ct);
        return new(accounts.Select(x => View(x, user)).ToArray());
    }

    private async Task<SavingsAccount> Find(User user, Guid id, CancellationToken ct) =>
        await db.SavingsAccounts.SingleOrDefaultAsync(x => x.UserId == user.Id && x.Id == id && !x.Deleted, ct)
            ?? throw new ApiError("not_found", 404);

    public async Task<SavingsEntryPage> Entries(User user, Guid id, int page, CancellationToken ct)
    {
        if (page is < 0 or > 10000) throw new ApiError("invalid_page");
        // Account totals and the page must describe the same committed journal state.
        await using var transaction = await db.LockUser(user, ct);
        var account = await Find(user, id, ct);
        var entries = await db.SavingsEntries.AsNoTracking().Where(x => x.UserId == user.Id && x.AccountId == id)
            .OrderByDescending(x => x.Date).ThenByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip(page * 20).Take(21).ToArrayAsync(ct);
        await transaction.CommitAsync(ct);
        return new(View(account, user), entries.Take(20).Select(ViewEntry).ToArray(), page, entries.Length > 20);
    }

    public async Task<SavingsAccountDto> Create(User user, CreateSavingsRequest input, CancellationToken ct)
    {
        if (input.RequestId == Guid.Empty) throw new ApiError("invalid_request_id");
        var name = Rules.Text(input.Name, 60);
        await using var transaction = await db.LockUser(user, ct);
        var previous = await db.SavingsAccounts.SingleOrDefaultAsync(x => x.Id == input.RequestId, ct);
        if (previous is not null)
        {
            if (previous.UserId != user.Id || previous.Deleted || previous.Name != name)
                throw new ApiError("conflict", 409);
            return View(previous, user);
        }
        if (await db.SavingsAccounts.CountAsync(x => x.UserId == user.Id && !x.Deleted, ct) >= 50) throw new ApiError("savings_limit");
        var account = new SavingsAccount { Id = input.RequestId, UserId = user.Id, Name = name, Currency = user.Currency };
        db.Add(account);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return View(account, user);
    }

    public async Task<SavingsEntryDto> Add(User user, Guid id, SavingsEntryRequest input, CancellationToken ct)
    {
        if (input.RequestId == Guid.Empty) throw new ApiError("invalid_request_id");
        await using var transaction = await db.LockUser(user, ct);
        var account = await Find(user, id, ct);
        var amount = Rules.Amount(input.Amount, user.Currency);
        if (input.Kind is not ("deposit" or "withdrawal")) throw new ApiError("invalid_savings_kind");
        if (input.Date == DateOnly.MinValue) throw new ApiError("invalid_date");
        var description = input.Description.Trim();
        if (description.Length > 500) throw new ApiError("invalid_text");
        var previous = await db.SavingsEntries.SingleOrDefaultAsync(x => x.Id == input.RequestId, ct);
        if (previous is not null)
        {
            if (previous.UserId != user.Id || previous.AccountId != id || previous.Kind != input.Kind
                || previous.Amount != amount || previous.Date != input.Date || previous.Description != description)
                throw new ApiError("conflict", 409);
            return ViewEntry(previous);
        }
        if (input.Kind == "withdrawal" && amount > account.Added - account.Spent)
            throw new ApiError("insufficient_savings");
        const decimal maxTotal = 999999999999999999999999m;
        if ((input.Kind == "deposit" ? account.Added : account.Spent) > maxTotal - amount)
            throw new ApiError("savings_total_limit");
        if (input.Kind == "deposit") account.Added += amount; else account.Spent += amount;
        account.Version++;
        var entry = new SavingsEntry
        {
            Id = input.RequestId,
            UserId = user.Id,
            AccountId = account.Id,
            Kind = input.Kind,
            Amount = amount,
            Date = input.Date,
            Description = description,
            CreatedAt = clock.GetUtcNow()
        };
        db.Add(entry);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return ViewEntry(entry);
    }

    public async Task Delete(User user, Guid id, int version, CancellationToken ct)
    {
        await using var transaction = await db.LockUser(user, ct);
        var account = await db.SavingsAccounts.SingleOrDefaultAsync(x => x.UserId == user.Id && x.Id == id, ct)
            ?? throw new ApiError("not_found", 404);
        if (account.Deleted && account.Version == version + 1) return;
        if (account.Deleted || account.Version != version) throw new ApiError("conflict", 409);
        account.Deleted = true;
        account.Version++;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private static SavingsAccountDto View(SavingsAccount account, User user) => new(account.Id, account.Name, user.Currency,
        Rules.Money(account.Added - account.Spent), Rules.Money(account.Added), Rules.Money(account.Spent), account.Version);
    private static SavingsEntryDto ViewEntry(SavingsEntry entry) => new(entry.Id, entry.Kind, Rules.Money(entry.Amount), entry.Date, entry.Description);
}
