namespace DataLayer.Services;

public class RmqSettings
{   // can be read out of configuration (appsettings.json)
    public required string ConnectionString { get; init; }
    public required string ExchangeName { get; init; }
    public required string QueueName { get; init; }
    public required string RoutingKey { get; init; }
}