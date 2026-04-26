namespace FinanceManagement.API.Services;

// Used when RESEND_API_KEY is not set (local dev, CI without secrets).
// Logs the fact a send was attempted; returns true so calling code's
// throttle bookkeeping still records "sent".
public sealed class NoOpEmailSender : IEmailSender
{
    private readonly ILogger<NoOpEmailSender> _logger;

    public NoOpEmailSender(ILogger<NoOpEmailSender> logger) => _logger = logger;

    public Task<bool> SendAsync(string toAddress, string subject, string htmlBody, CancellationToken ct)
    {
        _logger.LogInformation("NoOpEmailSender: would have sent '{Subject}' to {To}", subject, toAddress);
        return Task.FromResult(true);
    }
}
