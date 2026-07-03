using System;
using System.Collections.Generic;
using VERMAXION.Models;
using Xunit;

namespace VERMAXION.Tests;

public sealed class LifecyclePolicyTests
{
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
}
