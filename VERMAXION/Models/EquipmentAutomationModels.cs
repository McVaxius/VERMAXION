using System;
using System.Collections.Generic;
using System.Linq;

namespace VERMAXION.Models;

public enum EquipmentSlot
{
    Head = 2,
    Body = 3,
    Hands = 4,
    Legs = 6,
    Feet = 7,
}

public sealed record GearsetSnapshot(
    int GearsetId,
    uint ClassJobId,
    bool IsCombat,
    bool IsJob,
    int Level,
    string Name,
    IReadOnlyList<uint> ItemIds);

public sealed record SeasonalInventoryItem(
    uint ItemId,
    string Name,
    EquipmentSlot Slot);

public enum RecommendedEquipmentProgress
{
    Pending,
    Complete,
    Failed,
}

public interface IEquipmentAutomationRuntime
{
    DateTime UtcNow { get; }
    ulong CharacterContentId { get; }
    uint CurrentJobId { get; }
    int CurrentGearsetId { get; }
    IReadOnlyList<GearsetSnapshot> GetValidGearsets();
    IReadOnlyList<uint> GetEquippedItemIds();
    bool TryEquipGearset(int gearsetId, out string error);
    bool IsGearsetEquipped(int gearsetId, uint classJobId);
    bool TryBeginRecommendedEquipment(uint classJobId, out string error);
    RecommendedEquipmentProgress PollRecommendedEquipment(out string error);
    void CancelRecommendedEquipment();
    bool TryUpdateGearset(int gearsetId, IReadOnlyList<uint> expectedItemIds, out string error);
    bool IsGearsetSaveVerified(int gearsetId, IReadOnlyList<uint> expectedItemIds, out string error);
    IReadOnlyList<SeasonalInventoryItem> FindSeasonalInventoryItems(IReadOnlyCollection<uint> curatedItemIds);
    bool TryMoveSeasonalItemToEquipped(SeasonalInventoryItem item, out string error);
    bool IsSeasonalItemEquipped(SeasonalInventoryItem item);
}

public static class EquipmentAutomationPolicy
{
    public static IReadOnlyList<GearsetSnapshot> BuildGearUpdaterTargets(IEnumerable<GearsetSnapshot> gearsets)
        => gearsets
            .Where(gearset => gearset.Level > 0 && gearset.ClassJobId > 0)
            .GroupBy(gearset => gearset.ClassJobId)
            .Select(group => group.OrderBy(gearset => gearset.GearsetId).First())
            .OrderBy(gearset => gearset.ClassJobId)
            .ThenBy(gearset => gearset.GearsetId)
            .ToList();

    public static GearsetSnapshot? SelectHighestCombatJob(
        IEnumerable<GearsetSnapshot> gearsets,
        uint currentJobId)
        => gearsets
            .Where(gearset => gearset.IsCombat && gearset.Level > 0 && gearset.ClassJobId > 0)
            .GroupBy(gearset => gearset.ClassJobId)
            .Select(group => group.OrderBy(gearset => gearset.GearsetId).First())
            .OrderByDescending(gearset => gearset.Level)
            .ThenByDescending(gearset => gearset.IsJob)
            .ThenByDescending(gearset => gearset.ClassJobId == currentJobId)
            .ThenBy(gearset => gearset.ClassJobId)
            .ThenBy(gearset => gearset.GearsetId)
            .FirstOrDefault();

    public static GearsetSnapshot? SelectCurrentGearset(
        IEnumerable<GearsetSnapshot> gearsets,
        int currentGearsetId,
        uint currentJobId)
        => gearsets.FirstOrDefault(gearset =>
            gearset.GearsetId == currentGearsetId &&
            gearset.ClassJobId == currentJobId);

    public static IReadOnlyList<uint> DeduplicateCuratedItemIds(IEnumerable<uint> itemIds)
        => itemIds.Where(itemId => itemId != 0).Distinct().OrderBy(itemId => itemId).ToList();

