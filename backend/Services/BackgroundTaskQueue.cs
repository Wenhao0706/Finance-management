using System.Threading.Channels;

namespace FinanceManagement.API.Services;

// Bounded in-memory queue. Items dropped on full (TryEnqueue returns false).
// Best-effort by design — no persistence, restart-loss is acceptable for
// security alerts (the lockout itself is what's load-bearing).
public sealed class BackgroundTaskQueue : IBackgroundTaskQueue
{
    private readonly Channel<Func<IServiceProvider, CancellationToken, Task>> _channel;

    public BackgroundTaskQueue(int capacity = 1000)
    {
        _channel = Channel.CreateBounded<Func<IServiceProvider, CancellationToken, Task>>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
            });
    }

    public bool TryEnqueue(Func<IServiceProvider, CancellationToken, Task> work)
        => _channel.Writer.TryWrite(work);

    public ValueTask<Func<IServiceProvider, CancellationToken, Task>> DequeueAsync(CancellationToken ct)
        => _channel.Reader.ReadAsync(ct);
}
