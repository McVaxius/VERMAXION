using System;
using System.Collections.Generic;
using System.Linq;
using VERMAXION.Models;
using Xunit;

namespace VERMAXION.Tests;

public sealed class EquipmentAutomationTests
{
    [Fact]
    public void GearUpdaterTargetsOneDeterministicSavedGearsetPerUnlockedClassJob()
    {
        var gearsets = new[]
        {
            Gearset(20, 2, level: 50),
            Gearset(5, 2, level: 50),
            Gearset(99, 1, level: 10),
            Gearset(4, 3, level: 0),
        };

        var targets = EquipmentAutomationPolicy.BuildGearUpdaterTargets(gearsets);

        Assert.Equal([99, 5], targets.Select(target => target.GearsetId));
    }

    [Fact]
    public void HighestCombatTiePrefersJobThenCurrentThenStableIds()
    {
        var gearsets = new[]
        {
            Gearset(9, 1, level: 100, combat: true, isJob: false),
            Gearset(8, 20, level: 100, combat: true, isJob: true),
            Gearset(7, 21, level: 100, combat: true, isJob: true),
            Gearset(1, 99, level: 100, combat: false, isJob: true),
        };

        Assert.Equal(21u, EquipmentAutomationPolicy.SelectHighestCombatJob(gearsets, 21)!.ClassJobId);
        Assert.Equal(20u, EquipmentAutomationPolicy.SelectHighestCombatJob(gearsets, 30)!.ClassJobId);
    }

    [Fact]
    public void CurrentJobEquipmentEquipsRecommendedOnceAndSavesCapturedGearset()
    {
        var runtime = new FakeRuntime
        {
            CurrentGearsetId = 12,
            CurrentJobId = 21,
            Gearsets = [Gearset(12, 21, items: [1, 2, 3])],
            EquippedItems = [1, 2, 3],
        };
        var machine = new CurrentJobEquipmentStateMachine(runtime);

        Assert.True(machine.Start(out _));
        machine.Tick();
        machine.Tick();
        machine.Tick();
        machine.Tick();

        Assert.True(machine.IsComplete);
        Assert.Equal(1, runtime.RecommendedBeginCount);
        Assert.Equal(1, runtime.RecommendedPollCount);
        Assert.Equal([12], runtime.UpdatedGearsets);
        Assert.Equal(0, runtime.ConfirmationPollCount);
    }

    [Fact]
    public void CurrentJobEquipmentAbortsIfGearsetChanges()
    {
        var runtime = new FakeRuntime
        {
            CurrentGearsetId = 12,
            CurrentJobId = 21,
            Gearsets = [Gearset(12, 21)],
        };
        var machine = new CurrentJobEquipmentStateMachine(runtime);
        Assert.True(machine.Start(out _));

        runtime.CurrentGearsetId = 13;
        machine.Tick();

        Assert.True(machine.IsFailed);
        Assert.Contains("changed", machine.Status, StringComparison.OrdinalIgnoreCase);
        Assert.True(runtime.RecommendedCancelCount > 0);
    }

    [Fact]
    public void GearUpdaterRestoresStartingGearsetAfterPartialFailure()
    {
        var runtime = new FakeRuntime
        {
            CurrentGearsetId = 7,
            CurrentJobId = 2,
            Gearsets =
            [
                Gearset(3, 1, items: [30, 31]),
                Gearset(7, 2, items: [70, 71]),
            ],
            EquippedItems = [70, 71],
            UpdateSucceeds = false,
        };
        var machine = new GearUpdaterStateMachine(runtime);
        Assert.True(machine.Start(out _));

        for (var i = 0; i < 12 && machine.IsActive; i++)
            machine.Tick();

        Assert.Equal(EquipmentTaskTerminalState.Failed, machine.TerminalState);
        Assert.Equal(7, runtime.CurrentGearsetId);
        Assert.Equal(2u, runtime.CurrentJobId);
        Assert.Contains(7, runtime.EquipRequests);
        Assert.Equal(runtime.EquipRequests.Count, runtime.ConfirmationClickCount);
        Assert.True(runtime.RecommendedCancelCount > 0);
    }

