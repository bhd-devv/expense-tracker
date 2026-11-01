using ExpenseTracker.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Features.Users;

public sealed record ProfileDto(Guid Id, string Language, string Theme, string TimeZone, string Currency,
    Guid? DefaultAccountId, bool AiConsent, int Version, DateOnly Today);
public sealed record PreferencesRequest(string Language, string Theme, string TimeZone, string Currency,
    Guid? DefaultAccountId, bool AiConsent, int Version);

public sealed class UserService(Database db)
{
    public static readonly (string Code, string Name)[] Categories =
    [
        ("food", "Їжа та напої"), ("transport", "Транспорт"), ("shopping", "Покупки"),
        ("home", "Дім"), ("health", "Здоров'я"), ("leisure", "Дозвілля"), ("other", "Інше")
    ];

    public async Task<User> GetOrCreate(long telegramId, string? language, CancellationToken ct)
    {
        if (telegramId <= 0) throw new ApiError("unauthorized", 401);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({telegramId})", ct);
        var user = await db.Users.SingleOrDefaultAsync(x => x.TelegramId == telegramId, ct);
        if (user is null)
        {
            user = new User { TelegramId = telegramId, Language = language?.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true ? "en" : "uk" };
            var account = new Account { UserId = user.Id, Name = user.Language == "en" ? "Everyday" : "Основний" };
            user.DefaultAccountId = account.Id;
            db.Add(user);
            db.Add(account);
            db.AddRange(Categories.Select(x => new Category { UserId = user.Id, Code = x.Code, Name = x.Name }));
            await db.SaveChangesAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return user;
    }

    public async Task<User> Current(HttpContext context, CancellationToken ct)
    {
        if (!Guid.TryParse(context.User.FindFirst("user_id")?.Value, out var userId)) throw new ApiError("unauthorized", 401);
        return await db.Users.SingleOrDefaultAsync(x => x.Id == userId, ct) ?? throw new ApiError("unauthorized", 401);
    }

    public static ProfileDto View(User user) => new(user.Id, user.Language, user.Theme, user.TimeZone,
        user.Currency, user.DefaultAccountId, user.AiConsent, user.Version, Rules.Today(user, TimeProvider.System));

    public async Task<ProfileDto> Update(User user, PreferencesRequest input, CancellationToken ct)
    {
        if (input.Version != user.Version) throw new ApiError("conflict", 409);
        if (input.Language is not ("uk" or "en") || input.Theme is not ("auto" or "light" or "dark")
            || !Rules.Currencies.Contains(input.Currency)) throw new ApiError("invalid_preferences");
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(input.TimeZone, out _)) throw new ApiError("invalid_timezone");
        if (input.DefaultAccountId is { } accountId && !await db.Accounts.AnyAsync(x => x.UserId == user.Id && x.Id == accountId && !x.Archived, ct))
            throw new ApiError("invalid_account");
        user.Language = input.Language;
        user.Theme = input.Theme;
        user.TimeZone = input.TimeZone;
        user.Currency = input.Currency;
        user.DefaultAccountId = input.DefaultAccountId;
        user.AiConsent = input.AiConsent;
        user.Version++;
        await db.SaveChangesAsync(ct);
        return View(user);
    }
}
