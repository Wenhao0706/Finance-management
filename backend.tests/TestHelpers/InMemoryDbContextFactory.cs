using FinanceManagement.API.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FinanceManagement.API.Tests.TestHelpers;

// Each test gets a fresh in-memory SQLite DB — full EF query support
// (unlike EF.InMemory which can't translate complex LINQ).
public sealed class InMemoryDbContextFactory : IDisposable
{
    private readonly SqliteConnection _connection;

    public InMemoryDbContextFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        // Note: EnsureCreated builds the schema but does NOT apply HasData() seed
        // rows (see AppDbContext.OnModelCreating — 10 categories). Tests that need
        // reference data must insert it explicitly.
        using var context = Create();
        context.Database.EnsureCreated();
    }

    public AppDbContext Create()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        return new AppDbContext(options);
    }

    public void Dispose() => _connection.Dispose();
}
