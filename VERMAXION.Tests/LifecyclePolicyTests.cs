using System;
using System.Collections.Generic;
using VERMAXION.Models;
using Xunit;

namespace VERMAXION.Tests;

public sealed class LifecyclePolicyTests
{
    [Fact]
    public void RetainerlessTimerKeepsOneExpiryAndMeasuresFromOwnedCompletion()
    {
        var account = new AccountConfig();
        Assert.False(account.RetainerlessTimerEnabled);
        Assert.Equal(30, account.RetainerlessTimerIntervalMinutes);
        account.RetainerlessTimerIntervalMinutes = 0;
        Assert.Equal(1, account.RetainerlessTimerIntervalMinutes);
        var now = new DateTime(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);
        DateTime? due = LifecyclePolicy.UpdateRetainerlessTimerDueUtc(
            now, null, 30, false, true, false, false, null);
        Assert.Null(due);
        due = LifecyclePolicy.UpdateRetainerlessTimerDueUtc(now, due, 30, true, true, true, false, null);
        Assert.Equal(now.AddMinutes(30), due);
        Assert.False(LifecyclePolicy.CanStartRetainerlessTimer(now, due, true, false, false, true, default));

        // Repeated blocked opportunities retain one expiry, regardless of the number of intervals missed.
        for (var interval = 1; interval <= 5; interval++)
        {
            var blockedNow = now.AddHours(interval);
            due = LifecyclePolicy.UpdateRetainerlessTimerDueUtc(blockedNow, due, 30, true, true, false, false, null);
            Assert.Equal(now.AddMinutes(30), due);
            Assert.False(LifecyclePolicy.CanStartRetainerlessTimer(blockedNow, due, false, false, false, true, default));
        }
        var start = now.AddHours(5);
        Assert.True(LifecyclePolicy.CanStartRetainerlessTimer(start, due, true, false, false, true, default));
        var clear = new SuppressionSnapshot(true, false, false);
        Assert.True(LifecyclePolicy.CanStartRetainerlessTimer(start, due, true, true, true, false, clear));
        Assert.False(LifecyclePolicy.CanStartRetainerlessTimer(start, due, true, true, false, false, clear));
        Assert.False(LifecyclePolicy.CanStartRetainerlessTimer(start, due, true, true, true, true, clear));
        Assert.False(LifecyclePolicy.CanStartRetainerlessTimer(start, due, true, true, true, false, default));
        Assert.False(LifecyclePolicy.CanStartRetainerlessTimer(start, due, true, true, true, false, new(true, true, false)));
        Assert.False(LifecyclePolicy.CanStartRetainerlessTimer(start, due, true, true, true, false, new(true, false, true)));

        // Both phase placements retain the ordinary enabled/due filter and configured ordering.
        Assert.Equal(["before", "after"], LifecyclePolicy.BuildRunnableQueue(
            ["before", "disabled", "after", "not-due"], _ => true, id => id is "before" or "after"));
        due = LifecyclePolicy.UpdateRetainerlessTimerDueUtc(start, due, 30, true, true, false, true, null);
        Assert.Null(due);
        var completed = start.AddHours(2);
        due = LifecyclePolicy.UpdateRetainerlessTimerDueUtc(completed.AddSeconds(5), due, 30, true, true, false, false, completed);
        Assert.Equal(completed.AddMinutes(30), due);
        Assert.False(LifecyclePolicy.CanStartRetainerlessTimer(completed.AddMinutes(29), due, true, false, true, false, clear));
        Assert.True(LifecyclePolicy.CanStartRetainerlessTimer(completed.AddMinutes(30), due, true, false, true, false, clear));
        Assert.Equal(start.AddMinutes(30), LifecyclePolicy.UpdateRetainerlessTimerDueUtc(
            start, null, 30, true, true, false, false, start)); // Immediate/no-due completion.
        var cancelled = completed.AddMinutes(10);
        Assert.Equal(cancelled.AddMinutes(30), LifecyclePolicy.UpdateRetainerlessTimerDueUtc(
            cancelled.AddSeconds(5), null, 30, true, true, false, false, cancelled));

        // Logout/FULL STOP discard the expiry; login/reload/enable start a fresh full interval.
        Assert.Null(LifecyclePolicy.UpdateRetainerlessTimerDueUtc(completed, due, 30, true, false, false, false, null));
        Assert.Null(LifecyclePolicy.UpdateRetainerlessTimerDueUtc(completed, due, 30, false, true, false, false, null));
        Assert.Equal(completed.AddMinutes(30), LifecyclePolicy.UpdateRetainerlessTimerDueUtc(completed, null, 30, true, true, false, false, null));
        Assert.Equal(completed.AddMinutes(30), LifecyclePolicy.UpdateRetainerlessTimerDueUtc(completed, now, 30, true, true, true, false, null));
        Assert.Equal(SuppressionLeaseAction.PreserveExternal,
            SuppressionLeasePolicy.DecideRelease(false, SuppressionReadResult.Known(true)));
        Assert.Equal(SuppressionLeaseAction.Release,
            SuppressionLeasePolicy.DecideRelease(true, SuppressionReadResult.Known(true)));
        Assert.Equal(SuppressionLeaseAction.WaitForRemote,
            SuppressionLeasePolicy.DecideRelease(true, SuppressionReadResult.Unknown("unreadable")));
    }

