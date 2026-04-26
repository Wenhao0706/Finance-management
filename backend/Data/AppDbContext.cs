using Microsoft.EntityFrameworkCore;
using FinanceManagement.API.Models;

namespace FinanceManagement.API.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<LoginAttempt> LoginAttempts => Set<LoginAttempt>();
    public DbSet<BlockedIp> BlockedIps => Set<BlockedIp>();
    public DbSet<EmailNotification> EmailNotifications => Set<EmailNotification>();
    public DbSet<Budget> Budgets => Set<Budget>();
    public DbSet<CategoryBudget> CategoryBudgets => Set<CategoryBudget>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Transaction>()
            .Property(t => t.Amount)
            .HasPrecision(18, 2);

        modelBuilder.Entity<Transaction>()
            .HasIndex(t => new { t.UserId, t.Date });

        modelBuilder.Entity<LoginAttempt>()
            .Property(a => a.Email)
            .HasMaxLength(254);  // RFC 5321 max email length

        modelBuilder.Entity<LoginAttempt>()
            .Property(a => a.IpAddress)
            .HasMaxLength(45);   // IPv6 textual max

        modelBuilder.Entity<LoginAttempt>()
            .HasIndex(a => new { a.Email, a.IpAddress, a.AttemptedAt });

        modelBuilder.Entity<Category>().HasData(
            new Category { Id = 1,  Name = "Salary",         Type = "income",  Icon = "payments",       Classification = null },
            new Category { Id = 2,  Name = "Freelance",      Type = "income",  Icon = "work",           Classification = null },
            new Category { Id = 3,  Name = "Investments",    Type = "income",  Icon = "trending_up",    Classification = null },
            new Category { Id = 4,  Name = "Food & Dining",  Type = "expense", Icon = "restaurant",     Classification = "Need" },
            new Category { Id = 5,  Name = "Transportation", Type = "expense", Icon = "directions_car", Classification = "Need" },
            new Category { Id = 6,  Name = "Housing",        Type = "expense", Icon = "home",           Classification = "Need" },
            new Category { Id = 7,  Name = "Utilities",      Type = "expense", Icon = "bolt",           Classification = "Need" },
            new Category { Id = 8,  Name = "Entertainment",  Type = "expense", Icon = "movie",          Classification = "Want" },
            new Category { Id = 9,  Name = "Healthcare",     Type = "expense", Icon = "local_hospital", Classification = "Need" },
            new Category { Id = 10, Name = "Shopping",       Type = "expense", Icon = "shopping_cart",  Classification = "Want" },
            new Category { Id = 11, Name = "Savings",        Type = "expense", Icon = "savings",        Classification = "Savings" }
        );

        modelBuilder.Entity<Category>()
            .Property(c => c.Classification)
            .HasMaxLength(16);

        modelBuilder.Entity<Transaction>()
            .Property(t => t.Classification)
            .HasMaxLength(16);

        modelBuilder.Entity<Budget>(b =>
        {
            b.HasIndex(x => new { x.UserId, x.Year, x.Month }).IsUnique();
            b.Property(x => x.UserId).HasMaxLength(128).IsRequired();
            b.Property(x => x.ExpectedIncome).HasPrecision(18, 2);
            b.Property(x => x.NeedsPct).HasPrecision(5, 4);
            b.Property(x => x.WantsPct).HasPrecision(5, 4);
            b.Property(x => x.SavingsPct).HasPrecision(5, 4);
        });

        modelBuilder.Entity<CategoryBudget>(b =>
        {
            b.HasIndex(x => new { x.UserId, x.CategoryId }).IsUnique();
            b.Property(x => x.UserId).HasMaxLength(128).IsRequired();
            b.Property(x => x.Classification).HasMaxLength(16);
            b.Property(x => x.MonthlyCap).HasPrecision(18, 2);
        });

        modelBuilder.Entity<BlockedIp>(b =>
        {
            b.HasIndex(x => x.IpAddress).IsUnique();
            b.HasIndex(x => x.ExpiresAt);
            b.Property(x => x.IpAddress).HasMaxLength(45).IsRequired();
            b.Property(x => x.Reason).HasMaxLength(64).IsRequired();
        });

        modelBuilder.Entity<EmailNotification>(b =>
        {
            b.HasIndex(x => new { x.Kind, x.Recipient, x.Key, x.SentAt });
            b.Property(x => x.Kind).HasMaxLength(32).IsRequired();
            b.Property(x => x.Recipient).HasMaxLength(254).IsRequired();  // RFC 5321 max email length, matches LoginAttempt.Email
            b.Property(x => x.Key).HasMaxLength(64).IsRequired();
        });
    }
}
