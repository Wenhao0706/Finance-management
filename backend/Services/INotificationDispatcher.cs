namespace FinanceManagement.API.Services;

public interface INotificationDispatcher
{
    Task DispatchLockoutAlertAsync(string attemptedEmail, string ipAddress, string? userAgent, CancellationToken ct);
}

public interface IFirebaseUserLookup
{
    Task<FirebaseUserInfo?> LookupAsync(string emailOrUid, CancellationToken ct);
}

public sealed record FirebaseUserInfo(string? DisplayName, string? Email = null);

public sealed record DispatcherOptions(string AdminEmail, string ResetPasswordUrl);
