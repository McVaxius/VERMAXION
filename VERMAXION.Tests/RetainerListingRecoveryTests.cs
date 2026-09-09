using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VERMAXION.Models;
using Xunit;

namespace VERMAXION.Tests;

public sealed class RetainerListingRecoveryTests
{
    // Synthetic listings: two identical stacks, one other selected item and one unselected item.
    private static List<RetainerListing> Listings() =>
    [
        new(0, 100, 2, false, "Duplicate"),
        new(1, 100, 2, false, "Duplicate"),
        new(2, 200, 1, true, "Selected"),
        new(3, 300, 1, false, "Unselected"),
    ];

    public static IEnumerable<object[]> Interruptions()
    {
        foreach (var lossPoint in new[] { "before dispatch", "dispatch pending", "dispatch completed" })
        foreach (var unknown in new[] { "none", "suppression", "busy" })
        foreach (var random in new[] { false, true })
            yield return new object[] { lossPoint, unknown, random };
    }

    [Theory]
    [MemberData(nameof(Interruptions))]
    public void InterruptedRefillDrainsReconcilesAndFinishesOriginalSelections(
        string lossPoint, string unreadable, bool random)
    {
        var live = Listings();
        var progress = new RetainerListingProgress();
        var selected = random ? new[] { live[0], live[2] } : live.ToArray();
        progress.Initialize(selected);
        var expectedWithdrawals = selected.Length;
        var withdrawals = 0;

        // Finish a non-duplicate first, then interrupt while processing a duplicate.
        var completed = live[2];
        progress.BeginWithdrawal(completed, live);
        progress.MarkDispatched();
        live.Remove(completed);
        Assert.True(progress.VerifyWithdrawal(live));
        withdrawals++;

        var duplicate = live[1];
        progress.BeginWithdrawal(duplicate, live);
        if (lossPoint != "before dispatch")
            progress.MarkDispatched();
        if (lossPoint == "dispatch completed")
        {
            live.Remove(duplicate);
            withdrawals++;
        }

        var owned = new SuppressionSnapshot(true, true, true);
        var idle = PluginBusyReadResult.Known(false);
        var lost = owned with { RemoteSuppressed = false };
        Assert.NotNull(RetainerControlPolicy.GetBlocker(lost, idle, false));
        Assert.Equal(expectedWithdrawals - 1, progress.Remaining);

        // Unreadable IPC and queued work block even after the suppression write.
        if (unreadable == "suppression")
            Assert.NotNull(RetainerControlPolicy.GetBlocker(owned with { RemoteKnown = false }, idle, false));
        if (unreadable == "busy")
            Assert.NotNull(RetainerControlPolicy.GetBlocker(owned, PluginBusyReadResult.Failed("unavailable"), false));
        Assert.NotNull(RetainerControlPolicy.GetBlocker(owned, PluginBusyReadResult.Known(true), false));

        // A delayed callback can finish while AR drains. Reconciliation must count it once.
        if (lossPoint == "dispatch pending")
        {
            live.Remove(duplicate);
            withdrawals++;
        }
        Assert.Null(RetainerControlPolicy.GetBlocker(owned, idle, false));
        live = live.Select((listing, row) => listing with { Slot = row + 10 }).ToList();
        Assert.Equal(lossPoint != "before dispatch", progress.ReconcileAfterInterruption(live));
        Assert.False(progress.ReconcileAfterInterruption(live));

        // Initialization on a fresh scan cannot reroll or add the unselected duplicate.
        var remaining = progress.Remaining;
        progress.Initialize(Listings());
        Assert.Equal(remaining, progress.Remaining);
        while (progress.FindNext(live) is { } next)
        {
            Assert.True(next.Slot >= 10);
            progress.BeginWithdrawal(next, live);
            progress.MarkDispatched();
            live.Remove(next);
            Assert.True(progress.VerifyWithdrawal(live));
            Assert.False(progress.VerifyWithdrawal(live));
            withdrawals++;
        }
        Assert.Equal(0, progress.Remaining);
        Assert.Equal(expectedWithdrawals, withdrawals);
        Assert.Equal(random ? 1 : 0, live.Count(listing => listing.ItemId == 100));
        Assert.Equal(random ? 1 : 0, live.Count(listing => listing.ItemId == 300));

        // The next retainer starts only after the current target completes.
        progress.Reset();
        Assert.False(progress.Initialized);
        progress.Initialize([new(0, 400, 1, false, "Next retainer")]);
        var nextLive = new[] { progress.FindNext([new(7, 400, 1, false, "Next retainer")])! };
        progress.BeginWithdrawal(nextLive[0], nextLive);
        progress.MarkDispatched();
        Assert.True(progress.VerifyWithdrawal([]));
        Assert.Equal(0, progress.Remaining);

        // Release only VMX's lease after closure/quiet; a second release is a no-op.
        Assert.Equal(SuppressionLeaseAction.Release,
            SuppressionLeasePolicy.DecideRelease(true, SuppressionReadResult.Known(true)));
        Assert.Equal(SuppressionLeaseAction.None,
            SuppressionLeasePolicy.DecideRelease(false, SuppressionReadResult.Known(false)));
        Assert.Equal(SuppressionLeaseAction.PreserveExternal,
            SuppressionLeasePolicy.DecideRelease(false, SuppressionReadResult.Known(true)));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void RouteWaitingAndFormalHandoffDoNotDeadlockOnBusy(bool formal, bool routing)
    {
        var owned = new SuppressionSnapshot(true, true, true);
        var busy = PluginBusyReadResult.Known(true);
        Assert.Null(RetainerControlPolicy.GetBlocker(owned, busy, formal, routing));
        Assert.NotNull(RetainerControlPolicy.GetBlocker(owned with { RemoteSuppressed = false }, busy, formal, routing));
        Assert.NotNull(RetainerControlPolicy.GetBlocker(owned with { RemoteKnown = false }, busy, formal, routing));
        Assert.NotNull(RetainerControlPolicy.GetBlocker(owned with { OwnedByVermaxion = false }, busy, formal, routing));
        Assert.NotNull(RetainerControlPolicy.GetBlocker(owned, busy, false, false));
    }

    [Theory]
    [InlineData("Quitter ?", "Quitter ?", true)]
    [InlineData("\u0002Quitter ?\u0003\n", "Quitter ?", true)]
    [InlineData("やめますか？", "やめますか？", true)]
    [InlineData("Unrelated purchase?", "Quitter ?", false)]
    [InlineData("Prefix Quitter ? suffix", "Quitter ?", false)]
    [InlineData("", "Quitter ?", false)]
    [InlineData("Quitter ?", "", false)]
    public void BuybackRequiresTheEntireReadableLocalizedPrompt(string prompt, string expected, bool allowed)
        => Assert.Equal(allowed, RetainerControlPolicy.IsBuybackPrompt(prompt, expected));

    [Fact]
    public void QuantityAndQualityChangesCannotVerifyAnotherIdenticalListing()
    {
        var selected = new RetainerListing(0, 100, 2, false, "Selected");
        var other = selected with { Slot = 1, Quantity = 3, IsHq = true };
        var progress = new RetainerListingProgress();
        progress.Initialize([selected]);
        progress.BeginWithdrawal(selected, [selected, other]);
        progress.MarkDispatched();
        Assert.False(progress.VerifyWithdrawal([selected]));
        Assert.Equal(1, progress.Remaining);
        progress.Reset(); // Full Stop / character change discards pending work.
        Assert.False(progress.VerifyWithdrawal([]));
        Assert.Null(progress.FindNext([selected]));
    }

    [Fact]
    public void RepeatedRestartsRetainTheExistingNoProgressLimit()
    {
        var now = new DateTime(2026, 9, 8, 0, 0, 0, DateTimeKind.Utc);
        var progress = new RetainerListingProgress();
        var live = Listings();
        progress.Initialize([live[0]]);
        progress.BeginRecovery(now);
        progress.ExcludeOwnershipWait(TimeSpan.FromMinutes(10));
        Assert.False(progress.RecoveryTimedOut(now.AddMinutes(14)));
        progress.BeginRecovery(now.AddMinutes(14));
        progress.ReconcileAfterInterruption(live);
        progress.Initialize(live);
        Assert.True(progress.RecoveryTimedOut(now.AddMinutes(15)));

        progress.BeginWithdrawal(live[0], live);
        progress.MarkDispatched();
        live.RemoveAt(0);
        Assert.True(progress.VerifyWithdrawal(live));
        Assert.False(progress.RecoveryTimedOut(now.AddMinutes(16)));
        progress.BeginRecovery(now.AddMinutes(16));
        progress.Reset();
        Assert.False(progress.RecoveryTimedOut(now.AddHours(1)));
    }

    [Fact]
    public void ProductionWiringGuardsRecoveryCleanupAndHandoff()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string Read(string path) => File.ReadAllText(Path.Combine(root, "VERMAXION", path));
        var refill = Read("Services/RetainerListingRefillService.cs");
        var engine = Read("Services/VermaxionEngine.cs");
        var plugin = Read("Plugin.cs");
        var workshop = Read("Services/WorkshopBellService.cs");
        Assert.Contains("engine.ManualStartRefillListings()", Read("Windows/MainWindow.cs"));
        Assert.Contains("Engine.RequiresAutoRetainerSuppression", plugin);
        Assert.Contains("retainerListingRefillService.CancelForCharacterChange()", engine);
        Assert.Contains("TimeSpan.FromSeconds(2)", plugin[plugin.IndexOf("private void ProcessBeforeArSuppressionRecovery()")..]);
        Assert.True(refill.IndexOf("var blocker = ControlBlocker(state") < refill.IndexOf("if (IsTimedOut())"));
        Assert.Contains("interruptedElapsed.GetValueOrDefault(newState)", refill);
        Assert.Contains("progress.RecoveryTimedOut(DateTime.UtcNow)", refill);
        Assert.Contains("progress.ReconcileAfterInterruption(slots)", refill);
        Assert.Contains("IsExpectedActiveRetainer(CurrentTarget, out detail)", refill);
        Assert.Contains("StartOwned(route, ControlBlocker)", refill);
        Assert.Contains("controlBlocker?.Invoke(IsWaitingForRoute)", workshop);
        Assert.DoesNotContain("ClickYesIfVisible()", refill);

        var close = refill[refill.IndexOf("private bool TryCloseVisibleRetainerUi(")..];
        Assert.True(close.IndexOf("ControlBlocker(false)") < close.IndexOf("TryClickYesIfPromptAllowed"));
        Assert.True(close.IndexOf("GetAddonText(215)") < close.IndexOf("mode == RetainerUiCloseMode.FullClose && retainerListCloseSecondPending"));
        Assert.Contains("allowUnreadable: false", close);
        Assert.Contains("!buybackConfirmationClicked && RetainerControlPolicy.IsBuybackPrompt", close);
        Assert.Contains("GetAddonText(2383)", close);
        Assert.Contains("mode == RetainerUiCloseMode.ReturnToRetainerList && addonName == RetainerListAddonName", close);
        Assert.Contains("now - closeNoSurfaceSince < CloseNoSurfaceGrace", close);
        var handoff = engine[engine.IndexOf("case EngineState.SignalingARDone:")..];
        Assert.True(handoff.IndexOf("ReleaseSuppressionIfOwned()") < handoff.IndexOf("arService.FinishPostProcess()"));
        Assert.Contains("suppressionReleasedForHandoff = true", handoff);
    }
}
