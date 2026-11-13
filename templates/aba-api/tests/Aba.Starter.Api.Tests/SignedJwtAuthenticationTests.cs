using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Aba.Starter.Api.Tests;

/// <summary>
/// Tests the real JwtBearer signature/issuer/audience/lifetime pipeline without
/// calling a real IdP. The fixed key is disposable TEST DATA, never a production key.
/// </summary>
public sealed class SignedJwtAuthenticationTests
{
    private const string Issuer = "https://identity.aba.test";
    private const string Audience = "aba-secure-api";
    private const string AlternateIssuer = "https://impostor.aba.test";
    private const string AlternateAudience = "wrong-api";

    private static readonly SymmetricSecurityKey TestSigningKey = new(
        Encoding.UTF8.GetBytes("aba-disposable-jwt-test-key-ONLY-1234567890abcdef"));

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                services.PostConfigure<JwtBearerOptions>(
                    JwtBearerDefaults.AuthenticationScheme, options =>
                    {
                        // No network call to OIDC discovery: tokens still undergo real
                        // signature, issuer, audience and expiration validation.
                        options.Authority = null;
                        options.MetadataAddress = $"{Issuer}/.well-known/openid-configuration";
                        options.ConfigurationManager = null;
                        options.TokenValidationParameters = new TokenValidationParameters
                        {
                            ValidateIssuerSigningKey = true,
                            IssuerSigningKey = TestSigningKey,
                            RequireSignedTokens = true,
                            ValidateIssuer = true,
                            ValidIssuer = Issuer,
                            ValidateAudience = true,
                            ValidAudience = Audience,
                            ValidateLifetime = true,
                            RequireExpirationTime = true,
                            ClockSkew = TimeSpan.Zero
                        };
                    });
            });
        });

    private static string CreateJwt(
        string issuer = Issuer,
        string audience = Audience,
        SecurityKey? signingKey = null,
        bool expired = false,
        bool withTenant = true)
    {
        var claims = new List<Claim> { new("sub", "test-user") };
        if (withTenant)
            claims.Add(new Claim("tenant_id", "tenant-alpha"));
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: DateTime.UtcNow.AddHours(-1),
            expires: expired ? DateTime.UtcNow.AddMinutes(-1) : DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(
                signingKey ?? TestSigningKey, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static async Task<HttpStatusCode> InvokeAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, "/api/v1/system/authorization-check");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    [Fact]
    public async Task ValidSignedTokenPassesBearerValidation()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.NoContent, await InvokeAsync(client, CreateJwt()));
    }

    [Fact]
    public async Task InvalidSignatureIsRejected()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var otherKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes("another-disposable-test-signing-key-ONLY-123456"));
        Assert.Equal(HttpStatusCode.Unauthorized,
            await InvokeAsync(client, CreateJwt(signingKey: otherKey)));
    }

    [Fact]
    public async Task WrongIssuerIsRejected()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            await InvokeAsync(client, CreateJwt(issuer: AlternateIssuer)));
    }

    [Fact]
    public async Task WrongAudienceIsRejected()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            await InvokeAsync(client, CreateJwt(audience: AlternateAudience)));
    }

    [Fact]
    public async Task ExpiredSignedTokenIsRejected()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            await InvokeAsync(client, CreateJwt(expired: true)));
    }

    [Fact]
    public async Task MalformedBearerStringIsRejected()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            await InvokeAsync(client, "not-a-jwt"));
    }
}
