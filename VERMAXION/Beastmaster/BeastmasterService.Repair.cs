using System;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using VERMAXION.IPC;
using VERMAXION.Services;

namespace VERMAXION.Beastmaster;

internal sealed unsafe partial class BeastmasterService
{
    private readonly AdsIpcClient ads;
    private BeastmasterSettings? repairBoundarySettings;
    private int repairBoundaryRun;
    private uint repairBoundaryBoard;
    private bool repairRequestAccepted;
    private bool repairUtilityObserved;
    private bool repairCancellationRequested;
    private string repairRequestedMode = string.Empty;
    private ulong repairCharacter;
    private DateTime repairRequestedAt;
    private int? repairDurabilityBefore;
    private bool extractionPrepared;
    private DateTime foodPendingUntil;
    private DateTime foodNextAttempt;
    private float foodPriorRemaining;
    internal string RepairStatus { get; private set; } = string.Empty;
    internal string FoodStatus { get; private set; } = string.Empty;

    private bool TryPrepareRegistrationRepair()
    {
        if (rankingCheckboxTest) return false;
        // A resumed Start keeps observing its accepted utility rather than
        // issuing a second request or restarting its NPC travel.
        if (repairRequestAccepted && repairCharacter == owner)
        {
            repairBoundarySettings = runSettings;
            repairBoundaryRun = CurrentRun;
            repairBoundaryBoard = Settings.Board;
            Transition(RunState.Repairing, "Repairing before entry");
            return true;
        }
        if (ReferenceEquals(repairBoundarySettings, runSettings) &&
            repairBoundaryRun == CurrentRun && repairBoundaryBoard == Settings.Board) return false;
        repairBoundarySettings = runSettings;
        repairBoundaryRun = CurrentRun;
        repairBoundaryBoard = Settings.Board;
        extractionPrepared = false;
        RepairStatus = string.Empty;
        FoodStatus = string.Empty;
        foodPendingUntil = foodNextAttempt = default;
        foodPriorRemaining = 0;

        // Existing native entry/results interactions always own progression.
        // Repair is only preparation for a board that has yet to register.
        if (operatorStopped || IsPaused || resultSeen || HasOpenRegistrationScreen || EntryPending ||
            !NativeQueueIsIdle() || IsCrucibleTerritory(Svc.ClientState.TerritoryType) ||
            Svc.Condition[ConditionFlag.BoundByDuty] || Svc.Condition[ConditionFlag.BoundByDuty56] ||
            CrucibleUi.IsVisible(CrucibleUi.ResultWindow) || CrucibleUi.IsVisible(CrucibleUi.YesNo)) return false;
        var mode = BeastmasterRepairPolicy.ToAdsMode(Settings.RepairMode);
        if (mode.Length == 0) { RepairStatus = "Repair disabled."; return false; }
        if (Settings.RepairThreshold == 0)
        { RepairStatus = "Repair threshold is 0%; automatic repair is disabled."; return false; }
        if (!TryReadRepairableDurability(out var durability))
        { RepairStatus = "Repair skipped: equipped durability is unavailable."; return false; }
        if (!BeastmasterRepairPolicy.ShouldRepair(Settings.RepairMode, Settings.RepairThreshold, durability))
        {
            RepairStatus = $"Equipped durability {durability}%; repair starts below {Settings.RepairThreshold}%.";
            return false;
        }
        if (!RepairPlayerReady() || ownsNavigation || ownsTravel)
        { RepairStatus = "Repair skipped: the player is busy; continuing."; return false; }
        var snapshot = ads.Refresh(force: true);
        if (!snapshot.IsAvailable || !snapshot.StatusReadable)
        { RepairStatus = "Repair skipped: ADS is unavailable; continuing."; return false; }
        if (snapshot.UtilityRunning)
        { RepairStatus = "Repair skipped: another ADS utility is active; continuing."; return false; }

        StopNavigation();
        repairRequestedMode = mode;
        repairCharacter = owner;
        repairRequestedAt = DateTime.UtcNow;
        repairDurabilityBefore = durability;
        repairUtilityObserved = false;
        repairCancellationRequested = false;
        if (!ads.StartRepair(mode, out _))
        {
            ClearRepairRequest();
            RepairStatus = "Repair was not accepted; continuing.";
            return false;
        }
        repairRequestAccepted = true;
        RepairStatus = "Repair requested; waiting for ADS.";
        Transition(RunState.Repairing, "Repairing before entry");
        return true;
    }

