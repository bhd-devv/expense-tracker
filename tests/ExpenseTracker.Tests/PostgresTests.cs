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

    [PostgresFact]
    public async Task OctoberSeedIsAtomicOncePerActorAndVisibleAcrossChannels()
    {
        await using var factory = await Factory();
        var telegramId = Random.Shared.NextInt64(10_000_000_000, 90_000_000_000);
        using var browser = await Browser(factory, telegramId);
        using var stranger = await Browser(factory, telegramId + 1);
        var profile = await browser.GetFromJsonAsync<ProfileDto>("/api/profile") ?? throw new InvalidOperationException();
        var catalog = await browser.GetFromJsonAsync<CatalogDto>("/api/catalog") ?? throw new InvalidOperationException();
        using var bot = factory.CreateClient();
        bot.DefaultRequestHeaders.Authorization = new("Bearer", SecurityTests.ServiceKey);
        Assert.Equal(HttpStatusCode.Forbidden, (await bot.PostAsync("/internal/bot/actor/seed", null)).StatusCode);
        bot.DefaultRequestHeaders.Add("X-Telegram-User-Id", telegramId.ToString());
        var replies = await Task.WhenAll(bot.PostAsync("/internal/bot/actor/seed", null), bot.PostAsync("/internal/bot/actor/seed", null));
        var results = new List<SeedResultDto>();
        foreach (var reply in replies)
        {
            reply.EnsureSuccessStatusCode();
            results.Add(await reply.Content.ReadFromJsonAsync<SeedResultDto>() ?? throw new InvalidOperationException());
        }
        Assert.Equal(new[] { 0, 70 }, results.Select(x => x.CreatedCount).Order().ToArray());
        Assert.All(results, result =>
        {
            Assert.Equal(70, result.TotalCount);
            Assert.Equal(new DateOnly(2026, 10, 1), result.From);
            Assert.Equal(new DateOnly(2026, 10, 31), result.To);
        });
        Assert.Equal(profile, await browser.GetFromJsonAsync<ProfileDto>("/api/profile"));
        var after = await browser.GetFromJsonAsync<CatalogDto>("/api/catalog") ?? throw new InvalidOperationException();
        Assert.Equal(catalog.Categories, after.Categories);
        Assert.Equal(catalog.Accounts.Count + 4, after.Accounts.Count);
        var seeded = new List<ExpenseDto>();
        for (var page = 0; page < 4; page++)
        {
            var expenses = await browser.GetFromJsonAsync<ExpensePage>($"/api/expenses?from=2026-10-01&to=2026-10-31&page={page}") ?? throw new InvalidOperationException();
            Assert.Equal(page < 3, expenses.HasMore);
            seeded.AddRange(expenses.Items);
        }
        Assert.Equal(70, seeded.Count);
        Assert.Equal(31, seeded.Select(x => x.Date).Distinct().Count());
        Assert.Equal(7, seeded.Select(x => x.CategoryId).Distinct().Count());
        Assert.All(seeded, expense =>
        {
            Assert.Equal("bot", expense.Source);
            Assert.StartsWith("Демо · ", expense.Description);
            Assert.InRange(expense.Date, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));
        });
        var report = await browser.GetFromJsonAsync<ReportDto>("/api/reports?from=2026-10-01&to=2026-10-31") ?? throw new InvalidOperationException();
        Assert.Equal(64, report.Totals.Single(x => x.Currency == "UAH").Count);
        Assert.Equal(3, report.Totals.Single(x => x.Currency == "USD").Count);
        Assert.Equal(3, report.Totals.Single(x => x.Currency == "EUR").Count);
        var expense = seeded[0];
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/expenses/{expense.Id}")).StatusCode);
        var edited = await browser.PutAsJsonAsync($"/api/expenses/{expense.Id}",
            new ExpenseInput("1.25", expense.AccountId, expense.CategoryId, "Edited demo", expense.Date, expense.Version));
        edited.EnsureSuccessStatusCode();
        var shared = await bot.GetFromJsonAsync<ExpenseDto>($"/internal/bot/actor/expenses/{expense.Id}");
        Assert.Equal("1.25", shared!.Amount);
        var removed = seeded[1];
        (await browser.DeleteAsync($"/api/expenses/{removed.Id}?version={removed.Version}")).EnsureSuccessStatusCode();
        var retry = await (await bot.PostAsync("/internal/bot/actor/seed", null)).Content.ReadFromJsonAsync<SeedResultDto>();
        Assert.Equal(0, retry!.CreatedCount);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync($"/api/expenses/{removed.Id}")).StatusCode);
        Assert.Equal("1.25", (await browser.GetFromJsonAsync<ExpenseDto>($"/api/expenses/{expense.Id}"))!.Amount);
        Assert.Empty((await stranger.GetFromJsonAsync<ExpensePage>("/api/expenses"))!.Items);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        Assert.Equal(69, await db.Expenses.CountAsync(x => x.UserId == profile.Id));
        Assert.Equal(1, await db.DemoSeeds.CountAsync(x => x.UserId == profile.Id));
    }

    [PostgresFact]
    public async Task SeedRespectsCatalogLimitAndArchivedCategories()
    {
        await using var factory = await Factory();
        var telegramId = Random.Shared.NextInt64(10_000_000_000, 90_000_000_000);
        using var browser = await Browser(factory, telegramId);
        var profile = await browser.GetFromJsonAsync<ProfileDto>("/api/profile") ?? throw new InvalidOperationException();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Database>();
            db.Accounts.AddRange(Enumerable.Range(1, 46).Select(i => new Account { UserId = profile.Id, Name = $"Existing {i}" }));
            var food = await db.Categories.SingleAsync(x => x.UserId == profile.Id && x.Code == "food");
            food.Archived = true;
            await db.SaveChangesAsync();
        }
        using var bot = factory.CreateClient();
        bot.DefaultRequestHeaders.Authorization = new("Bearer", SecurityTests.ServiceKey);
        bot.DefaultRequestHeaders.Add("X-Telegram-User-Id", telegramId.ToString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await bot.PostAsync("/internal/bot/actor/seed", null)).StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Database>();
            Assert.False(await db.DemoSeeds.AnyAsync(x => x.UserId == profile.Id));
            Assert.False(await db.Expenses.AnyAsync(x => x.UserId == profile.Id));
            Assert.Equal(7, await db.Categories.CountAsync(x => x.UserId == profile.Id));
            var extra = await db.Accounts.FirstAsync(x => x.UserId == profile.Id && x.Id != profile.DefaultAccountId);
            db.Accounts.Remove(extra);
            await db.SaveChangesAsync();
        }
        (await bot.PostAsync("/internal/bot/actor/seed", null)).EnsureSuccessStatusCode();
        var catalog = await browser.GetFromJsonAsync<CatalogDto>("/api/catalog") ?? throw new InvalidOperationException();
        Assert.Equal(50, catalog.Accounts.Count);
        Assert.Single(catalog.Categories.Where(x => x.Code == "food" && x.Archived));
        Assert.Single(catalog.Categories.Where(x => x.Code == "food" && !x.Archived));
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
