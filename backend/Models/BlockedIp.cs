namespace FinanceManagement.API.Models;

// One row per IP that was auto-soft-blocked due to multi-email probing.
// Inserted by LockoutEscalationDetector; checked by IpBlockMiddleware.
public class BlockedIp
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string IpAddress { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
}
