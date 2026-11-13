using Aba.Starter.Platform.Messaging;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Aba.Starter.Api.Tests;

public sealed class AbaMessagingOptionsTests
{
    [Fact]
    public void DisabledMessagingIgnoresTransportAndDoesNotRequireRabbitConnection()
    {
        var options = Parse(new Dictionary<string, string?>
        {
            ["Platform:Messaging:Enabled"] = "false",
            ["Platform:Messaging:Transport"] = "invalid",
            ["ConnectionStrings:AbaRabbitMQ"] = "not-a-uri",
            ["ConnectionStrings:AbaKafka"] = "not-a-host:9092"
        }, isDevelopment: false);

        Assert.False(options.Enabled);
        Assert.Equal(AbaMessagingTransport.Local, options.Transport);
        Assert.Null(options.RabbitMqConnectionString);
    }

    [Fact]
    public void EnabledMessagingDefaultsToLocalTransportWithoutRabbitConnection()
    {
        var options = Parse(new Dictionary<string, string?>
        {
            ["Platform:Messaging:Enabled"] = "true"
        }, isDevelopment: false);

        Assert.True(options.Enabled);
        Assert.Equal(AbaMessagingTransport.Local, options.Transport);
        Assert.Null(options.RabbitMqConnectionString);
    }

