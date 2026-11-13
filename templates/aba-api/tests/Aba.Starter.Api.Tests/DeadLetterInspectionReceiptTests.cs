using Aba.Starter.Platform.Messaging;
using Aba.Starter.Platform.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Aba.Starter.Api.Tests;

public sealed class DeadLetterInspectionReceiptTests
{
    [Fact]
    public void ReceiptModelStoresOneBoundedHashPerEnvelopeAndHasShortLifetime()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        var options = new DbContextOptionsBuilder<AbaPlatformDbContext>()
            .UseSqlite(connection)
            .Options;
        using var db = new AbaPlatformDbContext(options);

        var entity = db.Model.FindEntityType(typeof(AbaDeadLetterInspectionReceipt));
        Assert.NotNull(entity);
        Assert.Equal("aba_deadletter_inspection_receipts", entity!.GetTableName());
        Assert.Equal(nameof(AbaDeadLetterInspectionReceipt.EnvelopeId),
            Assert.Single(entity.FindPrimaryKey()!.Properties).Name);
        Assert.Equal(64, entity.FindProperty(nameof(AbaDeadLetterInspectionReceipt.TokenHash))!.GetMaxLength());
        Assert.Equal(1024, entity.FindProperty(nameof(AbaDeadLetterInspectionReceipt.MessageType))!.GetMaxLength());
        Assert.Equal(1024, entity.FindProperty(nameof(AbaDeadLetterInspectionReceipt.ExceptionType))!.GetMaxLength());
        Assert.True(entity.FindProperty(nameof(AbaDeadLetterInspectionReceipt.ConsumedUtc))!.IsNullable);
        Assert.Equal(TimeSpan.FromMinutes(5), AbaDeadLetterAdministration.InspectionReceiptLifetime);
    }
}
