using System;
using System.Linq;
using System.Numerics;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using VERMAXION.IPC;
using Action = System.Action;

namespace VERMAXION.Beastmaster;

internal sealed unsafe partial class BeastmasterService : IDisposable
{
    internal enum RunState { Idle, Refreshing, Travelling, OpeningBoard, SettingTeam, Confirming, WaitingDuty, Running, Leaving, WeeklyVerifying, Recovering, Complete, Failed, Repairing, WeeklyPreparing }
    private readonly Func<BeastmasterSettings> getSettings;
    private readonly Func<BeastmasterCharacterData> getCharacterData;
    private readonly Action save;
    private readonly VNavmeshIPC navigation;
    private readonly LifestreamIPC travel;
    private readonly CrucibleMenus menus = new();
    private IBeastmasterCombat combat = new BeastmasterCombat();
    private BeastmasterCombatProvider combatProvider;
    private BeastmasterSettings? runSettings;
    private BeastmasterCharacterData? runData;
    private BeastmasterSettings? displayedRunSettings;
    private ulong displayedRunCharacter;
    private int displayedRunLimit;
    private bool displayedWeekly;
    private bool hasRunDisplay;
    private bool completionCommandPending;
    private BeastmasterRoute? route;
    private ulong owner;
    private DateTime stateSince;
    private DateTime pausedAt;
    private DateTime nextTravel;
    private DateTime nextCleanupAttempt;
    private DateTime challengedAt;
    private bool ownsNavigation;
    private bool ownsTravel;
    private bool resultSeen;
    private DateTime resultReadStarted;
    private bool resultRanksObserved;
    private bool resultRanksUnavailable;
    private readonly BeastmasterReadRecovery registrationReadRecovery = new();
    private string lastReadFailure = string.Empty;
    private string lastCleanupFailure = string.Empty;
    private bool confirmingTrainingCompletion;
    private bool ownershipNotebookRequested;
    private System.Collections.Generic.HashSet<uint> expectedResultTeam = [];
    private bool waitingForRevival;
    private bool disposed;
    private bool operatorStopped = true;
    internal bool StopAfterNextOutcome { get; private set; }
    private RunState recoveryState;
    private DateTime recoveryAt;
    private string status = "Idle";
    private Vector3 stationaryPosition;
    private DateTime stationarySince;

    public RunState State { get; private set; }
    public bool IsPaused { get; private set; }
    public bool IsActive => State is not (RunState.Idle or RunState.Complete or RunState.Failed) || ownsNavigation || ownsTravel || EntryPending || combat.OwnsState || refreshCleanupPending || repairRequestAccepted;
    public string Status => EntryPending && entryAttempt!.StopRequested
        ? "Stopping Crucible entry."
        : IsPaused ? "Paused: " + status : State == RunState.Running && menus.Status.Length > 0 ? menus.Status : status;
    public string StatusText => Status;
    public int CurrentRun { get; private set; }
    internal bool HasRunDisplay => hasRunDisplay && displayedRunSettings != null && Svc.PlayerState.IsLoaded &&
        Svc.PlayerState.ContentId == displayedRunCharacter;
    internal int DisplayRunLimit => Math.Max(1, displayedRunLimit);
    internal uint DisplayBoard => HasRunDisplay ? displayedRunSettings!.Board : Settings.Board;
    internal int DisplayTrainingTarget => BeastmasterSelection.TrainingTarget(HasRunDisplay ? displayedRunSettings! : Settings);
    internal BeastmasterRunGoal DisplayGoal => HasRunDisplay ? displayedRunSettings!.Goal : Settings.Goal;
    internal bool DisplayWeekly => HasRunDisplay && displayedWeekly;
    internal int DisplayCompletedRuns => HasRunDisplay
        ? BeastmasterSelection.CompletedBoardRuns(CurrentRun, resultSeen, DisplayRunLimit) : 0;
    internal int ScanReadCount => teamSetup.RankScanReadCount;
    internal int ScanTotal => teamSetup.RankScanTotal;
    internal string DisplayPhase => IsPaused ? "Paused" : State switch
    {
        RunState.Travelling => "Travelling to Lauda",
        RunState.OpeningBoard => fastRankScanPending ? "Checking new beasts" : "Preparing team",
        RunState.SettingTeam => "Preparing team",
        RunState.Confirming or RunState.WaitingDuty => "Waiting to enter the Crucible",
        RunState.Running => resultSeen ? "Finishing board" : waitingForRevival ? "Waiting for revival"
            : Svc.Condition[ConditionFlag.InCombat] ? "Fighting" : "Following the Crucible board",
        RunState.Leaving => "Leaving the completed board",
        RunState.WeeklyVerifying => "Waiting for fresh weekly challenge credit.",
        RunState.WeeklyPreparing => "Waiting for a fresh Challenge Log load for this character and reset.",
        RunState.Recovering => "Recovering",
        RunState.Refreshing => "Refresh beasts",
        RunState.Repairing => "Repairing before entry",
        RunState.Complete => "Complete",
        RunState.Failed => "Unavailable",
        _ => IsActive ? "Stopping" : HasRunDisplay ? "Stopped" : "Ready",
    };

    private void RememberRunDisplay(bool weekly)
    {
        StopAfterNextOutcome = false;
        completionCommandPending = false;
        displayedRunSettings = runSettings;
        displayedRunCharacter = owner;
        displayedRunLimit = weekly ? EffectiveRunCount : Settings.RunCount;
        displayedWeekly = weekly;
        hasRunDisplay = true;
    }

    public int RouteStep => route?.Step ?? 0;
    public int RouteStepCount => route?.StepCount ?? 0;
    public bool ResultObserved { get; set; }
    internal bool BoardRunCompleted { get; private set; }
    internal bool CanReinspectCurrent => !disposed && Svc.ClientState.IsLoggedIn && Svc.PlayerState.IsLoaded &&
        Svc.PlayerState.ContentId != 0;
    internal BeastmasterSettings Settings => runSettings ?? getSettings();
    internal BeastmasterCharacterData CharacterData => runData ?? getCharacterData();
    internal bool CanObserveCharacter => Svc.ClientState.IsLoggedIn && Svc.PlayerState.IsLoaded && Svc.PlayerState.ContentId != 0 &&
        (owner == 0 || owner == Svc.PlayerState.ContentId);
    private System.Collections.Generic.HashSet<uint>? preparedOwned;
    internal System.Collections.Generic.IReadOnlyCollection<uint>? PreparedOwned => preparedOwned;

    public BeastmasterService(Func<BeastmasterSettings> settings, Func<BeastmasterCharacterData> characterData,
        Action save, VNavmeshIPC navigation, LifestreamIPC travel, AdsIpcClient ads)
    {
        getSettings = settings; getCharacterData = characterData; this.save = save;
        this.navigation = navigation; this.travel = travel;
        this.ads = ads;
        BeastmasterRuntime.Service = this;
    }

