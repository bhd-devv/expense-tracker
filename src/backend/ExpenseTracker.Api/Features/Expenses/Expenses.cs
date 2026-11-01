using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ExpenseTracker.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Features.Expenses;

public sealed record ExpenseInput(string Amount, string Currency, Guid CategoryId, string Description, DateOnly Date, int Version);
public sealed record ExpenseDto(Guid Id, string Amount, string Currency, string Description, DateOnly Date,
    Guid CategoryId, string Source, int Version);
public sealed record ExpensePage(IReadOnlyList<ExpenseDto> Items, int Page, bool HasMore);
public sealed record DraftDto(Guid Id, string Status, string? Amount, string Currency, string Description, string Transcript,
    DateOnly Date, Guid? CategoryId, string? ErrorCode, int Version, Guid? ExpenseId);
public sealed record CreateDraftRequest(Guid RequestId, string Text);
public sealed record ReportRow(string Currency, Guid CategoryId, string Amount, int Count);
public sealed record ReportTotal(string Currency, string Amount, int Count);
public sealed record ReportDto(IReadOnlyList<ReportTotal> Totals, IReadOnlyList<ReportRow> Categories);
public sealed record VersionRequest(int Version);
public sealed record DraftEditRequest(string? Amount, string Currency, string Description, Guid? CategoryId, DateOnly Date, int Version);

public sealed class ExpenseService(Database db, TimeProvider clock, OpenRouter ai, ILogger<ExpenseService>? logger = null)
{
    public static ExpenseDto View(Expense x) => new(x.Id, Rules.Money(x.Amount), x.Currency, x.Description, x.Date,
        x.CategoryId, x.Source, x.Version);

    public static DraftDto ViewDraft(Draft x) => new(
        x.Id, x.Status, x.Amount is { } amount ? Rules.Money(amount) : null, x.Currency, x.Description,
        x.Transcript, x.Date, x.CategoryId, x.ErrorCode, x.Version, x.ExpenseId);

    public async Task<ExpensePage> List(User user, DateOnly? from, DateOnly? to, Guid? categoryId, int page, CancellationToken ct)
    {
        if (page is < 0 or > 100000 || from > to) throw new ApiError("invalid_filter");
        var query = Filter(user.Id, from, to, categoryId);
        var items = await query.OrderByDescending(x => x.Date).ThenByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
            .Skip(page * 20).Take(21).ToListAsync(ct);
        return new(items.Take(20).Select(View).ToArray(), page, items.Count > 20);
    }

    private IQueryable<Expense> Filter(Guid userId, DateOnly? from, DateOnly? to, Guid? categoryId) =>
        db.Expenses.AsNoTracking().Where(x => x.UserId == userId && (from == null || x.Date >= from)
            && (to == null || x.Date <= to) && (categoryId == null || x.CategoryId == categoryId));

    public async Task<ReportDto> Report(User user, DateOnly? from, DateOnly? to, Guid? categoryId, CancellationToken ct)
    {
        if (from > to) throw new ApiError("invalid_filter");
        var rows = await Filter(user.Id, from, to, categoryId).GroupBy(x => new { x.Currency, x.CategoryId })
            .Select(g => new { g.Key.Currency, g.Key.CategoryId, Amount = g.Sum(x => x.Amount), Count = g.Count() }).ToListAsync(ct);
        return new(rows.GroupBy(x => x.Currency).Select(g => new ReportTotal(g.Key, Rules.Money(g.Sum(x => x.Amount)), g.Sum(x => x.Count))).ToArray(),
            rows.OrderByDescending(x => x.Amount).Select(x => new ReportRow(x.Currency, x.CategoryId, Rules.Money(x.Amount), x.Count)).ToArray());
    }

    public async Task<Expense> Find(User user, Guid id, CancellationToken ct) =>
        await db.Expenses.SingleOrDefaultAsync(x => x.UserId == user.Id && x.Id == id, ct) ?? throw new ApiError("not_found", 404);

    public async Task<Draft> FindDraft(User user, Guid id, CancellationToken ct) =>
        await db.Drafts.SingleOrDefaultAsync(x => x.UserId == user.Id && x.Id == id, ct) ?? throw new ApiError("not_found", 404);

