using DataLayer.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Tests.NUnit.Services;

[TestFixture]
public class RmqPublisherReadinessTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    private sealed class PendingOperation
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task EnterAsync()
        {
            _entered.TrySetResult();
            return _completion.Task;
        }

        public Task WaitForEntryAsync() => _entered.Task.WaitAsync(TestTimeout);
        public void Complete() => _completion.TrySetResult();
        public void Fail(Exception exception) => _completion.TrySetException(exception);
        public void Cancel(CancellationToken token) => _completion.TrySetCanceled(token);
    }

    private Mock<IConnectionFactory> _factory = null!;
    private Mock<IConnection> _connection = null!;
    private Mock<IChannel> _channel = null!;
#pragma warning disable NUnit1032 // TearDown awaits DisposeAsync through AsTask().WaitAsync(TestTimeout).
    private RmqPublisherService _publisher = null!;
#pragma warning restore NUnit1032
    private IMessageReadiness _readiness = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new Mock<IConnectionFactory>();
        _connection = new Mock<IConnection>();
        _channel = new Mock<IChannel>();
        _connection.SetupGet(c => c.IsOpen).Returns(true);
        _channel.SetupGet(c => c.IsOpen).Returns(true);
        _factory.Setup(f => f.CreateConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_connection.Object);
        _connection.Setup(c => c.CreateChannelAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_channel.Object);
        _publisher = new RmqPublisherService(_factory.Object, Options.Create(new RmqSettings
        {
            ConnectionString = "amqp://unused", UserName = "", UserPassword = "",
            ExchangeName = "invoices", QueueName = "invoices", RoutingKey = "invoice.*"
        }), NullLogger<RmqPublisherService>.Instance);
        _readiness = _publisher;
    }

    [TearDown]
    public async Task TearDown() => await _publisher.DisposeAsync().AsTask().WaitAsync(TestTimeout);

    [Test]
    public async Task ExplicitInitialization_PreparesTopologyWithoutPublishing_AndReusesResources()
    {
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        await _publisher.InitializeAsync(token).WaitAsync(TestTimeout, token);
        await _publisher.InitializeAsync(token).WaitAsync(TestTimeout, token);
        AssertState(MessageReadinessState.Ready);
        _factory.Verify(f => f.CreateConnectionAsync(token), Times.Once);
        _connection.Verify(c => c.CreateChannelAsync(null, token), Times.Once);
        _channel.Verify(c => c.ExchangeDeclareAsync("invoices", "topic", true, false, null, false, false, token), Times.Once);
        _channel.Verify(c => c.QueueDeclareAsync("invoices", true, false, false, null, false, false, token), Times.Once);
        _channel.Verify(c => c.QueueBindAsync("invoices", "invoices", "invoice.*", null, false, token), Times.Once);
        _channel.Verify(c => c.BasicPublishAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
            It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task InitializationFailure_DoesNotLogExceptionCredentials()
    {
        var logger = new Mock<ILogger<RmqPublisherService>>();
        const string secret = "amqp://user:private-password@broker";
        _factory.Setup(f => f.CreateConnectionAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(secret));
        await using var publisher = new RmqPublisherService(_factory.Object, Options.Create(new RmqSettings
        {
            ConnectionString = "amqp://unused", UserName = "", UserPassword = "",
            ExchangeName = "invoices", QueueName = "invoices", RoutingKey = "invoice.*"
        }), logger.Object);
        await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.InitializeAsync().WaitAsync(TestTimeout));
        logger.Verify(l => l.Log(It.IsAny<LogLevel>(), It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((value, type) => value != null && value.ToString()!.Contains(secret)),
            It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Never);
        logger.Verify(l => l.Log(It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
            It.Is<Exception?>(ex => ex != null), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Never);
    }

    [Test]
    public void InitialState_IsConnecting()
    {
        AssertState(MessageReadinessState.Connecting);
        _factory.Verify(f => f.CreateConnectionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Initialization_WaitsForBindingBeforeReady()
    {
        var binding = new PendingOperation();
        _channel.Setup(c => c.QueueBindAsync("invoices", "invoices", "invoice.*", null, false,
            It.IsAny<CancellationToken>())).Returns(binding.EnterAsync);
        var publish = _publisher.PublishInvoiceEventAsync("created", new { Id = 1 });
        try
        {
            await binding.WaitForEntryAsync();
            AssertState(MessageReadinessState.Connecting);
            _channel.Verify(c => c.BasicPublishAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        finally
        {
            binding.Complete();
            await publish.WaitAsync(TestTimeout);
        }
        AssertState(MessageReadinessState.Ready);
        _channel.Verify(c => c.BasicPublishAsync("invoices", "invoice.created", true,
            It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ConnectionCreationFailure_IsUnavailable_AndNextPublishCanRecover()
    {
        var failure = new InvalidOperationException("Test failure");
        _factory.Setup(f => f.CreateConnectionAsync(It.IsAny<CancellationToken>())).ThrowsAsync(failure);

        await AssertFailureAndRecoveryAsync();
    }

    [Test]
    public async Task ChannelCreationFailure_IsUnavailable_AndNextPublishCanRecover()
    {
        var failure = new InvalidOperationException("Test failure");
        _connection.Setup(c => c.CreateChannelAsync(null, It.IsAny<CancellationToken>())).ThrowsAsync(failure);

        await AssertFailureAndRecoveryAsync();
    }

    [Test]
    public async Task ExchangeDeclarationFailure_IsUnavailable_AndNextPublishCanRecover()
    {
        var failure = new InvalidOperationException("Test failure");
        _channel.Setup(c => c.ExchangeDeclareAsync("invoices", "topic", true, false, null, false, false,
            It.IsAny<CancellationToken>())).ThrowsAsync(failure);

        await AssertFailureAndRecoveryAsync();
    }

    [Test]
    public async Task QueueDeclarationFailure_IsUnavailable_AndNextPublishCanRecover()
    {
        var failure = new InvalidOperationException("Test failure");
        _channel.Setup(c => c.QueueDeclareAsync("invoices", true, false, false, null, false, false,
            It.IsAny<CancellationToken>())).ThrowsAsync(failure);

        await AssertFailureAndRecoveryAsync();
    }

    [Test]
    public async Task QueueBindingFailure_IsUnavailable_AndNextPublishCanRecover()
    {
        var failure = new InvalidOperationException("Test failure");
        _channel.Setup(c => c.QueueBindAsync("invoices", "invoices", "invoice.*", null, false,
            It.IsAny<CancellationToken>())).ThrowsAsync(failure);

        await AssertFailureAndRecoveryAsync();
    }

    [Test]
    public async Task PublishFailure_IsUnavailable_AndNextPublishCanRecover()
    {
        var failure = new InvalidOperationException("Test failure");
        _channel.Setup(c => c.BasicPublishAsync("invoices", "invoice.created", true,
            It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);

        await AssertFailureAndRecoveryAsync();
    }

    private async Task AssertFailureAndRecoveryAsync()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _publisher.PublishInvoiceEventAsync("created", new { Id = 1 }).WaitAsync(TestTimeout));
        AssertState(MessageReadinessState.Unavailable);

        _channel.Reset();
        _channel.SetupGet(c => c.IsOpen).Returns(true);
        _factory.Setup(f => f.CreateConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_connection.Object);
        _connection.Setup(c => c.CreateChannelAsync(null, It.IsAny<CancellationToken>())).ReturnsAsync(_channel.Object);
        await _publisher.PublishInvoiceEventAsync("created", new { Id = 2 }).WaitAsync(TestTimeout);
        AssertState(MessageReadinessState.Ready);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Shutdown_IsUnavailable_UntilTopologyIsInitializedAgain(bool connectionLost)
    {
        await _publisher.PublishInvoiceEventAsync("created", new { Id = 1 }).WaitAsync(TestTimeout);
        var shutdown = new ShutdownEventArgs(ShutdownInitiator.Peer, 320, "Test shutdown");
        if (connectionLost)
            await _connection.RaiseAsync(c => c.ConnectionShutdownAsync += null, _connection.Object, shutdown).WaitAsync(TestTimeout);
        else
            await _channel.RaiseAsync(c => c.ChannelShutdownAsync += null, _channel.Object, shutdown).WaitAsync(TestTimeout);
        AssertState(MessageReadinessState.Unavailable);
        await _publisher.PublishInvoiceEventAsync("created", new { Id = 2 }).WaitAsync(TestTimeout);
        AssertState(MessageReadinessState.Ready);
        _channel.Verify(c => c.QueueBindAsync("invoices", "invoices", "invoice.*", null, false,
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Test]
    public async Task Cleanup_IsUnavailableWhileDisposalIsPending()
    {
        await _publisher.PublishInvoiceEventAsync("created", new { Id = 1 }).WaitAsync(TestTimeout);
        var disposal = new PendingOperation();
        _channel.Setup(c => c.DisposeAsync()).Returns(() => new ValueTask(disposal.EnterAsync()));
        var cleanup = _publisher.DisposeAsync();
        try
        {
            await disposal.WaitForEntryAsync();
            AssertState(MessageReadinessState.Unavailable);
        }
        finally
        {
            disposal.Complete();
            await cleanup.AsTask().WaitAsync(TestTimeout);
        }
    }

    [Test]
    public async Task Cleanup_IsUnavailable_AndCannotReinitialize()
    {
        await _publisher.PublishInvoiceEventAsync("created", new { Id = 1 }).WaitAsync(TestTimeout);
        await _publisher.DisposeAsync().AsTask().WaitAsync(TestTimeout);
        AssertState(MessageReadinessState.Unavailable);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => _publisher.PublishInvoiceEventAsync("created", new { Id = 2 }).WaitAsync(TestTimeout));
        AssertState(MessageReadinessState.Unavailable);
    }

    [Test]
    public async Task Publishing_WaitsForInitialization()
    {
        var operation = new PendingOperation();
        _channel.Setup(c => c.QueueBindAsync("invoices", "invoices", "invoice.*", null, false,
            It.IsAny<CancellationToken>())).Returns(operation.EnterAsync);
        var holding = _publisher.InitializeAsync();
        Task? waiting = null;
        try
        {
            await operation.WaitForEntryAsync();
            waiting = _publisher.PublishInvoiceEventAsync("created", new { Id = 1 });
            Assert.That(holding.IsCompleted, Is.False);
            Assert.That(waiting.IsCompleted, Is.False);
            AssertState(MessageReadinessState.Connecting);
            _factory.Verify(f => f.CreateConnectionAsync(It.IsAny<CancellationToken>()), Times.Once);
            _channel.Verify(c => c.BasicPublishAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()), Times.Never);
            _channel.Verify(c => c.DisposeAsync(), Times.Never);
        }
        finally
        {
            operation.Complete();
            try
            {
                await holding.WaitAsync(TestTimeout);
            }
            finally
            {
                if (waiting != null)
                    await waiting.WaitAsync(TestTimeout);
            }
        }
        AssertState(MessageReadinessState.Ready);
        _channel.Verify(c => c.BasicPublishAsync("invoices", "invoice.created", true,
            It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Disposal_WaitsForInitialization()
    {
        var operation = new PendingOperation();
        _channel.Setup(c => c.QueueBindAsync("invoices", "invoices", "invoice.*", null, false,
            It.IsAny<CancellationToken>())).Returns(operation.EnterAsync);
        var holding = _publisher.InitializeAsync();
        Task? waiting = null;
        try
        {
            await operation.WaitForEntryAsync();
            waiting = _publisher.DisposeAsync().AsTask();
            Assert.That(holding.IsCompleted, Is.False);
            Assert.That(waiting.IsCompleted, Is.False);
            AssertState(MessageReadinessState.Connecting);
            _factory.Verify(f => f.CreateConnectionAsync(It.IsAny<CancellationToken>()), Times.Once);
            _channel.Verify(c => c.BasicPublishAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()), Times.Never);
            _channel.Verify(c => c.DisposeAsync(), Times.Never);
        }
        finally
        {
            operation.Complete();
            try
            {
                await holding.WaitAsync(TestTimeout);
            }
            finally
            {
                if (waiting != null)
                    await waiting.WaitAsync(TestTimeout);
            }
        }
        AssertState(MessageReadinessState.Unavailable);
        _channel.Verify(c => c.DisposeAsync(), Times.Once);
        _connection.Verify(c => c.DisposeAsync(), Times.Once);
    }

    [Test]
    public async Task Initialization_WaitsForPublishing()
    {
        await _publisher.InitializeAsync().WaitAsync(TestTimeout);
        var operation = new PendingOperation();
        _channel.Setup(c => c.BasicPublishAsync("invoices", "invoice.created", true,
            It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .Returns(() => new ValueTask(operation.EnterAsync()));
        var holding = _publisher.PublishInvoiceEventAsync("created", new { Id = 1 });
        Task? waiting = null;
        try
        {
            await operation.WaitForEntryAsync();
            waiting = _publisher.InitializeAsync();
            Assert.That(holding.IsCompleted, Is.False);
            Assert.That(waiting.IsCompleted, Is.False);
            _factory.Verify(f => f.CreateConnectionAsync(It.IsAny<CancellationToken>()), Times.Once);
            _channel.Verify(c => c.DisposeAsync(), Times.Never);
        }
        finally
        {
            operation.Complete();
            try
            {
                await holding.WaitAsync(TestTimeout);
            }
            finally
            {
                if (waiting != null)
                    await waiting.WaitAsync(TestTimeout);
            }
        }
        _factory.Verify(f => f.CreateConnectionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Disposal_WaitsForPublishing()
    {
        await _publisher.InitializeAsync().WaitAsync(TestTimeout);
        var operation = new PendingOperation();
        _channel.Setup(c => c.BasicPublishAsync("invoices", "invoice.created", true,
            It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .Returns(() => new ValueTask(operation.EnterAsync()));
        var holding = _publisher.PublishInvoiceEventAsync("created", new { Id = 1 });
        Task? waiting = null;
        try
        {
            await operation.WaitForEntryAsync();
            waiting = _publisher.DisposeAsync().AsTask();
            Assert.That(holding.IsCompleted, Is.False);
            Assert.That(waiting.IsCompleted, Is.False);
            _factory.Verify(f => f.CreateConnectionAsync(It.IsAny<CancellationToken>()), Times.Once);
            _channel.Verify(c => c.DisposeAsync(), Times.Never);
        }
        finally
        {
            operation.Complete();
            try
            {
                await holding.WaitAsync(TestTimeout);
            }
            finally
            {
                if (waiting != null)
                    await waiting.WaitAsync(TestTimeout);
            }
        }
        AssertState(MessageReadinessState.Unavailable);
        _channel.Verify(c => c.DisposeAsync(), Times.Once);
        _connection.Verify(c => c.DisposeAsync(), Times.Once);
    }

    [Test]
    public async Task Initialization_WaitsForDisposal_ThenThrowsDisposed()
    {
        await _publisher.InitializeAsync().WaitAsync(TestTimeout);
        var operation = new PendingOperation();
        _channel.Setup(c => c.DisposeAsync()).Returns(() => new ValueTask(operation.EnterAsync()));
        var holding = _publisher.DisposeAsync().AsTask();
        Task? waiting = null;
        try
        {
            await operation.WaitForEntryAsync();
            waiting = _publisher.InitializeAsync();
            Assert.That(holding.IsCompleted, Is.False);
            Assert.That(waiting.IsCompleted, Is.False);
            AssertState(MessageReadinessState.Unavailable);
            _factory.Verify(f => f.CreateConnectionAsync(It.IsAny<CancellationToken>()), Times.Once);
            _channel.Verify(c => c.BasicPublishAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        finally
        {
            operation.Complete();
            try
            {
                await holding.WaitAsync(TestTimeout);
            }
            finally
            {
                if (waiting != null)
                    await Assert.ThrowsAsync<ObjectDisposedException>(() => waiting.WaitAsync(TestTimeout));
            }
        }
        _factory.Verify(f => f.CreateConnectionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Publishing_WaitsForDisposal_ThenThrowsDisposed()
    {
        await _publisher.InitializeAsync().WaitAsync(TestTimeout);
        var operation = new PendingOperation();
        _channel.Setup(c => c.DisposeAsync()).Returns(() => new ValueTask(operation.EnterAsync()));
        var holding = _publisher.DisposeAsync().AsTask();
        Task? waiting = null;
        try
        {
            await operation.WaitForEntryAsync();
            waiting = _publisher.PublishInvoiceEventAsync("created", new { Id = 1 });
            Assert.That(holding.IsCompleted, Is.False);
            Assert.That(waiting.IsCompleted, Is.False);
            AssertState(MessageReadinessState.Unavailable);
            _factory.Verify(f => f.CreateConnectionAsync(It.IsAny<CancellationToken>()), Times.Once);
            _channel.Verify(c => c.BasicPublishAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        finally
        {
            operation.Complete();
            try
            {
                await holding.WaitAsync(TestTimeout);
            }
            finally
            {
                if (waiting != null)
                    await Assert.ThrowsAsync<ObjectDisposedException>(() => waiting.WaitAsync(TestTimeout));
            }
        }
        _factory.Verify(f => f.CreateConnectionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task CallerCancellation_WhileWaitingForLock_PreservesReady()
    {
        await _publisher.InitializeAsync().WaitAsync(TestTimeout);
        var operation = new PendingOperation();
        _channel.Setup(c => c.BasicPublishAsync("invoices", "invoice.created", true,
            It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .Returns(() => new ValueTask(operation.EnterAsync()));
        using var cancellation = new CancellationTokenSource();
        var holder = _publisher.PublishInvoiceEventAsync("created", new { Id = 1 });
        Task? waiter = null;
        try
        {
            await operation.WaitForEntryAsync();
            waiter = _publisher.PublishInvoiceEventAsync("created", new { Id = 2 }, cancellation.Token);
            Assert.That(waiter.IsCompleted, Is.False);
            cancellation.Cancel();
            await Assert.CatchAsync<OperationCanceledException>(() => waiter.WaitAsync(TestTimeout));
            AssertState(MessageReadinessState.Ready);
            _channel.Verify(c => c.BasicPublishAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            cancellation.Cancel();
            operation.Complete();
            try
            {
                await holder.WaitAsync(TestTimeout);
            }
            finally
            {
                if (waiter != null)
                    await Assert.CatchAsync<OperationCanceledException>(() => waiter.WaitAsync(TestTimeout));
            }
        }
    }

    [Test]
    public async Task CallerCancellation_DuringHealthyPublish_PreservesReady()
    {
        await _publisher.InitializeAsync().WaitAsync(TestTimeout);
        using var cancellation = new CancellationTokenSource();
        var operation = new PendingOperation();
        _channel.Setup(c => c.BasicPublishAsync("invoices", "invoice.created", true,
            It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), cancellation.Token))
            .Returns(() => new ValueTask(operation.EnterAsync()));
        var publish = _publisher.PublishInvoiceEventAsync("created", new { Id = 1 }, cancellation.Token);
        try
        {
            await operation.WaitForEntryAsync();
            cancellation.Cancel();
        }
        finally
        {
            cancellation.Cancel();
            operation.Cancel(cancellation.Token);
            await Assert.CatchAsync<OperationCanceledException>(() => publish.WaitAsync(TestTimeout));
        }
        AssertState(MessageReadinessState.Ready);
        _channel.Verify(c => c.BasicPublishAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
            It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task CallerCancellation_DuringPublish_AfterShutdown_RemainsUnavailable(bool connectionLost)
    {
        await _publisher.InitializeAsync().WaitAsync(TestTimeout);
        using var cancellation = new CancellationTokenSource();
        var operation = new PendingOperation();
        _channel.Setup(c => c.BasicPublishAsync("invoices", "invoice.created", true,
            It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), cancellation.Token))
            .Returns(() => new ValueTask(operation.EnterAsync()));
        var publish = _publisher.PublishInvoiceEventAsync("created", new { Id = 1 }, cancellation.Token);
        try
        {
            await operation.WaitForEntryAsync();
            cancellation.Cancel();
            var reason = new ShutdownEventArgs(ShutdownInitiator.Peer, 320, "Test shutdown");
            if (connectionLost)
                await _connection.RaiseAsync(c => c.ConnectionShutdownAsync += null, _connection.Object, reason).WaitAsync(TestTimeout);
            else
                await _channel.RaiseAsync(c => c.ChannelShutdownAsync += null, _channel.Object, reason).WaitAsync(TestTimeout);
            AssertState(MessageReadinessState.Unavailable);
        }
        finally
        {
            cancellation.Cancel();
            operation.Cancel(cancellation.Token);
            await Assert.CatchAsync<OperationCanceledException>(() => publish.WaitAsync(TestTimeout));
        }
        AssertState(MessageReadinessState.Unavailable);
        _channel.Verify(c => c.BasicPublishAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
            It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task CallerCancellation_DuringPublish_WithClosedResource_IsUnavailable(bool connectionClosed)
    {
        await _publisher.InitializeAsync().WaitAsync(TestTimeout);
        using var cancellation = new CancellationTokenSource();
        var operation = new PendingOperation();
        _channel.Setup(c => c.BasicPublishAsync("invoices", "invoice.created", true,
            It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), cancellation.Token))
            .Returns(() => new ValueTask(operation.EnterAsync()));
        var publish = _publisher.PublishInvoiceEventAsync("created", new { Id = 1 }, cancellation.Token);
        try
        {
            await operation.WaitForEntryAsync();
            cancellation.Cancel();
            if (connectionClosed) _connection.SetupGet(c => c.IsOpen).Returns(false);
            else _channel.SetupGet(c => c.IsOpen).Returns(false);
        }
        finally
        {
            cancellation.Cancel();
            operation.Cancel(cancellation.Token);
            await Assert.CatchAsync<OperationCanceledException>(() => publish.WaitAsync(TestTimeout));
        }
        AssertState(MessageReadinessState.Unavailable);
        _channel.Verify(c => c.BasicPublishAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
            It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task PublishFailure_WithOrWithoutCallerCancellation_IsUnavailable(bool cancelCaller)
    {
        await _publisher.InitializeAsync().WaitAsync(TestTimeout);
        using var cancellation = new CancellationTokenSource();
        var operation = new PendingOperation();
        _channel.Setup(c => c.BasicPublishAsync("invoices", "invoice.created", true,
            It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), cancellation.Token))
            .Returns(() => new ValueTask(operation.EnterAsync()));
        var publish = _publisher.PublishInvoiceEventAsync("created", new { Id = 1 }, cancellation.Token);
        try
        {
            await operation.WaitForEntryAsync();
            if (cancelCaller) cancellation.Cancel();
        }
        finally
        {
            operation.Fail(new InvalidOperationException("Publish failed"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => publish.WaitAsync(TestTimeout));
        }
        AssertState(MessageReadinessState.Unavailable);
    }

    [Test]
    public async Task PublishCancellation_WithoutCallerCancellation_IsUnavailable()
    {
        await _publisher.InitializeAsync().WaitAsync(TestTimeout);
        _channel.Setup(c => c.BasicPublishAsync("invoices", "invoice.created", true,
            It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        await Assert.CatchAsync<OperationCanceledException>(() =>
            _publisher.PublishInvoiceEventAsync("created", new { Id = 1 }).WaitAsync(TestTimeout));
        AssertState(MessageReadinessState.Unavailable);
    }

    [Test]
    public async Task InitializationCancellation_DuringChannelCreation_CleansPartialResources_AndNeverBecomesReady()
    {
        using var cancellation = new CancellationTokenSource();
        var operation = new PendingOperation();
        _connection.Setup(c => c.CreateChannelAsync(null, cancellation.Token)).Returns(async () =>
        {
            await operation.EnterAsync();
            return _channel.Object;
        });
        var initialization = _publisher.InitializeAsync(cancellation.Token);
        try
        {
            await operation.WaitForEntryAsync();
            AssertState(MessageReadinessState.Connecting);
            cancellation.Cancel();
        }
        finally
        {
            cancellation.Cancel();
            operation.Cancel(cancellation.Token);
            await Assert.CatchAsync<OperationCanceledException>(() => initialization.WaitAsync(TestTimeout));
        }
        AssertState(MessageReadinessState.Unavailable);
        _connection.Verify(c => c.DisposeAsync(), Times.Once);
        _channel.Verify(c => c.DisposeAsync(), Times.Never);
        _channel.Verify(c => c.BasicPublishAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
            It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task InitializationCancellation_DuringBinding_CleansPartialResources_AndNeverBecomesReady()
    {
        using var cancellation = new CancellationTokenSource();
        var operation = new PendingOperation();
        _channel.Setup(c => c.QueueBindAsync("invoices", "invoices", "invoice.*", null, false,
            cancellation.Token)).Returns(operation.EnterAsync);
        var initialization = _publisher.InitializeAsync(cancellation.Token);
        try
        {
            await operation.WaitForEntryAsync();
            AssertState(MessageReadinessState.Connecting);
            cancellation.Cancel();
        }
        finally
        {
            cancellation.Cancel();
            operation.Cancel(cancellation.Token);
            await Assert.CatchAsync<OperationCanceledException>(() => initialization.WaitAsync(TestTimeout));
        }
        AssertState(MessageReadinessState.Unavailable);
        _connection.Verify(c => c.DisposeAsync(), Times.Once);
        _channel.Verify(c => c.DisposeAsync(), Times.Once);
        _channel.Verify(c => c.BasicPublishAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
            It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task InitializationCancellation_WhenBindingCompletesAfterCancellation_CleansPartialResources_AndNeverBecomesReady()
    {
        using var cancellation = new CancellationTokenSource();
        var operation = new PendingOperation();
        _channel.Setup(c => c.QueueBindAsync("invoices", "invoices", "invoice.*", null, false,
            cancellation.Token)).Returns(operation.EnterAsync);
        var initialization = _publisher.InitializeAsync(cancellation.Token);
        try
        {
            await operation.WaitForEntryAsync();
            AssertState(MessageReadinessState.Connecting);
            cancellation.Cancel();
        }
        finally
        {
            cancellation.Cancel();
            operation.Complete();
            await Assert.CatchAsync<OperationCanceledException>(() => initialization.WaitAsync(TestTimeout));
        }
        AssertState(MessageReadinessState.Unavailable);
        _connection.Verify(c => c.DisposeAsync(), Times.Once);
        _channel.Verify(c => c.DisposeAsync(), Times.Once);
        _channel.Verify(c => c.BasicPublishAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
            It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private void AssertState(MessageReadinessState expected)
    {
        Assert.That(_readiness.State, Is.EqualTo(expected));
        Assert.That(_readiness.CanWriteInvoices, Is.EqualTo(expected == MessageReadinessState.Ready));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SerializationFailure_PreservesState_WithoutMessagingOperations(bool initialized)
    {
        if (initialized) await _publisher.InitializeAsync().WaitAsync(TestTimeout);
        var initialState = _readiness.State;
        var invalidPayload = new List<object>();
        invalidPayload.Add(invalidPayload);

        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() =>
            _publisher.PublishInvoiceEventAsync("created", invalidPayload).WaitAsync(TestTimeout));

        AssertState(initialState);
        _factory.Verify(f => f.CreateConnectionAsync(It.IsAny<CancellationToken>()),
            initialized ? Times.Once : Times.Never);
        _channel.Verify(c => c.BasicPublishAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
            It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
