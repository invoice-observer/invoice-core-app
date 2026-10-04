namespace DataLayer.Services;

public class RmqSettings
{   // can be read out of configuration (appsettings.json)
    public required string ConnectionString { get; init; } = string.Empty;
    public required string ExchangeName { get; init; } = string.Empty;
    public required string QueueName { get; init; } = string.Empty;
    public required string RoutingKey { get; init; } = string.Empty;
}