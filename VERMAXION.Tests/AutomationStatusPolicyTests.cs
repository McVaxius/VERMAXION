using System;
using VERMAXION.Models;
using Xunit;

namespace VERMAXION.Tests;

public sealed class AutomationStatusPolicyTests
{
    private static readonly DateTime GeneratedAt = new(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void IdleStatusUsesVersionedContract()
    {
        var status = AutomationStatusPolicy.Evaluate(new AutomationOwnershipSnapshot(), GeneratedAt);

        Assert.Equal(1, status.Version);
        Assert.False(status.IsBusy);
        Assert.Equal("Idle", status.Activity);
        Assert.Equal(GeneratedAt, status.GeneratedAtUtc);
    }

    [Theory]
    [InlineData(nameof(AutomationOwnershipSnapshot.EngineOwnsLiveWork), "Engine")]
    [InlineData(nameof(AutomationOwnershipSnapshot.FishingActive), "Fishing")]
    [InlineData(nameof(AutomationOwnershipSnapshot.FishingRelogActive), "FishingRelog")]
    [InlineData(nameof(AutomationOwnershipSnapshot.FishingRelogPending), "FishingRelog")]
    [InlineData(nameof(AutomationOwnershipSnapshot.FishingCleanupActive), "FishingCleanup")]
    [InlineData(nameof(AutomationOwnershipSnapshot.BeforeAutoRetainerActive), "BeforeAutoRetainer")]
    [InlineData(nameof(AutomationOwnershipSnapshot.SuppressionRecoveryActive), "SuppressionRecovery")]
    [InlineData(nameof(AutomationOwnershipSnapshot.ManualServiceActive), "ManualService")]
    [InlineData(nameof(AutomationOwnershipSnapshot.CharacterPostprocessRequested), "CharacterPostprocessPending")]
    public void EveryOwnershipSourceProducesBusyStatus(string property, string expectedActivity)
    {
        var snapshot = new AutomationOwnershipSnapshot
        {
            EngineOwnsLiveWork = property == nameof(AutomationOwnershipSnapshot.EngineOwnsLiveWork),
            FishingActive = property == nameof(AutomationOwnershipSnapshot.FishingActive),
            FishingRelogActive = property == nameof(AutomationOwnershipSnapshot.FishingRelogActive),
            FishingRelogPending = property == nameof(AutomationOwnershipSnapshot.FishingRelogPending),
            FishingCleanupActive = property == nameof(AutomationOwnershipSnapshot.FishingCleanupActive),
            BeforeAutoRetainerActive = property == nameof(AutomationOwnershipSnapshot.BeforeAutoRetainerActive),
            SuppressionRecoveryActive = property == nameof(AutomationOwnershipSnapshot.SuppressionRecoveryActive),
            ManualServiceActive = property == nameof(AutomationOwnershipSnapshot.ManualServiceActive),
            CharacterPostprocessRequested = property == nameof(AutomationOwnershipSnapshot.CharacterPostprocessRequested),
            State = "OwnedState",
            Summary = "Owned summary",
        };

        var status = AutomationStatusPolicy.Evaluate(snapshot, GeneratedAt);

        Assert.True(status.IsBusy);
        Assert.Equal(expectedActivity, status.Activity);
        Assert.Equal("OwnedState", status.State);
        Assert.Equal("Owned summary", status.Summary);
    }

    [Fact]
    public void FishingLifecyclePriorityKeepsCleanupAndRelogSpecific()
    {
        var cleanup = AutomationStatusPolicy.Evaluate(new AutomationOwnershipSnapshot
        {
            EngineOwnsLiveWork = true,
            FishingActive = true,
            FishingCleanupActive = true,
        }, GeneratedAt);
        var relog = AutomationStatusPolicy.Evaluate(new AutomationOwnershipSnapshot
        {
            EngineOwnsLiveWork = true,
            FishingActive = true,
            FishingCleanupActive = true,
            FishingRelogActive = true,
        }, GeneratedAt);

        Assert.Equal("FishingCleanup", cleanup.Activity);
        Assert.Equal("FishingRelog", relog.Activity);
    }
}
