using Aba.Starter.Api.Platform;
using Xunit;

namespace Aba.Starter.Api.Tests;

public sealed class SecurityGuardTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void LocalHttpOidcDiscoveryRequiresDevelopmentOptIn(
        bool allowHttp, bool expectedHttps)
    {
        Assert.Equal(expectedHttps, StartupSecurityGuard.RequireHttpsMetadata(
            true, allowHttp, "http://localhost:8180"));
    }

    [Theory]
    [InlineData("http://localhost:8180", false)]
    [InlineData("http://127.0.0.1:8180", false)]
    [InlineData("http://identity.example.invalid", true)]
    [InlineData("http://user:pass@localhost:8180", true)]
    [InlineData("https://identity.example.invalid", true)]
    public void DevelopmentOptInNeverAllowsRemoteOrCredentialedHttpDiscovery(
        string authority, bool expectedRequireHttps)
    {
        Assert.Equal(expectedRequireHttps,
            StartupSecurityGuard.RequireHttpsMetadata(true, true, authority));
    }

    [Fact]
    public void ProductionNeverAllowsHttpDiscoveryEvenWithDevelopmentFlag()
    {
        Assert.True(StartupSecurityGuard.RequireHttpsMetadata(
            false, true, "https://identity.example.invalid"));
        Assert.True(StartupSecurityGuard.RequireHttpsMetadata(
            false, true, "http://localhost:8180"));
        StartupSecurityGuard.Validate(false, "https://identity.example.invalid", "aba-api");
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("http://identity.example.com", "api")]
    [InlineData("https://identity.example.com", null)]
    [InlineData("https://person:pass@identity.example.com", "api")]
    public void ProductionRejectsMissingOrInsecureOidc(string? authority, string? audience)
    {
        Assert.Throws<InvalidOperationException>(() =>
            StartupSecurityGuard.Validate(false, authority, audience));
    }

    [Fact]
    public void ProductionAcceptsExplicitSecureOidc()
    {
        StartupSecurityGuard.Validate(false, "https://identity.example.com", "api");
    }

    [Fact]
    public void LocalDevelopmentDoesNotRequireIdentityProvider()
    {
        StartupSecurityGuard.Validate(true, null, null);
    }
}
