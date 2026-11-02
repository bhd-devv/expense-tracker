using ExpenseTracker.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Features.Users;

public sealed record ProfileDto(Guid Id, string Language, string Theme, string TimeZone, string Currency,
    bool AiConsent, int Version, DateOnly Today, string InputMode = "ai");
public sealed record PreferencesRequest(string Language, string Theme, string TimeZone, string Currency,
    bool AiConsent, int Version, string? InputMode = null);

public sealed class UserService(Database db, ILogger<UserService>? logger = null)
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
        var created = user is null;
        if (user is null)
        {
            user = new User { TelegramId = telegramId, Language = language?.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true ? "en" : "uk" };
            db.Add(user);
            db.AddRange(Categories.Select(x => new Category { UserId = user.Id, Code = x.Code, Name = x.Name }));
            await db.SaveChangesAsync(ct);
        }
        await transaction.CommitAsync(ct);
        if (created) logger?.LogInformation("User created userId={UserId}", user.Id);
        return user;
    }

    public async Task<User> Current(HttpContext context, CancellationToken ct)
    {
        if (!Guid.TryParse(context.User.FindFirst("user_id")?.Value, out var userId)) throw new ApiError("unauthorized", 401);
        return await db.Users.SingleOrDefaultAsync(x => x.Id == userId, ct) ?? throw new ApiError("unauthorized", 401);
    }

    public static ProfileDto View(User user) => new(user.Id, user.Language, user.Theme, user.TimeZone,
        user.Currency, user.AiConsent, user.Version, Rules.Today(user, TimeProvider.System), user.InputMode);

    public async Task<ProfileDto> Update(User user, PreferencesRequest input, CancellationToken ct)
    {
        if (input.Version != user.Version) throw new ApiError("conflict", 409);
        if (input.Language is not ("uk" or "en") || input.Theme is not ("auto" or "light" or "dark")
            || !Rules.Currencies.Contains(input.Currency)
            || input.InputMode is not (null or "manual" or "ai")) throw new ApiError("invalid_preferences");
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(input.TimeZone, out _)) throw new ApiError("invalid_timezone");
        user.Language = input.Language;
        user.Theme = input.Theme;
        user.TimeZone = input.TimeZone;
        user.Currency = input.Currency;
        user.AiConsent = input.AiConsent;
        user.InputMode = input.InputMode ?? user.InputMode;
        user.Version++;
        await db.SaveChangesAsync(ct);
        logger?.LogInformation("Preferences saved userId={UserId} version={Version}", user.Id, user.Version);
        return View(user);
    }
}