    public static bool IsCrucibleTerritory(uint territory) => territory is >= 1339 and <= 1343;
    public static uint TerritoryForBoard(uint board) => 1338 + Math.Clamp(board, 1u, 5u);

    internal static int? EquippedItemLevel => Svc.PlayerState.IsLoaded && UIState.Instance() != null
        ? UIState.Instance()->CurrentItemLevel : null;

    internal bool TryGetEntryRequirements(out int minimumLevel, out int minimumItemLevel) =>
        TryGetEntryRequirements(Settings.Board, out minimumLevel, out minimumItemLevel);

    private static bool TryGetEntryRequirements(uint board, out int minimumLevel, out int minimumItemLevel)
    {
        minimumLevel = minimumItemLevel = 0;
        // The five captured boards use ContentFinderCondition rows 1088 through 1092.
        if (board is < 1 or > 5 || !Svc.Data.GetExcelSheet<ContentFinderCondition>().TryGetRow(1087 + board, out var duty) ||
            duty.TerritoryType.RowId != TerritoryForBoard(board)) return false;
        minimumLevel = duty.ClassJobLevelRequired;
        minimumItemLevel = duty.ItemLevelRequired;
        return true;
    }

    internal bool MeetsEntryRequirements(out string reason) => MeetsEntryRequirements(Settings.Board, out reason);

    private static bool MeetsEntryRequirements(uint board, out string reason)
    {
        if (!TryGetEntryRequirements(board, out var minimumLevel, out var minimumItemLevel))
        { reason = "Entry requirements unavailable for this board."; return false; }
        if (EquippedItemLevel is not { } equipped)
        { reason = "Equipped item level is unavailable."; return false; }
        var level = Svc.Objects.LocalPlayer?.Level ?? 0;
        if (level < minimumLevel || equipped < minimumItemLevel)
        {
            reason = $"Requires level {minimumLevel} and item level {minimumItemLevel}; current level {level}, item level {equipped}.";
            return false;
        }
        reason = string.Empty;
        return true;
    }

    public bool CanStart(out string reason) => CanStartConfigured(getSettings(), false, out reason);

    private static bool CanStartCombat(BeastmasterSettings settings, out string reason)
        => settings.CombatProvider == BeastmasterCombatProvider.Minerva
            ? BeastmasterMinervaCombat.CanStart(out reason)
            : BeastmasterCombat.CanStart(out reason);

    private bool TryCaptureCombatProvider(BeastmasterSettings settings, out string reason)
    {
        if (!combat.TryRelease(out reason)) return false;
        if (combatProvider != settings.CombatProvider)
        {
            combat = settings.CombatProvider == BeastmasterCombatProvider.Minerva
                ? new BeastmasterMinervaCombat() : new BeastmasterCombat();
            combatProvider = settings.CombatProvider;
        }
        return true;
    }

    public bool CanStartConfigured(BeastmasterSettings settings, bool weekly, out string reason)
    {
        if (CanReinspectCurrent && IsActive)
        { reason = string.Empty; return true; }
        if (weekly) return CanStartWeekly(settings, out reason);
        if (disposed) { reason = "Beastmaster service is disposed"; return false; }
        if (IsActive) { reason = "A Beastmaster run or its cleanup is already active"; return false; }
        var inside = IsCrucibleTerritory(Svc.ClientState.TerritoryType);
        var nativeEntry = QueueMatches(new BeastmasterEntryAttempt(Svc.PlayerState.ContentId, settings.Board)) ||
            CrucibleUi.TryPrompt(17787, out _, finalLineOnly: true);
        var registration = HasOpenRegistrationScreen;
        if (!(registration ? RegistrationInteractionReady : inside || nativeEntry ? BeastmasterPlayer.IsValid : BeastmasterPlayer.IsReady) ||
            Svc.Objects.LocalPlayer is not { } player)
        { reason = "Wait until the current character is ready"; return false; }
        if (player.ClassJob.Value.Abbreviation.ExtractText() != "BST")
        { reason = "Crucible requires Beastmaster"; return false; }
        if (!inside && !nativeEntry && !NativeQueueIsIdle())
        { reason = "An existing duty queue must settle before starting Beastmaster."; return false; }
        if (!inside && settings.Board is < 1 or > 5) { reason = "Select one of the five Crucible boards"; return false; }
        if (settings.Goal == BeastmasterRunGoal.TrainOwned && settings.TeamMode != CrucibleTeamMode.Leveling)
        { reason = "Training all owned beasts requires a training team method."; return false; }
        if (!inside && !MeetsEntryRequirements(settings.Board, out reason)) return false;
        if (!inside && !nativeEntry && !RunContextReady(out reason)) return false;
        if (!Plugin.CommandManager.Commands.ContainsKey("/vnav")) { reason = "vnavmesh is required for board navigation"; return false; }
        if (Svc.Party.Length > 1) { reason = "Leave the party before entering the Crucible"; return false; }
        if (!CanStartCombat(settings, out reason)) return false;
        reason = string.Empty;
        return true;
    }

    public bool Start() => StartConfigured(getSettings(), false);

    public bool StartConfigured(BeastmasterSettings settings, bool weekly)
    {
        if (CanReinspectCurrent && IsActive) return RestartFromCurrent(settings, manual: true, weekly: weekly);
        if (weekly) return StartWeeklyConfigured(settings);
        if (!CanStartConfigured(settings, false, out var reason)) { status = reason; return false; }
        if (!TryCaptureCombatProvider(settings, out reason)) { status = "Combat restoration pending: " + reason; return false; }
        runSettings = settings.Clone(); runData = getCharacterData(); owner = Svc.PlayerState.ContentId;
        operatorStopped = false; recoveryAt = default;
        preparedOwned = null;
        if (IsCrucibleTerritory(Svc.ClientState.TerritoryType))
        {
            runSettings.Board = Svc.ClientState.TerritoryType - 1338u;
        }
        runData.EnsureFamiliars(CrucibleTeam.FamiliarSheetDatas.Keys);
        confirmingTrainingCompletion = false;
        RequestFastRankScan();
        CurrentRun = 1; IsPaused = false; pausedAt = default; ResultObserved = false; resultSeen = false; BoardRunCompleted = false;
        RememberRunDisplay(weekly: false);
        boardStep = 0; waitingForRevival = false; menus.Reset();
        var observedEntry = new BeastmasterEntryAttempt(owner, Settings.Board);
        if (!IsCrucibleTerritory(Svc.ClientState.TerritoryType) &&
            (QueueMatches(observedEntry) || CrucibleUi.TryPrompt(17787, out _, finalLineOnly: true)))
        {
            entryAttempt = observedEntry;
            Transition(RunState.WaitingDuty, "Rechecking the current Crucible entry.");
            if (!TryConfirmEntryPrompt() && !combat.TrySuspend(out var entryCombatReason)) Fail(entryCombatReason);
            return true;
        }
        if (IsCrucibleTerritory(Svc.ClientState.TerritoryType))
        {
            if (!combat.TrySuspend(out var combatReason)) { Fail(combatReason); return State == RunState.Recovering; }
            BeginRoute(resume: true);
        }
        else
        {
            if (!combat.TrySuspend(out var combatReason)) { Fail(combatReason); return State == RunState.Recovering; }
            PrepareOrdinaryRegistration(afterBoard: false);
        }
        return State != RunState.Failed;
    }

