using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SBSaaS.Application.Constants;
using Polly;
using RabbitMQ.Client.Exceptions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using SBSaaS.Application.Interfaces;
using SBSaaS.Application.Models;
using SBSaaS.Infrastructure.Messaging;
using SBSaaS.Domain.Enums;
using SBSaaS.Infrastructure.Persistence;
using System.Net.Sockets;
using SBSaaS.Worker.Services;
using SBSaaS.Worker.Models;
using Minio;
using Minio.DataModel.Args;
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
        private RabbitMQ.Client.IConnection? _connection;
        private RabbitMQ.Client.IModel? _channel;
        private readonly string _exchangeName;
        private readonly string _queueName;
        private readonly RabbitMQ.Client.ConnectionFactory _factory;
        private readonly IAsyncPolicy _retryPolicy;

        public FileScanConsumerWorker(ILogger<FileScanConsumerWorker> logger, IOptions<RabbitMqOptions> rabbitMqOptions, IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            var options = rabbitMqOptions.Value;

            _factory = new RabbitMQ.Client.ConnectionFactory()
            {
                HostName = options.Host,
                UserName = options.User,
                Password = options.Password,
                Port = options.Port,
                // Asenkron consumer'lar için bu ayar kritik öneme sahiptir. `DispatchConsumersAsync` eskimiştir (obsolete).
                // Tek seferde işlenecek mesaj sayısını belirler.
                ConsumerDispatchConcurrency = 1
            };

            _exchangeName = options.Exchange ?? "sbsaas.direct";
            _queueName = "sbsaas.scans.file_uploaded";

            // Polly ile yeniden deneme politikası oluştur.
            // BrokerUnreachableException veya SocketException durumunda, üssel olarak artan bir bekleme süresiyle 5 kez yeniden dene.
            _retryPolicy = Policy
                .Handle<BrokerUnreachableException>()
                .Or<SocketException>()
                .WaitAndRetryAsync(5, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), (ex, time) =>
                {
                    _logger.LogWarning(ex, "RabbitMQ'ya bağlanılamadı. {time} sonra yeniden denenecek.", time);
                });
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("File Scan Consumer Service starting.");

            // Bağlantı kurma işlemini yeniden deneme politikası ile StartAsync içinde çalıştır.
            await _retryPolicy.ExecuteAsync(() =>
            {
                _connection = _factory.CreateConnection();
                _channel = _connection.CreateModel(); // Bu metot RabbitMQ.Client.IModel döndürür.
                return Task.CompletedTask;
            });

            if (_channel == null)
            {
                _logger.LogCritical("RabbitMQ'ya tüm denemelere rağmen bağlanılamadı. Worker başlatılamıyor.");
                throw new InvalidOperationException("RabbitMQ'ya bağlanılamadı, servis başlatılamıyor.");
            }

            _logger.LogInformation("RabbitMQ'ya başarıyla bağlanıldı ve kuyruk dinlenmeye hazır.");
            _channel.ExchangeDeclare(exchange: _exchangeName, type: RabbitMQ.Client.ExchangeType.Direct, durable: true);
            _channel.QueueDeclare(queue: _queueName, durable: true, exclusive: false, autoDelete: false, arguments: null);

            // MinIO'nun olayları gönderdiği routing key'i dinle.
            // Bu, minio-init.sh script'indeki 'routing_key' ile eşleşmelidir.
            var routingKey = "file.uploaded";
            _channel.QueueBind(queue: _queueName, exchange: _exchangeName, routingKey: routingKey);

            // DispatchConsumersAsync = true ayarlandığı için, asenkron olayları doğru işlemek üzere AsyncEventingBasicConsumer kullanılmalıdır.
            var consumer = new RabbitMQ.Client.Events.AsyncEventingBasicConsumer(_channel);
            consumer.Received += async (sender, ea) =>
            {
                var body = ea.Body.ToArray();
                var message = Encoding.UTF8.GetString(body);
                _logger.LogDebug("Gelen Ham Mesaj: {Message}", message);

                bool allRecordsProcessedSuccessfully = true;
                try
                {
                    var notification = JsonSerializer.Deserialize<MinioNotification>(message);

                    if (notification?.Records != null)
                    {
                        // MinIO tek bir olayda birden fazla kayıt gönderebilir.
                        foreach (var record in notification.Records)
                        {
                            try
                            {
                                await ProcessMinioEvent(record);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Mesajdaki bir kayıt işlenirken kritik hata oluştu: {ObjectName}. Mesaj tekrar denenecek.", record.S3.Object.Key);
                                allRecordsProcessedSuccessfully = false;
                                // Bir kayıt başarısız olursa, tüm mesajın yeniden denenmesi için döngüden çık.
                                break;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Gelen mesaj ayrıştırılamadı veya işlenemedi. Mesaj tekrar denenecek. Mesaj: {Message}", message);
                    allRecordsProcessedSuccessfully = false;
                }

                if (allRecordsProcessedSuccessfully)
                {
                    _channel.BasicAck(ea.DeliveryTag, false);
                }
                else
                {
                    // Başarısız olan mesajı yeniden denemek üzere kuyruğa geri gönder.
                    // Not: Sürekli hata veren "zehirli" mesajlar için bir Dead-Letter Queue (DLQ) stratejisi düşünülmelidir.
                    _channel.BasicNack(ea.DeliveryTag, false, requeue: true);
                }
            };

            _channel.BasicConsume(queue: _queueName, autoAck: false, consumer: consumer);
        }

        private async Task ProcessMinioEvent(MinioEventRecord record)
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var minioClient = scope.ServiceProvider.GetRequiredService<IMinioClient>();
                var antivirusScanner = scope.ServiceProvider.GetRequiredService<IAntivirusScanner>();
                var dbContext = scope.ServiceProvider.GetRequiredService<SbsDbContext>();
                var meteringService = scope.ServiceProvider.GetRequiredService<IMeteringService>();
                var tenantContext = (WorkerTenantContext)scope.ServiceProvider.GetRequiredService<ITenantContext>();
                var userContext = (WorkerUserContext)scope.ServiceProvider.GetRequiredService<ICurrentUser>();

                var bucketName = record.S3.Bucket.Name;
                // MinIO'dan gelen nesne adı URL kodlanmış olabilir, decode edelim.
                var objectName = System.Net.WebUtility.UrlDecode(record.S3.Object.Key);

                // Yinelenen olayları önlemek için veritabanını kontrol et.
                var existingFile = await dbContext.Files.AsNoTracking().FirstOrDefaultAsync(f => f.StorageObjectName == objectName);
                if (existingFile != null)
                {
                    _logger.LogWarning("Bu dosya ({ObjectName}) için zaten bir kayıt mevcut. Yinelenen olay atlanıyor.", objectName);
                    return;
                }

                _logger.LogInformation("Yeni dosya olayı işleniyor: Bucket={Bucket}, Object={Object}", bucketName, objectName);

                // 1. Dosyanın metadata'sını MinIO'dan al.
                var statArgs = new StatObjectArgs().WithBucket(bucketName).WithObject(objectName);
                var stats = await minioClient.StatObjectAsync(statArgs);

                // 2. Metadata'dan Tenant ve User bilgilerini çıkar.
                if (!stats.MetaData.TryGetValue("x-amz-meta-tenant-id", out var tenantIdStr) || !Guid.TryParse(tenantIdStr, out var tenantId))
                {
                    // Sessizce dönmek yerine exception fırlat. Bu, mesajın kaybolmasını önler ve NACK ile tekrar denenmesini sağlar.
                    throw new InvalidOperationException($"Dosya metadata'sında geçerli 'x-amz-meta-tenant-id' bulunamadı: {objectName}");
                }

                if (!stats.MetaData.TryGetValue("x-amz-meta-uploaded-by-user-id", out var userIdStr) || !Guid.TryParse(userIdStr, out var userId) || userId == Guid.Empty)
                {
                    // Sessizce dönmek yerine exception fırlat.
                    throw new InvalidOperationException($"Dosya metadata'sında geçerli 'x-amz-meta-uploaded-by-user-id' bulunamadı: {objectName}");
                }

                // 3. Worker'ın bu işlem kapsamı için context'ini ayarla.
                tenantContext.SetTenantId(tenantId);
                userContext.SetUserId(userId);

                // 4. Veritabanına dosya kaydını oluştur.
                var fileEntity = new SBSaaS.Domain.Entities.File
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    OriginalFileName = stats.MetaData.GetValueOrDefault("x-amz-meta-original-filename", objectName),
                    ContentType = stats.ContentType,
                    Size = stats.Size,
                    BucketName = bucketName,
                    StorageObjectName = objectName,
                    Checksum = stats.ETag,
                    ScanStatus = ScanStatus.PendingScan
                };
                dbContext.Files.Add(fileEntity);
                await dbContext.SaveChangesAsync(); // SaveChangesAsync, CreatedAt/CreatedBy alanlarını otomatik doldurur.

                _logger.LogInformation("Dosya veritabanına kaydedildi. FileId: {FileId}. Şimdi taranıyor...", fileEntity.Id);

                // 5. Dosyayı stream olarak tara.
                ScanResult? scanResult = null; // NullReferenceException'ı önlemek için başlangıç değeri ata.
                try
                {
                    var getArgs = new GetObjectArgs()
                        .WithBucket(bucketName)
                        .WithObject(objectName)
                        .WithCallbackStream(async (stream, cancellationToken) =>
                        {
                            // Dosyayı doğrudan stream olarak tarayıcıya gönder. Belleğe yükleme.
                            scanResult = await antivirusScanner.ScanFileAsync(stream, cancellationToken);
                        });

                    await minioClient.GetObjectAsync(getArgs);

                    if (scanResult == null)
                    {
                        throw new InvalidOperationException($"Dosya taranamadı veya dosya boş: {objectName}. Tarama sonucu null döndü.");
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"MinIO'dan dosya okunurken veya taranırken hata oluştu: {objectName}", ex);
                }
                
                _logger.LogInformation("Tarama tamamlandı: FileId={FileId}, Infected={IsInfected}", fileEntity.Id, scanResult.IsInfected);

                // 6. Tarama sonucunu veritabanına işle.
                await UpdateFileScanResultAsync(fileEntity, scanResult!, dbContext, minioClient, meteringService);
            }
        }

        private async Task UpdateFileScanResultAsync(Domain.Entities.File fileEntity, ScanResult scanResult, SbsDbContext dbContext, IMinioClient minioClient, IMeteringService meteringService)
        {
            // Entity zaten context tarafından izleniyor, tekrar çekmeye gerek yok.
            // var fileEntity = await dbContext.Files.FirstOrDefaultAsync(f => f.Id == fileId);

            if (fileEntity == null)
            {
                _logger.LogWarning("Tarama sonucunu güncellemek için dosya entity'si bulunamadı.");
                return;
            }

            fileEntity.ScanStatus = scanResult.IsInfected ? ScanStatus.ScannedInfected : ScanStatus.ScannedClean;
            fileEntity.ScannedAt = DateTimeOffset.UtcNow;
            fileEntity.ScanResultDetails = scanResult.VirusName;

            if (scanResult.IsInfected)
            {
                _logger.LogWarning("Dosya {FileId} virüslü. MinIO'dan siliniyor ve DB'de silindi olarak işaretleniyor.", fileEntity.Id);
                try
                {
                    if (!string.IsNullOrEmpty(fileEntity.BucketName) && !string.IsNullOrEmpty(fileEntity.StorageObjectName))
                    {
                        var rmArgs = new RemoveObjectArgs().WithBucket(fileEntity.BucketName).WithObject(fileEntity.StorageObjectName);
                        await minioClient.RemoveObjectAsync(rmArgs);
                        _logger.LogInformation("Dosya {FileId} MinIO'dan başarıyla silindi.", fileEntity.Id);
                    }
                    else
                    {
                        _logger.LogWarning("BucketName veya StorageObjectName boş olduğu için dosya {FileId} MinIO'dan silinemedi.", fileEntity.Id);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Virüslü dosya {FileId} MinIO'dan silinirken hata oluştu.", fileEntity.Id);
                    // Hata durumunda bile DB kaydını güncellemeye devam et
                }

                // Dosyayı veritabanından geçici olarak sil (soft-delete).
                // SbsDbContext içindeki interceptor, bu işlemi yakalayıp IsDeleted, DeletedBy, DeletedAt alanlarını dolduracaktır.
                dbContext.Files.Remove(fileEntity);
            }
            else
            {
                // Faz 4 - Dosya temizse kullanım ölçümlemesi yap.
                // Idempotency key olarak dosyanın ETag'ini (checksum) kullanabiliriz. Bu, aynı olayın iki kez işlenip
                // müşterinin iki kez ücretlendirilmesini engeller.
                var idempotencyKey = $"{fileEntity.StorageObjectName}:{fileEntity.Checksum}";
                await meteringService.RecordUsageAsync(
                    fileEntity.TenantId,
                    MeteringKeys.StorageBytes,
                    fileEntity.Size,
                    MeteringSources.FileUploadSuccessful,
                    idempotencyKey);
            }

            await dbContext.SaveChangesAsync();
            _logger.LogInformation("Dosya {FileId} tarama sonucu veritabanına güncellendi.", fileEntity.Id);
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
            _channel?.Dispose();
            _connection?.Close();
            _connection?.Dispose();
        }
    }
}
