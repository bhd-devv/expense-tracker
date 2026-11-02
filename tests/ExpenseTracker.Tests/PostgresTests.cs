using System.Net;
using System.Net.Http.Json;
using ExpenseTracker.Api.Features.Auth;
using ExpenseTracker.Api.Features.Catalogs;
using ExpenseTracker.Api.Features.Expenses;
using ExpenseTracker.Api.Features.Users;
using ExpenseTracker.Api.Features.Savings;
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

    [PostgresFact]
    public async Task SavingsAreSharedExactIdempotentAndIsolatedFromExpensesAndOtherOwners()
    {
        await using var factory = await Factory();
        var telegramId = Random.Shared.NextInt64(10_000_000_000, 90_000_000_000);
        using var browser = await Browser(factory, telegramId);
        using var stranger = await Browser(factory, telegramId + 1);
        using var bot = factory.CreateClient();
        bot.DefaultRequestHeaders.Authorization = new("Bearer", SecurityTests.ServiceKey);
        bot.DefaultRequestHeaders.Add("X-Telegram-User-Id", telegramId.ToString());
        var reportBefore = await browser.GetStringAsync("/api/reports");
        var create = new CreateSavingsRequest(Guid.NewGuid(), "Vacation", "USD");
        var created = await browser.PostAsJsonAsync("/api/savings", create);
        created.EnsureSuccessStatusCode();
        var account = await created.Content.ReadFromJsonAsync<SavingsAccountDto>() ?? throw new InvalidOperationException();
        Assert.Equal("0", account.Balance);
        Assert.Equal(account, await (await browser.PostAsJsonAsync("/api/savings", create)).Content.ReadFromJsonAsync<SavingsAccountDto>());
        Assert.Equal(HttpStatusCode.Conflict, (await browser.PostAsJsonAsync("/api/savings", create with { Name = "Changed" })).StatusCode);
        var path = $"/api/savings/{account.Id}/entries";
        var input = new SavingsEntryRequest(Guid.NewGuid(), "deposit", "0.10", new(2026, 11, 2), "Synthetic deposit");
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsJsonAsync(path, input)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(path)).StatusCode);
        var duplicate = await Task.WhenAll(browser.PostAsJsonAsync(path, input), browser.PostAsJsonAsync(path, input));
        foreach (var response in duplicate) response.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await browser.PostAsJsonAsync(path, input with { Amount = "1" })).StatusCode);
        foreach (var invalid in new SavingsEntryRequest[]
        {
            input with { RequestId = Guid.NewGuid(), Amount = "0" },
            input with { RequestId = Guid.NewGuid(), Amount = "-1" },
            input with { RequestId = Guid.NewGuid(), Amount = "0.001" },
            input with { RequestId = Guid.NewGuid(), Kind = "other" },
            input with { RequestId = Guid.NewGuid(), Date = default },
            input with { RequestId = Guid.Empty }
        })
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await browser.PostAsJsonAsync(path, invalid)).StatusCode);
        var internalPath = $"/internal/bot/actor/savings/{account.Id}/entries";
        (await bot.PostAsJsonAsync(internalPath, input with { RequestId = Guid.NewGuid(), Amount = "0.20" })).EnsureSuccessStatusCode();
        var current = await browser.GetFromJsonAsync<SavingsEntryPage>(path) ?? throw new InvalidOperationException();
        Assert.Equal("0.3", current.Account.Balance);
        Assert.Equal("0.3", current.Account.Added);
        Assert.Equal(2, current.Items.Count);
        // Two spenders see the same initial balance. Only one can withdraw 0.20.
        var spending = input with { RequestId = Guid.NewGuid(), Kind = "withdrawal", Amount = "0.20" };
        var concurrent = await Task.WhenAll(browser.PostAsJsonAsync(path, spending),
            bot.PostAsJsonAsync(internalPath, spending with { RequestId = Guid.NewGuid() }));
        Assert.Single(concurrent.Where(x => x.StatusCode == HttpStatusCode.OK));
        Assert.Single(concurrent.Where(x => x.StatusCode == HttpStatusCode.UnprocessableEntity));
        current = await browser.GetFromJsonAsync<SavingsEntryPage>(path) ?? throw new InvalidOperationException();
        Assert.Equal("0.1", current.Account.Balance);
        Assert.Equal("0.2", current.Account.Spent);
        Assert.Equal("UAH", current.Account.Currency);
        Assert.Equal(3, current.Items.Count);
        // Retrying the original deposit after subsequent spending must not add money.
        (await browser.PostAsJsonAsync(path, input)).EnsureSuccessStatusCode();
        Assert.Equal("0.1", (await bot.GetFromJsonAsync<SavingsEntryPage>(internalPath))?.Account.Balance);
        var profile = await browser.GetFromJsonAsync<ProfileDto>("/api/profile") ?? throw new InvalidOperationException();
        (await browser.PutAsJsonAsync("/api/profile", new PreferencesRequest(profile.Language, profile.Theme,
            profile.TimeZone, "EUR", profile.AiConsent, profile.Version))).EnsureSuccessStatusCode();
        Assert.Equal("EUR", (await browser.GetFromJsonAsync<SavingsEntryPage>(path))?.Account.Currency);
        Assert.Equal(reportBefore, await browser.GetStringAsync("/api/reports"));
        Assert.Empty((await stranger.GetFromJsonAsync<SavingsAccountsDto>("/api/savings"))?.Items ?? throw new InvalidOperationException());
        for (var i = 0; i < 20; i++)
            (await browser.PostAsJsonAsync(path, input with { RequestId = Guid.NewGuid(), Amount = "1", Date = new(2026, 10, 1) })).EnsureSuccessStatusCode();
        current = await browser.GetFromJsonAsync<SavingsEntryPage>(path) ?? throw new InvalidOperationException();
        Assert.Equal(20, current.Items.Count);
        Assert.True(current.HasMore);
        var next = await browser.GetFromJsonAsync<SavingsEntryPage>(path + "?page=1") ?? throw new InvalidOperationException();
        Assert.Equal(3, next.Items.Count);
        Assert.False(next.HasMore);
        Assert.Empty(current.Items.Select(x => x.Id).Intersect(next.Items.Select(x => x.Id)));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await browser.GetAsync(path + "?page=-1")).StatusCode);
    }

    [PostgresFact]
    public async Task CategoryLimitsUseLocalMonthAndTrackEditsDeletesAndOlderClients()
    {
        var clock = new Clock { Now = new(2026, 11, 30, 22, 30, 0, TimeSpan.Zero) };
        await using var factory = await Factory(services => services.AddSingleton<TimeProvider>(clock));
        var telegramId = Random.Shared.NextInt64(1_000_000, 9_000_000_000);
        using var browser = await Browser(factory, telegramId, clock.Now);
        using var stranger = await Browser(factory, telegramId + 1, clock.Now);
        var catalog = await browser.GetFromJsonAsync<CatalogDto>("/api/catalog") ?? throw new InvalidOperationException();
        var category = catalog.Categories[0];
        Assert.Null(category.Limit);
        var path = $"/api/categories/{category.Id}/limit";
        var input = new CategoryLimitRequest("100", "UAH", category.Version);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PutAsJsonAsync(path, input)).StatusCode);
        var response = await browser.PutAsJsonAsync(path, input);
        response.EnsureSuccessStatusCode();
        category = await response.Content.ReadFromJsonAsync<CatalogItemDto>() ?? throw new InvalidOperationException();
        var limit = category.Limit ?? throw new InvalidOperationException();
        Assert.Equal(new DateOnly(2026, 12, 1), limit.From);
        Assert.Equal(new DateOnly(2026, 12, 31), limit.To);
        Assert.Equal("0", limit.Spent);
        Assert.Equal(0m, limit.UsedPercent);
        Assert.Equal(HttpStatusCode.Conflict, (await browser.PutAsJsonAsync(path, input)).StatusCode);
        foreach (var invalid in new CategoryLimitRequest[]
        {
            new("0", "UAH", category.Version), new("-1", "UAH", category.Version),
            new("0.001", "UAH", category.Version), new("10.001", "JPY", category.Version), new(null, "UAH", category.Version)
        })
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await browser.PutAsJsonAsync(path, invalid)).StatusCode);

        async Task<ExpenseDto> Save(string amount, string currency, Guid categoryId, DateOnly day)
        {
            var created = await browser.PostAsJsonAsync("/api/drafts", new CreateDraftRequest(Guid.NewGuid(), "", "manual"));
            created.EnsureSuccessStatusCode();
            var draft = await created.Content.ReadFromJsonAsync<DraftDto>() ?? throw new InvalidOperationException();
            var saved = await browser.PostAsJsonAsync($"/api/drafts/{draft.Id}/confirm",
                new ExpenseInput(amount, currency, categoryId, "Budget sample", day, draft.Version));
            saved.EnsureSuccessStatusCode();
            return await saved.Content.ReadFromJsonAsync<ExpenseDto>() ?? throw new InvalidOperationException();
        }
        async Task<CategoryLimitDto> Current()
        {
            var current = await browser.GetFromJsonAsync<CatalogDto>("/api/catalog") ?? throw new InvalidOperationException();
            return current.Categories.Single(x => x.Id == category.Id).Limit ?? throw new InvalidOperationException();
        }
        var first = await Save("40", "UAH", category.Id, new(2026, 12, 1));
        await Save("900", "USD", category.Id, new(2026, 12, 1));
        await Save("200", "UAH", catalog.Categories[1].Id, new(2026, 12, 1));
        await Save("30", "UAH", category.Id, new(2026, 11, 30));
        await Save("5", "UAH", category.Id, new(2027, 1, 1));
        var pending = await browser.PostAsJsonAsync("/api/drafts", new CreateDraftRequest(Guid.NewGuid(), "", "manual"));
        pending.EnsureSuccessStatusCode();
        var pendingDraft = await pending.Content.ReadFromJsonAsync<DraftDto>() ?? throw new InvalidOperationException();
        (await browser.PutAsJsonAsync($"/api/drafts/{pendingDraft.Id}",
            new DraftEditRequest("5000", "UAH", "Pending", category.Id, new(2026, 12, 1), pendingDraft.Version))).EnsureSuccessStatusCode();
        Assert.Equal("940", (await Current()).Spent);
        Assert.Equal(940m, (await Current()).UsedPercent);
        var last = await Save("85", "UAH", category.Id, new(2026, 12, 31));
        limit = await Current();
        Assert.Equal("1025", limit.Spent);
        Assert.Equal(1025m, limit.UsedPercent);
        Assert.True(limit.Exceeded);
        (await browser.PutAsJsonAsync($"/api/expenses/{last.Id}", new ExpenseInput("70", "UAH", category.Id,
            last.Description, last.Date, last.Version))).EnsureSuccessStatusCode();
        Assert.Equal("1010", (await Current()).Spent);
        (await browser.DeleteAsync($"/api/expenses/{first.Id}?version={first.Version}")).EnsureSuccessStatusCode();
        limit = await Current();
        Assert.Equal("970", limit.Spent);
        Assert.True(limit.Exceeded);

        // An older rename/icon client omits all budget fields and must preserve the limit.
        response = await browser.PutAsJsonAsync($"/api/categories/{category.Id}",
            new CatalogRequest(category.Name, category.Archived, category.Version, "coffee"));
        response.EnsureSuccessStatusCode();
        category = await response.Content.ReadFromJsonAsync<CatalogItemDto>() ?? throw new InvalidOperationException();
        Assert.Equal(limit, category.Limit);
        var profile = await browser.GetFromJsonAsync<ProfileDto>("/api/profile") ?? throw new InvalidOperationException();
        (await browser.PutAsJsonAsync("/api/profile", new PreferencesRequest(profile.Language, profile.Theme,
            profile.TimeZone, "USD", profile.AiConsent, profile.Version))).EnsureSuccessStatusCode();
        Assert.Equal("USD", (await Current()).Currency);
        using var bot = factory.CreateClient();
        bot.DefaultRequestHeaders.Authorization = new("Bearer", SecurityTests.ServiceKey);
        bot.DefaultRequestHeaders.Add("X-Telegram-User-Id", telegramId.ToString());
        response = await bot.PutAsJsonAsync($"/internal/bot/actor/categories/{category.Id}/limit",
            new CategoryLimitRequest("1000", "USD", category.Version));
        response.EnsureSuccessStatusCode();
        category = await response.Content.ReadFromJsonAsync<CatalogItemDto>() ?? throw new InvalidOperationException();
        Assert.Equal("970", (await Current()).Spent);
        Assert.Equal(97m, (await Current()).UsedPercent);
        clock.Now = new(2026, 12, 31, 22, 30, 0, TimeSpan.Zero);
        using var january = await Browser(factory, telegramId, clock.Now);
        var nextCatalog = await january.GetFromJsonAsync<CatalogDto>("/api/catalog") ?? throw new InvalidOperationException();
        limit = nextCatalog.Categories.Single(x => x.Id == category.Id).Limit ?? throw new InvalidOperationException();
        Assert.Equal(new DateOnly(2027, 1, 1), limit.From);
        Assert.Equal("5", limit.Spent);
        response = await january.PutAsJsonAsync(path, new CategoryLimitRequest(null, null, category.Version));
        response.EnsureSuccessStatusCode();
        Assert.Null((await response.Content.ReadFromJsonAsync<CatalogItemDto>())?.Limit);
    }

    [PostgresFact]
    public async Task CategoryDeletionChecksOwnershipVersionAndReferencesWithoutChangingExpenses()
    {
        await using var factory = await Factory();
        var telegramId = Random.Shared.NextInt64(1_000_000, 9_000_000_000);
        using var browser = await Browser(factory, telegramId);
        using var stranger = await Browser(factory, telegramId + 1);
        var catalog = await browser.GetFromJsonAsync<CatalogDto>("/api/catalog") ?? throw new InvalidOperationException();
        var category = catalog.Categories[0];
        var path = $"/api/categories/{category.Id}";
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync($"{path}?version={category.Version}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await browser.DeleteAsync($"{path}?version={category.Version - 1}")).StatusCode);

        var created = await browser.PostAsJsonAsync("/api/drafts", new CreateDraftRequest(Guid.NewGuid(), "", "manual"));
        created.EnsureSuccessStatusCode();
        var draft = await created.Content.ReadFromJsonAsync<DraftDto>() ?? throw new InvalidOperationException();
        var edited = await browser.PutAsJsonAsync($"/api/drafts/{draft.Id}",
            new DraftEditRequest("12.50", "UAH", "Lunch", category.Id, draft.Date, draft.Version));
        edited.EnsureSuccessStatusCode();
        draft = await edited.Content.ReadFromJsonAsync<DraftDto>() ?? throw new InvalidOperationException();
        var blocked = await browser.DeleteAsync($"{path}?version={category.Version}");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, blocked.StatusCode);
        Assert.Contains("category_in_use", await blocked.Content.ReadAsStringAsync());

        var saved = await browser.PostAsJsonAsync($"/api/drafts/{draft.Id}/confirm",
            new ExpenseInput("12.50", "UAH", category.Id, "Lunch", draft.Date, draft.Version));
        saved.EnsureSuccessStatusCode();
        var expense = await saved.Content.ReadFromJsonAsync<ExpenseDto>() ?? throw new InvalidOperationException();
        var report = await browser.GetStringAsync("/api/reports");
        blocked = await browser.DeleteAsync($"{path}?version={category.Version}");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, blocked.StatusCode);
        Assert.Contains("category_in_use", await blocked.Content.ReadAsStringAsync());
        Assert.Equal(expense, await browser.GetFromJsonAsync<ExpenseDto>($"/api/expenses/{expense.Id}"));
        Assert.Equal(report, await browser.GetStringAsync("/api/reports"));

        (await browser.DeleteAsync($"/api/expenses/{expense.Id}?version={expense.Version}")).EnsureSuccessStatusCode();
        using var bot = factory.CreateClient();
        bot.DefaultRequestHeaders.Authorization = new("Bearer", SecurityTests.ServiceKey);
        bot.DefaultRequestHeaders.Add("X-Telegram-User-Id", telegramId.ToString());
        Assert.Equal(HttpStatusCode.NoContent, (await bot.DeleteAsync($"/internal/bot/actor/categories/{category.Id}?version={category.Version}")).StatusCode);
        catalog = await browser.GetFromJsonAsync<CatalogDto>("/api/catalog") ?? throw new InvalidOperationException();
        Assert.DoesNotContain(catalog.Categories, item => item.Id == category.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.DeleteAsync($"{path}?version={category.Version}")).StatusCode);
    }

    [PostgresFact]
    public async Task CategoryIconChangesPreserveNamesAndRespectOwnershipVersionsAndOlderClients()
    {
        await using var factory = await Factory();
        var telegramId = Random.Shared.NextInt64(1_000_000, 9_000_000_000);
        using var browser = await Browser(factory, telegramId);
        using var stranger = await Browser(factory, telegramId + 1);
        var catalog = await browser.GetFromJsonAsync<CatalogDto>("/api/catalog") ?? throw new InvalidOperationException();
        var original = catalog.Categories.First(item => item.Code == "food");
        Assert.Equal("utensils-crossed", original.Icon);
        var input = new CatalogRequest(original.Name, original.Archived, original.Version, "coffee");
        var response = await browser.PutAsJsonAsync($"/api/categories/{original.Id}", input);
        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<CatalogItemDto>() ?? throw new InvalidOperationException();
        Assert.Equal(original.Code, updated.Code);
        Assert.Equal(original.Name, updated.Name);
        Assert.Equal("coffee", updated.Icon);
        Assert.Equal(HttpStatusCode.Conflict, (await browser.PutAsJsonAsync($"/api/categories/{original.Id}", input)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PutAsJsonAsync($"/api/categories/{original.Id}", input with { Version = updated.Version })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await browser.PutAsJsonAsync($"/api/categories/{original.Id}",
            input with { Version = updated.Version, Icon = "<svg>" })).StatusCode);
        // Renaming/archiving from an older client omits icon and must preserve the selected one.
        response = await browser.PutAsJsonAsync($"/api/categories/{original.Id}", new CatalogRequest("My coffee", true, updated.Version));
        response.EnsureSuccessStatusCode();
        updated = await response.Content.ReadFromJsonAsync<CatalogItemDto>() ?? throw new InvalidOperationException();
        Assert.Equal("coffee", updated.Icon);
        Assert.Null(updated.Code);
        using var bot = factory.CreateClient();
        bot.DefaultRequestHeaders.Authorization = new("Bearer", SecurityTests.ServiceKey);
        bot.DefaultRequestHeaders.Add("X-Telegram-User-Id", telegramId.ToString());
        var shared = await bot.GetFromJsonAsync<CatalogDto>("/internal/bot/actor/catalog") ?? throw new InvalidOperationException();
        Assert.Equal(updated, shared.Categories.First(item => item.Id == original.Id));
        var library = await bot.GetFromJsonAsync<CategoryIconLibraryDto>("/internal/bot/actor/category-icons") ?? throw new InvalidOperationException();
        Assert.Contains(library.Groups.SelectMany(group => group.Icons), icon => icon.Id == updated.Icon);
        var created = await browser.PostAsJsonAsync("/api/categories", new CatalogRequest("Cycling", false, 0, "bike"));
        created.EnsureSuccessStatusCode();
        Assert.Equal("bike", (await created.Content.ReadFromJsonAsync<CatalogItemDto>())?.Icon);
    }

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
        var draft = await (await browser.PostAsJsonAsync("/api/drafts", new CreateDraftRequest(Guid.NewGuid(), "Кава 85")))
            .Content.ReadFromJsonAsync<DraftDto>() ?? throw new InvalidOperationException();
        var input = new ExpenseInput("85.00", "UAH", catalog.Categories[0].Id, "Кава", draft.Date, draft.Version);
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
    public async Task ManualEntrySharesPreferenceAcrossChannelsAndSkipsAiWithoutConsent()
    {
        var calls = 0;
        await using var factory = await Factory(services =>
            services.AddHttpClient<OpenRouter>().ConfigurePrimaryHttpMessageHandler(() => new AiHandler(_ =>
            {
                calls++;
                throw new InvalidOperationException("Manual entry must not call AI");
            })));
        var telegramId = Random.Shared.NextInt64(1_000_000, 9_000_000_000);
        using var browser = await Browser(factory, telegramId);
        var profile = await browser.GetFromJsonAsync<ProfileDto>("/api/profile") ?? throw new InvalidOperationException();
        var preferences = new PreferencesRequest(profile.Language, profile.Theme, profile.TimeZone, profile.Currency,
            false, profile.Version, "manual");
        (await browser.PutAsJsonAsync("/api/profile", preferences)).EnsureSuccessStatusCode();
        using var bot = factory.CreateClient();
        bot.DefaultRequestHeaders.Authorization = new("Bearer", SecurityTests.ServiceKey);
        bot.DefaultRequestHeaders.Add("X-Telegram-User-Id", telegramId.ToString());
        var shared = await bot.GetFromJsonAsync<ProfileDto>("/internal/bot/actor/profile") ?? throw new InvalidOperationException();
        Assert.Equal("manual", shared.InputMode);
        Assert.False(shared.AiConsent);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await browser.PutAsJsonAsync("/api/profile",
            preferences with { Version = shared.Version, InputMode = "invalid" })).StatusCode);
        var input = new CreateDraftRequest(Guid.NewGuid(), "");
        var response = await bot.PostAsJsonAsync("/internal/bot/actor/drafts", input);
        response.EnsureSuccessStatusCode();
        var draft = await response.Content.ReadFromJsonAsync<DraftDto>() ?? throw new InvalidOperationException();
        Assert.Equal("ready", draft.Status);
        Assert.Null(draft.ErrorCode);
        Assert.Equal(shared.Today, draft.Date);
        Assert.Equal(shared.Currency, draft.Currency);
        Assert.Equal(draft, await (await bot.PostAsJsonAsync("/internal/bot/actor/drafts", input)).Content.ReadFromJsonAsync<DraftDto>());
        Assert.Equal(HttpStatusCode.Conflict, (await bot.PostAsJsonAsync("/internal/bot/actor/drafts", input with { Mode = "ai", Text = "Coffee 85" })).StatusCode);
        var catalog = await browser.GetFromJsonAsync<CatalogDto>("/api/catalog") ?? throw new InvalidOperationException();
        var confirmation = new ExpenseInput("12.50", "EUR", catalog.Categories[0].Id, "Manual coffee", draft.Date.AddDays(-1), draft.Version);
        var saved = await browser.PostAsJsonAsync($"/api/drafts/{draft.Id}/confirm", confirmation);
        saved.EnsureSuccessStatusCode();
        Assert.Equal(confirmation.Date, (await saved.Content.ReadFromJsonAsync<ExpenseDto>())?.Date);
        (await browser.PostAsJsonAsync($"/api/drafts/{draft.Id}/confirm", confirmation)).EnsureSuccessStatusCode();
        var sharedExpenses = await bot.GetFromJsonAsync<ExpensePage>("/internal/bot/actor/expenses") ?? throw new InvalidOperationException();
        Assert.Single(sharedExpenses.Items);
        // Older clients can update preferences without resetting the newly selected mode.
        (await browser.PutAsJsonAsync("/api/profile", preferences with { Version = shared.Version, InputMode = null })).EnsureSuccessStatusCode();
        Assert.Equal("manual", (await browser.GetFromJsonAsync<ProfileDto>("/api/profile"))?.InputMode);
        Assert.Equal(0, calls);
    }

    [PostgresFact]
    public async Task ProfileCurrencyRelabelsDraftsExpensesReportsAndLimitsWithoutChangingAmounts()
    {
        await using var factory = await Factory();
        var telegramId = Random.Shared.NextInt64(100_000_000, 900_000_000);
        using var browser = await Browser(factory, telegramId);
        var profile = await browser.GetFromJsonAsync<ProfileDto>("/api/profile") ?? throw new InvalidOperationException();
        var catalog = await browser.GetFromJsonAsync<CatalogDto>("/api/catalog") ?? throw new InvalidOperationException();
        var category = catalog.Categories[0];
        (await browser.PutAsJsonAsync($"/api/categories/{category.Id}/limit",
            new CategoryLimitRequest("100.50", "EUR", category.Version))).EnsureSuccessStatusCode();
        var input = new CreateDraftRequest(Guid.NewGuid(), "Lunch", "manual");
        var created = await browser.PostAsJsonAsync("/api/drafts", input);
        created.EnsureSuccessStatusCode();
        var draft = await created.Content.ReadFromJsonAsync<DraftDto>() ?? throw new InvalidOperationException();
        var edited = await browser.PutAsJsonAsync($"/api/drafts/{draft.Id}",
            new DraftEditRequest("12.50", "EUR", "Lunch", category.Id, draft.Date, draft.Version));
        edited.EnsureSuccessStatusCode();
        draft = await edited.Content.ReadFromJsonAsync<DraftDto>() ?? throw new InvalidOperationException();
        Assert.Equal("UAH", draft.Currency);
        var accountInput = new CreateSavingsRequest(Guid.NewGuid(), "Relabel sample", "EUR");
        var accountResponse = await browser.PostAsJsonAsync("/api/savings", accountInput);
        accountResponse.EnsureSuccessStatusCode();
        var account = await accountResponse.Content.ReadFromJsonAsync<SavingsAccountDto>() ?? throw new InvalidOperationException();
        Assert.Equal("UAH", account.Currency);
        var savingsPath = $"/api/savings/{account.Id}/entries";
        (await browser.PostAsJsonAsync(savingsPath,
            new SavingsEntryRequest(Guid.NewGuid(), "deposit", "12.50", draft.Date, "Relabel sample"))).EnsureSuccessStatusCode();
        var updated = await browser.PutAsJsonAsync("/api/profile",
            new PreferencesRequest(profile.Language, profile.Theme, profile.TimeZone, "JPY", false, profile.Version));
        updated.EnsureSuccessStatusCode();
        profile = await updated.Content.ReadFromJsonAsync<ProfileDto>() ?? throw new InvalidOperationException();
        var replay = await (await browser.PostAsJsonAsync("/api/drafts", input)).Content.ReadFromJsonAsync<DraftDto>()
            ?? throw new InvalidOperationException();
        Assert.Equal("JPY", replay.Currency);
        Assert.Equal("12.5", replay.Amount);
        Assert.Equal(draft.Version, replay.Version);
        // An already open client sends its old currency. The backend uses the profile.
        var confirmation = new ExpenseInput("12.50", "EUR", category.Id, "Lunch", draft.Date, draft.Version);
        var saved = await browser.PostAsJsonAsync($"/api/drafts/{draft.Id}/confirm", confirmation);
        saved.EnsureSuccessStatusCode();
        var expense = await saved.Content.ReadFromJsonAsync<ExpenseDto>() ?? throw new InvalidOperationException();
        Assert.Equal("JPY", expense.Currency);
        Assert.Equal("12.5", expense.Amount);
        (await browser.PutAsJsonAsync("/api/profile",
            new PreferencesRequest(profile.Language, profile.Theme, profile.TimeZone, "USD", false, profile.Version))).EnsureSuccessStatusCode();
        using var bot = factory.CreateClient();
        bot.DefaultRequestHeaders.Authorization = new("Bearer", SecurityTests.ServiceKey);
        bot.DefaultRequestHeaders.Add("X-Telegram-User-Id", telegramId.ToString());
        var shared = await bot.GetFromJsonAsync<ExpenseDto>($"/internal/bot/actor/expenses/{expense.Id}")
            ?? throw new InvalidOperationException();
        Assert.Equal("USD", shared.Currency);
        Assert.Equal(expense.Amount, shared.Amount);
        Assert.Equal(expense.Version, shared.Version);
        (await browser.PostAsJsonAsync($"/api/drafts/{draft.Id}/confirm", confirmation with { Currency = "JPY" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict,
            (await browser.PostAsJsonAsync($"/api/drafts/{draft.Id}/confirm", confirmation with { Amount = "13" })).StatusCode);
        var report = await browser.GetFromJsonAsync<ReportDto>("/api/reports") ?? throw new InvalidOperationException();
        Assert.Equal("USD", Assert.Single(report.Totals).Currency);
        Assert.Equal("12.5", Assert.Single(report.Totals).Amount);
        Assert.Single((await browser.GetFromJsonAsync<ExpensePage>("/api/expenses"))?.Items ?? []);
        catalog = await browser.GetFromJsonAsync<CatalogDto>("/api/catalog") ?? throw new InvalidOperationException();
        var limit = catalog.Categories.Single(x => x.Id == category.Id).Limit ?? throw new InvalidOperationException();
        Assert.Equal("USD", limit.Currency);
        Assert.Equal("100.5", limit.Amount);
        Assert.Equal("12.5", limit.Spent);
        var journal = await bot.GetFromJsonAsync<SavingsEntryPage>($"/internal/bot/actor/savings/{account.Id}/entries")
            ?? throw new InvalidOperationException();
        Assert.Equal("USD", journal.Account.Currency);
        Assert.Equal("12.5", journal.Account.Balance);
        Assert.Equal("12.5", Assert.Single(journal.Items).Amount);
        // A creation retry keeps the account ID after a currency preference change.
        var repeatedAccount = await browser.PostAsJsonAsync("/api/savings", accountInput);
        repeatedAccount.EnsureSuccessStatusCode();
        Assert.Equal(account.Id, (await repeatedAccount.Content.ReadFromJsonAsync<SavingsAccountDto>())?.Id);
        // Relabeling does not rewrite persisted amounts or historical currency metadata.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        var stored = await db.Expenses.AsNoTracking().SingleAsync(x => x.Id == expense.Id);
        Assert.Equal(12.50m, stored.Amount);
        Assert.Equal("JPY", stored.Currency);
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
        var draft = await (await browser.PostAsJsonAsync("/api/drafts", new CreateDraftRequest(Guid.NewGuid(), "Taxi 100"))).Content.ReadFromJsonAsync<DraftDto>() ?? throw new InvalidOperationException();
        var input = new ExpenseInput("100", "UAH", own.Categories[0].Id, "Taxi", draft.Date, draft.Version);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await browser.PostAsJsonAsync($"/api/drafts/{draft.Id}/confirm", input with { CategoryId = foreign.Categories[0].Id })).StatusCode);
        var results = await Task.WhenAll(browser.PostAsJsonAsync($"/api/drafts/{draft.Id}/confirm", input), browser.PostAsJsonAsync($"/api/drafts/{draft.Id}/confirm", input));
        Assert.All(results, result => Assert.Equal(HttpStatusCode.OK, result.StatusCode));
        Assert.Single((await browser.GetFromJsonAsync<ExpensePage>("/api/expenses"))!.Items);
        var expense = await results[0].Content.ReadFromJsonAsync<ExpenseDto>() ?? throw new InvalidOperationException();
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
        Assert.Equal("UAH", Assert.Single(report.Totals).Currency);
        Assert.Equal(70, Assert.Single(report.Totals).Count);
        var expense = seeded[0];
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/expenses/{expense.Id}")).StatusCode);
        var edited = await browser.PutAsJsonAsync($"/api/expenses/{expense.Id}",
            new ExpenseInput("1.25", expense.Currency, expense.CategoryId, "Edited demo", expense.Date, expense.Version));
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
            db.Categories.AddRange(Enumerable.Range(1, 43).Select(i => new Category { UserId = profile.Id, Name = $"Existing {i}" }));
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
            Assert.Equal(50, await db.Categories.CountAsync(x => x.UserId == profile.Id));
            var extra = await db.Categories.FirstAsync(x => x.UserId == profile.Id && x.Code == null);
            db.Categories.Remove(extra);
            await db.SaveChangesAsync();
        }
        (await bot.PostAsync("/internal/bot/actor/seed", null)).EnsureSuccessStatusCode();
        var catalog = await browser.GetFromJsonAsync<CatalogDto>("/api/catalog") ?? throw new InvalidOperationException();
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
                var content = System.Text.Json.JsonSerializer.Serialize(new { amount = "85.20", currency = "EUR", categoryId = category.ToString(), description = "Кава", transcript = "Кава 85 євро 20 центів", date = (string?)null, dateNeedsClarification = false });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { choices = new[] { new { message = new { content } } } }) });
            }));
        });
        // Configure fake credentials only. Requests go to the injected in-memory HTTP handler.
        var configuration = factory.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
        configuration["OpenRouter:ApiKey"] = "synthetic-key";
        configuration["OpenRouter:TextModel"] = "synthetic-model";
        using var browser = await Browser(factory, Random.Shared.NextInt64(100_000_000, 900_000_000), clock.Now);
        var profile = await browser.GetFromJsonAsync<ProfileDto>("/api/profile") ?? throw new InvalidOperationException();
        (await browser.PutAsJsonAsync("/api/profile", new PreferencesRequest(profile.Language, profile.Theme, profile.TimeZone, profile.Currency, true, profile.Version))).EnsureSuccessStatusCode();
        var catalog = await browser.GetFromJsonAsync<CatalogDto>("/api/catalog") ?? throw new InvalidOperationException();
        category = catalog.Categories.First(x => x.Code == "food").Id;
        var input = new CreateDraftRequest(Guid.NewGuid(), "Кава 85 євро 20 центів");
        var response = await browser.PostAsJsonAsync("/api/drafts", input);
        response.EnsureSuccessStatusCode();
        var draft = await response.Content.ReadFromJsonAsync<DraftDto>() ?? throw new InvalidOperationException();
        Assert.Equal("ready", draft.Status);
        Assert.Equal("85.2", draft.Amount);
        Assert.Equal(profile.Currency, draft.Currency);
        Assert.Equal(category, draft.CategoryId);
        Assert.Equal(new DateOnly(2026, 11, 1), draft.Date);
        (await browser.PostAsJsonAsync("/api/drafts", input)).EnsureSuccessStatusCode();
        Assert.Equal(1, calls);
        var saved = await browser.PostAsJsonAsync($"/api/drafts/{draft.Id}/confirm", new ExpenseInput("85.2", "EUR", category, draft.Description, draft.Date, draft.Version));
        saved.EnsureSuccessStatusCode();
    }

    [PostgresFact]
    public async Task AiDatesAreValidatedAndAmbiguousDatesRequireExplicitSelection()
    {
        string? proposedDate = null;
        var unclear = false;
        var category = Guid.Empty;
        var clock = new Clock();
        await using var factory = await Factory(services =>
        {
            services.AddSingleton<TimeProvider>(clock);
            services.AddHttpClient<OpenRouter>().ConfigurePrimaryHttpMessageHandler(() => new AiHandler(async request =>
            {
                using var body = System.Text.Json.JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                Assert.Contains("2026-11-01", body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
                clock.Now = clock.Now.AddSeconds(2);
                var content = System.Text.Json.JsonSerializer.Serialize(new { amount = "85", currency = "UAH",
                    categoryId = category.ToString(), description = "Кава", transcript = "Кава", date = proposedDate,
                    dateNeedsClarification = unclear });
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { choices = new[] { new { message = new { content } } } }) };
            }));
        });
        var config = factory.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
        config["OpenRouter:ApiKey"] = "synthetic-key";
        config["OpenRouter:TextModel"] = "synthetic-model";
        using var browser = await Browser(factory, Random.Shared.NextInt64(1_000_000, 9_000_000_000), clock.Now);
        var profile = await browser.GetFromJsonAsync<ProfileDto>("/api/profile") ?? throw new InvalidOperationException();
        (await browser.PutAsJsonAsync("/api/profile", new PreferencesRequest(profile.Language, profile.Theme,
            profile.TimeZone, profile.Currency, true, profile.Version))).EnsureSuccessStatusCode();
        var catalog = await browser.GetFromJsonAsync<CatalogDto>("/api/catalog") ?? throw new InvalidOperationException();
        category = catalog.Categories[0].Id;
        foreach (var sample in new (string? Date, bool Unclear, DateOnly Expected)[]
        {
            (null, false, new(2026, 11, 1)),
            ("2026-10-31", false, new(2026, 10, 31)),
            ("2026-02-30", false, default),
            (null, true, default),
            ("0001-01-01", false, default)
        })
        {
            clock.Now = new(2026, 11, 1, 21, 59, 59, TimeSpan.Zero);
            proposedDate = sample.Date;
            unclear = sample.Unclear;
            var input = new CreateDraftRequest(Guid.NewGuid(), "Кава 85 грн");
            var response = await browser.PostAsJsonAsync("/api/drafts", input);
            response.EnsureSuccessStatusCode();
            var draft = await response.Content.ReadFromJsonAsync<DraftDto>() ?? throw new InvalidOperationException();
            Assert.Equal(sample.Expected, draft.Date);
            Assert.Equal(draft, await (await browser.PostAsJsonAsync("/api/drafts", input)).Content.ReadFromJsonAsync<DraftDto>());
            if (sample.Expected == default)
            {
                Assert.Equal("date_needs_clarification", draft.ErrorCode);
                var confirmation = new ExpenseInput("85", "UAH", category, "Кава", draft.Date, draft.Version);
                Assert.Equal(HttpStatusCode.UnprocessableEntity, (await browser.PostAsJsonAsync($"/api/drafts/{draft.Id}/confirm", confirmation)).StatusCode);
                var edited = await browser.PutAsJsonAsync($"/api/drafts/{draft.Id}", new DraftEditRequest(draft.Amount,
                    draft.Currency, draft.Description, draft.CategoryId, new(2026, 11, 1), draft.Version));
                edited.EnsureSuccessStatusCode();
                draft = await edited.Content.ReadFromJsonAsync<DraftDto>() ?? throw new InvalidOperationException();
                Assert.Null(draft.ErrorCode);
                (await browser.PostAsJsonAsync($"/api/drafts/{draft.Id}/confirm", confirmation with { Date = draft.Date, Version = draft.Version })).EnsureSuccessStatusCode();
            }
        }
    }

}