    private bool TryPrepareRegistrationExtraction()
    {
        if (rankingCheckboxTest || extractionPrepared) return false;
        extractionPrepared = true;
        if (!Settings.ExtractMateria || operatorStopped || IsPaused || resultSeen || EntryPending ||
            HasOpenRegistrationScreen || !NativeQueueIsIdle() ||
            IsCrucibleTerritory(Svc.ClientState.TerritoryType) || Svc.Condition[ConditionFlag.BoundByDuty] ||
            Svc.Condition[ConditionFlag.BoundByDuty56] || !RepairPlayerReady() || ownsNavigation || ownsTravel ||
            CrucibleUi.IsVisible(CrucibleUi.ResultWindow) || CrucibleUi.IsVisible(CrucibleUi.YesNo)) return false;
        var snapshot = ads.Refresh(force: true);
        if (!snapshot.IsAvailable || !snapshot.StatusReadable || snapshot.UtilityRunning)
        { RepairStatus = "Materia extraction unavailable; continuing."; return false; }
        StopNavigation();
        repairRequestedMode = "extract-materia";
        repairCharacter = owner;
        repairRequestedAt = DateTime.UtcNow;
        repairUtilityObserved = repairCancellationRequested = false;
        if (!ads.StartExtractMateria())
        { ClearRepairRequest(); RepairStatus = "Materia extraction unavailable; continuing."; return false; }
        repairRequestAccepted = true;
        RepairStatus = "Extracting materia before entry.";
        Transition(RunState.Repairing, "Preparing supplies");
        return true;
    }

    // Called only by pre-entry preparation, a safe board walkway or the actual team window.
    // A temporary out-of-combat flag after revival is never a food opportunity.
    internal bool TryPrepareFood(bool betweenFights = false)
    {
        if (rankingCheckboxTest || operatorStopped || IsPaused || resultSeen || Settings.FoodItemId == 0 ||
            BeastmasterPlayer.HasStatus(418) || !BeastmasterPlayer.IsValid ||
            Svc.Condition[ConditionFlag.InCombat] || Svc.Objects.LocalPlayer is not { IsDead: false } ||
            betweenFights && (!IsCrucibleTerritory(Svc.ClientState.TerritoryType) ||
                !(route?.FoodBoundary == true || CrucibleUi.IsOpen(CrucibleUi.BoardLayout) &&
                    CrucibleUi.IsOpen(CrucibleUi.TeamWindow))) ||
            !betweenFights && (IsCrucibleTerritory(Svc.ClientState.TerritoryType) ||
                Svc.Condition[ConditionFlag.BoundByDuty] || Svc.Condition[ConditionFlag.BoundByDuty56] ||
                EntryPending || !NativeQueueIsIdle() || HasOpenRegistrationScreen) ||
            CrucibleUi.IsVisible(CrucibleUi.ResultWindow) ||
            CrucibleUi.IsVisible(CrucibleUi.YesNo)) return false;
        var now = DateTime.UtcNow;
        var remaining = GameHelpers.GetStatusTimeRemaining(48);
        if (foodPendingUntil != default)
        {
            if (remaining > foodPriorRemaining)
            { foodPendingUntil = default; FoodStatus = $"Well Fed: {remaining:F0}s."; return false; }
            if (now < foodPendingUntil) return true;
            foodPendingUntil = default;
            foodNextAttempt = now.AddSeconds(5);
            FoodStatus = "Food was not confirmed; continuing.";
            return false;
        }
        if (!BeastmasterRepairPolicy.ShouldRefreshFood(Settings.FoodItemId, remaining) || now < foodNextAttempt)
            return false;
        if (!BeastmasterPlayer.IsReady)
        {
            FoodStatus = "Selected food is not usable; continuing.";
            return false;
        }
        var useId = Settings.FoodItemId;
        var hq = useId >= 1_000_000;
        var baseId = hq ? useId - 1_000_000 : useId;
        var inventory = InventoryManager.Instance();
        if (inventory == null || inventory->GetInventoryItemCount(baseId, hq) == 0)
        { foodNextAttempt = now.AddSeconds(5); FoodStatus = "Selected food unavailable; continuing."; return false; }
        if (!Svc.Data.GetExcelSheet<Item>().TryGetRow(baseId, out var item) ||
            item.ItemUICategory.RowId != 46 || item.ItemAction.ValueNullable is not { } action ||
            action.Data[0] != 48)
        { foodNextAttempt = now.AddSeconds(5); FoodStatus = "Selected food is not usable; continuing."; return false; }
        var stoppedForFood = ownsNavigation;
        if (ownsNavigation)
        {
            StopNavigation();
            if (ownsNavigation) { FoodStatus = "Preparing supplies"; return true; }
        }
        foodNextAttempt = now.AddSeconds(5);
        if (GameHelpers.GetItemActionStatus(useId) != 0 || !GameHelpers.UseItem(useId))
        { FoodStatus = "Selected food is not usable; continuing."; return stoppedForFood; }
        foodPriorRemaining = remaining;
        foodPendingUntil = now.AddSeconds(5);
        FoodStatus = "Eating selected food.";
        return true;
    }

