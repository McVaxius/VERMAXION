using VERMAXION.Models;
using Xunit;

namespace VERMAXION.Tests;

public sealed class LootGoblinMapGatherManualRunPolicyTests
{
    [Fact]
    public void IdleRowShowsDailyStatus()
    {
        var status = LootGoblinMapGatherRowPolicy.GetStatus(
            "Done today",
            LootGoblinMapGatherServiceState.Idle,
            "Idle");

        Assert.Equal("Done today", status);
    }

    [Theory]
    [InlineData(LootGoblinMapGatherServiceState.Running, "Waiting for GatherBuddy Reborn...")]
    [InlineData(LootGoblinMapGatherServiceState.Failed, "Gather job disabled for current character.")]
    public void RunningOrFailedRowShowsServiceStatus(
        LootGoblinMapGatherServiceState state,
        string serviceStatus)
    {
        var status = LootGoblinMapGatherRowPolicy.GetStatus("Daily", state, serviceStatus);

        Assert.Equal(serviceStatus, status);
    }

    [Fact]
    public void ManualSuccessStampsOnlyMatchingCharacter()
    {
        var matching = new LootGoblinMapGatherManualRunTracker();
        matching.Begin("Alice@World", AcceptedResponse());

        var other = new LootGoblinMapGatherManualRunTracker();
        other.Begin("Alice@World", AcceptedResponse());

        Assert.True(matching.Complete(SuccessResponse(), "Alice@World"));
        Assert.False(other.Complete(SuccessResponse(), "Bob@World"));
    }

    [Theory]
    [InlineData("Failed")]
    [InlineData("Cancelled")]
    public void FailureOrCancelNeverStamps(string state)
    {
        var tracker = new LootGoblinMapGatherManualRunTracker();
        tracker.Begin("Alice@World", AcceptedResponse());

        var response = new LootGoblinMapGatherResponse
        {
            Accepted = true,
            Terminal = true,
            Success = false,
            State = state,
            Message = state,
        };

        Assert.False(tracker.Complete(response, "Alice@World"));
    }

    private static LootGoblinMapGatherResponse AcceptedResponse()
        => new()
        {
            Accepted = true,
            Terminal = false,
            State = "Gathering",
        };

    private static LootGoblinMapGatherResponse SuccessResponse()
        => new()
        {
            Accepted = true,
            Terminal = true,
            Success = true,
            State = "Completed",
        };
}
