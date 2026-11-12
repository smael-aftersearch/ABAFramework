namespace Aba.LocalMessaging;

/// <summary>Handlers implement idempotent inbox consumption in their owning context.</summary>
public interface IIntegrationEventHandler
{
    bool CanHandle(string eventType);
    Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken = default);
}

/// <summary>
/// Delivers messages at least once. If any subscriber fails, the producer retains
/// the message and successful consumers safely ignore its replay via their inbox.
/// In phase 1, dispatch occurs within one process (not through an external broker).
/// </summary>
public sealed class OutboxDispatcher
{
    private readonly IReadOnlyList<IOutboxSource> _sources;
    private readonly IReadOnlyList<IIntegrationEventHandler> _handlers;

    public OutboxDispatcher(IEnumerable<IOutboxSource> sources, IEnumerable<IIntegrationEventHandler> handlers)
    {
        _sources = sources.ToArray();
        _handlers = handlers.ToArray();
    }

    public async Task<int> DispatchOnceAsync(CancellationToken cancellationToken = default)
    {
        var delivered = 0;
        foreach (var source in _sources)
        {
            foreach (var message in await source.PeekDueAsync(100, cancellationToken))
            {
                try
                {
                    var consumers = _handlers.Where(h => h.CanHandle(message.EventType)).ToArray();
                    if (consumers.Length == 0)
                        throw new InvalidOperationException($"No subscriber for {message.EventType}.");
                    foreach (var consumer in consumers)
                        await consumer.HandleAsync(message, cancellationToken);
                    await source.AcknowledgeAsync(message.Id, cancellationToken);
                    delivered++;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    await source.RecordFailureAsync(message.Id, exception.Message, cancellationToken);
                }
            }
        }
        return delivered;
    }
}