    private static bool RepairPlayerReady() => BeastmasterPlayer.IsReady && RegistrationPlayerReady &&
        !Svc.Condition[ConditionFlag.Mounted] && !Svc.Condition[ConditionFlag.Mounting] &&
        !Svc.Condition[ConditionFlag.Mounting71] && !Svc.Condition[ConditionFlag.InFlight] &&
        !Svc.Condition[ConditionFlag.Diving] && !CrucibleUi.IsVisible("ContentsFinderConfirm");

    private static bool TryReadRepairableDurability(out int lowest)
    {
        lowest = int.MaxValue;
        try
        {
            var manager = InventoryManager.Instance();
            var equipped = manager == null ? null : manager->GetInventoryContainer(InventoryType.EquippedItems);
            if (equipped == null || !equipped->IsLoaded) return false;
            var items = Svc.Data.GetExcelSheet<Item>();
            for (var index = 0; index < equipped->Size; index++)
            {
                var slot = equipped->GetInventorySlot(index);
                if (slot == null || slot->ItemId == 0) continue;
                if (!items.TryGetRow(slot->ItemId, out var item)) return false;
                var condition = BeastmasterRepairPolicy.ConditionPercent(item.ClassJobRepair.RowId,
                    item.ItemRepair.RowId, slot->Condition);
                if (condition is { } percent) lowest = Math.Min(lowest, percent);
            }
            return lowest != int.MaxValue;
        }
        catch { return false; }
    }

    private bool RepairMatches(AdsUtilityStatusSnapshot snapshot) =>
        repairCharacter == owner && CanObserveCharacter &&
        BeastmasterRepairPolicy.CanCancelOwnedRequest(repairRequestAccepted, repairRequestedAt,
            repairRequestedMode, snapshot.StatusReadable, snapshot.UtilityRunning,
            snapshot.UtilityTask, snapshot.UtilityMode, snapshot.UtilityCompletedAtUtc);

