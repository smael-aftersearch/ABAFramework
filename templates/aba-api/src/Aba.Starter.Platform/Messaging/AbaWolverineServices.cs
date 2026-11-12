using Aba.Starter.Platform.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;
using Wolverine.SqlServer;
using JasperFx;
using Microsoft.Extensions.Hosting;

namespace Aba.Starter.Platform.Messaging;

/// <summary>
/// Opt-in real Wolverine relational durability. A SQL store is mandatory; no
/// in-memory outbox is silently substituted for a durable one.
/// </summary>
public static class AbaWolverineServices
{
    public static void AddAbaWolverine(this WebApplicationBuilder builder, AbaDatabaseOptions database)
    {
        if (!builder.Configuration.GetValue<bool>("Platform:Messaging:Enabled"))
            return;

        if (!database.Enabled || string.IsNullOrWhiteSpace(database.ConnectionString))
            throw new InvalidOperationException(
                "Durable Wolverine messaging requires PostgreSql or SqlServer under Platform:Database.");

        builder.Services.AddAbaDeadLetterAdministration(builder.Configuration, database.Enabled);
        builder.Host.UseWolverine(options =>
        {
            // Wolverine 6 moved its Roslyn compiler to an explicit opt-in package.
            options.UseRuntimeCompilation();
            options.Discovery.IncludeAssembly(typeof(AbaDurabilityProbeHandler).Assembly);
            if (database.Provider == AbaDatabaseProvider.PostgreSql)
                options.PersistMessagesWithPostgresql(database.ConnectionString, "aba_messages");
            else
                options.PersistMessagesWithSqlServer(database.ConnectionString, "aba_messages");

            // An internal synthetic poison probe is deterministic and must
            // enter the durable dead-letter store without retry storms.
            options.OnException<AbaPoisonProbeException>().MoveToErrorQueue();
            options.UseEntityFrameworkCoreTransactions();
            options.Policies.UseDurableLocalQueues();
            options.Policies.AutoApplyTransactions();
            // In explicitly opted-in Development tests only, create Wolverine
            // relational envelope resources before processing messages.
            // No schema DDL at ordinary startup unless a local test explicitly
            // opts into development provisioning; Production always requires a
            // reviewed provisioned schema.
            options.AutoBuildMessageStorageOnStartup =
                builder.Environment.IsDevelopment()
                && builder.Configuration.GetValue<bool>("Platform:Messaging:AutoProvisionInDevelopment")
                    ? AutoCreate.CreateOrUpdate
                    : AutoCreate.None;
        });
    }
}
