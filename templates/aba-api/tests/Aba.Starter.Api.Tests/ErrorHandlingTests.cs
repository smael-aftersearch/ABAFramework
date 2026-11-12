using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Aba.Starter.Api.Platform;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace Aba.Starter.Api.Tests;

public sealed class ErrorHandlingTests
{
    private const string SecretExceptionMessage = "private-error-message-must-not-escape";

    [Theory]
    [InlineData("request-safe_123", true)]
    [InlineData("unsafe request id!", false)]
    public async Task ProductionExceptionReturnsSafeProblemDetailsAndCorrelationId(
        string submittedId, bool isValid)
    {
        // Run the same middleware extensions as Program in a real in-memory
        // Production HTTP pipeline. The fault is test-only, never mapped in
        // the generated customer API.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Production"
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddAbaErrorHandling();
        await using var app = builder.Build();
        app.UseAbaErrorHandling();
        app.MapGet("/_test/fail", () => Task.FromException<IResult>(
            new InvalidOperationException(SecretExceptionMessage)));
        await app.StartAsync();

        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/_test/fail");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("X-Correlation-ID", submittedId);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var payload = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(SecretExceptionMessage, payload);
        Assert.DoesNotContain("InvalidOperationException", payload);
        using var document = JsonDocument.Parse(payload);
        var problem = document.RootElement;
        Assert.Equal(500, problem.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("type").GetString()));
        Assert.False(problem.TryGetProperty("stackTrace", out _));

        var returnedId = Assert.Single(response.Headers.GetValues("X-Correlation-ID"));
        Assert.False(string.IsNullOrWhiteSpace(returnedId));
        Assert.Equal(returnedId, problem.GetProperty("correlationId").GetString());
        if (isValid)
            Assert.Equal(submittedId, returnedId);
        else
            Assert.NotEqual(submittedId, returnedId);
    }
}
