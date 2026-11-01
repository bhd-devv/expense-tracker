using System.Text.Json;
using ExpenseTracker.Api.Features.Users;
using ExpenseTracker.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Features.BotIntegration;

public sealed record InboxRequest(long UpdateId, JsonElement Payload);
public sealed record LeaseDto(Guid Id, Guid Token, JsonElement Payload, int Attempts);
public sealed record LeaseResult(Guid Token, bool Success);
public sealed record ConversationDto(string State, string? EntityId, string? Field, int Version);
public sealed record ConversationRequest(string State, string? EntityId, string? Field, int Version);

public static class BotEndpoints
{
    public static void MapBot(this WebApplication app)
    {
        var group = app.MapGroup("/internal/bot").RequireAuthorization("Bot");
        group.MapPost("/inbox", async (InboxRequest input, Database db, CancellationToken ct) =>
        {
            if (input.UpdateId < 0 || input.Payload.GetRawText().Length > 65536) throw new ApiError("invalid_update");
            var key = input.UpdateId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Deliveries\" (\"Id\",\"Kind\",\"DedupeKey\",\"Payload\",\"AvailableAt\",\"Attempts\") VALUES ({Guid.NewGuid()},'inbox',{key},{input.Payload.GetRawText()},{DateTimeOffset.UtcNow},0) ON CONFLICT (\"Kind\",\"DedupeKey\") DO NOTHING", ct);
            return Results.NoContent();
        });
        group.MapPost("/queues/{kind}/claim", async (string kind, Database db, TimeProvider clock, CancellationToken ct) =>
        {
            if (kind is not ("inbox" or "outbox")) throw new ApiError("not_found", 404);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var now = clock.GetUtcNow();
            var delivery = await db.Deliveries.FromSqlInterpolated($"SELECT * FROM \"Deliveries\" WHERE \"Kind\"={kind} AND \"CompletedAt\" IS NULL AND \"Attempts\" < 8 AND \"AvailableAt\" <= {now} AND (\"LeaseUntil\" IS NULL OR \"LeaseUntil\" <= {now}) ORDER BY \"AvailableAt\" LIMIT 1 FOR UPDATE SKIP LOCKED")
                .SingleOrDefaultAsync(ct);
            if (delivery is null) return Results.NoContent();
            var token = Guid.NewGuid();
            delivery.Lease = token;
            delivery.LeaseUntil = now.AddMinutes(3);
            delivery.Attempts++;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Results.Ok(new LeaseDto(delivery.Id, token, JsonSerializer.Deserialize<JsonElement>(delivery.Payload), delivery.Attempts));
        }).Produces<LeaseDto>(200).Produces(204);
        group.MapPost("/queues/{id:guid}/result", async (Guid id, LeaseResult input, Database db, TimeProvider clock, CancellationToken ct) =>
        {
            var now = clock.GetUtcNow();
            var query = db.Deliveries.Where(x => x.Id == id && x.Lease == input.Token && x.LeaseUntil > now && x.CompletedAt == null);
            var changed = input.Success
                ? await query.ExecuteUpdateAsync(set => set.SetProperty(x => x.CompletedAt, now).SetProperty(x => x.Payload, "{}"), ct)
                : await query.ExecuteUpdateAsync(set => set.SetProperty(x => x.LeaseUntil, (DateTimeOffset?)null).SetProperty(x => x.AvailableAt, now.AddSeconds(30)), ct);
            if (changed == 0) throw new ApiError("lease_expired", 409);
            return Results.NoContent();
        });
        var actor = group.MapGroup("/actor").RequireAuthorization("BotActor");
        actor.MapWorkspace("bot");
        actor.MapGet("/conversation", async (HttpContext context, UserService users, Database db, TimeProvider clock, CancellationToken ct) =>
        {
            var user = await users.Current(context, ct);
            var state = await db.Conversations.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == user.Id, ct);
            return state is not null && state.ExpiresAt > clock.GetUtcNow()
                ? new ConversationDto(state.State, state.EntityId, state.Field, state.Version)
                : new ConversationDto("idle", null, null, state?.Version ?? 0);
        });
        actor.MapPut("/conversation", async (ConversationRequest input, HttpContext context, UserService users, Database db, TimeProvider clock, CancellationToken ct) =>
        {
            var user = await users.Current(context, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({user.TelegramId})", ct);
            var state = await db.Conversations.SingleOrDefaultAsync(x => x.UserId == user.Id, ct);
            if ((state?.Version ?? 0) != input.Version) throw new ApiError("conflict", 409);
            if (input.State.Length > 40 || input.Field?.Length > 40 || input.EntityId?.Length > 100) throw new ApiError("invalid_state");
            if (state is null) { state = new Conversation { UserId = user.Id }; db.Add(state); }
            state.State = input.State;
            state.EntityId = input.EntityId;
            state.Field = input.Field;
            state.ExpiresAt = clock.GetUtcNow().AddMinutes(30);
            state.Version++;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new ConversationDto(state.State, state.EntityId, state.Field, state.Version);
        });
    }
}
