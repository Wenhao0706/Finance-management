namespace FinanceManagement.API.Models;

// Records every Firebase login attempt the frontend reports back to us.
// Used by LockoutService to compute progressive delays. Lockout is keyed
// on (Email, IpAddress) — never on Email alone — so an attacker who
// guesses someone else's email can't lock the victim out from a different IP.
public class LoginAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public bool Success { get; set; }
    public DateTime AttemptedAt { get; set; } = DateTime.UtcNow;
    public string? UserAgent { get; set; }
    public string? FirebaseErrorCode { get; set; }
}
