using System.Diagnostics;
using System.Text.RegularExpressions;
using Aba.Starter.Platform.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;
using Xunit;

namespace Aba.Starter.Platform.IntegrationTests;

/// <summary>
/// Opt-in, isolated CI test that ACTUALLY stops and restarts the GitHub
/// Actions-provisioned redis:7-alpine service; never accepts an arbitrary PID,
/// remote host or a non-service Docker container.
/// </summary>
public sealed class RedisRestartDrillTests
{
    private static void RequireCiService(
        string? connection, string? containerId, bool ci, bool enabled)
    {
        if (!ci || !enabled || string.IsNullOrWhiteSpace(containerId) ||
            !Regex.IsMatch(containerId, "^[0-9a-f]{64}$") ||
            string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("Redis restart requires an explicit disposable CI container.");
        var parsed = ConfigurationOptions.Parse(connection);
        if (parsed.EndPoints.Count != 1 ||
            parsed.EndPoints[0].ToString() is not ("localhost:6379" or "127.0.0.1:6379"))
            throw new InvalidOperationException("Redis restart targets only the CI loopback service on port 6379.");
    }

    private static async Task<string> DockerAsync(params string[] arguments)
    {
        var processInfo = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments) processInfo.ArgumentList.Add(argument);
        using var process = Process.Start(processInfo)
            ?? throw new InvalidOperationException("Docker must be available for the isolated CI Redis fixture.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
        var output = await stdout;
        var error = await stderr;
        if (process.ExitCode != 0)
            throw new InvalidOperationException("Disposable Redis container operation failed: " + error);
        return output.Trim();
    }

    [Fact]
    [Trait("Category", "RedisRestartDrill")]
    public void UnsafeRedisRestartTargetsAreRejected()
    {
        const string id = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        Assert.Throws<InvalidOperationException>(() =>
            RequireCiService("localhost:6379", id, false, true));
        Assert.Throws<InvalidOperationException>(() =>
            RequireCiService("localhost:6379", id, true, false));
        Assert.Throws<InvalidOperationException>(() =>
            RequireCiService("localhost:6379", "../../container", true, true));
        Assert.Throws<InvalidOperationException>(() =>
            RequireCiService("redis.company.com:6379", id, true, true));
        Assert.Throws<InvalidOperationException>(() =>
            RequireCiService("localhost:6380", id, true, true));
        Assert.Throws<InvalidOperationException>(() =>
            RequireCiService("localhost:6379,redis.company.com:6379", id, true, true));
    }

    [Fact]
    [Trait("Category", "RedisRestartDrill")]
    public async Task RealClientFailsClosedUsesFreshSourceAndRecoversAfterServiceRestart()
    {
        var connection = Environment.GetEnvironmentVariable("ABA_TEST_REDIS");
        var id = Environment.GetEnvironmentVariable("ABA_TEST_REDIS_CONTAINER_ID");
        RequireCiService(connection, id, Environment.GetEnvironmentVariable("CI") == "true",
            Environment.GetEnvironmentVariable("ABA_ENABLE_REDIS_RESTART_DRILL") == "yes");

        // Never trust an arbitrary hex container ID; check exact service image.
        Assert.Equal("redis:7-alpine",
            await DockerAsync("inspect", "--format", "{{.Config.Image}}", id!));
        var settings = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Platform:Cache:Provider"] = "Redis",
                ["ConnectionStrings:AbaRedis"] =
                    "localhost:6379,abortConnect=false,connectRetry=0,connectTimeout=500,asyncTimeout=700"
            }).Build();
        var registrations = new ServiceCollection().AddLogging();
        registrations.AddAbaCaching(settings);
        await using var services = registrations.BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var query = scope.ServiceProvider.GetRequiredService<ReadThroughQueryCache>();
        var cache = scope.ServiceProvider.GetRequiredService<ScopedJsonCache>();
        var key = Guid.NewGuid().ToString("N");
        var loads = 0;

        var initial = await query.GetOrLoadAsync(
            "test-tenant", key, TimeSpan.FromSeconds(20),
            _ => Task.FromResult("initial-" + Interlocked.Increment(ref loads)));
        Assert.Equal("initial-1", initial);
        Assert.Equal("initial-1", await query.GetOrLoadAsync<string>(
            "test-tenant", key, TimeSpan.FromSeconds(20),
            _ => throw new InvalidOperationException("Expected live Redis cache hit.")));
        Assert.Equal(1, loads);
        var stopped = false;
        try
        {
            await DockerAsync("stop", "--time", "0", id!);
            stopped = true;
            // Both behaviours use the SAME REAL IDistributedCache while the
            // backing Redis container is actually offline.
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var strictLoader = 0;
            var failure = await Record.ExceptionAsync(() => query.GetOrLoadAsync(
                "test-tenant", "strict-" + key, TimeSpan.FromMinutes(1),
                _ => { Interlocked.Increment(ref strictLoader); return Task.FromResult("unsafe"); },
                CacheOutagePolicy.FailClosed, deadline.Token));
            Assert.NotNull(failure);
            Assert.True(failure is RedisException or TimeoutException,
                "Untrusted Redis failure must not be silently bypassed: " + failure.GetType());
            Assert.Equal(0, strictLoader);

            var freshCalls = 0;
            Assert.Equal("authoritative", await query.GetOrLoadAsync(
                "test-tenant", "fresh-" + key, TimeSpan.FromSeconds(5),
                _ => { Interlocked.Increment(ref freshCalls); return Task.FromResult("authoritative"); },
                CacheOutagePolicy.UseAuthoritativeSource, deadline.Token));
            Assert.Equal(1, freshCalls);
        }
        finally
        {
            // Always restore only the exact, pre-validated CI Redis container,
            // including on assertion failures and timeouts.
            if (stopped) await DockerAsync("start", id!);
        }

        var recovered = false;
        Exception? last = null;
        for (var n = 0; n < 60; n++)
        {
            try
            {
                await cache.PutAsync("test-tenant", "recovery-" + key, "connected",
                    TimeSpan.FromSeconds(10));
                if (await cache.GetAsync<string>("test-tenant", "recovery-" + key) == "connected")
                { recovered = true; break; }
            }
            catch (Exception ex) when (ex is RedisException or TimeoutException)
            { last = ex; }
            await Task.Delay(500);
        }
        Assert.True(recovered, "Redis client failed to reconnect to a real restarted container: " + last);
        Assert.Equal(HealthStatus.Healthy,
            (await ActivatorUtilities.CreateInstance<AbaRedisHealthCheck>(scope.ServiceProvider)
                .CheckHealthAsync(new HealthCheckContext())).Status);

        var fresh = 0;
        Assert.Equal("after-restart", await query.GetOrLoadAsync(
            "test-tenant", "restarted-" + key, TimeSpan.FromSeconds(3),
            _ => { Interlocked.Increment(ref fresh); return Task.FromResult("after-restart"); }));
        Assert.Equal("after-restart", await query.GetOrLoadAsync<string>(
            "test-tenant", "restarted-" + key, TimeSpan.FromSeconds(3),
            _ => throw new InvalidOperationException("Expected a recovered cache hit.")));
        Assert.Equal(1, fresh);
        Assert.Null(await cache.GetAsync<string>("other-tenant", "restarted-" + key));
    }
}
