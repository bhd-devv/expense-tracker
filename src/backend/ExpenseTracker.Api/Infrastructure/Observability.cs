using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace ExpenseTracker.Api.Infrastructure;

// Exception messages/Data can contain SQL parameters, URLs, tokens or user input.
// Keep the full chain of types and stack locations without those values.
public static class SafeError
{
    public static object Describe(Exception error) => new
    {
        type = error.GetType().FullName,
        code = error switch
        {
            ApiError api => api.Code,
            Npgsql.PostgresException postgres => postgres.SqlState,
            _ => null
        },
        hresult = error.HResult,
        stack = error.StackTrace,
        inner = error is AggregateException aggregate
            ? aggregate.InnerExceptions.Select(Describe).ToArray()
            : error.InnerException is { } inner ? new[] { Describe(inner) } : []
    };
}

public sealed class SafeJsonConsole() : ConsoleFormatter("safe-json")
{
    private static object? Value(object? value) => value switch
    {
        null or string or bool or byte or short or int or long or uint or ulong or float or double or decimal
            or Guid or DateTime or DateTimeOffset => value,
        _ => value.GetType().Name
    };

    public override void Write<TState>(in LogEntry<TState> entry, IExternalScopeProvider? scopes, TextWriter writer)
    {
        var properties = new Dictionary<string, object?>();
        scopes?.ForEachScope((scope, target) =>
        {
            if (scope is IEnumerable<KeyValuePair<string, object?>> values)
                foreach (var item in values)
                    if (item.Key is "requestId" or "traceId" or "method" or "route" or "resourceId") target[item.Key] = Value(item.Value);
        }, properties);
        if (entry.Exception is null && entry.State is IEnumerable<KeyValuePair<string, object?>> state)
            foreach (var item in state.Where(x => x.Key != "{OriginalFormat}")) properties[item.Key] = Value(item.Value);
        writer.WriteLine(JsonSerializer.Serialize(new
        {
            timestamp = DateTimeOffset.UtcNow,
            level = entry.LogLevel.ToString(),
            category = entry.Category,
            eventId = entry.EventId.Id,
            message = entry.Exception is null ? entry.Formatter(entry.State, null) : "Operation failed (exception details sanitized)",
            properties,
            error = entry.Exception is { } error ? SafeError.Describe(error) : null
        }));
    }
}

public sealed class RequestLogging(RequestDelegate next, ILogger<RequestLogging> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var started = Stopwatch.GetTimestamp();
        // Use the matched template rather than a raw URL/query supplied by a caller.
        var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unmatched";
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["requestId"] = context.TraceIdentifier,
            ["traceId"] = Activity.Current?.TraceId.ToString(),
            ["method"] = context.Request.Method,
            ["route"] = route,
            ["resourceId"] = Guid.TryParse(context.Request.RouteValues["id"]?.ToString(), out var id) ? id : null
        });
        // ExceptionHandler clears response headers; set correlation when the response starts.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Request-Id"] = context.TraceIdentifier;
            return Task.CompletedTask;
        });
        logger.LogInformation("Request started");
        var failed = false;
        try { await next(context); }
        catch
        {
            failed = true;
            throw;
        }
        finally
        {
            var status = context.RequestAborted.IsCancellationRequested ? 499 : failed ? 500 : context.Response.StatusCode;
            var level = status >= 500 ? LogLevel.Error : status >= 400 ? LogLevel.Warning : LogLevel.Information;
            logger.Log(level, "Request completed status={Status} elapsedMs={ElapsedMs} userId={UserId}",
                status, Stopwatch.GetElapsedTime(started).TotalMilliseconds, context.User.FindFirst("user_id")?.Value);
        }
    }
}
