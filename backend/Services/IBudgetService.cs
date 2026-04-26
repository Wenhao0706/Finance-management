namespace FinanceManagement.API.Services;

public interface IBudgetService
{
    Task<BudgetSnapshot> GetSnapshotAsync(string userId, int year, int month, CancellationToken ct);
    Task<BudgetSnapshot> UpdateBudgetAsync(string userId, int year, int month, BudgetUpdate update, CancellationToken ct);
    Task<IReadOnlyList<CategoryBudgetEntry>> GetCategoryBudgetsAsync(string userId, CancellationToken ct);
    Task<CategoryBudgetEntry> UpdateCategoryBudgetAsync(string userId, int categoryId, CategoryBudgetUpdate update, CancellationToken ct);
}

public sealed record BudgetSnapshot(
    PeriodInfo Period,
    decimal ExpectedIncome,
    bool ExpectedIncomeIsExplicit,
    BudgetPercentages Percentages,
    BucketsUsage Buckets,
    IReadOnlyList<CategoryCapUsage> CategoryCaps);

public sealed record BudgetPercentages(decimal Needs, decimal Wants, decimal Savings);

public sealed record BucketsUsage(BucketUsage Needs, BucketUsage Wants, BucketUsage Savings);

public sealed record BucketUsage(
    decimal CapBase,
    decimal CarryIn,
    decimal CapEffective,
    decimal Spent,
    decimal PctUsed,
    string Status);

public sealed record CategoryCapUsage(
    int CategoryId,
    string Name,
    string? Classification,
    decimal MonthlyCap,
    decimal Spent,
    decimal PctUsed,
    string Status);

public sealed record BudgetUpdate(
    decimal? ExpectedIncome,
    BudgetPercentages? Percentages);

public sealed record CategoryBudgetEntry(
    int CategoryId,
    string Name,
    string? DefaultClassification,
    string? Classification,
    decimal? MonthlyCap,
    bool HasOverride);

public sealed record CategoryBudgetUpdate(
    string? Classification,
    decimal? MonthlyCap);
