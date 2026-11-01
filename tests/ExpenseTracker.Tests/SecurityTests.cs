using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ExpenseTracker.Api.Features.Auth;
using ExpenseTracker.Api.Infrastructure;
using Xunit;

namespace ExpenseTracker.Tests;

public sealed class SecurityTests
{
    internal const string BotToken = "123456:test-token-used-only-in-local-tests";
    internal const string ServiceKey = "local-test-service-key-not-for-production-12345678";

    internal static string Proof(long userId, DateTimeOffset now, string token = BotToken)
    {
        var fields = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["auth_date"] = now.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["query_id"] = Guid.NewGuid().ToString(),
            ["user"] = JsonSerializer.Serialize(new { id = userId, language_code = "uk" })
        };
        var canonical = string.Join('\n', fields.Select(x => $"{x.Key}={x.Value}"));
        var key = HMACSHA256.HashData(Encoding.UTF8.GetBytes("WebAppData"), Encoding.UTF8.GetBytes(token));
        fields["hash"] = Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(canonical)));
        return string.Join('&', fields.Select(x => $"{x.Key}={Uri.EscapeDataString(x.Value)}"));
    }

    [Fact]
    public void TelegramProofAcceptsSignedIdentityAndRejectsTampering()
    {
        var now = DateTimeOffset.UtcNow;
        var proof = Proof(123, now);
        Assert.Equal(123, TelegramProof.Validate(proof, BotToken, now).Id);
        Assert.Throws<ApiError>(() => TelegramProof.Validate(proof.Replace("123", "456"), BotToken, now));
        Assert.Throws<ApiError>(() => TelegramProof.Validate(proof, "wrong-token", now));
        Assert.Throws<ApiError>(() => TelegramProof.Validate(proof + "&auth_date=0", BotToken, now));
    }

    [Fact]
    public void TelegramProofRejectsExpiredAndFutureMessages()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<ApiError>(() => TelegramProof.Validate(Proof(1, now.AddMinutes(-6)), BotToken, now));
        Assert.Throws<ApiError>(() => TelegramProof.Validate(Proof(1, now.AddMinutes(1)), BotToken, now));
    }

    [Theory]
    [InlineData("0", "UAH")]
    [InlineData("-2", "UAH")]
    [InlineData("1.001", "UAH")]
    [InlineData("1.1", "JPY")]
    [InlineData("1e3", "USD")]
    [InlineData("NaN", "EUR")]
    public void MoneyRejectsInvalidAmounts(string value, string currency) =>
        Assert.Throws<ApiError>(() => Rules.Amount(value, currency));

    [Fact]
    public void TodayUsesUsersTimezone()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 31, 23, 30, 0, TimeSpan.Zero));
        Assert.Equal(new DateOnly(2026, 11, 1), Rules.Today(new User { TimeZone = "Europe/Kyiv" }, clock));
        Assert.Equal(new DateOnly(2026, 10, 31), Rules.Today(new User { TimeZone = "America/New_York" }, clock));
    }
}

internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
