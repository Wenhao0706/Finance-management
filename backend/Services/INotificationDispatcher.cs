namespace FinanceManagement.API.Services;

public interface INotificationDispatcher
{
    Task DispatchLockoutAlertAsync(string attemptedEmail, string ipAddress, string? userAgent, CancellationToken ct);
}

public interface IFirebaseUserLookup
{
    Task<FirebaseUserInfo?> LookupAsync(string email, CancellationToken ct);
}

public sealed record FirebaseUserInfo(string? DisplayName);

public sealed record DispatcherOptions(string AdminEmail, string ResetPasswordUrl);
