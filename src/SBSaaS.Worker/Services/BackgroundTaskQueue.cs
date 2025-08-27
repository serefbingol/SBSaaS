using System.Threading.Channels;
using SBSaaS.Application.Interfaces;

namespace SBSaaS.Worker.Services;

/// <summary>
/// Arka plan görevleri için thread-safe bir kuyruk sağlayan IBackgroundTaskQueue implementasyonu.
/// System.Threading.Channels kullanarak verimli ve non-blocking bir yapı sunar.
/// </summary>
public sealed class BackgroundTaskQueue : IBackgroundTaskQueue
{
    private readonly Channel<Func<IServiceProvider, CancellationToken, Task>> _queue;

    public BackgroundTaskQueue(int capacity)
    {
        // Kapasite, kuyruğun ne kadar görev tutabileceğini belirler.
        // Bounded (sınırlı) bir kanal kullanmak, belleğin kontrolsüz büyümesini engeller.
        var options = new BoundedChannelOptions(capacity)
        {
            // Kuyruk doluysa, eklemeye çalışan thread'i (WriteAsync) bekletir.
            FullMode = BoundedChannelFullMode.Wait
        };
        _queue = Channel.CreateBounded<Func<IServiceProvider, CancellationToken, Task>>(options);
    }

    public async ValueTask EnqueueTaskAsync(Func<IServiceProvider, CancellationToken, Task> taskItem)
    {
        if (taskItem is null)
        {
            throw new ArgumentNullException(nameof(taskItem));
        }

        // Görevi kuyruğa asenkron olarak yazar. Kuyruk doluysa, yer açılana kadar bekler.
        await _queue.Writer.WriteAsync(taskItem);
    }

    public async ValueTask<Func<IServiceProvider, CancellationToken, Task>> DequeueTaskAsync(CancellationToken cancellationToken)
    {
        // Kuyruktan bir öğe okunana kadar asenkron olarak bekler.
        var taskItem = await _queue.Reader.ReadAsync(cancellationToken);
        return taskItem;
    }
}