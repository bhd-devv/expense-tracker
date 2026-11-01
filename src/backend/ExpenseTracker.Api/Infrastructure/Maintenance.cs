using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Infrastructure;

public sealed class Maintenance(IServiceScopeFactory scopes, TimeProvider clock, ILogger<Maintenance> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<Database>();
                var now = clock.GetUtcNow();
                logger.LogInformation("Maintenance started");
                var sessions = await db.Sessions.Where(x => x.ExpiresAt < now).ExecuteDeleteAsync(stoppingToken);
                var proofs = await db.AuthProofs.Where(x => x.ExpiresAt < now).ExecuteDeleteAsync(stoppingToken);
                var conversations = await db.Conversations.Where(x => x.ExpiresAt < now && x.State != "idle").ExecuteUpdateAsync(
                    set => set.SetProperty(x => x.State, "idle").SetProperty(x => x.EntityId, (string?)null)
                        .SetProperty(x => x.Field, (string?)null).SetProperty(x => x.Version, x => x.Version + 1), stoppingToken);
                var drafts = await db.Drafts.Where(x => x.ExpiresAt < now && x.Status != "saved" && x.Status != "cancelled").ExecuteUpdateAsync(
                    set => set.SetProperty(x => x.Transcript, "").SetProperty(x => x.Description, "")
                        .SetProperty(x => x.Amount, (decimal?)null).SetProperty(x => x.AccountId, (Guid?)null).SetProperty(x => x.CategoryId, (Guid?)null)
                        .SetProperty(x => x.Status, "cancelled").SetProperty(x => x.Version, x => x.Version + 1), stoppingToken);
                var deliveries = await db.Deliveries.Where(x => x.CompletedAt == null && x.AvailableAt < now.AddDays(-1)).ExecuteUpdateAsync(
                    set => set.SetProperty(x => x.Payload, "{}").SetProperty(x => x.CompletedAt, now), stoppingToken);
                logger.LogInformation("Maintenance completed sessions={Sessions} proofs={Proofs} conversations={Conversations} drafts={Drafts} deliveries={Deliveries}",
                    sessions, proofs, conversations, drafts, deliveries);
            }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested)
            { logger.LogError("Maintenance failed: {Failure}", System.Text.Json.JsonSerializer.Serialize(SafeError.Describe(error))); }
        }
    }
}
