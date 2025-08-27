using SBSaaS.Application.Interfaces;

namespace SBSaaS.Worker;

/// <summary>
/// A background service that dequeues and executes tasks from the IBackgroundTaskQueue.
/// </summary>
public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IBackgroundTaskQueue _taskQueue;
    private readonly IServiceProvider _serviceProvider;

    public Worker(ILogger<Worker> logger, IBackgroundTaskQueue taskQueue, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _taskQueue = taskQueue;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Arka plan görev işleyicisi (Worker) çalışmaya başladı.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Kuyruktan bir görev çekmeyi bekle
                var workItem = await _taskQueue.DequeueTaskAsync(stoppingToken);
                _logger.LogInformation("Yeni bir görev alındı. İşleniyor...");

                // Her görev için yeni bir scope oluştur. Bu, scoped servislerin (DbContext gibi)
                // her görev için ayrı bir instance ile çalışmasını sağlar.
                using var scope = _serviceProvider.CreateScope();
                await workItem(scope.ServiceProvider, stoppingToken);

                _logger.LogInformation("Görev başarıyla tamamlandı.");
            }
            // Servis durdurulduğunda bu exception normaldir, görmezden gel.
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Bir görev işlenirken beklenmedik bir hata oluştu.");
            }
        }

        _logger.LogInformation("Arka plan görev işleyicisi (Worker) durduruluyor.");
    }
}