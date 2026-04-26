using FinanceManagement.API.Data;
using Microsoft.EntityFrameworkCore;

namespace FinanceManagement.API.Services;

public sealed class PeriodSummaryService : IPeriodSummaryService
{
    private const string SavingsCategoryName = "Savings";

    private readonly AppDbContext _db;

    public PeriodSummaryService(AppDbContext db) => _db = db;

    public async Task<PeriodSummary> GetMonthlyAsync(string userId, int year, int month, CancellationToken ct)
    {
        var periodStart = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd   = periodStart.AddMonths(1);

        var (income, expenses, savings, breakdown) = await ComputeWindowAsync(userId, periodStart, periodEnd, ct);
        var balance = await ComputeRunningBalanceAsync(userId, periodEnd, ct);

        return new PeriodSummary(
            new PeriodInfo(year, month),
            income, expenses, savings,
            NetFlow: income - expenses,
            RunningBalance: balance,
            CategoryBreakdown: breakdown,
            MonthlyBreakdown: null);
    }

    public async Task<PeriodSummary> GetYearlyAsync(string userId, int year, CancellationToken ct)
    {
        var periodStart = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd   = periodStart.AddYears(1);

        var (income, expenses, savings, breakdown) = await ComputeWindowAsync(userId, periodStart, periodEnd, ct);
        var balance = await ComputeRunningBalanceAsync(userId, periodEnd, ct);
        var monthly = await ComputeMonthlyBreakdownAsync(userId, periodStart, periodEnd, ct);

        return new PeriodSummary(
            new PeriodInfo(year, null),
            income, expenses, savings,
            NetFlow: income - expenses,
            RunningBalance: balance,
            CategoryBreakdown: breakdown,
            MonthlyBreakdown: monthly);
    }

    private async Task<(decimal income, decimal expenses, decimal savings, IReadOnlyList<CategoryAmount> breakdown)>
        ComputeWindowAsync(string userId, DateTime start, DateTime end, CancellationToken ct)
    {
        var income = await _db.Transactions
            .Where(t => t.UserId == userId && t.Date >= start && t.Date < end && t.Type == "income")
            .SumAsync(t => (decimal?)t.Amount, ct) ?? 0m;

        var expenseRows = await _db.Transactions
            .Where(t => t.UserId == userId && t.Date >= start && t.Date < end && t.Type == "expense")
            .GroupBy(t => t.Category)
            .Select(g => new { Category = g.Key, Amount = g.Sum(t => t.Amount) })
            .ToListAsync(ct);

        var totalExpenses = expenseRows.Sum(r => r.Amount);
        var savings = expenseRows.FirstOrDefault(r => r.Category == SavingsCategoryName)?.Amount ?? 0m;

        var breakdown = expenseRows
            .OrderByDescending(r => r.Amount)
            .Select(r => new CategoryAmount(
                r.Category,
                r.Amount,
                totalExpenses == 0m ? 0m : Math.Round(r.Amount / totalExpenses * 100m, 1)))
            .ToList();

        return (income, totalExpenses, savings, breakdown);
    }

    private async Task<decimal> ComputeRunningBalanceAsync(string userId, DateTime periodEnd, CancellationToken ct)
    {
        // Single SUM with conditional sign — EF Core translates the ternary
        // into a CASE expression. Faster than two round-trips.
        return await _db.Transactions
            .Where(t => t.UserId == userId && t.Date < periodEnd)
            .SumAsync(t => (decimal?)(t.Type == "income" ? t.Amount : -t.Amount), ct) ?? 0m;
    }

    private async Task<IReadOnlyList<MonthlyEntry>> ComputeMonthlyBreakdownAsync(
        string userId, DateTime yearStart, DateTime yearEnd, CancellationToken ct)
    {
        // Pull rows once, pivot in memory. Avoids 12 round-trips.
        var rows = await _db.Transactions
            .Where(t => t.UserId == userId && t.Date >= yearStart && t.Date < yearEnd)
            .Select(t => new { t.Date.Month, t.Type, t.Category, t.Amount })
            .ToListAsync(ct);

        var entries = new List<MonthlyEntry>(12);
        for (int m = 1; m <= 12; m++)
        {
            var monthRows = rows.Where(r => r.Month == m).ToList();
            var income = monthRows.Where(r => r.Type == "income").Sum(r => r.Amount);
            var expenses = monthRows.Where(r => r.Type == "expense").Sum(r => r.Amount);
            var savings = monthRows.Where(r => r.Type == "expense" && r.Category == SavingsCategoryName).Sum(r => r.Amount);
            entries.Add(new MonthlyEntry(m, income, expenses, savings, income - expenses));
        }
        return entries;
    }
}
