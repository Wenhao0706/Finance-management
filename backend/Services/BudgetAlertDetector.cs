using FinanceManagement.API.Data;
using FinanceManagement.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceManagement.API.Services;

public sealed class BudgetAlertDetector : IBudgetAlertDetector
{
    private const string KindBucket = "BudgetBucket100";
    private const string KindCategory = "BudgetCategory100";

    private readonly AppDbContext _db;
    private readonly IBudgetService _budgetSvc;
    private readonly IBackgroundTaskQueue _queue;
    private readonly ILogger<BudgetAlertDetector> _logger;

    public BudgetAlertDetector(AppDbContext db, IBudgetService budgetSvc, IBackgroundTaskQueue queue, ILogger<BudgetAlertDetector> logger)
    {
        _db = db;
        _budgetSvc = budgetSvc;
        _queue = queue;
        _logger = logger;
    }

    public async Task OnTransactionChangedAsync(string userId, int year, int month, CancellationToken ct)
    {
        var snapshot = await _budgetSvc.GetSnapshotAsync(userId, year, month, ct);

        // Buckets
        await CheckBucketAsync(userId, year, month, "Need",   snapshot.Buckets.Needs, ct);
        await CheckBucketAsync(userId, year, month, "Want",   snapshot.Buckets.Wants, ct);
        await CheckBucketAsync(userId, year, month, "Savings", snapshot.Buckets.Savings, ct);

        // Categories with caps
        foreach (var cat in snapshot.CategoryCaps)
        {
            if (cat.Status != "over") continue;
            await CheckCategoryAsync(userId, year, month, cat, ct);
        }
    }

    private async Task CheckBucketAsync(string userId, int year, int month, string bucket, BucketUsage usage, CancellationToken ct)
    {
        if (usage.Status != "over") return;

        var key = $"{year:D4}-{month:D2}:{bucket}";
        var monthStart = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var alreadySent = await _db.EmailNotifications.AnyAsync(
            n => n.Kind == KindBucket && n.Key == key && n.SentAt >= monthStart, ct);
        if (alreadySent)
        {
            _logger.LogInformation("BudgetBucket100 already sent for {Key}", key);
            return;
        }

        var enq = _queue.TryEnqueue(async (sp, qct) =>
        {
            var lookup = sp.GetRequiredService<IFirebaseUserLookup>();
            var sender = sp.GetRequiredService<IEmailSender>();
            var db = sp.GetRequiredService<AppDbContext>();
            var user = await lookup.LookupAsync(userId, qct);
            if (user is null) return;  // can't email a user we can't resolve

            var (subject, html) = EmailTemplates.BudgetBucketOverspendAlert(
                user.DisplayName, bucket, usage.CapEffective, usage.Spent, year, month);

            var recipient = user.Email ?? userId;
            var ok = await sender.SendAsync(recipient, subject, html, qct);
            if (!ok) return;

            db.EmailNotifications.Add(new EmailNotification
            {
                Kind = KindBucket,
                Recipient = recipient,
                Key = key,
                SentAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(qct);
        });

        if (!enq) _logger.LogWarning("Queue full; dropping budget alert dispatch");
    }

    private async Task CheckCategoryAsync(string userId, int year, int month, CategoryCapUsage cat, CancellationToken ct)
    {
        var key = $"{year:D4}-{month:D2}:cat-{cat.CategoryId}";
        var monthStart = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var alreadySent = await _db.EmailNotifications.AnyAsync(
            n => n.Kind == KindCategory && n.Key == key && n.SentAt >= monthStart, ct);
        if (alreadySent) return;

        var enq = _queue.TryEnqueue(async (sp, qct) =>
        {
            var lookup = sp.GetRequiredService<IFirebaseUserLookup>();
            var sender = sp.GetRequiredService<IEmailSender>();
            var db = sp.GetRequiredService<AppDbContext>();
            var user = await lookup.LookupAsync(userId, qct);
            if (user is null) return;

            var (subject, html) = EmailTemplates.BudgetCategoryOverspendAlert(
                user.DisplayName, cat.Name, cat.MonthlyCap, cat.Spent, year, month);

            var recipient = user.Email ?? userId;
            var ok = await sender.SendAsync(recipient, subject, html, qct);
            if (!ok) return;

            db.EmailNotifications.Add(new EmailNotification
            {
                Kind = KindCategory,
                Recipient = recipient,
                Key = key,
                SentAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(qct);
        });

        if (!enq) _logger.LogWarning("Queue full; dropping budget alert dispatch");
    }
}