    private bool UpdateRegistrationRepair()
    {
        if (State != RunState.Repairing)
        {
            // The matching Yes prompt is handled before this method. Once it
            // advances entry, repair must release instead of owning that flow.
            if (repairRequestAccepted && State is RunState.WaitingDuty or RunState.Running or RunState.Leaving)
            {
                if (!repairCancellationRequested) CancelOwnedRepair();
                else ObserveStoppedRepair();
            }
            return false;
        }
        // An already-visible entry or an entered duty takes its ordinary path.
        // Reinspect even after cancellation, including a duty entered while paused.
        if (IsCrucibleTerritory(Svc.ClientState.TerritoryType) || HasOpenRegistrationScreen || EntryPending ||
            !NativeQueueIsIdle() || CrucibleUi.IsVisible(CrucibleUi.ResultWindow))
        {
            if (!repairCancellationRequested) CancelOwnedRepair();
            else ObserveStoppedRepair();
            if (!operatorStopped && !IsPaused)
            {
                if (IsCrucibleTerritory(Svc.ClientState.TerritoryType)) BeginRoute(resume: true);
                else RestartFromCurrent(Settings);
            }
            return true;
        }
        if (!repairRequestAccepted)
        {
            if (!operatorStopped && !IsPaused && BeastmasterPlayer.IsValid) BeginRegistrationAfterRepair();
            return true;
        }
        var snapshot = ads.Refresh();
        var elapsed = DateTime.UtcNow - repairRequestedAt;
        if (repairRequestedMode == "extract-materia")
        {
            if (RepairMatches(snapshot) && elapsed < TimeSpan.FromSeconds(180))
            { RepairStatus = "Extracting materia before entry."; return true; }
            if (snapshot.StatusReadable && snapshot.UtilityRunning)
            {
                if (RepairMatches(snapshot)) CancelOwnedRepair();
                ClearRepairRequest();
                RepairStatus = "Materia extraction unconfirmed; continuing.";
                return true;
            }
            if (!BeastmasterRepairPolicy.HasFreshCompletion(repairRequestedAt, snapshot.StatusReadable,
                snapshot.UtilityRunning, snapshot.UtilityCompletedAtUtc) && elapsed < TimeSpan.FromSeconds(180))
                return true;
            var result = ads.ReadExtractionStatus();
            var confirmed = result.Readable && result.Done && !result.Running && result.Succeeded == true &&
                result.CompletedAtUtc >= repairRequestedAt;
            ClearRepairRequest();
            RepairStatus = confirmed ? "Materia extraction complete." : "Materia extraction unconfirmed; continuing.";
            return true;
        }
        if (RepairMatches(snapshot))
        {
            repairUtilityObserved = true;
            if (elapsed >= TimeSpan.FromSeconds(180))
            {
                if (!repairCancellationRequested) CancelOwnedRepair();
                // A failed cancellation cannot turn preparation into a
                // permanent Start/Stop gate. Never re-dispatch this boundary.
                ClearRepairRequest();
                RepairStatus = "Repair timed out; continuing.";
            }
            else if (!repairCancellationRequested) RepairStatus = "Repairing equipment; entry is paused.";
            return true;
        }
        if (snapshot.StatusReadable && snapshot.UtilityRunning)
        {
            // Matching task/mode is not sufficient after a fresh completion:
            // another caller may now own ADS, so never cancel its utility.
            ClearRepairRequest();
            RepairStatus = "Repair ownership changed; continuing.";
            return true;
        }
        var completed = BeastmasterRepairPolicy.HasFreshCompletion(repairRequestedAt,
            snapshot.StatusReadable, snapshot.UtilityRunning, snapshot.UtilityCompletedAtUtc);
        if (!completed && !repairUtilityObserved && elapsed < TimeSpan.FromSeconds(5)) return true;
        if (!completed && elapsed < TimeSpan.FromSeconds(180)) return true;
        if (!completed)
        {
            ClearRepairRequest();
            RepairStatus = "Repair timed out; continuing.";
            return true;
        }
        if (!string.IsNullOrWhiteSpace(snapshot.UtilityLastFailure))
        {
            ClearRepairRequest();
            RepairStatus = "Repair failed; continuing.";
            return true;
        }
        // ADS keeps NPC+inn running through the inn return. An idle receipt plus
        // normal player readiness establishes that its selected travel settled.
        if (!BeastmasterPlayer.IsReady || ownsTravel && (!travel.TryReadBusy(out var busy) || busy))
        {
            if (elapsed < TimeSpan.FromSeconds(180))
            { RepairStatus = "Repair finished; waiting for travel to settle."; return true; }
            ClearRepairRequest();
            RepairStatus = "Repair timed out; continuing.";
            return true;
        }
        FinishRepairObservation();
        return true;
    }

    private void FinishRepairObservation()
    {
        var before = repairDurabilityBefore;
        ClearRepairRequest();
        if (!TryReadRepairableDurability(out var after))
        { RepairStatus = "Repair ended; equipped durability is unavailable; continuing."; return; }
        if (before is not { } initial || after <= initial)
        { RepairStatus = "Repair did not improve equipped durability; continuing."; return; }
        RepairStatus = BeastmasterRepairPolicy.ShouldRepair(Settings.RepairMode, Settings.RepairThreshold, after)
            ? $"Repair improved durability to {after}%, but it is still below {Settings.RepairThreshold}%; continuing."
            : $"Repair complete; equipped durability {after}%.";
    }

    private void CancelOwnedRepair()
    {
        if (!repairRequestAccepted) return;
        var snapshot = ads.Refresh(force: true);
        if (!RepairMatches(snapshot))
        {
            // Unknown or replaced ADS state cannot authorize cancellation.
            ClearRepairRequest();
            return;
        }
        repairCancellationRequested = true;
        if (ads.CancelUtility(out _))
        {
            ClearRepairRequest();
            RepairStatus = "Repair cancelled.";
        }
        else RepairStatus = "Repair cancellation is unconfirmed; waiting for the owned utility to settle.";
    }

    private void ObserveStoppedRepair()
    {
        var snapshot = ads.Refresh();
        if (DateTime.UtcNow - repairRequestedAt < TimeSpan.FromSeconds(180) &&
            (RepairMatches(snapshot) || !snapshot.StatusReadable)) return;
        ClearRepairRequest();
    }

    private void ClearRepairRequest()
    {
        repairRequestAccepted = false;
        repairUtilityObserved = false;
        repairCancellationRequested = false;
        repairRequestedMode = string.Empty;
        repairCharacter = 0;
        repairRequestedAt = default;
        repairDurabilityBefore = null;
    }
}
