using Aba.Starter.Platform.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;
using Microsoft.EntityFrameworkCore;

namespace Aba.Starter.Platform.Messaging;

/// <summary>
/// Internal technical probe for integration-test verification of durable delivery.
/// It is not a business integration contract and no HTTP endpoint publishes it.
/// </summary>
/// <param name="PublishNonce">Distinguishes intentional repeated publishes with the same logical id.</param>
public sealed record AbaDurabilityProbe(Guid Id, Guid PublishNonce = default);

public static class AbaDurabilityProbeHandler
{
    public static async Task Handle(AbaDurabilityProbe message, AbaPlatformDbContext db)
    {
        // A broker may redeliver after a lost ACK. Keep the technical effect
        // idempotent under the same durable EF transaction as the receipt.
        var key = message.Id.ToString("N");
        db.IdempotencyReceipts.Add(new IdempotencyReceipt
        {
            Scope = "aba-durability-probe-attempt",
            Key = $"{key}:{Guid.NewGuid():N}",
            CreatedUtc = DateTimeOffset.UtcNow
        });
        if (await db.IdempotencyReceipts.AsNoTracking().AnyAsync(x =>
            x.Scope == "aba-durability-probe" && x.Key == key))
            return;

        db.IdempotencyReceipts.Add(new IdempotencyReceipt
        {
            Scope = "aba-durability-probe",
            Key = key,
            CreatedUtc = DateTimeOffset.UtcNow
        });
        // Wolverine EF transactional middleware performs SaveChanges and outbox flushing.
    }
}

/// <summary>
/// Technical transaction probe, not a domain command. Its EF row and emitted event
/// must either both commit, or both roll back. Never use it for real business data.
/// </summary>
public sealed record AbaTransactionalOutboxProbe(Guid Id, bool FailAfterPublish);

public static class AbaTransactionalOutboxProbeHandler
{
    public static async Task Handle(
        AbaTransactionalOutboxProbe command, AbaPlatformDbContext db, IMessageBus bus)
    {
        db.IdempotencyReceipts.Add(new IdempotencyReceipt
        {
            Scope = "aba-outbox-transaction",
            Key = command.Id.ToString("N"),
            CreatedUtc = DateTimeOffset.UtcNow
        });

        // This is deliberately published *before* the injected failure.
        // Wolverine's EF unit of work must buffer the outgoing envelope and
        // commit both record and outbox together.
        await bus.PublishAsync(new AbaDurabilityProbe(command.Id));
        if (command.FailAfterPublish)
            throw new InvalidOperationException("Injected pre-commit fault for outbox atomicity test.");
    }
}


/// <summary>
/// Test-only technical poison probe. A message is retried/dead-lettered until
/// an EF receipt indicates the environment was fixed; never used by products.
/// </summary>
public sealed record AbaPoisonProbe(Guid Id);

public sealed class AbaPoisonProbeException : Exception
{
    public AbaPoisonProbeException() : base("Synthetic technical poison message") { }
}

public static class AbaPoisonProbeHandler
{
    public static void Handle(AbaPoisonProbe message, AbaPlatformDbContext db)
    {
        var key = message.Id.ToString("N");
        if (!db.IdempotencyReceipts.Any(x => x.Scope == "aba-poison-fixed" && x.Key == key))
            throw new AbaPoisonProbeException();

        db.IdempotencyReceipts.Add(new IdempotencyReceipt
        {
            Scope = "aba-poison-handled",
            Key = key,
            CreatedUtc = DateTimeOffset.UtcNow
        });
    }
}


/// <summary>
/// Technical, CI-only ACK-loss probe. A receipt commits independently of the
/// Wolverine handler ACK. After killing the worker before it returns, the
/// durable queue must redeliver the message without duplicating the effect.
/// Not a business event or permission to crash a production handler.
/// </summary>
public sealed record AbaCrashAckProbe(Guid Id);

