using SBSaaS.Application.Interfaces;
using SBSaaS.Worker.Services;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace SBSaaS.Worker;

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
        _logger.LogInformation("Worker service started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var scanRequest = await _taskQueue.DequeueBackgroundWorkItemAsync(stoppingToken);

                _logger.LogInformation("Processing scan request for {BucketName}/{ObjectName}",
                    scanRequest.BucketName, scanRequest.ObjectName);

                // Create a new scope for services that are scoped (like IFileStorage, IAntivirusScanner)
                using (var scope = _serviceProvider.CreateScope())
                {
                    var fileStorage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
                    var antivirusScanner = scope.ServiceProvider.GetRequiredService<IAntivirusScanner>();

                    // Download the file
                    await using var fileStream = await fileStorage.DownloadAsync(
                        scanRequest.BucketName, scanRequest.ObjectName, stoppingToken);

                    // Scan the file
                    fileStream.Position = 0; // Reset stream position for scanner
                    bool isClean = await antivirusScanner.ScanAsync(fileStream);

                    if (!isClean)
                    {
                        _logger.LogWarning("File {BucketName}/{ObjectName} is INFECTED. Deleting...",
                            scanRequest.BucketName, scanRequest.ObjectName);
                        await fileStorage.DeleteAsync(scanRequest.BucketName, scanRequest.ObjectName, stoppingToken);
                        _logger.LogInformation("Infected file {BucketName}/{ObjectName} deleted.",
                            scanRequest.BucketName, scanRequest.ObjectName);
                    }
                    else
                    {
                        _logger.LogInformation("File {BucketName}/{ObjectName} is clean.",
                            scanRequest.BucketName, scanRequest.ObjectName);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // When the app is shutting down, a cancellation might be requested.
                _logger.LogInformation("Worker service is stopping.");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred executing background task.");
            }
        }

        _logger.LogInformation("Worker service stopped.");
    }
}