namespace FinanceManagement.API.Models;

// One row per outbound email actually sent. Used as the throttle ledger
// (NotificationDispatcher checks this before sending another email of the
// same kind to the same recipient/key combination).
public class EmailNotification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Kind { get; set; } = string.Empty;       // "UserAlert" | "AdminAlert"
    public string Recipient { get; set; } = string.Empty;  // email address sent to
    public string Key { get; set; } = string.Empty;        // throttle dimension (IP)
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}
