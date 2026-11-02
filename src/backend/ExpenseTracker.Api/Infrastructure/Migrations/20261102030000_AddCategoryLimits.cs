using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExpenseTracker.Api.Infrastructure.Migrations;

public partial class AddCategoryLimits : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(name: "MonthlyLimit", table: "Categories", type: "numeric(20,4)", precision: 20, scale: 4, nullable: true);
        migrationBuilder.AddColumn<string>(name: "LimitCurrency", table: "Categories", type: "text", nullable: true);
        migrationBuilder.AddCheckConstraint(name: "valid_category_limit", table: "Categories",
            sql: "(\"MonthlyLimit\" IS NULL AND \"LimitCurrency\" IS NULL) OR (\"MonthlyLimit\" IS NOT NULL AND \"MonthlyLimit\" > 0 AND \"LimitCurrency\" IS NOT NULL)");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(name: "valid_category_limit", table: "Categories");
        migrationBuilder.DropColumn(name: "MonthlyLimit", table: "Categories");
        migrationBuilder.DropColumn(name: "LimitCurrency", table: "Categories");
    }
}