    public async Task<DraftDto> Create(User user, Guid requestId, string text, string source, byte[]? audio, CancellationToken ct)
    {
        if (requestId == Guid.Empty) throw new ApiError("invalid_request_id");
        if (audio is null) text = Rules.Text(text, 2000);
        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(audio ?? Encoding.UTF8.GetBytes(text)));
        var inputHash = fingerprint;
        Draft draft;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({user.TelegramId})", ct);
            var existing = await db.Drafts.SingleOrDefaultAsync(x => x.UserId == user.Id && x.RequestId == requestId, ct);
            if (existing is not null)
            {
                if (existing.InputHash != inputHash) throw new ApiError("idempotency_conflict", 409);
                logger?.LogInformation("Draft creation replayed draftId={DraftId} userId={UserId}", existing.Id, user.Id);
                return ViewDraft(existing);
            }
            draft = new Draft
            {
                UserId = user.Id,
                RequestId = requestId,
                InputHash = inputHash,
                Currency = user.Currency,
                Date = Rules.Today(user, clock),
                Transcript = text,
                Source = source,
                InputKind = audio is null ? "text" : "voice",
                ExpiresAt = clock.GetUtcNow().AddMinutes(30)
            };
            db.Drafts.Add(draft);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        logger?.LogInformation("Draft created draftId={DraftId} userId={UserId} source={Source} inputKind={InputKind}",
            draft.Id, user.Id, source, draft.InputKind);
        try
        {
            if (!user.AiConsent) throw new ApiError("ai_consent_required");
            var categories = await db.Categories.Where(x => x.UserId == user.Id && !x.Archived).ToArrayAsync(ct);
            var currency = draft.Currency;
            var result = await ai.Parse(text, audio, categories, currency, ct);
            currency = result.Currency ?? currency;
            if (!Rules.Currencies.Contains(currency)) throw new ApiError("invalid_currency");
            draft.Currency = currency;
            draft.Transcript = audio is null ? text : Rules.Text(result.Transcript, 2000);
            draft.Description = string.IsNullOrWhiteSpace(result.Description) ? draft.Transcript : Rules.Text(result.Description);
            draft.Amount = result.Amount is null ? null : Rules.Amount(result.Amount, currency);
            draft.CategoryId = Guid.TryParse(result.CategoryId, out var category) && categories.Any(x => x.Id == category) ? category : null;
            draft.Status = "ready";
            draft.ErrorCode = draft.Amount is null || draft.CategoryId is null ? "needs_clarification" : null;
        }
        catch (Exception error) when (error is ApiError or HttpRequestException or JsonException or TaskCanceledException)
        {
            draft.Status = "failed";
            draft.ErrorCode = error is ApiError apiError ? apiError.Code : "ai_unavailable";
            logger?.LogWarning("Draft processing failed draftId={DraftId} code={Code} failure={Failure}",
                draft.Id, draft.ErrorCode, JsonSerializer.Serialize(SafeError.Describe(error)));
        }
        draft.Version++;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            logger?.LogWarning("Draft processing result superseded draftId={DraftId}", draft.Id);
            db.Entry(draft).State = EntityState.Detached;
        }
        logger?.LogInformation("Draft processing finished draftId={DraftId}", draft.Id);
        return ViewDraft(await FindDraft(user, draft.Id, ct));
    }

    public async Task<ExpenseDto> Confirm(User user, Guid id, ExpenseInput input, CancellationToken ct)
    {
        await using var transaction = await db.LockUser(user, ct);
        var draft = await db.Drafts.FromSqlInterpolated($"SELECT * FROM \"Drafts\" WHERE \"Id\" = {id} AND \"UserId\" = {user.Id} FOR UPDATE")
            .SingleOrDefaultAsync(ct) ?? throw new ApiError("not_found", 404);
        if (draft.ExpenseId is { } savedId)
        {
            var saved = await Find(user, savedId, ct);
            if (Rules.Amount(input.Amount, saved.Currency) != saved.Amount || input.CategoryId != saved.CategoryId
                || input.Currency != saved.Currency || Rules.Text(input.Description) != saved.Description || input.Date != saved.Date)
                throw new ApiError("idempotency_conflict", 409);
            logger?.LogInformation("Expense confirmation replayed expenseId={ExpenseId} userId={UserId}", saved.Id, user.Id);
            return View(saved);
        }
        if (draft.Status is "cancelled" or "processing" || draft.ExpiresAt <= clock.GetUtcNow()) throw new ApiError("draft_expired", 409);
        if (input.Version != draft.Version) throw new ApiError("conflict", 409);
        var expense = new Expense { Id = id, UserId = user.Id, Source = draft.Source };
        await Apply(user, expense, input, ct);
        db.Expenses.Add(expense);
        draft.ExpenseId = expense.Id;
        draft.Status = "saved";
        ClearContent(draft);
        draft.Version++;
        if (draft.Source == "bot")
            db.Deliveries.Add(new Delivery { Kind = "outbox", DedupeKey = $"saved:{id}", Payload = JsonSerializer.Serialize(new { kind = "saved", telegramId = user.TelegramId, expenseId = id }) });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        logger?.LogInformation("Expense saved expenseId={ExpenseId} userId={UserId} source={Source}", expense.Id, user.Id, draft.Source);
        return View(expense);
    }

    internal async Task Apply(User user, Expense expense, ExpenseInput input, CancellationToken ct)
    {
        var category = await db.Categories.SingleOrDefaultAsync(x => x.UserId == user.Id && x.Id == input.CategoryId, ct) ?? throw new ApiError("invalid_category");
        if (category.Archived && expense.CategoryId != category.Id)
            throw new ApiError("archived_reference");
        if (!Rules.Currencies.Contains(input.Currency)) throw new ApiError("invalid_currency");
        expense.Amount = Rules.Amount(input.Amount, input.Currency);
        expense.Currency = input.Currency;
        expense.CategoryId = category.Id;
        expense.Description = Rules.Text(input.Description);
        if (input.Date == default) throw new ApiError("invalid_date");
        expense.Date = input.Date;
    }

    public async Task<ExpenseDto> Edit(User user, Guid id, ExpenseInput input, CancellationToken ct)
    {
        await using var transaction = await db.LockUser(user, ct);
        var expense = await Find(user, id, ct);
        if (input.Version != expense.Version) throw new ApiError("conflict", 409);
        await Apply(user, expense, input, ct);
        expense.Version++;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        logger?.LogInformation("Expense edited expenseId={ExpenseId} userId={UserId} version={Version}", expense.Id, user.Id, expense.Version);
        return View(expense);
    }

    public async Task Delete(User user, Guid id, int version, CancellationToken ct)
    {
        await using var transaction = await db.LockUser(user, ct);
        var expense = await Find(user, id, ct);
        if (expense.Version != version) throw new ApiError("conflict", 409);
        db.Remove(expense);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        logger?.LogInformation("Expense deleted expenseId={ExpenseId} userId={UserId}", id, user.Id);
    }

    private static void ClearContent(Draft draft)
    {
        draft.Transcript = "";
        draft.Description = "";
        draft.Amount = null;
        draft.CategoryId = null;
    }

    public async Task<DraftDto> EditDraft(User user, Guid id, DraftEditRequest input, CancellationToken ct)
    {
        await using var transaction = await db.LockUser(user, ct);
        var draft = await FindDraft(user, id, ct);
        if (draft.Status is "cancelled" or "saved" or "processing" || draft.ExpiresAt <= clock.GetUtcNow()) throw new ApiError("draft_expired", 409);
        if (input.Version != draft.Version) throw new ApiError("conflict", 409);
        if (input.CategoryId is { } categoryId && !await db.Categories.AnyAsync(x => x.UserId == user.Id && x.Id == categoryId && !x.Archived, ct)) throw new ApiError("invalid_category");
        if (!Rules.Currencies.Contains(input.Currency)) throw new ApiError("invalid_currency");
        draft.Currency = input.Currency;
        draft.Amount = string.IsNullOrWhiteSpace(input.Amount) ? null : Rules.Amount(input.Amount, input.Currency);
        draft.Description = string.IsNullOrEmpty(input.Description) ? "" : Rules.Text(input.Description);
        if (input.Date == default) throw new ApiError("invalid_date");
        draft.Date = input.Date;
        draft.CategoryId = input.CategoryId;
        draft.Version++;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        logger?.LogInformation("Draft edited draftId={DraftId} userId={UserId} version={Version}", draft.Id, user.Id, draft.Version);
        return ViewDraft(draft);
    }

    public async Task<DraftDto> Cancel(User user, Guid id, int version, CancellationToken ct)
    {
        await using var transaction = await db.LockUser(user, ct);
        var draft = await FindDraft(user, id, ct);
        if (draft.ExpenseId is not null) throw new ApiError("already_saved", 409);
        if (draft.Status == "cancelled") return ViewDraft(draft);
        if (draft.Version != version) throw new ApiError("conflict", 409);
        draft.Status = "cancelled";
        ClearContent(draft);
        draft.Version++;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        logger?.LogInformation("Draft cancelled draftId={DraftId} userId={UserId}", draft.Id, user.Id);
        return ViewDraft(draft);
    }
}
