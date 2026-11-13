using System.Security.Claims;
using Aba.Starter.Platform;
using Aba.Starter.Platform.Messaging;
using Aba.Starter.Platform.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;
using Wolverine.Persistence.Durability;
using Wolverine.Persistence.Durability.DeadLetterManagement;
using Xunit;

namespace Aba.Starter.Platform.IntegrationTests;

/// <summary>Actual SQL and Wolverine poison/quarantine/replay with audited operator.</summary>
public sealed class DeadLetterOperatorIntegrationTests
{
    [Theory]
    [InlineData("PostgreSql", "ABA_TEST_POSTGRESQL")]
    [InlineData("SqlServer", "ABA_TEST_SQLSERVER")]
    public async Task FailedProbeQuarantinesAndOnlyExplicitlyAuthorizedReplaySucceeds(
        string provider, string env)
    {
        var connection = Environment.GetEnvironmentVariable(env);
        Assert.False(string.IsNullOrWhiteSpace(connection));
        var probeId = Guid.NewGuid();
        var key = probeId.ToString("N");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development"
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?>
        {
            ["Platform:Database:Provider"] = provider,
            ["ConnectionStrings:AbaDatabase"] = connection,
            ["Platform:Cache:Provider"] = "Memory",
            ["Platform:Messaging:Enabled"] = "true",
            ["Platform:Messaging:AutoProvisionInDevelopment"] = "true",
            ["Platform:Messaging:DeadLetterOperatorEnabled"] = "true",
            ["Platform:Audit:Enabled"] = "true",
            ["Platform:Audit:PrimaryKeyId"] = "key-test",
            ["Platform:Audit:Keys:key-test"] =
                Convert.ToBase64String(Enumerable.Repeat((byte)99, 32).ToArray())
        });
        builder.Services.AddHealthChecks();
        builder.AddAbaPlatform();
        await using var app = builder.Build();

        await using (var setup = app.Services.CreateAsyncScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<AbaPlatformDbContext>();
            for (var i=0;;i++)
            {
                try { await db.Database.MigrateAsync(); break; }
                catch (Exception ex) when (i < 29 &&
                    (ex is System.Data.Common.DbException or TimeoutException))
                {
                    await Task.Delay(2000);
                }
            }
        }

