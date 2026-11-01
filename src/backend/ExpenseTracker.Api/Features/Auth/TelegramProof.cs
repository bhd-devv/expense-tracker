using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ExpenseTracker.Api.Infrastructure;
using Microsoft.AspNetCore.WebUtilities;

namespace ExpenseTracker.Api.Features.Auth;

public sealed record VerifiedTelegramUser(long Id, string? Language, string Fingerprint, DateTimeOffset ExpiresAt);

public sealed class TelegramValidationOptions
{
    public int ProofMaxAgeSeconds { get; set; } = 300;
    public int ProofFutureSkewSeconds { get; set; } = 30;
}

public static class TelegramProof
{
    public static VerifiedTelegramUser Validate(string initData, string botToken, DateTimeOffset now, int maxAgeSeconds = 300, int futureSkewSeconds = 30)
    {
        if (string.IsNullOrEmpty(initData) || initData.Length > 16384 || string.IsNullOrWhiteSpace(botToken)) throw new ApiError("invalid_telegram_proof", 401);
        var fields = QueryHelpers.ParseQuery(initData);
        if (fields.Any(x => x.Value.Count != 1) || !fields.TryGetValue("hash", out var supplied))
            throw new ApiError("invalid_telegram_proof", 401);
        var canonical = string.Join('\n', fields.Where(x => x.Key != "hash").OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => $"{x.Key}={x.Value}"));
        var key = HMACSHA256.HashData(Encoding.UTF8.GetBytes("WebAppData"), Encoding.UTF8.GetBytes(botToken));
        var expected = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(canonical));
        byte[] hash;
        try { hash = Convert.FromHexString(supplied.ToString()); }
        catch (FormatException) { throw new ApiError("invalid_telegram_proof", 401); }
        if (!CryptographicOperations.FixedTimeEquals(expected, hash)) throw new ApiError("invalid_telegram_proof", 401);
        if (!fields.TryGetValue("auth_date", out var date) || !long.TryParse(date, NumberStyles.None, CultureInfo.InvariantCulture, out var unix)
            || unix < now.AddSeconds(-maxAgeSeconds).ToUnixTimeSeconds() || unix > now.AddSeconds(futureSkewSeconds).ToUnixTimeSeconds())
            throw new ApiError("expired_telegram_proof", 401);
        try
        {
            using var user = JsonDocument.Parse(fields["user"].ToString());
            var id = user.RootElement.GetProperty("id").GetInt64();
            if (id <= 0) throw new ApiError("invalid_telegram_proof", 401);
            var language = user.RootElement.TryGetProperty("language_code", out var lang) ? lang.GetString() : null;
            return new(id, language, Convert.ToHexStringLower(expected), DateTimeOffset.FromUnixTimeSeconds(unix).AddSeconds(maxAgeSeconds));
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        { throw new ApiError("invalid_telegram_proof", 401); }
    }
}
