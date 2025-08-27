using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using SBSaaS.Application.Events;
using SBSaaS.Application.Interfaces;
using SBSaaS.Application.Models;
using SBSaaS.Domain.Entities;
using SBSaaS.Domain.Enums;
using SBSaaS.Infrastructure.Persistence;
using SBSaaS.Worker.Services;
using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace SBSaaS.Worker
{
    public class FileScanConsumerWorker : IHostedService, IDisposable
    {
        private readonly ILogger<FileScanConsumerWorker> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConnection _connection;
        private readonly IModel _channel;
        private readonly string _exchangeName;
        private readonly string _queueName;

        public FileScanConsumerWorker(ILogger<FileScanConsumerWorker> logger, IConfiguration configuration, IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;

            var factory = new ConnectionFactory()
            {
                HostName = configuration["RabbitMQ:Host"],
                UserName = configuration["RabbitMQ:User"],
                Password = configuration["RabbitMQ:Password"],
                Port = int.Parse(configuration["RabbitMQ:Port"] ?? "5672"),
                DispatchConsumersAsync = true // Async event handler için önemli
            };

            _exchangeName = configuration["RabbitMQ:Exchange"] ?? "sbsaas.direct";
            _queueName = "sbsaas.scans.file_uploaded";

            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("File Scan Consumer Service starting.");

            _channel.ExchangeDeclare(exchange: _exchangeName, type: ExchangeType.Direct, durable: true);
            _channel.QueueDeclare(queue: _queueName, durable: true, exclusive: false, autoDelete: false, arguments: null);
            
            var routingKey = typeof(FileUploadedForScanEvent).Name;
            _channel.QueueBind(queue: _queueName, exchange: _exchangeName, routingKey: routingKey);

            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.Received += async (model, ea) =>
            {
                var body = ea.Body.ToArray();
                var messageJson = Encoding.UTF8.GetString(body);
                var eventMessage = JsonSerializer.Deserialize<FileUploadedForScanEvent>(messageJson);

                if (eventMessage != null)
                {
                    _logger.LogInformation("Received FileUploadedForScanEvent for FileId: {FileId}, TenantId: {TenantId}", eventMessage.FileId, eventMessage.TenantId);
                    try
                    {
                        await ProcessFileScan(eventMessage);

                        _channel.BasicAck(ea.DeliveryTag, false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing message for FileId: {FileId}", eventMessage.FileId);
                        _channel.BasicNack(ea.DeliveryTag, false, true); 
                    }
                }
            };

            _channel.BasicConsume(queue: _queueName, autoAck: false, consumer: consumer);

            return Task.CompletedTask;
        }

        private async Task ProcessFileScan(FileUploadedForScanEvent eventMessage)
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var fileStorage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
                var antivirusScanner = scope.ServiceProvider.GetRequiredService<IAntivirusScanner>();
                var dbContext = scope.ServiceProvider.GetRequiredService<SbsDbContext>();
                var tenantContext = (WorkerTenantContext)scope.ServiceProvider.GetRequiredService<ITenantContext>();

                // Set TenantId for the current scope
                tenantContext.SetTenantId(eventMessage.TenantId);

                _logger.LogInformation("Processing scan for StorageObject: {ObjectName} for Tenant: {TenantId}", eventMessage.StorageObjectName, eventMessage.TenantId);

                ScanResult scanResult;
                using (var fileStream = await fileStorage.DownloadAsync(eventMessage.BucketName, eventMessage.StorageObjectName, CancellationToken.None))
                {
                    scanResult = await antivirusScanner.ScanFileAsync(fileStream, CancellationToken.None);
                }

                _logger.LogInformation("Scan complete for file {FileId}. Infected: {IsInfected}", eventMessage.FileId, scanResult.IsInfected);

                await UpdateFileScanResultAsync(eventMessage.FileId, scanResult, dbContext, fileStorage);
            }
        }

        private async Task UpdateFileScanResultAsync(Guid fileId, ScanResult scanResult, SbsDbContext dbContext, IFileStorage fileStorage)
        {
            var fileEntity = await dbContext.Files.FirstOrDefaultAsync(f => f.Id == fileId);

            if (fileEntity == null)
            {
                _logger.LogWarning("File entity with ID {FileId} not found for updating scan result.", fileId);
                return;
            }

            fileEntity.ScanStatus = scanResult.IsInfected ? ScanStatus.ScannedInfected : ScanStatus.ScannedClean;
            fileEntity.ScannedAt = DateTimeOffset.UtcNow;
            fileEntity.ScanResultDetails = scanResult.VirusName;

            if (scanResult.IsInfected)
            {
                _logger.LogWarning("File {FileId} is infected. Deleting from MinIO and marking as deleted in DB.", fileId);
                try
                {
                    await fileStorage.DeleteAsync(fileEntity.BucketName, fileEntity.StorageObjectName, CancellationToken.None);
                    _logger.LogInformation("File {FileId} successfully deleted from MinIO.", fileId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to delete infected file {FileId} from MinIO.", fileId);
                    // Hata durumunda bile DB kaydını güncellemeye devam et
                }

                fileEntity.IsDeleted = true;
                fileEntity.DeletedUtc = DateTimeOffset.UtcNow;
                fileEntity.DeletedByUserId = "System"; // Or a specific system user ID
            }

            await dbContext.SaveChangesAsync();
            _logger.LogInformation("File {FileId} scan result updated in database.", fileId);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("File Scan Consumer Service stopping.");
            Dispose();
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _channel?.Close();
            _connection?.Close();
            _channel?.Dispose();
            _connection?.Dispose();
        }
    }
}
