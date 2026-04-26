using System.Net;
using System.Net.Http.Json;
using FinanceManagement.API.Services;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceManagement.API.Tests.Integration;

public class BudgetEndpointTests
{
    [Fact]
    public async Task PutBudget_ThenGetBudget_ReturnsSameValues()
    {
        await using var factory = new TestWebAppFactory();
        var client = factory.CreateClient();

        var put = await client.PutAsJsonAsync("/api/budgets/2026/4", new
        {
            expectedIncome = 4000m,
            percentages = new { needs = 0.6m, wants = 0.3m, savings = 0.1m },
        });
        put.EnsureSuccessStatusCode();

        var get = await client.GetAsync("/api/budgets/2026/4");
        get.EnsureSuccessStatusCode();
        var s = await get.Content.ReadFromJsonAsync<BudgetSnapshot>();

        Assert.NotNull(s);
        Assert.Equal(4000m, s!.ExpectedIncome);
        Assert.True(s.ExpectedIncomeIsExplicit);
        Assert.Equal(0.6m, s.Percentages.Needs);
        Assert.Equal(2400m, s.Buckets.Needs.CapBase); // 4000 × 0.6
    }

    [Fact]
    public async Task GetBudget_NoExplicitRow_DerivesFromPreviousMonth()
    {
        await using var factory = new TestWebAppFactory();
        // Seed prev month income directly via DbContext
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinanceManagement.API.Data.AppDbContext>();
            db.Transactions.Add(new FinanceManagement.API.Models.Transaction
            {
                UserId = factory.TestUserId, Type = "income", Category = "Salary",
                Amount = 2500m, Date = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                Description = "Mar salary",
            });
            db.SaveChanges();
        }

        var client = factory.CreateClient();
        var resp = await client.GetAsync("/api/budgets/2026/4");
        resp.EnsureSuccessStatusCode();
        var s = await resp.Content.ReadFromJsonAsync<BudgetSnapshot>();

        Assert.Equal(2500m, s!.ExpectedIncome);
        Assert.False(s.ExpectedIncomeIsExplicit);
    }

    [Fact]
    public async Task PutBudget_BadPercentages_Returns400()
    {
        await using var factory = new TestWebAppFactory();
        var client = factory.CreateClient();

        var resp = await client.PutAsJsonAsync("/api/budgets/2026/4", new
        {
            expectedIncome = (decimal?)null,
            percentages = new { needs = 0.5m, wants = 0.3m, savings = 0.3m },  // sums to 1.1
        });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task PutBudget_BadMonth_Returns400()
    {
        await using var factory = new TestWebAppFactory();
        var client = factory.CreateClient();

        var resp = await client.PutAsJsonAsync("/api/budgets/2026/13", new { expectedIncome = 1000m, percentages = (object?)null });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task PutCategoryBudget_SetsCapAndClassification()
    {
        await using var factory = new TestWebAppFactory();
        var client = factory.CreateClient();

        var resp = await client.PutAsJsonAsync("/api/category-budgets/4", new
        {
            classification = "Want",
            monthlyCap = 500m,
        });
        resp.EnsureSuccessStatusCode();

        var list = await client.GetAsync("/api/category-budgets");
        list.EnsureSuccessStatusCode();
        var entries = await list.Content.ReadFromJsonAsync<List<CategoryBudgetEntry>>();
        var foodEntry = entries!.Single(e => e.CategoryId == 4);

        Assert.True(foodEntry.HasOverride);
        Assert.Equal("Want", foodEntry.Classification);
        Assert.Equal(500m, foodEntry.MonthlyCap);
        Assert.Equal("Need", foodEntry.DefaultClassification);
    }
}
