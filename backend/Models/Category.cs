namespace FinanceManagement.API.Models;

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "expense"; // "income" or "expense"
    public string Icon { get; set; } = "default";
    public string? Classification { get; set; }   // "Need" | "Want" | "Savings" | null (income)
}
