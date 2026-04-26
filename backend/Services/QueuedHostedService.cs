namespace FinanceManagement.API.Services;

// Drains BackgroundTaskQueue. Each work item runs in its own DI scope so
// scoped services (DbContext) work correctly. Exceptions in work items are
// logged and swallowed — never crash the host.
public sealed class QueuedHostedService : BackgroundService
{
    private readonly IBackgroundTaskQueue _queue;
    private readonly IServiceProvider _services;
    private readonly ILogger<QueuedHostedService> _logger;

    public QueuedHostedService(IBackgroundTaskQueue queue, IServiceProvider services, ILogger<QueuedHostedService> logger)
    {
        _queue = queue;
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            Func<IServiceProvider, CancellationToken, Task> work;
            try
            {
                work = await _queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            using var scope = _services.CreateScope();
            try
            {
                await work(scope.ServiceProvider, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background task failed");
            }
        }
    }
}
