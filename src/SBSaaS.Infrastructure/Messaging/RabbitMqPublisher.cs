using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using SBSaaS.Application.Interfaces;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace SBSaaS.Infrastructure.Messaging
{
    public class RabbitMqPublisher : IMessagePublisher, System.IDisposable
    {
        private readonly IConnection _connection;
        private readonly IModel _channel;
        private readonly RabbitMqOptions _options;

        public RabbitMqPublisher(IOptions<RabbitMqOptions> options)
        {
            _options = options.Value;
            var factory = new ConnectionFactory()
            {
                HostName = _options.Host,
                Port = _options.Port,
                UserName = _options.User,
                Password = _options.Password
            };
            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();

            _channel.ExchangeDeclare(exchange: _options.Exchange, type: ExchangeType.Direct, durable: true);
        }

        public Task Publish<T>(T message) where T : class
        {
            var messageType = typeof(T).Name;
            var body = JsonSerializer.Serialize(message);
            var bodyBytes = Encoding.UTF8.GetBytes(body);

            var properties = _channel.CreateBasicProperties();
            properties.Persistent = true;

            _channel.BasicPublish(
                exchange: _options.Exchange,
                routingKey: messageType,
                basicProperties: properties,
                body: bodyBytes);

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _channel?.Dispose();
            _connection?.Dispose();
        }
    }
}
