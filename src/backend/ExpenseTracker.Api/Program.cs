using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using ExpenseTracker.Api.Features;
using ExpenseTracker.Api.Features.Auth;
using ExpenseTracker.Api.Features.BotIntegration;
using ExpenseTracker.Api.Features.Catalogs;
using ExpenseTracker.Api.Features.Expenses;
using ExpenseTracker.Api.Features.Users;
using ExpenseTracker.Api.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args.Where(x => x != "--migrate").ToArray());
var configuration = builder.Configuration;
var development = builder.Environment.IsDevelopment();
var connection = configuration.GetConnectionString("Database");
if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException("ConnectionStrings__Database is required");
var origin = configuration["App:Origin"] ?? "";
if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri) || originUri.Scheme is not ("http" or "https")
    || originUri.AbsolutePath != "/" || originUri.Query.Length != 0 || originUri.Fragment.Length != 0 || originUri.UserInfo.Length != 0
    || (!development && originUri.Scheme != "https"))
    throw new InvalidOperationException("App__Origin must be an absolute HTTPS origin in production");
if (!development && ((configuration["Telegram:ServiceKey"]?.Length ?? 0) < 32 || string.IsNullOrWhiteSpace(configuration["Telegram:BotToken"])
    || string.IsNullOrWhiteSpace(configuration["App:KeyPath"])))
    throw new InvalidOperationException("Production requires Telegram credentials and persistent App__KeyPath");

builder.Services.AddDbContext<Database>(options => options.UseNpgsql(connection));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOptions<TelegramValidationOptions>().Bind(configuration.GetSection("Telegram"))
    .Validate(options => options.ProofMaxAgeSeconds is >= 60 and <= 600 && options.ProofFutureSkewSeconds is >= 0 and <= 60, "Invalid Telegram proof TTL/skew")
    .ValidateOnStart();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<ExpenseService>();
builder.Services.AddSingleton<TelegramBotIdentity>();
builder.Services.AddHttpClient("TelegramIdentity", client =>
{
    client.BaseAddress = new Uri("https://api.telegram.org/");
    client.Timeout = TimeSpan.FromSeconds(10);
    client.MaxResponseContentBufferSize = 64 * 1024;
}).RemoveAllLoggers(); // Telegram API URLs contain the bot token.
builder.Services.AddHostedService<Maintenance>();
builder.Services.AddHttpClient<OpenRouter>(client =>
{
    client.BaseAddress = new Uri(configuration["OpenRouter:BaseUrl"] ?? "https://openrouter.ai/api/v1/");
    client.Timeout = TimeSpan.FromSeconds(75);
    client.MaxResponseContentBufferSize = 64 * 1024;
});
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict;
    options.SerializerOptions.RespectNullableAnnotations = true;
});
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<Errors>();
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = 11 * 1024 * 1024);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 11 * 1024 * 1024);
var keyPath = string.IsNullOrWhiteSpace(configuration["App:KeyPath"])
    ? Path.Combine(Path.GetTempPath(), "expense-tracker-keys") : configuration["App:KeyPath"] ?? throw new InvalidOperationException();
builder.Services.AddDataProtection().SetApplicationName("expense-tracker")
    .PersistKeysToFileSystem(new DirectoryInfo(keyPath));
builder.Services.AddSingleton<SessionStore>();
builder.Services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
    .Configure<SessionStore>((options, store) => options.SessionStore = store);
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = development ? "expense-tracker.dev" : "__Host-expense-tracker";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = development ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.Path = "/";
        options.ExpireTimeSpan = TimeSpan.FromHours(24);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
        options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
        options.Events.OnValidatePrincipal = context =>
        {
            if (!context.Properties.Items.TryGetValue("absolute", out var value) || !long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var absolute)
                || absolute <= context.HttpContext.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow().ToUnixTimeSeconds())
                context.RejectPrincipal();
            return Task.CompletedTask;
        };
    }).AddScheme<AuthenticationSchemeOptions, BotAuthentication>("Bot", _ => { });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Browser", policy => policy.AddAuthenticationSchemes(CookieAuthenticationDefaults.AuthenticationScheme).RequireAuthenticatedUser().RequireClaim("user_id"))
    .AddPolicy("Bot", policy => policy.AddAuthenticationSchemes("Bot").RequireClaim("service", "bot"))
    .AddPolicy("BotActor", policy => policy.AddAuthenticationSchemes("Bot").RequireClaim("service", "bot").RequireClaim("user_id"));
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-Token";
    options.Cookie.Name = development ? "expense-tracker.csrf.dev" : "__Host-expense-tracker-csrf";
    options.Cookie.SecurePolicy = development ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1) }));
    options.AddPolicy("ai", context => RateLimitPartition.GetFixedWindowLimiter(context.User.FindFirst("user_id")?.Value ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
    options.AddPolicy("workspace", context => RateLimitPartition.GetFixedWindowLimiter(context.User.FindFirst("user_id")?.Value ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1) }));
});
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var proxy in configuration.GetSection("App:KnownProxies").Get<string[]>() ?? [])
        options.KnownProxies.Add(IPAddress.Parse(proxy));
});

var app = builder.Build();
if (args.Contains("--migrate"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<Database>().Database.MigrateAsync();
    return;
}
app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.Headers.CacheControl = "no-store";
        if (context.Request.Method is not ("GET" or "HEAD" or "OPTIONS"))
        {
            if (context.Request.Headers.Origin != origin.TrimEnd('/')) throw new ApiError("invalid_origin", 403);
            try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
            catch (AntiforgeryValidationException) { throw new ApiError("invalid_csrf", 403); }
        }
    }
    await next(context);
});
app.MapGet("/api/health/live", () => Results.Ok(new { status = "ok" }));
app.MapGet("/api/health/ready", async (Database db, CancellationToken ct) =>
    await db.Database.CanConnectAsync(ct) ? Results.Ok(new { status = "ok" }) : Results.StatusCode(503));
if (development) app.MapOpenApi();
app.MapAuth();
app.MapGroup("/api").RequireAuthorization("Browser").RequireRateLimiting("workspace").MapWorkspace("mini_app");
app.MapBot();
app.Run();

public partial class Program;