    private bool TryConfirmEntryPrompt()
    {
        if (operatorStopped || IsPaused || !CanObserveCharacter ||
            State is not (RunState.Travelling or RunState.OpeningBoard or RunState.SettingTeam or
                RunState.Confirming or RunState.WaitingDuty or RunState.Recovering or RunState.Repairing) ||
            IsCrucibleTerritory(Svc.ClientState.TerritoryType) ||
            !CrucibleUi.TryPrompt(17787, out var prompt, finalLineOnly: true)) return false;

        // A matching question is the current entry, including after a reload.
        // Handle it before idle/combat readiness or unrelated queue-pop windows.
        // ADS cleanup can close SelectYesno. Confirm this prompt first; the
        // repair observer releases its utility after the prompt disappears.
        entryAttempt ??= new BeastmasterEntryAttempt(owner, Settings.Board);
        if (entryAttempt.Character != owner || entryAttempt.StopRequested) return false;
        if (State != RunState.WaitingDuty)
            Transition(RunState.WaitingDuty, "Rechecking the current Crucible entry.");
        var yes = ((FFXIVClientStructs.FFXIV.Client.UI.AddonSelectYesno*)prompt)->YesButton;
        if (yes != null && yes->IsEnabled && entryAttempt.TryConfirmBoardPrompt(CrucibleUi.PromptText(prompt)))
            CrucibleUi.Screens.Prompt.Yes(prompt);
        return true;
    }

    private bool RestartFromCurrent(BeastmasterSettings settings, bool manual = false, bool weekly = false)
    {
        var changedCharacter = owner != 0 && owner != Svc.PlayerState.ContentId;
        var freshPlan = changedCharacter || manual && State is (RunState.Idle or RunState.Complete or RunState.Failed);
        // Start also captures a provider when it supersedes a standalone refresh with no run plan.
        // Same-plan recovery keeps the original provider even after saved settings change.
        if ((freshPlan || runSettings == null) && !TryCaptureCombatProvider(settings, out var combatReason))
        { status = "Combat restoration pending: " + combatReason; return false; }
        if (changedCharacter)
        {
            // Explicit Start selects this character's profile. Old entry and board
            // observations cannot authorize actions or completion on this character.
            CloseWeeklyChallengeLog();
            CancelOwnedRepair();
            StopNavigation();
            if (ownsTravel) travel.TryAbort();
            CancelFastRankScan();
            refreshCleanupPending = false; refreshTraversal = null;
            entryAttempt = null; route = null; menus.Reset();
            preparedOwned = null; expectedResultTeam.Clear();
            weeklyActive = false; weeklyAttempted.Clear(); weeklyRunReset = 0;
            weeklyCreditDeadline = default; weeklyEnteredBoard = 0;
            IsPaused = false; pausedAt = default;
        }
        else ResumePausedDeadlines();
        operatorStopped = false;
        if (manual) recoveryAt = default;
        IsPaused = false; pausedAt = default;
        owner = Svc.PlayerState.ContentId;
        runData = getCharacterData();
        runSettings ??= settings.Clone();
        if (freshPlan)
        {
            runSettings = settings.Clone(); CurrentRun = 1;
            BoardRunCompleted = ResultObserved = resultSeen = false;
            resultRanksObserved = resultRanksUnavailable = false;
            resultReadStarted = default;
            if (weekly)
            {
                RefreshWeeklyStatus(weeklyRegistered);
                runSettings.Goal = BeastmasterRunGoal.FixedRuns;
                runSettings.CompletionCommand = string.Empty;
                weeklyActive = true; weeklyRunReset = 0;
                weeklyAttempted.Clear(); CurrentRun = 0;
            }
            else weeklyActive = false;
            runData.EnsureFamiliars(CrucibleTeam.FamiliarSheetDatas.Keys);
            RequestFastRankScan();
            RememberRunDisplay(weekly);
        }
        if (CurrentRun < 1 && !weeklyActive) CurrentRun = 1;
        if (!changedCharacter && (IsRefreshing || refreshCleanupPending))
        {
            MarkRefreshIncomplete("Beast refresh cancelled; partial observations were retained.");
            // A new explicit Start supersedes restoring the cancelled browse view.
            refreshCleanupPending = false; refreshTraversal = null;
        }
        confirmingTrainingCompletion = false;
        registrationReadRecovery.Reset(); lastReadFailure = string.Empty;
        if (!BeastmasterPlayer.IsValid)
        { Fail("Wait until the current character is ready"); return true; }
        if (Svc.Objects.LocalPlayer?.ClassJob.Value.Abbreviation.ExtractText() != "BST")
        { Fail("Crucible requires Beastmaster"); return true; }
        if (!IsCrucibleTerritory(Svc.ClientState.TerritoryType) && Svc.Party.Length > 1)
        { Fail("Leave the party before entering the Crucible"); return true; }
        if (TryStopAfterBoardExit()) return true;
        if (!freshPlan && weeklyActive && (State == RunState.WeeklyVerifying ||
                State == RunState.Recovering && recoveryState == RunState.WeeklyVerifying))
        {
            Transition(RunState.WeeklyVerifying, "Waiting for fresh weekly challenge credit.");
            VerifyWeeklyCredit();
            return true;
        }
        if (resultSeen)
        {
            // Retrying the completed board's exit must retain its result and run
            // ordinal, including while the duty is still unloading.
            if (!IsCrucibleTerritory(Svc.ClientState.TerritoryType)) FinishRun();
            else if (SuspendCombat())
            {
                StopNavigation();
                Transition(RunState.Running, "Finishing the board");
            }
            return true;
        }
        if (IsCrucibleTerritory(Svc.ClientState.TerritoryType))
        {
            runSettings.Board = Svc.ClientState.TerritoryType - 1338u;
            if (weeklyActive && CurrentRun == 0)
            {
                CurrentRun = 1;
                weeklyAttempted.Add(runSettings.Board);
                RememberRunDisplay(weekly: true);
            }
            entryAttempt?.ObserveEntry(Svc.ClientState.TerritoryType);
            entryAttempt = null; // The actual entered board supersedes local entry bookkeeping.
            BeginRoute(resume: true, preserveMenus: !freshPlan);
            return true;
        }
        if (weeklyActive && CurrentRun == 0) { BeginWeeklyPreparation(); return true; }
        entryAttempt ??= new BeastmasterEntryAttempt(owner, Settings.Board);
        if (QueueMatches(entryAttempt) || CrucibleUi.TryPrompt(17787, out _, finalLineOnly: true))
        {
            entryAttempt.Resume();
            Transition(RunState.WaitingDuty, "Rechecking the current Crucible entry.");
            TryConfirmEntryPrompt();
            return true;
        }
        // With no live queue or prompt, replace stale local entry bookkeeping.
        if (NativeQueueIsIdle()) entryAttempt = null;
        if (EntryPending)
        {
            entryAttempt!.Resume();
            Transition(RunState.WaitingDuty, "Rechecking the current Crucible entry.");
            return true;
        }
        StopNavigation(); menus.Reset(); route = null;
        if (manual)
        {
            preparedOwned = null;
            RequestFastRankScan();
            PrepareRunOwnership(out _);
        }
        BeginRegistration();
        return true;
    }

