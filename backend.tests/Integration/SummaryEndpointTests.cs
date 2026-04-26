using System.Net;
using System.Net.Http.Json;
using FinanceManagement.API.Data;
using FinanceManagement.API.Models;
using FinanceManagement.API.Services;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceManagement.API.Tests.Integration;

public class SummaryEndpointTests
{
    private static void SeedTransactions(TestWebAppFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var months = new[]
        {
            new DateTime(2025, 11, 15, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 12, 15, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 2, 15, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc),
        };

        foreach (var date in months)
        {
            db.Transactions.Add(new Transaction
            {
                UserId = factory.TestUserId,
                Type = "income",
                Category = "Salary",
                Amount = 3000m,
                Date = date,
                Description = "Monthly salary",
            });
            db.Transactions.Add(new Transaction
            {
                UserId = factory.TestUserId,
                Type = "expense",
                Category = "Housing",
                Amount = 1200m,
                Date = date.AddDays(1),
                Description = "Rent",
            });
            db.Transactions.Add(new Transaction
            {
                UserId = factory.TestUserId,
                Type = "expense",
                Category = "Savings",
                Amount = 500m,
                Date = date.AddDays(2),
                Description = "To savings account",
            });
        }
        db.SaveChanges();
    }

    [Fact]
    public async Task GetMonthlySummary_ReturnsCorrectTotals()
    {
        await using var factory = new TestWebAppFactory();
        SeedTransactions(factory);
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/api/transactions/summary?year=2026&month=4");
        resp.EnsureSuccessStatusCode();
        var s = await resp.Content.ReadFromJsonAsync<PeriodSummary>();

        Assert.NotNull(s);
        Assert.Equal(2026, s!.Period.Year);
        Assert.Equal(4, s.Period.Month);
        Assert.Equal(3000m, s.Income);
        Assert.Equal(1700m, s.Expenses);  // 1200 + 500
        Assert.Equal(500m, s.Savings);
        Assert.Equal(1300m, s.NetFlow);
    }

    [Fact]
    public async Task GetYearlySummary_AggregatesMonthly()
    {
        await using var factory = new TestWebAppFactory();
        SeedTransactions(factory);
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/api/transactions/summary?year=2026");
        resp.EnsureSuccessStatusCode();
        var s = await resp.Content.ReadFromJsonAsync<PeriodSummary>();

        Assert.NotNull(s);
        Assert.Null(s!.Period.Month);
        Assert.Equal(12000m, s.Income);  // 4 months in 2026 × $3000
        Assert.Equal(6800m, s.Expenses);  // 4 × ($1200 + $500)
        Assert.NotNull(s.MonthlyBreakdown);
        Assert.Equal(12, s.MonthlyBreakdown!.Count);
        Assert.Equal(3000m, s.MonthlyBreakdown.Single(e => e.Month == 1).Income);
        Assert.Equal(0m, s.MonthlyBreakdown.Single(e => e.Month == 12).Income);
    }

    [Fact]
    public async Task GetSummary_NoParams_DefaultsToCurrentMonth()
    {
        await using var factory = new TestWebAppFactory();
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/api/transactions/summary");
        resp.EnsureSuccessStatusCode();
        var s = await resp.Content.ReadFromJsonAsync<PeriodSummary>();

        var now = DateTime.UtcNow;
        Assert.Equal(now.Year, s!.Period.Year);
        Assert.Equal(now.Month, s.Period.Month);
    }

    [Fact]
    public async Task InvalidMonth_Returns400()
    {
        await using var factory = new TestWebAppFactory();
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/api/transactions/summary?year=2026&month=13");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }
}
