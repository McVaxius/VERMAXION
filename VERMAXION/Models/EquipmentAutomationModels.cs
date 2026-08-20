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

public sealed record UnlockedJobSnapshot(
    uint ClassJobId,
    bool IsCombat,
    bool IsJob,
    int Level,
    string Name,
    string Abbreviation);

public sealed record CurrentGearsetPersistenceResult(
    bool Success,
    int GearsetId,
    uint ClassJobId,
    bool Created,
    IReadOnlyList<uint> ExpectedItemIds,
    string Error);

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

public enum StylistGearsetUpdateProgress
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
    IReadOnlyList<UnlockedJobSnapshot> GetUnlockedJobs();
    IReadOnlyList<uint> GetEquippedItemIds();
    bool TryEquipGearset(int gearsetId, out string error);
    bool TryConfirmGearsetChangePrompt();
    bool IsGearsetEquipped(int gearsetId, uint classJobId);
    bool TryBeginRecommendedEquipment(uint classJobId, out string error);
    RecommendedEquipmentProgress PollRecommendedEquipment(out string error);
    void CancelRecommendedEquipment();
    bool TryBeginStylistGearsetUpdate(int gearsetId, out string error);
    StylistGearsetUpdateProgress PollStylistGearsetUpdate(out string error);
    bool TryMoveBestMainHandToEquipped(UnlockedJobSnapshot job, out string error);
    bool TryPersistCurrentGearset(out CurrentGearsetPersistenceResult result);
    bool TryUpdateGearset(int gearsetId, IReadOnlyList<uint> expectedItemIds, out string error);
    bool IsGearsetSaveVerified(int gearsetId, uint expectedClassJobId, IReadOnlyList<uint> expectedItemIds, out string error);
    IReadOnlyList<SeasonalInventoryItem> FindSeasonalInventoryItems(IReadOnlyCollection<uint> curatedItemIds);
    bool TryMoveSeasonalItemToEquipped(SeasonalInventoryItem item, out string error);
    bool IsSeasonalItemEquipped(SeasonalInventoryItem item);
}

internal sealed class GearsetConfirmationWindow
{
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(3);

    private DateTime closesAt;

    public bool IsOpen { get; private set; }

    public void Open(DateTime nowUtc)
    {
        closesAt = nowUtc + Duration;
        IsOpen = true;
    }

    public bool Poll(IEquipmentAutomationRuntime runtime)
    {
        if (!IsOpen)
            return true;

        var nowUtc = runtime.UtcNow;
        if (nowUtc > closesAt)
        {
            IsOpen = false;
            return true;
        }

        if (runtime.TryConfirmGearsetChangePrompt() || nowUtc >= closesAt)
        {
            IsOpen = false;
            return true;
        }

        return false;
    }

    public void Reset()
    {
        closesAt = DateTime.MinValue;
        IsOpen = false;
    }
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
    private readonly GearsetConfirmationWindow confirmationWindow = new();
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
    private bool verifyingStylistSave;
    private EquipmentTaskTerminalState restoreTerminalState;
    private string restoreFailure = string.Empty;

    public enum State
    {
        Idle,
        EquippingGearset,
        ConfirmingGearset,
        WaitingForGearset,
        StartingStylist,
        WaitingForStylist,
        StartingRecommended,
        WaitingForRecommended,
        SettlingRecommended,
        SavingGearset,
        WaitingForSave,
        RestoringStartingGearset,
        ConfirmingStartingGearset,
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

        if (runtime.UtcNow - startedAt >= OverallTimeout &&
            CurrentState is not (State.RestoringStartingGearset or State.ConfirmingStartingGearset))
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
                    restoreFailure = equipError;
                    confirmationWindow.Open(runtime.UtcNow);
                    SetState(
                        State.ConfirmingGearset,
                        $"Native gearset {target.GearsetId} request returned an error; checking for its confirmation prompt.");
                    break;
                }

                restoreFailure = string.Empty;
                confirmationWindow.Open(runtime.UtcNow);
                SetState(State.ConfirmingGearset, $"Checking for a confirmation prompt for {target.Name}");
                break;

            case State.ConfirmingGearset:
                if (!confirmationWindow.Poll(runtime))
                    break;

                SetState(
                    State.WaitingForGearset,
                    string.IsNullOrWhiteSpace(restoreFailure)
                        ? $"Verifying {target!.Name}"
                        : $"Verifying {target!.Name} after native error: {restoreFailure}");
                break;

