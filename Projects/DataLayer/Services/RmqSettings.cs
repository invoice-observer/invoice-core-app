namespace DataLayer.Services;

public class RmqSettings
{
    public string ConnectionString { get; } = string.Empty;
    public string ExchangeName { get; } = string.Empty;
    public string QueueName { get; } = string.Empty;
    public string RoutingKey { get; } = string.Empty;
}