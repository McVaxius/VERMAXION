#nullable enable

using System.Collections.Generic;
using VERMAXION.Models;
using Xunit;

namespace VERMAXION.Tests;

public sealed class HenchmanTakeoverPolicyTests
{
    [Fact]
    public void AbsentOrDisabledHenchmanAllowsTakeover()
    {
        var result = Evaluate(loaded: false);

        Assert.True(result.AllowTakeover);
        Assert.False(result.Loaded);
    }

    [Fact]
    public void LoadedIdleHenchmanAllowsTakeover()
    {
        var result = Evaluate(loaded: true, busyReadSucceeded: true, busy: false);

        Assert.True(result.AllowTakeover);
        Assert.False(result.Busy);
    }

    [Fact]
    public void BusyOnABoatInExactWaitingStateAllowsTakeover()
    {
        var result = Evaluate(
            loaded: true,
            busyReadSucceeded: true,
            busy: true,
            stateReadSucceeded: true,
            taskName: HenchmanTakeoverPolicy.SafeTaskName,
            taskDescription: HenchmanTakeoverPolicy.SafeTaskDescription);

        Assert.True(result.AllowTakeover);
    }

    [Theory]
    [MemberData(nameof(UnsafeOnABoatDescriptions))]
    public void BusyOnABoatOutsideExactWaitingStateBlocks(string description)
    {
        var result = Evaluate(
            loaded: true,
            busyReadSucceeded: true,
            busy: true,
            stateReadSucceeded: true,
            taskName: HenchmanTakeoverPolicy.SafeTaskName,
            taskDescription: description);

        Assert.False(result.AllowTakeover);
        Assert.Equal(description, result.TaskDescription);
    }

    [Fact]
    public void OtherBusyHenchmanTaskBlocks()
    {
        var result = Evaluate(
            loaded: true,
            busyReadSucceeded: true,
            busy: true,
            stateReadSucceeded: true,
            taskName: "On Your Mark",
            taskDescription: HenchmanTakeoverPolicy.SafeTaskDescription);

        Assert.False(result.AllowTakeover);
    }

    [Theory]
    [InlineData(false, true, "Henchman.IsBusy IPC failed")]
    [InlineData(true, false, "Henchman reflection failed")]
    public void LoadedHenchmanReadFailureBlocks(bool busyReadSucceeded, bool stateReadSucceeded, string failure)
    {
        var result = Evaluate(
            loaded: true,
            busyReadSucceeded,
            busy: true,
            stateReadSucceeded,
            failureReason: failure);

        Assert.False(result.AllowTakeover);
        Assert.Equal(failure, result.Reason);
    }

    public static IEnumerable<object[]> UnsafeOnABoatDescriptions()
    {
        yield return ["Waiting for boarding SelectString"];
        yield return ["Targeting Dryskthota"];
        yield return ["Waiting for reel in"];
        yield return ["Waiting for Ocean Fishing results"];
        yield return [string.Empty];
        yield return ["Unknown state"];
        yield return ["waiting for Ocean Fishing time window"];
    }

    private static HenchmanTakeoverReadiness Evaluate(
        bool loaded,
        bool busyReadSucceeded = false,
        bool busy = false,
        bool stateReadSucceeded = false,
        string? taskName = null,
        string? taskDescription = null,
        string? failureReason = null)
        => HenchmanTakeoverPolicy.Evaluate(
            loaded,
            busyReadSucceeded,
            busy,
            stateReadSucceeded,
            taskName,
            taskDescription,
            failureReason);
}
