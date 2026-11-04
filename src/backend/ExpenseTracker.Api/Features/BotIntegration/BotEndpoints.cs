using System.Text.Json;
using ExpenseTracker.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Features.BotIntegration;

public sealed record InboxRequest(long UpdateId, JsonElement Payload);
public sealed record LeaseDto(Guid Id, Guid Token, JsonElement Payload, int Attempts);
public sealed record LeaseResult(Guid Token, bool Success);

public static class BotEndpoints
{
    public static void MapBot(this WebApplication app)
    {
        var group = app.MapGroup("/internal/bot").RequireAuthorization("Bot");
        group.MapPost("/inbox", async (InboxRequest input, Database db, CancellationToken ct) =>
        {
            if (input.UpdateId < 0 || input.Payload.GetRawText().Length > 65536) throw new ApiError("invalid_update");
            var key = input.UpdateId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Deliveries\" (\"Id\",\"Kind\",\"DedupeKey\",\"Payload\",\"AvailableAt\",\"Attempts\") VALUES ({Guid.NewGuid()},'inbox',{key},{input.Payload.GetRawText()},{DateTimeOffset.UtcNow},0) ON CONFLICT (\"Kind\",\"DedupeKey\") DO NOTHING", ct);
            app.Logger.LogInformation("Telegram update accepted updateId={UpdateId} duplicate={Duplicate}", input.UpdateId, inserted == 0);
            return Results.NoContent();
        });
        group.MapPost("/queues/{kind}/claim", async (string kind, Database db, TimeProvider clock, CancellationToken ct) =>
        {
            if (kind != "inbox") throw new ApiError("not_found", 404);
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
            app.Logger.LogInformation("Delivery claimed deliveryId={DeliveryId} kind={Kind} attempt={Attempt}", delivery.Id, kind, delivery.Attempts);
            if (delivery.Attempts == 8) app.Logger.LogWarning("Delivery final attempt deliveryId={DeliveryId} kind={Kind}", delivery.Id, kind);
            return Results.Ok(new LeaseDto(delivery.Id, token, JsonSerializer.Deserialize<JsonElement>(delivery.Payload), delivery.Attempts));
        }).Produces<LeaseDto>(200).Produces(204);
        group.MapPost("/queues/{id:guid}/result", async (Guid id, LeaseResult input, Database db, TimeProvider clock, CancellationToken ct) =>
        {
            var now = clock.GetUtcNow();
            var query = db.Deliveries.Where(x => x.Kind == "inbox" && x.Id == id && x.Lease == input.Token && x.LeaseUntil > now && x.CompletedAt == null);
            var changed = input.Success
                ? await query.ExecuteUpdateAsync(set => set.SetProperty(x => x.CompletedAt, now).SetProperty(x => x.Payload, "{}"), ct)
                : await query.ExecuteUpdateAsync(set => set.SetProperty(x => x.LeaseUntil, (DateTimeOffset?)null).SetProperty(x => x.AvailableAt, now.AddSeconds(30)), ct);
            if (changed == 0) throw new ApiError("lease_expired", 409);
            app.Logger.Log(input.Success ? LogLevel.Information : LogLevel.Warning,
                "Delivery result deliveryId={DeliveryId} success={Success}", id, input.Success);
            return Results.NoContent();
        });
    }
}