    private void RecoverCurrentRun()
    {
        if (operatorStopped || IsPaused || DateTime.UtcNow < recoveryAt) return;
        recoveryAt = DateTime.UtcNow.AddSeconds(5);
        if (!CanObserveCharacter || !BeastmasterPlayer.IsValid) return;
        if (weeklyActive && CurrentRun == 0) { BeginWeeklyPreparation(); return; }
        if (recoveryState == RunState.WeeklyVerifying && weeklyActive)
        { Transition(RunState.WeeklyVerifying, "Waiting for fresh weekly challenge credit."); VerifyWeeklyCredit(); return; }
        if (resultSeen && !IsCrucibleTerritory(Svc.ClientState.TerritoryType))
        { FinishRun(); return; }
        RestartFromCurrent(Settings);
    }

    public bool Start(BeastmasterSettings settings)
    {
        return StartConfigured(settings, false);
    }

    private bool RunContextReady(out string reason)
    {
        if (!CanObserveCharacter || !(HasOpenRegistrationScreen ? RegistrationInteractionReady : BeastmasterPlayer.IsReady) ||
            Svc.Objects.LocalPlayer is not { IsDead: false })
        { reason = "Wait until the current character is ready"; return false; }
        if (Svc.Condition[ConditionFlag.BoundByDuty])
        { reason = "Leave the current duty before starting Beastmaster."; return false; }
        reason = string.Empty;
        return true;
    }

    private bool PrepareRunOwnership(out string reason)
    {
        if (!CanObserveCharacter || !CrucibleTeam.TryObserveOwnership(CharacterData, out var owned))
        { reason = "Native beast ownership data is unavailable; recorded observations were retained."; return false; }
        preparedOwned = owned;
        CrucibleTeam.UpdateCache();
        Save();
        reason = string.Empty;
        return true;
    }

    private bool TrainingRanksMeetTarget() =>
        BeastmasterSelection.RequiresTrainingCompletionRead(Settings, preparedOwned, CharacterData.Familiars);

    private void PrepareOrdinaryRegistration(bool afterBoard)
    {
        // Native registration can request ownership. Rank discovery uses the
        // original missing-only Lauda scan, never an automatic full refresh.
        if (preparedOwned == null) PrepareRunOwnership(out _);
        if (afterBoard) { CurrentRun++; resultSeen = false; ResultObserved = false; }
        BeginRegistration();
    }

    private static bool OutsideCompletionReady(ulong character) =>
        character != 0 && Svc.ClientState.IsLoggedIn && Svc.PlayerState.IsLoaded &&
        Svc.PlayerState.ContentId == character && BeastmasterPlayer.IsReady &&
        Svc.Objects.LocalPlayer is { IsDead: false } && !IsCrucibleTerritory(Svc.ClientState.TerritoryType) &&
        !Svc.Condition[ConditionFlag.BoundByDuty] && !Svc.Condition[ConditionFlag.BoundByDuty56] &&
        !CrucibleUi.IsVisible(CrucibleUi.ResultWindow) && NativeQueueIsIdle();

    internal bool CompletionCommandPending => completionCommandPending;

    internal bool CanStopAfterNextOutcome => HasRunDisplay && !operatorStopped &&
        State is not (RunState.Idle or RunState.Refreshing or RunState.Complete or RunState.Failed);

    internal void ToggleStopAfterNextOutcome()
    {
        if (StopAfterNextOutcome) StopAfterNextOutcome = false;
        else if (CanStopAfterNextOutcome) StopAfterNextOutcome = true;
    }

    private void StopAtRequestedOutcome(string reason)
    {
        Stop();
        status = reason;
    }

    private bool TryStopAfterBoardExit()
    {
        // An entered route proves this attempt reached a board. Deaths within
        // that board keep retrying; only an observed departure settles it.
        if (!StopAfterNextOutcome || route == null || resultSeen || !CanObserveCharacter ||
            !BeastmasterPlayer.IsValid || IsCrucibleTerritory(Svc.ClientState.TerritoryType) ||
            Svc.Condition[ConditionFlag.BoundByDuty] || Svc.Condition[ConditionFlag.BoundByDuty56] ||
            !NativeQueueIsIdle()) return false;
        StopAtRequestedOutcome("Stopped after leaving the current board.");
        return true;
    }

    internal bool CanFinalizeOrdinaryCompletion => !disposed && State == RunState.Complete &&
        !IsActive && !IsPaused && HasRunDisplay && !displayedWeekly && OutsideCompletionReady(displayedRunCharacter);

    internal void DiscardCompletionCommand(string? reason = null)
    {
        var pending = completionCommandPending;
        completionCommandPending = false;
        if (!pending || string.IsNullOrEmpty(reason)) return;
        status += "\n" + reason;
        Svc.Log.Warning("[Beastmaster] " + reason);
    }

    internal bool TryExecuteCompletionCommand()
    {
        if (!completionCommandPending) return true;
        if (!CanFinalizeOrdinaryCompletion) return false;
        var completedSettings = displayedRunSettings!;
        var command = (completedSettings.CompletionCommand ?? string.Empty).Trim();
        // Consume before dispatch: a command may unload plugins or terminate the client.
        completionCommandPending = false;
        if (command.Length == 0) return true;
        if (!command.StartsWith('/') || command.Contains('\r') || command.Contains('\n'))
        {
            status += "\nCompletion command must be a single slash command.";
            Svc.Log.Warning("[Beastmaster] Completion command must be a single slash command.");
            return true;
        }
        var sent = VERMAXION.Services.CommandHelper.TrySendCommand(command);
        // Plugin commands can synchronously Stop or Start another plan.
        if (State == RunState.Complete && ReferenceEquals(displayedRunSettings, completedSettings))
            status += sent ? "\nCompletion command sent." : "\nCompletion command could not be sent; it will not be repeated.";
        if (!sent) Svc.Log.Warning("[Beastmaster] Completion command could not be sent; it will not be repeated.");
        return true;
    }

