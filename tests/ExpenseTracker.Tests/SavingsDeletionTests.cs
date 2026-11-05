using System.Net;
using System.Net.Http.Json;
using ExpenseTracker.Api.Features.Savings;
using ExpenseTracker.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ExpenseTracker.Tests;

public sealed partial class PostgresTests
{
    [PostgresFact]
    public async Task SavingsDeletionProtectsOwnershipVersionsAndRetryWithoutRestoringTheJournal()
    {
        await using var factory = await Factory();
        var telegramId = Random.Shared.NextInt64(10_000_000_000, 90_000_000_000);
        using var browser = await Browser(factory, telegramId);
        using var stranger = await Browser(factory, telegramId + 1);
        var reportBefore = await browser.GetStringAsync("/api/reports");
        var create = new CreateSavingsRequest(Guid.NewGuid(), "Delete with history", "UAH");
        var response = await browser.PostAsJsonAsync("/api/savings", create);
        response.EnsureSuccessStatusCode();
        var account = await response.Content.ReadFromJsonAsync<SavingsAccountDto>() ?? throw new InvalidOperationException();
        Assert.Equal(1, account.Version);
        var path = $"/api/savings/{account.Id}";
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync(path + "?version=1")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.DeleteAsync(path)).StatusCode);
        var entry = new SavingsEntryRequest(Guid.NewGuid(), "deposit", "12.50", new(2026, 11, 5), "Synthetic deposit");
        (await browser.PostAsJsonAsync(path + "/entries", entry)).EnsureSuccessStatusCode();
        account = (await browser.GetFromJsonAsync<SavingsEntryPage>(path + "/entries"))?.Account ?? throw new InvalidOperationException();
        Assert.Equal(2, account.Version);
        Assert.Equal(HttpStatusCode.Conflict, (await browser.DeleteAsync(path + "?version=1")).StatusCode);
        var deletePath = path + $"?version={account.Version}";
        var duplicates = await Task.WhenAll(browser.DeleteAsync(deletePath), browser.DeleteAsync(deletePath));
        Assert.All(duplicates, result => Assert.Equal(HttpStatusCode.NoContent, result.StatusCode));
        Assert.Empty((await browser.GetFromJsonAsync<SavingsAccountsDto>("/api/savings"))?.Items ?? throw new InvalidOperationException());
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync(path + "/entries")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.PostAsJsonAsync(path + "/entries", entry)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await browser.PostAsJsonAsync("/api/savings", create)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await browser.DeleteAsync(path + "?version=999")).StatusCode);
        Assert.Equal(reportBefore, await browser.GetStringAsync("/api/reports"));

        // Empty accounts can also be deleted. A later account has its own fresh request ID.
        var empty = await (await browser.PostAsJsonAsync("/api/savings", create with { RequestId = Guid.NewGuid(), Name = "Empty" }))
            .Content.ReadFromJsonAsync<SavingsAccountDto>() ?? throw new InvalidOperationException();
        (await browser.DeleteAsync($"/api/savings/{empty.Id}?version={empty.Version}")).EnsureSuccessStatusCode();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        Assert.True((await db.SavingsAccounts.SingleAsync(x => x.Id == account.Id)).Deleted);
        Assert.Equal(1, await db.SavingsEntries.CountAsync(x => x.AccountId == account.Id));
    }

    [PostgresFact]
    public async Task ConcurrentSavingsEntryAndDeletionCannotSilentlyDeleteAnUnseenChange()
    {
        await using var factory = await Factory();
        var telegramId = Random.Shared.NextInt64(10_000_000_000, 90_000_000_000);
        using var browser = await Browser(factory, telegramId);
        var account = await (await browser.PostAsJsonAsync("/api/savings", new CreateSavingsRequest(Guid.NewGuid(), "Concurrent", "UAH")))
            .Content.ReadFromJsonAsync<SavingsAccountDto>() ?? throw new InvalidOperationException();
        var path = $"/api/savings/{account.Id}";
        var add = browser.PostAsJsonAsync(path + "/entries", new SavingsEntryRequest(Guid.NewGuid(), "deposit", "1", new(2026, 11, 5), "Synthetic"));
        var delete = browser.DeleteAsync(path + $"?version={account.Version}");
        await Task.WhenAll(add, delete);
        if (delete.Result.StatusCode == HttpStatusCode.NoContent)
            Assert.Equal(HttpStatusCode.NotFound, add.Result.StatusCode);
        else
        {
            Assert.Equal(HttpStatusCode.Conflict, delete.Result.StatusCode);
            Assert.Equal(HttpStatusCode.OK, add.Result.StatusCode);
            var current = (await browser.GetFromJsonAsync<SavingsEntryPage>(path + "/entries"))?.Account ?? throw new InvalidOperationException();
            Assert.Equal("1", current.Balance);
            (await browser.DeleteAsync(path + $"?version={current.Version}")).EnsureSuccessStatusCode();
        }
    }
}
