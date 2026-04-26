using FinanceManagement.API.Data;
using FinanceManagement.API.Models;
using Microsoft.EntityFrameworkCore;

namespace FinanceManagement.API.Services;

public sealed class BudgetService : IBudgetService
{
    private const decimal DefaultNeedsPct = 0.50m;
    private const decimal DefaultWantsPct = 0.30m;
    private const decimal DefaultSavingsPct = 0.20m;
    private const string SavingsCategoryName = "Savings";

    private readonly AppDbContext _db;

    public BudgetService(AppDbContext db) => _db = db;

    public async Task<BudgetSnapshot> GetSnapshotAsync(string userId, int year, int month, CancellationToken ct)
    {
        var (periodStart, periodEnd) = MonthRange(year, month);
        var prevStart = periodStart.AddMonths(-1);

        var budget = await _db.Budgets
            .FirstOrDefaultAsync(b => b.UserId == userId && b.Year == year && b.Month == month, ct);

        var (expectedIncome, isExplicit) = await ResolveExpectedIncomeAsync(userId, budget, prevStart, periodStart, ct);

        var pcts = budget is null
            ? new BudgetPercentages(DefaultNeedsPct, DefaultWantsPct, DefaultSavingsPct)
            : new BudgetPercentages(budget.NeedsPct, budget.WantsPct, budget.SavingsPct);

        var thisMonthSpent = await ComputeBucketSpentAsync(userId, periodStart, periodEnd, ct);
        var prevCarryIn = await ComputeCarryInAsync(userId, year, month, ct);

        var needs   = BuildBucketUsage(expectedIncome, pcts.Needs,   prevCarryIn.Needs,   thisMonthSpent.Needs);
        var wants   = BuildBucketUsage(expectedIncome, pcts.Wants,   prevCarryIn.Wants,   thisMonthSpent.Wants);
        var savings = BuildBucketUsage(expectedIncome, pcts.Savings, prevCarryIn.Savings, thisMonthSpent.Savings);

        var categoryCaps = await ComputeCategoryCapsAsync(userId, periodStart, periodEnd, ct);

        return new BudgetSnapshot(
            new PeriodInfo(year, month),
            expectedIncome,
            isExplicit,
            pcts,
            new BucketsUsage(needs, wants, savings),
            categoryCaps);
    }