    [Fact]
    public void EnabledMessagingRejectsUnknownTransport()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Parse(new Dictionary<string, string?>
        {
            ["Platform:Messaging:Enabled"] = "true",
            ["Platform:Messaging:Transport"] = "unsupported"
        }, isDevelopment: true));

        Assert.Contains("must be Local, RabbitMQ or Kafka", error.Message);
    }

    [Theory]
    [InlineData("not-a-uri")]
    [InlineData("amqp:///missing-host")]
    [InlineData("https://rabbit.example.test")]
    public void RabbitMqRejectsInvalidConnectionUrisWithoutEchoingConfiguration(string connection)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Parse(new Dictionary<string, string?>
        {
            ["Platform:Messaging:Enabled"] = "true",
            ["Platform:Messaging:Transport"] = "RabbitMQ",
            ["ConnectionStrings:AbaRabbitMQ"] = connection
        }, isDevelopment: true));

        Assert.Contains("absolute amqp:// or amqps:// URI", error.Message);
        Assert.DoesNotContain(connection, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("amqp://service:secret@rabbit.example.test/v1")]
    [InlineData("amqps://guest:guest@rabbit.example.test/v1")]
    [InlineData("amqps://user@rabbit.example.test/v1")]
    public void ProductionRequiresTlsAndNonGuestCredentialsWithoutLeakingSecret(string connection)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Parse(new Dictionary<string, string?>
        {
            ["Platform:Messaging:Enabled"] = "true",
            ["Platform:Messaging:Transport"] = "RabbitMQ",
            ["ConnectionStrings:AbaRabbitMQ"] = connection
        }, isDevelopment: false));

        Assert.Contains("Production RabbitMQ requires amqps://", error.Message);
        Assert.DoesNotContain("secret", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("guest:guest" + '@', error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DevelopmentAcceptsLocalGuestCredentialsAndDevOnlyProvisioning()
    {
        const string connection = "amqp://guest:guest@localhost:5672/%2F";
        var options = Parse(new Dictionary<string, string?>
        {
            ["Platform:Messaging:Enabled"] = "true",
            ["Platform:Messaging:Transport"] = "RabbitMQ",
            ["Platform:Messaging:AutoProvisionInDevelopment"] = "true",
            ["ConnectionStrings:AbaRabbitMQ"] = connection
        }, isDevelopment: true);

        Assert.Equal(AbaMessagingTransport.RabbitMq, options.Transport);
        Assert.Equal(connection, options.RabbitMqConnectionString);
        Assert.True(options.AutoProvisionInDevelopment);
    }

    [Fact]
    public void DevelopmentAcceptsKafkaBootstrapServersWithoutSasl()
    {
        var options = Parse(new Dictionary<string, string?>
        {
            ["Platform:Messaging:Enabled"] = "true",
            ["Platform:Messaging:Transport"] = "Kafka",
            ["ConnectionStrings:AbaKafka"] = "localhost:9092, kafka.internal:9093"
        }, isDevelopment: true);

        Assert.Equal(AbaMessagingTransport.Kafka, options.Transport);
        Assert.Equal("localhost:9092, kafka.internal:9093", options.KafkaBootstrapServers);
        Assert.Equal("Plaintext", options.KafkaSecurityProtocol);
        Assert.Null(options.KafkaSaslPassword);
    }

    [Theory]
    [InlineData("")]
    [InlineData("localhost")]
    [InlineData("kafka.example.test:70000")]
    [InlineData("kafka.example.test:0")]
    [InlineData("kafka.example.test:9092,")]
    [InlineData("user:secret@kafka.example.test:9092")]
    [InlineData("kafka.example.test:9092/path")]
    public void KafkaRejectsInvalidBootstrapServersWithoutEchoingInput(string bootstrapServers)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Parse(new Dictionary<string, string?>
        {
            ["Platform:Messaging:Enabled"] = "true",
            ["Platform:Messaging:Transport"] = "Kafka",
            ["ConnectionStrings:AbaKafka"] = bootstrapServers
        }, isDevelopment: true));

        Assert.Contains("host:port bootstrap servers", error.Message);
        Assert.DoesNotContain("secret", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("kafka.example", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Plaintext", "", "", "", "Production Kafka requires SASL_SSL")]
    [InlineData("SaslPlaintext", "Plain", "service", "secret", "Production Kafka requires SASL_SSL")]
    [InlineData("SaslSsl", "OAuthBearer", "service", "secret", "SaslMechanism must be Plain")]
    public void ProductionKafkaRequiresTlsAndSupportedSaslCredentials(
        string protocol, string mechanism, string username, string password, string expectedMessage)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Parse(new Dictionary<string, string?>
        {
            ["Platform:Messaging:Enabled"] = "true",
            ["Platform:Messaging:Transport"] = "Kafka",
            ["ConnectionStrings:AbaKafka"] = "kafka.example.test:9093",
            ["Platform:Messaging:Kafka:SecurityProtocol"] = protocol,
            ["Platform:Messaging:Kafka:SaslMechanism"] = mechanism,
            ["Platform:Messaging:Kafka:SaslUsername"] = username,
            ["Platform:Messaging:Kafka:SaslPassword"] = password
        }, isDevelopment: false));

        Assert.Contains(expectedMessage, error.Message);
        Assert.DoesNotContain("secret", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void KafkaRejectsPartialSaslSettings()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Parse(new Dictionary<string, string?>
        {
            ["Platform:Messaging:Enabled"] = "true",
            ["Platform:Messaging:Transport"] = "Kafka",
            ["ConnectionStrings:AbaKafka"] = "kafka.example.test:9093",
            ["Platform:Messaging:Kafka:SecurityProtocol"] = "SaslSsl",
            ["Platform:Messaging:Kafka:SaslMechanism"] = "Plain",
            ["Platform:Messaging:Kafka:SaslUsername"] = "service"
        }, isDevelopment: true));

        Assert.Contains("mechanism, username and password together", error.Message);
    }

    [Fact]
    public void ProductionKafkaAcceptsSaslSslScramWithCredentials()
    {
        var options = Parse(new Dictionary<string, string?>
        {
            ["Platform:Messaging:Enabled"] = "true",
            ["Platform:Messaging:Transport"] = "Kafka",
            ["ConnectionStrings:AbaKafka"] = "kafka.example.test:9093",
            ["Platform:Messaging:Kafka:SecurityProtocol"] = "SaslSsl",
            ["Platform:Messaging:Kafka:SaslMechanism"] = "ScramSha512",
            ["Platform:Messaging:Kafka:SaslUsername"] = "service",
            ["Platform:Messaging:Kafka:SaslPassword"] = "secret"
        }, isDevelopment: false);

        Assert.Equal(AbaMessagingTransport.Kafka, options.Transport);
        Assert.Equal("SaslSsl", options.KafkaSecurityProtocol);
        Assert.Equal("ScramSha512", options.KafkaSaslMechanism);
    }

    [Fact]
    public void MessagingOptionsStringRepresentationRedactsBrokerCredentials()
    {
        var options = Parse(new Dictionary<string, string?>
        {
            ["Platform:Messaging:Enabled"] = "true",
            ["Platform:Messaging:Transport"] = "Kafka",
            ["ConnectionStrings:AbaKafka"] = "kafka.example.test:9093",
            ["Platform:Messaging:Kafka:SecurityProtocol"] = "SaslSsl",
            ["Platform:Messaging:Kafka:SaslMechanism"] = "Plain",
            ["Platform:Messaging:Kafka:SaslUsername"] = "service-user",
            ["Platform:Messaging:Kafka:SaslPassword"] = "never-print-this-secret"
        }, isDevelopment: false);

        Assert.DoesNotContain("never-print-this-secret", options.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("service-user", options.ToString(), StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", options.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionRejectsDevelopmentTopologyProvisioning()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Parse(new Dictionary<string, string?>
        {
            ["Platform:Messaging:Enabled"] = "true",
            ["Platform:Messaging:AutoProvisionInDevelopment"] = "true"
        }, isDevelopment: false));

        Assert.Contains("only be enabled in Development", error.Message);
    }

    private static AbaMessagingOptions Parse(
        Dictionary<string, string?> values, bool isDevelopment)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        return AbaMessagingOptions.FromConfiguration(configuration, isDevelopment);
    }
}