    private bool CompleteOrdinary(string text)
    {
        if (!OutsideCompletionReady(owner)) return false;
        StopAfterNextOutcome = false;
        if (resultRanksUnavailable) text += "\nCompleted board; beast ranks and EXP remain unconfirmed.";
        completionCommandPending = !displayedWeekly && displayedRunSettings != null &&
            !string.IsNullOrWhiteSpace(displayedRunSettings.CompletionCommand);
        operatorStopped = true;
        Transition(RunState.Complete, text);
        preparedOwned = null;
        if (combat.TryRelease(out var reason)) { owner = 0; runData = null; runSettings = null; }
        else status = text + " Combat restoration pending: " + reason;
        return true;
    }

    private void BeginRegistration()
    {
        if (TryPrepareRegistrationRepair()) return;
        BeginRegistrationAfterRepair();
    }

    private void BeginRegistrationAfterRepair()
    {
        if (!PrepareEntryObservation(out var entryReason)) { Fail(entryReason); return; }
        teamSetup.Start(Settings.TeamMode); boardStep = 0;
        ownershipNotebookRequested = false;
        registrationReadRecovery.Reset(); lastReadFailure = string.Empty;
        Transition(Svc.ClientState.TerritoryType == LaudaTerritory ? RunState.OpeningBoard : RunState.Travelling,
            "Travelling to Lauda");
    }

    public void Update()
    {
        if (disposed) return;
        // Missing identity is a temporary observation gap. A different loaded
        // character also waits for explicit Start; neither cancels the old plan.
        if (owner != 0 && (!Svc.ClientState.IsLoggedIn || !Svc.PlayerState.IsLoaded ||
                Svc.PlayerState.ContentId == 0 || Svc.PlayerState.ContentId != owner))
        { stationarySince = default; return; }
        if (State is not (RunState.Idle or RunState.Complete or RunState.Failed) && owner == 0)
        { stationarySince = default; return; }
        try
        {
            if (ShouldResumeStationaryRun(DateTime.UtcNow))
            {
                Svc.Log.Warning("[Crewcible] Stationary for 30 seconds outside combat; resuming the current board.");
                RestartFromCurrent(Settings);
                return;
            }
            if (owner != 0) runData = getCharacterData();
            if (!UpdateCleanup()) return;
            if (TryStopAfterBoardExit()) return;
            if (TryConfirmEntryPrompt()) return;
            if (State == RunState.Refreshing)
            {
                UpdateFullRefresh();
                return;
            }
            // ADS owns its accepted repair menus and travel. Observe it even
            // while paused/zoning, before normal readiness and entry movement.
            if (UpdateRegistrationRepair()) return;
            // Results own progression even if the native death state lingers.
            if (State == RunState.Running && !resultSeen && !CrucibleUi.IsOpen(CrucibleUi.ResultWindow) &&
                Svc.Objects.LocalPlayer is { IsDead: true } && !waitingForRevival)
            {
                StopNavigation(); route?.ResetAfterDeath(); menus.Reset(); waitingForRevival = true;
                Svc.Log.Information("[Beastmaster] Player died; resetting the current fight before revival.");
            }
            if (State is not (RunState.Idle or RunState.Complete or RunState.Failed) &&
                (State != RunState.Running || IsPaused || !BeastmasterPlayer.IsValid || !combat.IsRunning) &&
                !SuspendCombat()) return;
            if (IsPaused || !BeastmasterPlayer.IsValid) return;
            if (State == RunState.WeeklyPreparing) { UpdateWeeklyPreparation(); return; }
            if (State == RunState.Recovering) { RecoverCurrentRun(); return; }
            CrucibleTeam.UpdateCache();
            if (State is RunState.Idle or RunState.Complete or RunState.Failed) return;
            if (weeklyActive && !(State is RunState.WaitingDuty or RunState.Running or RunState.Leaving or RunState.WeeklyVerifying) && !ValidateWeeklyEntry(out var weeklyEntryReason))
            { Fail(weeklyEntryReason); return; }
            if (weeklyActive && !ValidateWeeklyFreshness(out var weeklyFreshReason))
            { Fail(weeklyFreshReason); return; }
            if (State is not (RunState.Running or RunState.Leaving or RunState.WeeklyVerifying) && DateTime.UtcNow - stateSince > TimeSpan.FromMinutes(5))
            { Fail("Crucible registration did not complete within five minutes"); return; }
            switch (State)
            {
                case RunState.Recovering:
                    RecoverCurrentRun();
                    break;
                case RunState.Travelling:
                    TravelToLauda();
                    break;
                case RunState.OpeningBoard:
                    if (!MeetsEntryRequirements(out var openingReason)) { Fail(openingReason); break; }
                    if (confirmingTrainingCompletion)
                    {
                        StopNavigation();
                        if (TryCloseTrainingRegistration() &&
                            CompleteOrdinary("All owned beasts have reached this board's training target."))
                            confirmingTrainingCompletion = false;
                        break;
                    }
                    if (HasOpenRegistrationScreen)
                    {
                        StopNavigation();
                        if (RegistrationInteractionReady && OpenBoard(Settings.Board))
                            Transition(RunState.SettingTeam, "Setting up the team");
                        break;
                    }
                    if (Svc.Objects.LocalPlayer is not { } local) break;
                    if (Vector3.Distance(local.Position, LaudaPosition) > 4)
                    { MoveTo(LaudaPosition); break; }
                    StopNavigation();
                    if (OpenBoard(Settings.Board)) Transition(RunState.SettingTeam, "Setting up the team");
                    break;
                case RunState.SettingTeam:
                    if (!MeetsEntryRequirements(out var teamReason)) { Fail(teamReason); break; }
                    if (preparedOwned == null && !PrepareRunOwnership(out var ownershipReason))
                    {
                        status = ownershipReason;
                        // Let the native collection UI request its ownership data.
                        // This is the existing team-picking notebook, not a rank scan.
                        if (!ownershipNotebookRequested && !CrucibleUi.IsVisible(CrucibleUi.BestiaryWindow) &&
                            CrucibleUi.TryReady(CrucibleUi.TeamWindow, out var ownershipParty))
                        {
                            ownershipNotebookRequested = true;
                            CrucibleUi.Screens.PetParty.OpenBestiary(ownershipParty);
                        }
                        break;
                    }
                    status = teamSetup.Status;
                    if (teamSetup.Update())
                    {
                        if (teamSetup.Error is { } error)
                        {
                            if (Settings.Goal == BeastmasterRunGoal.TrainOwned && TrainingRanksMeetTarget())
                            {
                                confirmingTrainingCompletion = true;
                                Transition(RunState.OpeningBoard, "All owned beasts have reached this board's training target.");
                            }
                            else Fail(error);
                        }
                        else Transition(RunState.Confirming, "Challenging the board");
                    }
                    break;
                case RunState.Confirming:
                    if (!MeetsEntryRequirements(out var entryReason)) { Fail(entryReason); break; }
                    if (!CrucibleUi.TryReady(CrucibleUi.BoardLayout, out var layout)) break;
                    if (CrucibleUi.Team() is not { } entryTeam) break;
                    if (!BeastmasterSelection.CanEnterBoard(entryTeam.Select(familiar => familiar.Number)))
                    { Fail("Crucible entry requires at least three different familiars. The team changed before challenge confirmation."); break; }
                    if (!teamSetup.ValidateReadyTeam(out var plannedTeamReason)) { Fail(plannedTeamReason); break; }
                    if (CrucibleTeam.RememberTeam(entryTeam)) Save();
                    if (CrucibleUi.IsVisible(CrucibleUi.YesNo)) break;
                    if (!NativeQueueIsIdle()) { Fail("An existing duty queue must settle before starting Beastmaster."); break; }
                    if (QuestManager.IsQuestComplete(71045)) CrucibleUi.Screens.StageDetail.SetDegree(layout, Settings.Degree);
                    entryAttempt = new BeastmasterEntryAttempt(owner, Settings.Board);
                    challengedAt = DateTime.UtcNow;
                    CrucibleUi.Screens.StageDetail.Confirm(layout);
                    Transition(RunState.WaitingDuty, "Waiting to enter the Crucible");
                    break;
                case RunState.WaitingDuty:
                    if (IsCrucibleTerritory(Svc.ClientState.TerritoryType)) { BeginRoute(); break; }
                    if (CrucibleUi.TryReady("ContentsFinderConfirm", out var commence))
                    {
                        if (entryAttempt is not { } attempt || !QueueMatches(attempt, requirePop: true)) break;
                        if (weeklyActive)
                        {
                            if (!ValidateWeeklyEntry(out var commenceReason)) { Fail(commenceReason); break; }
                        }
                        var finder = ContentsFinder.Instance();
                        if (finder == null || finder->QueueInfo.QueueState == FFXIVClientStructs.FFXIV.Client.Enums.ContentsFinderQueueState.Accepted) break;
                        var button = ((FFXIVClientStructs.FFXIV.Client.UI.AddonContentsFinderConfirm*)commence)->CommenceButton;
                        if (button == null || !button->IsEnabled || !attempt.TryCommence(exactQueue: true)) break;
                        new ECommons.UIHelpers.AddonMasterImplementations.AddonMaster.ContentsFinderConfirm(commence).Commence();
                    }
                    break;
                case RunState.Running:
                    if (IsCrucibleTerritory(Svc.ClientState.TerritoryType) && Svc.ClientState.TerritoryType != TerritoryForBoard(Settings.Board))
                    { Fail("The entered Crucible board does not match the requested board."); break; }
                    if (!IsCrucibleTerritory(Svc.ClientState.TerritoryType))
                    { if (resultSeen) FinishRun(); else Fail("Left the Crucible before a result was observed"); break; }
                    if (CrucibleUi.IsOpen(CrucibleUi.ResultWindow))
                    { if (!resultSeen) resultReadStarted = DateTime.UtcNow; resultSeen = true; StopNavigation(); }
                    if (!resultSeen) route?.ObserveArena();
                    if (resultSeen || route?.AllowsCombat != true)
                    { if (!SuspendCombat()) break; }
                    if (!resultSeen && Svc.Objects.LocalPlayer is { IsDead: true })
                    { StopNavigation(); if (SuspendCombat()) status = "Waiting for revival"; break; }
                    if (!resultSeen && waitingForRevival)
                    {
                        if (!BeastmasterPlayer.IsCombatReady || !NavigationSettled())
                        { if (SuspendCombat()) status = "Waiting for revival"; break; }
                        waitingForRevival = false;
                        Svc.Log.Information("[Beastmaster] Revived; resuming the current fight with the selected combat provider.");
                    }
                    menus.Update();
                    if (State != RunState.Running) break;
                    if (ResultObserved) { FinishRun(); break; }
                    if (menus.LootRejectionPending) { status = menus.Status; break; }
                    if (!resultSeen) route?.Update();
                    if (route?.AllowsCombat != true) SuspendCombat();
                    break;
                case RunState.Leaving:
                    if (!IsCrucibleTerritory(Svc.ClientState.TerritoryType)) FinishRun();
                    else menus.Update();
                    break;
                case RunState.WeeklyVerifying:
                    VerifyWeeklyCredit();
                    break;
            }
        }
        catch (BeastmasterReadUnavailableException ex)
        {
            var window = CrucibleUi.Ready(ex.Window);
            var recovering = registrationReadRecovery.Ready(ex.Window, (nint)window, DateTime.UtcNow);
            status = recovering
                ? $"Rechecking {ex.Window}; waiting for a valid native control."
                : $"Waiting for {ex.Window} data.";
            if (lastReadFailure != ex.Message)
            {
                lastReadFailure = ex.Message;
                Svc.Log.Debug($"[Beastmaster] Native screen observation pending: {ex.Message}");
            }
            // The next update reacquires the window and fully validates its data.
            // Registration cannot safely guess or replay a team mutation.
        }
        catch (Exception ex) { Svc.Log.Error(ex, "[Beastmaster] Run update needs recovery"); Fail(ex.Message); }
    }

