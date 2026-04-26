namespace FinanceManagement.API.Services;

public interface IEmailSender
{
    Task<bool> SendAsync(string toAddress, string subject, string htmlBody, CancellationToken ct);
}
