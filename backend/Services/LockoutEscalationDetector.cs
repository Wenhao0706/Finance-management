using FinanceManagement.API.Data;
using FinanceManagement.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceManagement.API.Services;

public sealed class LockoutEscalationDetector : ILockoutEscalationDetector
{
    private static readonly TimeSpan EmailWindow    = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan IpWideWindow   = TimeSpan.FromHours(1);
    private static readonly TimeSpan IpBlockDuration = TimeSpan.FromHours(24);
    private const int EmailAlertThreshold = 10;
    private const int IpBlockThreshold    = 20;

    private readonly AppDbContext _db;
    private readonly IBackgroundTaskQueue _queue;
    private readonly ILogger<LockoutEscalationDetector> _logger;

    public LockoutEscalationDetector(AppDbContext db, IBackgroundTaskQueue queue, ILogger<LockoutEscalationDetector> logger)
    {
        _db = db;
        _queue = queue;
        _logger = logger;
    }

    public async Task OnFailureRecordedAsync(string email, string ipAddress, string? userAgent, CancellationToken ct)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var now = DateTime.UtcNow;
        var emailSince = now - EmailWindow;

        var failuresForPair = await _db.LoginAttempts.CountAsync(
            a => a.Email == normalized
                 && a.IpAddress == ipAddress
                 && !a.Success
                 && a.AttemptedAt >= emailSince, ct);

        if (failuresForPair == EmailAlertThreshold)
        {
            var enqueued = _queue.TryEnqueue(async (sp, qct) =>
            {
                var dispatcher = sp.GetRequiredService<INotificationDispatcher>();
                await dispatcher.DispatchLockoutAlertAsync(normalized, ipAddress, userAgent, qct);
            });

            if (!enqueued)
            {
                _logger.LogWarning("Background queue full; dropping lockout alert dispatch");
            }
        }

        var ipSince = now - IpWideWindow;
        var failuresForIp = await _db.LoginAttempts.CountAsync(
            a => a.IpAddress == ipAddress
                 && !a.Success
                 && a.AttemptedAt >= ipSince, ct);

        if (failuresForIp >= IpBlockThreshold)
        {
            var existing = await _db.BlockedIps.FirstOrDefaultAsync(
                b => b.IpAddress == ipAddress && b.ExpiresAt > now, ct);

            if (existing is null)
            {
                _db.BlockedIps.Add(new BlockedIp
                {
                    IpAddress = ipAddress,
                    Reason = "ProbingMultipleEmails",
                    CreatedAt = now,
                    ExpiresAt = now + IpBlockDuration,
                });
                await _db.SaveChangesAsync(ct);
                _logger.LogWarning("Soft-blocked IP {Ip} until {Until} (probing multiple emails)", ipAddress, now + IpBlockDuration);
            }
        }
    }
}