            case State.WaitingForGearset:
                if (runtime.IsGearsetEquipped(target!.GearsetId, target.ClassJobId))
                {
                    equipAttempts = 0;
                    restoreFailure = string.Empty;
                    SetState(State.StartingStylist, $"Checking Stylist fast path for {target.Name}");
                }
                else if (StepTimedOut())
                {
                    if (equipAttempts >= MaxEquipAttempts)
                    {
                        var errorSuffix = string.IsNullOrWhiteSpace(restoreFailure)
                            ? string.Empty
                            : $" Last native error: {restoreFailure}";
                        FailWithRestore(
                            $"Gearset {target.GearsetId} did not become active after {equipAttempts} attempts.{errorSuffix}");
                    }
                    else
                        SetState(State.EquippingGearset, $"Retrying {target.Name}");
                }
                break;

            case State.StartingStylist:
                if (runtime.TryBeginStylistGearsetUpdate(target!.GearsetId, out var stylistError))
                {
                    verifyingStylistSave = false;
                    SetState(State.WaitingForStylist, $"Waiting for Stylist to update {target.Name}");
                }
                else
                {
                    SetState(
                        State.StartingRecommended,
                        string.IsNullOrWhiteSpace(stylistError)
                            ? $"Preparing native recommended gear for {target.Name}"
                            : $"Stylist unavailable for {target.Name}; using native updater: {stylistError}");
                }
                break;

            case State.WaitingForStylist:
                var stylistProgress = runtime.PollStylistGearsetUpdate(out var stylistProgressError);
                if (stylistProgress == StylistGearsetUpdateProgress.Complete)
                {
                    if (!runtime.IsGearsetEquipped(target!.GearsetId, target.ClassJobId))
                    {
                        FailWithRestore($"The active job or gearset changed during Stylist update for {target.Name}.");
                        break;
                    }

                    expectedItems = runtime.GetEquippedItemIds().ToArray();
                    verifyingStylistSave = true;
                    SetState(State.WaitingForSave, $"Verifying exact Stylist save for {target.Name}");
                }
                else if (stylistProgress == StylistGearsetUpdateProgress.Failed)
                {
                    var fallbackReason = string.IsNullOrWhiteSpace(stylistProgressError)
                        ? "bounded Stylist wait expired"
                        : stylistProgressError;
                    SetState(State.StartingRecommended, $"Stylist failed for {target!.Name}; using native updater: {fallbackReason}");
                }
                else if (StepTimedOut())
                {
                    FailWithRestore($"Stylist remained busy beyond the bounded wait for {target!.Name}.");
                }
                break;

            case State.StartingRecommended:
                verifyingStylistSave = false;
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
                    SetState(State.SettlingRecommended, $"Allowing {target!.Name} equipment to settle before persistence");
                else if (progress == RecommendedEquipmentProgress.Failed)
                    FailWithRestore($"Recommended equipment failed for {target!.Name}: {progressError}");
                else if (StepTimedOut())
                    FailWithRestore($"Recommended equipment timed out for {target!.Name}.");
                break;

            case State.SettlingRecommended:
                if (!runtime.IsGearsetEquipped(target!.GearsetId, target.ClassJobId))
                {
                    FailWithRestore($"The active job or gearset changed while recommended gear settled for {target.Name}.");
                    break;
                }

                if (runtime.UtcNow - stateEnteredAt >= TimeSpan.FromSeconds(2))
                    SetState(State.SavingGearset, $"Saving {target.Name}");
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
                if (runtime.IsGearsetSaveVerified(target!.GearsetId, target.ClassJobId, expectedItems, out _))
                {
                    CompleteTarget();
                }
                else if (StepTimedOut())
                {
                    runtime.IsGearsetSaveVerified(target.GearsetId, target.ClassJobId, expectedItems, out var verifyError);
                    if (verifyingStylistSave)
                    {
                        verifyingStylistSave = false;
                        SetState(State.StartingRecommended, $"Stylist exact-save verification failed for {target.Name}; using native updater: {verifyError}");
                    }
                    else
                    {
                        FailWithRestore($"Native save verification timed out for {target.Name}: {verifyError}");
                    }
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
                    if (equipAttempts >= MaxEquipAttempts)
                    {
                        restoreTerminalState = EquipmentTaskTerminalState.Failed;
                        Status = $"Could not verify restoration of starting gearset {startingGearsetId}: {restoreFailure}";
                        FinishRestore();
                        break;
                    }

                    equipAttempts++;
                    if (!runtime.TryEquipGearset(startingGearsetId, out var restoreError))
                        restoreFailure = restoreError;
                    else
                        restoreFailure = string.Empty;

                    confirmationWindow.Open(runtime.UtcNow);
                    SetState(
                        State.ConfirmingStartingGearset,
                        $"Checking for a confirmation prompt while restoring gearset {startingGearsetId}.");
                }
                break;

