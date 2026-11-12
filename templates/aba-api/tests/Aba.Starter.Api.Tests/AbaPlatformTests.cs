using Aba.Starter.Platform.Caching;
using Aba.Starter.Platform.Observability;
using Aba.Starter.Platform.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Aba.Starter.Api.Tests;

public sealed class AbaPlatformTests
{
    private static IConfiguration Config(IDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void OtlpEndpointMustBeHttpsOutsideDevelopment()
    {
        var config = Config(new Dictionary<string, string?>
        {
            ["Platform:Observability:Enabled"] = "true",
            ["Platform:Observability:ServiceName"] = "Example.Api",
            ["Platform:Observability:OtlpEndpoint"] = "http://telemetry.example/v1/traces"
        });
        Assert.Throws<InvalidOperationException>(() =>
            AbaObservabilityOptions.FromConfiguration(config, isDevelopment: false));
        Assert.True(AbaObservabilityOptions.FromConfiguration(config, isDevelopment: true).Enabled);
    }

    [Fact]
    public void BlankProjectHasNoDatabase()
    {
        var db = AbaDatabaseOptions.FromConfiguration(Config(new Dictionary<string, string?>()));
        Assert.False(db.Enabled);
        Assert.Equal(AbaDatabaseProvider.None, db.Provider);
    }

    [Theory]
    [InlineData("PostgreSql", "Npgsql.EntityFrameworkCore.PostgreSQL")]
    [InlineData("postgres", "Npgsql.EntityFrameworkCore.PostgreSQL")]
    [InlineData("SqlServer", "Microsoft.EntityFrameworkCore.SqlServer")]
    [InlineData("mssql", "Microsoft.EntityFrameworkCore.SqlServer")]
    public void BothEfCoreProvidersCanBeSelectedWithoutAConnection(
        string configured, string expectedProvider)
    {
        var config = Config(new Dictionary<string, string?>
        {
            ["Platform:Database:Provider"] = configured,
            ["ConnectionStrings:AbaDatabase"] = configured.Equals("SqlServer", StringComparison.OrdinalIgnoreCase)
                || configured.Equals("mssql", StringComparison.OrdinalIgnoreCase)
                ? "Server=127.0.0.1;Database=aba;User ID=sa;Password=NotARealPassword1!;TrustServerCertificate=True"
                : "Host=127.0.0.1;Database=aba;Username=aba;Password=test"
        });
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAbaPersistence(AbaDatabaseOptions.FromConfiguration(config));
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<AbaPlatformDbContext>();
        Assert.Equal(expectedProvider, context.Database.ProviderName);
    }

    [Theory]
    [InlineData("Oracle")]
    [InlineData("SqlLite")]
    public void UnknownDatabaseIsRejected(string provider) =>
        Assert.Throws<InvalidOperationException>(() =>
            AbaDatabaseOptions.FromConfiguration(Config(new Dictionary<string, string?>
                { ["Platform:Database:Provider"] = provider })));

    [Fact]
    public void MissingConnectionIsRejected() =>
        Assert.Throws<InvalidOperationException>(() =>
            AbaDatabaseOptions.FromConfiguration(Config(new Dictionary<string, string?>
                { ["Platform:Database:Provider"] = "PostgreSql" })));

    [Fact]
    public void RedisCannotBeEnabledWithoutConnection() =>
        Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddAbaCaching(Config(new Dictionary<string, string?>
                { ["Platform:Cache:Provider"] = "Redis" })));

    [Fact]
    public void UnknownCacheProviderIsRejected() =>
        Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddAbaCaching(Config(new Dictionary<string, string?>
                { ["Platform:Cache:Provider"] = "Memcached" })));

    [Fact]
    public async Task ScopedCacheIsIsolatedAndInvalidates()
    {
        var services = new ServiceCollection();
        services.AddAbaCaching(Config(new Dictionary<string, string?>()));
        using var sp = services.BuildServiceProvider();
        var cache = sp.GetRequiredService<ScopedJsonCache>();
        await cache.PutAsync("tenant-a", "user-1", "alpha", TimeSpan.FromMinutes(1));
        Assert.Equal("alpha", await cache.GetAsync<string>("tenant-a", "user-1"));
        Assert.Null(await cache.GetAsync<string>("tenant-b", "user-1"));
        await cache.RemoveAsync("tenant-a", "user-1");
        Assert.Null(await cache.GetAsync<string>("tenant-a", "user-1"));
    }

    [Fact]
    public async Task NonPositiveCacheTtlIsRejected()
    {
        var services = new ServiceCollection();
        services.AddAbaCaching(Config(new Dictionary<string, string?>()));
        using var sp = services.BuildServiceProvider();
        var cache = sp.GetRequiredService<ScopedJsonCache>();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            cache.PutAsync("scope", "key", 1, TimeSpan.Zero));
    }
}