        await app.StartAsync();
        try
        {
            await using (var sender = app.Services.CreateAsyncScope())
            {
                var bus = sender.ServiceProvider.GetRequiredService<IMessageBus>();
                await bus.SendAsync(new AbaPoisonProbe(probeId));
            }
            var store = app.Services.GetRequiredService<IMessageStore>();
            DeadLetterEnvelope? dead = null;
            for (var i = 0; i < 120; i++)
            {
                var result = await store.DeadLetters.QueryAsync(
                    new DeadLetterEnvelopeQuery { PageSize = 100 },
                    CancellationToken.None);
                dead = result.Envelopes.FirstOrDefault(x =>
                    x.MessageType.Contains("AbaPoisonProbe", StringComparison.Ordinal));
                if (dead is not null) break;
                await Task.Delay(500);
            }
            Assert.NotNull(dead);
            Assert.False(dead.Replayable);

            await using var scope = app.Services.CreateAsyncScope();
            var admin = scope.ServiceProvider.GetRequiredService<AbaDeadLetterAdministration>();
            var anonymous = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim("sub", "operator-1"),
                new Claim(AbaDeadLetterAdministration.PermissionClaim, "deadletters:replay")
            ]));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                admin.RequestReplayAsync(dead.Id, dead.MessageType, dead.ExceptionType,
                    anonymous, "ticket-test-123"));
            var authenticated = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim("sub", "operator-1"),
                new Claim(AbaDeadLetterAdministration.PermissionClaim, "deadletters:inspect"),
                new Claim(AbaDeadLetterAdministration.PermissionClaim, "deadletters:replay")
            ], "validated-operator-identity"));
            // Negative tenant/operator bypass cases run against actual poison
            // envelopes on both disposable SQL providers.
            var splitPermission = new ClaimsPrincipal(new[]
            {
                new ClaimsIdentity([new Claim("sub", "operator-1")], "validated-operator-identity"),
                new ClaimsIdentity([
                    new Claim(AbaDeadLetterAdministration.PermissionClaim, "deadletters:replay")
                ])
            });
            var splitSubject = new ClaimsPrincipal(new[]
            {
                new ClaimsIdentity([
                    new Claim(AbaDeadLetterAdministration.PermissionClaim, "deadletters:inspect")
                ], "validated-operator-identity"),
                new ClaimsIdentity([new Claim("sub", "forged-operator")])
            });
            var ambiguousAuthenticated = new ClaimsPrincipal(new[]
            {
                new ClaimsIdentity([
                    new Claim("sub", "operator-1"),
                    new Claim(AbaDeadLetterAdministration.PermissionClaim, "deadletters:replay")
                ], "validated-operator-identity"),
                new ClaimsIdentity([new Claim("sub", "other-operator")], "other-authenticated-identity")
            });
            var auditBeforeDenials = await scope.ServiceProvider
                .GetRequiredService<AbaAuditLedger>().VerifyAsync("system");
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                admin.RequestReplayAsync(dead.Id, dead.MessageType, dead.ExceptionType,
                    splitPermission, "ticket-test-123"));
            await Assert.ThrowsAsync<ArgumentException>(() =>
                admin.InspectAsync(dead.Id, splitSubject, "ticket-test-123"));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                admin.RequestReplayAsync(dead.Id, dead.MessageType, dead.ExceptionType,
                    ambiguousAuthenticated, "ticket-test-123"));
            var auditAfterDenials = await scope.ServiceProvider
                .GetRequiredService<AbaAuditLedger>().VerifyAsync("system");
            Assert.True(auditAfterDenials.Valid, auditAfterDenials.Reason);
            Assert.Equal(auditBeforeDenials.VerifiedRecords, auditAfterDenials.VerifiedRecords);
            Assert.False((await store.DeadLetters.DeadLetterEnvelopeByIdAsync(dead.Id))!.Replayable);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                admin.RequestReplayAsync(dead.Id, "wrong-message", dead.ExceptionType,
                    authenticated, "ticket-test-123"));
            var inspected = await admin.InspectAsync(dead.Id, authenticated, "ticket-test-123");
            Assert.Equal(dead.MessageType, inspected.MessageType);
            Assert.Equal(dead.ExceptionType, inspected.ExceptionType);

            // Simulate a corrected dependency before touching the dead letter.
            var db = scope.ServiceProvider.GetRequiredService<AbaPlatformDbContext>();
            db.IdempotencyReceipts.Add(new IdempotencyReceipt
            {
                Scope = "aba-poison-fixed", Key = key, CreatedUtc = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
            Assert.True(await admin.RequestReplayAsync(dead.Id, dead.MessageType,
                dead.ExceptionType, authenticated, "ticket-test-123"));

            var handled = false;
            for (var i = 0; i < 120; i++)
            {
                await using var poll = app.Services.CreateAsyncScope();
                var check = poll.ServiceProvider.GetRequiredService<AbaPlatformDbContext>();
                if (await check.IdempotencyReceipts.AsNoTracking().AnyAsync(
                    x => x.Scope == "aba-poison-handled" && x.Key == key))
                {
                    handled = true;
                    break;
                }
                await Task.Delay(500);
            }
            Assert.True(handled, "The replayed dead letter did not complete after dependency repair.");
            var ledger = scope.ServiceProvider.GetRequiredService<AbaAuditLedger>();
            var verification = await ledger.VerifyAsync("system");
            Assert.True(verification.Valid, verification.Reason);
            Assert.True(verification.VerifiedRecords >= 3);
        }
        finally
        {
            await app.StopAsync();
        }
    }
}
