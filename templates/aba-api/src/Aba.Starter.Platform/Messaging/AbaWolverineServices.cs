using Aba.Starter.Platform.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;
using Wolverine.SqlServer;

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

        builder.Host.UseWolverine(options =>
        {
            if (database.Provider == AbaDatabaseProvider.PostgreSql)
                options.PersistMessagesWithPostgresql(database.ConnectionString, "aba_messages");
            else
                options.PersistMessagesWithSqlServer(database.ConnectionString, "aba_messages");

            options.UseEntityFrameworkCoreTransactions();
            options.Policies.UseDurableLocalQueues();
            options.Policies.AutoApplyTransactions();
        });
    }
}
