using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace DataLayer.Services
{
    public sealed class RmqPublisherService(
        IConnectionFactory factory,
        IOptions<RmqSettings> config,
        ILogger<RmqPublisherService> logger)
        : IMessagePublisherService, IMessageReadiness, IAsyncDisposable
    {
        private int _readiness = (int)MessageReadinessState.Connecting;
        public MessageReadinessState State => (MessageReadinessState)Volatile.Read(ref _readiness);
        public bool CanWriteInvoices => State == MessageReadinessState.Ready;

        // Serialize initialization, publishing and cleanup; shutdown callbacks only update readiness.
        private readonly SemaphoreSlim _initializationLock = new(1, 1);
        private IConnection? _connection;
        private IChannel? _channel;

        private bool _disposed;

        private async Task<(IConnection Connection, IChannel)> EnsureInitializedAsync(
            CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (State == MessageReadinessState.Ready && _connection is { IsOpen: true } && _channel is { IsOpen: true })
                return (_connection, _channel);

            try
            {
                logger.LogInformation("Initializing RabbitMQ connection to {ExchangeName}", config.Value.ExchangeName);

                // Clean up, just for the case...
                await CleanupResourcesAsync();
                Volatile.Write(ref _readiness, (int)MessageReadinessState.Connecting);

                // Create new connection and channel...
                _connection = await factory.CreateConnectionAsync(cancellationToken);
                _connection.ConnectionShutdownAsync += OnShutdownAsync;
                _channel = await _connection.CreateChannelAsync(null, cancellationToken);
                _channel.ChannelShutdownAsync += OnShutdownAsync;

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

                if (!_connection.IsOpen || !_channel.IsOpen ||
                    Interlocked.CompareExchange(ref _readiness, (int)MessageReadinessState.Ready,
                        (int)MessageReadinessState.Connecting) != (int)MessageReadinessState.Connecting)
                    throw new InvalidOperationException("RabbitMQ became unavailable during initialization.");
                logger.LogInformation("RabbitMQ connection initialized successfully");

                return (_connection, _channel);
            }
            catch (Exception ex)
            {
                logger.LogError("Failed to initialize RabbitMQ connection ({ExceptionType})", ex.GetType().Name);
                await CleanupResourcesAsync();
                throw;
            }
        }

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            await _initializationLock.WaitAsync(cancellationToken);
            try
            {
                await EnsureInitializedAsync(cancellationToken);
            }
            finally
            {
                _initializationLock.Release();
            }
        }

        public async Task PublishInvoiceEventAsync(string eventType, object data,
            CancellationToken cancellationToken = default)
        {
            await _initializationLock.WaitAsync(cancellationToken);
            try
            {
                var (_, channel) = await EnsureInitializedAsync(cancellationToken);

                var routingKey = $"invoice.{eventType.ToLower()}";
                var message = JsonSerializer.Serialize(data);
                var body = Encoding.UTF8.GetBytes(message);

                var (formattedMessage, isJson) = FormatMessageAsJsonOrText(Encoding.UTF8.GetString(body));
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
                Volatile.Write(ref _readiness, (int)MessageReadinessState.Unavailable);
                throw;
            }
            finally
            {
                _initializationLock.Release();
            }
        }

        private Task OnShutdownAsync(object sender, ShutdownEventArgs args)
        {
            Volatile.Write(ref _readiness, (int)MessageReadinessState.Unavailable);
            return Task.CompletedTask;
        }

        private async Task CleanupResourcesAsync()
        {
            Volatile.Write(ref _readiness, (int)MessageReadinessState.Unavailable);
            try
            {
                if (_channel != null)
                {
                    _channel.ChannelShutdownAsync -= OnShutdownAsync;
                    await _channel.DisposeAsync();
                    _channel = null;
                }

                if (_connection != null)
                {
                    _connection.ConnectionShutdownAsync -= OnShutdownAsync;
                    await _connection.DisposeAsync();
                    _connection = null;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning("Error during RabbitMQ resource cleanup ({ExceptionType})", ex.GetType().Name);
            }
            finally
            {
                Volatile.Write(ref _readiness, (int)MessageReadinessState.Unavailable);
            }
        }

        public async ValueTask DisposeAsync()
        {
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
                _initializationLock.Release();
            }
        }
        private static (string formattedMessage, bool isJson) FormatMessageAsJsonOrText(string messageRaw)
        {
            try
            {
                var jsonObj = JsonDocument.Parse(messageRaw);
                var formattedMessage = JsonSerializer.Serialize(
                    jsonObj,
#pragma warning disable CA1869
                    new JsonSerializerOptions
#pragma warning restore CA1869
                    {
                        WriteIndented = true
                    });
                return (formattedMessage, true);
            }
            catch (JsonException)
            {
                return (messageRaw, false);
            }
        }
    }
}
