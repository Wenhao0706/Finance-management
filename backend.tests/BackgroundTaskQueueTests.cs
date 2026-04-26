using FinanceManagement.API.Services;

namespace FinanceManagement.API.Tests;

public class BackgroundTaskQueueTests
{
    [Fact]
    public async Task EnqueuedItem_IsDequeued()
    {
        var queue = new BackgroundTaskQueue(capacity: 4);
        Func<IServiceProvider, CancellationToken, Task> work = (_, _) => Task.CompletedTask;

        var enqueued = queue.TryEnqueue(work);
        var dequeued = await queue.DequeueAsync(CancellationToken.None);

        Assert.True(enqueued);
        Assert.Same(work, dequeued);
    }

    [Fact]
    public void TryEnqueue_ReturnsFalse_WhenFull()
    {
        var queue = new BackgroundTaskQueue(capacity: 1);
        Func<IServiceProvider, CancellationToken, Task> work = (_, _) => Task.CompletedTask;

        Assert.True(queue.TryEnqueue(work));
        Assert.False(queue.TryEnqueue(work));
    }
}
