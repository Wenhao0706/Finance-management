using FinanceManagement.API.Data;
using FinanceManagement.API.Models;
using FinanceManagement.API.Services;
using FinanceManagement.API.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FinanceManagement.API.Tests;

public class BudgetAlertDetectorTests : IDisposable
{
    private readonly InMemoryDbContextFactory _dbFactory = new();
    private const string Uid = "user-1";

    public void Dispose() => _dbFactory.Dispose();

    private sealed class FakeQueue : IBackgroundTaskQueue
    {
        public List<Func<IServiceProvider, CancellationToken, Task>> Items { get; } = new();
        public bool TryEnqueue(Func<IServiceProvider, CancellationToken, Task> work) { Items.Add(work); return true; }
        public ValueTask<Func<IServiceProvider, CancellationToken, Task>> DequeueAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private (BudgetAlertDetector det, FakeQueue q) Build(AppDbContext db)
    {
        var budgetSvc = new BudgetService(db);
        var queue = new FakeQueue();
        var det = new BudgetAlertDetector(db, budgetSvc, queue, NullLogger<BudgetAlertDetector>.Instance);
        return (det, queue);
    }

    private static void AddTxn(AppDbContext db, string type, string category, decimal amount, DateTime date, string? cls = null)
    {
        db.Transactions.Add(new Transaction
        {
            UserId = Uid, Type = type, Category = category, Amount = amount, Date = date,
            Description = "test", Classification = cls,
        });
    }

    [Fact]
    public async Task BucketCrosses100_EnqueuesAlert()
    {
        using var db = _dbFactory.Create();
        db.Budgets.Add(new Budget { UserId = Uid, Year = 2026, Month = 4, ExpectedIncome = 1000m });
        AddTxn(db, "expense", "Housing", 600m, new DateTime(2026, 4, 5, 0, 0, 0, DateTimeKind.Utc));  // Needs 600/500 → over
        await db.SaveChangesAsync();

        var (det, q) = Build(db);
        await det.OnTransactionChangedAsync(Uid, 2026, 4, CancellationToken.None);

        Assert.Single(q.Items);
    }

    [Fact]
    public async Task BucketAlreadyOver_DoesNotEnqueueAgain()
    {
        using var db = _dbFactory.Create();
        db.Budgets.Add(new Budget { UserId = Uid, Year = 2026, Month = 4, ExpectedIncome = 1000m });
        AddTxn(db, "expense", "Housing", 600m, new DateTime(2026, 4, 5, 0, 0, 0, DateTimeKind.Utc));
        // Already-sent alert recorded
        db.EmailNotifications.Add(new EmailNotification
        {
            Kind = "BudgetBucket100",
            Recipient = "user@example.com",
            Key = "2026-04:Need",
            SentAt = new DateTime(2026, 4, 5, 1, 0, 0, DateTimeKind.Utc),
        });
        await db.SaveChangesAsync();

        var (det, q) = Build(db);
        await det.OnTransactionChangedAsync(Uid, 2026, 4, CancellationToken.None);

        Assert.Empty(q.Items);
    }

    [Fact]
    public async Task BucketUnder100_DoesNotEnqueue()
    {
        using var db = _dbFactory.Create();
        db.Budgets.Add(new Budget { UserId = Uid, Year = 2026, Month = 4, ExpectedIncome = 1000m });
        AddTxn(db, "expense", "Housing", 400m, new DateTime(2026, 4, 5, 0, 0, 0, DateTimeKind.Utc));  // Needs 400/500 → 80%, ok
        await db.SaveChangesAsync();

        var (det, q) = Build(db);
        await det.OnTransactionChangedAsync(Uid, 2026, 4, CancellationToken.None);

        Assert.Empty(q.Items);
    }

    [Fact]
    public async Task CategoryCapCrosses100_EnqueuesAlert()
    {
        using var db = _dbFactory.Create();
        db.Budgets.Add(new Budget { UserId = Uid, Year = 2026, Month = 4, ExpectedIncome = 5000m });
        db.CategoryBudgets.Add(new CategoryBudget { UserId = Uid, CategoryId = 4, MonthlyCap = 200m });  // Food cap 200
        AddTxn(db, "expense", "Food & Dining", 250m, new DateTime(2026, 4, 5, 0, 0, 0, DateTimeKind.Utc));
        await db.SaveChangesAsync();

        var (det, q) = Build(db);
        await det.OnTransactionChangedAsync(Uid, 2026, 4, CancellationToken.None);

        Assert.Single(q.Items);  // Category Food crossed
    }

    [Fact]
    public async Task BucketAndCategory_BothCross_TwoSeparateEnqueues()
    {
        using var db = _dbFactory.Create();
        db.Budgets.Add(new Budget { UserId = Uid, Year = 2026, Month = 4, ExpectedIncome = 1000m });
        db.CategoryBudgets.Add(new CategoryBudget { UserId = Uid, CategoryId = 6, MonthlyCap = 400m });  // Housing cap 400
        AddTxn(db, "expense", "Housing", 600m, new DateTime(2026, 4, 5, 0, 0, 0, DateTimeKind.Utc));  // Needs 600/500 over + Housing cap 600/400 over
        await db.SaveChangesAsync();

        var (det, q) = Build(db);
        await det.OnTransactionChangedAsync(Uid, 2026, 4, CancellationToken.None);

        Assert.Equal(2, q.Items.Count);
    }
}