    [Fact]
    public void RunnableQueuePreservesConfiguredOrderAndFiltersSkippedTasks()
    {
        var queue = LifecyclePolicy.BuildRunnableQueue(
            ["third", "first", "disabled", "not-due", "second"],
            id => id != "third",
            id => id is "first" or "second");

        Assert.Equal(["first", "second"], queue);
    }

    [Fact]
    public void SkippedTaskAndNoWorkRunDoNotRequireSettling()
    {
        Assert.False(LifecyclePolicy.RequiresSettling(ownedWorkStarted: false));
    }

    [Fact]
    public void OwnedWorkRequiresSettlingWithoutTimeoutRelease()
    {
        Assert.True(LifecyclePolicy.RequiresSettling(ownedWorkStarted: true));
    }

    [Fact]
    public void NoWorkArPostprocessStillSettlesWhenExternalWorldIsBlocked()
    {
        Assert.True(LifecyclePolicy.RequiresFinalSettling(
            ownedWorkStarted: false,
            arPostprocessOwned: true,
            externalBlockerPresent: true));
    }

    [Fact]
    public void NoWorkArPostprocessCanFinishImmediatelyWhenWorldIsSafe()
    {
        Assert.False(LifecyclePolicy.RequiresFinalSettling(
            ownedWorkStarted: false,
            arPostprocessOwned: true,
            externalBlockerPresent: false));
    }

    [Fact]
    public void ReappearingFinalBlockerReturnsNoWorkArPostprocessToSettling()
    {
        Assert.False(LifecyclePolicy.RequiresFinalSettling(false, true, false));
        Assert.True(LifecyclePolicy.RequiresFinalSettling(false, true, true));
    }

    [Theory]
    [InlineData(900, true, true, false, false, false, false, false, false)]
    [InlineData(1163, true, true, false, false, false, false, false, false)]
    [InlineData(129, true, true, true, false, false, false, false, false)]
    [InlineData(129, true, true, false, true, false, false, false, false)]
    [InlineData(129, true, true, false, false, true, false, false, false)]
    [InlineData(129, true, true, false, false, false, true, false, false)]
    [InlineData(129, true, true, false, false, false, false, true, false)]
    [InlineData(129, true, true, false, false, false, false, false, true)]
    [InlineData(129, false, true, false, false, false, false, false, false)]
    [InlineData(129, true, false, false, false, false, false, false, false)]
    public void ExternalWorldBlockersRetainHandoff(
        uint territoryType,
        bool loggedIn,
        bool playerAvailable,
        bool lifestreamBusy,
        bool boundByDuty,
        bool dutyQueueActive,
        bool areaTransitionActive,
        bool occupiedOrCutscene,
        bool combatOrCasting)
    {
        var blocker = ExternalHandoffPolicy.GetBlocker(new ExternalHandoffSnapshot(
            territoryType,
            IKDResultVisible: false,
            IKDResultReady: false,
            lifestreamBusy,
            boundByDuty,
            dutyQueueActive,
            areaTransitionActive,
            occupiedOrCutscene,
            combatOrCasting,
            loggedIn,
            playerAvailable));

        Assert.NotNull(blocker);
    }

    [Fact]
    public void IKDResultReadinessIsReportedAsAnExternalBlocker()
    {
        var notReady = BuildSafeExternalSnapshot() with
        {
            IKDResultVisible = true,
            IKDResultReady = false,
        };
        var ready = notReady with { IKDResultReady = true };

        Assert.Contains("not ready", ExternalHandoffPolicy.GetBlocker(notReady));
        Assert.Equal("IKDResult addon is visible", ExternalHandoffPolicy.GetBlocker(ready));
    }

    [Fact]
    public void ClearedTransitionAndAvailablePlayerAreExternallySettled()
    {
        var transitioning = BuildSafeExternalSnapshot() with { AreaTransitionActive = true };
        var settled = transitioning with { AreaTransitionActive = false };

        Assert.Equal("area transition is active", ExternalHandoffPolicy.GetBlocker(transitioning));
        Assert.Null(ExternalHandoffPolicy.GetBlocker(settled));
    }

    [Fact]
    public void OverlappingStartIsRejected()
    {
        Assert.False(LifecyclePolicy.CanStart(isRunning: true));
        Assert.True(LifecyclePolicy.CanStart(isRunning: false));
    }

    [Fact]
    public void PreRunTimeoutSkipsOnlyBeforeWorkStarts()
    {
        var timeout = TimeSpan.FromSeconds(120);

        Assert.True(LifecyclePolicy.ShouldSkipBeforeArForTimeout(timeout, workStarted: false, timeout));
        Assert.False(LifecyclePolicy.ShouldSkipBeforeArForTimeout(timeout + TimeSpan.FromHours(1), workStarted: true, timeout));
    }

