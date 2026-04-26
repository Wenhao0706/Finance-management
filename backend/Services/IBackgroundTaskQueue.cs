namespace FinanceManagement.API.Services;

public interface IBackgroundTaskQueue
{
    bool TryEnqueue(Func<IServiceProvider, CancellationToken, Task> work);
    ValueTask<Func<IServiceProvider, CancellationToken, Task>> DequeueAsync(CancellationToken ct);
}
