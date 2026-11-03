using DataLayer.Services;
using DataLayer.Models;
using InvoiceCoreApp.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Tests.NUnit.Infrastructure;
using System.Threading.Channels;

namespace Tests.NUnit.Services;

[TestFixture]
public class RmqStartupWorkerTests
{
    private Mock<IConnectionFactory> _factory = null!;
    private Mock<IConnection> _connection = null!;
    private Mock<IChannel> _channel = null!;
    private RmqPublisherService _publisher = null!;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [SetUp]
    public void SetUp()
    {
        _factory = new Mock<IConnectionFactory>();
        _connection = new Mock<IConnection>();
        _channel = new Mock<IChannel>();
        _connection.SetupGet(c => c.IsOpen).Returns(true);
        _channel.SetupGet(c => c.IsOpen).Returns(true);
        _connection.Setup(c => c.CreateChannelAsync(null, It.IsAny<CancellationToken>())).ReturnsAsync(_channel.Object);
        _factory.Setup(f => f.CreateConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_connection.Object);
        _publisher = new RmqPublisherService(_factory.Object, Options.Create(new RmqSettings
        {
            ConnectionString = "amqp://unused", UserName = "", UserPassword = "",
            ExchangeName = "invoices", QueueName = "invoices", RoutingKey = "invoice.*"
        }), NullLogger<RmqPublisherService>.Instance);
    }

    [TearDown]
    public async Task TearDown() => await _publisher.DisposeAsync();

    [TestCase(true)]
    [TestCase(false)]
    public async Task Outage_RejectsInvoiceWrite_ThenWorkerRestoresWrites(bool connectionLost)
    {
        await using var fixture = new SqliteInvoiceFixture();
        await fixture.SeedAsync();
        var service = new SqLiteInvoiceService(fixture.Db, _publisher, _publisher);
        var delays = new ControlledDelays();
        using var worker = new RmqStartupWorker(_publisher, NullLogger<RmqStartupWorker>.Instance, delays.WaitAsync);
        try
        {
            await worker.StartAsync(CancellationToken.None).WaitAsync(Timeout);
            var healthyWait = await delays.NextAsync();
            Assert.That(healthyWait.Duration, Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(_publisher.State, Is.EqualTo(MessageReadinessState.Ready));
            await ShutdownAsync(connectionLost);
            Assert.That(_publisher.State, Is.EqualTo(MessageReadinessState.Unavailable));
            var before = await fixture.SnapshotAsync();
            var reads = fixture.Commands.Reads;
            await Assert.ThrowsAsync<MessagingUnavailableException>(() =>
                service.AddAsync(new Invoice { Description = "rejected" }));
            Assert.That(fixture.Commands.Reads, Is.EqualTo(reads));
            Assert.That(fixture.Db.ChangeTracker.Entries(), Is.Empty);
            Assert.That(await fixture.SnapshotAsync(), Is.EqualTo(before));
            VerifyPublishes(Times.Never());

            healthyWait.Completion.SetResult();
            var recoveredWait = await delays.NextAsync();
            Assert.That(recoveredWait.Duration, Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(_publisher.State, Is.EqualTo(MessageReadinessState.Ready));
            _factory.Verify(f => f.CreateConnectionAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
            _channel.Verify(c => c.ExchangeDeclareAsync("invoices", "topic", true, false, null, false, false,
                It.IsAny<CancellationToken>()), Times.Exactly(2));
            _channel.Verify(c => c.QueueDeclareAsync("invoices", true, false, false, null, false, false,
                It.IsAny<CancellationToken>()), Times.Exactly(2));
            _channel.Verify(c => c.QueueBindAsync("invoices", "invoices", "invoice.*", null, false,
                It.IsAny<CancellationToken>()), Times.Exactly(2));
            await service.AddAsync(new Invoice { Description = "accepted" });
            Assert.That(await service.GetAllAsync(), Has.Count.EqualTo(2));
            VerifyPublishes(Times.Once());
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None).WaitAsync(Timeout);
        }
    }

    private async Task ShutdownAsync(bool connectionLost = true)
    {
        var shutdown = new ShutdownEventArgs(ShutdownInitiator.Peer, 320, "Test outage");
        if (connectionLost)
            await _connection.RaiseAsync(c => c.ConnectionShutdownAsync += null, _connection.Object, shutdown);
        else
            await _channel.RaiseAsync(c => c.ChannelShutdownAsync += null, _channel.Object, shutdown);
    }

    private void VerifyPublishes(Times times) =>
        _channel.Verify(c => c.BasicPublishAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
            It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()), times);

    private sealed class ControlledDelays
    {
        private readonly Channel<(TimeSpan Duration, TaskCompletionSource Completion)> _waits =
            Channel.CreateUnbounded<(TimeSpan, TaskCompletionSource)>();

        public Task WaitAsync(TimeSpan duration, CancellationToken token)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waits.Writer.TryWrite((duration, completion));
            return completion.Task.WaitAsync(token);
        }

        public Task<(TimeSpan Duration, TaskCompletionSource Completion)> NextAsync() =>
            _waits.Reader.ReadAsync().AsTask().WaitAsync(Timeout);
    }

