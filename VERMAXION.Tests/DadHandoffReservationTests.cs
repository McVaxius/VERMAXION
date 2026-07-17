using System;
using System.Text.Json;
using VERMAXION.Models;
using Xunit;

namespace VERMAXION.Tests;

public sealed class DadHandoffReservationTests
{
    private static readonly DateTime Now = new(2026, 7, 11, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(DadHandoffReservationState.Pending, "Pending")]
    [InlineData(DadHandoffReservationState.Granting, "Granting")]
    [InlineData(DadHandoffReservationState.Granted, "Granted")]
    [InlineData(DadHandoffReservationState.Released, "Released")]
    [InlineData(DadHandoffReservationState.Rejected, "Rejected")]
    public void CanonicalStatusSerializationWritesStringStates(
        DadHandoffReservationState state,
        string expected)
    {
        var json = DadHandoffJson.SerializeStatus(new DadHandoffReservationStatus { State = state });

        using var document = JsonDocument.Parse(json);
        var stateElement = document.RootElement.GetProperty("state");
        Assert.Equal(JsonValueKind.String, stateElement.ValueKind);
        Assert.Equal(expected, stateElement.GetString());
    }

    [Fact]
    public void CanonicalStatusSerializationNeverFallsBackToUnknownNumericState()
    {
        Assert.Throws<JsonException>(() => DadHandoffJson.SerializeStatus(
            new DadHandoffReservationStatus { State = (DadHandoffReservationState)99 }));
    }

    [Fact]
    public void BusyReservationBlocksNewWorkUntilSafeGrant()
    {
        var machine = new DadHandoffReservationMachine();
        var busy = Observation(vermaxionBusy: true, arBusy: true, multiMode: true);

        var pending = machine.Reserve(Request(), busy, Now);

        Assert.Equal(DadHandoffReservationState.Pending, pending.State);
        Assert.True(machine.BlocksNewWork);
        var granting = machine.Observe(Observation(false, true, false), safeToGrant: false, Now.AddSeconds(1));
        Assert.Equal(DadHandoffReservationState.Granting, granting.State);
        var granted = machine.Observe(Observation(false, false, false), safeToGrant: true, Now.AddSeconds(2));
        Assert.Equal(DadHandoffReservationState.Granted, granted.State);
        Assert.True(machine.BlocksNewWork);
    }

    [Fact]
    public void RenewalIsIdempotentAndExtendsFixedLease()
    {
        var machine = new DadHandoffReservationMachine();
        var observation = Observation(false, false, false);
        var original = machine.Reserve(Request(), observation, Now);

        var renewed = machine.Reserve(Request(), observation, Now.AddSeconds(5));

        Assert.Equal(DadHandoffReservationState.Granting, renewed.State);
        Assert.Equal(original.CreatedAtUtc, renewed.CreatedAtUtc);
        Assert.Equal(Now.AddSeconds(20), renewed.LeaseExpiresUtc);
    }

    [Fact]
    public void ReleasedReservationCanReacquireSameTokenAsFreshAttempt()
    {
        var machine = new DadHandoffReservationMachine();
        var observation = Observation(false, false, false);
        machine.Reserve(Request(), observation, Now);
        Assert.Equal(
            DadHandoffReservationState.Granted,
            machine.Observe(observation, safeToGrant: true, Now.AddSeconds(1)).State);
        Assert.Equal(
            DadHandoffReservationState.Released,
            machine.Release("operation", observation, Now.AddSeconds(2)).State);

        var reacquired = machine.Reserve(Request(), observation, Now.AddSeconds(3));

        Assert.Equal(DadHandoffReservationState.Granting, reacquired.State);
        Assert.Equal(Now.AddSeconds(3), reacquired.CreatedAtUtc);
        Assert.Equal(Now.AddSeconds(18), reacquired.LeaseExpiresUtc);
        Assert.True(machine.BlocksNewWork);
        Assert.Equal(
            DadHandoffReservationState.Granted,
            machine.Observe(observation, safeToGrant: true, Now.AddSeconds(4)).State);
    }

    [Fact]
    public void ExpiredReservationCanReacquireSameTokenAsFreshAttempt()
    {
        var machine = new DadHandoffReservationMachine();
        var observation = Observation(false, false, false);
        machine.Reserve(Request(), observation, Now);
        Assert.Equal(
            DadHandoffReservationState.Granted,
            machine.Observe(observation, safeToGrant: true, Now.AddSeconds(1)).State);
        Assert.Equal(
            DadHandoffReservationState.Released,
            machine.Observe(observation, safeToGrant: false, Now.AddSeconds(15)).State);

        var reacquired = machine.Reserve(Request(), observation, Now.AddSeconds(16));

        Assert.Equal(DadHandoffReservationState.Granting, reacquired.State);
        Assert.Equal(Now.AddSeconds(16), reacquired.CreatedAtUtc);
        Assert.Equal(Now.AddSeconds(31), reacquired.LeaseExpiresUtc);
        Assert.True(machine.BlocksNewWork);
        Assert.Equal(
            DadHandoffReservationState.Granted,
            machine.Observe(observation, safeToGrant: true, Now.AddSeconds(17)).State);
    }

    [Fact]
    public void CrashLeaseExpiryReleasesVermaxion()
    {
        var machine = new DadHandoffReservationMachine();
        var observation = Observation(false, false, false);
        machine.Reserve(Request(), observation, Now);

        var released = machine.Observe(observation, safeToGrant: true, Now.AddSeconds(15));

        Assert.Equal(DadHandoffReservationState.Released, released.State);
        Assert.False(machine.BlocksNewWork);
    }

