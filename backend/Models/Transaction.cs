namespace FinanceManagement.API.Models;

public class Transaction
{
    public int Id { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Type { get; set; } = "expense"; // "income" or "expense"
    public string Category { get; set; } = string.Empty;
    public DateTime Date { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
