namespace FinanceManagement.API.Services;

public interface IPeriodSummaryService
{
    Task<PeriodSummary> GetMonthlyAsync(string userId, int year, int month, CancellationToken ct);
    Task<PeriodSummary> GetYearlyAsync(string userId, int year, CancellationToken ct);
}

public sealed record PeriodSummary(
    PeriodInfo Period,
    decimal Income,
    decimal Expenses,
    decimal Savings,
    decimal NetFlow,
    decimal RunningBalance,
    IReadOnlyList<CategoryAmount> CategoryBreakdown,
    IReadOnlyList<MonthlyEntry>? MonthlyBreakdown,
    BudgetSnapshot? Budget);   // NEW — null for yearly view, populated for monthly

public sealed record PeriodInfo(int Year, int? Month);

public sealed record CategoryAmount(string Category, decimal Amount, decimal Percentage);

public sealed record MonthlyEntry(int Month, decimal Income, decimal Expenses, decimal Savings, decimal NetFlow);
