using ExpenseTracker.Api.Features.Users;
using ExpenseTracker.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Features.Expenses;

public sealed record SeedResultDto(int CreatedCount, int TotalCount, DateOnly From, DateOnly To);

public sealed class DemoSeedService(Database db, ExpenseService expenses, TimeProvider clock)
{
    private static readonly DateOnly From = new(2026, 10, 1);
    private static readonly DateOnly To = new(2026, 10, 31);

    private static readonly (string Uk, string En, string Currency)[] Accounts =
    [
        ("Демо · Картка", "Demo · Card", "UAH"),
        ("Демо · Готівка", "Demo · Cash", "UAH"),
        ("Демо · Онлайн USD", "Demo · Online USD", "USD"),
        ("Демо · Подорож EUR", "Demo · Travel EUR", "EUR")
    ];

    private sealed record Sample(int Day, decimal Amount, int Account, string Category, string Uk, string En);

    private static List<Sample> Samples()
    {
        Sample[] daily =
        [
            new(0, 32m, 0, "transport", "Метро й автобус", "Metro and bus"),
            new(0, 475.65m, 0, "food", "Продукти на вечерю", "Groceries for dinner"),
            new(0, 1630.40m, 0, "home", "Комунальні послуги", "Utilities"),
            new(0, 1299m, 0, "shopping", "Теплий светр", "Warm sweater"),
            new(0, 286.70m, 0, "health", "Аптека", "Pharmacy"),
            new(0, 240m, 0, "leisure", "Кіно з друзями", "Cinema with friends"),
            new(0, 150m, 0, "other", "Поповнення телефону", "Mobile top-up")
        ];
        var samples = new List<Sample>();
        for (var day = 1; day <= 31; day++)
        {
            samples.Add(new(day, 55m + day % 6 * 5m + day % 3 * 0.50m, 1, "food", "Кава та сніданок", "Coffee and breakfast"));
            var sample = daily[(day - 1) % daily.Length];
            samples.Add(sample with { Day = day, Amount = sample.Amount + day % 4 * 15.25m });
        }
        samples.AddRange(
        [
            new(1, 12000m, 0, "home", "Оренда квартири", "Apartment rent"),
            new(31, 680.90m, 0, "shopping", "Покупки наприкінці місяця", "End-of-month shopping"),
            new(5, 5.99m, 2, "leisure", "Музична підписка", "Music subscription"),
            new(15, 19.99m, 2, "leisure", "Комп'ютерна гра", "Computer game"),
            new(25, 24.50m, 2, "other", "Онлайн-курс", "Online course"),
            new(8, 12.80m, 3, "transport", "Квиток на потяг", "Train ticket"),
            new(18, 79m, 3, "home", "Готель на одну ніч", "One night at a hotel"),
            new(28, 16.90m, 3, "shopping", "Книга в подорожі", "Book while travelling")
        ]);
        return samples;
    }

    public async Task<SeedResultDto> Seed(User user, CancellationToken ct)
    {
        await using var transaction = await db.LockUser(user, ct);
        var receipt = await db.DemoSeeds.SingleOrDefaultAsync(x => x.UserId == user.Id && x.Period == From, ct);
        if (receipt is not null)
        {
            await transaction.CommitAsync(ct);
            return new(0, receipt.ExpenseCount, From, To);
        }

        var categories = await db.Categories.Where(x => x.UserId == user.Id).ToListAsync(ct);
        var missing = UserService.Categories.Where(item => !categories.Any(x => x.Code == item.Code && !x.Archived)).ToArray();
        if (categories.Count + missing.Length > 50 || await db.Accounts.CountAsync(x => x.UserId == user.Id, ct) + Accounts.Length > 50)
            throw new ApiError("catalog_limit");

        var english = user.Language == "en";
        var accounts = Accounts.Select(item => new Account
        {
            UserId = user.Id,
            Name = english ? item.En : item.Uk,
            Currency = item.Currency
        }).ToArray();
        db.Accounts.AddRange(accounts);
        foreach (var item in missing)
        {
            var category = new Category { UserId = user.Id, Code = item.Code, Name = item.Name };
            categories.Add(category);
            db.Categories.Add(category);
        }
        await db.SaveChangesAsync(ct);

        var samples = Samples();
        foreach (var sample in samples)
        {
            var expense = new Expense { Id = Guid.NewGuid(), UserId = user.Id, Source = "bot", CreatedAt = clock.GetUtcNow() };
            var account = accounts[sample.Account];
            var category = categories.First(x => x.Code == sample.Category && !x.Archived);
            var description = (english ? "Demo · " : "Демо · ") + (english ? sample.En : sample.Uk);
            await expenses.Apply(user, expense, new ExpenseInput(Rules.Money(sample.Amount), account.Id, category.Id,
                description, new DateOnly(2026, 10, sample.Day), 1), ct);
            db.Expenses.Add(expense);
        }
        db.DemoSeeds.Add(new DemoSeed { UserId = user.Id, Period = From, ExpenseCount = samples.Count, CreatedAt = clock.GetUtcNow() });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(samples.Count, samples.Count, From, To);
    }
}
