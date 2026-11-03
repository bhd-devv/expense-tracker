using System.Net;
using System.Net.Http.Json;
using ExpenseTracker.Api.Features.Tasks;
using ExpenseTracker.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ExpenseTracker.Tests;

public sealed class TaskScheduleTests
{
    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(2, 1, false)]
    [InlineData(2, 2, true)]
    [InlineData(3, 2, false)]
    [InlineData(3, 3, true)]
    [InlineData(7, 6, false)]
    [InlineData(7, 7, true)]
    [InlineData(7, 14, true)]
    [InlineData(2, -2, false)]
    [InlineData(0, 0, false)]
    [InlineData(4, 4, false)]
    public void ScheduleIsAnchoredAcrossMonthsAndLeapDay(int interval, int offset, bool expected)
    {
        var start = new DateOnly(2028, 2, 28);
        Assert.Equal(expected, TaskService.IsDue(start, interval, start.AddDays(offset)));
    }
}

public sealed partial class PostgresTests
{
    [PostgresFact]
    public async Task TasksUseLocalDaysProtectOwnershipAndRejectStaleOrUnscheduledMarks()
    {
        var clock = new Clock { Now = new(2026, 11, 3, 22, 30, 0, TimeSpan.Zero) };
        await using var factory = await Factory(services => services.AddSingleton<TimeProvider>(clock));
        var telegramId = Random.Shared.NextInt64(10_000_000_000, 90_000_000_000);
        using var browser = await Browser(factory, telegramId, clock.Now);
        using var stranger = await Browser(factory, telegramId + 1, clock.Now);
        using var bot = factory.CreateClient();
        bot.DefaultRequestHeaders.Authorization = new("Bearer", SecurityTests.ServiceKey);
        bot.DefaultRequestHeaders.Add("X-Telegram-User-Id", telegramId.ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await bot.GetAsync("/internal/bot/actor/tasks")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await bot.GetAsync("/api/tasks")).StatusCode);

        var reportBefore = await browser.GetStringAsync("/api/reports");
        var create = new CreateTaskRequest(Guid.NewGuid(), "Read", 2, new(2026, 11, 2));
        var response = await browser.PostAsJsonAsync("/api/tasks", create);
        response.EnsureSuccessStatusCode();
        var task = await response.Content.ReadFromJsonAsync<RecurringTaskDto>() ?? throw new InvalidOperationException();
        var day = await browser.GetFromJsonAsync<TaskListDto>("/api/tasks") ?? throw new InvalidOperationException();
        Assert.Equal(new DateOnly(2026, 11, 4), day.Today); // Kyiv has already crossed midnight.
        Assert.True(task.IsDue);
        Assert.True(task.CanComplete);
        Assert.False(task.Completed);
        Assert.Equal(task.Id, (await (await browser.PostAsJsonAsync("/api/tasks", create)).Content.ReadFromJsonAsync<RecurringTaskDto>())?.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await browser.PostAsJsonAsync("/api/tasks", create with { IntervalDays = 3 })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await browser.PostAsJsonAsync("/api/tasks", create with { RequestId = Guid.NewGuid(), IntervalDays = 4 })).StatusCode);
        Assert.Empty((await stranger.GetFromJsonAsync<TaskListDto>("/api/tasks"))?.Items ?? throw new InvalidOperationException());
        var path = $"/api/tasks/{task.Id}";
        var mark = new TaskCompletionRequest(day.Today, true, task.Version);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PutAsJsonAsync(path + "/completion", new { mark.Date, mark.Version })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PutAsJsonAsync(path + "/completion", mark)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PutAsJsonAsync(path, new RenameTaskRequest("Foreign", task.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync(path + $"?version={task.Version}")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await browser.PutAsJsonAsync(path + "/completion", mark with { Date = new(2026, 11, 3) })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await browser.PutAsJsonAsync(path + "/completion", mark with { Date = new(2026, 11, 6) })).StatusCode);
        var concurrent = await Task.WhenAll(browser.PutAsJsonAsync(path + "/completion", mark), browser.PutAsJsonAsync(path + "/completion", mark));
        Assert.Single(concurrent.Where(x => x.StatusCode == HttpStatusCode.NoContent));
        Assert.Single(concurrent.Where(x => x.StatusCode == HttpStatusCode.Conflict));
        day = await browser.GetFromJsonAsync<TaskListDto>("/api/tasks") ?? throw new InvalidOperationException();
        task = Assert.Single(day.Items);
        Assert.True(task.Completed);

        // The previous occurrence remains independently incomplete; a skipped day never shifts cadence.
        var past = await browser.GetFromJsonAsync<TaskListDto>("/api/tasks?date=2026-11-02") ?? throw new InvalidOperationException();
        Assert.False(Assert.Single(past.Items).Completed);
        Assert.True(Assert.Single(past.Items).IsDue);
        (await browser.PutAsJsonAsync(path + "/completion", mark with { Date = create.StartDate, Version = task.Version })).EnsureSuccessStatusCode();
        day = await browser.GetFromJsonAsync<TaskListDto>("/api/tasks") ?? throw new InvalidOperationException();
        task = Assert.Single(day.Items);
        (await browser.PutAsJsonAsync(path + "/completion", mark with { Completed = false, Version = task.Version })).EnsureSuccessStatusCode();
        day = await browser.GetFromJsonAsync<TaskListDto>("/api/tasks") ?? throw new InvalidOperationException();
        task = Assert.Single(day.Items);
        Assert.False(task.Completed);
        past = await browser.GetFromJsonAsync<TaskListDto>("/api/tasks?date=2026-11-02") ?? throw new InvalidOperationException();
        Assert.True(Assert.Single(past.Items).Completed);

        (await browser.PutAsJsonAsync(path, new RenameTaskRequest("Read a book", task.Version))).EnsureSuccessStatusCode();
        task = Assert.Single((await browser.GetFromJsonAsync<TaskListDto>("/api/tasks"))?.Items ?? throw new InvalidOperationException());
        Assert.Equal("Read a book", task.Name);
        (await browser.DeleteAsync(path + $"?version={task.Version}")).EnsureSuccessStatusCode();
        Assert.Empty((await browser.GetFromJsonAsync<TaskListDto>("/api/tasks"))?.Items ?? throw new InvalidOperationException());
        Assert.Equal(HttpStatusCode.Conflict, (await browser.PostAsJsonAsync("/api/tasks", create)).StatusCode);
        Assert.Equal(reportBefore, await browser.GetStringAsync("/api/reports"));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        Assert.Equal(1, await db.TaskCompletions.CountAsync(x => x.TaskId == task.Id));
        Assert.True((await db.RecurringTasks.SingleAsync(x => x.Id == task.Id)).Deleted);
    }
}