            case State.ConfirmingStartingGearset:
                if (!confirmationWindow.Poll(runtime))
                    break;

                SetState(
                    State.RestoringStartingGearset,
                    string.IsNullOrWhiteSpace(restoreFailure)
                        ? $"Verifying restoration of starting gearset {startingGearsetId}."
                        : $"Verifying restoration of starting gearset {startingGearsetId} after native error: {restoreFailure}");
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
        verifyingStylistSave = false;
        restoreTerminalState = EquipmentTaskTerminalState.None;
        restoreFailure = string.Empty;
        confirmationWindow.Reset();
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

    private void CompleteTarget()
    {
        CompletedTargetCount++;
        targetIndex++;
        if (targetIndex >= targets.Count)
            BeginRestore(EquipmentTaskTerminalState.Complete, "All class/job gearsets updated; restoring starting gearset.");
        else
            SetState(State.EquippingGearset, $"Preparing {targets[targetIndex].Name}");
    }

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

public sealed class GearsetBootstrapStateMachine
{
    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan OverallTimeout = TimeSpan.FromMinutes(5);

    private readonly IEquipmentAutomationRuntime runtime;
    private readonly GearsetConfirmationWindow confirmationWindow = new();
    private IReadOnlyList<UnlockedJobSnapshot> targets = [];
    private IReadOnlyList<uint> expectedItems = [];
    private DateTime startedAt;
    private DateTime stateEnteredAt;
    private ulong startingContentId;
    private uint startingJobId;
    private int anchorGearsetId = -1;
    private int targetIndex;
    private string lastSkipReason = string.Empty;

    public enum State
    {
        Idle,
        PersistingCurrentJob,
        WaitingForCurrentSave,
        MovingTargetMainHand,
        WaitingForTargetJob,
        StartingRecommended,
        WaitingForRecommended,
        SettlingRecommended,
        PersistingTarget,
        WaitingForTargetSave,
        RestoringAnchor,
        ConfirmingAnchor,
        WaitingForAnchor,
        Complete,
        Failed,
        Cancelled,
    }

    public GearsetBootstrapStateMachine(IEquipmentAutomationRuntime runtime)
    {
        this.runtime = runtime;
    }

    public State CurrentState { get; private set; } = State.Idle;
    public string Status { get; private set; } = "Idle";
    public bool IsActive => CurrentState is not (State.Idle or State.Complete or State.Failed or State.Cancelled);
    public bool IsComplete => CurrentState == State.Complete;
    public bool IsFailed => CurrentState is State.Failed or State.Cancelled;
    public int CreatedTargetCount { get; private set; }
    public int SkippedTargetCount { get; private set; }
    public int TargetCount => targets.Count;

    public bool Start(out string reason)
    {
        Reset();
        startingContentId = runtime.CharacterContentId;
        startingJobId = runtime.CurrentJobId;
        if (startingContentId == 0 || startingJobId == 0)
        {
            reason = "Current character or job data is unavailable.";
            SetState(State.Failed, reason);
            return false;
        }

        startedAt = runtime.UtcNow;
        SetState(State.PersistingCurrentJob, "Persisting the current job as the restoration anchor.");
        reason = Status;
        return true;
    }