    public static IReadOnlyList<SeasonalInventoryItem> SelectSeasonalItems(
        IEnumerable<SeasonalInventoryItem> candidates,
        Func<int, int> selectIndex)
        => candidates
            .GroupBy(candidate => candidate.Slot)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var ordered = group.OrderBy(candidate => candidate.ItemId).ToList();
                var index = Math.Clamp(selectIndex(ordered.Count), 0, ordered.Count - 1);
                return ordered[index];
            })
            .ToList();

    public static bool ItemSignaturesMatch(IReadOnlyList<uint> expected, IReadOnlyList<uint> actual)
        => expected.Count == actual.Count && expected.SequenceEqual(actual);
}

public enum EquipmentTaskTerminalState
{
    None,
    Complete,
    Failed,
    Cancelled,
}

public sealed class GearUpdaterStateMachine
{
    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan OverallTimeout = TimeSpan.FromMinutes(5);
    private const int MaxEquipAttempts = 3;

    private readonly IEquipmentAutomationRuntime runtime;
    private IReadOnlyList<GearsetSnapshot> targets = [];
    private IReadOnlyList<uint> expectedItems = [];
    private DateTime startedAt;
    private DateTime stateEnteredAt;
    private ulong startingContentId;
    private int startingGearsetId = -1;
    private uint startingJobId;
    private int targetIndex;
    private int equipAttempts;
    private bool terminalAfterRestore;
    private EquipmentTaskTerminalState restoreTerminalState;
    private string restoreFailure = string.Empty;

    public enum State
    {
        Idle,
        EquippingGearset,
        WaitingForGearset,
        StartingRecommended,
        WaitingForRecommended,
        SavingGearset,
        WaitingForSave,
        RestoringStartingGearset,
        Complete,
        Failed,
        Cancelled,
    }

    public GearUpdaterStateMachine(IEquipmentAutomationRuntime runtime)
    {
        this.runtime = runtime;
    }

    public State CurrentState { get; private set; } = State.Idle;
    public string Status { get; private set; } = "Idle";
    public int CompletedTargetCount { get; private set; }
    public int TargetCount => targets.Count;
    public bool IsActive => CurrentState is not (State.Idle or State.Complete or State.Failed or State.Cancelled);
    public EquipmentTaskTerminalState TerminalState => CurrentState switch
    {
        State.Complete => EquipmentTaskTerminalState.Complete,
        State.Failed => EquipmentTaskTerminalState.Failed,
        State.Cancelled => EquipmentTaskTerminalState.Cancelled,
        _ => EquipmentTaskTerminalState.None,
    };

    public bool Start(out string reason)
    {
        Reset();
        var gearsets = runtime.GetValidGearsets();
        var startingGearset = EquipmentAutomationPolicy.SelectCurrentGearset(
            gearsets,
            runtime.CurrentGearsetId,
            runtime.CurrentJobId);
        if (startingGearset == null)
        {
            reason = "The active gearset/job is not a valid saved gearset.";
            FailWithoutRestore(reason);
            return false;
        }

        targets = EquipmentAutomationPolicy.BuildGearUpdaterTargets(gearsets);
        if (targets.Count == 0)
        {
            reason = "No valid saved gearsets represent an unlocked class or job.";
            FailWithoutRestore(reason);
            return false;
        }

        startingContentId = runtime.CharacterContentId;
        startingGearsetId = startingGearset.GearsetId;
        startingJobId = startingGearset.ClassJobId;
        startedAt = runtime.UtcNow;
        targetIndex = 0;
        SetState(State.EquippingGearset, $"Preparing {targets[0].Name}");
        reason = $"Prepared {targets.Count} deterministic class/job gearset targets.";
        return true;
    }

