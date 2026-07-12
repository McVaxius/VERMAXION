using System;
using VERMAXION.Models;
using Xunit;

namespace VERMAXION.Tests;

public sealed class DadHandoffReservationTests
{
    private static readonly DateTime Now = new(2026, 7, 11, 12, 0, 0, DateTimeKind.Utc);

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
        machine.Reserve(Request(), observation, Now);

        var renewed = machine.Reserve(Request(), observation, Now.AddSeconds(5));

        Assert.Equal(DadHandoffReservationState.Granting, renewed.State);
        Assert.Equal(Now.AddSeconds(20), renewed.LeaseExpiresUtc);
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
}