    public void Tick()
    {
        if (!IsActive)
            return;

        if (runtime.CharacterContentId != startingContentId)
        {
            runtime.CancelRecommendedEquipment();
            SetState(State.Failed, "Character changed during gearset bootstrap.");
            return;
        }

        if (runtime.UtcNow - startedAt >= OverallTimeout &&
            CurrentState is not (State.RestoringAnchor or State.ConfirmingAnchor or State.WaitingForAnchor))
        {
            runtime.CancelRecommendedEquipment();
            BeginRestore("Gearset bootstrap exceeded its five-minute overall timeout.", failed: true);
            return;
        }

        var target = targetIndex < targets.Count ? targets[targetIndex] : null;
        switch (CurrentState)
        {
            case State.PersistingCurrentJob:
                if (!runtime.TryPersistCurrentGearset(out var currentResult) || !currentResult.Success)
                {
                    SetState(State.Failed, $"Current-job persistence is required: {currentResult.Error}");
                    break;
                }

                anchorGearsetId = currentResult.GearsetId;
                expectedItems = currentResult.ExpectedItemIds;
                SetState(State.WaitingForCurrentSave, "Verifying the exact current-job restoration anchor.");
                break;

            case State.WaitingForCurrentSave:
                if (runtime.IsGearsetSaveVerified(anchorGearsetId, startingJobId, expectedItems, out _))
                {
                    var existingJobs = runtime.GetValidGearsets()
                        .Select(gearset => gearset.ClassJobId)
                        .ToHashSet();
                    targets = runtime.GetUnlockedJobs()
                        .Where(job => job.ClassJobId != startingJobId && !existingJobs.Contains(job.ClassJobId))
                        .OrderBy(job => job.ClassJobId)
                        .ToList();
                    targetIndex = 0;
                    if (targets.Count == 0)
                        BeginRestore("Current job persisted; no missing unlocked class/job gearsets were found.", failed: false);
                    else
                        SetState(State.MovingTargetMainHand, $"Preparing missing {targets[0].Name} gearset.");
                }
                else if (StepTimedOut())
                {
                    runtime.IsGearsetSaveVerified(anchorGearsetId, startingJobId, expectedItems, out var verifyError);
                    SetState(State.Failed, $"Current-job persistence could not be verified: {verifyError}");
                }
                break;

            case State.MovingTargetMainHand:
                if (!runtime.TryMoveBestMainHandToEquipped(target!, out var moveError))
                {
                    SkipTarget(moveError);
                    break;
                }

                SetState(State.WaitingForTargetJob, $"Waiting for exact {target!.Abbreviation} job switch.");
                break;

            case State.WaitingForTargetJob:
                if (runtime.CurrentJobId == target!.ClassJobId)
                    SetState(State.StartingRecommended, $"Preparing recommended equipment for {target.Name}.");
                else if (StepTimedOut())
                    SkipTarget($"Main-hand move did not switch to exact job {target.Abbreviation} within 15 seconds.");
                break;

            case State.StartingRecommended:
                if (!runtime.TryBeginRecommendedEquipment(target!.ClassJobId, out var recommendedError))
                    SkipTarget($"Recommended equipment setup failed for {target.Name}: {recommendedError}");
                else
                    SetState(State.WaitingForRecommended, $"Equipping recommended {target.Name} gear.");
                break;

            case State.WaitingForRecommended:
                if (runtime.CurrentJobId != target!.ClassJobId)
                {
                    runtime.CancelRecommendedEquipment();
                    SkipTarget($"Job changed while recommended {target.Name} gear was being prepared.");
                    break;
                }

                var progress = runtime.PollRecommendedEquipment(out var progressError);
                if (progress == RecommendedEquipmentProgress.Complete)
                    SetState(State.SettlingRecommended, $"Allowing {target.Name} equipment to settle before persistence.");
                else if (progress == RecommendedEquipmentProgress.Failed)
                    SkipTarget($"Recommended equipment failed for {target.Name}: {progressError}");
                else if (StepTimedOut())
                {
                    runtime.CancelRecommendedEquipment();
                    SkipTarget($"Recommended equipment timed out for {target.Name}.");
                }
                break;

            case State.SettlingRecommended:
                if (runtime.CurrentJobId != target!.ClassJobId)
                    SkipTarget($"Job changed while {target.Name} equipment settled.");
                else if (runtime.UtcNow - stateEnteredAt >= TimeSpan.FromSeconds(2))
                    SetState(State.PersistingTarget, $"Persisting exact {target.Name} equipment.");
                break;

            case State.PersistingTarget:
                if (!runtime.TryPersistCurrentGearset(out var targetResult) || !targetResult.Success)
                {
                    SkipTarget($"Could not persist {target!.Name}: {targetResult.Error}");
                    break;
                }

                expectedItems = targetResult.ExpectedItemIds;
                SetState(State.WaitingForTargetSave, $"Verifying exact {target!.Name} gearset {targetResult.GearsetId}.");
                pendingTargetGearsetId = targetResult.GearsetId;
                break;

            case State.WaitingForTargetSave:
                if (runtime.IsGearsetSaveVerified(pendingTargetGearsetId, target!.ClassJobId, expectedItems, out _))
                {
                    CreatedTargetCount++;
                    AdvanceTarget();
                }
                else if (StepTimedOut())
                {
                    runtime.IsGearsetSaveVerified(pendingTargetGearsetId, target!.ClassJobId, expectedItems, out var targetVerifyError);
                    SkipTarget($"Exact {target.Name} save could not be verified: {targetVerifyError}");
                }
                break;

            case State.RestoringAnchor:
                if (runtime.IsGearsetEquipped(anchorGearsetId, startingJobId))
                {
                    FinishRestore();
                    break;
                }

                if (!runtime.TryEquipGearset(anchorGearsetId, out var restoreError))
                    lastSkipReason = restoreError;
                confirmationWindow.Open(runtime.UtcNow);
                SetState(State.ConfirmingAnchor, "Checking for a confirmation prompt while restoring the current-job anchor.");
                break;

            case State.ConfirmingAnchor:
                if (confirmationWindow.Poll(runtime))
                    SetState(State.WaitingForAnchor, "Verifying current-job anchor restoration.");
                break;

            case State.WaitingForAnchor:
                if (runtime.IsGearsetEquipped(anchorGearsetId, startingJobId))
                    FinishRestore();
                else if (StepTimedOut())
                    SetState(State.Failed, $"Current-job anchor restoration failed: {lastSkipReason}");
                break;
        }
    }

