namespace FinanceManagement.API.Models;

// Per-user override of a Category's Classification + monthly cap.
// Both fields nullable: Classification null = inherit Category default;
// MonthlyCap null = uncapped.
public class CategoryBudget
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = string.Empty;
    public int CategoryId { get; set; }
    public string? Classification { get; set; }
    public decimal? MonthlyCap { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
