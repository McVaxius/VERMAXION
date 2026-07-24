#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using VERMAXION.Models;
using Xunit;

namespace VERMAXION.Tests;

public sealed class RetainerEquipmentPolicyTests
{
    private const ulong RetainerId = 10;

    [Fact]
    public void AllThreeSourceModesHaveDistinctGearsetAndArmoryBehavior()
    {
        var inventorySaved = Candidate(1, RetainerEquipmentSlot.Head, RetainerGearSource.Inventory, 10, 0, saved: true);
        var armoryFree = Candidate(2, RetainerEquipmentSlot.Head, RetainerGearSource.Armory, 20, 0);
        var armorySaved = Candidate(3, RetainerEquipmentSlot.Head, RetainerGearSource.Armory, 30, 0, saved: true);

        Assert.True(RetainerEquipmentPolicy.IsSourceEligible(
            inventorySaved, RetainerGearSourceMode.IgnoreArmory, nonUniqueOnly: false));
        Assert.False(RetainerEquipmentPolicy.IsSourceEligible(
            armoryFree, RetainerGearSourceMode.IgnoreArmory, nonUniqueOnly: false));

        Assert.False(RetainerEquipmentPolicy.IsSourceEligible(
            inventorySaved, RetainerGearSourceMode.IgnoreGearset, nonUniqueOnly: false));
        Assert.True(RetainerEquipmentPolicy.IsSourceEligible(
            armoryFree, RetainerGearSourceMode.IgnoreGearset, nonUniqueOnly: false));
        Assert.False(RetainerEquipmentPolicy.IsSourceEligible(
            armorySaved, RetainerGearSourceMode.IgnoreGearset, nonUniqueOnly: false));

        Assert.True(RetainerEquipmentPolicy.IsSourceEligible(
            armorySaved, RetainerGearSourceMode.AllGear, nonUniqueOnly: false));
    }

    [Fact]
    public void NonUniqueFilterRemainsIndependentOfSourceMode()
    {
        var unique = Candidate(1, RetainerEquipmentSlot.Head, RetainerGearSource.Inventory, 50, 0, unique: true);

        Assert.False(RetainerEquipmentPolicy.IsSourceEligible(
            unique, RetainerGearSourceMode.AllGear, nonUniqueOnly: true));
        Assert.True(RetainerEquipmentPolicy.IsSourceEligible(
            unique, RetainerGearSourceMode.IgnoreArmory, nonUniqueOnly: false));
    }

    [Fact]
    public void GatheringAllocationUsesPerceptionOnly()
    {
        var profile = Profile(
            RetainerMetricKind.GatheringPerception,
            currentMetric: 100,
            new Dictionary<RetainerEquipmentSlot, int> { [RetainerEquipmentSlot.Head] = 10 });
        var highItemLevel = Candidate(1, RetainerEquipmentSlot.Head, RetainerGearSource.Inventory, 999, 12);
        var highPerception = Candidate(2, RetainerEquipmentSlot.Head, RetainerGearSource.Inventory, 1, 40);

        var result = RetainerEquipmentPolicy.Allocate(
            [profile],
            [highItemLevel, highPerception],
            RetainerGearSourceMode.AllGear,
            nonUniqueOnly: false);

        Assert.Single(result.Moves);
        Assert.Equal(2u, result.Moves[0].Candidate.ItemId);
        Assert.Equal(130, result.ProjectedMetrics[RetainerId]);
    }

    [Fact]
    public void CombatAllocationMaximizesFinalAverageItemLevel()
    {
        var profile = Profile(
            RetainerMetricKind.CombatItemLevel,
            currentMetric: 10,
            new Dictionary<RetainerEquipmentSlot, int>
            {
                [RetainerEquipmentSlot.Head] = 10,
                [RetainerEquipmentSlot.Body] = 10,
            });
        var weakHead = Candidate(1, RetainerEquipmentSlot.Head, RetainerGearSource.Inventory, 20, 500);
        var strongHead = Candidate(2, RetainerEquipmentSlot.Head, RetainerGearSource.Inventory, 50, 0);
        var body = Candidate(3, RetainerEquipmentSlot.Body, RetainerGearSource.Inventory, 30, 0);

        var result = RetainerEquipmentPolicy.Allocate(
            [profile],
            [weakHead, strongHead, body],
            RetainerGearSourceMode.AllGear,
            nonUniqueOnly: false);

        Assert.Equal(2, result.Moves.Count);
        Assert.Contains(result.Moves, move => move.Candidate.ItemId == 2);
        Assert.Contains(result.Moves, move => move.Candidate.ItemId == 3);
        Assert.Equal(40, result.ProjectedMetrics[RetainerId]);
    }

