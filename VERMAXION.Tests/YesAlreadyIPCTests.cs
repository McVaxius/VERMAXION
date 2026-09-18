using System.Collections.Generic;
using Dalamud.Plugin.Services;
using VERMAXION.IPC;
using Xunit;

namespace VERMAXION.Tests;

public sealed class YesAlreadyIPCTests
{
    [Fact]
    public void PauseRestoresMissingOwnedEntryAndReleasePreservesOtherPlugin()
    {
        var stopRequests = Plugin.PluginInterface.GetOrCreateData<HashSet<string>>(
            "YesAlready.StopRequests", () => []);
        stopRequests.Clear();
        stopRequests.Add("OtherPlugin");
        using var ipc = new YesAlreadyIPC(new IPluginLog());

        ipc.Pause();
        Assert.True(ipc.IsPaused);
        Assert.Contains("VERMAXION", stopRequests);

        stopRequests.Remove("VERMAXION");
        Assert.True(ipc.IsPaused);
        ipc.Pause();
        ipc.Pause();

        Assert.True(ipc.IsPaused);
        Assert.Equal(2, stopRequests.Count);
        Assert.Contains("VERMAXION", stopRequests);
        Assert.Contains("OtherPlugin", stopRequests);

        ipc.Unpause();

        Assert.False(ipc.IsPaused);
        Assert.Equal("OtherPlugin", Assert.Single(stopRequests));

        ipc.Pause();
        ipc.Dispose();

        Assert.False(ipc.IsPaused);
        Assert.Equal("OtherPlugin", Assert.Single(stopRequests));
    }
}
