namespace FinanceManagement.API.Services;

public interface IBudgetAlertDetector
{
    // Called after every Transaction Create/Update/Delete that affects the
    // given (userId, year, month). Detects threshold crossings and enqueues
    // 100% emails. Throttled — at most one email per (bucket-or-category)
    // per month per user.
    Task OnTransactionChangedAsync(string userId, int year, int month, CancellationToken ct);
}