    public void Tick()
    {
        if (!IsActive)
            return;

        if (runtime.CharacterContentId != startingContentId)
        {
            runtime.CancelRecommendedEquipment();
            FailWithoutRestore("Character changed during Gear Updater; native work was cancelled.");
            return;
        }

        if (runtime.UtcNow - startedAt >= OverallTimeout)
        {
            FailWithRestore("Gear Updater exceeded its five-minute overall timeout.");
            return;
        }

        var target = targetIndex < targets.Count ? targets[targetIndex] : null;
        switch (CurrentState)
        {
            case State.EquippingGearset:
                equipAttempts++;
                if (!runtime.TryEquipGearset(target!.GearsetId, out var equipError))
                {
                    if (equipAttempts >= MaxEquipAttempts)
                        FailWithRestore($"Could not equip gearset {target.GearsetId}: {equipError}");
                    else
                        SetState(State.WaitingForGearset, $"Retrying gearset {target.GearsetId}: {equipError}");
                    return;
                }

                SetState(State.WaitingForGearset, $"Verifying {target.Name}");
                break;

            case State.WaitingForGearset:
                if (runtime.IsGearsetEquipped(target!.GearsetId, target.ClassJobId))
                {
                    equipAttempts = 0;
                    SetState(State.StartingRecommended, $"Preparing recommended gear for {target.Name}");
                }
                else if (StepTimedOut())
                {
                    if (equipAttempts >= MaxEquipAttempts)
                        FailWithRestore($"Gearset {target.GearsetId} did not become active after {equipAttempts} attempts.");
                    else
                        SetState(State.EquippingGearset, $"Retrying {target.Name}");
                }
                break;

            case State.StartingRecommended:
                if (!runtime.TryBeginRecommendedEquipment(target!.ClassJobId, out var recommendedError))
                {
                    FailWithRestore($"Recommended equipment setup failed for {target.Name}: {recommendedError}");
                    return;
                }

                SetState(State.WaitingForRecommended, $"Equipping recommended gear for {target.Name}");
                break;

            case State.WaitingForRecommended:
                var progress = runtime.PollRecommendedEquipment(out var progressError);
                if (progress == RecommendedEquipmentProgress.Complete)
                    SetState(State.SavingGearset, $"Saving {target!.Name}");
                else if (progress == RecommendedEquipmentProgress.Failed)
                    FailWithRestore($"Recommended equipment failed for {target!.Name}: {progressError}");
                else if (StepTimedOut())
                    FailWithRestore($"Recommended equipment timed out for {target!.Name}.");
                break;

            case State.SavingGearset:
                if (!runtime.IsGearsetEquipped(target!.GearsetId, target.ClassJobId))
                {
                    FailWithRestore($"The active job or gearset changed before saving {target.Name}.");
                    return;
                }

                expectedItems = runtime.GetEquippedItemIds().ToArray();
                if (!runtime.TryUpdateGearset(target.GearsetId, expectedItems, out var saveError))
                {
                    FailWithRestore($"Native gearset update failed for {target.Name}: {saveError}");
                    return;
                }

                SetState(State.WaitingForSave, $"Verifying native save for {target.Name}");
                break;

            case State.WaitingForSave:
                if (runtime.IsGearsetSaveVerified(target!.GearsetId, expectedItems, out _))
                {
                    CompletedTargetCount++;
                    targetIndex++;
                    if (targetIndex >= targets.Count)
                        BeginRestore(EquipmentTaskTerminalState.Complete, "All class/job gearsets updated; restoring starting gearset.");
                    else
                        SetState(State.EquippingGearset, $"Preparing {targets[targetIndex].Name}");
                }
                else if (StepTimedOut())
                {
                    runtime.IsGearsetSaveVerified(target.GearsetId, expectedItems, out var verifyError);
                    FailWithRestore($"Native save verification timed out for {target.Name}: {verifyError}");
                }
                break;

            case State.RestoringStartingGearset:
                if (runtime.IsGearsetEquipped(startingGearsetId, startingJobId))
                {
                    FinishRestore();
                    return;
                }

                if (equipAttempts == 0 || StepTimedOut())
                {
                    equipAttempts++;
                    if (!runtime.TryEquipGearset(startingGearsetId, out var restoreError))
                        restoreFailure = restoreError;

                    if (equipAttempts >= MaxEquipAttempts)
                    {
                        restoreTerminalState = EquipmentTaskTerminalState.Failed;
                        Status = $"Could not restore starting gearset {startingGearsetId}: {restoreFailure}";
                        FinishRestore();
                    }
                    else
                    {
                        stateEnteredAt = runtime.UtcNow;
                    }
                }
                break;
        }
    }