    [Theory]
    [InlineData(false, false, true, 1, false, BeforeArArmPolicy.MultiModeUnreadableReason)]
    [InlineData(true, false, true, 1, false, BeforeArArmPolicy.MultiModeOffReason)]
    [InlineData(true, true, false, 1, false, BeforeArArmPolicy.NoDueWorkReason)]
    [InlineData(true, true, true, 0, false, BeforeArArmPolicy.NoDueWorkReason)]
    [InlineData(true, true, true, 1, true, "ready")]
    public void BeforeArArmPolicyRequiresEnabledMultiModeAndDueWork(
        bool multiModeReadSucceeded,
        bool multiModeEnabled,
        bool characterEnabled,
        int dueTaskCount,
        bool expectedArm,
        string expectedReason)
    {
        var decision = BeforeArArmPolicy.Evaluate(
            multiModeReadSucceeded,
            multiModeEnabled,
            characterEnabled,
            dueTaskCount);

        Assert.Equal(expectedArm, decision.ShouldArm);
        Assert.Equal(expectedReason, decision.Reason);
    }

    [Fact]
    public void ArmedSuppressionStallReleasesAfterTimeoutWhileIdleAndArBusy()
    {
        var now = DateTime.UtcNow;
        var armedAt = now - TimeSpan.FromMinutes(5);

        Assert.True(BeforeArArmedStallPolicy.ShouldRelease(
            now,
            armedAt,
            BeforeArGateState.Armed,
            suppressionOwnedByVermaxion: true,
            engineOwnsActiveWork: false,
            loginTransitionStarted: false,
            autoRetainerBusy: true));
    }

    [Fact]
    public void ArmedSuppressionStallDoesNotReleaseBeforeTimeout()
    {
        var now = DateTime.UtcNow;
        var armedAt = now - TimeSpan.FromMinutes(4.99);

        Assert.False(BeforeArArmedStallPolicy.ShouldRelease(
            now,
            armedAt,
            BeforeArGateState.Armed,
            suppressionOwnedByVermaxion: true,
            engineOwnsActiveWork: false,
            loginTransitionStarted: false,
            autoRetainerBusy: true));
    }

    [Theory]
    [InlineData(BeforeArGateState.WaitingForWorldReady, false)]
    [InlineData(BeforeArGateState.Armed, true)]
    public void ArmedSuppressionStallDoesNotReleaseDuringLoginOrActiveWork(
        BeforeArGateState state,
        bool engineOwnsActiveWork)
    {
        var now = DateTime.UtcNow;
        var armedAt = now - TimeSpan.FromMinutes(6);

        Assert.False(BeforeArArmedStallPolicy.ShouldRelease(
            now,
            armedAt,
            state,
            suppressionOwnedByVermaxion: true,
            engineOwnsActiveWork,
            loginTransitionStarted: state == BeforeArGateState.WaitingForWorldReady,
            autoRetainerBusy: true));
    }

    [Theory]
    [MemberData(nameof(SuppressionCases))]
    public void SuppressionLeaseDecisionMatchesOwnershipContract(
        bool acquire,
        bool owned,
        SuppressionReadResult remote,
        SuppressionLeaseAction expected)
    {
        var actual = acquire
            ? SuppressionLeasePolicy.DecideAcquire(owned, remote)
            : SuppressionLeasePolicy.DecideRelease(owned, remote);

        Assert.Equal(expected, actual);
    }

    public static IEnumerable<object[]> SuppressionCases()
    {
        yield return [true, true, SuppressionReadResult.Known(false), SuppressionLeaseAction.Acquire];
        yield return [false, true, SuppressionReadResult.Known(false), SuppressionLeaseAction.ClearStaleOwnership];
        yield return [true, false, SuppressionReadResult.Unknown("IPC unavailable"), SuppressionLeaseAction.WaitForRemote];
        yield return [false, true, SuppressionReadResult.Unknown("IPC unavailable"), SuppressionLeaseAction.WaitForRemote];
        yield return [true, false, SuppressionReadResult.Known(true), SuppressionLeaseAction.PreserveExternal];
        yield return [false, false, SuppressionReadResult.Known(true), SuppressionLeaseAction.PreserveExternal];
        yield return [false, true, SuppressionReadResult.Known(true), SuppressionLeaseAction.Release];
    }

    private static ExternalHandoffSnapshot BuildSafeExternalSnapshot()
        => new(
            TerritoryType: 129,
            IKDResultVisible: false,
            IKDResultReady: false,
            LifestreamBusy: false,
            BoundByDuty: false,
            DutyQueueActive: false,
            AreaTransitionActive: false,
            OccupiedOrCutscene: false,
            CombatOrCasting: false,
            LoggedIn: true,
            PlayerAvailable: true);
}