    private void TravelToLauda()
    {
        if (Svc.ClientState.TerritoryType == LaudaTerritory)
        {
            if (ownsTravel && (!travel.TryReadBusy(out var busy) || busy)) return;
            ownsTravel = false; Transition(RunState.OpeningBoard, "Approaching Lauda"); return;
        }
        // A void command dispatch does not prove travel started. Recheck idle
        // state after the existing cooldown until the destination is reached.
        if (DateTime.UtcNow < nextTravel || !BeastmasterPlayer.IsReady ||
            Player.IsAnimationLocked || Svc.Condition[ConditionFlag.Occupied] ||
            !travel.TryReadBusy(out var travelBusy) || travelBusy) return;
        var aetheryte = Svc.Data.GetExcelSheet<Aetheryte>().FirstOrDefault(a => a.Territory.RowId == LaudaTerritory && a.IsAetheryte);
        var destination = aetheryte.PlaceName.Value.Name.ExtractText();
        if (destination.Length == 0) { Fail("No travel destination for Lauda was found"); return; }
        ownsTravel = travel.ExecuteCommand(destination);
        nextTravel = DateTime.UtcNow.AddSeconds(5);
        if (!ownsTravel) Fail("Travel to Lauda could not start");
    }

    private void BeginRoute(bool resume = false, bool preserveMenus = false)
    {
        if (Svc.ClientState.TerritoryType != TerritoryForBoard(Settings.Board))
        { Fail("The entered Crucible board does not match the requested board."); return; }
        if (weeklyActive && !ValidateWeeklyEntry(out var weeklyRouteReason))
        { Fail(weeklyRouteReason); return; }
        if (weeklyActive) weeklyEnteredBoard = Settings.Board;
        StopNavigation();
        // Same-board continuation retains ownership of pending native questions
        // and attempted purchases; rebuilding the route must not orphan them.
        if (!preserveMenus) menus.Reset();
        resultSeen = false; ResultObserved = false; waitingForRevival = false;
        resultRanksObserved = resultRanksUnavailable = false; resultReadStarted = default;
        registrationReadRecovery.Reset(); lastReadFailure = string.Empty;
        expectedResultTeam = resume ? [] : [..CharacterData.Team];
        if (!combat.TrySuspend(out var combatReason)) { Fail(combatReason); return; }
        route = new BeastmasterRoute(this, Svc.ClientState.TerritoryType, resume);
        Transition(RunState.Running, resume ? "Resuming the current Crucible board" : "Following the Crucible board");
    }

