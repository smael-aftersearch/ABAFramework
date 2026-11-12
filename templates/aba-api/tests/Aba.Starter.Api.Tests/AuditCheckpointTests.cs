using Aba.Starter.Platform.Persistence;
using Xunit;

namespace Aba.Starter.Api.Tests;

public sealed class AuditCheckpointTests
{
    private static readonly byte[] Secret = Enumerable.Repeat((byte)0x61, 32).ToArray();
    private static AbaAuditCheckpointCandidate Candidate() =>
        new("tenant-one", 5, new string('a', 64));

    [Fact]
    public void IndependentKeySignaturesRoundTripAndDetectMutations()
    {
        var issued = new DateTimeOffset(2026, 11, 13, 0, 0, 0, TimeSpan.Zero);
        var signed = AbaAuditCheckpointCodec.Sign(Candidate(), "offline-v1", Secret, issued);
        Assert.True(AbaAuditCheckpointCodec.Verify(signed, Secret));
        Assert.Equal("offline-v1", signed.KeyId);
        Assert.False(AbaAuditCheckpointCodec.Verify(signed with { TenantId = "tenant-two" }, Secret));
        Assert.False(AbaAuditCheckpointCodec.Verify(signed with { Sequence = 6 }, Secret));
        Assert.False(AbaAuditCheckpointCodec.Verify(signed with { HeadDigest = new string('b', 64) }, Secret));
        Assert.False(AbaAuditCheckpointCodec.Verify(signed with { IssuedUtc = issued.AddHours(1) }, Secret));
        Assert.False(AbaAuditCheckpointCodec.Verify(signed with { KeyId = "offline-v2" }, Secret));
        Assert.False(AbaAuditCheckpointCodec.Verify(signed with { Signature = new string('f', 64) }, Secret));
        Assert.False(AbaAuditCheckpointCodec.Verify(signed, Enumerable.Repeat((byte)0x62, 32).ToArray()));
    }

    [Fact]
    public void UnsignedInvalidAndWeakCheckpointsFailClosed()
    {
        var issued = DateTimeOffset.UtcNow;
        Assert.Throws<ArgumentException>(() =>
            AbaAuditCheckpointCodec.Sign(Candidate(), "offline-v1", new byte[16], issued));
        Assert.Throws<ArgumentException>(() =>
            AbaAuditCheckpointCodec.Sign(Candidate() with { Sequence = 0 }, "offline-v1", Secret, issued));
        Assert.Throws<ArgumentException>(() =>
            AbaAuditCheckpointCodec.Sign(Candidate() with { HeadDigest = "unsafe" }, "offline-v1", Secret, issued));
        Assert.Throws<ArgumentException>(() =>
            AbaAuditCheckpointCodec.Sign(Candidate(), "invalid key", Secret, issued));
        var valid = AbaAuditCheckpointCodec.Sign(Candidate(), "offline-v1", Secret, issued.ToUniversalTime());
        Assert.False(AbaAuditCheckpointCodec.Verify(valid with { Signature = "tampered" }, Secret));
    }

    [Fact]
    public void OfflineSigningKeyCanRotateWithoutChangingDatabaseRecordHmac()
    {
        var first = AbaAuditCheckpointCodec.Sign(Candidate(), "offline-v1", Secret,
            DateTimeOffset.UtcNow.ToUniversalTime());
        var rotatedKey = Enumerable.Repeat((byte)0x22, 32).ToArray();
        var later = AbaAuditCheckpointCodec.Sign(Candidate() with { Sequence = 6 },
            "offline-v2", rotatedKey, DateTimeOffset.UtcNow.ToUniversalTime());
        Assert.True(AbaAuditCheckpointCodec.Verify(first, Secret));
        Assert.True(AbaAuditCheckpointCodec.Verify(later, rotatedKey));
        Assert.False(AbaAuditCheckpointCodec.Verify(later, Secret));
    }
}
