using ExpenseTracker.Api.Features.Catalogs;
using ExpenseTracker.Api.Features.Expenses;
using ExpenseTracker.Api.Features.Users;
using ExpenseTracker.Api.Infrastructure;

namespace ExpenseTracker.Api.Features;

public static class WorkspaceEndpoints
{
    public static void MapWorkspace(this RouteGroupBuilder group, string source)
    {
        group.MapGet("/profile", async (HttpContext context, UserService users, CancellationToken ct) =>
            UserService.View(await users.Current(context, ct)));
        group.MapPut("/profile", async (PreferencesRequest input, HttpContext context, UserService users, CancellationToken ct) =>
            await users.Update(await users.Current(context, ct), input, ct));
        group.MapGet("/catalog", async (HttpContext context, UserService users, CatalogService catalogs, CancellationToken ct) =>
            await catalogs.List(await users.Current(context, ct), ct));
        group.MapPost("/categories", async (CatalogRequest input, HttpContext context, UserService users, CatalogService catalogs, CancellationToken ct) =>
            await catalogs.Category(await users.Current(context, ct), null, input, ct));
        group.MapPut("/categories/{id:guid}", async (Guid id, CatalogRequest input, HttpContext context, UserService users, CatalogService catalogs, CancellationToken ct) =>
            await catalogs.Category(await users.Current(context, ct), id, input, ct));
        group.MapGet("/expenses", async (DateOnly? from, DateOnly? to, Guid? categoryId, int? page,
            HttpContext context, UserService users, ExpenseService expenses, CancellationToken ct) =>
            await expenses.List(await users.Current(context, ct), from, to, categoryId, page ?? 0, ct));
        group.MapGet("/expenses/{id:guid}", async (Guid id, HttpContext context, UserService users, ExpenseService expenses, CancellationToken ct) =>
            ExpenseService.View(await expenses.Find(await users.Current(context, ct), id, ct)));
        group.MapPut("/expenses/{id:guid}", async (Guid id, ExpenseInput input, HttpContext context, UserService users, ExpenseService expenses, CancellationToken ct) =>
            await expenses.Edit(await users.Current(context, ct), id, input, ct));
        group.MapDelete("/expenses/{id:guid}", async (Guid id, int version, HttpContext context, UserService users, ExpenseService expenses, CancellationToken ct) =>
        {
            await expenses.Delete(await users.Current(context, ct), id, version, ct);
            return Results.NoContent();
        });
        group.MapGet("/reports", async (DateOnly? from, DateOnly? to, Guid? categoryId,
            HttpContext context, UserService users, ExpenseService expenses, CancellationToken ct) =>
            await expenses.Report(await users.Current(context, ct), from, to, categoryId, ct));
        group.MapPost("/drafts", async (CreateDraftRequest input, HttpContext context, UserService users, ExpenseService expenses, CancellationToken ct) =>
            await expenses.Create(await users.Current(context, ct), input.RequestId, input.Text, source, null, ct)).RequireRateLimiting("ai");
        group.MapPost("/drafts/audio", async (HttpRequest request, HttpContext context, UserService users, ExpenseService expenses, ILogger<OpenRouter> logger, CancellationToken ct) =>
        {
            var user = await users.Current(context, ct);
            if (!user.AiConsent) throw new ApiError("ai_consent_required");
            if (!request.HasFormContentType) throw new ApiError("invalid_audio");
            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("audio") ?? throw new ApiError("invalid_audio");
            if (file.Length is <= 0 or > 10 * 1024 * 1024) throw new ApiError("audio_too_large", 413);
            if (!Guid.TryParse(form["requestId"], out var requestId)) throw new ApiError("invalid_request_id");
            await using var stream = file.OpenReadStream();
            var audio = await OpenRouter.NormalizeAudio(stream, ct, logger);
            return await expenses.Create(user, requestId, "", source, audio, ct);
        }).DisableAntiforgery().RequireRateLimiting("ai");
        group.MapGet("/drafts/{id:guid}", async (Guid id, HttpContext context, UserService users, ExpenseService expenses, CancellationToken ct) =>
            ExpenseService.ViewDraft(await expenses.FindDraft(await users.Current(context, ct), id, ct)));
        group.MapPut("/drafts/{id:guid}", async (Guid id, DraftEditRequest input, HttpContext context, UserService users, ExpenseService expenses, CancellationToken ct) =>
            await expenses.EditDraft(await users.Current(context, ct), id, input, ct));
        group.MapPost("/drafts/{id:guid}/confirm", async (Guid id, ExpenseInput input, HttpContext context, UserService users, ExpenseService expenses, CancellationToken ct) =>
            await expenses.Confirm(await users.Current(context, ct), id, input, ct));
        group.MapPost("/drafts/{id:guid}/cancel", async (Guid id, VersionRequest input, HttpContext context, UserService users, ExpenseService expenses, CancellationToken ct) =>
            await expenses.Cancel(await users.Current(context, ct), id, input.Version, ct));
    }
}