    public async Task<BudgetSnapshot> UpdateBudgetAsync(string userId, int year, int month, BudgetUpdate update, CancellationToken ct)
    {
        if (update.ExpectedIncome is < 0)
            throw new ArgumentException("Expected income must be non-negative.", nameof(update));

        if (update.Percentages is { } p)
        {
            var sum = p.Needs + p.Wants + p.Savings;
            if (Math.Abs(sum - 1.0m) > 0.0001m)
                throw new ArgumentException("Percentages must sum to 1.0 (got " + sum + ").", nameof(update));
            if (p.Needs < 0 || p.Wants < 0 || p.Savings < 0)
                throw new ArgumentException("Percentages must be non-negative.", nameof(update));
        }

        var existing = await _db.Budgets
            .FirstOrDefaultAsync(b => b.UserId == userId && b.Year == year && b.Month == month, ct);

        if (existing is null)
        {
            existing = new Budget
            {
                UserId = userId, Year = year, Month = month,
                ExpectedIncome = update.ExpectedIncome,
                NeedsPct = update.Percentages?.Needs ?? DefaultNeedsPct,
                WantsPct = update.Percentages?.Wants ?? DefaultWantsPct,
                SavingsPct = update.Percentages?.Savings ?? DefaultSavingsPct,
            };
            _db.Budgets.Add(existing);
        }
        else
        {
            existing.ExpectedIncome = update.ExpectedIncome;
            if (update.Percentages is { } pct)
            {
                existing.NeedsPct = pct.Needs;
                existing.WantsPct = pct.Wants;
                existing.SavingsPct = pct.Savings;
            }
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        return await GetSnapshotAsync(userId, year, month, ct);
    }

    public async Task<IReadOnlyList<CategoryBudgetEntry>> GetCategoryBudgetsAsync(string userId, CancellationToken ct)
    {
        var categories = await _db.Categories
            .Where(c => c.Type == "expense")
            .OrderBy(c => c.Id)
            .ToListAsync(ct);

        var overrides = await _db.CategoryBudgets
            .Where(cb => cb.UserId == userId)
            .ToListAsync(ct);

        var result = new List<CategoryBudgetEntry>(categories.Count);
        foreach (var cat in categories)
        {
            var ov = overrides.FirstOrDefault(o => o.CategoryId == cat.Id);
            result.Add(new CategoryBudgetEntry(
                CategoryId: cat.Id,
                Name: cat.Name,
                DefaultClassification: cat.Classification,
                Classification: ov?.Classification ?? cat.Classification,
                MonthlyCap: ov?.MonthlyCap,
                HasOverride: ov is not null));
        }
        return result;
    }

    public async Task<CategoryBudgetEntry> UpdateCategoryBudgetAsync(string userId, int categoryId, CategoryBudgetUpdate update, CancellationToken ct)
    {
        var cat = await _db.Categories.FirstOrDefaultAsync(c => c.Id == categoryId, ct)
            ?? throw new ArgumentException("Category not found.", nameof(categoryId));
        if (cat.Type != "expense")
            throw new ArgumentException("Cannot set classification or cap on income category.", nameof(categoryId));

        if (update.Classification is { } cls && cls != "Need" && cls != "Want" && cls != "Savings")
            throw new ArgumentException("Classification must be Need, Want, or Savings.", nameof(update));

        if (update.MonthlyCap is < 0)
            throw new ArgumentException("Monthly cap must be non-negative.", nameof(update));

        var existing = await _db.CategoryBudgets
            .FirstOrDefaultAsync(cb => cb.UserId == userId && cb.CategoryId == categoryId, ct);

        if (existing is null)
        {
            existing = new CategoryBudget
            {
                UserId = userId, CategoryId = categoryId,
                Classification = update.Classification, MonthlyCap = update.MonthlyCap,
            };
            _db.CategoryBudgets.Add(existing);
        }
        else
        {
            existing.Classification = update.Classification;
            existing.MonthlyCap = update.MonthlyCap;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);

        return new CategoryBudgetEntry(
            CategoryId: cat.Id,
            Name: cat.Name,
            DefaultClassification: cat.Classification,
            Classification: existing.Classification ?? cat.Classification,
            MonthlyCap: existing.MonthlyCap,
            HasOverride: true);
    }

    private static (DateTime Start, DateTime End) MonthRange(int year, int month)
    {
        var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        return (start, start.AddMonths(1));
    }

    private async Task<(decimal Income, bool IsExplicit)> ResolveExpectedIncomeAsync(
        string userId, Budget? budget, DateTime prevStart, DateTime currentStart, CancellationToken ct)
    {
        if (budget?.ExpectedIncome is { } explicitIncome)
            return (explicitIncome, true);

        var derived = await _db.Transactions
            .Where(t => t.UserId == userId && t.Date >= prevStart && t.Date < currentStart && t.Type == "income")
            .SumAsync(t => (decimal?)t.Amount, ct) ?? 0m;
        return (derived, false);
    }

    private async Task<(decimal Needs, decimal Wants, decimal Savings)> ComputeBucketSpentAsync(
        string userId, DateTime periodStart, DateTime periodEnd, CancellationToken ct)
    {
        var txns = await _db.Transactions
            .Where(t => t.UserId == userId && t.Date >= periodStart && t.Date < periodEnd && t.Type == "expense")
            .Select(t => new { t.Amount, TxnClassification = t.Classification, t.Category })
            .ToListAsync(ct);

        if (txns.Count == 0) return (0m, 0m, 0m);

        var categoryDefaults = await _db.Categories
            .Where(c => c.Type == "expense")
            .Select(c => new { c.Name, c.Classification })
            .ToListAsync(ct);
        var catDefaultByName = categoryDefaults.ToDictionary(c => c.Name, c => c.Classification);

        var userOverrides = await (
            from cb in _db.CategoryBudgets
            where cb.UserId == userId && cb.Classification != null
            join c in _db.Categories on cb.CategoryId equals c.Id
            select new { c.Name, cb.Classification }
        ).ToListAsync(ct);
        var userOverrideByName = userOverrides.ToDictionary(x => x.Name, x => x.Classification);

        decimal needs = 0m, wants = 0m, savings = 0m;
        foreach (var t in txns)
        {
            var cls = t.TxnClassification;
            if (cls is null && userOverrideByName.TryGetValue(t.Category, out var userCls)) cls = userCls;
            if (cls is null && catDefaultByName.TryGetValue(t.Category, out var defCls)) cls = defCls;

            switch (cls)
            {
                case "Need": needs += t.Amount; break;
                case "Want": wants += t.Amount; break;
                case "Savings": savings += t.Amount; break;
            }
        }
        return (needs, wants, savings);
    }

    private async Task<(decimal Needs, decimal Wants, decimal Savings)> ComputeCarryInAsync(
        string userId, int year, int month, CancellationToken ct)
    {
        var prevYear = month == 1 ? year - 1 : year;
        var prevMonth = month == 1 ? 12 : month - 1;

        var (prevStart, prevEnd) = MonthRange(prevYear, prevMonth);
        var prevAny = await _db.Transactions.AnyAsync(t => t.UserId == userId && t.Date >= prevStart && t.Date < prevEnd, ct)
                   || await _db.Budgets.AnyAsync(b => b.UserId == userId && b.Year == prevYear && b.Month == prevMonth, ct);
        if (!prevAny) return (0m, 0m, 0m);

        var prevBudget = await _db.Budgets
            .FirstOrDefaultAsync(b => b.UserId == userId && b.Year == prevYear && b.Month == prevMonth, ct);
        var prevPrevStart = prevStart.AddMonths(-1);
        var (prevIncome, _) = await ResolveExpectedIncomeAsync(userId, prevBudget, prevPrevStart, prevStart, ct);

        var prevPcts = prevBudget is null
            ? new BudgetPercentages(DefaultNeedsPct, DefaultWantsPct, DefaultSavingsPct)
            : new BudgetPercentages(prevBudget.NeedsPct, prevBudget.WantsPct, prevBudget.SavingsPct);

        var prevSpent = await ComputeBucketSpentAsync(userId, prevStart, prevEnd, ct);
        var prevPrevCarry = await ComputeCarryInAsync(userId, prevYear, prevMonth, ct);

        return (
            (prevIncome * prevPcts.Needs   + prevPrevCarry.Needs)   - prevSpent.Needs,
            (prevIncome * prevPcts.Wants   + prevPrevCarry.Wants)   - prevSpent.Wants,
            (prevIncome * prevPcts.Savings + prevPrevCarry.Savings) - prevSpent.Savings);
    }

    private static BucketUsage BuildBucketUsage(decimal expectedIncome, decimal pct, decimal carryIn, decimal spent)
    {
        var capBase = expectedIncome * pct;
        var capEffective = capBase + carryIn;
        var pctUsed = capEffective <= 0 ? 0m : Math.Round(spent / capEffective * 100m, 1);
        var status = pctUsed >= 100m ? "over" : pctUsed >= 80m ? "warn" : "ok";
        return new BucketUsage(capBase, carryIn, capEffective, spent, pctUsed, status);
    }

    private async Task<IReadOnlyList<CategoryCapUsage>> ComputeCategoryCapsAsync(
        string userId, DateTime periodStart, DateTime periodEnd, CancellationToken ct)
    {
        var caps = await (
            from cb in _db.CategoryBudgets
            where cb.UserId == userId && cb.MonthlyCap != null
            join c in _db.Categories on cb.CategoryId equals c.Id
            select new { c.Id, c.Name, CategoryDefault = c.Classification, OverrideCls = cb.Classification, Cap = cb.MonthlyCap!.Value }
        ).ToListAsync(ct);

        if (caps.Count == 0) return Array.Empty<CategoryCapUsage>();

        var spentByName = await _db.Transactions
            .Where(t => t.UserId == userId && t.Date >= periodStart && t.Date < periodEnd && t.Type == "expense")
            .GroupBy(t => t.Category)
            .Select(g => new { Name = g.Key, Sum = g.Sum(t => t.Amount) })
            .ToListAsync(ct);
        var spentMap = spentByName.ToDictionary(s => s.Name, s => s.Sum);

        return caps.Select(c =>
        {
            var spent = spentMap.GetValueOrDefault(c.Name, 0m);
            var pctUsed = c.Cap == 0m ? 0m : Math.Round(spent / c.Cap * 100m, 1);
            var status = pctUsed >= 100m ? "over" : pctUsed >= 80m ? "warn" : "ok";
            return new CategoryCapUsage(
                c.Id, c.Name,
                c.OverrideCls ?? c.CategoryDefault,
                c.Cap, spent, pctUsed, status);
        }).ToList();
    }
}
