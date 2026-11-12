using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace Aba.Starter.Api.Tests;

public sealed class ReadinessTests
{
    [Fact]
    public async Task FailingTaggedDependencyDegradesReadinessButNotLiveness()
    {
        // No external database/Redis is required for this deterministic
        // failure case. A real provider-outage drill remains a separate gate.
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                services.AddHealthChecks().AddCheck("injected-ready-dependency",
                    () => HealthCheckResult.Unhealthy("Injected outage."),
                    tags: ["ready"]);
            });
        });
        using var client = factory.CreateClient();

        using var readiness = await client.GetAsync("/health/ready");
        using var liveness = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, readiness.StatusCode);
        Assert.Equal(HttpStatusCode.OK, liveness.StatusCode);
    }
}
