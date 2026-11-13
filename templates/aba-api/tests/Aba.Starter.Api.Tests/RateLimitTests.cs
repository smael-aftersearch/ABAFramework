using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
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
    public void SharedQuotaOptInRequiresRedisAndApplicationNamespace()
    {
        Assert.False(AbaSharedRateLimitOptions.FromConfiguration(Settings(), true).Enabled);
        Assert.Throws<InvalidOperationException>(() =>
            AbaSharedRateLimitOptions.FromConfiguration(Settings(
                ("Platform:RateLimit:Shared:Enabled", "true")), true));
        Assert.Throws<InvalidOperationException>(() =>
            AbaSharedRateLimitOptions.FromConfiguration(Settings(
                ("Platform:RateLimit:Shared:Enabled", "true"),
                ("ConnectionStrings:AbaRedis", "127.0.0.1:6379")), true));
        foreach (var invalid in new[] { "../other", "UPPER", "ab", "tenant|escape" })
            Assert.Throws<InvalidOperationException>(() =>
                AbaSharedRateLimitOptions.FromConfiguration(Settings(
                    ("Platform:RateLimit:Shared:Enabled", "true"),
                    ("Platform:RateLimit:Shared:Namespace", invalid),
                    ("ConnectionStrings:AbaRedis", "127.0.0.1:6379")), true));
        var settings = AbaSharedRateLimitOptions.FromConfiguration(Settings(
            ("Platform:RateLimit:Shared:Enabled", "true"),
            ("Platform:RateLimit:Shared:Namespace", "orders-api"),
            ("ConnectionStrings:AbaRedis", "127.0.0.1:6379"),
            ("Platform:RateLimit:PermitLimit", "3")), true);
        Assert.True(settings.Enabled);
        Assert.Equal(3, settings.PermitLimit);
        Assert.Equal("orders-api", settings.Namespace);
    }

    [Theory]
    [InlineData("127.0.0.1:6379")]
    [InlineData("127.0.0.1:6379,ssl=false")]
    public void NonDevelopmentSharedQuotaRejectsPlaintextRedis(string connection)
    {
        Assert.Throws<InvalidOperationException>(() =>
            AbaSharedRateLimitOptions.FromConfiguration(Settings(
                ("Platform:RateLimit:Shared:Enabled", "true"),
                ("Platform:RateLimit:Shared:Namespace", "orders-api"),
                ("ConnectionStrings:AbaRedis", connection)), false));
    }

    [Fact]
    public void NonDevelopmentSharedQuotaAcceptsTlsAndDisabledModeNeedsNoRedis()
    {
        var settings = AbaSharedRateLimitOptions.FromConfiguration(Settings(
            ("Platform:RateLimit:Shared:Enabled", "true"),
            ("Platform:RateLimit:Shared:Namespace", "orders-api"),
            ("ConnectionStrings:AbaRedis", "127.0.0.1:6379,ssl=true")), false);
        Assert.True(settings.Enabled);
        Assert.Equal("orders-api", settings.Namespace);
        // Invariant: disabled opt-in never creates a Redis security dependency.
        Assert.False(AbaSharedRateLimitOptions.FromConfiguration(Settings(), false).Enabled);
    }

    [Fact]
    public void UnsignedTenantHeaderAndClaimsCannotChangeAnonymousQuotaPartition()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.25");
        // An untrusted identity has claims but no authenticated principal.
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", "fake"), new Claim("tenant_id", "fake-tenant")]));
        context.Request.Headers["X-Tenant-ID"] = "tenant-one";
        var first = AbaRateLimitOptions.PartitionKey(context);
        context.Request.Headers["X-Tenant-ID"] = "tenant-two";
        context.Request.Headers["X-Forwarded-For"] = "198.51.100.7";
        Assert.Equal("ip:192.0.2.25", first);
        Assert.Equal(first, AbaRateLimitOptions.PartitionKey(context));
    }

    [Theory]
    [InlineData("/health/live", true)]
    [InlineData("/health/ready", true)]
    [InlineData("/api/v1/system/status", false)]
    public void HealthExemptionsMatchSharedAndLocal(string route, bool expected)
        => Assert.Equal(expected, AbaRateLimitOptions.IsHealthPath(new PathString(route)));

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
    public void QuotaPartitionCannotComposeClaimsAcrossIdentities()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.25");
        var verified = new ClaimsIdentity(
            [new Claim("sub", "alpha"), new Claim("tenant_id", "tenant-one")], "Bearer");
        context.User = new ClaimsPrincipal(verified);
        var trustedPartition = AbaRateLimitOptions.PartitionKey(context);
        Assert.StartsWith("jwt:", trustedPartition);

        context.User = new ClaimsPrincipal([
            verified, new ClaimsIdentity([
                new Claim("sub", "attacker"), new Claim("tenant_id", "foreign")])]);
        context.Request.Headers["X-Tenant-ID"] = "foreign";
        Assert.Equal(trustedPartition, AbaRateLimitOptions.PartitionKey(context));

        // An unsigned subject cannot borrow authentication from an identity
        // that has only a tenant claim to create independent Redis/local quotas.
        context.User = new ClaimsPrincipal([
            new ClaimsIdentity([new Claim("tenant_id", "tenant-one")], "Bearer"),
            new ClaimsIdentity([new Claim("sub", "attacker")])]);
        Assert.Equal("ip:192.0.2.25", AbaRateLimitOptions.PartitionKey(context));

        // Even two authenticated identities cannot synthesize a new JWT quota.
        context.User = new ClaimsPrincipal([
            verified, new ClaimsIdentity([new Claim("sub", "attacker")], "Bearer")]);
        Assert.Equal("ip:192.0.2.25", AbaRateLimitOptions.PartitionKey(context));
    }

    [Fact]
    public void AmbiguousTenantClaimsCannotCreateMultipleQuotaPartitions()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.25");
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", "alpha"), new Claim("tenant_id", "tenant-a"),
             new Claim("tenant_id", "tenant-b")], "Bearer"));
        var first = AbaRateLimitOptions.PartitionKey(context);
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", "alpha"), new Claim("tenant_id", "tenant-b"),
             new Claim("tenant_id", "tenant-a")], "Bearer"));
        context.Request.Headers["X-Tenant-ID"] = "tenant-c";
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.22";
        Assert.StartsWith("jwt:", first);
        Assert.Equal(first, AbaRateLimitOptions.PartitionKey(context));

        // Local and shared Redis quotas derive their key from this same partition.
        // Invalid tenant claims cannot create an additional valid tenant quota.
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", "alpha")], "Bearer"));
        Assert.Equal(first, AbaRateLimitOptions.PartitionKey(context));
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
    public async Task FixedWindowLimiterExhaustsAfterConfiguredPermits()
    {
        var settings = new AbaRateLimitOptions(PermitLimit: 2, WindowSeconds: 300);
        using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(
            settings.ForRequest);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/system/status";
        using var one = await limiter.AcquireAsync(context, 1);
        using var two = await limiter.AcquireAsync(context, 1);
        using var three = await limiter.AcquireAsync(context, 1);
        Assert.True(one.IsAcquired);
        Assert.True(two.IsAcquired);
        Assert.False(three.IsAcquired);
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
        var effectiveConfig = factory.Services.GetService(typeof(IConfiguration))
            as IConfiguration;
        Assert.NotNull(effectiveConfig);
        Assert.Equal(2, effectiveConfig.GetValue<int>(
            "Platform:RateLimit:PermitLimit"));
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
