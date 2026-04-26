using System.Net.Http.Json;
using FinanceManagement.API.Data;
using FinanceManagement.API.Models;
using FinanceManagement.API.Services;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceManagement.API.Tests.Integration;

public class BudgetAlertIntegrationTests
{
    [Fact]
    public async Task TransactionThatCrossesBucket100_EnqueuesEmail()
    {
        await using var factory = new TestWebAppFactory();
        factory.Lookup.EmailByUid[factory.TestUserId] = "alex@example.com";
        // Set $1000 budget so Needs cap = $500
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Budgets.Add(new Budget { UserId = factory.TestUserId, Year = 2026, Month = 4, ExpectedIncome = 1000m });
            db.SaveChanges();
        }
        var client = factory.CreateClient();

        // Add a $600 Housing expense → Needs bucket 600/500 → over 100%
        var resp = await client.PostAsJsonAsync("/api/transactions", new
        {
            description = "Rent",
            amount = 600m,
            type = "expense",
            category = "Housing",
            date = "2026-04-05T00:00:00Z",
        });
        resp.EnsureSuccessStatusCode();

        await WaitForEmailAsync(factory, expectedCount: 1);

        Assert.Single(factory.Sender.Sent);
        Assert.Contains("over budget on Need", factory.Sender.Sent[0].Subject);
    }

    [Fact]
    public async Task TransactionThatDoesNotCross_NoEmail()
    {
        await using var factory = new TestWebAppFactory();
        factory.Lookup.EmailByUid[factory.TestUserId] = "alex@example.com";
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Budgets.Add(new Budget { UserId = factory.TestUserId, Year = 2026, Month = 4, ExpectedIncome = 1000m });
            db.SaveChanges();
        }
        var client = factory.CreateClient();

        var resp = await client.PostAsJsonAsync("/api/transactions", new
        {
            description = "Groceries",
            amount = 100m,
            type = "expense",
            category = "Food & Dining",
            date = "2026-04-05T00:00:00Z",
        });
        resp.EnsureSuccessStatusCode();

        await Task.Delay(300);  // give queue a moment
        Assert.Empty(factory.Sender.Sent);
    }

    [Fact]
    public async Task SecondTransactionAfterCross_ThrottledNoSecondEmail()
    {
        await using var factory = new TestWebAppFactory();
        factory.Lookup.EmailByUid[factory.TestUserId] = "alex@example.com";
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Budgets.Add(new Budget { UserId = factory.TestUserId, Year = 2026, Month = 4, ExpectedIncome = 1000m });
            db.SaveChanges();
        }
        var client = factory.CreateClient();

        // First crossing
        await client.PostAsJsonAsync("/api/transactions", new
        {
            description = "Rent", amount = 600m, type = "expense", category = "Housing",
            date = "2026-04-05T00:00:00Z",
        });
        await WaitForEmailAsync(factory, expectedCount: 1);
        // Wait for the throttle row to be persisted before posting the second
        // transaction — otherwise the second detector check can race ahead and
        // enqueue a duplicate before the first task's SaveChanges completes.
        await WaitForNotificationRowAsync(factory, expectedCount: 1);

        // Second crossing — should NOT trigger another email
        await client.PostAsJsonAsync("/api/transactions", new
        {
            description = "Plumber", amount = 100m, type = "expense", category = "Housing",
            date = "2026-04-10T00:00:00Z",
        });
        await Task.Delay(500);

        Assert.Single(factory.Sender.Sent);
    }

    private static async Task WaitForNotificationRowAsync(TestWebAppFactory factory, int expectedCount, int timeoutMs = 3000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (db.EmailNotifications.Count() >= expectedCount) return;
            await Task.Delay(50);
        }
    }

    private static async Task WaitForEmailAsync(TestWebAppFactory factory, int expectedCount, int timeoutMs = 3000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (factory.Sender.Sent.Count >= expectedCount) return;
            await Task.Delay(50);
        }
    }
}
