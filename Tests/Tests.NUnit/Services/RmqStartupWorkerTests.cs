using DataLayer.Services;
using InvoiceCoreApp.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using RabbitMQ.Client;

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

    [TestCase(0)]
    [TestCase(5)]
    public async Task Startup_RetriesUntilSuccess_ThenFinishes(int failures)
    {
        var attempts = 0;
        var delays = new List<TimeSpan>();
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
                Assert.That(_publisher.State, Is.EqualTo(MessageReadinessState.Unavailable));
                delays.Add(delay);
                return Task.CompletedTask;
            });
        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!.WaitAsync(Timeout);
        Assert.That(attempts, Is.EqualTo(failures + 1));
        Assert.That(delays, Is.EqualTo(new[] { 1, 2, 5, 10, 10 }.Take(failures).Select(seconds => TimeSpan.FromSeconds(seconds))));
        Assert.That(_publisher.State, Is.EqualTo(MessageReadinessState.Ready));
        await worker.StopAsync(CancellationToken.None);
        _channel.Verify(c => c.DisposeAsync(), Times.Never);
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
            await worker.ExecuteTask.WaitAsync(Timeout);
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
