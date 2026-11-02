using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExpenseTracker.Api.Infrastructure.Migrations;

public partial class AddSavingsAccounts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SavingsAccounts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                Currency = table.Column<string>(type: "text", nullable: false),
                Added = table.Column<decimal>(type: "numeric(28,4)", precision: 28, scale: 4, nullable: false),
                Spent = table.Column<decimal>(type: "numeric(28,4)", precision: 28, scale: 4, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SavingsAccounts", x => x.Id);
                table.UniqueConstraint("AK_SavingsAccounts_UserId_Id", x => new { x.UserId, x.Id });
                table.ForeignKey("FK_SavingsAccounts_Users_UserId", x => x.UserId, "Users", "Id", onDelete: ReferentialAction.Cascade);
                table.CheckConstraint("valid_savings_balance", "\"Added\" >= 0 AND \"Spent\" >= 0 AND \"Spent\" <= \"Added\"");
            });
        migrationBuilder.CreateTable(
            name: "SavingsEntries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                Kind = table.Column<string>(type: "text", nullable: false),
                Amount = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                Date = table.Column<DateOnly>(type: "date", nullable: false),
                Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SavingsEntries", x => x.Id);
                table.ForeignKey("FK_SavingsEntries_SavingsAccounts_UserId_AccountId", x => new { x.UserId, x.AccountId },
                    "SavingsAccounts", new[] { "UserId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.CheckConstraint("positive_savings_amount", "\"Amount\" > 0");
                table.CheckConstraint("valid_savings_kind", "\"Kind\" IN ('deposit', 'withdrawal')");
            });
        migrationBuilder.CreateIndex("IX_SavingsEntries_UserId_AccountId_Date_CreatedAt_Id", "SavingsEntries",
            new[] { "UserId", "AccountId", "Date", "CreatedAt", "Id" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Rolling back application code is safe; dropping a financial journal is not.
        throw new NotSupportedException("Savings journals must be retained. Roll back application images without reverting this migration.");
    }
}
