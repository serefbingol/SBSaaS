using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using SBSaaS.Common.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Minio;
using Minio.DataModel.Args;
using nClam;

namespace SBSaaS.Worker.Services;

public class FileProcessingService : IHostedService
{
    private readonly ILogger<FileProcessingService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IConnection _connection;
    private readonly IModel _channel;
    private readonly IMinioClient _minioClient;
    private readonly ClamClient _clamClient;

    public FileProcessingService(ILogger<FileProcessingService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        var factory = new ConnectionFactory
        {
            HostName = _configuration["RABBITMQ_HOST"] ?? "rabbitmq",
            Port = int.Parse(_configuration["RABBITMQ_PORT"] ?? "5672"),
            UserName = _configuration["RABBITMQ_USER"] ?? "guest",
            Password = _configuration["RABBITMQ_PASSWORD"] ?? "guest"
        };
        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();
        _minioClient = new MinioClient()
            .WithEndpoint(_configuration["Minio:Endpoint"] ?? "minio:9000")
            .WithCredentials(_configuration["Minio:AccessKey"] ?? throw new InvalidOperationException("Minio:AccessKey is not configured."), _configuration["Minio:SecretKey"] ?? throw new InvalidOperationException("Minio:SecretKey is not configured."))
            .WithSSL(false)
            .Build();
        _clamClient = new ClamClient(_configuration["Clamav:Host"] ?? "clamav", int.Parse(_configuration["Clamav:Port"] ?? "3310"));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _channel.QueueDeclare(queue: "minio-file-events", durable: true, exclusive: false, autoDelete: false, arguments: null);
        var consumer = new EventingBasicConsumer(_channel);
        consumer.Received += async (model, ea) =>
        {
            var body = ea.Body.ToArray();
            var message = Encoding.UTF8.GetString(body);
            try
            {
                var minioEvent = JsonSerializer.Deserialize<MinioEventRecord>(message);
                if (minioEvent?.EventName?.Contains("s3:ObjectCreated") == true)
                {
                    await ProcessFileAsync(minioEvent.S3, ea.DeliveryTag);
                }
                _channel.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Mesaj işlenirken kalıcı bir hata oluştu.");
                _channel.BasicNack(deliveryTag: ea.DeliveryTag, multiple: false, requeue: false);
            }
        };
        _channel.BasicConsume(queue: "minio-file-events", autoAck: false, consumer: consumer);
        _logger.LogInformation("Worker servisi başlatıldı ve RabbitMQ kuyruğunu dinliyor.");
        await Task.Delay(-1, cancellationToken);
    }

    private async Task ProcessFileAsync(FileUploadedEvent fileEvent, ulong deliveryTag)
    {
        _logger.LogInformation("Dosya işleme süreci başladı. Dosya: {FilePath}", fileEvent.FilePath);
        try
        {
            var fileStream = new MemoryStream();
            await _minioClient.GetObjectAsync(new GetObjectArgs().WithBucket(fileEvent.BucketName).WithObject(fileEvent.FilePath).WithCallbackStream(stream => stream.CopyTo(fileStream)), cancellationToken: CancellationToken.None);
            fileStream.Position = 0;

            var scanResult = await _clamClient.SendAndScanFileAsync(fileStream);

            if (scanResult.Result == ClamScanResults.VirusDetected)
            {
                _logger.LogWarning("!!! Virüs bulundu: {FilePath}. Karantina işlemi başlatılıyor.", fileEvent.FilePath);
                await QuarantineFileAsync(fileEvent);
            }
            else
            {
                _logger.LogInformation("Dosya temiz. İşlem tamamlandı: {FilePath}", fileEvent.FilePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dosya işlenirken veya ClamAV taramasında hata oluştu.");
            _channel.BasicNack(deliveryTag: deliveryTag, multiple: false, requeue: true);
        }
    }

    private Task QuarantineFileAsync(FileUploadedEvent fileEvent)
    {
        _logger.LogInformation("Dosya karantinaya alınıyor: {FilePath}", fileEvent.FilePath);
        // TODO: Implement quarantine logic, e.g., move the file to a 'quarantine' bucket.
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _connection.Close();
        _logger.LogInformation("Worker servisi durduruldu.");
        return Task.CompletedTask;
    }
}
