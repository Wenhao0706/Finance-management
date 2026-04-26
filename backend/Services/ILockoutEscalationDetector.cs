namespace FinanceManagement.API.Services;

public interface ILockoutEscalationDetector
{
    Task OnFailureRecordedAsync(string email, string ipAddress, string? userAgent, CancellationToken ct);
}