    private int pendingTargetGearsetId = -1;
    private bool failAfterRestore;
    private string completionStatus = string.Empty;

    public void Cancel(string reason)
    {
        if (!IsActive)
            return;
        runtime.CancelRecommendedEquipment();
        if (runtime.CharacterContentId == startingContentId && anchorGearsetId >= 0)
            runtime.TryEquipGearset(anchorGearsetId, out _);
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
        startingJobId = 0;
        anchorGearsetId = -1;
        pendingTargetGearsetId = -1;
        targetIndex = 0;
        lastSkipReason = string.Empty;
        failAfterRestore = false;
        completionStatus = string.Empty;
        confirmationWindow.Reset();
        CreatedTargetCount = 0;
        SkippedTargetCount = 0;
        CurrentState = State.Idle;
        Status = "Idle";
    }

    private bool StepTimedOut() => runtime.UtcNow - stateEnteredAt >= StepTimeout;

    private void SkipTarget(string reason)
    {
        runtime.CancelRecommendedEquipment();
        SkippedTargetCount++;
        lastSkipReason = reason;
        AdvanceTarget();
    }

    private void AdvanceTarget()
    {
        targetIndex++;
        pendingTargetGearsetId = -1;
        expectedItems = [];
        if (targetIndex >= targets.Count)
        {
            BeginRestore(
                $"Gearset bootstrap created {CreatedTargetCount} and skipped {SkippedTargetCount} missing class/job gearset(s)." +
                (string.IsNullOrWhiteSpace(lastSkipReason) ? string.Empty : $" Last skip: {lastSkipReason}"),
                failed: false);
        }
        else
        {
            SetState(State.MovingTargetMainHand, $"Preparing missing {targets[targetIndex].Name} gearset.");
        }
    }

    private void BeginRestore(string status, bool failed)
    {
        runtime.CancelRecommendedEquipment();
        completionStatus = status;
        failAfterRestore = failed;
        if (anchorGearsetId < 0 || runtime.CurrentJobId == startingJobId && runtime.CurrentGearsetId == anchorGearsetId)
            FinishRestore();
        else
            SetState(State.RestoringAnchor, $"{status} Restoring current-job anchor.");
    }

    private void FinishRestore()
        => SetState(failAfterRestore ? State.Failed : State.Complete, completionStatus);

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
    private readonly GearsetConfirmationWindow confirmationWindow = new();
    private GearsetSnapshot? target;
    private DateTime lastAttemptAt;
    private string lastEquipError = string.Empty;
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

        if (confirmationWindow.IsOpen)
        {
            if (!confirmationWindow.Poll(runtime))
                return;

            lastAttemptAt = runtime.UtcNow;
            Status = string.IsNullOrWhiteSpace(lastEquipError)
                ? $"Verifying gearset {target.GearsetId} after its confirmation window."
                : $"Verifying gearset {target.GearsetId} after native error: {lastEquipError}";
            return;
        }

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
        if (!runtime.TryEquipGearset(target.GearsetId, out var error))
            lastEquipError = error;
        else
            lastEquipError = string.Empty;