    public void Cancel(string reason)
    {
        if (!IsActive)
            return;

        runtime.CancelRecommendedEquipment();
        if (runtime.CharacterContentId == startingContentId && startingGearsetId >= 0)
            runtime.TryEquipGearset(startingGearsetId, out _);
        SetState(State.Cancelled, reason);
    }

    public void Reset()
    {
        runtime.CancelRecommendedEquipment();
        targets = [];
        expectedItems = [];
        startedAt = DateTime.MinValue;
        stateEnteredAt = DateTime.MinValue;
        startingContentId = 0;
        startingGearsetId = -1;
        startingJobId = 0;
        targetIndex = 0;
        equipAttempts = 0;
        terminalAfterRestore = false;
        restoreTerminalState = EquipmentTaskTerminalState.None;
        restoreFailure = string.Empty;
        CompletedTargetCount = 0;
        CurrentState = State.Idle;
        Status = "Idle";
    }

    private bool StepTimedOut() => runtime.UtcNow - stateEnteredAt >= StepTimeout;

    private void FailWithRestore(string reason)
    {
        Status = reason;
        BeginRestore(EquipmentTaskTerminalState.Failed, reason);
    }

    private void FailWithoutRestore(string reason)
        => SetState(State.Failed, reason);

    private void BeginRestore(EquipmentTaskTerminalState terminalState, string status)
    {
        runtime.CancelRecommendedEquipment();
        terminalAfterRestore = true;
        restoreTerminalState = terminalState;
        equipAttempts = 0;
        SetState(State.RestoringStartingGearset, status);
    }

    private void FinishRestore()
    {
        if (!terminalAfterRestore)
            return;

        SetState(restoreTerminalState switch
        {
            EquipmentTaskTerminalState.Complete => State.Complete,
            EquipmentTaskTerminalState.Cancelled => State.Cancelled,
            _ => State.Failed,
        }, Status);
    }

    private void SetState(State state, string status)
    {
        CurrentState = state;
        Status = status;
        stateEnteredAt = runtime.UtcNow;
    }
}

public sealed class HighestCombatJobStateMachine
{
    private static readonly TimeSpan AttemptInterval = TimeSpan.FromSeconds(2);
    private const int MaxAttempts = 3;
    private readonly IEquipmentAutomationRuntime runtime;
    private GearsetSnapshot? target;
    private DateTime lastAttemptAt;
    private int attempts;

    public HighestCombatJobStateMachine(IEquipmentAutomationRuntime runtime)
    {
        this.runtime = runtime;
    }

    public bool IsActive { get; private set; }
    public bool IsComplete { get; private set; }
    public bool IsFailed { get; private set; }
    public string Status { get; private set; } = "Idle";
    public GearsetSnapshot? Target => target;

    public bool Start(out string reason)
    {
        Reset();
        target = EquipmentAutomationPolicy.SelectHighestCombatJob(runtime.GetValidGearsets(), runtime.CurrentJobId);
        if (target == null)
        {
            reason = "No combat job with an unlocked level and valid saved gearset is available.";
            IsFailed = true;
            Status = reason;
            return false;
        }

        IsActive = true;
        Status = $"Equipping {target.Name} (level {target.Level})";
        reason = Status;
        return true;
    }

    public void Tick()
    {
        if (!IsActive || target == null)
            return;

        if (runtime.IsGearsetEquipped(target.GearsetId, target.ClassJobId))
        {
            IsActive = false;
            IsComplete = true;
            Status = $"Equipped {target.Name} via gearset {target.GearsetId}.";
            return;
        }

        if (attempts > 0 && runtime.UtcNow - lastAttemptAt < AttemptInterval)
            return;

        if (attempts >= MaxAttempts)
        {
            FailFinalVerification();
            return;
        }

        attempts++;
        lastAttemptAt = runtime.UtcNow;
        if (!runtime.TryEquipGearset(target.GearsetId, out var error) && attempts >= MaxAttempts)
        {
            IsActive = false;
            IsFailed = true;
            Status = $"Could not equip {target.Name} after {attempts} attempts: {error}";
        }
        else if (attempts >= MaxAttempts && !runtime.IsGearsetEquipped(target.GearsetId, target.ClassJobId))
        {
            Status = $"Verifying final equip attempt for {target.Name}.";
        }
    }

