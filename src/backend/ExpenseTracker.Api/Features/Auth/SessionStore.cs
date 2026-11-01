using ExpenseTracker.Api.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Features.Auth;

public sealed class SessionStore(IServiceScopeFactory scopes, IDataProtectionProvider protection, TimeProvider clock) : ITicketStore
{
    private readonly IDataProtector protector = protection.CreateProtector("expense-tracker.sessions.v1");

    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var key = Guid.NewGuid().ToString("N");
        await Save(key, ticket, true);
        return key;
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket) => Save(key, ticket, false);

    private async Task Save(string key, AuthenticationTicket ticket, bool create)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        var session = create ? new Session { Id = key } : await db.Sessions.FindAsync(key);
        if (session is null) return;
        session.Ticket = protector.Protect(TicketSerializer.Default.Serialize(ticket));
        session.ExpiresAt = ticket.Properties.ExpiresUtc ?? clock.GetUtcNow().AddHours(24);
        if (create) db.Sessions.Add(session);
        await db.SaveChangesAsync();
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        var session = await db.Sessions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == key);
        if (session is null || session.ExpiresAt <= clock.GetUtcNow()) return null;
        try { return TicketSerializer.Default.Deserialize(protector.Unprotect(session.Ticket)); }
        catch (System.Security.Cryptography.CryptographicException) { return null; }
    }

    public async Task RemoveAsync(string key)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await db.Sessions.Where(x => x.Id == key).ExecuteDeleteAsync();
    }
}
