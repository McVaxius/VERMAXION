using System;
using System.IO;
using Xunit;

namespace VERMAXION.Tests;

public sealed class InnParkTerritoryPolicyTests
{
    [Fact]
    public void RosterAssignmentDiagnosticDoesNotLogCharacterIdentity()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var service = File.ReadAllText(Path.Combine(root, "VERMAXION", "Services", "FishingService.cs"));
        var start = service.IndexOf("private int ResolveAssignedSpot", StringComparison.Ordinal);
        var end = service.IndexOf("private static Vector3[] SnapshotOtherPlayerPositions", start, StringComparison.Ordinal);
        var method = service[start..end];

        Assert.DoesNotContain("self={myKey}", method);
        Assert.Contains("assignedSpot={assignedSpotIndex}", method);
    }

    [Fact]
    public void ChatMessagesReachFishingInventoryRecovery()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var plugin = File.ReadAllText(Path.Combine(root, "VERMAXION", "Plugin.cs"));
        var start = plugin.IndexOf("private void OnChatMessage", StringComparison.Ordinal);
        var end = plugin.IndexOf("private void OnARCharacterReady", start, StringComparison.Ordinal);
        var method = plugin[start..end];

        Assert.Contains("FishingService.HandleChatMessage(message.Message.TextValue);", method);
    }

    [Fact]
    public void IdleInnParkDoesNotRunDuringAnAutoRetainerPostprocess()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var plugin = File.ReadAllText(Path.Combine(root, "VERMAXION", "Plugin.cs"));
        var start = plugin.IndexOf("private void ProcessIdleInnPark()", StringComparison.Ordinal);
        var end = plugin.IndexOf("private void ProcessStuckDetectionSuppression()", start, StringComparison.Ordinal);
        var method = plugin[start..end];

        var guard = method.IndexOf("if (ARPostProcessService.IsRequested)", StringComparison.Ordinal);
        var busyRead = method.IndexOf("var arBusy = AutoRetainerIPC.ReadBusyState();", StringComparison.Ordinal);
        var busyGuard = method.IndexOf("if (!arBusy.Success || arBusy.Busy)", StringComparison.Ordinal);
        var timerUpdate = method.IndexOf("nextInnParkCheckUtc =", StringComparison.Ordinal);
        var firstCommand = method.IndexOf("LifestreamIPC.ExecuteCommand", StringComparison.Ordinal);

        Assert.True(guard >= 0, "InnPark must yield while an AutoRetainer character postprocess is requested.");
        Assert.True(busyRead >= 0, "InnPark must read AutoRetainer state before issuing movement.");
        Assert.True(busyGuard >= 0, "InnPark must yield while AutoRetainer reports busy or unreadable.");
        Assert.True(guard < timerUpdate, "The postprocess guard must run before InnPark consumes its timer.");
        Assert.True(busyGuard < timerUpdate, "The AutoRetainer busy guard must run before InnPark consumes its timer.");
        Assert.True(guard < firstCommand, "The postprocess guard must run before InnPark issues Lifestream commands.");
        Assert.True(busyGuard < firstCommand, "The AutoRetainer busy guard must run before InnPark issues Lifestream commands.");
    }

    [Fact]
    public void IdleInnParkUsesOfficialInnTerritoryCatalog()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var plugin = File.ReadAllText(Path.Combine(root, "VERMAXION", "Plugin.cs"));
        var start = plugin.IndexOf("private void ProcessIdleInnPark()", StringComparison.Ordinal);
        var end = plugin.IndexOf("private void ProcessStuckDetectionSuppression()", start, StringComparison.Ordinal);
        var method = plugin[start..end];

        Assert.Contains("Inns.List.Any(inn => inn == territory)", method);
        Assert.DoesNotContain("Array.IndexOf(InnTerritories, territory)", method);
    }

    [Fact]
    public void IdleInnParkDoesNotExitAnAlreadyParkedInn()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var plugin = File.ReadAllText(Path.Combine(root, "VERMAXION", "Plugin.cs"));
        var start = plugin.IndexOf("private void ProcessIdleInnPark()", StringComparison.Ordinal);
        var end = plugin.IndexOf("private void ProcessStuckDetectionSuppression()", start, StringComparison.Ordinal);
        var method = plugin[start..end];

        var inInn = method.IndexOf("var inInn = Inns.List.Any(inn => inn == territory);", StringComparison.Ordinal);
        var firstReturnAfterInnCheck = method.IndexOf("return;", inInn, StringComparison.Ordinal);
        var parkCommand = method.IndexOf("LifestreamIPC.ExecuteCommand(\"/li inn\")", StringComparison.Ordinal);

        Assert.DoesNotContain("LifestreamIPC.ExecuteCommand(\"/li limsa\")", method);
        Assert.True(inInn >= 0, "InnPark must classify the current territory before parking.");
        Assert.True(firstReturnAfterInnCheck > inInn && firstReturnAfterInnCheck < parkCommand,
            "An already-parked character must return before the /li inn parking command.");
    }
}
