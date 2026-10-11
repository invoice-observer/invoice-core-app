namespace DataLayer.Services;

public class RmqSettings
{   // can be read out of configuration (appsettings.json)
    public required string ConnectionString { get; init; } // no credentials, e.g. amqps://host/vhost
    public required string UserName { get; init; }
    public required string UserPassword { get; init; } // secret: user-secrets / environment variable only
    public required string ExchangeName { get; init; }
    public required string QueueName { get; init; }
    public required string RoutingKey { get; init; }
}