        confirmationWindow.Open(runtime.UtcNow);
        Status = attempts >= MaxAttempts
            ? $"Polling the final confirmation window for {target.Name}."
            : $"Polling the confirmation window for {target.Name}.";
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
        lastEquipError = string.Empty;
        attempts = 0;
        confirmationWindow.Reset();
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
        SettlingRecommended,
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
                    SetState(State.SettlingRecommended, "Allowing recommended equipment to settle before persistence.");
                else if (progress == RecommendedEquipmentProgress.Failed)
                    SetState(State.Failed, $"Recommended equipment failed: {progressError}");
                else if (StepTimedOut())
                {
                    runtime.CancelRecommendedEquipment();
                    SetState(State.Failed, "Recommended equipment timed out.");
                }
                break;

            case State.SettlingRecommended:
                if (runtime.UtcNow - stateEnteredAt >= TimeSpan.FromSeconds(2))
                    SetState(State.Saving, "Saving the captured gearset natively.");
                break;

            case State.Saving:
                expectedItems = runtime.GetEquippedItemIds().ToArray();
                if (!runtime.TryUpdateGearset(startingGearset.GearsetId, expectedItems, out var saveError))
                    SetState(State.Failed, $"Native gearset update failed: {saveError}");
                else
                    SetState(State.WaitingForSave, "Verifying the exact native gearset save.");
                break;

            case State.WaitingForSave:
                if (runtime.IsGearsetSaveVerified(startingGearset.GearsetId, startingGearset.ClassJobId, expectedItems, out _))
                    SetState(State.Complete, $"Updated gearset {startingGearset.GearsetId}.");
                else if (StepTimedOut())
                {
                    runtime.IsGearsetSaveVerified(startingGearset.GearsetId, startingGearset.ClassJobId, expectedItems, out var verifyError);
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
    private const int MaxRestoreAttempts = 3;
    private readonly IEquipmentAutomationRuntime runtime;
    private readonly GearsetConfirmationWindow confirmationWindow = new();
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
    private int restoreAttempts;
    private string failureReason = string.Empty;

    public enum State
    {
        Idle,
        MovingItem,
        WaitingForMove,
        Saving,
        WaitingForSave,
        Restoring,
        ConfirmingRestore,
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

        if (CurrentState is not (State.Restoring or State.ConfirmingRestore) &&
            (runtime.CurrentGearsetId != startingGearset.GearsetId ||
             runtime.CurrentJobId != startingGearset.ClassJobId))
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
                if (runtime.IsGearsetSaveVerified(startingGearset.GearsetId, startingGearset.ClassJobId, expectedItems, out _))
                    SetState(State.Complete, $"Saved {selectedItems.Count} seasonal slot item(s).");
                else if (StepTimedOut())
                {
                    runtime.IsGearsetSaveVerified(startingGearset.GearsetId, startingGearset.ClassJobId, expectedItems, out var verifyError);
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

                if (restoreAttempts == 0 || StepTimedOut())
                {
                    if (restoreAttempts >= MaxRestoreAttempts)
                    {
                        SetState(State.Failed, $"{failureReason} Starting gearset restoration could not be verified after {restoreAttempts} attempts.");
                        break;
                    }

                    restoreAttempts++;
                    if (!runtime.TryEquipGearset(startingGearset.GearsetId, out var restoreError))
                        failureReason = $"{failureReason} Restore failed: {restoreError}";

                    confirmationWindow.Open(runtime.UtcNow);
                    SetState(
                        State.ConfirmingRestore,
                        $"{failureReason} Checking for a restoration confirmation prompt.");
                }
                break;

            case State.ConfirmingRestore:
                if (!confirmationWindow.Poll(runtime))
                    break;

                SetState(State.Restoring, $"{failureReason} Verifying starting gearset restoration.");
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
        restoreAttempts = 0;
        failureReason = string.Empty;
        confirmationWindow.Reset();
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

        restoreAttempts = 0;
        SetState(State.Restoring, $"{reason} Restoring starting gearset.");
    }

    private void SetState(State state, string status)
    {
        CurrentState = state;
        Status = status;
        stateEnteredAt = runtime.UtcNow;
    }
}
