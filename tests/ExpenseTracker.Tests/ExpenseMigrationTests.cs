using ExpenseTracker.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace ExpenseTracker.Tests;

public sealed class ExpenseMigrationTests
{
    [PostgresFact]
    public async Task RemovingFinancialAccountsPreservesExpensesDraftCurrenciesAndSeedReceipts()
    {
        var connection = Environment.GetEnvironmentVariable("EXPENSE_TEST_DATABASE") ?? throw new InvalidOperationException();
        var settings = new NpgsqlConnectionStringBuilder(connection);
        if (!(settings.Database ?? "").EndsWith("_test", StringComparison.Ordinal))
            throw new InvalidOperationException("Migration tests require a database ending in _test");
        var schema = "migration_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(connection);
        await admin.OpenAsync();
        // The identifier consists solely of a fixed prefix and a generated hexadecimal GUID.
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin))
            await create.ExecuteNonQueryAsync();
        try
        {
            settings.SearchPath = schema;
            await using var db = new Database(new DbContextOptionsBuilder<Database>().UseNpgsql(settings.ConnectionString).Options);
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261101195658_AddDemoSeeds");
            var userId = Guid.NewGuid();
            var accountId = Guid.NewGuid();
            var categoryId = Guid.NewGuid();
            var expenseId = Guid.NewGuid();
            var linkedDraftId = Guid.NewGuid();
            var unlinkedDraftId = Guid.NewGuid();
            var day = new DateOnly(2026, 10, 12);
            var now = DateTimeOffset.UtcNow;
            var expires = now.AddMinutes(30);
            var fingerprint = new string('a', 64);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Users" ("Id", "TelegramId", "Language", "Theme", "TimeZone", "Currency", "DefaultAccountId", "AiConsent", "Version")
                VALUES ({userId}, {42L}, 'uk', 'auto', 'Europe/Kyiv', 'GBP', {accountId}, false, 1);
                INSERT INTO "Accounts" ("Id", "UserId", "Name", "Currency", "Archived", "Version")
                VALUES ({accountId}, {userId}, 'Legacy', 'EUR', false, 1);
                INSERT INTO "Categories" ("Id", "UserId", "Name", "Code", "Archived", "Version")
                VALUES ({categoryId}, {userId}, 'Food', 'food', false, 1);
                INSERT INTO "Expenses" ("Id", "UserId", "AccountId", "CategoryId", "Amount", "Currency", "Description", "Date", "Source", "Version", "CreatedAt")
                VALUES ({expenseId}, {userId}, {accountId}, {categoryId}, {12.34m}, 'EUR', 'Synthetic lunch', {day}, 'bot', 3, {now});
                INSERT INTO "Drafts" ("Id", "UserId", "RequestId", "InputHash", "Source", "InputKind", "Transcript", "Amount", "Description", "AccountId", "CategoryId", "Date", "Status", "Version", "ExpiresAt")
                VALUES ({linkedDraftId}, {userId}, {Guid.NewGuid()}, {fingerprint + ":" + accountId}, 'bot', 'text', 'Synthetic', {5m}, 'Synthetic', {accountId}, {categoryId}, {day}, 'ready', 2, {expires}),
                       ({unlinkedDraftId}, {userId}, {Guid.NewGuid()}, {fingerprint + ":"}, 'mini_app', 'text', 'Synthetic', NULL, '', NULL, NULL, {day}, 'failed', 2, {expires});
                INSERT INTO "DemoSeeds" ("UserId", "Period", "ExpenseCount", "CreatedAt")
                VALUES ({userId}, {new DateOnly(2026, 10, 1)}, 70, {now});
                INSERT INTO "Conversations" ("UserId", "State", "EntityId", "Field", "Version", "ExpiresAt")
                VALUES ({userId}, 'record', {linkedDraftId.ToString()}, 'd.accountId.2', 4, {expires});
                """);
            await migrator.MigrateAsync();
            var expense = await db.Expenses.SingleAsync();
            Assert.Equal(expenseId, expense.Id);
            Assert.Equal(12.34m, expense.Amount);
            Assert.Equal("EUR", expense.Currency);
            Assert.Equal(day, expense.Date);
            Assert.Equal(3, expense.Version);
            var drafts = await db.Drafts.ToDictionaryAsync(x => x.Id);
            Assert.Equal("EUR", drafts[linkedDraftId].Currency);
            Assert.Equal("GBP", drafts[unlinkedDraftId].Currency);
            Assert.All(drafts.Values, draft => Assert.Equal(fingerprint, draft.InputHash));
            Assert.Equal("GBP", (await db.Users.SingleAsync()).Currency);
            Assert.Equal(70, (await db.DemoSeeds.SingleAsync()).ExpenseCount);
            var conversation = await db.Conversations.SingleAsync();
            Assert.Equal("idle", conversation.State);
            Assert.Null(conversation.EntityId);
            Assert.Null(conversation.Field);
            Assert.Equal(5, conversation.Version);
            var obsoleteColumns = await db.Database.SqlQuery<int>($"""
                SELECT count(*)::integer AS "Value" FROM information_schema.columns
                WHERE table_schema = {schema}
                  AND (table_name = 'Accounts' OR column_name IN ('AccountId', 'DefaultAccountId'))
                """).SingleAsync();
            Assert.Equal(0, obsoleteColumns);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
