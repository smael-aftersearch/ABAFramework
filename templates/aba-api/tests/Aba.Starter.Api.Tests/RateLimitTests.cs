using System.Net;
using System.Security.Claims;
using Aba.Starter.Api.Platform;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Aba.Starter.Api.Tests;

public sealed class RateLimitTests
{
    private static IConfiguration Settings(params (string Key, string? Value)[] pairs)
        => new ConfigurationBuilder().AddInMemoryCollection(
            pairs.ToDictionary(x => x.Key, x => x.Value)).Build();

    [Theory]
    [InlineData("0", "60")]
    [InlineData("10001", "60")]
    [InlineData("1", "0")]
    [InlineData("120", "3601")]
    public void RejectsInvalidLimitConfiguration(string permits, string window)
    {
        Assert.Throws<InvalidOperationException>(() =>
            AbaRateLimitOptions.FromConfiguration(Settings(
                ("Platform:RateLimit:PermitLimit", permits),
                ("Platform:RateLimit:WindowSeconds", window))));
    }

    [Fact]
    public void AuthenticatedUserPartitionDoesNotTrustTenantHeader()
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", "alpha"), new Claim("tenant_id", "tenant-one")], "Bearer"));
        context.Request.Headers["X-Tenant-ID"] = "untrusted";
        var first = AbaRateLimitOptions.PartitionKey(context);
        context.Request.Headers["X-Tenant-ID"] = "other";
        Assert.Equal(first, AbaRateLimitOptions.PartitionKey(context));
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", "alpha"), new Claim("tenant_id", "tenant-two")], "Bearer"));
        Assert.NotEqual(first, AbaRateLimitOptions.PartitionKey(context));
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", "beta"), new Claim("tenant_id", "tenant-one")], "Bearer"));
        Assert.NotEqual(first, AbaRateLimitOptions.PartitionKey(context));
    }

    [Fact]
    public void AnonymousPartitionUsesPeerIpNotSpoofedForwardedHeaders()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.25");
        context.Request.Headers["X-Forwarded-For"] = "10.0.0.1";
        var first = AbaRateLimitOptions.PartitionKey(context);
        context.Request.Headers["X-Forwarded-For"] = "10.0.0.2";
        Assert.Equal("ip:192.0.2.25", first);
        Assert.Equal(first, AbaRateLimitOptions.PartitionKey(context));
    }

    [Fact]
    public async Task HealthRemainsResponsiveAfterOrdinaryRequestQuotaIsExhausted()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Platform:RateLimit:PermitLimit"] = "2",
                    ["Platform:RateLimit:WindowSeconds"] = "300"
                }));
        });
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK,
            (await client.GetAsync("/api/v1/system/status")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await client.GetAsync("/api/v1/system/status")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests,
            (await client.GetAsync("/api/v1/system/status")).StatusCode);
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.OK,
                (await client.GetAsync("/health/live")).StatusCode);
            Assert.Equal(HttpStatusCode.OK,
                (await client.GetAsync("/health/ready")).StatusCode);
        }
    }
}
