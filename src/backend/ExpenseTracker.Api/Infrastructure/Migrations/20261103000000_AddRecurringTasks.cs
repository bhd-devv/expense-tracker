using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExpenseTracker.Api.Infrastructure.Migrations;

public partial class AddRecurringTasks : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "RecurringTasks",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                IntervalDays = table.Column<int>(type: "integer", nullable: false),
                StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Deleted = table.Column<bool>(type: "boolean", nullable: false),
                Version = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RecurringTasks", x => x.Id);
                table.UniqueConstraint("AK_RecurringTasks_UserId_Id", x => new { x.UserId, x.Id });
                table.ForeignKey("FK_RecurringTasks_Users_UserId", x => x.UserId, "Users", "Id", onDelete: ReferentialAction.Cascade);
                table.CheckConstraint("valid_task_interval", "\"IntervalDays\" IN (1, 2, 3, 7)");
            });
        migrationBuilder.CreateTable(
            name: "TaskCompletions",
            columns: table => new
            {
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                Date = table.Column<DateOnly>(type: "date", nullable: false),
                TaskId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TaskCompletions", x => new { x.UserId, x.Date, x.TaskId });
                table.ForeignKey("FK_TaskCompletions_RecurringTasks_UserId_TaskId", x => new { x.UserId, x.TaskId },
                    "RecurringTasks", new[] { "UserId", "Id" }, onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.CreateIndex("IX_RecurringTasks_UserId_Deleted_CreatedAt_Id", "RecurringTasks", new[] { "UserId", "Deleted", "CreatedAt", "Id" });
        migrationBuilder.CreateIndex("IX_TaskCompletions_UserId_TaskId", "TaskCompletions", new[] { "UserId", "TaskId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Retain task history when rolling back application images.
        throw new NotSupportedException("Task history must be retained. Roll back application images without reverting this migration.");
    }
}
