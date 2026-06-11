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
    public void AutomatedRunsGateHenchmanTakeoverAndManualRunsBypassIt()
    {
        Assert.True(LifecyclePolicy.ShouldGateHenchmanTakeover(automatedRun: true));
        Assert.False(LifecyclePolicy.ShouldGateHenchmanTakeover(automatedRun: false));
    }

    [Fact]
    public void PreRunTimeoutSkipsOnlyBeforeWorkStarts()
    {
        var timeout = TimeSpan.FromSeconds(120);

        Assert.True(LifecyclePolicy.ShouldSkipBeforeArForTimeout(timeout, workStarted: false, timeout));
        Assert.False(LifecyclePolicy.ShouldSkipBeforeArForTimeout(timeout + TimeSpan.FromHours(1), workStarted: true, timeout));
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
