using FinanceManagement.API.Services;

namespace FinanceManagement.API.Tests.TestHelpers;

// Test double that records calls — used by NotificationDispatcher tests
// and integration tests so we never hit Resend's real API in tests.
public sealed class RecordingEmailSender : IEmailSender
{
    public record SentEmail(string To, string Subject, string HtmlBody);

    public List<SentEmail> Sent { get; } = new();
    public bool ReturnValue { get; set; } = true;

    public Task<bool> SendAsync(string toAddress, string subject, string htmlBody, CancellationToken ct)
    {
        Sent.Add(new SentEmail(toAddress, subject, htmlBody));
        return Task.FromResult(ReturnValue);
    }
}
