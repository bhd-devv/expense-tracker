using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using ExpenseTracker.Api.Features.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace ExpenseTracker.Api.Features.Auth;

public sealed class BotAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder, IConfiguration configuration, UserService users)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Path.StartsWithSegments("/internal/bot")) return AuthenticateResult.NoResult();
        var expected = configuration["Telegram:ServiceKey"] ?? "";
        var supplied = Request.Headers.Authorization.ToString();
        if (expected.Length < 32 || !CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes($"Bearer {expected}")), SHA256.HashData(Encoding.UTF8.GetBytes(supplied))))
            return AuthenticateResult.Fail("Invalid service credential");
        var claims = new List<Claim> { new("service", "bot") };
        var actor = Request.Headers["X-Telegram-User-Id"].ToString();
        if (actor.Length > 0)
        {
            if (!long.TryParse(actor, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
                return AuthenticateResult.Fail("Invalid actor");
            var user = await users.GetOrCreate(id, Request.Headers["X-Telegram-Language"], Context.RequestAborted);
            claims.Add(new("user_id", user.Id.ToString()));
        }
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name));
    }
}