    [Fact]
    public void CombatAllocationUsesAutoRetainerTwoHandedMainHandWeight()
    {
        var profile = Profile(
            RetainerMetricKind.CombatItemLevel,
            currentMetric: 10,
            new Dictionary<RetainerEquipmentSlot, int>
            {
                [RetainerEquipmentSlot.MainHand] = 10,
                [RetainerEquipmentSlot.Head] = 10,
            }) with
        {
            CombatSlotWeights = new Dictionary<RetainerEquipmentSlot, int>
            {
                [RetainerEquipmentSlot.MainHand] = 2,
                [RetainerEquipmentSlot.Head] = 1,
            },
            CombatMetricDivisor = 3,
        };
        var weapon = Candidate(1, RetainerEquipmentSlot.MainHand, RetainerGearSource.Inventory, 30, 0);
        var head = Candidate(2, RetainerEquipmentSlot.Head, RetainerGearSource.Inventory, 40, 0);

        var result = RetainerEquipmentPolicy.Allocate(
            [profile],
            [weapon, head],
            RetainerGearSourceMode.AllGear,
            nonUniqueOnly: false);

        Assert.Equal(2, result.Moves.Count);
        Assert.Equal(33, result.ProjectedMetrics[RetainerId]);
        Assert.Equal(40, result.Moves.Single(move => move.Slot == RetainerEquipmentSlot.MainHand).Improvement);
    }

    [Fact]
    public void RingAllocationNeverReusesOnePhysicalItem()
    {
        var profile = Profile(
            RetainerMetricKind.CombatItemLevel,
            currentMetric: 1,
            new Dictionary<RetainerEquipmentSlot, int>
            {
                [RetainerEquipmentSlot.RingLeft] = 1,
                [RetainerEquipmentSlot.RingRight] = 1,
            });
        var samePhysicalFirst = RingCandidate(1, containerSlot: 5, itemLevel: 50);
        var samePhysicalDuplicate = RingCandidate(2, containerSlot: 5, itemLevel: 60);
        var secondPhysical = RingCandidate(3, containerSlot: 6, itemLevel: 40);

        var result = RetainerEquipmentPolicy.Allocate(
            [profile],
            [samePhysicalFirst, samePhysicalDuplicate, secondPhysical],
            RetainerGearSourceMode.AllGear,
            nonUniqueOnly: false);

        Assert.Equal(2, result.Moves.Count);
        Assert.Equal(2, result.Moves.Select(move => move.Candidate.PhysicalKey).Distinct().Count());
    }

    [Fact]
    public void ZeroTargetsAreNoOpsAndUnknownPositiveTargetsRequireWork()
    {
        var unknown = Profile(
            RetainerMetricKind.CombatItemLevel,
            currentMetric: -1,
            new Dictionary<RetainerEquipmentSlot, int>());

        Assert.False(RetainerEquipmentPolicy.RequiresWork(unknown, 0, 0));
        Assert.True(RetainerEquipmentPolicy.RequiresWork(unknown, 1, 0));
    }

