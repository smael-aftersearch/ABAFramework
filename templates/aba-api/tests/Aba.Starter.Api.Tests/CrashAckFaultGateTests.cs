using Aba.Starter.Platform.Messaging;
using Xunit;

namespace Aba.Starter.Api.Tests;

public sealed class CrashAckFaultGateTests
{
    [Fact]
    public void CrashAckModeMustBeCiOptedInBeforeDatabaseEffects()
    {
        var marker = Path.Combine(Path.GetTempPath(), "aba-crash-unit-marker");
        Assert.Throws<InvalidOperationException>(() =>
            AbaCrashAckProbeHandler.RequireIsolatedCrashMarker(marker, false, true));
        Assert.Throws<InvalidOperationException>(() =>
            AbaCrashAckProbeHandler.RequireIsolatedCrashMarker(marker, true, false));
        Assert.Throws<InvalidOperationException>(() =>
            AbaCrashAckProbeHandler.RequireIsolatedCrashMarker("", true, true));
        AbaCrashAckProbeHandler.RequireIsolatedCrashMarker(marker, true, true);
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
