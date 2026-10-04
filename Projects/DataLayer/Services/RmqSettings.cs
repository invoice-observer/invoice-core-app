namespace DataLayer.Services;

public class RmqSettings
{
    public string ConnectionString { get; init; } = string.Empty;
    public string ExchangeName { get; init; } = string.Empty;
    public string QueueName { get; init; } = string.Empty;
    public string RoutingKey { get; init; } = string.Empty;
}