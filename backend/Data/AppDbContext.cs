using Microsoft.EntityFrameworkCore;
using FinanceManagement.API.Models;

namespace FinanceManagement.API.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<LoginAttempt> LoginAttempts => Set<LoginAttempt>();

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
            new Category { Id = 1, Name = "Salary", Type = "income", Icon = "payments" },
            new Category { Id = 2, Name = "Freelance", Type = "income", Icon = "work" },
            new Category { Id = 3, Name = "Investments", Type = "income", Icon = "trending_up" },
            new Category { Id = 4, Name = "Food & Dining", Type = "expense", Icon = "restaurant" },
            new Category { Id = 5, Name = "Transportation", Type = "expense", Icon = "directions_car" },
            new Category { Id = 6, Name = "Housing", Type = "expense", Icon = "home" },
            new Category { Id = 7, Name = "Utilities", Type = "expense", Icon = "bolt" },
            new Category { Id = 8, Name = "Entertainment", Type = "expense", Icon = "movie" },
            new Category { Id = 9, Name = "Healthcare", Type = "expense", Icon = "local_hospital" },
            new Category { Id = 10, Name = "Shopping", Type = "expense", Icon = "shopping_cart" }
        );
    }
}
