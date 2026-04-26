using FinanceManagement.API.Data;
using FinanceManagement.API.Models;
using FinanceManagement.API.Services;
using FinanceManagement.API.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanceManagement.API.Tests;

public class LockoutEscalationDetectorTests : IDisposable
{
    private readonly InMemoryDbContextFactory _dbFactory = new();

    public void Dispose() => _dbFactory.Dispose();

    private static void AddFailures(AppDbContext db, string email, string ip, int count, TimeSpan? offsetFromNow = null)
    {
        var baseTime = DateTime.UtcNow - (offsetFromNow ?? TimeSpan.Zero);
        for (int i = 0; i < count; i++)
        {
            db.LoginAttempts.Add(new LoginAttempt
            {
                Email = email,
                IpAddress = ip,
                Success = false,
                AttemptedAt = baseTime.AddSeconds(-i),
            });
        }
        db.SaveChanges();
    }

    private (LockoutEscalationDetector detector, FakeQueue queue) BuildDetector(AppDbContext db)
    {
        var queue = new FakeQueue();
        var detector = new LockoutEscalationDetector(db, queue, NullLogger<LockoutEscalationDetector>.Instance);
        return (detector, queue);
    }

    [Fact]
    public async Task At_Exactly_10_Failures_Enqueues_Dispatch()
    {
        using var db = _dbFactory.Create();
        AddFailures(db, "a@example.com", "1.2.3.4", 10);

        var (detector, queue) = BuildDetector(db);
        await detector.OnFailureRecordedAsync("a@example.com", "1.2.3.4", "ua", CancellationToken.None);

        Assert.Single(queue.Items);
    }

    [Fact]
    public async Task At_11_Failures_Does_Not_Re_Enqueue()
    {
        using var db = _dbFactory.Create();
        AddFailures(db, "a@example.com", "1.2.3.4", 11);

        var (detector, queue) = BuildDetector(db);
        await detector.OnFailureRecordedAsync("a@example.com", "1.2.3.4", "ua", CancellationToken.None);

        Assert.Empty(queue.Items);
    }

    [Fact]
    public async Task At_9_Failures_Does_Not_Enqueue()
    {
        using var db = _dbFactory.Create();
        AddFailures(db, "a@example.com", "1.2.3.4", 9);

        var (detector, queue) = BuildDetector(db);
        await detector.OnFailureRecordedAsync("a@example.com", "1.2.3.4", "ua", CancellationToken.None);

        Assert.Empty(queue.Items);
    }

    [Fact]
    public async Task At_20_IpWide_Failures_Across_Emails_Inserts_BlockedIp()
    {
        using var db = _dbFactory.Create();
        for (int i = 0; i < 20; i++)
        {
            db.LoginAttempts.Add(new LoginAttempt
            {
                Email = $"victim{i}@example.com",
                IpAddress = "9.9.9.9",
                Success = false,
                AttemptedAt = DateTime.UtcNow.AddMinutes(-1),
            });
        }
        await db.SaveChangesAsync();

        var (detector, _) = BuildDetector(db);
        await detector.OnFailureRecordedAsync("victim20@example.com", "9.9.9.9", "ua", CancellationToken.None);

        var blocked = db.BlockedIps.SingleOrDefault();
        Assert.NotNull(blocked);
        Assert.Equal("9.9.9.9", blocked!.IpAddress);
    }

    [Fact]
    public async Task Existing_Active_Block_Is_Not_Duplicated()
    {
        using var db = _dbFactory.Create();
        db.BlockedIps.Add(new BlockedIp
        {
            IpAddress = "9.9.9.9",
            Reason = "ProbingMultipleEmails",
            ExpiresAt = DateTime.UtcNow.AddHours(12),
        });
        for (int i = 0; i < 25; i++)
        {
            db.LoginAttempts.Add(new LoginAttempt
            {
                Email = $"victim{i}@example.com",
                IpAddress = "9.9.9.9",
                Success = false,
                AttemptedAt = DateTime.UtcNow.AddMinutes(-1),
            });
        }
        await db.SaveChangesAsync();

        var (detector, _) = BuildDetector(db);
        await detector.OnFailureRecordedAsync("victim26@example.com", "9.9.9.9", "ua", CancellationToken.None);

        Assert.Single(db.BlockedIps.ToList());
    }

    private sealed class FakeQueue : IBackgroundTaskQueue
    {
        public List<Func<IServiceProvider, CancellationToken, Task>> Items { get; } = new();

        public bool TryEnqueue(Func<IServiceProvider, CancellationToken, Task> work)
        {
            Items.Add(work);
            return true;
        }

        public ValueTask<Func<IServiceProvider, CancellationToken, Task>> DequeueAsync(CancellationToken ct)
            => throw new NotSupportedException();
    }
}
