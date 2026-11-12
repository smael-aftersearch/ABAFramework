using Aba.Starter.Platform.Caching;
using Aba.Starter.Platform.Messaging;
using Aba.Starter.Platform.Persistence;
using Microsoft.AspNetCore.Builder;

namespace Aba.Starter.Platform;

/// <summary>Composition helper: technical wiring only, no business domain policy.</summary>
public static class AbaPlatformSetup
{
    public static void AddAbaPlatform(this WebApplicationBuilder builder)
    {
        var database = AbaDatabaseOptions.FromConfiguration(builder.Configuration);
        builder.Services.AddAbaPersistence(database);
        builder.Services.AddAbaCaching(builder.Configuration);
        builder.AddAbaWolverine(database);
    }
}
