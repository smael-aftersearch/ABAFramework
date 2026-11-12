using System.Text.Json;
using Aba.Foundation;

namespace Aba.LocalMessaging;

public interface IOutboxSource
{
    string Name { get; }
    Task<IReadOnlyList<OutboxMessage>> PeekDueAsync(int limit, CancellationToken cancellationToken = default);
    Task AcknowledgeAsync(Guid messageId, CancellationToken cancellationToken = default);
    Task RecordFailureAsync(Guid messageId, string error, CancellationToken cancellationToken = default);
    Task<bool> ReplayDeadLetterAsync(Guid messageId, CancellationToken cancellationToken = default);
}

public sealed class StoreEnvelope<TState> where TState : class, new()
{
    public TState State { get; set; } = new();
    public List<OutboxMessage> Outbox { get; set; } = [];
    public HashSet<Guid> Inbox { get; set; } = [];
}

/// <summary>
/// PHASE-1 LOCAL DEVELOPMENT ADAPTER ONLY. A single JSON file atomically persists
/// one bounded context's state, inbox, and outbox. Single-process; no database,
/// multi-host locking, or production availability guarantees. Replace with
/// a context-owned PostgreSQL transaction before production.
/// </summary>
public sealed class DurableContextStore<TState> : IOutboxSource where TState : class, new()
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private readonly string _file;
    private readonly IClock _clock;

    public string Name { get; }

    public DurableContextStore(string name, string file, IClock clock)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Context name required.", nameof(name));
        Name = name;
        _file = file;
        _clock = clock;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_file))!);
    }

    public async Task<TResult> ReadAsync<TResult>(Func<TState, TResult> reader,
        CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadAsync(cancellationToken);
            return reader(state.State);
        }
        finally { _mutex.Release(); }
    }

    public async Task<TResult> ExecuteAsync<TResult>(Func<TState, List<OutboxMessage>, TResult> command,
        CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            var envelope = await LoadAsync(cancellationToken);
            var result = command(envelope.State, envelope.Outbox);
            await SaveAsync(envelope, cancellationToken);
            return result;
        }
        finally { _mutex.Release(); }
    }

    /// <summary>Business update + consumer inbox entry + produced events commit together.</summary>
    public async Task<bool> ConsumeOnceAsync(Guid messageId, Action<TState, List<OutboxMessage>> apply,
        CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            var envelope = await LoadAsync(cancellationToken);
            if (envelope.Inbox.Contains(messageId)) return false;
            apply(envelope.State, envelope.Outbox);
            envelope.Inbox.Add(messageId);
            await SaveAsync(envelope, cancellationToken);
            return true;
        }
        finally { _mutex.Release(); }
    }

    public async Task<IReadOnlyList<OutboxMessage>> PeekDueAsync(int limit,
        CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            var envelope = await LoadAsync(cancellationToken);
            return envelope.Outbox
                .Where(m => !m.DeadLettered && m.NextAttemptAtUtc <= _clock.UtcNow)
                .OrderBy(m => m.OccurredAtUtc)
                .Take(Math.Max(0, limit))
                .ToArray();
        }
        finally { _mutex.Release(); }
    }

    public async Task AcknowledgeAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            var envelope = await LoadAsync(cancellationToken);
            envelope.Outbox.RemoveAll(x => x.Id == messageId);
            await SaveAsync(envelope, cancellationToken);
        }
        finally { _mutex.Release(); }
    }

    public async Task RecordFailureAsync(Guid messageId, string error,
        CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            var envelope = await LoadAsync(cancellationToken);
            var message = envelope.Outbox.FirstOrDefault(x => x.Id == messageId);
            if (message is null) return;
            message.Attempts++;
            message.LastError = error.Length > 500 ? error[..500] : error;
            message.DeadLettered = message.Attempts >= 5;
            message.NextAttemptAtUtc = _clock.UtcNow.AddSeconds(Math.Min(60, 1 << Math.Min(message.Attempts, 5)));
            await SaveAsync(envelope, cancellationToken);
        }
        finally { _mutex.Release(); }
    }

    public async Task<bool> ReplayDeadLetterAsync(Guid messageId,
        CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            var envelope = await LoadAsync(cancellationToken);
            var message = envelope.Outbox.FirstOrDefault(x => x.Id == messageId && x.DeadLettered);
            if (message is null) return false;
            message.DeadLettered = false;
            message.Attempts = 0;
            message.LastError = null;
            message.NextAttemptAtUtc = _clock.UtcNow;
            await SaveAsync(envelope, cancellationToken);
            return true;
        }
        finally { _mutex.Release(); }
    }

    private async Task<StoreEnvelope<TState>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_file)) return new StoreEnvelope<TState>();
        await using var stream = File.OpenRead(_file);
        return await JsonSerializer.DeserializeAsync<StoreEnvelope<TState>>(stream, Json, cancellationToken)
            ?? throw new InvalidDataException($"Corrupt store: {_file}");
    }

    private async Task SaveAsync(StoreEnvelope<TState> envelope, CancellationToken cancellationToken)
    {
        var tmp = _file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, envelope, Json, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            // Atomic rename on the same local filesystem; not a distributed transaction.
            File.Move(tmp, _file, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }
}
