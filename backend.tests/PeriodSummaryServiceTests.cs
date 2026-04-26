using FinanceManagement.API.Data;
using FinanceManagement.API.Models;
using FinanceManagement.API.Services;
using FinanceManagement.API.Tests.TestHelpers;

namespace FinanceManagement.API.Tests;

public class PeriodSummaryServiceTests : IDisposable
{
    private readonly InMemoryDbContextFactory _dbFactory = new();
    private const string Uid = "user-1";

    public void Dispose() => _dbFactory.Dispose();

    private static void AddTxn(AppDbContext db, string type, string category, decimal amount, DateTime date, string userId = Uid)
    {
        db.Transactions.Add(new Transaction
        {
            UserId = userId,
            Type = type,
            Category = category,
            Amount = amount,
            Date = date,
            Description = "test",
        });
    }

    [Fact]
    public async Task EmptyUser_ReturnsAllZeros()
    {
        using var db = _dbFactory.Create();
        var svc = new PeriodSummaryService(db);

        var s = await svc.GetMonthlyAsync(Uid, 2026, 4, CancellationToken.None);

        Assert.Equal(0m, s.Income);
        Assert.Equal(0m, s.Expenses);
        Assert.Equal(0m, s.Savings);
        Assert.Equal(0m, s.NetFlow);
        Assert.Equal(0m, s.RunningBalance);
        Assert.Empty(s.CategoryBreakdown);
        Assert.Null(s.MonthlyBreakdown);
        Assert.Equal(2026, s.Period.Year);
        Assert.Equal(4, s.Period.Month);
    }

    [Fact]
    public async Task SingleIncome_ProducesIncomeAndPositiveBalance()
    {
        using var db = _dbFactory.Create();
        AddTxn(db, "income", "Salary", 3000m, new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc));
        await db.SaveChangesAsync();

        var svc = new PeriodSummaryService(db);
        var s = await svc.GetMonthlyAsync(Uid, 2026, 4, CancellationToken.None);

