using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExpenseTracker.Api.Infrastructure.Migrations;

public partial class RemoveFinancialAccounts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "Currency", table: "Drafts", type: "text", nullable: true);
        // Copy the currency before removing the source. Expense currencies already live on Expenses.
        migrationBuilder.Sql("""
            UPDATE "Drafts" AS d
            SET "Currency" = COALESCE(
                (SELECT a."Currency" FROM "Accounts" AS a WHERE a."Id" = d."AccountId" AND a."UserId" = d."UserId"),
                (SELECT u."Currency" FROM "Users" AS u WHERE u."Id" = d."UserId")),
                "InputHash" = split_part(d."InputHash", ':', 1),
                "Version" = d."Version" + 1;
            """);
        migrationBuilder.AlterColumn<string>(name: "Currency", table: "Drafts", type: "text", nullable: false,
            oldClrType: typeof(string), oldType: "text", oldNullable: true);
        migrationBuilder.DropForeignKey(name: "FK_Expenses_Accounts_UserId_AccountId", table: "Expenses");
        migrationBuilder.DropIndex(name: "IX_Expenses_UserId_AccountId", table: "Expenses");
        migrationBuilder.DropColumn(name: "AccountId", table: "Expenses");
        migrationBuilder.DropColumn(name: "AccountId", table: "Drafts");
        migrationBuilder.DropColumn(name: "DefaultAccountId", table: "Users");
        migrationBuilder.DropTable(name: "Accounts");
        // Previous bot filters/steps encode the removed catalogue. Keep draft records, reset navigation.
        migrationBuilder.Sql("""
            UPDATE "Conversations" SET "State" = 'idle', "EntityId" = NULL,
                "Field" = NULL, "Version" = "Version" + 1;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Removed financial accounts cannot be reconstructed. Restore a pre-release backup with matching application images.");
}
