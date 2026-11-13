using Aba.Starter.Platform.Messaging;
using Xunit;

namespace Aba.Starter.Api.Tests;

public sealed class DeadLetterSummaryRedactionTests
{
    [Fact]
    public void OrdinaryStringFormattingRedactsTransientReplayCapability()
    {
        const string token = "one-time-replay-capability-secret";
        var summary = new AbaDeadLetterAdministration.AbaDeadLetterSummary(
            Guid.NewGuid(), "Example.Message", "Example.Exception", false, token);

        Assert.Equal(token, summary.ReplayToken);

        var formatted = new[]
        {
            summary.ToString(),
            $"{summary}",
            string.Format("{0}", summary)
        };

        foreach (var value in formatted)
        {
            Assert.DoesNotContain(token, value, StringComparison.Ordinal);
            Assert.Contains("ReplayToken = [REDACTED]", value, StringComparison.Ordinal);
        }
    }
}
