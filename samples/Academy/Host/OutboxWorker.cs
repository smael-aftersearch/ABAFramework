using Aba.LocalMessaging;

namespace Academy.Host;

/// <summary>Single-process development dispatcher; replace with durable broker worker.</summary>
public sealed class OutboxWorker(OutboxDispatcher dispatcher, ILogger<OutboxWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await dispatcher.DispatchOnceAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromMilliseconds(300), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Local outbox dispatcher failed; retrying.");
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
    }
}
