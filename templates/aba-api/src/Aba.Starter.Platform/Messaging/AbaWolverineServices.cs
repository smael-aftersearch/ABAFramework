using Aba.Starter.Platform.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.Postgresql;
using Wolverine.SqlServer;
using Wolverine.RabbitMQ;
using Wolverine.Kafka;
using Confluent.Kafka;
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
        => AddAbaWolverine(builder, database, configureMessaging: null);

    public static void AddAbaWolverine(
        this WebApplicationBuilder builder,
        AbaDatabaseOptions database,
        Action<WolverineOptions>? configureMessaging)
    {
        var messaging = AbaMessagingOptions.FromConfiguration(
            builder.Configuration, builder.Environment.IsDevelopment());
        if (!messaging.Enabled)
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
            options.Policies.OnException<AbaPoisonProbeException>().MoveToErrorQueue();
            options.UseEntityFrameworkCoreTransactions();
            options.Policies.UseDurableLocalQueues();
            options.Policies.AutoApplyTransactions();

            if (messaging.Transport == AbaMessagingTransport.RabbitMq)
            {
                var rabbitMq = options.UseRabbitMq(messaging.RabbitMqConnectionString!);
                // The relational outbox may only delete a message after the
                // broker confirms it. Tracking makes a rejected publish throw
                // so the durable envelope remains available for retry.
                rabbitMq.ConfigureChannelCreation(channel =>
                {
                    channel.PublisherConfirmationsEnabled = true;
                    channel.PublisherConfirmationTrackingEnabled = true;
                });
                if (messaging.AutoProvisionInDevelopment)
                    rabbitMq.AutoProvision();
            }
            else if (messaging.Transport == AbaMessagingTransport.Kafka)
            {
                var kafka = options.UseKafka(messaging.KafkaBootstrapServers!);
                kafka.ConfigureClient(client =>
                {
                    if (!Enum.TryParse<SecurityProtocol>(
                        messaging.KafkaSecurityProtocol, true, out var securityProtocol))
                        throw new InvalidOperationException("The validated Kafka security protocol is unavailable.");
                    client.SecurityProtocol = securityProtocol;

                    if (!string.IsNullOrWhiteSpace(messaging.KafkaSaslMechanism))
                    {
                        if (!Enum.TryParse<SaslMechanism>(
                            messaging.KafkaSaslMechanism, true, out var saslMechanism))
                            throw new InvalidOperationException("The validated Kafka SASL mechanism is unavailable.");
                        client.SaslMechanism = saslMechanism;
                        client.SaslUsername = messaging.KafkaSaslUsername;
                        client.SaslPassword = messaging.KafkaSaslPassword;
                    }
                });
                kafka.ConfigureProducers(producer =>
                {
                    producer.EnableIdempotence = true;
                    producer.Acks = Acks.All;
                });
                if (messaging.AutoProvisionInDevelopment)
                    kafka.AutoProvision();
            }

            // Products add their own assembly discovery and explicitly versioned
            // message routes here. Platform provides no business queue or policy.
            configureMessaging?.Invoke(options);

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
