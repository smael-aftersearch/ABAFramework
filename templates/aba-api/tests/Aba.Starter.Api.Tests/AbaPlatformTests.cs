using Aba.Starter.Platform.Caching;
using Aba.Starter.Platform.Observability;
using Aba.Starter.Platform.Persistence;
using Aba.Starter.Platform.Security;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
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
    public void TenantResolverIgnoresUnsignedUserSuppliedHeaders()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("tenant_id", "injected-tenant")]));
        Assert.Null(AbaTenantClaims.Resolve(anonymous, "tenant_id"));
        var authenticated = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("tenant_id", "tenant.alpha")], "validated-bearer"));
        Assert.Equal("tenant.alpha", AbaTenantClaims.Resolve(authenticated, "tenant_id"));
        var missing = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", "user-1")], "validated-bearer"));
        Assert.Null(AbaTenantClaims.Resolve(missing, "tenant_id"));
        var malformed = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("tenant_id", "../tenant 1")], "validated-bearer"));
        Assert.Null(AbaTenantClaims.Resolve(malformed, "tenant_id"));
    }

    [Fact]
    public async Task TenantPolicyRejectsSplitAndAmbiguousPrincipalIdentities()
    {
        var verifiedWithoutTenant = new ClaimsIdentity(
            [new Claim("sub", "user-a")], "Bearer");
        var unsignedForeignTenant = new ClaimsIdentity(
            [new Claim("tenant_id", "tenant-b")]);
        var split = new ClaimsPrincipal([verifiedWithoutTenant, unsignedForeignTenant]);
        Assert.Null(AbaTenantClaims.Resolve(split, "tenant_id"));

        // Two authenticated identities are ambiguous; never combine their claims.
        var otherAuthenticated = new ClaimsIdentity(
            [new Claim("tenant_id", "tenant-b")], "Bearer");
        Assert.Null(AbaTenantClaims.Resolve(
            new ClaimsPrincipal([verifiedWithoutTenant, otherAuthenticated]), "tenant_id"));

        // A legitimate signed tenant claim remains authoritative even if an unsigned
        // identity attempts to supply a conflicting tenant.
        var verifiedWithTenant = new ClaimsIdentity(
            [new Claim("sub", "user-a"), new Claim("tenant_id", "tenant-a")], "Bearer");
        var trusted = new ClaimsPrincipal([verifiedWithTenant, unsignedForeignTenant]);
        Assert.Equal("tenant-a", AbaTenantClaims.Resolve(trusted, "tenant_id"));

        var policy = AbaTenantClaims.BuildPolicy(new AbaTenantOptions(true, "tenant_id"), "Bearer");
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddAuthorization();
        using var provider = serviceCollection.BuildServiceProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        Assert.False((await authorization.AuthorizeAsync(split, resource: null, policy)).Succeeded);
        Assert.True((await authorization.AuthorizeAsync(trusted, resource: null, policy)).Succeeded);
    }

    [Fact]
    public async Task OptInTenancyPolicyRequiresAuthenticatedTenantClaim()
    {
        var configured = new AbaTenantOptions(true, "tenant_id");
        var policy = AbaTenantClaims.BuildPolicy(configured, "Bearer");
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddAuthorization();
        using var provider = serviceCollection.BuildServiceProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var accepted = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("tenant_id", "tenant-a")], "bearer"));
        var denied = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", "person-123")], "bearer"));
        Assert.True((await authorization.AuthorizeAsync(accepted, resource: null, policy)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(denied, resource: null, policy)).Succeeded);
    }

    [Fact]
    public void TenantModeDefaultsToOff()
    {
        var config = Config(new Dictionary<string, string?>());
        Assert.False(AbaTenantOptions.FromConfiguration(config).Enabled);
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

    [Theory]
    [InlineData("PostgreSql", "Host=localhost;Database=aba;Username=aba;Password=test", false)]
    [InlineData("PostgreSql", "Host=localhost;Database=aba;Username=aba;Password=test;SSL Mode=Require", false)]
    [InlineData("PostgreSql", "Host=localhost;Database=aba;Username=aba;Password=test;SSL Mode=VerifyCA", false)]
    [InlineData("PostgreSql", "Host=localhost;Database=aba;Username=aba;Password=test;SSL Mode=VerifyFull", true)]
    [InlineData("SqlServer", "Server=localhost;Database=aba;User ID=sa;Password=Test!123;Encrypt=False", false)]
    [InlineData("SqlServer", "Server=localhost;Database=aba;User ID=sa;Password=Test!123;Encrypt=True;TrustServerCertificate=True", false)]
    [InlineData("SqlServer", "Server=localhost;Database=aba;User ID=sa;Password=Test!123;Encrypt=True;TrustServerCertificate=False", true)]
    [InlineData("SqlServer", "Server=localhost;Database=aba;User ID=sa;Password=Test!123;Encrypt=Strict", true)]
    public void ProductionDatabaseRequiresVerifiedEncryptedTransport(
        string provider, string connection, bool accepted)
    {
        var settings = AbaDatabaseOptions.FromConfiguration(Config(
            new Dictionary<string, string?>
            {
                ["Platform:Database:Provider"] = provider,
                ["ConnectionStrings:AbaDatabase"] = connection
            }));
        if (accepted)
            settings.ValidateTransportSecurity(isDevelopment: false);
        else
            Assert.Throws<InvalidOperationException>(() =>
                settings.ValidateTransportSecurity(isDevelopment: false));

        // Disposable local/test database servers may use self-signed certs,
        // but only when host environment is Development.
        settings.ValidateTransportSecurity(isDevelopment: true);
    }

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
