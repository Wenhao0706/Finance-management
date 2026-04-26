using System.Net;

namespace FinanceManagement.API.Services;

// Inline HTML templates. Kept simple — two emails, both informational.
public static class EmailTemplates
{
    public static (string Subject, string Html) UserSecurityAlert(
        string? displayName, string ip, DateTime utcTimestamp, string resetPasswordUrl)
    {
        var name = string.IsNullOrWhiteSpace(displayName) ? "Hi there" : $"Hi {WebUtility.HtmlEncode(displayName)}";
        var when = utcTimestamp.ToString("u");
        var subject = "Sign-in attempts blocked on your Finance Management account";
        var html = $@"
<!DOCTYPE html><html><body style='font-family:system-ui,sans-serif;max-width:520px;'>
  <p>{name},</p>
  <p>We blocked further sign-in attempts on your Finance Management account after 10 failed attempts from IP <code>{WebUtility.HtmlEncode(ip)}</code> at {when} UTC.</p>
  <p>If this was you, you can wait 15 minutes and try again. <strong>If it wasn't, change your password now.</strong></p>
  <p><a href='{WebUtility.HtmlEncode(resetPasswordUrl)}' style='display:inline-block;padding:10px 16px;background:#2563eb;color:white;text-decoration:none;border-radius:6px;'>Reset password</a></p>
  <hr style='border:none;border-top:1px solid #ddd;margin:24px 0;'>
  <p style='font-size:12px;color:#666;'>You're receiving this because someone tried to sign in to your Finance Management account.</p>
</body></html>";
        return (subject, html);
    }

    public static (string Subject, string Html) AdminProbeAlert(
        string ip, string attemptedEmail, string? userAgent, DateTime utcTimestamp, int totalFailsFromIpToday)
    {
        var subject = $"[Finance Management] Probe attempt: {ip}";
        var when = utcTimestamp.ToString("u");
        var ua = WebUtility.HtmlEncode(userAgent ?? "(none)");
        var html = $@"
<!DOCTYPE html><html><body style='font-family:ui-monospace,monospace;'>
  <p>10 failed attempts from <strong>{WebUtility.HtmlEncode(ip)}</strong> in last 15 min targeting <strong>{WebUtility.HtmlEncode(attemptedEmail)}</strong> (unregistered).</p>
  <table cellpadding='4' style='border-collapse:collapse;font-size:13px;'>
    <tr><td>IP</td><td>{WebUtility.HtmlEncode(ip)}</td></tr>
    <tr><td>User-Agent</td><td>{ua}</td></tr>
    <tr><td>Attempted email</td><td>{WebUtility.HtmlEncode(attemptedEmail)}</td></tr>
    <tr><td>Time (UTC)</td><td>{when}</td></tr>
    <tr><td>Total fails from this IP today</td><td>{totalFailsFromIpToday}</td></tr>
  </table>
</body></html>";
        return (subject, html);
    }
}
