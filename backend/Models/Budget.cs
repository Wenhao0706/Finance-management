namespace FinanceManagement.API.Models;

// Per-user-per-month budget config. ExpectedIncome null = derive from
// previous month's actual income. Percentages must sum to 1.0 (validated
// in BudgetService). One row per (UserId, Year, Month).
public class Budget
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = string.Empty;
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal? ExpectedIncome { get; set; }
    public decimal NeedsPct { get; set; } = 0.50m;
    public decimal WantsPct { get; set; } = 0.30m;
    public decimal SavingsPct { get; set; } = 0.20m;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
