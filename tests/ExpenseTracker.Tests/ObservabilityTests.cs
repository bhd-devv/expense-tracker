using System.Text.Json;
using ExpenseTracker.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ExpenseTracker.Tests;

public sealed class ObservabilityTests
{
    [Theory]
    [InlineData("GET", "/api/health/live", 200)]
    [InlineData("POST", "/api/auth/telegram", 403)]
    public async Task RequestIdIsReturnedForSuccessAndHandledFailure(string method, string path, int status)
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        using var response = await client.SendAsync(request);
        Assert.Equal(status, (int)response.StatusCode);
        var requestId = Assert.Single(response.Headers.GetValues("X-Request-Id"));
        Assert.False(string.IsNullOrWhiteSpace(requestId));
        if (status >= 400)
        {
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(requestId, problem.RootElement.GetProperty("requestId").GetString());
        }
    }

    [Fact]
    public void FormatterKeepsCorrelationAndExceptionLocationsWithoutMessagesOrSql()
    {
        Exception error;
        try { throw new InvalidOperationException("private-note-and-token", new Exception("private-inner")); }
        catch (Exception caught) { error = caught; }
        var scopes = new LoggerExternalScopeProvider();
        using var scope = scopes.Push(new Dictionary<string, object?> { ["requestId"] = "synthetic-request" });
        var state = new Dictionary<string, object?> { ["sql"] = "private-sql" };
        var entry = new LogEntry<Dictionary<string, object?>>(LogLevel.Error, "test", new EventId(1), state,
            error, (_, exception) => $"Leaked: {exception}");
        using var output = new StringWriter();
        new SafeJsonConsole().Write(in entry, scopes, output);
        var text = output.ToString();
        Assert.DoesNotContain("private-", text);
        using var json = JsonDocument.Parse(text);
        Assert.Equal("synthetic-request", json.RootElement.GetProperty("properties").GetProperty("requestId").GetString());
        Assert.Equal("System.InvalidOperationException", json.RootElement.GetProperty("error").GetProperty("type").GetString());
        Assert.Contains(nameof(FormatterKeepsCorrelationAndExceptionLocationsWithoutMessagesOrSql),
            json.RootElement.GetProperty("error").GetProperty("stack").GetString());
        Assert.Single(json.RootElement.GetProperty("error").GetProperty("inner").EnumerateArray());
    }
}
