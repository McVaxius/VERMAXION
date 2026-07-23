#nullable enable

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
