using System.Text.Json.Serialization;
using ExpenseTracker.Api.Features.Users;
using ExpenseTracker.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Features.Tasks;

public sealed record CreateTaskRequest(Guid RequestId, string Name, int IntervalDays, DateOnly StartDate);
public sealed record RenameTaskRequest(string Name, int Version);
public sealed record TaskCompletionRequest(DateOnly Date, [property: JsonRequired] bool Completed, int Version);
public sealed record RecurringTaskDto(Guid Id, string Name, int IntervalDays, DateOnly StartDate,
    int Version, bool IsDue, bool Completed, bool CanComplete);
public sealed record TaskListDto(DateOnly Today, DateOnly Date, IReadOnlyList<RecurringTaskDto> Items);

public sealed class TaskService(Database db, TimeProvider clock)
{
    public static bool IsDue(DateOnly start, int intervalDays, DateOnly date) =>
        intervalDays is 1 or 2 or 3 or 7 && date >= start && (date.DayNumber - start.DayNumber) % intervalDays == 0;

    public async Task<TaskListDto> List(User user, DateOnly? date, CancellationToken ct)
    {
        var today = Rules.Today(user, clock);
        var day = date ?? today;
        // Read definitions and checkmarks from the same committed state.
        await using var transaction = await db.LockUser(user, ct);
        var tasks = await db.RecurringTasks.AsNoTracking().Where(x => x.UserId == user.Id && !x.Deleted)
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToArrayAsync(ct);
        var taskIds = tasks.Select(x => x.Id).ToArray();
        var completed = await db.TaskCompletions.AsNoTracking().Where(x => x.UserId == user.Id && x.Date == day && taskIds.Contains(x.TaskId))
            .Select(x => x.TaskId).ToArrayAsync(ct);
        await transaction.CommitAsync(ct);
        var ids = completed.ToHashSet();
        return new(today, day, tasks.Select(x => View(x, day, today, ids.Contains(x.Id))).ToArray());
    }

    public async Task<RecurringTaskDto> Create(User user, CreateTaskRequest input, CancellationToken ct)
    {
        if (input.RequestId == Guid.Empty) throw new ApiError("invalid_request_id");
        var name = Rules.Text(input.Name, 120);
        if (input.IntervalDays is not (1 or 2 or 3 or 7)) throw new ApiError("invalid_task_interval");
        if (input.StartDate == DateOnly.MinValue) throw new ApiError("invalid_date");
        await using var transaction = await db.LockUser(user, ct);
        var previous = await db.RecurringTasks.SingleOrDefaultAsync(x => x.Id == input.RequestId, ct);
        if (previous is not null)
        {
            if (previous.UserId != user.Id || previous.Deleted || previous.Name != name
                || previous.IntervalDays != input.IntervalDays || previous.StartDate != input.StartDate)
                throw new ApiError("conflict", 409);
            var today = Rules.Today(user, clock);
            var completed = await db.TaskCompletions.AnyAsync(x => x.UserId == user.Id && x.TaskId == previous.Id && x.Date == today, ct);
            return View(previous, today, today, completed);
        }
        if (await db.RecurringTasks.CountAsync(x => x.UserId == user.Id && !x.Deleted, ct) >= 100)
            throw new ApiError("task_limit");
        var task = new RecurringTask
        {
            Id = input.RequestId, UserId = user.Id, Name = name, IntervalDays = input.IntervalDays,
            StartDate = input.StartDate, CreatedAt = clock.GetUtcNow()
        };
        db.Add(task);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        var day = Rules.Today(user, clock);
        return View(task, day, day, false);
    }

    private async Task<RecurringTask> Find(User user, Guid id, CancellationToken ct) =>
        await db.RecurringTasks.SingleOrDefaultAsync(x => x.UserId == user.Id && x.Id == id && !x.Deleted, ct)
            ?? throw new ApiError("not_found", 404);

    public async Task Rename(User user, Guid id, RenameTaskRequest input, CancellationToken ct)
    {
        var name = Rules.Text(input.Name, 120);
        await using var transaction = await db.LockUser(user, ct);
        var task = await Find(user, id, ct);
        if (task.Version != input.Version) throw new ApiError("conflict", 409);
        task.Name = name;
        task.Version++;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task Complete(User user, Guid id, TaskCompletionRequest input, CancellationToken ct)
    {
        await using var transaction = await db.LockUser(user, ct);
        var task = await Find(user, id, ct);
        if (task.Version != input.Version) throw new ApiError("conflict", 409);
        if (input.Date > Rules.Today(user, clock) || !IsDue(task.StartDate, task.IntervalDays, input.Date))
            throw new ApiError("task_not_due");
        var mark = await db.TaskCompletions.SingleOrDefaultAsync(x => x.UserId == user.Id && x.TaskId == id && x.Date == input.Date, ct);
        if (input.Completed && mark is null)
            db.Add(new TaskCompletion { UserId = user.Id, TaskId = id, Date = input.Date });
        else if (!input.Completed && mark is not null) db.Remove(mark);
        task.Version++;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task Delete(User user, Guid id, int version, CancellationToken ct)
    {
        await using var transaction = await db.LockUser(user, ct);
        var task = await Find(user, id, ct);
        if (task.Version != version) throw new ApiError("conflict", 409);
        // Keep the ID reserved so an old create request cannot resurrect a deleted task.
        task.Deleted = true;
        task.Version++;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private static RecurringTaskDto View(RecurringTask task, DateOnly date, DateOnly today, bool completed)
    {
        var due = IsDue(task.StartDate, task.IntervalDays, date);
        return new(task.Id, task.Name, task.IntervalDays, task.StartDate, task.Version, due, completed, due && date <= today);
    }
}

public static class TaskEndpoints
{
    // Intentionally mapped only to the browser API, never to the bot actor API.
    public static void MapTasks(this RouteGroupBuilder group)
    {
        group.MapGet("/tasks", async (DateOnly? date, HttpContext context, UserService users, TaskService tasks, CancellationToken ct) =>
            await tasks.List(await users.Current(context, ct), date, ct));
        group.MapPost("/tasks", async (CreateTaskRequest input, HttpContext context, UserService users, TaskService tasks, CancellationToken ct) =>
            await tasks.Create(await users.Current(context, ct), input, ct));
        group.MapPut("/tasks/{id:guid}", async (Guid id, RenameTaskRequest input, HttpContext context, UserService users, TaskService tasks, CancellationToken ct) =>
        {
            await tasks.Rename(await users.Current(context, ct), id, input, ct);
            return Results.NoContent();
        }).Produces(StatusCodes.Status204NoContent);
        group.MapPut("/tasks/{id:guid}/completion", async (Guid id, TaskCompletionRequest input, HttpContext context, UserService users, TaskService tasks, CancellationToken ct) =>
        {
            await tasks.Complete(await users.Current(context, ct), id, input, ct);
            return Results.NoContent();
        }).Produces(StatusCodes.Status204NoContent);
        group.MapDelete("/tasks/{id:guid}", async (Guid id, int version, HttpContext context, UserService users, TaskService tasks, CancellationToken ct) =>
        {
            await tasks.Delete(await users.Current(context, ct), id, version, ct);
            return Results.NoContent();
        }).Produces(StatusCodes.Status204NoContent);
    }
}
