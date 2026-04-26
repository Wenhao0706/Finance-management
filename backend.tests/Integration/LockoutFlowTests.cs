using System.Net.Http.Json;

namespace FinanceManagement.API.Tests.Integration;

public class LockoutFlowTests
{
    [Fact]
    public async Task Ten_Failed_Attempts_For_Registered_User_Sends_UserAlert()
    {
        await using var factory = new TestWebAppFactory();
        factory.Lookup.RegisteredEmails.Add("alex@example.com");
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "1.2.3.4");

        for (int i = 0; i < 10; i++)
        {
            var resp = await client.PostAsJsonAsync("/api/auth-events", new
            {
                email = "alex@example.com",
                success = false,
                errorCode = "auth/wrong-password",
            });
            resp.EnsureSuccessStatusCode();
        }

        // Background queue is async — give it time to drain.
        await WaitForEmailAsync(factory, expectedCount: 1);

        Assert.Single(factory.Sender.Sent);
        Assert.Equal("alex@example.com", factory.Sender.Sent[0].To);
    }

    [Fact]
    public async Task Ten_Failed_Attempts_For_Unregistered_User_Sends_AdminAlert()
    {
        await using var factory = new TestWebAppFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "5.6.7.8");

        for (int i = 0; i < 10; i++)
        {
            await client.PostAsJsonAsync("/api/auth-events", new
            {
                email = "ghost@example.com",
                success = false,
                errorCode = "auth/wrong-password",
            });
        }

        await WaitForEmailAsync(factory, expectedCount: 1);

        Assert.Single(factory.Sender.Sent);
        Assert.Contains("Probe attempt", factory.Sender.Sent[0].Subject);
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