        Assert.Equal(3000m, s.Income);
        Assert.Equal(0m, s.Expenses);
        Assert.Equal(3000m, s.NetFlow);
        Assert.Equal(3000m, s.RunningBalance);
        Assert.Empty(s.CategoryBreakdown);
    }

    [Fact]
    public async Task MixedTransactions_ComputeAllNumbersCorrectly()
    {
        using var db = _dbFactory.Create();
        AddTxn(db, "income", "Salary", 3000m, new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc));
        AddTxn(db, "expense", "Food & Dining", 450m, new DateTime(2026, 4, 5, 0, 0, 0, DateTimeKind.Utc));
        AddTxn(db, "expense", "Housing", 1200m, new DateTime(2026, 4, 10, 0, 0, 0, DateTimeKind.Utc));
        AddTxn(db, "expense", "Savings", 500m, new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc));
        await db.SaveChangesAsync();

        var svc = new PeriodSummaryService(db);
        var s = await svc.GetMonthlyAsync(Uid, 2026, 4, CancellationToken.None);

        Assert.Equal(3000m, s.Income);
        Assert.Equal(2150m, s.Expenses);  // 450 + 1200 + 500
        Assert.Equal(500m, s.Savings);
        Assert.Equal(850m, s.NetFlow);    // 3000 - 2150
        Assert.Equal(850m, s.RunningBalance);
        Assert.Equal(3, s.CategoryBreakdown.Count);
        Assert.Equal("Housing", s.CategoryBreakdown[0].Category);  // largest first
    }

    [Fact]
    public async Task RunningBalance_AccumulatesAcrossMonths()
    {
        using var db = _dbFactory.Create();
        // March: +500
        AddTxn(db, "income", "Salary", 1000m, new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc));
        AddTxn(db, "expense", "Food & Dining", 500m, new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc));
        // April: +200
        AddTxn(db, "income", "Salary", 1000m, new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc));
        AddTxn(db, "expense", "Food & Dining", 800m, new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc));
        await db.SaveChangesAsync();

        var svc = new PeriodSummaryService(db);
        var march = await svc.GetMonthlyAsync(Uid, 2026, 3, CancellationToken.None);
        var april = await svc.GetMonthlyAsync(Uid, 2026, 4, CancellationToken.None);

        Assert.Equal(500m, march.RunningBalance);
        Assert.Equal(200m, april.NetFlow);
        Assert.Equal(700m, april.RunningBalance);  // 500 (March cum) + 200 (April net)
    }

    [Fact]
    public async Task CategoryPercentages_SumToOneHundred()
    {
        using var db = _dbFactory.Create();
        AddTxn(db, "expense", "Food & Dining", 300m, new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc));
        AddTxn(db, "expense", "Housing", 600m, new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc));
        AddTxn(db, "expense", "Transportation", 100m, new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc));
        await db.SaveChangesAsync();

        var svc = new PeriodSummaryService(db);
        var s = await svc.GetMonthlyAsync(Uid, 2026, 4, CancellationToken.None);

        var totalPct = s.CategoryBreakdown.Sum(c => c.Percentage);
        Assert.InRange(totalPct, 99.5m, 100.5m);  // tolerance for rounding
    }

    [Fact]
    public async Task FuturePeriod_ReturnsZerosWithoutError()
    {
        using var db = _dbFactory.Create();
        AddTxn(db, "income", "Salary", 1000m, new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc));
        await db.SaveChangesAsync();

        var svc = new PeriodSummaryService(db);
        var future = await svc.GetMonthlyAsync(Uid, 2099, 1, CancellationToken.None);

        Assert.Equal(0m, future.Income);
        Assert.Equal(0m, future.Expenses);
        Assert.Equal(1000m, future.RunningBalance);  // running balance includes everything up to end of Jan 2099
    }

    [Fact]
    public async Task TransactionAtPeriodEnd_IncludedNotExcluded()
    {
        using var db = _dbFactory.Create();
        // Late-on-the-last-day transaction
        AddTxn(db, "income", "Salary", 100m, new DateTime(2026, 4, 30, 23, 59, 0, DateTimeKind.Utc));
        await db.SaveChangesAsync();

        var svc = new PeriodSummaryService(db);
        var april = await svc.GetMonthlyAsync(Uid, 2026, 4, CancellationToken.None);

        Assert.Equal(100m, april.Income);  // boundary correctness — uses < periodEnd not <=
    }

    [Fact]
    public async Task YearlySummary_AggregatesTwelveMonths()
    {
        using var db = _dbFactory.Create();
        // 1 income per month, $100 each
        for (int m = 1; m <= 12; m++)
        {
            AddTxn(db, "income", "Salary", 100m, new DateTime(2026, m, 15, 0, 0, 0, DateTimeKind.Utc));
        }
        // Expenses: $50 in March, $200 in October
        AddTxn(db, "expense", "Food & Dining", 50m, new DateTime(2026, 3, 20, 0, 0, 0, DateTimeKind.Utc));
        AddTxn(db, "expense", "Housing", 200m, new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc));
        await db.SaveChangesAsync();

        var svc = new PeriodSummaryService(db);
        var year = await svc.GetYearlyAsync(Uid, 2026, CancellationToken.None);

        Assert.Equal(1200m, year.Income);
        Assert.Equal(250m, year.Expenses);
        Assert.Equal(950m, year.NetFlow);
        Assert.Equal(950m, year.RunningBalance);
        Assert.Null(year.Period.Month);
        Assert.NotNull(year.MonthlyBreakdown);
        Assert.Equal(12, year.MonthlyBreakdown!.Count);
        Assert.Equal(50m, year.MonthlyBreakdown.Single(e => e.Month == 3).Expenses);
        Assert.Equal(200m, year.MonthlyBreakdown.Single(e => e.Month == 10).Expenses);
        Assert.Equal(0m, year.MonthlyBreakdown.Single(e => e.Month == 1).Expenses);  // unfilled months
    }
}
