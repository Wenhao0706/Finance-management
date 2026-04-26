using FinanceManagement.API.Data;
using FinanceManagement.API.Models;
using Microsoft.EntityFrameworkCore;

namespace FinanceManagement.API.Services;

public sealed class NotificationDispatcher : INotificationDispatcher
{
    private const string KindUser  = "UserAlert";
    private const string KindAdmin = "AdminAlert";
    private static readonly TimeSpan UserDedupWindow  = TimeSpan.FromHours(24);
    private static readonly TimeSpan AdminPerIpWindow = TimeSpan.FromHours(1);
    private const int AdminDailyCap = 5;

    private readonly AppDbContext _db;
    private readonly IEmailSender _sender;
    private readonly IFirebaseUserLookup _userLookup;
    private readonly DispatcherOptions _options;
    private readonly ILogger<NotificationDispatcher> _logger;

    public NotificationDispatcher(
        AppDbContext db,
        IEmailSender sender,
        IFirebaseUserLookup userLookup,
        DispatcherOptions options,
        ILogger<NotificationDispatcher> logger)
    {
        _db = db;
        _sender = sender;
        _userLookup = userLookup;
        _options = options;
        _logger = logger;
    }

    public async Task DispatchLockoutAlertAsync(string attemptedEmail, string ipAddress, string? userAgent, CancellationToken ct)
    {
        var normalizedEmail = attemptedEmail.Trim().ToLowerInvariant();
        FirebaseUserInfo? user = null;

        try
        {
            user = await _userLookup.LookupAsync(normalizedEmail, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Firebase user lookup failed; falling back to admin alert");
        }

        if (user is not null)
        {
            await TrySendUserAlertAsync(normalizedEmail, ipAddress, user.DisplayName, ct);
        }
        else
        {
            await TrySendAdminAlertAsync(normalizedEmail, ipAddress, userAgent, ct);
        }
    }

    private async Task TrySendUserAlertAsync(string email, string ip, string? displayName, CancellationToken ct)
    {
        var since = DateTime.UtcNow - UserDedupWindow;
        var alreadySent = await _db.EmailNotifications.AnyAsync(
            n => n.Kind == KindUser && n.Recipient == email && n.Key == ip && n.SentAt >= since, ct);

        if (alreadySent)
        {
            _logger.LogInformation("UserAlert throttled for {Recipient}/{Key}", MaskEmail(email), ip);
            return;
        }

        var (subject, html) = EmailTemplates.UserSecurityAlert(displayName, ip, DateTime.UtcNow, _options.ResetPasswordUrl);
        var ok = await _sender.SendAsync(email, subject, html, ct);

        if (!ok) return;

        _db.EmailNotifications.Add(new EmailNotification
        {
            Kind = KindUser,
            Recipient = email,
            Key = ip,
            SentAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("UserAlert dispatched for {Recipient}/{Key}", MaskEmail(email), ip);
    }

    private async Task TrySendAdminAlertAsync(string attemptedEmail, string ip, string? userAgent, CancellationToken ct)
    {
        var sinceHour = DateTime.UtcNow - AdminPerIpWindow;
        var perIp = await _db.EmailNotifications.AnyAsync(
            n => n.Kind == KindAdmin && n.Key == ip && n.SentAt >= sinceHour, ct);
        if (perIp)
        {
            _logger.LogInformation("AdminAlert throttled per-IP for {Key}", ip);
            return;
        }

        var sinceDay = DateTime.UtcNow.AddDays(-1);
        var dailyCount = await _db.EmailNotifications.CountAsync(
            n => n.Kind == KindAdmin && n.SentAt >= sinceDay, ct);
        if (dailyCount >= AdminDailyCap)
        {
            _logger.LogWarning("AdminAlert daily cap reached ({Cap}); dropping", AdminDailyCap);
            return;
        }

        var todayStart = DateTime.UtcNow.Date;
        var totalFailsFromIpToday = await _db.LoginAttempts.CountAsync(
            a => !a.Success && a.IpAddress == ip && a.AttemptedAt >= todayStart, ct);

        var (subject, html) = EmailTemplates.AdminProbeAlert(ip, attemptedEmail, userAgent, DateTime.UtcNow, totalFailsFromIpToday);
        var ok = await _sender.SendAsync(_options.AdminEmail, subject, html, ct);

        if (!ok) return;

        _db.EmailNotifications.Add(new EmailNotification
        {
            Kind = KindAdmin,
            Recipient = _options.AdminEmail,
            Key = ip,
            SentAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("AdminAlert dispatched for {Key}", ip);
    }

    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 0) return "***";
        var local = email.Substring(0, at);
        var domain = email.Substring(at);
        var visible = local.Length <= 3 ? local : local.Substring(0, 3);
        return visible + "***" + domain;
    }
}