    private void FailFinalVerification()
    {
        IsActive = false;
        IsFailed = true;
        Status = $"Gearset {target!.GearsetId} did not become active after {attempts} bounded attempts.";
    }

    public void Cancel(string reason)
    {
        IsActive = false;
        IsFailed = true;
        Status = reason;
    }

    public void Reset()
    {
        target = null;
        lastAttemptAt = DateTime.MinValue;
        attempts = 0;
        IsActive = false;
        IsComplete = false;
        IsFailed = false;
        Status = "Idle";
    }
}

public sealed class CurrentJobEquipmentStateMachine
{
    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(15);
    private readonly IEquipmentAutomationRuntime runtime;
    private GearsetSnapshot? startingGearset;
    private IReadOnlyList<uint> expectedItems = [];
    private DateTime stateEnteredAt;
    private ulong startingContentId;

    public enum State
    {
        Idle,
        StartingRecommended,
        WaitingForRecommended,
        Saving,
        WaitingForSave,
        Complete,
        Failed,
        Cancelled,
    }

    public CurrentJobEquipmentStateMachine(IEquipmentAutomationRuntime runtime)
    {
        this.runtime = runtime;
    }

    public State CurrentState { get; private set; } = State.Idle;
    public string Status { get; private set; } = "Idle";
    public bool IsActive => CurrentState is not (State.Idle or State.Complete or State.Failed or State.Cancelled);
    public bool IsComplete => CurrentState == State.Complete;
    public bool IsFailed => CurrentState == State.Failed;

    public bool Start(out string reason)
    {
        Reset();
        startingGearset = EquipmentAutomationPolicy.SelectCurrentGearset(
            runtime.GetValidGearsets(),
            runtime.CurrentGearsetId,
            runtime.CurrentJobId);
        if (startingGearset == null)
        {
            reason = "The active gearset/job is not a valid saved gearset.";
            SetState(State.Failed, reason);
            return false;
        }

        startingContentId = runtime.CharacterContentId;
        SetState(State.StartingRecommended, $"Preparing recommended gear for {startingGearset.Name}.");
        reason = Status;
        return true;
    }

    public void Tick()
    {
        if (!IsActive || startingGearset == null)
            return;

        if (runtime.CharacterContentId != startingContentId ||
            runtime.CurrentGearsetId != startingGearset.GearsetId ||
            runtime.CurrentJobId != startingGearset.ClassJobId)
        {
            runtime.CancelRecommendedEquipment();
            SetState(State.Failed, "Player changed character, job, or gearset during Current Job Equipment.");
            return;
        }

        switch (CurrentState)
        {
            case State.StartingRecommended:
                if (!runtime.TryBeginRecommendedEquipment(startingGearset.ClassJobId, out var beginError))
                    SetState(State.Failed, $"Recommended equipment setup failed: {beginError}");
                else
                    SetState(State.WaitingForRecommended, "Equipping recommended gear once.");
                break;

            case State.WaitingForRecommended:
                var progress = runtime.PollRecommendedEquipment(out var progressError);
                if (progress == RecommendedEquipmentProgress.Complete)
                    SetState(State.Saving, "Saving the captured gearset natively.");
                else if (progress == RecommendedEquipmentProgress.Failed)
                    SetState(State.Failed, $"Recommended equipment failed: {progressError}");
                else if (StepTimedOut())
                {
                    runtime.CancelRecommendedEquipment();
                    SetState(State.Failed, "Recommended equipment timed out.");
                }
                break;

            case State.Saving:
                expectedItems = runtime.GetEquippedItemIds().ToArray();
                if (!runtime.TryUpdateGearset(startingGearset.GearsetId, expectedItems, out var saveError))
                    SetState(State.Failed, $"Native gearset update failed: {saveError}");
                else
                    SetState(State.WaitingForSave, "Verifying the exact native gearset save.");
                break;

            case State.WaitingForSave:
                if (runtime.IsGearsetSaveVerified(startingGearset.GearsetId, expectedItems, out _))
                    SetState(State.Complete, $"Updated gearset {startingGearset.GearsetId}.");
                else if (StepTimedOut())
                {
                    runtime.IsGearsetSaveVerified(startingGearset.GearsetId, expectedItems, out var verifyError);
                    SetState(State.Failed, $"Native save verification timed out: {verifyError}");
                }
                break;
        }
    }

