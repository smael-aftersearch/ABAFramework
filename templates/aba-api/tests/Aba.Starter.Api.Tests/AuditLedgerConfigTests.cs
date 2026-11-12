using Aba.Starter.Platform.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aba.Starter.Api.Tests;

public sealed class AuditLedgerConfigTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(
            x => x.Key, x => x.Value)).Build();

    private static string Key(int value) =>
        Convert.ToBase64String(Enumerable.Repeat((byte)value, 32).ToArray());

    [Fact]
    public void DisabledByDefaultAndRequiresNoDatabaseOrKey()
    {
        var services = new ServiceCollection();
        services.AddAbaAudit(Config(), databaseEnabled: false);
        Assert.DoesNotContain(services, x => x.ServiceType == typeof(AbaAuditLedger));
    }

    [Fact]
    public void CannotEnableAuditWithoutRelationalDatabase()
    {
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() =>
            services.AddAbaAudit(Config(("Platform:Audit:Enabled", "true")),
                databaseEnabled: false));
    }

    [Fact]
    public void MissingAndTooShortHmacKeyAreRejectedAtStartup()
    {
        var services = new ServiceCollection();
        Assert.ThrowsAny<Exception>(() => services.AddAbaAudit(Config(
            ("Platform:Audit:Enabled", "true"),
            ("Platform:Audit:PrimaryKeyId", "key-v1")), true));

        Assert.Throws<InvalidOperationException>(() => services.AddAbaAudit(Config(
            ("Platform:Audit:Enabled", "true"),
            ("Platform:Audit:PrimaryKeyId", "key-v1"),
            ("Platform:Audit:Keys:key-v1", Convert.ToBase64String(new byte[16]))), true));

        Assert.Throws<InvalidOperationException>(() => services.AddAbaAudit(Config(
            ("Platform:Audit:Enabled", "true"),
            ("Platform:Audit:PrimaryKeyId", "key-v1"),
            ("Platform:Audit:Keys:key-v1", "not base64!")), true));
    }

    [Fact]
    public void RotationAcceptsBothKeyIdsWhenOldKeyRetained()
    {
        var settings = AbaAuditOptions.FromConfiguration(Config(
            ("Platform:Audit:PrimaryKeyId", "key-v2"),
            ("Platform:Audit:Keys:key-v1", Key(25)),
            ("Platform:Audit:Keys:key-v2", Key(26))));
        Assert.Equal("key-v2", settings.PrimaryKeyId);
    }

    [Theory]
    [InlineData("tenant-1", "actor-1", "inventory.update", "allowed", "request-42")]
    [InlineData("tenant-1", "actor-1", "invoice.reject", "denied", "request-43")]
    [InlineData("tenant-1", "actor-1", "inventory.update", "failed", "request-44")]
    public void RedactedStableAuditFieldsAreAllowed(
        string tenant, string actor, string action, string outcome, string request)
    {
        AbaAuditLedger.Validate(new(tenant, actor, action, outcome, request));
    }

    [Theory]
    [InlineData("tenant:evil|injection", "actor", "operation", "allowed", "request")]
    [InlineData("tenant", "alice@example.com", "operation", "allowed", "request")]
    [InlineData("tenant", "actor", "operation", "unknown", "request")]
    [InlineData("tenant", "actor", "operation", "allowed", "")]
    public void MissingOrPiiLikelyAuditFieldsFailClosed(
        string tenant, string actor, string action, string outcome, string request)
    {
        Assert.Throws<ArgumentException>(() =>
            AbaAuditLedger.Validate(new(tenant, actor, action, outcome, request)));
    }
}
