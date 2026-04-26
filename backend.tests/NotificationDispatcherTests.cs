using FinanceManagement.API.Data;
using FinanceManagement.API.Models;
using FinanceManagement.API.Services;
using FinanceManagement.API.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanceManagement.API.Tests;

public class NotificationDispatcherTests : IDisposable
{
    private readonly InMemoryDbContextFactory _dbFactory = new();

    public void Dispose() => _dbFactory.Dispose();

    private NotificationDispatcher BuildDispatcher(
        AppDbContext db,
        RecordingEmailSender sender,
        bool userExists,
        string? displayName = null)
    {
        var lookup = new Mock<IFirebaseUserLookup>();
        lookup.Setup(l => l.LookupAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(userExists ? new FirebaseUserInfo(displayName) : null);

        return new NotificationDispatcher(
            db,
            sender,
            lookup.Object,
            new DispatcherOptions("admin@example.com", "https://app.example.com/forgot-password"),
            NullLogger<NotificationDispatcher>.Instance);
    }

    [Fact]
    public async Task RegisteredUser_Receives_UserAlert()
    {
        using var db = _dbFactory.Create();
        var sender = new RecordingEmailSender();
        var dispatcher = BuildDispatcher(db, sender, userExists: true, displayName: "Alex");

        await dispatcher.DispatchLockoutAlertAsync("alex@example.com", "1.2.3.4", "ua", CancellationToken.None);

        Assert.Single(sender.Sent);
        Assert.Equal("alex@example.com", sender.Sent[0].To);
        Assert.Contains("Reset password", sender.Sent[0].HtmlBody);
        Assert.Single(db.EmailNotifications.ToList());
        Assert.Equal("UserAlert", db.EmailNotifications.Single().Kind);
    }

    [Fact]
    public async Task UnregisteredUser_Triggers_AdminAlert()
    {
        using var db = _dbFactory.Create();
        var sender = new RecordingEmailSender();
        var dispatcher = BuildDispatcher(db, sender, userExists: false);

        await dispatcher.DispatchLockoutAlertAsync("ghost@example.com", "1.2.3.4", "ua", CancellationToken.None);

        Assert.Single(sender.Sent);
        Assert.Equal("admin@example.com", sender.Sent[0].To);
        Assert.Contains("Probe attempt", sender.Sent[0].Subject);
        Assert.Equal("AdminAlert", db.EmailNotifications.Single().Kind);
    }

    [Fact]
    public async Task UserAlert_Throttled_Within_24h()
    {
        using var db = _dbFactory.Create();
        db.EmailNotifications.Add(new EmailNotification
        {
            Kind = "UserAlert",
            Recipient = "alex@example.com",
            Key = "1.2.3.4",
            SentAt = DateTime.UtcNow.AddHours(-2),
        });
        await db.SaveChangesAsync();

        var sender = new RecordingEmailSender();
        var dispatcher = BuildDispatcher(db, sender, userExists: true);

        await dispatcher.DispatchLockoutAlertAsync("alex@example.com", "1.2.3.4", "ua", CancellationToken.None);

        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task AdminAlert_Throttled_Per_Ip_Hourly()
    {
        using var db = _dbFactory.Create();
        db.EmailNotifications.Add(new EmailNotification
        {
            Kind = "AdminAlert",
            Recipient = "admin@example.com",
            Key = "1.2.3.4",
            SentAt = DateTime.UtcNow.AddMinutes(-30),
        });
        await db.SaveChangesAsync();

        var sender = new RecordingEmailSender();
        var dispatcher = BuildDispatcher(db, sender, userExists: false);

        await dispatcher.DispatchLockoutAlertAsync("ghost@example.com", "1.2.3.4", "ua", CancellationToken.None);

        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task AdminAlert_Daily_Cap_Of_5()
    {
        using var db = _dbFactory.Create();
        for (int i = 0; i < 5; i++)
        {
            db.EmailNotifications.Add(new EmailNotification
            {
                Kind = "AdminAlert",
                Recipient = "admin@example.com",
                Key = $"9.9.9.{i}",
                SentAt = DateTime.UtcNow.AddHours(-2 - i),
            });
        }
        await db.SaveChangesAsync();

        var sender = new RecordingEmailSender();
        var dispatcher = BuildDispatcher(db, sender, userExists: false);

        await dispatcher.DispatchLockoutAlertAsync("ghost@example.com", "5.5.5.5", "ua", CancellationToken.None);

        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task FirebaseLookupFailure_Falls_Back_To_AdminAlert()
    {
        using var db = _dbFactory.Create();
        var lookup = new Mock<IFirebaseUserLookup>();
        lookup.Setup(l => l.LookupAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new InvalidOperationException("boom"));

        var sender = new RecordingEmailSender();
        var dispatcher = new NotificationDispatcher(
            db, sender, lookup.Object,
            new DispatcherOptions("admin@example.com", "https://app/forgot"),
            NullLogger<NotificationDispatcher>.Instance);

        await dispatcher.DispatchLockoutAlertAsync("uncertain@example.com", "1.2.3.4", "ua", CancellationToken.None);

        Assert.Single(sender.Sent);
        Assert.Equal("admin@example.com", sender.Sent[0].To);
    }
}
