namespace SBSaaS.Application.Interfaces;

/// <summary>
/// Arka planda çalıştırılacak görevleri yöneten kuyruk için arayüz.
/// Bu arayüz, görevlerin kuyruğa eklenmesini (enqueue) ve kuyruktan alınmasını (dequeue) soyutlar.
/// </summary>
public interface IBackgroundTaskQueue
{
    /// <summary>
    /// Arka planda çalıştırılmak üzere bir görevi kuyruğa ekler.
    /// </summary>
    /// <param name="taskItem">Çalıştırılacak görev. Bu Func, bir IServiceProvider ve CancellationToken alır.</param>
    ValueTask EnqueueTaskAsync(Func<IServiceProvider, CancellationToken, Task> taskItem);

    /// <summary>
    /// Kuyruktan bir görevi alır.
    /// </summary>
    /// <param name="cancellationToken">İptal token'ı.</param>
    /// <returns>Kuyruktan alınan görev.</returns>
    ValueTask<Func<IServiceProvider, CancellationToken, Task>> DequeueTaskAsync(CancellationToken cancellationToken);
}
