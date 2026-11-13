using System.Net;
using Microsoft.Extensions.Configuration;

namespace Aba.Starter.Platform.Messaging;

public enum AbaMessagingTransport
{
    Local,
    RabbitMq,
    Kafka
}

/// <summary>Validated technical transport settings. Never contains a formatted secret in errors.</summary>
public sealed record AbaMessagingOptions(
    bool Enabled,
    AbaMessagingTransport Transport,
    string? RabbitMqConnectionString,
    string? KafkaBootstrapServers,
    string? KafkaSecurityProtocol,
    string? KafkaSaslMechanism,
    string? KafkaSaslUsername,
    string? KafkaSaslPassword,
    bool AutoProvisionInDevelopment)
{
    public override string ToString() =>
        $"AbaMessagingOptions {{ Enabled = {Enabled}, Transport = {Transport}, SecretConnectionSettings = [REDACTED] }}";

    public static AbaMessagingOptions FromConfiguration(
        IConfiguration configuration, bool isDevelopment)
    {
        if (!configuration.GetValue<bool>("Platform:Messaging:Enabled"))
            return new(false, AbaMessagingTransport.Local, null, null, null, null, null, null, false);

        var autoProvision = configuration.GetValue<bool>(
            "Platform:Messaging:AutoProvisionInDevelopment");
        if (autoProvision && !isDevelopment)
            throw new InvalidOperationException(
                "Platform:Messaging:AutoProvisionInDevelopment can only be enabled in Development.");

        var transportName = configuration["Platform:Messaging:Transport"];
        var transport = string.IsNullOrWhiteSpace(transportName)
            ? AbaMessagingTransport.Local
            : transportName.Trim().ToLowerInvariant() switch
            {
                "local" => AbaMessagingTransport.Local,
                "rabbitmq" => AbaMessagingTransport.RabbitMq,
                "kafka" => AbaMessagingTransport.Kafka,
                _ => throw new InvalidOperationException(
                    "Platform:Messaging:Transport must be Local, RabbitMQ or Kafka.")
            };

        if (transport == AbaMessagingTransport.Local)
            return new(true, transport, null, null, null, null, null, null, autoProvision);

        if (transport == AbaMessagingTransport.RabbitMq)
        {
            var connectionString = configuration.GetConnectionString("AbaRabbitMQ");
            if (!Uri.TryCreate(connectionString, UriKind.Absolute, out var uri)
                || uri.Host.Length == 0
                || (uri.Scheme != "amqp" && uri.Scheme != "amqps"))
                throw new InvalidOperationException(
                    "ConnectionStrings:AbaRabbitMQ must be an absolute amqp:// or amqps:// URI with a host.");

            if (!isDevelopment)
            {
                var credentials = uri.UserInfo;
                var separator = credentials.IndexOf(':');
                var userName = separator > 0
                    ? Uri.UnescapeDataString(credentials[..separator])
                    : string.Empty;
                var password = separator >= 0 && separator < credentials.Length - 1
                    ? Uri.UnescapeDataString(credentials[(separator + 1)..])
                    : string.Empty;
                if (uri.Scheme != "amqps"
                    || string.IsNullOrWhiteSpace(userName)
                    || string.IsNullOrWhiteSpace(password)
                    || (userName.Equals("guest", StringComparison.OrdinalIgnoreCase)
                        && password.Equals("guest", StringComparison.Ordinal)))
                    throw new InvalidOperationException(
                        "Production RabbitMQ requires amqps:// with a non-guest username and password.");
            }

            return new(true, transport, connectionString, null, null, null, null, null, autoProvision);
        }

        return ParseKafka(configuration, isDevelopment, autoProvision);
    }

    private static AbaMessagingOptions ParseKafka(
        IConfiguration configuration, bool isDevelopment, bool autoProvision)
    {
        var bootstrapServers = configuration.GetConnectionString("AbaKafka")?.Trim();
        var bootstrapEntries = bootstrapServers?.Split(',', StringSplitOptions.TrimEntries);
        if (bootstrapEntries is null
            || bootstrapEntries.Length == 0
            || bootstrapEntries.Any(server => !IsKafkaBootstrapServer(server)))
            throw new InvalidOperationException(
                "ConnectionStrings:AbaKafka must contain one or more host:port bootstrap servers.");

        var kafkaSettings = configuration.GetSection("Platform:Messaging:Kafka");
        var securityProtocol = kafkaSettings["SecurityProtocol"]?.Trim();
        var saslMechanism = kafkaSettings["SaslMechanism"]?.Trim();
        var saslUsername = kafkaSettings["SaslUsername"];
        var saslPassword = kafkaSettings["SaslPassword"];
        var hasSaslSetting = !string.IsNullOrWhiteSpace(saslMechanism)
            || !string.IsNullOrWhiteSpace(saslUsername)
            || !string.IsNullOrWhiteSpace(saslPassword);

        if (string.IsNullOrWhiteSpace(securityProtocol))
            securityProtocol = "Plaintext";
        if (!Enum.TryParse<Confluent.Kafka.SecurityProtocol>(securityProtocol, true, out var parsedProtocol)
            || !Enum.IsDefined(parsedProtocol))
            throw new InvalidOperationException(
                "Platform:Messaging:Kafka:SecurityProtocol must be a supported Kafka security protocol.");

        var saslProtocol = parsedProtocol is Confluent.Kafka.SecurityProtocol.SaslSsl
            or Confluent.Kafka.SecurityProtocol.SaslPlaintext;
        if (hasSaslSetting != saslProtocol)
            throw new InvalidOperationException(
                "Kafka SASL settings and SASL security protocol must be configured together.");

        if (hasSaslSetting && (string.IsNullOrWhiteSpace(saslMechanism)
            || string.IsNullOrWhiteSpace(saslUsername)
            || string.IsNullOrWhiteSpace(saslPassword)))
            throw new InvalidOperationException(
                "Kafka SASL requires a mechanism, username and password together.");

        if (hasSaslSetting && (!Enum.TryParse<Confluent.Kafka.SaslMechanism>(
            saslMechanism, true, out var parsedMechanism)
            || !Enum.IsDefined(parsedMechanism)
            || parsedMechanism is not (Confluent.Kafka.SaslMechanism.Plain
                or Confluent.Kafka.SaslMechanism.ScramSha256
                or Confluent.Kafka.SaslMechanism.ScramSha512)))
            throw new InvalidOperationException(
                "Platform:Messaging:Kafka:SaslMechanism must be Plain, ScramSha256 or ScramSha512.");

        if (hasSaslSetting)
            saslMechanism = Enum.Parse<Confluent.Kafka.SaslMechanism>(saslMechanism!, true).ToString();

        if (!isDevelopment && (parsedProtocol != Confluent.Kafka.SecurityProtocol.SaslSsl
            || !hasSaslSetting))
            throw new InvalidOperationException(
                "Production Kafka requires SASL_SSL with PLAIN, SCRAM-SHA-256 or SCRAM-SHA-512 credentials.");

        return new(true, AbaMessagingTransport.Kafka, null, bootstrapServers,
            parsedProtocol.ToString(), saslMechanism, saslUsername, saslPassword, autoProvision);
    }

    private static bool IsKafkaBootstrapServer(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Contains('@', StringComparison.Ordinal)
            || value.Contains('/', StringComparison.Ordinal)
            || !Uri.TryCreate("tcp://" + value, UriKind.Absolute, out var uri))
            return false;

        return !string.IsNullOrWhiteSpace(uri.Host)
            && uri.Port is > IPEndPoint.MinPort and <= IPEndPoint.MaxPort
            && uri.UserInfo.Length == 0
            && uri.Query.Length == 0
            && uri.Fragment.Length == 0
            && uri.AbsolutePath == "/";
    }
}
