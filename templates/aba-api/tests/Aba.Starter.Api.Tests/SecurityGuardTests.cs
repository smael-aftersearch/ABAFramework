using Aba.Starter.Api.Platform;
using Xunit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Aba.Starter.Api.Tests;

public sealed class SecurityGuardTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void LocalHttpOidcDiscoveryRequiresDevelopmentOptIn(
        bool allowHttp, bool expectedHttps)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseEnvironment("Development");
            host.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Authentication:Authority"] = "http://localhost:8180",
                    ["Authentication:Audience"] = "aba-api",
                    ["Authentication:AllowHttpMetadataInDevelopment"] = allowHttp.ToString()
                }));
        });
        var options = factory.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        Assert.Equal(expectedHttps, options.RequireHttpsMetadata);
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
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseEnvironment("Production");
            host.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Authentication:Authority"] = "https://identity.example.invalid",
                    ["Authentication:Audience"] = "aba-api",
                    ["Authentication:AllowHttpMetadataInDevelopment"] = "true"
                }));
        });
        var options = factory.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        Assert.True(options.RequireHttpsMetadata);
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