    [Fact]
    public void GearUpdaterRestoresStartingGearsetAfterSuccessfulMultiJobRun()
    {
        var runtime = new FakeRuntime
        {
            CurrentGearsetId = 40,
            CurrentJobId = 20,
            Gearsets =
            [
                Gearset(3, 1, items: [30, 31]),
                Gearset(7, 2, items: [70, 71]),
                Gearset(40, 20, items: [400, 401]),
            ],
            EquippedItems = [400, 401],
        };
        var machine = new GearUpdaterStateMachine(runtime);
        Assert.True(machine.Start(out _));

        for (var i = 0; i < 40 && machine.IsActive; i++)
            machine.Tick();

        Assert.Equal(EquipmentTaskTerminalState.Complete, machine.TerminalState);
        Assert.Equal(40, runtime.CurrentGearsetId);
        Assert.Equal(20u, runtime.CurrentJobId);
        Assert.Equal(3, machine.CompletedTargetCount);
        Assert.Equal(3, runtime.RecommendedBeginCount);
        Assert.Equal(runtime.EquipRequests.Count, runtime.ConfirmationClickCount);
    }

    [Fact]
    public void HighestCombatJobFailsAfterBoundedNativeEquipAttempts()
    {
        var runtime = new FakeRuntime
        {
            CurrentGearsetId = 1,
            CurrentJobId = 1,
            Gearsets = [Gearset(8, 21, level: 100, combat: true, isJob: true)],
            EquipSucceeds = true,
            ApplyEquipState = false,
        };
        var machine = new HighestCombatJobStateMachine(runtime);
        Assert.True(machine.Start(out _));

        for (var i = 0; i < 12 && machine.IsActive; i++)
        {
            machine.Tick();
            runtime.Advance(TimeSpan.FromSeconds(2));
        }

        Assert.True(machine.IsFailed);
        Assert.Equal(3, runtime.EquipRequests.Count);
        Assert.Equal(3, runtime.ConfirmationClickCount);
        Assert.Contains("bounded", machine.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConfirmationWindowPollsReadyPromptThroughExactThreeSecondBoundary()
    {
        var runtime = new FakeRuntime { ConfirmationReady = false };
        var window = new GearsetConfirmationWindow();
        window.Open(runtime.UtcNow);

        Assert.False(window.Poll(runtime));
        runtime.Advance(TimeSpan.FromMilliseconds(2999));
        Assert.False(window.Poll(runtime));

        runtime.Advance(TimeSpan.FromMilliseconds(1));
        runtime.ConfirmationReady = true;

        Assert.True(window.Poll(runtime));
        Assert.Equal(1, runtime.ConfirmationClickCount);
        Assert.False(window.IsOpen);
    }

    [Fact]
    public void ConfirmationWindowDoesNotClickPromptAfterBoundaryOrWhenNotReady()
    {
        var runtime = new FakeRuntime { ConfirmationReady = false };
        var window = new GearsetConfirmationWindow();
        window.Open(runtime.UtcNow);

        runtime.Advance(GearsetConfirmationWindow.Duration);

        Assert.True(window.Poll(runtime));
        Assert.Equal(0, runtime.ConfirmationClickCount);
        Assert.Equal(1, runtime.ConfirmationPollCount);

        window.Open(runtime.UtcNow);
        runtime.Advance(GearsetConfirmationWindow.Duration + TimeSpan.FromMilliseconds(1));
        runtime.ConfirmationReady = true;

        Assert.True(window.Poll(runtime));
        Assert.Equal(0, runtime.ConfirmationClickCount);
        Assert.Equal(1, runtime.ConfirmationPollCount);
    }

    [Fact]
    public void NativeErrorStillOwnsPromptWindowWithoutDuplicateEquipAndThenVerifiesActiveGearset()
    {
        var runtime = new FakeRuntime
        {
            CurrentGearsetId = 1,
            CurrentJobId = 1,
            Gearsets = [Gearset(8, 21, level: 100, combat: true, isJob: true)],
            EquipSucceeds = false,
            ApplyEquipState = false,
            ApplyEquipStateOnConfirmation = true,
            AutoReadyConfirmation = false,
            ConfirmationReady = false,
        };
        var machine = new HighestCombatJobStateMachine(runtime);
        Assert.True(machine.Start(out _));

        machine.Tick();
        Assert.Single(runtime.EquipRequests);

        runtime.Advance(TimeSpan.FromSeconds(1));
        machine.Tick();
        runtime.Advance(TimeSpan.FromSeconds(1));
        machine.Tick();

        Assert.Single(runtime.EquipRequests);
        Assert.True(machine.IsActive);

        runtime.ConfirmationReady = true;
        machine.Tick();
        Assert.Equal(1, runtime.ConfirmationClickCount);
        Assert.Single(runtime.EquipRequests);

        machine.Tick();

        Assert.True(machine.IsComplete);
        Assert.False(machine.IsFailed);
        Assert.Equal(8, runtime.CurrentGearsetId);
        Assert.Equal(21u, runtime.CurrentJobId);
    }

    [Fact]
    public void FinalNativeAttemptKeepsPollingBeforeFinalActivationVerification()
    {
        var runtime = new FakeRuntime
        {
            CurrentGearsetId = 1,
            CurrentJobId = 1,
            Gearsets = [Gearset(8, 21, level: 100, combat: true, isJob: true)],
            EquipSucceeds = false,
            ApplyEquipState = false,
            AutoReadyConfirmation = false,
            ConfirmationReady = false,
        };
        var machine = new HighestCombatJobStateMachine(runtime);
        Assert.True(machine.Start(out _));

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            machine.Tick();
            Assert.Equal(attempt, runtime.EquipRequests.Count);

            runtime.Advance(TimeSpan.FromSeconds(2));
            machine.Tick();
            Assert.Equal(attempt, runtime.EquipRequests.Count);
            Assert.True(machine.IsActive);

            runtime.Advance(TimeSpan.FromSeconds(1));
            if (attempt == 3)
                runtime.ConfirmationReady = true;
            machine.Tick();
            Assert.Equal(attempt, runtime.EquipRequests.Count);
            Assert.True(machine.IsActive);

            if (attempt < 3)
            {
                runtime.Advance(TimeSpan.FromSeconds(2));
            }
        }

        Assert.Equal(1, runtime.ConfirmationClickCount);
        runtime.Advance(TimeSpan.FromSeconds(2));
        machine.Tick();

        Assert.True(machine.IsFailed);
        Assert.Contains("bounded", machine.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SeasonalGearDerivesOneItemPerSlotAndRestoresOriginalItemsOnSaveFailure()
    {
        var runtime = new FakeRuntime
        {
            CurrentGearsetId = 4,
            CurrentJobId = 20,
            Gearsets = [Gearset(4, 20, items: [10, 11, 12])],
            EquippedItems = [10, 11, 12],
            SeasonalItems =
            [
                new SeasonalInventoryItem(100, "Head B", EquipmentSlot.Head),
                new SeasonalInventoryItem(90, "Head A", EquipmentSlot.Head),
                new SeasonalInventoryItem(200, "Body", EquipmentSlot.Body),
            ],
            UpdateSucceeds = false,
        };
        var machine = new SeasonalGearStateMachine(runtime, [200, 100, 90, 90], _ => 0);
        Assert.True(machine.Start(out _));
        Assert.Equal([90u, 200u], machine.SelectedItems.Select(item => item.ItemId));

        for (var i = 0; i < 12 && machine.IsActive; i++)
            machine.Tick();

        Assert.True(machine.IsFailed);
        Assert.Equal([10u, 11u, 12u], runtime.EquippedItems);
        Assert.Equal(4, runtime.CurrentGearsetId);
        Assert.Equal(0, runtime.RecommendedBeginCount);
        Assert.Equal(runtime.EquipRequests.Count, runtime.ConfirmationClickCount);
    }

    [Fact]
    public void SeasonalRestorationStopsAfterBoundedVerificationAttempts()
    {
        var runtime = new FakeRuntime
        {
            CurrentGearsetId = 4,
            CurrentJobId = 20,
            Gearsets = [Gearset(4, 20, items: [10, 11, 12])],
            EquippedItems = [10, 11, 12],
            SeasonalItems = [new SeasonalInventoryItem(90, "Head", EquipmentSlot.Head)],
            UpdateSucceeds = false,
            ApplyEquipState = false,
        };
        var machine = new SeasonalGearStateMachine(runtime, [90], _ => 0);
        Assert.True(machine.Start(out _));

        for (var i = 0; i < 20 && machine.IsActive; i++)
        {
            machine.Tick();
            if (machine.CurrentState == SeasonalGearStateMachine.State.ConfirmingRestore)
                machine.Tick();
            runtime.Advance(TimeSpan.FromSeconds(9));
        }

        Assert.True(machine.IsFailed);
        Assert.Contains("could not be verified", machine.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, runtime.EquipRequests.Count);
        Assert.Equal(3, runtime.ConfirmationClickCount);
    }

    [Fact]
    public void RecommendedEquipmentTimeoutCleansUpWithoutSaving()
    {
        var runtime = new FakeRuntime
        {
            CurrentGearsetId = 2,
            CurrentJobId = 20,
            Gearsets = [Gearset(2, 20)],
            RecommendedProgress = RecommendedEquipmentProgress.Pending,
        };
        var machine = new CurrentJobEquipmentStateMachine(runtime);
        Assert.True(machine.Start(out _));
        machine.Tick();
        runtime.Advance(TimeSpan.FromSeconds(16));
        machine.Tick();

        Assert.True(machine.IsFailed);
        Assert.Empty(runtime.UpdatedGearsets);
        Assert.True(runtime.RecommendedCancelCount > 0);
    }

    [Fact]
    public void CuratedIdsAreDeduplicatedWithoutLosingStableOrdering()
    {
        Assert.Equal([2u, 5u, 9u], EquipmentAutomationPolicy.DeduplicateCuratedItemIds([9, 2, 9, 0, 5]));
    }

    private static GearsetSnapshot Gearset(
        int id,
        uint job,
        int level = 50,
        bool combat = true,
        bool isJob = true,
        IReadOnlyList<uint>? items = null)
        => new(id, job, combat, isJob, level, $"GS{id}", items ?? [id.ToUInt()]);

    private sealed class FakeRuntime : IEquipmentAutomationRuntime
    {
        public DateTime UtcNow { get; private set; } = new(2026, 7, 22, 0, 0, 0, DateTimeKind.Utc);
        public ulong CharacterContentId { get; set; } = 123;
        public uint CurrentJobId { get; set; }
        public int CurrentGearsetId { get; set; }
        public IReadOnlyList<GearsetSnapshot> Gearsets { get; set; } = [];
        public IReadOnlyList<uint> EquippedItems { get; set; } = [];
        public IReadOnlyList<SeasonalInventoryItem> SeasonalItems { get; set; } = [];
        public bool EquipSucceeds { get; set; } = true;
        public bool ApplyEquipState { get; set; } = true;
        public bool ApplyEquipStateOnConfirmation { get; set; }
        public bool AutoReadyConfirmation { get; set; } = true;
        public bool ConfirmationReady { get; set; } = true;
        public bool UpdateSucceeds { get; set; } = true;
        public RecommendedEquipmentProgress RecommendedProgress { get; set; } = RecommendedEquipmentProgress.Complete;
        public int RecommendedBeginCount { get; private set; }
        public int RecommendedPollCount { get; private set; }
        public int RecommendedCancelCount { get; private set; }
        public int ConfirmationPollCount { get; private set; }
        public int ConfirmationClickCount { get; private set; }
        public List<int> EquipRequests { get; } = [];
        public List<int> UpdatedGearsets { get; } = [];
        private readonly Dictionary<int, IReadOnlyList<uint>> saved = [];
        private GearsetSnapshot? pendingGearset;

        public IReadOnlyList<GearsetSnapshot> GetValidGearsets() => Gearsets;
        public IReadOnlyList<uint> GetEquippedItemIds() => EquippedItems.ToArray();

        public bool TryEquipGearset(int gearsetId, out string error)
        {
            EquipRequests.Add(gearsetId);
            var target = Gearsets.FirstOrDefault(gearset => gearset.GearsetId == gearsetId);
            pendingGearset = target;
            if (AutoReadyConfirmation)
                ConfirmationReady = true;
            if (!EquipSucceeds || target == null)
            {
                error = "equip rejected";
                return false;
            }

            if (ApplyEquipState)
            {
                CurrentGearsetId = target.GearsetId;
                CurrentJobId = target.ClassJobId;
                EquippedItems = target.ItemIds.ToArray();
            }
            error = string.Empty;
            return true;
        }

        public bool TryConfirmGearsetChangePrompt()
        {
            ConfirmationPollCount++;
            if (!ConfirmationReady)
                return false;

            ConfirmationClickCount++;
            ConfirmationReady = false;
            if (ApplyEquipStateOnConfirmation && pendingGearset != null)
                ApplyGearset(pendingGearset);
            return true;
        }

        public bool IsGearsetEquipped(int gearsetId, uint classJobId)
            => CurrentGearsetId == gearsetId && CurrentJobId == classJobId;

        public bool TryBeginRecommendedEquipment(uint classJobId, out string error)
        {
            RecommendedBeginCount++;
            error = string.Empty;
            return true;
        }

        public RecommendedEquipmentProgress PollRecommendedEquipment(out string error)
        {
            RecommendedPollCount++;
            error = RecommendedProgress == RecommendedEquipmentProgress.Failed ? "recommend failed" : string.Empty;
            if (RecommendedProgress == RecommendedEquipmentProgress.Complete)
                EquippedItems = EquippedItems.Select(item => item + 1000).ToArray();
            return RecommendedProgress;
        }

        public void CancelRecommendedEquipment() => RecommendedCancelCount++;

        public bool TryUpdateGearset(int gearsetId, IReadOnlyList<uint> expectedItemIds, out string error)
        {
            if (!UpdateSucceeds)
            {
                error = "save rejected";
                return false;
            }

            UpdatedGearsets.Add(gearsetId);
            saved[gearsetId] = expectedItemIds.ToArray();
            error = string.Empty;
            return true;
        }

        public bool IsGearsetSaveVerified(int gearsetId, IReadOnlyList<uint> expectedItemIds, out string error)
        {
            var verified = saved.TryGetValue(gearsetId, out var actual) &&
                           EquipmentAutomationPolicy.ItemSignaturesMatch(expectedItemIds, actual);
            error = verified ? string.Empty : "not saved";
            return verified;
        }

        public IReadOnlyList<SeasonalInventoryItem> FindSeasonalInventoryItems(IReadOnlyCollection<uint> curatedItemIds)
            => SeasonalItems.Where(item => curatedItemIds.Contains(item.ItemId)).ToList();

        public bool TryMoveSeasonalItemToEquipped(SeasonalInventoryItem item, out string error)
        {
            var copy = EquippedItems.ToArray();
            var slot = (int)item.Slot;
            if (copy.Length <= slot)
                Array.Resize(ref copy, slot + 1);
            copy[slot] = item.ItemId;
            EquippedItems = copy;
            error = string.Empty;
            return true;
        }

        public bool IsSeasonalItemEquipped(SeasonalInventoryItem item)
            => EquippedItems.Count > (int)item.Slot && EquippedItems[(int)item.Slot] == item.ItemId;

        public void Advance(TimeSpan duration) => UtcNow += duration;

        private void ApplyGearset(GearsetSnapshot target)
        {
            CurrentGearsetId = target.GearsetId;
            CurrentJobId = target.ClassJobId;
            EquippedItems = target.ItemIds.ToArray();
        }
    }
}

internal static class EquipmentTestExtensions
{
    public static uint ToUInt(this int value) => (uint)value;
}
