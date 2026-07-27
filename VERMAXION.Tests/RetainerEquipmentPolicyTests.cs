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

    [Fact]
    public void ExactFingerprintDistinguishesHqGlamourBothStainsAndEveryMateriaField()
    {
        var exact = Fingerprint(
            encodedItemId: RetainerGearFingerprint.EncodeItemId(500, highQuality: true),
            glamourId: 600,
            stain0Id: 7,
            stain1Id: 8,
            materia0Id: 10,
            materia1Id: 11,
            materia2Id: 12,
            materia3Id: 13,
            materia4Id: 14,
            materia0Grade: 1,
            materia1Grade: 2,
            materia2Grade: 3,
            materia3Grade: 4,
            materia4Grade: 5);

        Assert.NotEqual(exact, exact with { EncodedItemId = 500 });
        Assert.NotEqual(exact, exact with { GlamourId = 601 });
        Assert.NotEqual(exact, exact with { Stain0Id = 9 });
        Assert.NotEqual(exact, exact with { Stain1Id = 9 });
        Assert.NotEqual(exact, exact with { Materia0Id = 20 });
        Assert.NotEqual(exact, exact with { Materia1Id = 21 });
        Assert.NotEqual(exact, exact with { Materia2Id = 22 });
        Assert.NotEqual(exact, exact with { Materia3Id = 23 });
        Assert.NotEqual(exact, exact with { Materia4Id = 24 });
        Assert.NotEqual(exact, exact with { Materia0Grade = 6 });
        Assert.NotEqual(exact, exact with { Materia1Grade = 6 });
        Assert.NotEqual(exact, exact with { Materia2Grade = 6 });
        Assert.NotEqual(exact, exact with { Materia3Grade = 6 });
        Assert.NotEqual(exact, exact with { Materia4Grade = 6 });
    }

    [Fact]
    public void SavedGearsetCountsReserveOnlyExactCopiesAndLeaveIdenticalSurplusEligible()
    {
        var savedHq = Fingerprint(
            RetainerGearFingerprint.EncodeItemId(500, highQuality: true),
            glamourId: 600,
            stain0Id: 7,
            materia0Id: 10,
            materia0Grade: 2);
        var normal = savedHq with { EncodedItemId = 500 };
        var differentGlamour = savedHq with { GlamourId = 601 };
        var remaining = RetainerEquipmentPolicy.CountSavedGearsetFingerprints(
            [savedHq, savedHq]);

        Assert.False(RetainerEquipmentPolicy.ConsumeSavedGearsetReservation(remaining, normal));
        Assert.False(RetainerEquipmentPolicy.ConsumeSavedGearsetReservation(remaining, differentGlamour));
        Assert.True(RetainerEquipmentPolicy.ConsumeSavedGearsetReservation(remaining, savedHq));
        Assert.True(RetainerEquipmentPolicy.ConsumeSavedGearsetReservation(remaining, savedHq));
        Assert.False(RetainerEquipmentPolicy.ConsumeSavedGearsetReservation(remaining, savedHq));
    }

    [Fact]
    public void SameClassCombatRetainersAndGatherersTransitionOnlyWhenTheOpenRetainerChanges()
    {
        var firstCombat = Retainer(id: 10, jobId: 19);
        var secondSameClassCombat = Retainer(id: 20, jobId: 19);
        var gatherer = Retainer(id: 30, jobId: 16);
        Assert.False(firstCombat.IsGathering);
        Assert.Equal(firstCombat.JobId, secondSameClassCombat.JobId);
        Assert.True(gatherer.IsGathering);

        var moves = new[]
        {
            Move(firstCombat.RetainerId, RetainerEquipmentSlot.Head),
            Move(firstCombat.RetainerId, RetainerEquipmentSlot.Body),
            Move(secondSameClassCombat.RetainerId, RetainerEquipmentSlot.Head),
            Move(secondSameClassCombat.RetainerId, RetainerEquipmentSlot.Body),
            Move(gatherer.RetainerId, RetainerEquipmentSlot.MainHand),
        };

        Assert.Equal(
            RetainerMoveSequenceAction.ApplyMove,
            RetainerMoveSequencePolicy.Decide(moves, 0, openRetainerId: 10));
        Assert.Equal(
            RetainerMoveSequenceAction.ApplyMove,
            RetainerMoveSequencePolicy.Decide(moves, 1, openRetainerId: 10));
        Assert.Equal(
            RetainerMoveSequenceAction.ReturnToList,
            RetainerMoveSequencePolicy.Decide(moves, 2, openRetainerId: 10));
        Assert.Equal(
            RetainerMoveSequenceAction.SelectRetainer,
            RetainerMoveSequencePolicy.Decide(moves, 2, openRetainerId: null));
        Assert.Equal(
            RetainerMoveSequenceAction.ApplyMove,
            RetainerMoveSequencePolicy.Decide(moves, 2, openRetainerId: 20));
        Assert.Equal(
            RetainerMoveSequenceAction.ReturnToList,
            RetainerMoveSequencePolicy.Decide(moves, 4, openRetainerId: 20));
        Assert.Equal(
            RetainerMoveSequenceAction.ApplyMove,
            RetainerMoveSequencePolicy.Decide(moves, 4, openRetainerId: 30));
    }

    [Fact]
    public void MultipleMovesOnOneRetainerKeepTheWindowAndThenCloseTheFinalWindow()
    {
        var moves = new[]
        {
            Move(retainerId: 10, RetainerEquipmentSlot.Head),
            Move(retainerId: 10, RetainerEquipmentSlot.Body),
        };

        Assert.Equal(
            RetainerMoveSequenceAction.ApplyMove,
            RetainerMoveSequencePolicy.Decide(moves, 0, openRetainerId: 10));
        Assert.Equal(
            RetainerMoveSequenceAction.ApplyMove,
            RetainerMoveSequencePolicy.Decide(moves, 1, openRetainerId: 10));
        Assert.Equal(
            RetainerMoveSequenceAction.CloseFinalWindow,
            RetainerMoveSequencePolicy.Decide(moves, 2, openRetainerId: 10));
        Assert.Equal(
            RetainerMoveSequenceAction.Finished,
            RetainerMoveSequencePolicy.Decide(moves, 2, openRetainerId: null));
    }

    [Fact]
    public void NonzeroNativeReturnCanStillVerifyAsynchronousExactDestinationSuccess()
    {
        var start = new DateTime(2026, 7, 26, 20, 0, 0, DateTimeKind.Utc);
        var policy = new RetainerMoveAttemptPolicy(start);

        Assert.Equal(
            RetainerMoveAttemptAction.Wait,
            policy.Evaluate(start.AddMilliseconds(499), RetainerMoveObservation.ExactSource()).Action);
        Assert.Equal(
            RetainerMoveAttemptAction.Dispatch,
            policy.Evaluate(start.AddMilliseconds(500), RetainerMoveObservation.ExactSource()).Action);
        policy.MarkDispatched(
            start.AddMilliseconds(500),
            new RetainerMoveRequestResult(true, 37, string.Empty));

        var customizedDestinationMismatch = new RetainerMoveObservation(
            DestinationReadable: true,
            DestinationMatches: false,
            SourceReadable: true,
            SourceMatches: true,
            Error: string.Empty);
        Assert.Equal(
            RetainerMoveAttemptAction.Wait,
            policy.Evaluate(start.AddMilliseconds(550), customizedDestinationMismatch).Action);
        Assert.Equal(
            RetainerMoveAttemptAction.Succeeded,
            policy.Evaluate(
                start.AddMilliseconds(600),
                RetainerMoveObservation.ExactDestination()).Action);
        Assert.Equal(
            RetainerMoveAttemptAction.None,
            policy.Evaluate(
                start.AddMilliseconds(601),
                RetainerMoveObservation.ExactDestination()).Action);
    }

    [Fact]
    public void MoveRetriesAreDelayedAndBoundedToThreeTotalAttempts()
    {
        var start = new DateTime(2026, 7, 26, 20, 0, 0, DateTimeKind.Utc);
        var policy = new RetainerMoveAttemptPolicy(start);
        var dispatches = 0;

        void DispatchAt(DateTime at, int nativeReturn)
        {
            Assert.Equal(
                RetainerMoveAttemptAction.Dispatch,
                policy.Evaluate(at, RetainerMoveObservation.ExactSource()).Action);
            dispatches++;
            policy.MarkDispatched(at, new RetainerMoveRequestResult(true, nativeReturn, string.Empty));
        }

        DispatchAt(start.AddMilliseconds(500), 1);
        Assert.Equal(
            RetainerMoveAttemptAction.Wait,
            policy.Evaluate(start.AddMilliseconds(2500), RetainerMoveObservation.ExactSource()).Action);
        Assert.Equal(
            RetainerMoveAttemptAction.Wait,
            policy.Evaluate(start.AddMilliseconds(2999), RetainerMoveObservation.ExactSource()).Action);
        DispatchAt(start.AddMilliseconds(3000), 2);
        Assert.Equal(
            RetainerMoveAttemptAction.Wait,
            policy.Evaluate(start.AddMilliseconds(5000), RetainerMoveObservation.ExactSource()).Action);
        DispatchAt(start.AddMilliseconds(5500), 3);
        var terminal = policy.Evaluate(
            start.AddMilliseconds(7500),
            RetainerMoveObservation.ExactSource());

        Assert.Equal(3, dispatches);
        Assert.Equal(3, policy.Attempt);
        Assert.Equal(RetainerMoveAttemptAction.TerminalFailure, terminal.Action);
        Assert.Equal(
            RetainerMoveAttemptAction.None,
            policy.Evaluate(
                start.AddMilliseconds(7501),
                RetainerMoveObservation.ExactSource()).Action);
    }

    [Fact]
    public void SourceDisappearanceProducesOneTerminalSignalWithoutRetry()
    {
        var start = new DateTime(2026, 7, 26, 20, 0, 0, DateTimeKind.Utc);
        var policy = new RetainerMoveAttemptPolicy(start);
        Assert.Equal(
            RetainerMoveAttemptAction.Dispatch,
            policy.Evaluate(start.AddMilliseconds(500), RetainerMoveObservation.ExactSource()).Action);
        policy.MarkDispatched(
            start.AddMilliseconds(500),
            new RetainerMoveRequestResult(true, 0, string.Empty));

        Assert.Equal(
            RetainerMoveAttemptAction.TerminalFailure,
            policy.Evaluate(
                start.AddMilliseconds(600),
                RetainerMoveObservation.SourceLost()).Action);
        Assert.Equal(
            RetainerMoveAttemptAction.None,
            policy.Evaluate(
                start.AddMilliseconds(601),
                RetainerMoveObservation.SourceLost()).Action);
        Assert.Equal(1, policy.Attempt);
    }

    [Fact]
    public void NoCandidatesStillProduceAnEmptySuccessfulAllocation()
    {
        var profile = Profile(
            RetainerMetricKind.CombatItemLevel,
            currentMetric: 10,
            new Dictionary<RetainerEquipmentSlot, int>
            {
                [RetainerEquipmentSlot.Head] = 10,
            });

        var result = RetainerEquipmentPolicy.Allocate(
            [profile],
            [],
            RetainerGearSourceMode.AllGear,
            nonUniqueOnly: false);

        Assert.Empty(result.Moves);
        Assert.Equal(10, result.ProjectedMetrics[RetainerId]);
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
            Fingerprint = RetainerGearFingerprint.Plain(itemId),
        };

    private static RetainerEquipmentMove Move(
        ulong retainerId,
        RetainerEquipmentSlot slot) =>
        new(
            retainerId,
            slot,
            Candidate((uint)(100 + (int)slot), slot, RetainerGearSource.Inventory, 100, 100),
            Improvement: 1);

    private static RetainerGearFingerprint Fingerprint(
        uint encodedItemId,
        uint glamourId = 0,
        byte stain0Id = 0,
        byte stain1Id = 0,
        ushort materia0Id = 0,
        ushort materia1Id = 0,
        ushort materia2Id = 0,
        ushort materia3Id = 0,
        ushort materia4Id = 0,
        byte materia0Grade = 0,
        byte materia1Grade = 0,
        byte materia2Grade = 0,
        byte materia3Grade = 0,
        byte materia4Grade = 0) =>
        new(
            encodedItemId,
            glamourId,
            stain0Id,
            stain1Id,
            materia0Id,
            materia1Id,
            materia2Id,
            materia3Id,
            materia4Id,
            materia0Grade,
            materia1Grade,
            materia2Grade,
            materia3Grade,
            materia4Grade);
}
