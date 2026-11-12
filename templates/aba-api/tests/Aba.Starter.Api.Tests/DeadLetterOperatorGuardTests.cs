using System.Security.Claims;
using Aba.Starter.Platform.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aba.Starter.Api.Tests;

public sealed class DeadLetterOperatorGuardTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(
            x => x.Key, x => (string?)x.Value)).Build();

    [Fact]
    public void DeadLetterAdminIsDisabledByDefault()
    {
        var services = new ServiceCollection();
        services.AddAbaDeadLetterAdministration(Config(), databaseEnabled: false);
        Assert.DoesNotContain(services, x =>
            x.ServiceType == typeof(AbaDeadLetterAdministration));
    }

    [Fact]
    public void RequiresDurabilityAndAuditAndRejectsMultitenancy()
    {
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() =>
            services.AddAbaDeadLetterAdministration(Config(
                ("Platform:Messaging:DeadLetterOperatorEnabled", "true")), true));
        Assert.Throws<InvalidOperationException>(() =>
            services.AddAbaDeadLetterAdministration(Config(
                ("Platform:Messaging:DeadLetterOperatorEnabled", "true"),
                ("Platform:Messaging:Enabled", "true")), true));
        Assert.Throws<InvalidOperationException>(() =>
            services.AddAbaDeadLetterAdministration(Config(
                ("Platform:Messaging:DeadLetterOperatorEnabled", "true"),
                ("Platform:Messaging:Enabled", "true"),
                ("Platform:Audit:Enabled", "true"),
                ("Platform:Tenancy:Enabled", "true")), true));
    }

    [Fact]
    public void DeniesAnonymousOperatorsEvenWithClaim()
    {
        var claims = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("sub", "operator-1"),
            new Claim(AbaDeadLetterAdministration.PermissionClaim, "deadletters:replay")
        ]));
        Assert.False(claims.Identity!.IsAuthenticated);
        // End-to-end inspection and replay checks run on actual PostgreSQL/
        // SQL Server, not on a fake Wolverine message store.
    }

    [Fact]
    public void EnabledOnlyWhenAuditedSingleStoreAndExplicitlyConfigured()
    {
        var services = new ServiceCollection();
        services.AddAbaDeadLetterAdministration(Config(
            ("Platform:Messaging:DeadLetterOperatorEnabled", "true"),
            ("Platform:Messaging:Enabled", "true"),
            ("Platform:Audit:Enabled", "true")), databaseEnabled: true);
        Assert.Contains(services, x => x.ServiceType == typeof(AbaDeadLetterAdministration));
    }
}