    private void FinishRun()
    {
        if (!SuspendCombat() || !OutsideCompletionReady(owner)) return;
        if (!resultRanksObserved && !resultRanksUnavailable)
        {
            if (resultReadStarted == default || DateTime.UtcNow - resultReadStarted < BeastmasterReadRecovery.Delay) return;
            AcceptUnconfirmedBoardResults();
            if (!resultRanksUnavailable) return;
        }
        BoardRunCompleted = true;
        if (StopAfterNextOutcome)
        {
            StopAtRequestedOutcome("Stopped after a clear; left the Crucible.");
            return;
        }
        if (weeklyActive) { BeginWeeklyCreditVerification(); return; }
        Save(); StopNavigation(); menus.Reset(); route = null;
        if (Settings.Goal == BeastmasterRunGoal.TrainOwned && TrainingRanksMeetTarget() &&
            !IsCrucibleTerritory(Svc.ClientState.TerritoryType))
        {
            confirmingTrainingCompletion = true;
            Transition(RunState.OpeningBoard, "All owned beasts have reached this board's training target.");
            return;
        }
        if (CurrentRun < Math.Max(1, Settings.RunCount))
        {
            if (IsCrucibleTerritory(Svc.ClientState.TerritoryType))
            { Transition(RunState.Leaving, "Leaving the completed board"); return; }
            PrepareOrdinaryRegistration(afterBoard: true);
        }
        else
        {
            CompleteOrdinary(Settings.Goal == BeastmasterRunGoal.TrainOwned
                ? "Training allowance complete; unfinished beasts will be checked at the next trigger."
                : "Crucible run complete");
        }
    }

    internal void Save() { if (owner == 0 || Svc.PlayerState.ContentId == owner) save(); }

    internal bool ObserveBoardResults(System.Collections.Generic.IReadOnlyList<(uint Number, uint NewRank, uint NewXP)> rows)
    {
        if (!BeastmasterSelection.ResultRanksComplete(expectedResultTeam, rows.Select(row => (row.Number, (int)row.NewRank))) ||
            rows.Any(row => !CrucibleTeam.FamiliarSheetDatas.ContainsKey(row.Number))) return false;
        resultRanksObserved = true;
        resultRanksUnavailable = false;
        return true;
    }

    internal void AcceptUnconfirmedBoardResults()
    {
        if (resultRanksObserved || resultRanksUnavailable || !resultSeen || !CanObserveCharacter ||
            IsCrucibleTerritory(Svc.ClientState.TerritoryType) && Svc.ClientState.TerritoryType != TerritoryForBoard(Settings.Board)) return;
        var participants = expectedResultTeam.Count > 0 ? expectedResultTeam
            : CharacterData.Team.Count > 0 ? CharacterData.Team.ToHashSet()
            : CharacterData.Owned;
        BeastmasterReadBounds.MarkResultsUnconfirmed(CharacterData, participants);
        resultRanksUnavailable = true;
        status = "Completed board; beast ranks and EXP remain unconfirmed.";
        Save();
        Svc.Log.Warning("[Beastmaster] Completed board; beast ranks and EXP remain unconfirmed. A successful refresh is required to confirm training completion.");
    }

    internal bool MoveTo(Vector3 position)
    {
        if (!SuspendCombat()) return false;
        if (!ownsNavigation)
        {
            if (!navigation.TryGetNavReady(out var ready) || !ready) return false;
            ownsNavigation = navigation.PathfindAndMoveTo(position, allowRecoveryJump: false);
        }
        return ownsNavigation;
    }
    internal bool NavigationSettled() => navigation.TryGetPathIsRunning(out var running) && !running
        && navigation.TryGetPathfindInProgress(out var pending) && !pending;
    internal void StopNavigation() { if (ownsNavigation && navigation.Stop() && NavigationSettled()) ownsNavigation = false; }
    internal void SetStatus(string text) => status = text;
    internal void Recover(string reason) => Fail(reason);
    internal bool CombatReady(out string reason)
        => combat.TrySuspend(out reason);
    internal BeastmasterCombat.Target ReadCombatTarget() => combat.ReadTarget();
    internal bool StartCombat(bool allowAttacks = true)
    {
        if (IsPaused || waitingForRevival || !BeastmasterPlayer.IsCombatReady ||
            Svc.Objects.LocalPlayer is not { IsDead: false } ||
            Svc.ClientState.TerritoryType != TerritoryForBoard(Settings.Board))
        { SuspendCombat(); return false; }
        StopNavigation();
        if (!NavigationSettled()) { status = "Waiting for owned movement to stop"; return false; }
        if (combat.TryActivate(out var reason, allowAttacks)) return true;
        Fail(reason); return false;
    }
    internal bool SuspendCombat()
    {
        if (!combat.OwnsState) return true;
        if (combat.TrySuspend(out var reason)) return true;
        Fail(reason); return false;
    }
    private bool ShouldResumeStationaryRun(DateTime now)
    {
        if (State != RunState.Running || operatorStopped || IsPaused || owner == 0 || !CanObserveCharacter ||
            !BeastmasterPlayer.IsValid || Svc.Objects.LocalPlayer is not { IsDead: false } player ||
            !IsCrucibleTerritory(Svc.ClientState.TerritoryType) ||
            Svc.ClientState.TerritoryType != TerritoryForBoard(Settings.Board) ||
            !Svc.Condition[ConditionFlag.BoundByDuty] || Svc.Condition[ConditionFlag.InCombat] ||
            resultSeen || ResultObserved || CrucibleUi.IsOpen(CrucibleUi.ResultWindow))
        {
            stationarySince = default;
            return false;
        }
        if (stationarySince == default || player.Position != stationaryPosition)
        {
            stationaryPosition = player.Position;
            stationarySince = now;
            return false;
        }
        if (now - stationarySince < TimeSpan.FromSeconds(30)) return false;
        stationarySince = now;
        return true;
    }

    private void Transition(RunState state, string text)
    {
        stationarySince = default;
        State = state; status = text; stateSince = DateTime.UtcNow;
    }

