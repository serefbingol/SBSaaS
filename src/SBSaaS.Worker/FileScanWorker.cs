using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Minio;
using RabbitMQ.Client;
using Minio.DataModel.Args;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;
using Npgsql;
using System.Net.Sockets;

public class FileScanWorker : BackgroundService
{
    private readonly ILogger<FileScanWorker> _logger;
    private readonly IConfiguration _config;
    private ConnectionFactory _factory;
    private IMinioClient _minio;
    private readonly string _exchange;
    private readonly string _clamHost;
    private readonly int _clamPort;

    public FileScanWorker(ILogger<FileScanWorker> logger, IConfiguration config)
    {
        _logger = logger;
        _config = config;

        // MinIO client
        var minioEndpoint = _config["Minio:Endpoint"] ?? throw new InvalidOperationException("Minio:Endpoint configuration is missing.");
        var minioAccessKey = _config["Minio:AccessKey"] ?? throw new InvalidOperationException("Minio:AccessKey configuration is missing.");
        var minioSecretKey = _config["Minio:SecretKey"] ?? throw new InvalidOperationException("Minio:SecretKey configuration is missing.");
        var parts = minioEndpoint.Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[1], out var minioPort))
        {
            throw new InvalidOperationException("Minio:Endpoint configuration is invalid. Expected format: host:port");
        }
        _minio = new MinioClient()
                    .WithEndpoint(parts[0], minioPort)
                    .WithCredentials(minioAccessKey, minioSecretKey)
                    .Build();

        // RabbitMQ factory
        _factory = new ConnectionFactory()
        {
            HostName = _config["RabbitMQ:Host"] ?? throw new InvalidOperationException("RabbitMQ:Host configuration is missing."),
            Port = int.Parse(_config["RabbitMQ:Port"] ?? throw new InvalidOperationException("RabbitMQ:Port configuration is missing.")),
            UserName = _config["RabbitMQ:User"] ?? throw new InvalidOperationException("RabbitMQ:User configuration is missing."),
            Password = _config["RabbitMQ:Password"] ?? throw new InvalidOperationException("RabbitMQ:Password configuration is missing."),
            DispatchConsumersAsync = true
        };

        _exchange = _config["RabbitMQ:Exchange"] ?? throw new InvalidOperationException("RabbitMQ:Exchange configuration is missing.");
        _clamHost = _config["ClamAV:Host"] ?? throw new InvalidOperationException("ClamAV:Host configuration is missing.");
        _clamPort = int.Parse(_config["ClamAV:Port"] ?? throw new InvalidOperationException("ClamAV:Port configuration is missing."));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var conn = _factory.CreateConnection();
        using var ch = conn.CreateModel();

        // Fanout exchange -> temporary queue bound
        ch.ExchangeDeclare(exchange: _exchange, type: ExchangeType.Fanout, durable: true);
        var queueName = ch.QueueDeclare().QueueName;
        ch.QueueBind(queue: queueName, exchange: _exchange, routingKey: "");

        var consumer = new AsyncEventingBasicConsumer(ch);
        consumer.Received += async (model, ea) =>
        {
            try
            {
                var body = ea.Body.ToArray();
                var json = Encoding.UTF8.GetString(body);
                var doc = JsonSerializer.Deserialize<JsonElement>(json);                
                var bucket = doc.TryGetProperty("bucket", out var bucketProp) ? bucketProp.GetString() : null;
                var objectName = doc.TryGetProperty("objectName", out var objectNameProp) ? objectNameProp.GetString() : null;

                if (string.IsNullOrEmpty(bucket) || string.IsNullOrEmpty(objectName))
                {
                    _logger.LogError("Received invalid message with missing bucket or objectName. Discarding. JSON: {json}", json);
                    ch.BasicAck(ea.DeliveryTag, false);
                    return;
                }

                _logger.LogInformation("Yeni dosya geldi: {bucket}/{objectName}", bucket, objectName);

                // 1) Dosyayı MinIO'dan indir
                using var ms = new MemoryStream();
                await _minio.GetObjectAsync(new GetObjectArgs()
                    .WithBucket(bucket)
                    .WithObject(objectName)
                    .WithCallbackStream(s => s.CopyTo(ms)));

                ms.Seek(0, SeekOrigin.Begin);

                // 2) ClamAV ile tara
                var scanResult = ScanStreamWithClamAV(ms, _clamHost, _clamPort);

                _logger.LogInformation("Tarama sonucu: {result}", scanResult);

                if (scanResult.Contains("FOUND"))
                {
                    // Virüslü -> sil
                    await _minio.RemoveObjectAsync(new RemoveObjectArgs().WithBucket(bucket).WithObject(objectName));
                    await InsertScanResultToDb(objectName, bucket, false, scanResult);
                    _logger.LogWarning("Virüslü dosya silindi: {objectName}", objectName);
                }
                else
                {
                    // Temiz -> DB'ye kaydet
                    await InsertScanResultToDb(objectName, bucket, true, scanResult);
                    _logger.LogInformation("Temiz dosya kaydedildi: {objectName}", objectName);
                }

                ch.BasicAck(ea.DeliveryTag, false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dosya tarama sırasında hata");
                ch.BasicNack(ea.DeliveryTag, false, requeue: true);
            }
        };

        ch.BasicConsume(queue: queueName, autoAck: false, consumer: consumer);

        // Keep running
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private string ScanStreamWithClamAV(Stream data, string host, int port)
    {
        // clamd INSTREAM protokolü: "INSTREAM\n" gönder, sonra 4 byte uzunluk + data chunk'lar.
        using var client = new TcpClient();
        client.Connect(host, port);
        using var stream = client.GetStream();
        var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        // send INSTREAM\n
        var inCmd = Encoding.ASCII.GetBytes("INSTREAM\n");
        stream.Write(inCmd, 0, inCmd.Length);

        // Gönderme chunk'ları (max chunk 1MB önerilir)
        data.Seek(0, SeekOrigin.Begin);
        var buffer = new byte[1024 * 256];
        int read;
        while ((read = data.Read(buffer, 0, buffer.Length)) > 0)
        {
            // 4 byte big-endian length
            var lenBytes = BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(read));
            stream.Write(lenBytes, 0, 4);
            stream.Write(buffer, 0, read);
        }

        // 0 uzunluğunda chunk ile bitir
        var zero = BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(0));
        stream.Write(zero, 0, 4);
        stream.Flush();

        // clamd dönüşünü oku (örn: "stream: OK" veya "stream: Eicar-Test-Signature FOUND")
        using var reader = new StreamReader(stream, Encoding.ASCII);
        var result = reader.ReadLine();
        client.Close();
        return result ?? "UNKNOWN";
    }

    private async Task InsertScanResultToDb(string objectName, string bucket, bool clean, string rawResult)
    {
        var cs = $"Host={_config["Postgres:Host"]};Port={_config["Postgres:Port"]};Username={_config["Postgres:User"]};Password={_config["Postgres:Password"]};Database={_config["Postgres:Database"]}";
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO app.file_scans (object_name, bucket, is_clean, raw_result, scanned_at)
            VALUES (@objectName, @bucket, @isClean, @rawResult, NOW());";
        cmd.Parameters.AddWithValue("objectName", objectName);
        cmd.Parameters.AddWithValue("bucket", bucket);
        cmd.Parameters.AddWithValue("isClean", clean);
        cmd.Parameters.AddWithValue("rawResult", rawResult);
        await cmd.ExecuteNonQueryAsync();
    }
}