    public void Cancel(string reason)
    {
        runtime.CancelRecommendedEquipment();
        SetState(State.Cancelled, reason);
    }

    public void Reset()
    {
        runtime.CancelRecommendedEquipment();
        startingGearset = null;
        expectedItems = [];
        startingContentId = 0;
        CurrentState = State.Idle;
        stateEnteredAt = DateTime.MinValue;
        Status = "Idle";
    }

    private bool StepTimedOut() => runtime.UtcNow - stateEnteredAt >= StepTimeout;

    private void SetState(State state, string status)
    {
        CurrentState = state;
        Status = status;
        stateEnteredAt = runtime.UtcNow;
    }
}

public sealed class SeasonalGearStateMachine
{
    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(8);
    private const int MaxMoveAttempts = 3;
    private readonly IEquipmentAutomationRuntime runtime;
    private readonly IReadOnlyList<uint> curatedIds;
    private readonly Func<int, int> selectIndex;
    private IReadOnlyList<SeasonalInventoryItem> selectedItems = [];
    private IReadOnlyList<uint> expectedItems = [];
    private IReadOnlyList<uint> startingItems = [];
    private GearsetSnapshot? startingGearset;
    private DateTime stateEnteredAt;
    private ulong startingContentId;
    private int selectedIndex;
    private int moveAttempts;
    private string failureReason = string.Empty;

    public enum State
    {
        Idle,
        MovingItem,
        WaitingForMove,
        Saving,
        WaitingForSave,
        Restoring,
        Complete,
        Failed,
        Cancelled,
    }

    public SeasonalGearStateMachine(
        IEquipmentAutomationRuntime runtime,
        IEnumerable<uint> curatedItemIds,
        Func<int, int>? selectIndex = null)
    {
        this.runtime = runtime;
        curatedIds = EquipmentAutomationPolicy.DeduplicateCuratedItemIds(curatedItemIds);
        this.selectIndex = selectIndex ?? (count => Random.Shared.Next(count));
    }

    public State CurrentState { get; private set; } = State.Idle;
    public string Status { get; private set; } = "Idle";
    public bool IsActive => CurrentState is not (State.Idle or State.Complete or State.Failed or State.Cancelled);
    public bool IsComplete => CurrentState == State.Complete;
    public bool IsFailed => CurrentState == State.Failed;
    public IReadOnlyList<SeasonalInventoryItem> SelectedItems => selectedItems;

    public bool Start(out string reason)
    {
        Reset();
        startingGearset = EquipmentAutomationPolicy.SelectCurrentGearset(
            runtime.GetValidGearsets(),
            runtime.CurrentGearsetId,
            runtime.CurrentJobId);
        if (startingGearset == null)
        {
            reason = "The active gearset/job is not a valid saved gearset.";
            SetState(State.Failed, reason);
            return false;
        }

        selectedItems = EquipmentAutomationPolicy.SelectSeasonalItems(
            runtime.FindSeasonalInventoryItems(curatedIds),
            selectIndex);
        if (selectedItems.Count == 0)
        {
            reason = "No curated seasonal gear is available in inventory or Armoury Chest.";
            SetState(State.Failed, reason);
            return false;
        }

        startingContentId = runtime.CharacterContentId;
        startingItems = runtime.GetEquippedItemIds().ToArray();
        selectedIndex = 0;
        SetState(State.MovingItem, $"Equipping {selectedItems[0].Name}.");
        reason = $"Selected {selectedItems.Count} seasonal slot item(s).";
        return true;
    }

