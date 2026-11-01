using System.Text.Json;
using System.Text.Json.Serialization;
using ExpenseTracker.Api.Infrastructure;

namespace ExpenseTracker.Api.Features.Auth;

public sealed class TelegramBotIdentity(IHttpClientFactory clients, IConfiguration configuration, ILogger<TelegramBotIdentity>? logger = null) : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private string? username;

    public async Task<string> GetUsername(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (username is not null) return username;
            logger?.LogInformation("Telegram identity lookup started");
            var token = configuration["Telegram:BotToken"];
            if (string.IsNullOrWhiteSpace(token)) throw new ApiError("telegram_unavailable", 503);
            using var client = clients.CreateClient("TelegramIdentity");
            using var response = await client.GetAsync($"/bot{token}/getMe", ct);
            logger?.LogInformation("Telegram identity response status={Status}", (int)response.StatusCode);
            if (!response.IsSuccessStatusCode) throw new ApiError("telegram_unavailable", 503);
            var identity = await response.Content.ReadFromJsonAsync<GetMeResponse>(ct);
            if (identity is not { Ok: true, Result: { IsBot: true, Username: { Length: > 0 } name } }
                || !name.All(character => char.IsAsciiLetterOrDigit(character) || character == '_'))
                throw new ApiError("telegram_unavailable", 503);
            username = name;
            logger?.LogInformation("Telegram identity cached");
            return name;
        }
        catch (Exception error) when (error is HttpRequestException or JsonException
            || error is OperationCanceledException && !ct.IsCancellationRequested)
        {
            logger?.LogError("Telegram identity lookup failed: {Failure}", JsonSerializer.Serialize(SafeError.Describe(error)));
            throw new ApiError("telegram_unavailable", 503);
        }
        finally { gate.Release(); }
    }

    public void Dispose() => gate.Dispose();

    private sealed record GetMeResponse(bool Ok, BotUser? Result);
    private sealed record BotUser([property: JsonPropertyName("is_bot")] bool IsBot, string? Username);
}
