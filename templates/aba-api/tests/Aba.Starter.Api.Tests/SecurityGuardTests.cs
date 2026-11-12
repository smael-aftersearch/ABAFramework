using Aba.Starter.Api.Platform;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
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

    [Theory]
    [InlineData("PostgreSql",
        "Host=localhost;Database=aba;Username=app;Password=DevOnly;SSL Mode=Require",
        "Production PostgreSQL requires SSL Mode=VerifyFull")]
    [InlineData("SqlServer",
        "Server=localhost;Database=aba;User ID=app;Password=DevOnly;Encrypt=True;TrustServerCertificate=True",
        "Production SQL Server requires Encrypt=True or Strict")]
    public void ProductionHostRefusesUnverifiedDatabaseTlsBeforeServingRequests(
        string provider, string insecureConnection, string diagnostic)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseEnvironment("Production");
            host.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Authentication:Authority"] = "https://idp.example.invalid/realms/aba",
                    ["Authentication:Audience"] = "aba-prod-api",
                    ["Platform:Database:Provider"] = provider,
                    ["ConnectionStrings:AbaDatabase"] = insecureConnection
                }));
        });
        var exception = Record.Exception(() =>
        {
            using var client = factory.CreateClient();
        });
        Assert.NotNull(exception);
        Assert.Contains(diagnostic, exception.ToString());
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