    [Fact]
    public void RetrySignatureOnlyChangesForMaterialInputs()
    {
        var candidate = Candidate(1, RetainerEquipmentSlot.Head, RetainerGearSource.Inventory, 10, 0);
        var first = RetainerEquipmentPolicy.BuildRetrySignature(
            100, 200, RetainerGearSourceMode.IgnoreGearset, true, [candidate]);
        var same = RetainerEquipmentPolicy.BuildRetrySignature(
            100, 200, RetainerGearSourceMode.IgnoreGearset, true, [candidate]);
        var targetChanged = RetainerEquipmentPolicy.BuildRetrySignature(
            101, 200, RetainerGearSourceMode.IgnoreGearset, true, [candidate]);
        var sourceChanged = RetainerEquipmentPolicy.BuildRetrySignature(
            100, 200, RetainerGearSourceMode.AllGear, true, [candidate]);

        Assert.Equal(first, same);
        Assert.NotEqual(first, targetChanged);
        Assert.NotEqual(first, sourceChanged);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CollectOnlyLeaseRestoresOriginalStateOnRelease(bool original)
    {
        var lease = new AutoRetainerCollectOnlyLease();

        Assert.True(lease.Acquire(original));
        Assert.False(lease.Acquire(!original));
        Assert.Equal(original, lease.Release());
        Assert.Null(lease.Release());
    }

    [Fact]
    public void ReadinessBlocksZeroTargets()
    {
        var result = Readiness(
            combatTarget: 0,
            perceptionTarget: 0,
            RetainerEquippingArProbe.Idle([Retainer(itemLevel: 1)]));

        Assert.False(result.CanRun);
        Assert.Equal(RetainerEquippingReadinessPolicy.ZeroTargetsReason, result.DisabledReason);
    }

    [Fact]
    public void ReadinessReportsUnreadableAndBusyAutoRetainerExactly()
    {
        var unreadableBusy = Readiness(
            100,
            0,
            RetainerEquippingArProbe.BusyReadFailed("IPC exploded"));
        var busy = Readiness(100, 0, RetainerEquippingArProbe.Busy());
        var unreadableRetainers = Readiness(
            100,
            0,
            RetainerEquippingArProbe.RetainerReadFailed("reflection failed"));

        Assert.Equal(
            "AutoRetainer busy state was unreadable: IPC exploded",
            unreadableBusy.DisabledReason);
        Assert.Equal(RetainerEquippingReadinessPolicy.AutoRetainerBusyReason, busy.DisabledReason);
        Assert.Equal(
            "AutoRetainer retainer data was unreadable: reflection failed",
            unreadableRetainers.DisabledReason);
    }

    [Fact]
    public void ReadinessDistinguishesNoSelectedRetainersFromTargetsMet()
    {
        var noneSelected = Readiness(
            100,
            0,
            RetainerEquippingArProbe.Idle([]));
        var targetMetWithVenture = Readiness(
            100,
            0,
            RetainerEquippingArProbe.Idle([
                Retainer(itemLevel: 100, hasVenture: true),
            ]));

        Assert.Equal(
            RetainerEquippingReadinessPolicy.NoEnabledRetainersReason,
            noneSelected.DisabledReason);
        Assert.Equal(
            RetainerEquippingReadinessPolicy.TargetsMetReason,
            targetMetWithVenture.DisabledReason);
    }

    [Fact]
    public void UnknownStatsAreTargetedAndReadyWhenNoVentureExists()
    {
        var result = Readiness(
            100,
            0,
            RetainerEquippingArProbe.Idle([
                Retainer(itemLevel: -1),
            ]));

        Assert.True(result.CanRun);
        Assert.Equal(1, result.TargetedRetainerCount);
        Assert.Equal("Ready: 1 targeted retainer", result.StatusText);
    }

    [Fact]
    public void AnyTargetedActiveVentureBlocksUntilAutoRetainerCollectsIt()
    {
        var result = Readiness(
            100,
            0,
            RetainerEquippingArProbe.Idle([
                Retainer(id: 1, itemLevel: 50),
                Retainer(id: 2, itemLevel: 60, hasVenture: true),
                Retainer(id: 3, itemLevel: 100, hasVenture: true),
            ]));

        Assert.False(result.CanRun);
        Assert.Equal(
            RetainerEquippingReadinessPolicy.ActiveTargetedVentureReason,
            result.DisabledReason);
    }

    [Fact]
    public void BelowTargetIdleRetainersAreReady()
    {
        var result = Readiness(
            100,
            0,
            RetainerEquippingArProbe.Idle([
                Retainer(id: 1, itemLevel: 50),
                Retainer(id: 2, itemLevel: 100, hasVenture: true),
            ]));

        Assert.True(result.CanRun);
        Assert.Equal(1, result.TargetedRetainerCount);
    }

    [Fact]
    public void OwnershipAndSessionBlockersHaveExactPriorityReasons()
    {
        var probe = RetainerEquippingArProbe.Idle([Retainer(itemLevel: 50)]);
        var loggedOut = RetainerEquippingReadinessPolicy.Evaluate(new(
            false, true, true, true, 100, 0, probe));
        var dad = RetainerEquippingReadinessPolicy.Evaluate(new(
            true, true, true, true, 100, 0, probe));
        var engine = RetainerEquippingReadinessPolicy.Evaluate(new(
            true, false, true, true, 100, 0, probe));
        var bell = RetainerEquippingReadinessPolicy.Evaluate(new(
            true, false, false, true, 100, 0, probe));

        Assert.Equal(RetainerEquippingReadinessPolicy.LoggedOutReason, loggedOut.DisabledReason);
        Assert.Equal(RetainerEquippingReadinessPolicy.DadOwnershipReason, dad.DisabledReason);
        Assert.Equal(RetainerEquippingReadinessPolicy.EngineActiveReason, engine.DisabledReason);
        Assert.Equal(RetainerEquippingReadinessPolicy.BellSessionReason, bell.DisabledReason);
    }

    [Fact]
    public void ReadinessCacheIsBriefAndForcedClickRefreshBypassesIt()
    {
        var cache = new RetainerEquippingArProbeCache(TimeSpan.FromSeconds(5));
        var now = new DateTime(2026, 7, 24, 10, 0, 0, DateTimeKind.Utc);
        var refreshCount = 0;
        RetainerEquippingArProbe Refresh()
        {
            refreshCount++;
            return RetainerEquippingArProbe.Idle([]);
        }

        cache.GetOrRefresh("character:targets", now, forceRefresh: false, Refresh);
        cache.GetOrRefresh("character:targets", now.AddSeconds(4), forceRefresh: false, Refresh);
        Assert.Equal(1, refreshCount);

        cache.GetOrRefresh("character:targets", now.AddSeconds(4), forceRefresh: true, Refresh);
        Assert.Equal(2, refreshCount);

        cache.GetOrRefresh("character:targets", now.AddSeconds(10), forceRefresh: false, Refresh);
        Assert.Equal(3, refreshCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancellationAndFullStopRestoreOriginalCollectOnlyAndClearCheckpoint(bool original)
    {
        var cancelDecision = RetainerCollectOnlyRestorationPolicy.Decide(
            checkpointPending: true,
            originalCollectOnly: original,
            preserveCheckpoint: false);
        var fullStopDecision = RetainerCollectOnlyRestorationPolicy.Decide(
            checkpointPending: true,
            originalCollectOnly: original,
            preserveCheckpoint: false);

        Assert.True(cancelDecision.ShouldRestore);
        Assert.Equal(original, cancelDecision.RestoreValue);
        Assert.True(cancelDecision.ClearCheckpoint);
        Assert.Equal(cancelDecision, fullStopDecision);
    }

    [Fact]
    public void LogoutRestorationCanPreserveTheCheckpointForRecovery()
    {
        var decision = RetainerCollectOnlyRestorationPolicy.Decide(
            checkpointPending: true,
            originalCollectOnly: false,
            preserveCheckpoint: true);

        Assert.True(decision.ShouldRestore);
        Assert.False(decision.RestoreValue);
        Assert.False(decision.ClearCheckpoint);
    }

    private static RetainerEquippingReadinessResult Readiness(
        int combatTarget,
        int perceptionTarget,
        RetainerEquippingArProbe probe) =>
        RetainerEquippingReadinessPolicy.Evaluate(new(
            CharacterLoggedIn: true,
            DadOwnsNewWorkBoundary: false,
            EngineRunActive: false,
            BellSessionActive: false,
            CombatItemLevelTarget: combatTarget,
            GatheringPerceptionTarget: perceptionTarget,
            AutoRetainer: probe));

    private static AutoRetainerRetainerSnapshot Retainer(
        ulong id = RetainerId,
        int itemLevel = 0,
        int perception = 0,
        bool hasVenture = false,
        uint jobId = 1) =>
        new(
            id,
            $"Retainer {id}",
            jobId,
            Level: 100,
            HasVenture: hasVenture,
            VentureEndsAtUnix: hasVenture ? 1 : 0,
            ItemLevel: itemLevel,
            Perception: perception);

    private static RetainerEquipmentProfile Profile(
        RetainerMetricKind metric,
        int currentMetric,
        IReadOnlyDictionary<RetainerEquipmentSlot, int> slots) =>
        new(RetainerId, metric, 100, currentMetric, slots);

    private static RetainerGearCandidate RingCandidate(uint itemId, int containerSlot, int itemLevel) =>
        new()
        {
            ItemId = itemId,
            Source = RetainerGearSource.Inventory,
            Container = 0,
            ContainerSlot = containerSlot,
            Slot = RetainerEquipmentSlot.RingLeft,
            IsRing = true,
            RequiredLevel = 1,
            ItemLevel = itemLevel,
            CompatibleRetainerIds = new HashSet<ulong> { RetainerId },
        };

    private static RetainerGearCandidate Candidate(
        uint itemId,
        RetainerEquipmentSlot slot,
        RetainerGearSource source,
        int itemLevel,
        int perception,
        bool saved = false,
        bool unique = false) =>
        new()
        {
            ItemId = itemId,
            Source = source,
            Container = (int)source,
            ContainerSlot = (int)itemId,
            Slot = slot,
            RequiredLevel = 1,
            ItemLevel = itemLevel,
            Perception = perception,
            IsInSavedGearset = saved,
            IsUnique = unique,
            CompatibleRetainerIds = new HashSet<ulong> { RetainerId },
        };
}
