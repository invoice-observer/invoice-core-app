using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;

namespace DataLayer.Services
{
    public sealed class RmqPublisherService(
        IConnectionFactory factory,
        IOptions<RmqSettings> config,
        ILogger<RmqPublisherService> logger)
        : IMessagePublisherService, IAsyncDisposable
    {
        // Lazy initialization components
        private readonly SemaphoreSlim _initializationLock = new(1, 1);
        private IConnection? _connection;
        private IChannel? _channel;
        private bool _initialized;
        private bool _disposed;

        private async Task<(IConnection Connection, IChannel)> EnsureInitializedAsync(
            CancellationToken cancellationToken)
        {
            // Already initialized? - just return existing connection and channel
            if (_initialized && _connection != null && _channel != null)
            {
                return (_connection, _channel);
            }

            // Acquire lock for initialization
            await _initializationLock.WaitAsync(cancellationToken);
            try
            {
                // Double-check: check _initialized again after acquiring lock
                if (_initialized && _connection != null && _channel != null)
                {
                    return (_connection, _channel);
                }

                logger.LogInformation("Initializing RabbitMQ connection to {ExchangeName}", config.Value.ExchangeName);

                // Clean up, just for the case...
                await CleanupResourcesAsync();

                // Create new connection and channel...
                _connection = await factory.CreateConnectionAsync(cancellationToken);
                _channel = await _connection.CreateChannelAsync(null, cancellationToken);

                // ..and setup infrastructure.
                await _channel.ExchangeDeclareAsync(
                    exchange: config.Value.ExchangeName,
                    type: "topic",
                    durable: true,
                    autoDelete: false,
                    cancellationToken: cancellationToken);

                await _channel.QueueDeclareAsync(
                    queue: config.Value.QueueName,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: null,
                    cancellationToken: cancellationToken);

                await _channel.QueueBindAsync(
                    queue: config.Value.QueueName,
                    exchange: config.Value.ExchangeName,
                    routingKey: config.Value.RoutingKey,
                    cancellationToken: cancellationToken);

                _initialized = true;
                logger.LogInformation("RabbitMQ connection initialized successfully");

                return (_connection, _channel);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to initialize RabbitMQ connection");
                await CleanupResourcesAsync();
                throw;
            }
            finally
            {
                _initializationLock.Release();
            }
        }

        public async Task PublishInvoiceEventAsync(string eventType, object data,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var (_, channel) = await EnsureInitializedAsync(cancellationToken);

                var routingKey = $"invoice.{eventType.ToLower()}";
                var message = JsonSerializer.Serialize(data);
                var body = Encoding.UTF8.GetBytes(message);

                var (formattedMessage, isJson) = FormatMessageAsJsonOrText(System.Text.Encoding.UTF8.GetString(body));
                if (isJson)
                {
                    logger.LogInformation("Publishing JSON message:\n      Routing key: {RoutingKey}\n      Message: {FormattedJson}",
                        routingKey, formattedMessage);
                }

                var properties = new BasicProperties
                {
                    Persistent = true,
                    ContentType = "application/json",
                    Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                };

                await channel.BasicPublishAsync(
                    config.Value.ExchangeName,
                    routingKey,
                    true,
                    properties,
                    body,
                    cancellationToken);

            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to publish {EventType} message", eventType);
                _initialized = false; // Force re-initialization on next attempt
                throw;
            }
        }

        private async Task CleanupResourcesAsync()
        {
            try
            {
                if (_channel != null)
                {
                    await _channel.DisposeAsync();
                    _channel = null;
                }

                if (_connection != null)
                {
                    await _connection.DisposeAsync();
                    _connection = null;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error during RabbitMQ resource cleanup");
            }
            finally
            {
                _initialized = false;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;

            await _initializationLock.WaitAsync();
            try
            {
                if (_disposed)
                    return;

                await CleanupResourcesAsync();
                _disposed = true;
            }
            finally
            {
                _initializationLock.Dispose();
            }
        }
        private static (string formattedMessage, bool isJson) FormatMessageAsJsonOrText(string messageRaw)
        {
            try
            {
                var jsonObj = System.Text.Json.JsonDocument.Parse(messageRaw);
                var formattedMessage = System.Text.Json.JsonSerializer.Serialize(
                    jsonObj,
#pragma warning disable CA1869
                    new System.Text.Json.JsonSerializerOptions
#pragma warning restore CA1869
                    {
                        WriteIndented = true
                    });
                return (formattedMessage, true);
            }
            catch (System.Text.Json.JsonException)
            {
                return (messageRaw, false);
            }
        }
    }
}