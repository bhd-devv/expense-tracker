using System.Globalization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Infrastructure;

public sealed class ApiError(string code, int status = 422) : Exception(code)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}

public sealed class Errors(IProblemDetailsService problems, ILogger<Errors> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (code, status) = exception switch
        {
            ApiError error => (error.Code, error.Status),
            DbUpdateConcurrencyException => ("conflict", 409),
            BadHttpRequestException => ("invalid_request", 400),
            _ => ("server_error", 500)
        };
        if (status == 500)
            logger.LogError("Unhandled error {ExceptionType}, trace {TraceId}", exception.GetType().Name, context.TraceIdentifier);
        context.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new() { Title = code, Status = status, Extensions = { ["code"] = code } }
        });
    }
}

public static class Rules
{
    public static readonly string[] Currencies = ["UAH", "USD", "EUR", "GBP", "PLN", "JPY"];

    public static string Text(string? value, int max = 500)
    {
        var text = value?.Trim() ?? "";
        if (text.Length is 0 || text.Length > max) throw new ApiError("invalid_text");
        return text;
    }

    public static decimal Amount(string value, string currency)
    {
        if (!Currencies.Contains(currency)) throw new ApiError("invalid_currency");
        var digits = currency == "JPY" ? 0 : 2;
        if (!decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
            || amount <= 0 || amount > 9999999999999999m || decimal.Round(amount, digits) != amount)
            throw new ApiError("invalid_amount");
        return amount;
    }

    public static string Money(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);
    public static DateOnly Today(User user, TimeProvider clock) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(user.TimeZone)).DateTime);
}
