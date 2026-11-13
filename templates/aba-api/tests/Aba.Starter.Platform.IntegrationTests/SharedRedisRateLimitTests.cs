using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Aba.Starter.Platform.IntegrationTests;

public sealed class SharedRedisRateLimitTests
{
    private static WebApplicationFactory<Program> Replica(
        string connection, string applicationNamespace, int permits = 3)
        => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Platform:RateLimit:PermitLimit"] = permits.ToString(),
                    ["Platform:RateLimit:WindowSeconds"] = "300",
                    ["Platform:RateLimit:Shared:Enabled"] = "true",
                    ["Platform:RateLimit:Shared:Namespace"] = applicationNamespace,
                    ["ConnectionStrings:AbaRedis"] = connection
                }));
        });

    [Fact]
    public async Task TwoIndependentHttpReplicasShareOneAtomicQuotaDespiteSpoofedHeaders()
    {
        var connection = Environment.GetEnvironmentVariable("ABA_TEST_REDIS");
        Assert.False(string.IsNullOrWhiteSpace(connection), "Real Redis connection required.");
        var ns = "ci-" + Guid.NewGuid().ToString("N");
        using var first = Replica(connection, ns);
        using var second = Replica(connection, ns);
        using var clientA = first.CreateClient();
        using var clientB = second.CreateClient();
        var calls = Enumerable.Range(0, 12).Select(async index =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/system/status");
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", $"203.0.113.{index + 1}");
            request.Headers.TryAddWithoutValidation("X-Tenant-ID", $"forged-{index}");
            using var response = await (index % 2 == 0 ? clientA : clientB).SendAsync(request);
            return response.StatusCode;
        });
        var statuses = await Task.WhenAll(calls);
        Assert.Equal(3, statuses.Count(x => x == HttpStatusCode.OK));
        Assert.Equal(9, statuses.Count(x => x == HttpStatusCode.TooManyRequests));
        Assert.Equal(HttpStatusCode.OK, (await clientA.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await clientB.GetAsync("/health/ready")).StatusCode);
    }

    [Fact]
    public async Task SeparateApplicationNamespacesCannotDrainEachOthersQuota()
    {
        var connection = Environment.GetEnvironmentVariable("ABA_TEST_REDIS");
        Assert.False(string.IsNullOrWhiteSpace(connection), "Real Redis connection required.");
        using var a = Replica(connection, "ci-" + Guid.NewGuid().ToString("N"), 1);
        using var b = Replica(connection, "ci-" + Guid.NewGuid().ToString("N"), 1);
        using var clientA = a.CreateClient();
        using var clientB = b.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await clientA.GetAsync("/api/v1/system/status")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests,
            (await clientA.GetAsync("/api/v1/system/status")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await clientB.GetAsync("/api/v1/system/status")).StatusCode);
    }

    [Fact]
    public async Task UnreachableSharedStoreFailsClosedAndLivenessIsIndependent()
    {
        // Unreachable loopback only; no Redis/production service is stopped.
        using var host = Replica(
            "127.0.0.1:1,abortConnect=false,connectTimeout=500,asyncTimeout=500,syncTimeout=500",
            "ci-redis-down");
        using var client = host.CreateClient();
        Assert.Equal(HttpStatusCode.ServiceUnavailable,
            (await client.GetAsync("/api/v1/system/status")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable,
            (await client.GetAsync("/health/ready")).StatusCode);
    }
}
