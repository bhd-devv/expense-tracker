using System.Net;
using System.Net.Http.Json;
using ExpenseTracker.Api.Features.Auth;
using ExpenseTracker.Api.Features.Catalogs;
using ExpenseTracker.Api.Features.Expenses;
using ExpenseTracker.Api.Features.Users;
using ExpenseTracker.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ExpenseTracker.Tests;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("EXPENSE_TEST_DATABASE")))
            Skip = "Set EXPENSE_TEST_DATABASE to an isolated database ending in _test";
    }
}

public sealed class PostgresTests
{
    private static async Task<WebApplicationFactory<Program>> Factory(Action<IServiceCollection>? configure = null)
    {
        var connection = Environment.GetEnvironmentVariable("EXPENSE_TEST_DATABASE") ?? throw new InvalidOperationException("No test database");
        if (!(new Npgsql.NpgsqlConnectionStringBuilder(connection).Database ?? "").EndsWith("_test", StringComparison.Ordinal))
            throw new InvalidOperationException("Integration tests require a database ending in _test");
        Environment.SetEnvironmentVariable("ConnectionStrings__Database", connection);
        Environment.SetEnvironmentVariable("Telegram__BotToken", SecurityTests.BotToken);
        Environment.SetEnvironmentVariable("Telegram__ServiceKey", SecurityTests.ServiceKey);
        Environment.SetEnvironmentVariable("App__Origin", "https://localhost");
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => { builder.UseEnvironment("Development"); if (configure is not null) builder.ConfigureServices(configure); });
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<Database>().Database.MigrateAsync();
        return factory;
    }

    private static async Task<HttpClient> Browser(WebApplicationFactory<Program> factory, long id, DateTimeOffset? proofTime = null)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("Origin", "https://localhost");
        await Csrf(client);
        (await client.PostAsJsonAsync("/api/auth/telegram", new LoginRequest(SecurityTests.Proof(id, proofTime ?? DateTimeOffset.UtcNow)))).EnsureSuccessStatusCode();
        await Csrf(client);
        return client;
    }

    private static async Task Csrf(HttpClient client)
    {
        var csrf = await client.GetFromJsonAsync<CsrfDto>("/api/auth/csrf") ?? throw new InvalidOperationException();
        client.DefaultRequestHeaders.Remove("X-CSRF-Token");
        client.DefaultRequestHeaders.Add("X-CSRF-Token", csrf.Token);
    }

    [PostgresFact]
    public async Task SharedChannelsOwnershipIdempotencyAndConcurrency()
    {
        await using var factory = await Factory();
        var userId = Random.Shared.NextInt64(1_000_000, 9_000_000_000);
        using var browser = await Browser(factory, userId);
        using var stranger = await Browser(factory, userId + 1);
        var catalog = await browser.GetFromJsonAsync<CatalogDto>("/api/catalog") ?? throw new InvalidOperationException();
        var draft = await (await browser.PostAsJsonAsync("/api/drafts", new CreateDraftRequest(Guid.NewGuid(), "Кава 85", null)))
            .Content.ReadFromJsonAsync<DraftDto>() ?? throw new InvalidOperationException();
        var input = new ExpenseInput("85.00", catalog.Accounts[0].Id, catalog.Categories[0].Id, "Кава", draft.Date, draft.Version);
        var first = await browser.PostAsJsonAsync($"/api/drafts/{draft.Id}/confirm", input);
        first.EnsureSuccessStatusCode();
        var expense = await first.Content.ReadFromJsonAsync<ExpenseDto>() ?? throw new InvalidOperationException();
        (await browser.PostAsJsonAsync($"/api/drafts/{draft.Id}/confirm", input)).EnsureSuccessStatusCode();
        Assert.Single((await browser.GetFromJsonAsync<ExpensePage>("/api/expenses"))!.Items);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/expenses/{expense.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await browser.PostAsJsonAsync($"/api/drafts/{draft.Id}/confirm", input with { Amount = "90" })).StatusCode);
        var edited = await browser.PutAsJsonAsync($"/api/expenses/{expense.Id}", input with { Amount = "90", Version = expense.Version });
        edited.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await browser.PutAsJsonAsync($"/api/expenses/{expense.Id}", input with { Version = expense.Version })).StatusCode);
        using var bot = factory.CreateClient();
        bot.DefaultRequestHeaders.Authorization = new("Bearer", SecurityTests.ServiceKey);
        bot.DefaultRequestHeaders.Add("X-Telegram-User-Id", userId.ToString());
        var shared = await bot.GetFromJsonAsync<ExpensePage>("/internal/bot/actor/expenses");
        Assert.Equal("90", Assert.Single(shared!.Items).Amount);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/internal/bot/actor/profile")).StatusCode);
    }

    [PostgresFact]
    public async Task CsrfLogoutAndProofReplayAreEnforced()
    {
        await using var factory = await Factory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var proof = new LoginRequest(SecurityTests.Proof(Random.Shared.NextInt64(10_000_000, 99_000_000), DateTimeOffset.UtcNow));
        client.DefaultRequestHeaders.Add("Origin", "https://localhost");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/auth/telegram", proof)).StatusCode);
        await Csrf(client);
        (await client.PostAsJsonAsync("/api/auth/telegram", proof)).EnsureSuccessStatusCode();
        await Csrf(client);
        (await client.PostAsync("/api/auth/logout", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        await Csrf(client);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/telegram", proof)).StatusCode);
    }
    [PostgresFact]
    public async Task ConcurrentConfirmationHasOneEffectAndForeignReferencesAreRejected()
    {
        await using var factory = await Factory();
        var userId = Random.Shared.NextInt64(100_000_000, 900_000_000);
        using var browser = await Browser(factory, userId);
        using var stranger = await Browser(factory, userId + 1);
        var own = await browser.GetFromJsonAsync<CatalogDto>("/api/catalog") ?? throw new InvalidOperationException();
        var foreign = await stranger.GetFromJsonAsync<CatalogDto>("/api/catalog") ?? throw new InvalidOperationException();
        var draft = await (await browser.PostAsJsonAsync("/api/drafts", new CreateDraftRequest(Guid.NewGuid(), "Taxi 100", null))).Content.ReadFromJsonAsync<DraftDto>() ?? throw new InvalidOperationException();
        var input = new ExpenseInput("100", own.Accounts[0].Id, own.Categories[0].Id, "Taxi", draft.Date, draft.Version);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await browser.PostAsJsonAsync($"/api/drafts/{draft.Id}/confirm", input with { AccountId = foreign.Accounts[0].Id })).StatusCode);
        var results = await Task.WhenAll(browser.PostAsJsonAsync($"/api/drafts/{draft.Id}/confirm", input), browser.PostAsJsonAsync($"/api/drafts/{draft.Id}/confirm", input));
        Assert.All(results, result => Assert.Equal(HttpStatusCode.OK, result.StatusCode));
        Assert.Single((await browser.GetFromJsonAsync<ExpensePage>("/api/expenses"))!.Items);
        var expense = await results[0].Content.ReadFromJsonAsync<ExpenseDto>() ?? throw new InvalidOperationException();
        Assert.Equal(HttpStatusCode.Conflict, (await browser.PutAsJsonAsync($"/api/accounts/{own.Accounts[0].Id}", new CatalogRequest("Taxi account", "USD", false, own.Accounts[0].Version))).StatusCode);
        (await browser.DeleteAsync($"/api/expenses/{expense.Id}?version={expense.Version}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await browser.PostAsJsonAsync($"/api/drafts/{draft.Id}/confirm", input)).StatusCode);
    }

    [PostgresFact]
    public async Task DurableInboxDeduplicatesAndFencesLeaseAcknowledgement()
    {
        await using var factory = await Factory();
        using var bot = factory.CreateClient();
        bot.DefaultRequestHeaders.Authorization = new("Bearer", SecurityTests.ServiceKey);
        var updateId = Random.Shared.NextInt64(1_000_000, 9_000_000_000);
        using var json = System.Text.Json.JsonDocument.Parse("{\"update_id\":1}");
        var body = new ExpenseTracker.Api.Features.BotIntegration.InboxRequest(updateId, json.RootElement);
        (await bot.PostAsJsonAsync("/internal/bot/inbox", body)).EnsureSuccessStatusCode();
        (await bot.PostAsJsonAsync("/internal/bot/inbox", body)).EnsureSuccessStatusCode();
        var lease = await (await bot.PostAsync("/internal/bot/queues/inbox/claim", null)).Content.ReadFromJsonAsync<ExpenseTracker.Api.Features.BotIntegration.LeaseDto>() ?? throw new InvalidOperationException();
        Assert.Equal(HttpStatusCode.Conflict, (await bot.PostAsJsonAsync($"/internal/bot/queues/{lease.Id}/result", new { token = Guid.NewGuid(), success = true })).StatusCode);
        (await bot.PostAsJsonAsync($"/internal/bot/queues/{lease.Id}/result", new { token = lease.Token, success = true })).EnsureSuccessStatusCode();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        Assert.Equal(1, await db.Deliveries.CountAsync(x => x.Kind == "inbox" && x.DedupeKey == updateId.ToString()));
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 1, 21, 59, 59, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class AiHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => reply(request);
    }

    [PostgresFact]
    public async Task AiDraftFreezesLocalDateAndIdempotentRetryDoesNotCallProviderAgain()
    {
        var clock = new Clock();
        var category = Guid.Empty;
        var calls = 0;
        await using var factory = await Factory(services =>
        {
            services.AddSingleton<TimeProvider>(clock);
            services.AddHttpClient<OpenRouter>().ConfigurePrimaryHttpMessageHandler(() => new AiHandler(_ =>
            {
                calls++;
                clock.Now = clock.Now.AddSeconds(2);
                var content = System.Text.Json.JsonSerializer.Serialize(new { amount = "85.20", currency = "UAH", categoryId = category.ToString(), description = "Кава", transcript = "Кава 85 гривень 20 копійок" });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { choices = new[] { new { message = new { content } } } }) });
            }));
        });
        // Configure fake credentials only. Requests go to the injected in-memory HTTP handler.
        var configuration = factory.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
        configuration["OpenRouter:ApiKey"] = "synthetic-key";
        configuration["OpenRouter:TextModel"] = "synthetic-model";
        using var browser = await Browser(factory, Random.Shared.NextInt64(100_000_000, 900_000_000), clock.Now);
        var profile = await browser.GetFromJsonAsync<ProfileDto>("/api/profile") ?? throw new InvalidOperationException();
        (await browser.PutAsJsonAsync("/api/profile", new PreferencesRequest(profile.Language, profile.Theme, profile.TimeZone, profile.Currency, profile.DefaultAccountId, true, profile.Version))).EnsureSuccessStatusCode();
        var catalog = await browser.GetFromJsonAsync<CatalogDto>("/api/catalog") ?? throw new InvalidOperationException();
        category = catalog.Categories.First(x => x.Code == "food").Id;
        var input = new CreateDraftRequest(Guid.NewGuid(), "Кава 85 гривень 20 копійок", null);
        var response = await browser.PostAsJsonAsync("/api/drafts", input);
        response.EnsureSuccessStatusCode();
        var draft = await response.Content.ReadFromJsonAsync<DraftDto>() ?? throw new InvalidOperationException();
        Assert.Equal("ready", draft.Status);
        Assert.Equal("85.2", draft.Amount);
        Assert.Equal(category, draft.CategoryId);
        Assert.Equal(new DateOnly(2026, 11, 1), draft.Date);
        (await browser.PostAsJsonAsync("/api/drafts", input)).EnsureSuccessStatusCode();
        Assert.Equal(1, calls);
        var saved = await browser.PostAsJsonAsync($"/api/drafts/{draft.Id}/confirm", new ExpenseInput("85.2", catalog.Accounts[0].Id, category, draft.Description, draft.Date, draft.Version));
        saved.EnsureSuccessStatusCode();
    }

}
