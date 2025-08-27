namespace SBSaaS.Infrastructure.Messaging
{
    public class RabbitMqOptions
    {
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 5672;
        public string User { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Exchange { get; set; } = "sbsaas.direct";
    }
}
