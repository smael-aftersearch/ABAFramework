using System.Net;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aba.Starter.Api.Tests;

public sealed class CorsTests
{
    private static WebApplicationFactory<Program> CreateFactory(
        string environment = "Development",
        params string[] allowedOrigins) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(allowedOrigins
                    .Select((origin, index) => new KeyValuePair<string, string?>(
                        $"Api:Cors:AllowedOrigins:{index}", origin)));
            });
        });

    [Fact]
    public async Task ConfiguredOriginCanUseAllowedMethodsWithoutCredentialCookies()
    {
        const string origin = "https://client.example";
        using var factory = CreateFactory("Development", origin);
        var configuredOrigins = factory.Services.GetRequiredService<IConfiguration>()
            .GetSection("Api:Cors:AllowedOrigins").Get<string[]>();
        Assert.Equal(origin, Assert.Single(configuredOrigins!));
        var corsProvider = factory.Services.GetRequiredService<ICorsPolicyProvider>();
        var corsPolicy = await corsProvider.GetPolicyAsync(new DefaultHttpContext(),
            Aba.Starter.Api.Platform.AbaCorsOptions.PolicyName);
        Assert.Contains(origin, corsPolicy!.Origins);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/system/authorization-check");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,x-correlation-id");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var allowedOrigins),
            $"CORS origin header missing. Response headers: {string.Join(", ", response.Headers.Select(h => h.Key))}");
        Assert.Equal(origin, Assert.Single(allowedOrigins!));
        Assert.Contains("GET", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Methods")));
        Assert.Contains("authorization", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Headers")),
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Access-Control-Allow-Credentials", response.Headers.Select(header => header.Key));
    }

    [Fact]
    public async Task UnlistedOriginReceivesNoCrossOriginPermission()
    {
        using var factory = CreateFactory("Development", "https://approved.example");
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("Origin", "https://attacker.example");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("Access-Control-Allow-Origin", response.Headers.Select(header => header.Key));
    }

    [Fact]
    public async Task EmptyAllowListDoesNotActLikeWildcard()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("Origin", "https://any-client.example");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("Access-Control-Allow-Origin", response.Headers.Select(header => header.Key));
    }

    [Fact]
    public void InvalidConfiguredOriginFailsBeforeHostCanServeRequests()
    {
        using var factory = CreateFactory("Development", "https://client.example/path");

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("Api:Cors:AllowedOrigins", exception.ToString());
    }

    [Theory]
    [InlineData("*", false)]
    [InlineData("https://client.example/path", false)]
    [InlineData("https://user:secret@client.example", false)]
    [InlineData("https://client.example?token=secret", false)]
    [InlineData("http://client.example", false)]
    [InlineData("http://localhost:5173", true)]
    [InlineData("http://127.0.0.1:5173", false)]
    public void CorsOriginsAreValidatedAgainstEnvironment(string origin, bool isDevelopment)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Api:Cors:AllowedOrigins:0"] = origin
            })
            .Build();

        if (isDevelopment && origin.StartsWith("http://", StringComparison.Ordinal))
        {
            var options = Aba.Starter.Api.Platform.AbaCorsOptions.FromConfiguration(
                configuration, isDevelopment: true);
            Assert.Equal(origin, Assert.Single(options.AllowedOrigins));
            return;
        }

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Aba.Starter.Api.Platform.AbaCorsOptions.FromConfiguration(configuration, isDevelopment));
        Assert.DoesNotContain("secret", exception.Message);
    }
}
