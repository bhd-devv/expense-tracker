using System.Net;
using System.Net.Http.Json;
using ExpenseTracker.Api.Features.Catalogs;
using ExpenseTracker.Api.Features.Incomes;
using ExpenseTracker.Api.Features.Users;
using Xunit;

namespace ExpenseTracker.Tests;

public sealed partial class PostgresTests
{
    [PostgresFact]
    public async Task IncomeIsExactScopedIdempotentAndSeparateFromExpenses()
    {
        await using var factory = await Factory();
        var telegramId = Random.Shared.NextInt64(10_000_000_000, 90_000_000_000);
        using var browser = await Browser(factory, telegramId);
        using var stranger = await Browser(factory, telegramId + 1);
        var expenseReport = await browser.GetStringAsync("/api/reports");
        var expenseCatalog = await browser.GetFromJsonAsync<CatalogDto>("/api/catalog") ?? throw new InvalidOperationException();
        var createCategory = new IncomeCategoryRequest(Guid.NewGuid(), "Salary", "tags", 0);
        var categoryResponse = await browser.PostAsJsonAsync("/api/income-categories", createCategory);
        categoryResponse.EnsureSuccessStatusCode();
        var category = await categoryResponse.Content.ReadFromJsonAsync<CatalogItemDto>() ?? throw new InvalidOperationException();
        Assert.Equal(category.Id, (await (await browser.PostAsJsonAsync("/api/income-categories", createCategory)).Content.ReadFromJsonAsync<CatalogItemDto>())?.Id);
        Assert.Empty((await stranger.GetFromJsonAsync<CatalogDto>("/api/income-categories"))?.Categories ?? throw new InvalidOperationException());
        Assert.Equal(HttpStatusCode.Conflict, (await browser.PostAsJsonAsync("/api/income-categories", createCategory with { Name = "Changed" })).StatusCode);
        var input = new CreateIncomeRequest(Guid.NewGuid(), "0.10", category.Id, "Salary", new(2026, 11, 5));
        var duplicates = await Task.WhenAll(browser.PostAsJsonAsync("/api/incomes", input), browser.PostAsJsonAsync("/api/incomes", input));
        foreach (var response in duplicates) response.EnsureSuccessStatusCode();
        var income = await duplicates[0].Content.ReadFromJsonAsync<IncomeDto>() ?? throw new InvalidOperationException();
        Assert.Equal("0.1", income.Amount);
        Assert.Equal(HttpStatusCode.Conflict, (await browser.PostAsJsonAsync("/api/incomes", input with { Amount = "2" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await stranger.PostAsJsonAsync("/api/incomes", input with { RequestId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await browser.PostAsJsonAsync("/api/incomes", input with { RequestId = Guid.NewGuid(), CategoryId = expenseCatalog.Categories[0].Id })).StatusCode);
        foreach (var amount in new[] { "0", "-1", "0.001" })
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await browser.PostAsJsonAsync("/api/incomes", input with { RequestId = Guid.NewGuid(), Amount = amount })).StatusCode);
        (await browser.PostAsJsonAsync("/api/incomes", input with { RequestId = Guid.NewGuid(), Amount = "0.20" })).EnsureSuccessStatusCode();
        var page = await browser.GetFromJsonAsync<IncomePage>("/api/incomes") ?? throw new InvalidOperationException();
        Assert.Equal("0.3", page.Total);
        Assert.Equal(2, page.Items.Count);
        Assert.Empty((await stranger.GetFromJsonAsync<IncomePage>("/api/incomes"))?.Items ?? throw new InvalidOperationException());
        Assert.Empty((await browser.GetFromJsonAsync<IncomePage>("/api/incomes?from=2026-11-06"))?.Items ?? throw new InvalidOperationException());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await browser.DeleteAsync($"/api/income-categories/{category.Id}?version={category.Version}")).StatusCode);
        var path = $"/api/incomes/{income.Id}";
        var edit = new IncomeInput("0.15", category.Id, "Updated", income.Date, income.Version);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PutAsJsonAsync(path, edit)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync(path + $"?version={income.Version}")).StatusCode);
        var updated = await (await browser.PutAsJsonAsync(path, edit)).Content.ReadFromJsonAsync<IncomeDto>() ?? throw new InvalidOperationException();
        Assert.Equal(updated, await (await browser.PutAsJsonAsync(path, edit)).Content.ReadFromJsonAsync<IncomeDto>());
        Assert.Equal(HttpStatusCode.Conflict, (await browser.PutAsJsonAsync(path, edit with { Amount = "0.16" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await browser.DeleteAsync(path + $"?version={income.Version}")).StatusCode);
        (await browser.DeleteAsync(path + $"?version={updated.Version}")).EnsureSuccessStatusCode();
        (await browser.DeleteAsync(path + $"?version={updated.Version}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await browser.PostAsJsonAsync("/api/incomes", input)).StatusCode);
        var remaining = Assert.Single((await browser.GetFromJsonAsync<IncomePage>("/api/incomes"))?.Items ?? throw new InvalidOperationException());
        (await browser.DeleteAsync($"/api/incomes/{remaining.Id}?version={remaining.Version}")).EnsureSuccessStatusCode();
        (await browser.DeleteAsync($"/api/income-categories/{category.Id}?version={category.Version}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await browser.PostAsJsonAsync("/api/income-categories", createCategory)).StatusCode);
        Assert.Equal(expenseReport, await browser.GetStringAsync("/api/reports"));
        Assert.Equal(expenseCatalog.Categories.Count, (await browser.GetFromJsonAsync<CatalogDto>("/api/catalog"))?.Categories.Count);
    }
}
