using Aba.Starter.Platform.Messaging;
using Xunit;

namespace Aba.Starter.Api.Tests;

public sealed class CrashAckFaultGateTests
{
    [Fact]
    public void CrashAckModeMustBeCiOptedInBeforeDatabaseEffects()
    {
        var directory = Path.Combine(Path.GetTempPath(),
            "aba-crash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var marker = Path.Combine(directory, "child-ready.marker");
        try
        {
            Assert.Throws<InvalidOperationException>(() =>
                AbaCrashAckProbeHandler.RequireIsolatedCrashMarker(marker, false, true));
            Assert.Throws<InvalidOperationException>(() =>
                AbaCrashAckProbeHandler.RequireIsolatedCrashMarker(marker, true, false));
            Assert.Throws<InvalidOperationException>(() =>
                AbaCrashAckProbeHandler.RequireIsolatedCrashMarker("", true, true));
            Assert.Throws<InvalidOperationException>(() =>
                AbaCrashAckProbeHandler.RequireIsolatedCrashMarker(
                    Path.Combine(Path.GetTempPath(), "aba-crash-unit-marker"), true, true));
            // A canonical fresh marker within the exact disposable directory is allowed.
            AbaCrashAckProbeHandler.RequireIsolatedCrashMarker(marker, true, true);
            Assert.False(File.Exists(marker));
        }
        finally
        {
            Directory.Delete(directory);
        }
    }

    [Fact]
    public void MarkerMustStayWithinTemporaryDirectory()
    {
        var temp = Path.GetFullPath(Path.GetTempPath());
        var outside = Path.GetFullPath(Path.Combine(temp, "..", "forbidden-marker"));
        Assert.Throws<InvalidOperationException>(() =>
            AbaCrashAckProbeHandler.RequireIsolatedCrashMarker(outside, true, true));
    }
}
