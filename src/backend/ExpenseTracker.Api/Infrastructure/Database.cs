using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Infrastructure;

public sealed class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long TelegramId { get; set; }
    public string Language { get; set; } = "uk";
    public string Theme { get; set; } = "auto";
    public string TimeZone { get; set; } = "Europe/Kyiv";
    public string Currency { get; set; } = "UAH";
    public bool AiConsent { get; set; }
    public string InputMode { get; set; } = "ai";
    public int Version { get; set; } = 1;
}

public sealed class Category
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Name { get; set; } = "";
    public string? Code { get; set; }
    public string? Icon { get; set; }
    public decimal? MonthlyLimit { get; set; }
    public string? LimitCurrency { get; set; }
    public bool Archived { get; set; }
    public int Version { get; set; } = 1;
}

public sealed class Expense
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid CategoryId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "UAH";
    public string Description { get; set; } = "";
    public DateOnly Date { get; set; }
    public string Source { get; set; } = "mini_app";
    public int Version { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Draft
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid RequestId { get; set; }
    public string InputHash { get; set; } = "";
    public string Source { get; set; } = "mini_app";
    public string InputKind { get; set; } = "text";
    public string Transcript { get; set; } = "";
    public decimal? Amount { get; set; }
    public string Description { get; set; } = "";
    public string Currency { get; set; } = "UAH";
    public Guid? CategoryId { get; set; }
    public DateOnly Date { get; set; }
    public string Status { get; set; } = "processing";
    public string? ErrorCode { get; set; }
    public int Version { get; set; } = 1;
    public DateTimeOffset ExpiresAt { get; set; }
    public Guid? ExpenseId { get; set; }
}

public sealed class SavingsAccount
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = "";
    public string Currency { get; set; } = "UAH";
    public decimal Added { get; set; }
    public decimal Spent { get; set; }
}

public sealed class SavingsEntry
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid AccountId { get; set; }
    public string Kind { get; set; } = "deposit";
    public decimal Amount { get; set; }
    public DateOnly Date { get; set; }
    public string Description { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class Session
{
    public string Id { get; set; } = "";
    public byte[] Ticket { get; set; } = [];
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class AuthProof
{
    public string Id { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class Conversation
{
    public Guid UserId { get; set; }
    public string State { get; set; } = "idle";
    public string? EntityId { get; set; }
    public string? Field { get; set; }
    public int Version { get; set; } = 1;
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class Delivery
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Kind { get; set; } = "inbox";
    public string DedupeKey { get; set; } = "";
    public string Payload { get; set; } = "{}";
    public DateTimeOffset AvailableAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? Lease { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int Attempts { get; set; }
}

public sealed class DemoSeed
{
    public Guid UserId { get; set; }
    public DateOnly Period { get; set; }
    public int ExpenseCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class Database(DbContextOptions<Database> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<Draft> Drafts => Set<Draft>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<AuthProof> AuthProofs => Set<AuthProof>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Delivery> Deliveries => Set<Delivery>();
    public DbSet<DemoSeed> DemoSeeds => Set<DemoSeed>();
    public DbSet<SavingsAccount> SavingsAccounts => Set<SavingsAccount>();
    public DbSet<SavingsEntry> SavingsEntries => Set<SavingsEntry>();

    // Serialize short financial/catalogue mutations for one owner. Never hold this over AI/Telegram I/O.
    public async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> LockUser(User user, CancellationToken ct)
    {
        var transaction = await Database.BeginTransactionAsync(ct);
        try
        {
            await Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({user.TelegramId})", ct);
            return transaction;
        }
        catch { await transaction.DisposeAsync(); throw; }
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<User>().HasIndex(x => x.TelegramId).IsUnique();
        model.Entity<User>().Property(x => x.Version).IsConcurrencyToken();
        model.Entity<User>().Property(x => x.InputMode).HasDefaultValue("ai");
        model.Entity<Category>().HasAlternateKey(x => new { x.UserId, x.Id });
        model.Entity<Category>().HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
        model.Entity<Category>().Property(x => x.Version).IsConcurrencyToken();
        model.Entity<Category>().Property(x => x.MonthlyLimit).HasPrecision(20, 4);
        model.Entity<Category>().ToTable(t => t.HasCheckConstraint("valid_category_limit",
            "(\"MonthlyLimit\" IS NULL AND \"LimitCurrency\" IS NULL) OR (\"MonthlyLimit\" IS NOT NULL AND \"MonthlyLimit\" > 0 AND \"LimitCurrency\" IS NOT NULL)"));
        model.Entity<Expense>(entity =>
        {
            entity.Property(x => x.Amount).HasPrecision(20, 4);
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.HasIndex(x => new { x.UserId, x.Date, x.Id });
            entity.HasOne<Category>().WithMany().HasForeignKey(x => new { x.UserId, x.CategoryId })
                .HasPrincipalKey(x => new { x.UserId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(t => t.HasCheckConstraint("positive_amount", "\"Amount\" > 0"));
        });
        model.Entity<Draft>().HasIndex(x => new { x.UserId, x.RequestId }).IsUnique();
        model.Entity<Draft>().HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
        model.Entity<Draft>().Property(x => x.Amount).HasPrecision(20, 4);
        model.Entity<Draft>().Property(x => x.Version).IsConcurrencyToken();
        model.Entity<Conversation>().HasKey(x => x.UserId);
        model.Entity<Conversation>().HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
        model.Entity<Conversation>().Property(x => x.Version).IsConcurrencyToken();
        model.Entity<Delivery>().HasIndex(x => new { x.Kind, x.DedupeKey }).IsUnique();
        model.Entity<Delivery>().HasIndex(x => new { x.Kind, x.CompletedAt, x.AvailableAt });
        model.Entity<DemoSeed>().HasKey(x => new { x.UserId, x.Period });
        model.Entity<DemoSeed>().HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
        model.Entity<SavingsAccount>(entity =>
        {
            entity.HasAlternateKey(x => new { x.UserId, x.Id });
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
            entity.Property(x => x.Name).HasMaxLength(60);
            entity.Property(x => x.Added).HasPrecision(28, 4);
            entity.Property(x => x.Spent).HasPrecision(28, 4);
            entity.ToTable(t => t.HasCheckConstraint("valid_savings_balance", "\"Added\" >= 0 AND \"Spent\" >= 0 AND \"Spent\" <= \"Added\""));
        });
        model.Entity<SavingsEntry>(entity =>
        {
            entity.Property(x => x.Amount).HasPrecision(20, 4);
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.HasIndex(x => new { x.UserId, x.AccountId, x.Date, x.CreatedAt, x.Id });
            entity.HasOne<SavingsAccount>().WithMany().HasForeignKey(x => new { x.UserId, x.AccountId })
                .HasPrincipalKey(x => new { x.UserId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(t =>
            {
                t.HasCheckConstraint("positive_savings_amount", "\"Amount\" > 0");
                t.HasCheckConstraint("valid_savings_kind", "\"Kind\" IN ('deposit', 'withdrawal')");
            });
        });
    }
}
