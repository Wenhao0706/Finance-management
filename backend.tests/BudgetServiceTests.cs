using FinanceManagement.API.Data;
using FinanceManagement.API.Models;
using FinanceManagement.API.Services;
using FinanceManagement.API.Tests.TestHelpers;

namespace FinanceManagement.API.Tests;

public class BudgetServiceTests : IDisposable
{
    private readonly InMemoryDbContextFactory _dbFactory = new();
    private const string Uid = "user-1";

    public void Dispose() => _dbFactory.Dispose();

    private static void AddTxn(AppDbContext db, string type, string category, decimal amount, DateTime date, string? classification = null, string userId = Uid)
    {
        db.Transactions.Add(new Transaction
        {
            UserId = userId, Type = type, Category = category, Amount = amount,
            Date = date, Description = "test", Classification = classification,
        });
    }

    [Fact]
    public async Task NewUserNoData_ExpectedIncome_DefaultsToZero()
    {
        using var db = _dbFactory.Create();
        var svc = new BudgetService(db);

        var s = await svc.GetSnapshotAsync(Uid, 2026, 4, CancellationToken.None);

        Assert.Equal(0m, s.ExpectedIncome);
        Assert.False(s.ExpectedIncomeIsExplicit);
        Assert.Equal(0.50m, s.Percentages.Needs);
        Assert.Equal(0.30m, s.Percentages.Wants);
        Assert.Equal(0.20m, s.Percentages.Savings);
        Assert.Equal(0m, s.Buckets.Needs.CapEffective);
        Assert.Empty(s.CategoryCaps);
    }

    [Fact]
    public async Task ExplicitBudget_OverridesDerivedIncome()
    {
        using var db = _dbFactory.Create();
        AddTxn(db, "income", "Salary", 1000m, new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc));
        db.Budgets.Add(new Budget { UserId = Uid, Year = 2026, Month = 4, ExpectedIncome = 5000m });
        await db.SaveChangesAsync();

        var svc = new BudgetService(db);
        var s = await svc.GetSnapshotAsync(Uid, 2026, 4, CancellationToken.None);

        Assert.Equal(5000m, s.ExpectedIncome);
        Assert.True(s.ExpectedIncomeIsExplicit);
        Assert.Equal(2500m, s.Buckets.Needs.CapBase);
    }

    [Fact]
    public async Task NoExplicitBudget_DerivesFromPreviousMonth()
    {
        using var db = _dbFactory.Create();
        AddTxn(db, "income", "Salary", 3000m, new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc));
        AddTxn(db, "income", "Freelance", 500m, new DateTime(2026, 3, 20, 0, 0, 0, DateTimeKind.Utc));
        await db.SaveChangesAsync();

        var svc = new BudgetService(db);
        var s = await svc.GetSnapshotAsync(Uid, 2026, 4, CancellationToken.None);

        Assert.Equal(3500m, s.ExpectedIncome);
        Assert.False(s.ExpectedIncomeIsExplicit);
    }

    [Fact]
    public async Task BucketSpent_UsesClassificationResolutionChain()
    {
        using var db = _dbFactory.Create();
        AddTxn(db, "expense", "Food & Dining", 100m, new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), classification: "Want");
        AddTxn(db, "expense", "Food & Dining", 200m, new DateTime(2026, 4, 2, 0, 0, 0, DateTimeKind.Utc), classification: null);
        db.Budgets.Add(new Budget { UserId = Uid, Year = 2026, Month = 4, ExpectedIncome = 1000m });
        await db.SaveChangesAsync();

        var svc = new BudgetService(db);
        var s = await svc.GetSnapshotAsync(Uid, 2026, 4, CancellationToken.None);

        Assert.Equal(200m, s.Buckets.Needs.Spent);
        Assert.Equal(100m, s.Buckets.Wants.Spent);
        Assert.Equal(0m, s.Buckets.Savings.Spent);
    }

    [Fact]
    public async Task CategoryBudgetClassificationOverride_AppliesToTransactionsWithoutOwnOverride()
    {
        using var db = _dbFactory.Create();
        db.CategoryBudgets.Add(new CategoryBudget { UserId = Uid, CategoryId = 4, Classification = "Want" });
        AddTxn(db, "expense", "Food & Dining", 300m, new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc));
        db.Budgets.Add(new Budget { UserId = Uid, Year = 2026, Month = 4, ExpectedIncome = 1000m });
        await db.SaveChangesAsync();

        var svc = new BudgetService(db);
        var s = await svc.GetSnapshotAsync(Uid, 2026, 4, CancellationToken.None);

        Assert.Equal(0m, s.Buckets.Needs.Spent);
        Assert.Equal(300m, s.Buckets.Wants.Spent);
    }

    [Fact]
    public async Task CarryIn_FlowsAcrossMonths()
    {
        using var db = _dbFactory.Create();
        db.Budgets.Add(new Budget { UserId = Uid, Year = 2026, Month = 3, ExpectedIncome = 1000m });
        AddTxn(db, "expense", "Housing", 300m, new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc));
        db.Budgets.Add(new Budget { UserId = Uid, Year = 2026, Month = 4, ExpectedIncome = 1000m });
        await db.SaveChangesAsync();

        var svc = new BudgetService(db);
        var s = await svc.GetSnapshotAsync(Uid, 2026, 4, CancellationToken.None);

        Assert.Equal(500m, s.Buckets.Needs.CapBase);
        Assert.Equal(200m, s.Buckets.Needs.CarryIn);
        Assert.Equal(700m, s.Buckets.Needs.CapEffective);
    }

    [Fact]
    public async Task UpdateBudget_RejectsPercentagesNotSummingToOne()
    {
        using var db = _dbFactory.Create();
        var svc = new BudgetService(db);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.UpdateBudgetAsync(Uid, 2026, 4,
                new BudgetUpdate(null, new BudgetPercentages(0.5m, 0.3m, 0.3m)),
                CancellationToken.None));
        Assert.Contains("percentage", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateBudget_RejectsNegativeIncome()
    {
        using var db = _dbFactory.Create();
        var svc = new BudgetService(db);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.UpdateBudgetAsync(Uid, 2026, 4,
                new BudgetUpdate(-100m, null),
                CancellationToken.None));
        Assert.Contains("income", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
