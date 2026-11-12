using System.Text.Json;

namespace Aba.LocalMessaging;

/// <summary>
/// A serialized, versioned public event. The envelope itself carries delivery metadata,
/// keeping transport mechanics out of every business contract.
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; init; }
    public string EventType { get; init; } = "";
    public string Payload { get; init; } = "";
    public DateTimeOffset OccurredAtUtc { get; init; }
    public string CorrelationId { get; init; } = "";
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAtUtc { get; set; }
    public string? LastError { get; set; }
    public bool DeadLettered { get; set; }

    public static OutboxMessage Create<TEvent>(TEvent data, DateTimeOffset now, string correlationId = "")
        where TEvent : notnull
    {
        var name = typeof(TEvent).FullName
            ?? throw new InvalidOperationException("An event must have a full type name.");
        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = name,
            Payload = JsonSerializer.Serialize(data),
            OccurredAtUtc = now,
            NextAttemptAtUtc = now,
            CorrelationId = correlationId
        };
    }

    public TEvent Read<TEvent>() where TEvent : class
        => JsonSerializer.Deserialize<TEvent>(Payload)
            ?? throw new InvalidOperationException($"Event payload is invalid for {typeof(TEvent).FullName}.");
}

public static class EventType
{
    public static string Of<TEvent>() => typeof(TEvent).FullName
        ?? throw new InvalidOperationException("An event must have a full type name.");
}
