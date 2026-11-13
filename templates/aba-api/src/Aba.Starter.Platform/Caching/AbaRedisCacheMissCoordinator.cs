using System.Diagnostics;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Aba.Starter.Platform.Caching;

/// <summary>
/// Best-effort distributed cache-miss lock plus Redis-backed cluster-wide
/// concurrency and fixed-window source-load budgets. This is not a correctness
/// lock, tenant grant or business idempotency mechanism.
/// </summary>
public sealed class AbaRedisCacheMissCoordinator(
    IConnectionMultiplexer redis,
    AbaCacheDistributedCoordinationOptions options,
    ILogger<AbaRedisCacheMissCoordinator>? logger = null) : IAbaCacheMissCoordinator
{
    private const string RenewOwnedLease = """
        if redis.call('GET', KEYS[1]) == ARGV[1] then
            return redis.call('PEXPIRE', KEYS[1], ARGV[2])
        end
        return 0
        """;

    private const string ReleaseOwnedLease = """
        if redis.call('GET', KEYS[1]) == ARGV[1] then
            return redis.call('DEL', KEYS[1])
        end
        return 0
        """;

    private const string IncrementSourceBudget = """
        local window = tonumber(ARGV[1])
        local limit = tonumber(ARGV[2])
        local loads = redis.call('INCR', KEYS[1])
        if loads == 1 or redis.call('TTL', KEYS[1]) == -1 then
            redis.call('EXPIRE', KEYS[1], window)
        end
        if loads <= limit then return 1 else return 0 end
        """;

    private readonly IDatabase _database = redis.GetDatabase();
    private readonly TimeSpan _leaseDuration = TimeSpan.FromSeconds(options.LeaseDurationSeconds);
    private readonly long _acquireTimeoutMilliseconds =
        (long)TimeSpan.FromSeconds(options.AcquireTimeoutSeconds).TotalMilliseconds;
    private readonly SemaphoreSlim _configurationLock = new(1, 1);
    private bool _configurationValidated;

    public async Task VerifyReadinessAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await _database.PingAsync().ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        await EnsureSharedConfigurationAsync(ct).ConfigureAwait(false);
    }

    public async Task<IAbaCacheMissLease> AcquireAsync(string scopedCacheKey, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopedCacheKey);
        ct.ThrowIfCancellationRequested();
        if (!options.Enabled || string.IsNullOrWhiteSpace(options.ApplicationNamespace))
            throw new InvalidOperationException("Distributed cache coordination is not enabled.");
        await EnsureSharedConfigurationAsync(ct).ConfigureAwait(false);

        // ScopedCacheKey is already a one-way hash of the caller's scope and key.
        // Raw tenant identifiers and query keys never enter Redis key names.
        var lockKey = new RedisKey(
            $"aba:cache-coordination:v1:{options.ApplicationNamespace}:load:{scopedCacheKey}");
        var sourceKeys = Enumerable.Range(0, options.MaxConcurrentSourceLoads)
            .Select(index => new RedisKey(
                $"aba:cache-coordination:v1:{options.ApplicationNamespace}:source:{index}"))
            .ToArray();
        var token = Guid.NewGuid().ToString("N");
        var deadline = Stopwatch.GetTimestamp() +
            (long)(_acquireTimeoutMilliseconds * (double)Stopwatch.Frequency / 1000d);
        var lockOwned = false;
        RedisKey? sourceKeyOwned = null;

        try
        {
            while (Stopwatch.GetTimestamp() < deadline)
            {
                ct.ThrowIfCancellationRequested();
                var lockTaken = await _database.StringSetAsync(
                    lockKey, token, _leaseDuration, When.NotExists).ConfigureAwait(false);
                if (lockTaken)
                {
                    lockOwned = true;
                    foreach (var sourceKey in sourceKeys)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (await _database.StringSetAsync(
                                sourceKey, token, _leaseDuration, When.NotExists).ConfigureAwait(false))
                        {
                            sourceKeyOwned = sourceKey;
                            return new RedisLease(_database, lockKey, sourceKey, token,
                                _leaseDuration, logger);
                        }
                    }

                    await ReleaseIfOwnedAsync(lockKey, token).ConfigureAwait(false);
                    lockOwned = false;
                }

                var remaining = deadline - Stopwatch.GetTimestamp();
                if (remaining <= 0)
                    break;
                var delay = TimeSpan.FromMilliseconds(Math.Min(50,
                    remaining * 1000d / Stopwatch.Frequency));
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (lockOwned)
                await ReleaseIfOwnedAsync(lockKey, token).ConfigureAwait(false);
            if (sourceKeyOwned is { } ownedSource)
                await ReleaseIfOwnedAsync(ownedSource, token).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            if (lockOwned)
                await ReleaseIfOwnedAsync(lockKey, token).ConfigureAwait(false);
            if (sourceKeyOwned is { } ownedSource)
                await ReleaseIfOwnedAsync(ownedSource, token).ConfigureAwait(false);
            throw new AbaCacheMissCoordinationUnavailableException();
        }

        throw new AbaCacheMissCoordinationUnavailableException();
    }

    public async Task AcquireSourceLoadPermitAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!options.Enabled || string.IsNullOrWhiteSpace(options.ApplicationNamespace))
            throw new InvalidOperationException("Distributed cache coordination is not enabled.");
        await EnsureSharedConfigurationAsync(ct).ConfigureAwait(false);

        var budgetKey = new RedisKey(
            $"aba:cache-coordination:v1:{options.ApplicationNamespace}:source-load-window");
        try
        {
            var result = await _database.ScriptEvaluateAsync(IncrementSourceBudget,
                [budgetKey],
                [(RedisValue)options.SourceLoadWindowSeconds,
                    (RedisValue)options.MaxSourceLoadsPerWindow]).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if ((long)result != 1)
                throw new AbaDistributedCacheSourceLoadLimitExceededException();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (AbaDistributedCacheSourceLoadLimitExceededException)
        {
            throw;
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            throw new AbaCacheMissCoordinationUnavailableException();
        }
    }

    private async Task ReleaseIfOwnedAsync(RedisKey key, RedisValue token)
    {
        try
        {
            await _database.ScriptEvaluateAsync(ReleaseOwnedLease, [key], [token]).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            // The bounded lease TTL releases the slot if Redis is still down.
        }
    }

    private async Task EnsureSharedConfigurationAsync(CancellationToken ct)
    {
        if (Volatile.Read(ref _configurationValidated))
            return;
        await _configurationLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_configurationValidated)
                return;
            var key = new RedisKey(
                $"aba:cache-coordination:v1:{options.ApplicationNamespace}:settings");
            var expected = string.Join(':', options.MaxConcurrentSourceLoads,
                options.MaxSourceLoadsPerWindow, options.SourceLoadWindowSeconds,
                options.AcquireTimeoutSeconds, options.LeaseDurationSeconds);
            try
            {
                var created = await _database.StringSetAsync(
                    key, expected, when: When.NotExists).ConfigureAwait(false);
                if (!created && !string.Equals(
                        await _database.StringGetAsync(key).ConfigureAwait(false),
                        expected, StringComparison.Ordinal))
                    throw new AbaCacheCoordinationConfigurationMismatchException();
                Volatile.Write(ref _configurationValidated, true);
            }
            catch (AbaCacheCoordinationConfigurationMismatchException)
            {
                throw;
            }
            catch (Exception ex) when (ex is RedisException or TimeoutException)
            {
                throw new AbaCacheMissCoordinationUnavailableException();
            }
        }
        finally
        {
            _configurationLock.Release();
        }
    }

    private sealed class RedisLease : IAbaCacheMissLease
    {
        private readonly IDatabase _database;
        private readonly RedisKey _lockKey;
        private readonly RedisKey _sourceKey;
        private readonly RedisValue _token;
        private readonly TimeSpan _leaseDuration;
        private readonly ILogger? _logger;
        private readonly CancellationTokenSource _stopRenewal = new();
        private readonly CancellationTokenSource _leaseLost = new();
        private readonly Task _renewal;
        private int _disposed;

        public RedisLease(
            IDatabase database,
            RedisKey lockKey,
            RedisKey sourceKey,
            RedisValue token,
            TimeSpan leaseDuration,
            ILogger? logger)
        {
            _database = database;
            _lockKey = lockKey;
            _sourceKey = sourceKey;
            _token = token;
            _leaseDuration = leaseDuration;
            _logger = logger;
            _renewal = Task.Run(RenewAsync);
        }

        public CancellationToken LeaseLost => _leaseLost.Token;

        public void ThrowIfLost()
        {
            if (LeaseLost.IsCancellationRequested)
                throw new AbaCacheMissCoordinationUnavailableException();
        }

        private async Task RenewAsync()
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(
                Math.Max(500, _leaseDuration.TotalMilliseconds / 3)));
            try
            {
                while (await timer.WaitForNextTickAsync(_stopRenewal.Token).ConfigureAwait(false))
                {
                    var milliseconds = (long)_leaseDuration.TotalMilliseconds;
                    var lockRenewed = await _database.ScriptEvaluateAsync(
                        RenewOwnedLease, [_lockKey], [_token, milliseconds]).ConfigureAwait(false);
                    var sourceRenewed = await _database.ScriptEvaluateAsync(
                        RenewOwnedLease, [_sourceKey], [_token, milliseconds]).ConfigureAwait(false);
                    if ((long)lockRenewed != 1 || (long)sourceRenewed != 1)
                    {
                        _leaseLost.Cancel();
                        return;
                    }
                }
            }
            catch (OperationCanceledException) when (_stopRenewal.IsCancellationRequested)
            {
                // Normal lease disposal.
            }
            catch (Exception ex) when (ex is RedisException or TimeoutException)
            {
                _leaseLost.Cancel();
                _logger?.LogWarning(
                    "Redis stopped renewing a distributed cache-miss lease; the source load is cancelled.");
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            _stopRenewal.Cancel();
            await _renewal.ConfigureAwait(false);
            try
            {
                await _database.ScriptEvaluateAsync(ReleaseOwnedLease, [_lockKey], [_token]).ConfigureAwait(false);
                await _database.ScriptEvaluateAsync(ReleaseOwnedLease, [_sourceKey], [_token]).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is RedisException or TimeoutException)
            {
                _logger?.LogWarning(
                    "Redis could not release a distributed cache-miss lease; its configured TTL will expire it.");
            }
            _stopRenewal.Dispose();
            _leaseLost.Dispose();
        }
    }
}