    [Fact]
    public void ConflictingTokenIsRejectedWithoutReplacingOwner()
    {
        var machine = new DadHandoffReservationMachine();
        var observation = Observation(true, true, true);
        machine.Reserve(Request(), observation, Now);
        var other = Request();
        other.OperationToken = "other";

        var rejected = machine.Reserve(other, observation, Now.AddSeconds(1));

        Assert.Equal(DadHandoffReservationState.Rejected, rejected.State);
        Assert.Equal("operation", machine.OperationToken);
        Assert.True(machine.BlocksNewWork);
    }

    [Theory]
    [InlineData(BeforeArGateState.Idle, true, true)]
    [InlineData(BeforeArGateState.WaitingForWorldReady, false, true)]
    [InlineData(BeforeArGateState.Armed, false, true)]
    [InlineData(BeforeArGateState.Running, true, false)]
    [InlineData(BeforeArGateState.Skipped, false, false)]
    public void OnlyUnstartedBeforeArGateYieldsToDad(
        BeforeArGateState gateState,
        bool loginPending,
        bool expected)
    {
        var snapshot = YieldSnapshot(gateState, loginPending);

        Assert.Equal(expected, DadHandoffBeforeArYieldPolicy.ShouldYield(snapshot));
    }

    [Fact]
    public void BeforeArGateNeverYieldsOverGenuinelyActiveWork()
    {
        var candidate = YieldSnapshot(BeforeArGateState.Armed, loginPending: true);

        Assert.False(DadHandoffBeforeArYieldPolicy.ShouldYield(candidate with { EngineOwnsActiveWork = true }));
        Assert.False(DadHandoffBeforeArYieldPolicy.ShouldYield(candidate with { FishingOwnsWork = true }));
        Assert.False(DadHandoffBeforeArYieldPolicy.ShouldYield(candidate with { ManualServiceOwnsWork = true }));
        Assert.False(DadHandoffBeforeArYieldPolicy.ShouldYield(candidate with { CharacterPostprocessRequested = true }));
        Assert.False(DadHandoffBeforeArYieldPolicy.ShouldYield(candidate with { CleanupOwnsWork = true }));
        Assert.False(DadHandoffBeforeArYieldPolicy.ShouldYield(candidate with { ReservationActive = false }));
    }

    [Fact]
    public void GrantPolicyDisablesMultiModeOnceThenWaitsForReadableArIdle()
    {
        var multiModeOn = Observation(vermaxionBusy: false, arBusy: true, multiMode: true);
        var draining = Observation(vermaxionBusy: false, arBusy: true, multiMode: false);
        var idle = Observation(vermaxionBusy: false, arBusy: false, multiMode: false);

        Assert.Equal(DadHandoffGrantAction.DisableMultiMode, DadHandoffGrantPolicy.Decide(multiModeOn));
        Assert.Equal(DadHandoffGrantAction.Wait, DadHandoffGrantPolicy.Decide(draining));
        Assert.Equal(DadHandoffGrantAction.Grant, DadHandoffGrantPolicy.Decide(idle));
        Assert.Equal(
            DadHandoffGrantAction.Wait,
            DadHandoffGrantPolicy.Decide(new DadHandoffObservation
            {
                MultiModeKnown = true,
                AutoRetainerBusyKnown = false,
            }));
    }

    [Fact]
    public void PreGrantMultiModeChangeRestoresOnlyWhenAttemptEndsWithoutGrant()
    {
        var lease = new DadHandoffPreGrantMultiModeLease();
        lease.EndWithoutGrant();
        Assert.False(lease.RestorePending);

        lease.RecordDisabledByVermaxion();
        lease.EndWithoutGrant();
        Assert.True(lease.RestorePending);

        lease.RecordRestored();
        Assert.False(lease.ChangedByVermaxion);
        Assert.False(lease.RestorePending);

        lease.RecordDisabledByVermaxion();
        lease.CompleteGrant();
        lease.EndWithoutGrant();
        Assert.False(lease.ChangedByVermaxion);
        Assert.False(lease.RestorePending);
    }

    private static DadHandoffReservationRequest Request()
        => new()
        {
            OperationToken = "operation",
            SchedulerRunId = "run",
            SlotId = "slot",
            AccountKey = "account",
            CharacterKey = "Character@World",
        };

    private static DadHandoffObservation Observation(bool vermaxionBusy, bool arBusy, bool multiMode)
        => new()
        {
            VermaxionBusy = vermaxionBusy,
            VermaxionActivity = vermaxionBusy ? "Engine" : "Idle",
            VermaxionState = vermaxionBusy ? "Running" : "Idle",
            AutoRetainerBusyKnown = true,
            AutoRetainerBusy = arBusy,
            MultiModeKnown = true,
            MultiModeEnabled = multiMode,
        };

    private static DadHandoffBeforeArYieldSnapshot YieldSnapshot(
        BeforeArGateState gateState,
        bool loginPending)
        => new(
            ReservationActive: true,
            GateState: gateState,
            LoginPending: loginPending,
            EngineOwnsActiveWork: false,
            FishingOwnsWork: false,
            ManualServiceOwnsWork: false,
            CharacterPostprocessRequested: false,
            CleanupOwnsWork: false);
}