public static class AbaCrashAckProbeHandler
{
    public static void RequireIsolatedCrashMarker(
        string marker, bool ci, bool explicitlyEnabled)
    {
        if (!ci || !explicitlyEnabled || string.IsNullOrWhiteSpace(marker))
            throw new InvalidOperationException(
                "ACK fault injection is forbidden outside an explicit disposable CI drill.");

        // Only the parent drill's fresh, direct child directory under the
        // system temp path may receive a signal. Reject aliases, arbitrary
        // temp files and symlink redirects BEFORE any database side effect.
        var full = Path.GetFullPath(marker);
        var directory = Path.GetDirectoryName(full);
        var root = Path.GetFullPath(Path.GetTempPath())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        const string prefix = "aba-crash-";
        var name = directory is null ? string.Empty : Path.GetFileName(directory);
        if (!Path.IsPathFullyQualified(marker) ||
            !string.Equals(marker, full, StringComparison.Ordinal) ||
            directory is null ||
            !string.Equals(Path.GetDirectoryName(directory), root, StringComparison.Ordinal) ||
            !name.StartsWith(prefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(name[prefix.Length..], "N", out _) ||
            Path.GetFileName(full) != "child-ready.marker" ||
            !Directory.Exists(directory) ||
            new DirectoryInfo(directory).LinkTarget is not null ||
            File.Exists(full) || Directory.Exists(full) ||
            new FileInfo(full).LinkTarget is not null)
            throw new InvalidOperationException(
                "Crash marker must be a new file in a direct disposable aba-crash GUID directory.");
    }

    /// <summary>
    /// Never overwrite an existing path (including a symlink). The pre-ACK
    /// signal is staged and moved only after its content is fully flushed.
    /// This remains a CI-only synthetic marker, not a business ACK.
    /// </summary>
    public static void WriteIsolatedCrashMarker(
        string marker, string value, bool ci, bool explicitlyEnabled, bool atomic = false)
    {
        RequireIsolatedCrashMarker(marker, ci, explicitlyEnabled);
        var destination = atomic ? marker + ".staged" : marker;
        using (var stream = new FileStream(destination, FileMode.CreateNew,
                   FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(stream))
            writer.Write(value);
        if (atomic)
            File.Move(destination, marker); // refuse replacement, even on a racing symlink
    }

    public static async Task Handle(
        AbaCrashAckProbe message, IServiceScopeFactory scopeFactory,
        CancellationToken ct)
    {
        var marker = Environment.GetEnvironmentVariable("ABA_CI_CRASH_ACK_MARKER");
        // Check the safety gate BEFORE touching any database row.
        if (!string.IsNullOrWhiteSpace(marker))
            RequireIsolatedCrashMarker(marker,
                Environment.GetEnvironmentVariable("CI") == "true",
                Environment.GetEnvironmentVariable("ABA_ENABLE_DESTRUCTIVE_CI_DRILLS") == "yes");
        var key = message.Id.ToString("N");
        await using var separate = scopeFactory.CreateAsyncScope();
        var receipts = separate.ServiceProvider.GetRequiredService<EfIdempotencyStore>();
        // Every handler execution writes a new attempt receipt; the independent
        // idempotent effect receipt has exactly one winner across re-deliveries.
        await receipts.TryClaimAsync("aba-crash-attempt-" + key,
            Guid.NewGuid().ToString("N"), ct);
        var first = await receipts.TryClaimAsync("aba-crash-effect", key, ct);

        if (string.IsNullOrWhiteSpace(marker))
            return;
        // The receipt is committed, but Wolverine has not acknowledged the
        // envelope: this handler has not yet returned to the runtime.
        // Publish a complete marker atomically: the parent must never
        // observe an empty file and SIGKILL before the commit evidence is
        // fully written to its synthetic control channel.
        WriteIsolatedCrashMarker(marker,
            first ? "effect-committed-before-ack" : "duplicate-committed-before-ack",
            Environment.GetEnvironmentVariable("CI") == "true",
            Environment.GetEnvironmentVariable("ABA_ENABLE_DESTRUCTIVE_CI_DRILLS") == "yes",
            atomic: true);
        await Task.Delay(Timeout.InfiniteTimeSpan, ct);
    }
}
