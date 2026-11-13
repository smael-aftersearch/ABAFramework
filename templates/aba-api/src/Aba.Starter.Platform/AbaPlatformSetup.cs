using Aba.Starter.Platform.Caching;
using Aba.Starter.Platform.Messaging;
using Aba.Starter.Platform.Observability;
using Aba.Starter.Platform.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Wolverine;

namespace Aba.Starter.Platform;

/// <summary>Composition helper: technical wiring only, no business domain policy.</summary>
public static class AbaPlatformSetup
{
    public static void AddAbaPlatform(
        this WebApplicationBuilder builder,
        Action<WolverineOptions>? configureMessaging = null)
    {
        var database = AbaDatabaseOptions.FromConfiguration(builder.Configuration);
        database.ValidateTransportSecurity(builder.Environment.IsDevelopment());
        builder.Services.AddAbaPersistence(database);
        builder.Services.AddAbaAudit(builder.Configuration, database.Enabled);
        builder.Services.AddAbaCaching(
            builder.Configuration, builder.Environment.IsDevelopment());
        builder.AddAbaWolverine(database, configureMessaging);
        builder.AddAbaObservability();
    }
}