    [TestCase(0)]
    [TestCase(5)]
    public async Task Startup_RetriesUntilReady_ThenWaitsUntilShutdown(int failures)
    {
        var attempts = 0;
        var delays = new List<TimeSpan>();
        var healthyWait = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _factory.Setup(f => f.CreateConnectionAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken token) =>
            {
                Assert.That(token.CanBeCanceled, Is.True);
                Assert.That(_publisher.State, Is.EqualTo(MessageReadinessState.Connecting));
                return ++attempts <= failures
                    ? Task.FromException<IConnection>(new InvalidOperationException("Broker unavailable"))
                    : Task.FromResult(_connection.Object);
            });
        using var worker = new RmqStartupWorker(_publisher, NullLogger<RmqStartupWorker>.Instance,
            (delay, token) =>
            {
                Assert.That(token.CanBeCanceled, Is.True);
                if (_publisher.State == MessageReadinessState.Ready)
                {
                    Assert.That(delay, Is.EqualTo(TimeSpan.FromSeconds(5)));
                    healthyWait.TrySetResult();
                    return Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
                }
                Assert.That(_publisher.State, Is.EqualTo(MessageReadinessState.Unavailable));
                delays.Add(delay);
                return Task.CompletedTask;
            });
        try
        {
            await worker.StartAsync(CancellationToken.None).WaitAsync(Timeout);
            await healthyWait.Task.WaitAsync(Timeout);
            Assert.That(worker.ExecuteTask!.IsCompleted, Is.False);
            Assert.That(attempts, Is.EqualTo(failures + 1));
            Assert.That(delays, Is.EqualTo(new[] { 1, 2, 5, 10, 10 }.Take(failures).Select(seconds => TimeSpan.FromSeconds(seconds))));
            Assert.That(_publisher.State, Is.EqualTo(MessageReadinessState.Ready));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None).WaitAsync(Timeout);
        }
        await worker.ExecuteTask!.WaitAsync(Timeout);
        Assert.That(attempts, Is.EqualTo(failures + 1));
        _channel.Verify(c => c.DisposeAsync(), Times.Never);
    }

    [Test]
    public async Task Recovery_RetriesFailures_AndResetsBackoffAfterSuccess()
    {
        var attempts = 0;
        var failuresRemaining = 0;
        _factory.Setup(f => f.CreateConnectionAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken _) =>
            {
                Interlocked.Increment(ref attempts);
                return failuresRemaining-- > 0
                    ? Task.FromException<IConnection>(new InvalidOperationException("Test outage"))
                    : Task.FromResult(_connection.Object);
            });
        var delays = new ControlledDelays();
        using var worker = new RmqStartupWorker(_publisher, NullLogger<RmqStartupWorker>.Instance, delays.WaitAsync);
        try
        {
            await worker.StartAsync(CancellationToken.None).WaitAsync(Timeout);
            var healthyWait = await delays.NextAsync();
            foreach (var failureCount in new[] { 5, 1 })
            {
                await ShutdownAsync();
                failuresRemaining = failureCount;
                healthyWait.Completion.SetResult();
                foreach (var seconds in new[] { 1, 2, 5, 10, 10 }.Take(failureCount))
                {
                    var retry = await delays.NextAsync();
                    Assert.That(retry.Duration, Is.EqualTo(TimeSpan.FromSeconds(seconds)));
                    Assert.That(_publisher.State, Is.EqualTo(MessageReadinessState.Unavailable));
                    retry.Completion.SetResult();
                }
                healthyWait = await delays.NextAsync();
                Assert.That(healthyWait.Duration, Is.EqualTo(TimeSpan.FromSeconds(5)));
                Assert.That(_publisher.State, Is.EqualTo(MessageReadinessState.Ready));
            }
            Assert.That(attempts, Is.EqualTo(9));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None).WaitAsync(Timeout);
        }
        await worker.ExecuteTask!.WaitAsync(Timeout);
        Assert.That(attempts, Is.EqualTo(9));
    }

    [Test]
    public async Task Ready_DoesNotRecreateConnection_AndCancellationStopsHealthyWait()
    {
        var delays = new ControlledDelays();
        using var worker = new RmqStartupWorker(_publisher, NullLogger<RmqStartupWorker>.Instance, delays.WaitAsync);
        try
        {
            await worker.StartAsync(CancellationToken.None).WaitAsync(Timeout);
            for (var check = 0; check < 3; check++)
            {
                var wait = await delays.NextAsync();
                Assert.That(wait.Duration, Is.EqualTo(TimeSpan.FromSeconds(5)));
                Assert.That(_publisher.State, Is.EqualTo(MessageReadinessState.Ready));
                _factory.Verify(f => f.CreateConnectionAsync(It.IsAny<CancellationToken>()), Times.Once);
                wait.Completion.SetResult();
            }
            var pendingWait = await delays.NextAsync();
            await worker.StopAsync(CancellationToken.None).WaitAsync(Timeout);
            await worker.ExecuteTask!.WaitAsync(Timeout);
            await ShutdownAsync();
            pendingWait.Completion.SetResult();
            _factory.Verify(f => f.CreateConnectionAsync(It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None).WaitAsync(Timeout);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Cancellation_DuringAttemptOrDelay_StopsWithoutAnotherAttempt(bool duringDelay)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var delays = 0;
        _factory.Setup(f => f.CreateConnectionAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken token) =>
            {
                Interlocked.Increment(ref attempts);
                if (duringDelay) throw new InvalidOperationException("Broker unavailable");
                entered.SetResult();
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
                return _connection.Object;
            });
        using var worker = new RmqStartupWorker(_publisher, NullLogger<RmqStartupWorker>.Instance,
            (delay, token) =>
            {
                Interlocked.Increment(ref delays);
                entered.SetResult();
                return Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
            });
        await worker.StartAsync(CancellationToken.None).WaitAsync(Timeout);
        await entered.Task.WaitAsync(Timeout);
        await worker.StopAsync(CancellationToken.None).WaitAsync(Timeout);
        await worker.ExecuteTask!.WaitAsync(Timeout);
        Assert.That(attempts, Is.EqualTo(1));
        Assert.That(delays, Is.EqualTo(duringDelay ? 1 : 0));
        Assert.That(_publisher.State, Is.EqualTo(MessageReadinessState.Unavailable));
    }

    [Test]
    public async Task HostStartup_IsNonblocking_AndWorkerUsesPublishingSingleton()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var connectionReady = new TaskCompletionSource<IConnection>(TaskCreationOptions.RunContinuationsAsynchronously);
        _factory.Setup(f => f.CreateConnectionAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken token) =>
            {
                entered.SetResult();
                return connectionReady.Task.WaitAsync(token);
            });
        var builder = Host.CreateApplicationBuilder();
        var delays = new ControlledDelays();
        builder.Services.AddSingleton<Func<TimeSpan, CancellationToken, Task>>(delays.WaitAsync);
        builder.Services.AddSingleton(_factory.Object);
        builder.Services.AddSingleton(Options.Create(new RmqSettings
        {
            ConnectionString = "amqp://unused", UserName = "", UserPassword = "",
            ExchangeName = "invoices", QueueName = "invoices", RoutingKey = "invoice.*"
        }));
        builder.Services.AddRmqPublisher();
        using var host = builder.Build();
        try
        {
            await host.StartAsync().WaitAsync(Timeout);
            await entered.Task.WaitAsync(Timeout);
            var publisher = host.Services.GetRequiredService<RmqPublisherService>();
            Assert.That(host.Services.GetRequiredService<IMessagePublisherService>(), Is.SameAs(publisher));
            Assert.That(host.Services.GetRequiredService<IMessageReadiness>(), Is.SameAs(publisher));
            Assert.That(publisher.State, Is.EqualTo(MessageReadinessState.Connecting));
            var worker = host.Services.GetServices<IHostedService>().OfType<RmqStartupWorker>().Single();
            Assert.That(worker.ExecuteTask!.IsCompleted, Is.False);
            connectionReady.SetResult(_connection.Object);
            var healthyWait = await delays.NextAsync();
            Assert.That(healthyWait.Duration, Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(publisher.State, Is.EqualTo(MessageReadinessState.Ready));
            await publisher.PublishInvoiceEventAsync("created", new { Id = 1 });
            _factory.Verify(f => f.CreateConnectionAsync(It.IsAny<CancellationToken>()), Times.Once);
            _channel.Verify(c => c.QueueBindAsync("invoices", "invoices", "invoice.*", null, false,
                It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            connectionReady.TrySetCanceled();
            await host.StopAsync().WaitAsync(Timeout);
            await host.Services.GetRequiredService<RmqPublisherService>().DisposeAsync();
        }
    }
}
