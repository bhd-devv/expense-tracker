using System.Net;
using System.Text;
using ExpenseTracker.Api.Features.Auth;
using ExpenseTracker.Api.Infrastructure;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ExpenseTracker.Tests;

public sealed class TelegramBotIdentityTests
{
    private const string Token = "123456:synthetic-test-token";
    private const string ValidResponse = "{\"ok\":true,\"result\":{\"id\":123456,\"is_bot\":true,\"username\":\"lime_test_bot\"}}";

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => respond(request, ct);
    }

    private sealed class Clients(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal("TelegramIdentity", name);
            return new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("https://api.telegram.org/") };
        }
    }

    private static TelegramBotIdentity Identity(HttpMessageHandler handler) => new(new Clients(handler),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Telegram:BotToken"] = Token }).Build());

    private static HttpResponseMessage Response(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    [Fact]
    public async Task ConcurrentRequestsResolveTokenOnceAndReuseUsername()
    {
        var calls = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new Handler(async (request, ct) =>
        {
            Interlocked.Increment(ref calls);
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal($"https://api.telegram.org/bot{Token}/getMe", request.RequestUri?.AbsoluteUri);
            started.SetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            return Response(ValidResponse);
        });
        using var identity = Identity(handler);
        var first = identity.GetUsername(default);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = identity.GetUsername(default);
        release.SetResult();
        Assert.Equal(["lime_test_bot", "lime_test_bot"], await Task.WhenAll(first, second));
        Assert.Equal("lime_test_bot", await identity.GetUsername(default));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("{}", 401)]
    [InlineData("{\"ok\":false}", 200)]
    [InlineData("not json", 200)]
    [InlineData("null", 200)]
    [InlineData("{\"ok\":true,\"result\":{\"is_bot\":false,\"username\":\"someone\"}}", 200)]
    [InlineData("{\"ok\":true,\"result\":{\"is_bot\":true}}", 200)]
    [InlineData("{\"ok\":true,\"result\":{\"is_bot\":true,\"username\":\"bad/name\"}}", 200)]
    public async Task FailedLookupReturnsSafeErrorAndNextRequestCanRecover(string json, int status)
    {
        var calls = 0;
        using var handler = new Handler((_, _) => Task.FromResult(++calls == 1
            ? Response(json, (HttpStatusCode)status) : Response(ValidResponse)));
        using var identity = Identity(handler);
        var error = await Assert.ThrowsAsync<ApiError>(() => identity.GetUsername(default));
        Assert.Equal(503, error.Status);
        Assert.Equal("telegram_unavailable", error.Code);
        Assert.DoesNotContain(Token, error.ToString());
        Assert.Equal("lime_test_bot", await identity.GetUsername(default));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task NetworkErrorDoesNotExposeToken()
    {
        using var handler = new Handler((request, _) => throw new HttpRequestException($"Failed {request.RequestUri}"));
        using var identity = Identity(handler);
        var error = await Assert.ThrowsAsync<ApiError>(() => identity.GetUsername(default));
        Assert.Equal(503, error.Status);
        Assert.DoesNotContain(Token, error.ToString());
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task CallerCancellationIsPreservedAndReleasesLookup()
    {
        using var cancellation = new CancellationTokenSource();
        var calls = 0;
        using var handler = new Handler((_, ct) =>
        {
            if (++calls == 1)
            {
                cancellation.Cancel();
                ct.ThrowIfCancellationRequested();
            }
            return Task.FromResult(Response(ValidResponse));
        });
        using var identity = Identity(handler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => identity.GetUsername(cancellation.Token));
        Assert.Equal("lime_test_bot", await identity.GetUsername(default));
    }
}
