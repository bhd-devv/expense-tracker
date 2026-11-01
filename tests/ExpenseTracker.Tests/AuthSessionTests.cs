using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace ExpenseTracker.Tests;

public sealed class AuthSessionTests
{
    private static WebApplicationFactory<Program> Factory() => new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Database"] = "Host=127.0.0.1;Database=unused_test;Username=synthetic;Password=synthetic",
                    ["App:Origin"] = "https://localhost"
                }));
        });

    private static CookieValidatePrincipalContext Context(IServiceProvider services, ClaimsPrincipal principal, bool expired = false)
    {
        var options = services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var http = new DefaultHttpContext { RequestServices = services, User = principal };
        http.Request.Scheme = "https";
        var absolute = DateTimeOffset.UtcNow.AddDays(expired ? -1 : 1).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var properties = new AuthenticationProperties { Items = { ["absolute"] = absolute } };
        var scheme = new AuthenticationScheme(CookieAuthenticationDefaults.AuthenticationScheme, null, typeof(CookieAuthenticationHandler));
        return new CookieValidatePrincipalContext(http, scheme, options, new AuthenticationTicket(principal, properties, scheme.Name));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CurrentAndLegacySessionsCanObtainAuthenticatedAntiforgeryTokens(bool legacy)
    {
        await using var factory = Factory();
        await using var scope = factory.Services.CreateAsyncScope();
        var userId = Guid.NewGuid().ToString();
        var identity = new ClaimsIdentity([new Claim("user_id", userId)], "Session");
        if (!legacy) identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId));
        var context = Context(scope.ServiceProvider, new ClaimsPrincipal(identity));
        await context.Options.Events.ValidatePrincipal(context);
        Assert.NotNull(context.Principal);
        Assert.Equal(userId, context.Principal.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        Assert.Equal(legacy, context.ShouldRenew);
        var csrf = scope.ServiceProvider.GetRequiredService<IAntiforgery>().GetAndStoreTokens(context.HttpContext);
        Assert.False(string.IsNullOrWhiteSpace(csrf.RequestToken));
        Assert.False(string.IsNullOrWhiteSpace(csrf.CookieToken));
    }

    [Theory]
    [InlineData("mismatched")]
    [InlineData("missing")]
    [InlineData("invalid")]
    [InlineData("expired")]
    public async Task InvalidSessionsAreRejectedWithoutUpgrade(string reason)
    {
        await using var factory = Factory();
        await using var scope = factory.Services.CreateAsyncScope();
        var identity = new ClaimsIdentity("Session");
        if (reason != "missing")
            identity.AddClaim(new Claim("user_id", reason == "invalid" ? "not-a-guid" : Guid.NewGuid().ToString()));
        if (reason == "mismatched")
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()));
        var context = Context(scope.ServiceProvider, new ClaimsPrincipal(identity), reason == "expired");
        await context.Options.Events.ValidatePrincipal(context);
        Assert.Null(context.Principal);
        Assert.False(context.ShouldRenew);
    }
}
