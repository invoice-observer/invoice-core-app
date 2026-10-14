using DataLayer.Services;

namespace InvoiceCoreApp.Services;

public sealed class RmqStartupWorker(
    RmqPublisherService publisher,
    ILogger<RmqStartupWorker> logger,
    Func<TimeSpan, CancellationToken, Task>? delay = null) : BackgroundService
{
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var retryIndex = 0;
        int[] retrySeconds = [1, 2, 5, 10];
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (publisher.State == MessageReadinessState.Ready)
                {
                    await _delay(TimeSpan.FromSeconds(5), stoppingToken);
                    continue;
                }

                logger.LogInformation("Starting RabbitMQ initialization attempt");
                try
                {
                    await publisher.InitializeAsync(stoppingToken);
                    retryIndex = 0;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception)
                {
                    stoppingToken.ThrowIfCancellationRequested();
                    var seconds = retrySeconds[retryIndex];
                    retryIndex = Math.Min(retryIndex + 1, retrySeconds.Length - 1);
                    // Do not log exception text: broker errors may contain connection credentials.
                    logger.LogWarning("RabbitMQ initialization failed; retrying in {DelaySeconds} seconds", seconds);
                    await _delay(TimeSpan.FromSeconds(seconds), stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown cancels the current attempt, healthy wait or retry delay.
        }
    }
}