    public void Pause()
    {
        if (!IsActive || IsPaused || State is RunState.Idle or RunState.Complete or RunState.Failed) return;
        stationarySince = default;
        IsPaused = true; pausedAt = DateTime.UtcNow; StopNavigation();
        if (ownsTravel) travel.TryAbort();
        route?.Pause();
        SuspendCombat();
    }
    private void ResumePausedDeadlines()
    {
        if (pausedAt != default)
        {
            var pausedFor = DateTime.UtcNow - pausedAt;
            stateSince += pausedFor;
            if (challengedAt != default) challengedAt += pausedFor;
            if (weeklyCreditDeadline != default) weeklyCreditDeadline += pausedFor;
            if (resultReadStarted != default) resultReadStarted += pausedFor;
            if (recoveryAt != default) recoveryAt += pausedFor;
            registrationReadRecovery.ResumeAfterPause(pausedFor);
            menus.ResumeAfterPause(pausedFor);
            teamSetup.ResumeAfterPause(pausedFor);
            ResumeRefreshAfterPause(pausedFor);
        }
    }
    public void Stop()
    {
        StopAfterNextOutcome = false;
        DiscardCompletionCommand();
        operatorStopped = true; recoveryAt = default;
        IsPaused = false; pausedAt = default;
        entryAttempt?.RequestStop();
        try { StopCore(); }
        catch (Exception ex)
        {
            // Stop remains callable even when a native observation fails. Keep
            // unresolved ownership for subsequent cleanup and an explicit Start.
            menus.Reset(); route = null;
            StopNavigation();
            if (ownsTravel) travel.TryAbort();
            combat.TryRelease(out _);
            Transition(RunState.Idle, $"Stopped; native cleanup remains unconfirmed: {ex.Message}");
            Svc.Log.Error(ex, "[Beastmaster] Stop will recheck native cleanup");
            UpdateCleanup();
        }
    }

    private void StopCore()
    {
        operatorStopped = true; recoveryAt = default;
        CloseWeeklyChallengeLog();
        CancelOwnedRepair();
        if (CanReinspectCurrent && owner != 0 && owner == Svc.PlayerState.ContentId) runData = getCharacterData();
        if (CanObserveCharacter && BeastmasterPlayer.IsValid && !IsCrucibleTerritory(Svc.ClientState.TerritoryType) && entryAttempt == null)
        {
            var observed = new BeastmasterEntryAttempt(Svc.PlayerState.ContentId, Settings.Board);
            if (QueueMatches(observed) || CrucibleUi.TryPrompt(17787, out _, finalLineOnly: true))
            {
                entryAttempt = observed;
                owner = Svc.PlayerState.ContentId; runData ??= getCharacterData(); runSettings ??= getSettings().Clone();
            }
        }
        confirmingTrainingCompletion = false;
        CancelFastRankScan();
        registrationReadRecovery.Reset(); lastReadFailure = string.Empty;
        CancelFullRefresh();
        entryAttempt?.RequestStop();
        StopNavigation(); if (ownsTravel) travel.TryAbort();
        weeklyActive = false; weeklyAttempted.Clear(); weeklyRunReset = 0; weeklyCreditDeadline = default;
        IsPaused = false; pausedAt = default; waitingForRevival = false; menus.Reset(); route = null;
        var released = combat.TryRelease(out var combatReason);
        preparedOwned = null;
        if (!EntryPending && !combat.OwnsState && !refreshCleanupPending && !repairRequestAccepted) { owner = 0; runData = null; runSettings = null; }
        Transition(RunState.Idle, released ? "Stopped" : "Stopped; combat restoration pending: " + combatReason); UpdateCleanup();
    }
    private bool UpdateCleanup()
    {
        try
        {
            UpdateRefreshCleanup();
            ReconcileEntry();
            if (operatorStopped && repairRequestAccepted) ObserveStoppedRepair();
            bool cleaning = IsPaused || State is RunState.Idle or RunState.Failed or RunState.Complete;
            if (ownsNavigation && cleaning && NavigationSettled()) ownsNavigation = false;
            if (ownsTravel && cleaning && travel.TryReadBusy(out var busy) && !busy) ownsTravel = false;
            if (!cleaning || DateTime.UtcNow < nextCleanupAttempt) return true;
            nextCleanupAttempt = DateTime.UtcNow.AddMilliseconds(500);
            if (ownsNavigation) StopNavigation();
            if (ownsTravel) travel.TryAbort();
            if (!confirmingTrainingCompletion && !IsPaused && State is RunState.Idle or RunState.Failed or RunState.Complete)
            {
                if (!combat.TryRelease(out var reason)) status = "Combat restoration pending: " + reason;
                if (!combat.OwnsState && !EntryPending && !refreshCleanupPending && !repairRequestAccepted) { owner = 0; runData = null; runSettings = null; }
            }
            lastCleanupFailure = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            status = $"Stopped; native cleanup remains unconfirmed: {ex.Message}";
            if (lastCleanupFailure != ex.Message)
            {
                lastCleanupFailure = ex.Message;
                Svc.Log.Error(ex, "[Beastmaster] Native cleanup remains pending; no successful restoration was inferred.");
            }
            return false;
        }
    }
    private void Fail(string reason)
    {
        if (operatorStopped || disposed) { status = reason; return; }
        if (State != RunState.Recovering)
        {
            recoveryState = State;
            recoveryAt = DateTime.UtcNow.AddSeconds(5);
        }
        if (recoveryAt == default || recoveryAt <= DateTime.UtcNow) recoveryAt = DateTime.UtcNow.AddSeconds(5);
        Transition(RunState.Recovering, $"Recovering in five seconds: {reason}");
        // Recovery remains scheduled even when a native cleanup call is the
        // operation that failed. An operator's Pause remains in force.
        try { StopNavigation(); }
        catch (Exception ex) { Svc.Log.Debug(ex, "[Beastmaster] Owned movement cleanup will be rechecked during recovery."); }
        try { combat.TrySuspend(out _); }
        catch (Exception ex) { Svc.Log.Debug(ex, "[Beastmaster] Combat suspension will be rechecked during recovery."); }
    }
    public void Dispose()
    {
        if (disposed) return;
        if (IsActive || IsPaused) Stop();
        CancelOwnedRepair();
        if (EntryPending) Svc.Log.Warning("[Beastmaster] Unloading with an unresolved native entry; no completion was inferred.");
        if (combat.OwnsState) Svc.Log.Warning("[Beastmaster] Unloading with combat-provider restoration unresolved; check the selected combat provider before another run.");
        entryWithdrawalHook?.Dispose(); entryWithdrawalHook = null;
        if (entryPromptListener) Svc.AddonLifecycle.UnregisterListener(AddonEvent.PreReceiveEvent, CrucibleUi.YesNo, ObserveEntryPromptEvent);
        if (entryPromptListener) Svc.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, CrucibleUi.YesNo, ObserveEntryPromptSetup);
        InstanceContentCrucible.Dispose(); disposed = true;
    }
}
