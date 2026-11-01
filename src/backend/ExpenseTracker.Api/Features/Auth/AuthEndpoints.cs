using System.Security.Claims;
using ExpenseTracker.Api.Features.Users;
using ExpenseTracker.Api.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Features.Auth;

public sealed record LoginRequest(string InitData);
public sealed record CsrfDto(string Token);
public sealed record PublicConfigDto(string BotUsername);

public static class AuthEndpoints
{
    public static void MapAuth(this WebApplication app)
    {
        app.MapGet("/api/config", async (TelegramBotIdentity bot, CancellationToken ct) =>
            new PublicConfigDto(await bot.GetUsername(ct)));
        app.MapGet("/api/auth/csrf", (HttpContext context, IAntiforgery csrf) =>
            new CsrfDto(csrf.GetAndStoreTokens(context).RequestToken ?? throw new InvalidOperationException("No antiforgery token")));
        app.MapGet("/api/auth/me", async (HttpContext context, UserService users, CancellationToken ct) =>
            UserService.View(await users.Current(context, ct))).RequireAuthorization("Browser");

        app.MapPost("/api/auth/telegram", async (LoginRequest input, HttpContext context, IConfiguration config,
            Database db, UserService users, TimeProvider clock, Microsoft.Extensions.Options.IOptions<TelegramValidationOptions> validation, CancellationToken ct) =>
        {
            var proof = TelegramProof.Validate(input.InitData, config["Telegram:BotToken"] ?? "", clock.GetUtcNow(), validation.Value.ProofMaxAgeSeconds, validation.Value.ProofFutureSkewSeconds);
            var user = await users.GetOrCreate(proof.Id, proof.Language, ct);
            if (await db.AuthProofs.AnyAsync(x => x.Id == proof.Fingerprint, ct))
            {
                if (context.User.FindFirst("user_id")?.Value == user.Id.ToString()) return UserService.View(user);
                throw new ApiError("replayed_telegram_proof", 401);
            }
            db.AuthProofs.Add(new AuthProof { Id = proof.Fingerprint, ExpiresAt = proof.ExpiresAt });
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException error) when (error.InnerException is Npgsql.PostgresException { SqlState: "23505" })
            { throw new ApiError("replayed_telegram_proof", 401); }
            var properties = new AuthenticationProperties
            {
                IsPersistent = true,
                IssuedUtc = clock.GetUtcNow(),
                ExpiresUtc = clock.GetUtcNow().AddHours(24),
                Items = { ["absolute"] = clock.GetUtcNow().AddDays(7).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture) }
            };
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("user_id", user.Id.ToString())], "Session"));
            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, properties);
            return UserService.View(user);
        }).RequireRateLimiting("auth");

        app.MapPost("/api/auth/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        }).RequireAuthorization("Browser");
    }
}
