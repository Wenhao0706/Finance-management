using FinanceManagement.API.Data;
using FinanceManagement.API.Models;
using Microsoft.EntityFrameworkCore;

namespace FinanceManagement.API.Services;

public record LockoutDecision(bool IsLocked, int RetryAfterSeconds, int FailedAttemptsInWindow);

public interface ILockoutService
{
    Task<LockoutDecision> CheckAsync(string email, string ipAddress, CancellationToken cancellationToken = default);
    Task RecordAsync(string email, string ipAddress, bool success, string? userAgent, string? firebaseErrorCode, CancellationToken cancellationToken = default);
}

// Progressive lockout layered on top of Firebase Auth's built-in throttling.
//
// Why we need our own:
//   1. Firebase's auth/too-many-requests message is opaque — we can show a
//      precise "wait N seconds" countdown to the user.
//   2. Audit trail of every login attempt (who, when, from where, why it failed).
//   3. Future hook for email notifications + step-up auth (Phase 4).
//
// Lockout policy (counted within a 15-min sliding window):
//   3 failures  → 30 sec delay
//   5 failures  → 2  min delay
//  10 failures  → 15 min delay
//
// Keying — partition by (email, ipAddress) pair, NEVER email alone. A bad
// actor at a different IP can't lock out a victim's account this way; they
// can only lock themselves out for that one email. Firebase's own
// auth/too-many-requests handles the across-IP attack, so this is purely
// to give the legitimate user a clearer, locally-tracked UX.
//
// A successful login resets the counter — only failures *after* the most
// recent success are counted toward the next lockout decision.
public class LockoutService : ILockoutService
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private readonly AppDbContext _db;
    private readonly ILockoutEscalationDetector? _escalator;
    private readonly ILogger<LockoutService>? _logger;

    public LockoutService(
        AppDbContext db,
        ILockoutEscalationDetector? escalator = null,
        ILogger<LockoutService>? logger = null)
    {
        _db = db;
        _escalator = escalator;
        _logger = logger;
    }

    public async Task<LockoutDecision> CheckAsync(string email, string ipAddress, CancellationToken cancellationToken = default)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var since = DateTime.UtcNow - Window;

        var attempts = await _db.LoginAttempts
            .Where(a => a.Email == normalized && a.IpAddress == ipAddress && a.AttemptedAt >= since)
            .OrderByDescending(a => a.AttemptedAt)
            .ToListAsync(cancellationToken);

        var lastSuccess = attempts.FirstOrDefault(a => a.Success);
        var failuresAfterLastSuccess = lastSuccess is null
            ? attempts.Where(a => !a.Success).ToList()
            : attempts.Where(a => !a.Success && a.AttemptedAt > lastSuccess.AttemptedAt).ToList();

        var failureCount = failuresAfterLastSuccess.Count;
        var lockoutSeconds = failureCount switch
        {
            >= 10 => 900,
            >= 5  => 120,
            >= 3  => 30,
            _     => 0,
        };

        if (lockoutSeconds == 0)
        {
            return new LockoutDecision(false, 0, failureCount);
        }

        var mostRecentFailureAt = failuresAfterLastSuccess[0].AttemptedAt;
        var lockoutExpiresAt = mostRecentFailureAt.AddSeconds(lockoutSeconds);
        var remaining = lockoutExpiresAt - DateTime.UtcNow;

        if (remaining <= TimeSpan.Zero)
        {
            return new LockoutDecision(false, 0, failureCount);
        }

        return new LockoutDecision(true, (int)Math.Ceiling(remaining.TotalSeconds), failureCount);
    }

    public async Task RecordAsync(string email, string ipAddress, bool success, string? userAgent, string? firebaseErrorCode, CancellationToken cancellationToken = default)
    {
        _db.LoginAttempts.Add(new LoginAttempt
        {
            Email = email.Trim().ToLowerInvariant(),
            IpAddress = ipAddress,
            Success = success,
            UserAgent = userAgent,
            FirebaseErrorCode = firebaseErrorCode,
        });

        await _db.SaveChangesAsync(cancellationToken);

        if (!success && _escalator is not null)
        {
            try
            {
                await _escalator.OnFailureRecordedAsync(email, ipAddress, userAgent, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Lockout escalation detector threw — lockout itself succeeded");
            }
        }
    }
}
