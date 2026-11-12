namespace Aba.Foundation;

/// <summary>A business invariant failed; safe to present as HTTP 400 in the demo.</summary>
public sealed class DomainRuleException(string message) : Exception(message)
{
}

/// <summary>Time is injectable so workflows can be tested deterministically.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
