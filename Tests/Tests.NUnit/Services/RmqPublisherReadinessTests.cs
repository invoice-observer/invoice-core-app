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
    private Mock<IConnectionFactory> _factory = null!;
    private Mock<IConnection> _connection = null!;
    private Mock<IChannel> _channel = null!;
    private RmqPublisherService _publisher = null!;
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
    public async Task TearDown() => await _publisher.DisposeAsync();

    [Test]
    public async Task ExplicitInitialization_PreparesTopologyWithoutPublishing_AndReusesResources()
    {
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        await _publisher.InitializeAsync(token);
        await _publisher.InitializeAsync(token);
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
        await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.InitializeAsync());
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
        var binding = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _channel.Setup(c => c.QueueBindAsync("invoices", "invoices", "invoice.*", null, false,
            It.IsAny<CancellationToken>())).Returns(binding.Task);
        var publish = _publisher.PublishInvoiceEventAsync("created", new { Id = 1 });
        AssertState(MessageReadinessState.Connecting);
        binding.SetResult();
        await publish;
        AssertState(MessageReadinessState.Ready);
        _channel.Verify(c => c.ExchangeDeclareAsync("invoices", "topic", true, false, null, false, false,
            It.IsAny<CancellationToken>()), Times.Once);
        _channel.Verify(c => c.QueueDeclareAsync("invoices", true, false, false, null, false, false,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestCase("connection")]
    [TestCase("channel")]
    [TestCase("exchange")]
    [TestCase("queue")]
    [TestCase("binding")]
    [TestCase("publish")]
    public async Task Failure_IsUnavailable_AndNextPublishCanRecover(string stage)
    {
        var failure = new InvalidOperationException("Test failure");
        switch (stage)
        {
            case "connection":
                _factory.Setup(f => f.CreateConnectionAsync(It.IsAny<CancellationToken>())).ThrowsAsync(failure);
                break;
            case "channel":
                _connection.Setup(c => c.CreateChannelAsync(null, It.IsAny<CancellationToken>())).ThrowsAsync(failure);
                break;
            case "exchange":
                _channel.Setup(c => c.ExchangeDeclareAsync("invoices", "topic", true, false, null, false, false,
                    It.IsAny<CancellationToken>())).ThrowsAsync(failure);
                break;
            case "queue":
                _channel.Setup(c => c.QueueDeclareAsync("invoices", true, false, false, null, false, false,
                    It.IsAny<CancellationToken>())).ThrowsAsync(failure);
                break;
            case "binding":
                _channel.Setup(c => c.QueueBindAsync("invoices", "invoices", "invoice.*", null, false,
                    It.IsAny<CancellationToken>())).ThrowsAsync(failure);
                break;
            case "publish":
                _channel.Setup(c => c.BasicPublishAsync("invoices", "invoice.created", true,
                    It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                    .ThrowsAsync(failure);
                break;
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => _publisher.PublishInvoiceEventAsync("created", new { Id = 1 }));
        AssertState(MessageReadinessState.Unavailable);

        _channel.Reset();
        _channel.SetupGet(c => c.IsOpen).Returns(true);
        _factory.Setup(f => f.CreateConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_connection.Object);
        _connection.Setup(c => c.CreateChannelAsync(null, It.IsAny<CancellationToken>())).ReturnsAsync(_channel.Object);
        await _publisher.PublishInvoiceEventAsync("created", new { Id = 2 });
        AssertState(MessageReadinessState.Ready);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Shutdown_IsUnavailable_UntilTopologyIsInitializedAgain(bool connectionLost)
    {
        await _publisher.PublishInvoiceEventAsync("created", new { Id = 1 });
        var shutdown = new ShutdownEventArgs(ShutdownInitiator.Peer, 320, "Test shutdown");
        if (connectionLost)
            await _connection.RaiseAsync(c => c.ConnectionShutdownAsync += null, _connection.Object, shutdown);
        else
            await _channel.RaiseAsync(c => c.ChannelShutdownAsync += null, _channel.Object, shutdown);
        AssertState(MessageReadinessState.Unavailable);
        await _publisher.PublishInvoiceEventAsync("created", new { Id = 2 });
        AssertState(MessageReadinessState.Ready);
        _channel.Verify(c => c.QueueBindAsync("invoices", "invoices", "invoice.*", null, false,
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Test]
    public async Task Cleanup_IsUnavailableWhileDisposalIsPending()
    {
        await _publisher.PublishInvoiceEventAsync("created", new { Id = 1 });
        var disposal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _channel.Setup(c => c.DisposeAsync()).Returns(new ValueTask(disposal.Task));
        var cleanup = _publisher.DisposeAsync();
        try
        {
            AssertState(MessageReadinessState.Unavailable);
        }
        finally
        {
            disposal.SetResult();
            await cleanup;
        }
    }

    [Test]
    public async Task Cleanup_IsUnavailable_AndCannotReinitialize()
    {
        await _publisher.PublishInvoiceEventAsync("created", new { Id = 1 });
        await _publisher.DisposeAsync();
        AssertState(MessageReadinessState.Unavailable);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => _publisher.PublishInvoiceEventAsync("created", new { Id = 2 }));
        AssertState(MessageReadinessState.Unavailable);
    }

    [TestCase("initialization", "publishing")]
    [TestCase("initialization", "disposal")]
    [TestCase("publishing", "initialization")]
    [TestCase("publishing", "disposal")]
    [TestCase("disposal", "initialization")]
    [TestCase("disposal", "publishing")]
    public async Task InitializationPublishingAndDisposal_AreSerialized(string holder, string waiter)
    {
        var timeout = TimeSpan.FromSeconds(5);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (holder != "initialization") await _publisher.InitializeAsync().WaitAsync(timeout);
        switch (holder)
        {
            case "initialization":
                _channel.Setup(c => c.QueueBindAsync("invoices", "invoices", "invoice.*", null, false,
                    It.IsAny<CancellationToken>())).Returns(release.Task);
                break;
            case "publishing":
                _channel.Setup(c => c.BasicPublishAsync("invoices", "invoice.created", true,
                    It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask(release.Task));
                break;
            case "disposal":
                _channel.Setup(c => c.DisposeAsync()).Returns(new ValueTask(release.Task));
                break;
        }

        Task ExecuteAsync(string operation) => operation switch
        {
            "initialization" => _publisher.InitializeAsync(),
            "publishing" => _publisher.PublishInvoiceEventAsync("created", new { Id = 1 }),
            _ => _publisher.DisposeAsync().AsTask()
        };

        var holding = ExecuteAsync(holder);
        var waiting = ExecuteAsync(waiter);
        try
        {
            Assert.That(holding.IsCompleted, Is.False);
            Assert.That(waiting.IsCompleted, Is.False);
            if (holder == "initialization") AssertState(MessageReadinessState.Connecting);
            if (holder == "disposal") AssertState(MessageReadinessState.Unavailable);
            _factory.Verify(f => f.CreateConnectionAsync(It.IsAny<CancellationToken>()), Times.Once);
            if (holder != "publishing")
                _channel.Verify(c => c.BasicPublishAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
                    It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()), Times.Never);
            if (holder != "disposal") _channel.Verify(c => c.DisposeAsync(), Times.Never);
        }
        finally
        {
            release.TrySetResult();
            await holding.WaitAsync(timeout);
            if (holder == "disposal")
                await Assert.ThrowsAsync<ObjectDisposedException>(() => waiting.WaitAsync(timeout));
            else
                await waiting.WaitAsync(timeout);
        }
        _factory.Verify(f => f.CreateConnectionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task CallerCancellation_WhileWaitingForLock_PreservesReady()
    {
        await _publisher.InitializeAsync();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _channel.Setup(c => c.BasicPublishAsync("invoices", "invoice.created", true,
            It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask(release.Task));
        var holder = _publisher.PublishInvoiceEventAsync("created", new { Id = 1 });
        using var cancellation = new CancellationTokenSource();
        var waiter = _publisher.PublishInvoiceEventAsync("created", new { Id = 2 }, cancellation.Token);
        try
        {
            Assert.That(waiter.IsCompleted, Is.False);
            cancellation.Cancel();
            await Assert.CatchAsync<OperationCanceledException>(() => waiter);
            AssertState(MessageReadinessState.Ready);
            _channel.Verify(c => c.BasicPublishAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            release.TrySetResult();
            await holder;
        }
    }

    [TestCase("none")]
    [TestCase("connection")]
    [TestCase("channel")]
    [TestCase("closed connection")]
    [TestCase("closed channel")]
    public async Task CallerCancellation_DuringPublish_PreservesHealthOrShutdown(string shutdown)
    {
        await _publisher.InitializeAsync();
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _channel.Setup(c => c.BasicPublishAsync("invoices", "invoice.created", true,
            It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), cancellation.Token))
            .Returns(() => { entered.SetResult(); return new ValueTask(release.Task); });
        var publish = _publisher.PublishInvoiceEventAsync("created", new { Id = 1 }, cancellation.Token);
        await entered.Task;
        try
        {
            cancellation.Cancel();
            var reason = new ShutdownEventArgs(ShutdownInitiator.Peer, 320, "Test shutdown");
            if (shutdown == "connection")
                await _connection.RaiseAsync(c => c.ConnectionShutdownAsync += null, _connection.Object, reason);
            if (shutdown == "channel")
                await _channel.RaiseAsync(c => c.ChannelShutdownAsync += null, _channel.Object, reason);
            if (shutdown == "closed connection") _connection.SetupGet(c => c.IsOpen).Returns(false);
            if (shutdown == "closed channel") _channel.SetupGet(c => c.IsOpen).Returns(false);
        }
        finally
        {
            release.TrySetCanceled(cancellation.Token);
            await Assert.CatchAsync<OperationCanceledException>(() => publish);
        }
        AssertState(shutdown == "none" ? MessageReadinessState.Ready : MessageReadinessState.Unavailable);
        _factory.Verify(f => f.CreateConnectionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _channel.Verify(c => c.BasicPublishAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
            It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task PublishFailure_WithOrWithoutCallerCancellation_IsUnavailable(bool cancelCaller)
    {
        await _publisher.InitializeAsync();
        using var cancellation = new CancellationTokenSource();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _channel.Setup(c => c.BasicPublishAsync("invoices", "invoice.created", true,
            It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), cancellation.Token))
            .Returns(new ValueTask(release.Task));
        var publish = _publisher.PublishInvoiceEventAsync("created", new { Id = 1 }, cancellation.Token);
        if (cancelCaller) cancellation.Cancel();
        release.SetException(new InvalidOperationException("Publish failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => publish);
        AssertState(MessageReadinessState.Unavailable);
    }

    [Test]
    public async Task PublishCancellation_WithoutCallerCancellation_IsUnavailable()
    {
        await _publisher.InitializeAsync();
        _channel.Setup(c => c.BasicPublishAsync("invoices", "invoice.created", true,
            It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        await Assert.CatchAsync<OperationCanceledException>(() =>
            _publisher.PublishInvoiceEventAsync("created", new { Id = 1 }));
        AssertState(MessageReadinessState.Unavailable);
    }

    [TestCase("channel", false)]
    [TestCase("binding", false)]
    [TestCase("binding", true)]
    public async Task InitializationCancellation_CleansPartialResources_AndNeverBecomesReady(
        string stage, bool setupCompletesDespiteCancellation)
    {
        using var cancellation = new CancellationTokenSource();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var channel = new TaskCompletionSource<IChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (stage == "channel")
            _connection.Setup(c => c.CreateChannelAsync(null, cancellation.Token)).Returns(channel.Task);
        else
            _channel.Setup(c => c.QueueBindAsync("invoices", "invoices", "invoice.*", null, false,
                cancellation.Token)).Returns(release.Task);
        var initialization = _publisher.InitializeAsync(cancellation.Token);
        AssertState(MessageReadinessState.Connecting);
        cancellation.Cancel();
        if (stage == "channel") channel.SetCanceled(cancellation.Token);
        else if (setupCompletesDespiteCancellation) release.SetResult();
        else release.SetCanceled(cancellation.Token);

        await Assert.CatchAsync<OperationCanceledException>(() => initialization);
        AssertState(MessageReadinessState.Unavailable);
        _connection.Verify(c => c.DisposeAsync(), Times.Once);
        _channel.Verify(c => c.DisposeAsync(), stage == "channel" ? Times.Never : Times.Once);
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
        if (initialized) await _publisher.InitializeAsync();
        var initialState = _readiness.State;
        var invalidPayload = new List<object>();
        invalidPayload.Add(invalidPayload);

        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() =>
            _publisher.PublishInvoiceEventAsync("created", invalidPayload));

        AssertState(initialState);
        _factory.Verify(f => f.CreateConnectionAsync(It.IsAny<CancellationToken>()),
            initialized ? Times.Once : Times.Never);
        _channel.Verify(c => c.BasicPublishAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
            It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