    public void Tick()
    {
        if (!IsActive || startingGearset == null)
            return;

        if (runtime.CharacterContentId != startingContentId)
        {
            SetState(State.Failed, "Character changed during Seasonal Gear; native work was cancelled.");
            return;
        }

        if (runtime.CurrentGearsetId != startingGearset.GearsetId ||
            runtime.CurrentJobId != startingGearset.ClassJobId)
        {
            FailWithRestore("Player changed job or gearset during Seasonal Gear.");
            return;
        }

        var item = selectedIndex < selectedItems.Count ? selectedItems[selectedIndex] : null;
        switch (CurrentState)
        {
            case State.MovingItem:
                moveAttempts++;
                if (!runtime.TryMoveSeasonalItemToEquipped(item!, out var moveError))
                {
                    if (moveAttempts >= MaxMoveAttempts)
                        FailWithRestore($"Could not equip {item!.Name}: {moveError}");
                    else
                        SetState(State.WaitingForMove, $"Retrying {item!.Name}: {moveError}");
                    return;
                }

                SetState(State.WaitingForMove, $"Verifying {item!.Name}.");
                break;

            case State.WaitingForMove:
                if (runtime.IsSeasonalItemEquipped(item!))
                {
                    moveAttempts = 0;
                    selectedIndex++;
                    if (selectedIndex >= selectedItems.Count)
                        SetState(State.Saving, "Saving seasonal equipment to the current gearset.");
                    else
                        SetState(State.MovingItem, $"Equipping {selectedItems[selectedIndex].Name}.");
                }
                else if (StepTimedOut())
                {
                    if (moveAttempts >= MaxMoveAttempts)
                        FailWithRestore($"Move verification timed out for {item!.Name}.");
                    else
                        SetState(State.MovingItem, $"Retrying {item!.Name}.");
                }
                break;

            case State.Saving:
                expectedItems = runtime.GetEquippedItemIds().ToArray();
                if (!runtime.TryUpdateGearset(startingGearset.GearsetId, expectedItems, out var saveError))
                    FailWithRestore($"Native seasonal gearset save failed: {saveError}");
                else
                    SetState(State.WaitingForSave, "Verifying seasonal gearset save.");
                break;

            case State.WaitingForSave:
                if (runtime.IsGearsetSaveVerified(startingGearset.GearsetId, expectedItems, out _))
                    SetState(State.Complete, $"Saved {selectedItems.Count} seasonal slot item(s).");
                else if (StepTimedOut())
                {
                    runtime.IsGearsetSaveVerified(startingGearset.GearsetId, expectedItems, out var verifyError);
                    FailWithRestore($"Seasonal gearset save verification timed out: {verifyError}");
                }
                break;

            case State.Restoring:
                if (runtime.IsGearsetEquipped(startingGearset.GearsetId, startingGearset.ClassJobId) &&
                    EquipmentAutomationPolicy.ItemSignaturesMatch(startingItems, runtime.GetEquippedItemIds()))
                {
                    SetState(State.Failed, failureReason);
                    return;
                }

                if (StepTimedOut())
                {
                    if (!runtime.TryEquipGearset(startingGearset.GearsetId, out var restoreError))
                        failureReason = $"{failureReason} Restore failed: {restoreError}";
                    stateEnteredAt = runtime.UtcNow;
                }
                break;
        }
    }

    public void Cancel(string reason)
    {
        if (startingGearset != null && runtime.CharacterContentId == startingContentId)
            runtime.TryEquipGearset(startingGearset.GearsetId, out _);
        SetState(State.Cancelled, reason);
    }

    public void Reset()
    {
        selectedItems = [];
        expectedItems = [];
        startingItems = [];
        startingGearset = null;
        startingContentId = 0;
        selectedIndex = 0;
        moveAttempts = 0;
        failureReason = string.Empty;
        CurrentState = State.Idle;
        Status = "Idle";
        stateEnteredAt = DateTime.MinValue;
    }

    private bool StepTimedOut() => runtime.UtcNow - stateEnteredAt >= StepTimeout;

    private void FailWithRestore(string reason)
    {
        failureReason = reason;
        if (startingGearset == null)
        {
            SetState(State.Failed, reason);
            return;
        }

        runtime.TryEquipGearset(startingGearset.GearsetId, out _);
        SetState(State.Restoring, $"{reason} Restoring starting gearset.");
    }

    private void SetState(State state, string status)
    {
        CurrentState = state;
        Status = status;
        stateEnteredAt = runtime.UtcNow;
    }
}
