using System;
using System.Linq;
using Dalamud.Game.Command;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.System.Input;
using VERMAXION.IPC;
using VERMAXION.Models;

namespace VERMAXION.Services;

/// <summary>
/// Chocobo Racing queue service.
/// Uses the older ContentsFinder row-selection path for the normal fully-unlocked case.
/// Target progression selects the observed Sagolii Road entry and verifies its native identity.
/// This mirrors the VerminionService structure for consistency.
/// </summary>
public class ChocoboRaceService : IDisposable
{
    private readonly ICommandManager commandManager;
    private readonly ICondition condition;
    private readonly IPluginLog log;
    private readonly ConfigManager configManager;
    private readonly ChokeAboIpcClient chokeAboIpcClient;
    private readonly Func<string?> progressionStartBlock;

    // Open the Gold Saucer duty pane through the same CFC anchor used in the older working path,
    // then select Chocobo Racing by row for characters that have the full set unlocked.
    private const uint ChocoboRaceAnchorCfcId = 576;
    private const uint InitialRaceCfcId = 497;
    private const uint SagoliiRouletteId = 18;
    private const int ChocoboRaceSelectionIndex = 10;
    private const byte MaxRaceChocoboRank = 50;

    private bool isActive = false;
    private ChocoboState state = ChocoboState.Idle;
    private DateTime stateEnteredAt = DateTime.MinValue;
    private int currentAttempt = 0;
    private int maxAttempts = 5;
    private bool joinAttempted = false;
    private bool dutySelected = false;
    private bool nativeRacingCategorySelected;
    private int dutySelectionAttempts = 0;
    private DateTime lastJoinRetry = DateTime.MinValue;
    private uint returnHomeOriginTerritory;
    private string rankGateCheckReason = string.Empty;
    private bool targetCycleEnabledForBatch;
    private bool targetReadyForBatch;
    private DateTime nextTargetStatusPollAt = DateTime.MinValue;
    private string targetCycleStatusReason = string.Empty;
    private DateTime allowanceSaveUtc;
    private ulong progressionContentId;
    private string progressionAccount = string.Empty;
    private string progressionCharacter = string.Empty;
    private bool queueCancellationRequested;
    private bool allowanceDirty;
    private bool resumeTargetCycle;
    private bool waitingForChokeAbo;
    private bool questInspectionCaptured;
    private bool tutorialInspectionCaptured;
    private bool raceResultInspectionCaptured;
    private int tutorialPromptIndex = -1;
    private DateTime tutorialPromptCheckUtc;
    private VirtualKey? tutorialHeldKey;
    private VirtualKey? tutorialNextKey;
    private DateTime tutorialKeyReleaseUtc;
    public DateTime NextContinuationUtc { get; private set; }

    public enum ChocoboState
    {
        Idle,
        WaitingForTargetCycle,
        UnlockingPrerequisite,
        CheckingRaceChocoboRank,
        OpeningGoldSaucerRankCheck,
        ReadingGoldSaucerRankCheck,
        ReturningHome,
        WaitingForHomeReady,
        OpeningDutyFinder,
        QueueingForDuty,
        WaitingForDutyPop,
        ClickingCommence,
        InDuty,
        WaitingForResult,
        DismissingResult,
        WaitingForPlayerAvailable,
        TestingGoldSaucerOpen,
        TestingGoldSaucerRank,
        Complete,
        Failed,
        Deferred,
    }

    public ChocoboState State => state;
    public int CurrentAttempt => currentAttempt;
    public bool IsActive => state != ChocoboState.Idle &&
                            state != ChocoboState.Complete &&
                            state != ChocoboState.Failed &&
                            state != ChocoboState.Deferred;
    public bool IsComplete => state == ChocoboState.Complete;
    public bool IsFailed => state == ChocoboState.Failed;
    public bool IsDeferred => state == ChocoboState.Deferred;
    public string StatusText => state switch
    {
        ChocoboState.Idle => "Idle",
        ChocoboState.WaitingForTargetCycle => $"Choke-abo target work: {targetCycleStatusReason}",
        ChocoboState.UnlockingPrerequisite => targetCycleStatusReason,
        ChocoboState.Deferred => $"Deferred: {targetCycleStatusReason}",
        ChocoboState.CheckingRaceChocoboRank => $"Checking rank before race {Math.Min(currentAttempt + 1, maxAttempts)}/{maxAttempts}",
        ChocoboState.OpeningGoldSaucerRankCheck => $"Opening GoldSaucerInfo rank fallback ({currentAttempt}/{maxAttempts})",
        ChocoboState.ReadingGoldSaucerRankCheck => $"Reading GoldSaucerInfo rank fallback ({currentAttempt}/{maxAttempts})",
        _ => $"{state} ({currentAttempt}/{maxAttempts})",
    };
    public string GoldSaucerRankTestStatus { get; private set; } = "Not tested yet.";

    public ChocoboRaceService(
        ICommandManager commandManager,
        IPluginLog log,
        ConfigManager configManager,
        ChokeAboIpcClient chokeAboIpcClient,
        Func<string?> progressionStartBlock)
    {
        this.commandManager = commandManager;
        this.log = log;
        this.condition = Plugin.Condition;
        this.configManager = configManager;
        this.chokeAboIpcClient = chokeAboIpcClient;
        this.progressionStartBlock = progressionStartBlock;
        log.Information("[ChocoboRace] Build marker chocobo-reload-recovery-20260925-1.");
    }

    public bool Start(bool resume = false)
    {
        if (IsActive) return false;
        resumeTargetCycle = resume;
        waitingForChokeAbo = false;
        NextContinuationUtc = DateTime.MinValue;
        // Get configured number of races from active character config
        var activeConfig = configManager.GetActiveConfig();
        maxAttempts = activeConfig?.ChocoboRacesPerDay ?? 5;
        currentAttempt = 0;
        joinAttempted = false;
        dutySelected = false;
        dutySelectionAttempts = 0;
        lastJoinRetry = DateTime.MinValue;
        returnHomeOriginTerritory = 0;
        targetCycleEnabledForBatch = false;
        targetReadyForBatch = false;
        nextTargetStatusPollAt = DateTime.MinValue;
        targetCycleStatusReason = string.Empty;
        isActive = true;

        var mode = activeConfig?.ChocoboAutomationMode ?? ChocoboAutomationMode.AlwaysRace;
        if (!Enum.IsDefined(mode))
            return Defer($"Saved Chocobo automation mode value {(int)mode} is invalid.");

        if (mode == ChocoboAutomationMode.AlwaysRace)
        {
            if (chokeAboIpcClient.ShouldBlockRacing())
                return Defer("Choke-abo reports breeding with no race chocobo ready.");

            BeginNextRace();
            return true;
        }

        if (activeConfig == null)
            return Defer("No active character configuration is available for Target Pedigree mode.");

        targetCycleEnabledForBatch = true;
        if (activeConfig.ChocoboProgressionPaused)
            return Defer("Chocobo progression is paused; use Resume.");
        progressionContentId = Plugin.PlayerState.ContentId;
        progressionAccount = configManager.CurrentAccountId;
        progressionCharacter = configManager.CurrentCharacterKey;
        queueCancellationRequested = false;
        if (activeConfig.ChocoboRacePending || IsRacingTerritory() && condition[ConditionFlag.BoundByDuty] ||
            IsQueued() && HasOwnedNativeQueue())
        {
            if (IsRacingTerritory() && condition[ConditionFlag.BoundByDuty])
            {
                ReconcileReloadAllowance(activeConfig);
                SetState(ChocoboState.InDuty);
                return true;
            }
            if (IsQueued() && HasOwnedNativeQueue())
            {
                ReconcileReloadAllowance(activeConfig);
                SetState(ChocoboState.WaitingForDutyPop);
                return true;
            }
            if (IsQueued() || condition[ConditionFlag.BoundByDuty] || !GameHelpers.IsPlayerAvailable())
                return Defer("Waiting for the previous race or queue to reconcile after reload.");
            ChocoboDailyAllowance.Sample(activeConfig, DateTime.UtcNow, false, afterReload: true);
            configManager.SaveCurrentAccount();
        }
        if (IsQueued() || condition[ConditionFlag.BoundByDuty]) return Defer("Existing duty or queue does not belong to this progression.");
        TrackAllowance(forceSave: true);
        if (BeginPrerequisiteIfNeeded()) return IsActive;
        return HandleTargetCycleResult(
            RequestTargetCycle(activeConfig));
    }

    private void BeginNextRace()
    {
        if (targetCycleEnabledForBatch && IsRacingTerritory() && condition[ConditionFlag.BoundByDuty])
        {
            SetState(ChocoboState.InDuty);
            return;
        }
        if (targetCycleEnabledForBatch && ChocoboDailyAllowance.Remaining(configManager.GetActiveConfig(), DateTime.UtcNow) <= 0)
        {
            if (IsQueued())
            {
                SetState(ChocoboState.WaitingForDutyPop);
                if (!CancelOwnedQueue()) return;
            }
            NextContinuationUtc = ChocoboDailyAllowance.ResetAt(DateTime.UtcNow).AddDays(1);
            Defer("Daily racing allowance exhausted; breeding remains eligible. Racing resets at 09:00 UTC.");
            return;
        }
        if (targetCycleEnabledForBatch && IsQueued() && HasOwnedNativeQueue())
        {
            log.Information("[ChocoboRace] Taking ownership of the registrar's verified racing queue.");
            SetState(ChocoboState.WaitingForDutyPop);
            return;
        }
        if (IsRankGateEnabled())
        {
            BeginRankGateCheckForNextRace();
            return;
        }

        log.Information("[ChocoboRace] Using VERMAXION observable one-race loop");
        ContinueToNextRaceQueue();
    }

    public void RunTask()
    {
        log.Information("[VERMAXION] Manual Chocobo Racing triggered");
        if (configManager.GetActiveConfig().ChocoboAutomationMode == ChocoboAutomationMode.TargetPedigree)
        {
            ResumeProgression();
            return;
        }
        if (!Start())
            Plugin.ChatGui.Print($"[Vermaxion] Chocobo Racing deferred: {targetCycleStatusReason}");
    }

    public void RequestGoldSaucerRankTest()
    {
        if (IsActive)
        {
            GoldSaucerRankTestStatus = $"Busy: {state}.";
            Plugin.ChatGui.Print($"[Vermaxion] Chocobo rank test busy: {state}.");
            return;
        }

        currentAttempt = 0;
        maxAttempts = 1;
        joinAttempted = false;
        dutySelected = false;
        dutySelectionAttempts = 0;
        GoldSaucerRankTestStatus = "Opening GoldSaucerInfo for rank test...";
        log.Information("[ChocoboRankTest] Opening GoldSaucerInfo with /goldsaucer");
        CommandHelper.SendCommand("/goldsaucer");
        SetState(ChocoboState.TestingGoldSaucerOpen);
    }

    public void Reset()
    {
        waitingForChokeAbo = false;
        ReleaseTutorialKey();
        StopOwnedUnlockQuest();
        TrackAllowance(forceSave: true);
        if (targetCycleEnabledForBatch && !IsRacingTerritory()) CancelOwnedQueue();
        GameHelpers.KeyUp(VirtualKey.W);
        SetState(ChocoboState.Idle);
        isActive = false;
        stateEnteredAt = DateTime.MinValue;
        currentAttempt = 0;
        joinAttempted = false;
        dutySelected = false;
        dutySelectionAttempts = 0;
        lastJoinRetry = DateTime.MinValue;
        returnHomeOriginTerritory = 0;
        rankGateCheckReason = string.Empty;
        targetCycleEnabledForBatch = false;
        targetReadyForBatch = false;
        nextTargetStatusPollAt = DateTime.MinValue;
        targetCycleStatusReason = string.Empty;
    }

    public void Dispose()
    {
        ReleaseTutorialKey();
        GameHelpers.KeyUp(VirtualKey.W);
        TrackAllowance(forceSave: true);
        StopOwnedUnlockQuest();
    }

    public void ResumeProgression()
    {
        if (IsActive) return;
        var config = configManager.GetActiveConfig();
        if (config.ChocoboAutomationMode != ChocoboAutomationMode.TargetPedigree)
        { Defer("Select Target Pedigree in Chocobo settings before resuming progression."); return; }
        if (config.ChocoboBreedingGoal != ChocoboBreedingGoal.ReachPedigree)
        { Defer("Offspring production is not available in this build; the saved goal remains paused."); return; }
        if (progressionStartBlock() is { } reason) { Defer(reason); return; }
        if ((IsQueued() || condition[ConditionFlag.BoundByDuty]) && !CanReconcileRacingActivity())
        { Defer("Wait for the current duty or queue to settle before manually resuming breeding."); return; }
        config.ChocoboProgressionPaused = false;
        configManager.SaveCurrentAccount();
        Start(resume: true);
    }

    public void PauseProgression()
    {
        var config = configManager.GetActiveConfig();
        config.ChocoboProgressionPaused = true;
        configManager.SaveCurrentAccount();
        chokeAboIpcClient.PauseTargetCycle(Plugin.PlayerState.ContentId);
        Reset();
    }

    private bool IsRankGateEnabled()
    {
        var activeConfig = configManager.GetActiveConfig();
        return activeConfig?.SkipChocoboRacingAtRank50 == true;
    }

    internal static string? QuestIpcPrefix => Plugin.PluginInterface.InstalledPlugins
        .Where(plugin => plugin.IsLoaded && plugin.InternalName is "WigglyQuest" or "Questionable")
        .OrderByDescending(plugin => plugin.InternalName == "WigglyQuest")
        .Select(plugin => plugin.InternalName).FirstOrDefault();

    internal bool OwnsCurrentUnlockQuest()
    {
        var questId = configManager.GetActiveConfig().ChocoboUnlockQuestId;
        var prefix = QuestIpcPrefix;
        if (questId == 0 || prefix == null) return false;
        try
        {
            return Plugin.PluginInterface.GetIpcSubscriber<string?>($"{prefix}.GetCurrentQuestId").InvokeFunc() == questId.ToString();
        }
        catch { return false; }
    }

    private bool BeginPrerequisiteIfNeeded()
    {
        var config = configManager.GetActiveConfig();
        ushort next = 0;
        foreach (var questId in new ushort[] { 434, 436, 565 })
            if (!QuestManager.IsQuestComplete(questId)) { next = questId; break; }
        if (config.ChocoboUnlockQuestId is 434 or 435 or 436 or 565 or 576 &&
            !QuestManager.IsQuestComplete(config.ChocoboUnlockQuestId)) next = config.ChocoboUnlockQuestId;
        if (next == 0 && !QuestManager.IsQuestComplete(576) &&
            TryReadLoadedRaceChocoboManagerRank(out var rank) && rank >= 40)
            next = 576;
        if (next == 0) return false;
        var quests = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Quest>();
        if (!quests.TryGetRow((uint)next + 65536, out var quest))
        { Defer("Required chocobo unlock quest data is unavailable."); return true; }
        var prefix = QuestIpcPrefix;
        if (prefix == null)
        { Defer($"{quest.Name} requires Questionable's existing single-quest route; Questionable is not loaded."); return true; }
        try
        {
            if (Plugin.PluginInterface.GetIpcSubscriber<bool>($"{prefix}.IsRunning").InvokeFunc())
            {
                if (config.ChocoboUnlockQuestId == next && OwnsCurrentUnlockQuest())
                {
                    targetCycleStatusReason = $"Completing {quest.Name} through Questionable.";
                    SetState(ChocoboState.UnlockingPrerequisite);
                }
                else Defer("Another Questionable quest owns the character.");
                return true;
            }
            if (Plugin.ObjectTable.LocalPlayer?.Level < quest.ClassJobLevel[0])
            { Defer($"{quest.Name} requires level {quest.ClassJobLevel[0]} on the current job."); return true; }
            if (Plugin.PluginInterface.GetIpcSubscriber<string, bool>($"{prefix}.IsQuestLocked").InvokeFunc(next.ToString()))
            {
                var missing = quest.PreviousQuest.Where(previous => previous.RowId != 0 &&
                    !QuestManager.IsQuestComplete((ushort)(previous.RowId & 0xFFFF))).ToArray();
                // World of Wonders is an existing Gold Saucer route; ordinary MSQ prerequisites stay outside this workflow.
                if (missing.Any(previous => previous.RowId == 65971))
                { next = 435; quest = quests.GetRow(65971); }
                else
                {
                    var names = string.Join(", ", missing.Select(previous => previous.Value.Name.ToString()));
                    Defer($"{quest.Name} is locked. Required level: {quest.ClassJobLevel[0]}. " +
                        (names.Length > 0 ? $"Unfinished prerequisite quests: {names}." : "The game's additional quest requirements are unmet."));
                    return true;
                }
            }
            config.ChocoboUnlockQuestId = next;
            configManager.SaveCurrentAccount();
            // This fork's resolver otherwise selects an in-progress class quest
            // after accepting the requested quest, ending its SingleQuest run.
            // Insert only our quest and remove only an entry we inserted.
            if (prefix == "WigglyQuest" && !Plugin.PluginInterface
                .GetIpcSubscriber<string, bool>("WigglyQuest.IsQuestInPriority").InvokeFunc(next.ToString()))
            {
                config.ChocoboUnlockPriorityInserted = true;
                configManager.SaveCurrentAccount();
                if (!Plugin.PluginInterface.GetIpcSubscriber<int, string, bool>("WigglyQuest.InsertQuestPriority")
                    .InvokeFunc(0, next.ToString()))
                { Defer($"Could not retain {quest.Name} as the selected single quest."); return true; }
            }
            if (!Plugin.PluginInterface.GetIpcSubscriber<string, bool>($"{prefix}.StartSingleQuest").InvokeFunc(next.ToString()))
            { Defer($"Questionable could not start the existing route for {quest.Name}."); return true; }
            targetCycleStatusReason = $"Completing {quest.Name} through Questionable; racing allowance is unchanged.";
            log.Information($"[ChocoboRace] Started owned single quest {next}: {quest.Name}");
            SetState(ChocoboState.UnlockingPrerequisite);
            nextTargetStatusPollAt = DateTime.UtcNow.AddSeconds(2);
        }
        catch (Exception ex) { Defer($"Chocobo quest route unavailable: {ex.Message}"); }
        return true;
    }

    private void UpdateUnlockQuest()
    {
        var config = configManager.GetActiveConfig();
        var questId = config.ChocoboUnlockQuestId;
        if (questId == 0) { Defer("Unlock quest ownership is missing."); return; }
        if (QuestManager.IsQuestComplete(questId))
        {
            StopOwnedUnlockQuest();
            config.ChocoboUnlockQuestId = 0;
            configManager.SaveCurrentAccount();
            log.Information($"[ChocoboRace] Verified unlock quest {questId} complete from game state.");
            commandManager.ProcessCommand("/chokeabo inspect");
            if (!BeginPrerequisiteIfNeeded())
                HandleTargetCycleResult(RequestTargetCycle(config));
            return;
        }
        try
        {
            if (QuestIpcPrefix is not { } prefix || !OwnsCurrentUnlockQuest() ||
                !Plugin.PluginInterface.GetIpcSubscriber<bool>($"{prefix}.IsRunning").InvokeFunc())
            {
                config.ChocoboProgressionPaused = true;
                configManager.SaveCurrentAccount();
                Defer("The owned chocobo unlock quest stopped before completion; use Resume to continue its current step.");
            }
        }
        catch (Exception ex) { Defer($"Cannot reconcile the owned unlock quest: {ex.Message}"); }
    }

    private void StopOwnedUnlockQuest()
    {
        if (progressionContentId == 0 || Plugin.PlayerState.ContentId != progressionContentId) return;
        if (OwnsCurrentUnlockQuest())
            commandManager.ProcessCommand(QuestIpcPrefix == "WigglyQuest" ? "/wqst stop" : "/qst stop");
        var config = configManager.GetActiveConfig();
        if (config.ChocoboUnlockPriorityInserted && config.ChocoboUnlockQuestId != 0 && QuestIpcPrefix == "WigglyQuest")
        {
            try
            {
                var present = Plugin.PluginInterface.GetIpcSubscriber<string, bool>("WigglyQuest.IsQuestInPriority")
                    .InvokeFunc(config.ChocoboUnlockQuestId.ToString());
                if (present && !Plugin.PluginInterface.GetIpcSubscriber<string, bool>("WigglyQuest.RemovePriorityQuest")
                    .InvokeFunc(config.ChocoboUnlockQuestId.ToString())) return;
                config.ChocoboUnlockPriorityInserted = false;
                configManager.SaveCurrentAccount();
            }
            catch (Exception ex) { log.Warning($"[ChocoboRace] Owned quest priority cleanup remains pending: {ex.Message}"); }
        }
    }

    private unsafe bool TryReadLoadedRaceChocoboManagerRank(out int currentRank)
    {
        currentRank = 0;
        try
        {
            var manager = RaceChocoboManager.Instance();
            if (manager == null || manager->State != RaceChocoboManager.RaceChocoboState.Loaded)
            {
                log.Debug("[ChocoboRace] RaceChocoboManager is not loaded; using GoldSaucerInfo rank fallback");
                return false;
            }

            currentRank = manager->Rank;
            return true;
        }
        catch (Exception ex)
        {
            log.Warning($"[ChocoboRace] Failed to read RaceChocoboManager rank; using GoldSaucerInfo fallback: {ex.Message}");
            return false;
        }
    }

    private static bool TryParseRaceChocoboRank(string rawText, out int rank)
        => int.TryParse(rawText.Trim(), out rank);

    private void BeginRankGateCheckForNextRace()
    {
        rankGateCheckReason = currentAttempt == 0
            ? "before first race"
            : $"before race {currentAttempt + 1}/{maxAttempts}";
        log.Information($"[ChocoboRace] Checking racing chocobo rank {rankGateCheckReason}");
        SetState(ChocoboState.CheckingRaceChocoboRank);
    }

    private void StartFirstManualRace()
    {
        currentAttempt = 0;
        SetState(ChocoboState.ReturningHome);
        log.Information($"[ChocoboRace] Preparing Chocobo Racing cycle (0/{maxAttempts}) with /li home");
    }

    private void ContinueAfterRankGateAllowsRace(int rank, string source)
    {
        log.Information($"[ChocoboRace] Racing chocobo rank {rank} from {source}; continuing {rankGateCheckReason}");
        ContinueToNextRaceQueue();
    }

    private void ContinueToNextRaceQueue()
    {
        if (currentAttempt == 0 && !targetCycleEnabledForBatch)
        {
            StartFirstManualRace();
            return;
        }

        log.Information($"[ChocoboRace] Starting race {currentAttempt + 1}/{maxAttempts}");
        SetState(ChocoboState.OpeningDutyFinder);
    }

    private void CompleteBecauseRaceChocoboIsMaxRank(int rank, string source)
    {
        log.Information($"[ChocoboRace] Racing chocobo rank {rank} from {source}; completing daily racing task without queueing more races");
        SetState(ChocoboState.Complete);
    }

    private void FailRankGate(string reason)
    {
        GameHelpers.TryCloseAddonByCallback("GoldSaucerInfo");
        log.Warning($"[ChocoboRace] Could not read racing chocobo rank {rankGateCheckReason}: {reason}. Rank-50 skip is enabled, so the daily racing task is failing instead of racing blind.");
        SetState(ChocoboState.Failed);
    }

    private void EvaluateRankGateResult(int rank, string source)
    {
        GameHelpers.TryCloseAddonByCallback("GoldSaucerInfo");

        if (rank >= MaxRaceChocoboRank)
        {
            CompleteBecauseRaceChocoboIsMaxRank(rank, source);
            return;
        }

        ContinueAfterRankGateAllowsRace(rank, source);
    }

    /// <summary>
    /// Open the Duty Finder to a specific duty using AgentContentsFinder.
    /// The manual Chocobo fallback then selects the Chocobo Racing row from ContentsFinder.
    /// </summary>
    /// <param name="contentFinderConditionId">ContentFinderCondition row ID</param>
    public static unsafe bool OpenDutyFinder(uint contentFinderConditionId)
    {
        try
        {
            var agent = AgentContentsFinder.Instance();
            if (agent == null)
            {
                Plugin.Log.Error("[DutyQueue] AgentContentsFinder is null");
                return false;
            }
            agent->OpenRegularDuty(contentFinderConditionId);
            Plugin.Log.Information($"[DutyQueue] Opened duty finder for CFC ID {contentFinderConditionId}");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[DutyQueue] Failed to open duty finder: {ex.Message}");
            return false;
        }
    }

    public void Update()
    {
        TrackAllowance();
        if (targetCycleEnabledForBatch && state == ChocoboState.Deferred && !chokeAboIpcClient.IsV3Available)
            waitingForChokeAbo = true;
        if (waitingForChokeAbo && chokeAboIpcClient.IsV3Available)
        {
            waitingForChokeAbo = false;
            NextContinuationUtc = DateTime.MinValue;
        }
        if (state == ChocoboState.Idle ||
            state == ChocoboState.Complete ||
            state == ChocoboState.Failed ||
            state == ChocoboState.Deferred)
            return;

        if (targetCycleEnabledForBatch && (Plugin.PlayerState.ContentId != progressionContentId || !Plugin.ClientState.IsLoggedIn))
        {
            Reset();
            return;
        }
        if (targetCycleEnabledForBatch && !IsRacingTerritory() &&
            state is ChocoboState.OpeningDutyFinder or ChocoboState.QueueingForDuty or ChocoboState.WaitingForDutyPop or ChocoboState.ClickingCommence &&
            ChocoboDailyAllowance.Remaining(configManager.GetActiveConfig(), DateTime.UtcNow) <= 0)
        {
            if (CancelOwnedQueue())
            {
                NextContinuationUtc = ChocoboDailyAllowance.ResetAt(DateTime.UtcNow).AddDays(1);
                Defer("Daily racing allowance exhausted; waiting until 09:00 UTC for another race.");
            }
            return;
        }

        var elapsed = (DateTime.UtcNow - stateEnteredAt).TotalSeconds;

        switch (state)
        {
            case ChocoboState.UnlockingPrerequisite:
                if (!questInspectionCaptured && elapsed >= 60)
                {
                    questInspectionCaptured = true;
                    commandManager.ProcessCommand("/chokeabo inspect");
                }
                if (DateTime.UtcNow < nextTargetStatusPollAt || !GameHelpers.IsPlayerAvailable()) return;
                nextTargetStatusPollAt = DateTime.UtcNow.AddSeconds(2);
                UpdateUnlockQuest();
                return;
            case ChocoboState.WaitingForTargetCycle:
                if (DateTime.UtcNow < nextTargetStatusPollAt)
                    return;

                HandleTargetCycleResult(
                    RequestTargetCycle(configManager.GetActiveConfig()));
                return;

            case ChocoboState.CheckingRaceChocoboRank:
                if (elapsed < 0.1)
                    return;

                if (!IsRankGateEnabled())
                {
                    log.Information("[ChocoboRace] Rank-50 skip disabled while checking rank; continuing without rank gate");
                    ContinueToNextRaceQueue();
                    return;
                }

                if (TryReadLoadedRaceChocoboManagerRank(out var managerRank))
                {
                    EvaluateRankGateResult(managerRank, "RaceChocoboManager");
                    return;
                }

                log.Information("[ChocoboRace] Opening GoldSaucerInfo with /goldsaucer for rank fallback");
                CommandHelper.SendCommand("/goldsaucer");
                SetState(ChocoboState.OpeningGoldSaucerRankCheck);
                return;

            case ChocoboState.OpeningGoldSaucerRankCheck:
                if (elapsed < 0.5)
                    return;

                if (GameHelpers.IsAddonVisible("GoldSaucerInfo"))
                {
                    log.Information("[ChocoboRace] GoldSaucerInfo visible; reading node 21 for rank fallback");
                    SetState(ChocoboState.ReadingGoldSaucerRankCheck);
                    return;
                }

                if (elapsed > 10)
                {
                    FailRankGate("GoldSaucerInfo did not open within 10 seconds");
                    return;
                }
                return;

            case ChocoboState.ReadingGoldSaucerRankCheck:
                if (elapsed < 0.2)
                    return;

                if (!GameHelpers.TryGetAddonText("GoldSaucerInfo", 21u, out var rawRankText))
                {
                    FailRankGate("GoldSaucerInfo node 21 text was unavailable");
                    return;
                }

                if (!TryParseRaceChocoboRank(rawRankText, out var fallbackRank))
                {
                    FailRankGate($"GoldSaucerInfo node 21 text '{rawRankText}' was not numeric");
                    return;
                }

                log.Information($"[ChocoboRace] GoldSaucerInfo node 21 text='{rawRankText}', parsed rank={fallbackRank}");
                EvaluateRankGateResult(fallbackRank, "GoldSaucerInfo node 21");
                return;

            case ChocoboState.ReturningHome:
                if (elapsed < 0.5)
                    return;

                returnHomeOriginTerritory = Plugin.ClientState.TerritoryType;
                log.Information("[ChocoboRace] Returning home before opening ContentsFinder: /li home");
                commandManager.ProcessCommand("/li home");
                SetState(ChocoboState.WaitingForHomeReady);
                return;

            case ChocoboState.WaitingForHomeReady:
                if (elapsed < 3)
                    return;

                if (Plugin.ClientState.TerritoryType != returnHomeOriginTerritory && GameHelpers.IsPlayerAvailable())
                {
                    log.Information("[ChocoboRace] /li home completed, opening duty finder");
                    SetState(ChocoboState.OpeningDutyFinder);
                }
                else if (elapsed > 12 && GameHelpers.IsPlayerAvailable())
                {
                    log.Information("[ChocoboRace] /li home settled without a territory change, opening duty finder");
                    SetState(ChocoboState.OpeningDutyFinder);
                }
                else if (elapsed > 25)
                {
                    log.Warning("[ChocoboRace] Timed out waiting for /li home to settle, opening duty finder anyway");
                    SetState(ChocoboState.OpeningDutyFinder);
                }
                return;

            case ChocoboState.OpeningDutyFinder:
                if (elapsed < 1) return;
                log.Information($"[ChocoboRace] Starting Chocobo Racing queue (attempt {currentAttempt + 1}/{maxAttempts})");
                
                if (OpenDutyFinder(targetCycleEnabledForBatch ? InitialRaceCfcId : ChocoboRaceAnchorCfcId))
                {
                    joinAttempted = false;
                    dutySelected = false;
                    nativeRacingCategorySelected = false;
                    dutySelectionAttempts = 0;
                    lastJoinRetry = DateTime.MinValue;
                    SetState(ChocoboState.QueueingForDuty);
                }
                else
                {
                    log.Error("[ChocoboRace] Failed to open duty finder");
                    SetState(ChocoboState.Failed);
                }
                break;

            case ChocoboState.QueueingForDuty:
                // Restore the older ContentsFinder-driven path for the normal unlocked case:
                // clear selection, pick the Chocobo Racing row, then press Join.
                if (elapsed < 6) return;

                if (targetCycleEnabledForBatch)
                {
                    if (IsQueued() && HasOwnedNativeQueue())
                    {
                        SetState(ChocoboState.WaitingForDutyPop);
                        return;
                    }
                    if (joinAttempted)
                    {
                        if (elapsed > 30)
                        {
                            NextContinuationUtc = DateTime.MaxValue;
                            Defer("Sagolii Road admission was not confirmed by the native queue.");
                        }
                        return;
                    }
                    if (!nativeRacingCategorySelected && GameHelpers.TryClickNativeButton("ContentsFinder", null, 50))
                    {
                        nativeRacingCategorySelected = true;
                        lastJoinRetry = DateTime.UtcNow;
                        return;
                    }
                    if (DateTime.UtcNow - lastJoinRetry < TimeSpan.FromSeconds(1)) return;
                    if (nativeRacingCategorySelected && !dutySelected &&
                        GameHelpers.TrySelectNativeListEntry("ContentsFinder", "Sagolii Road"))
                    {
                        dutySelected = true;
                        lastJoinRetry = DateTime.UtcNow;
                        return;
                    }
                    if (dutySelected && !HasOnlySagoliiSelection() &&
                        GameHelpers.TryCheckNativeListRow("ContentsFinder", "Sagolii Road", 5))
                    {
                        lastJoinRetry = DateTime.UtcNow;
                        return;
                    }
                    if (dutySelected && HasOnlySagoliiSelection() && GameHelpers.TryClickNativeButton("ContentsFinder", "Join", 74))
                    {
                        joinAttempted = true;
                        lastJoinRetry = DateTime.UtcNow;
                        return;
                    }
                    commandManager.ProcessCommand("/chokeabo inspect ContentsFinder");
                    NextContinuationUtc = DateTime.MaxValue;
                    Defer("The observed Sagolii Road selection or Join control could not be verified.");
                    return;
                }
                
                if (GameHelpers.IsAddonVisible("ContentsFinder"))
                {
                    if (currentAttempt == 0 && elapsed < 6.5)
                    {
                        log.Information("[ChocoboRace] Clearing duty selection for first run");
                        GameHelpers.FireAddonCallback("ContentsFinder", true, 12, 1);
                        return;
                    }

                    if (!dutySelected)
                    {
                        log.Information($"[ChocoboRace] Selecting Chocobo Racing row {ChocoboRaceSelectionIndex} from ContentsFinder");
                        GameHelpers.FireAddonCallback("ContentsFinder", true, 3, ChocoboRaceSelectionIndex);
                        dutySelectionAttempts++;
                        dutySelected = true;

                        log.Information("[ChocoboRace] Clicking Join after Chocobo selection");
                        GameHelpers.FireAddonCallback("ContentsFinder", true, 12, 0);
                        joinAttempted = true;
                        lastJoinRetry = DateTime.UtcNow;
                        return;
                    }

                    if (!joinAttempted && elapsed > 8)
                    {
                        log.Information("[ChocoboRace] Duty selected, clicking Join");
                        GameHelpers.FireAddonCallback("ContentsFinder", true, 12, 0);
                        joinAttempted = true;
                        lastJoinRetry = DateTime.UtcNow;
                    }
                    else if (joinAttempted && (DateTime.UtcNow - lastJoinRetry).TotalSeconds >= 5)
                    {
                        log.Information($"[ChocoboRace] ContentsFinder still visible after {elapsed:F1}s, retrying Join");
                        GameHelpers.FireAddonCallback("ContentsFinder", true, 12, 0);
                        lastJoinRetry = DateTime.UtcNow;
                    }
                }

                if (joinAttempted &&
                    (condition[ConditionFlag.WaitingForDutyFinder] || condition[ConditionFlag.WaitingForDuty]))
                {
                    log.Information("[ChocoboRace] Duty queue registered, waiting for race pop");
                    SetState(ChocoboState.WaitingForDutyPop);
                }
                else if (joinAttempted && elapsed > 8 && !GameHelpers.IsAddonVisible("ContentsFinder"))
                {
                    log.Information("[ChocoboRace] ContentsFinder closed, waiting for duty pop");
                    SetState(ChocoboState.WaitingForDutyPop);
                }
                else if (elapsed > 30)
                {
                    log.Warning($"[ChocoboRace] Timeout waiting for queue registration after selection attempts={dutySelectionAttempts}, retrying");
                    SetState(ChocoboState.OpeningDutyFinder);
                }
                break;

            case ChocoboState.WaitingForDutyPop:
                // Check for ContentsFinderConfirm addon (duty pop)
                if (GameHelpers.IsAddonVisible("ContentsFinderConfirm"))
                {
                    log.Information("[ChocoboRace] Race pop! Clicking Commence");
                    SetState(ChocoboState.ClickingCommence);
                }
                // Also check if already in duty
                else if (condition[ConditionFlag.BoundByDuty])
                {
                    log.Information("[ChocoboRace] Already in duty");
                    SetState(ChocoboState.InDuty);
                }
                else if (elapsed > 120) // 2 min timeout
                {
                    log.Warning("[ChocoboRace] Duty queue timeout - retrying");
                    SetState(ChocoboState.OpeningDutyFinder);
                }
                break;

            case ChocoboState.ClickingCommence:
                if (elapsed < 1) return;
                if (targetCycleEnabledForBatch && !HasOwnedNativeQueue())
                {
                    NextContinuationUtc = DateTime.MaxValue;
                    Defer("The pending duty is not a verified racing queue.");
                    return;
                }
                if (GameHelpers.IsAddonVisible("ContentsFinderConfirm"))
                {
                    log.Information("[ChocoboRace] Clicking Commence on ContentsFinderConfirm");
                    ResetTutorialForAdmission();
                    // Fire commence callback - typically callback index 8 = Commence button
                    GameHelpers.FireAddonCallback("ContentsFinderConfirm", true, 8);
                    SetState(ChocoboState.InDuty);
                }
                else
                {
                    SetState(ChocoboState.WaitingForDutyPop);
                }
                break;

            case ChocoboState.InDuty:
                if (Plugin.ClientState.TerritoryType == 417)
                    UpdateTutorialInput();
                if (Plugin.ClientState.TerritoryType == 417 && !tutorialInspectionCaptured && elapsed >= 8 &&
                    !condition[ConditionFlag.BetweenAreas] && !condition[ConditionFlag.BetweenAreas51])
                {
                    tutorialInspectionCaptured = true;
                    commandManager.ProcessCommand("/chokeabo inspect");
                }
                // Press W during the race and wait for RaceChocoboResult addon to appear
                if (GameHelpers.IsAddonVisible("RaceChocoboResult"))
                {
                    if (targetCycleEnabledForBatch && !raceResultInspectionCaptured)
                    {
                        raceResultInspectionCaptured = true;
                        commandManager.ProcessCommand("/chokeabo inspect RaceChocoboResult");
                    }
                    log.Information("[ChocoboRace] Race ended, RaceChocoboResult addon visible");
                    SetState(ChocoboState.WaitingForResult);
                }
                else if (!condition[ConditionFlag.BoundByDuty] && elapsed > 10)
                {
                    // Duty ended without result screen
                    log.Information("[ChocoboRace] Duty ended");
                    SetState(ChocoboState.WaitingForPlayerAvailable);
                }
                else if (elapsed > 600) // 10 min timeout
                {
                    log.Warning("[ChocoboRace] Race timeout");
                    SetState(ChocoboState.Failed);
                }
                else if (Plugin.ClientState.TerritoryType != 417 && elapsed > 5 && elapsed < 7) // Hold W key after 5 seconds
                {
                    GameHelpers.KeyDown(VirtualKey.W);
                }
                break;

            case ChocoboState.WaitingForResult:
                if (elapsed < 1) return;
                // Release W key when race ends
                GameHelpers.KeyUp(VirtualKey.W);
                
                // Check both possible addon names and log which one we're using
                bool raceResult = GameHelpers.IsAddonVisible("RaceChocoboResult");
                bool chocoboResult = GameHelpers.IsAddonVisible("ChocoboResult");
                
                if (raceResult)
                {
                    if (targetCycleEnabledForBatch)
                    {
                        if (TryLeaveNativeRaceResult()) SetState(ChocoboState.DismissingResult);
                        else if (elapsed > 10)
                        {
                            NextContinuationUtc = DateTime.MaxValue;
                            Defer("The native racing result Leave button is unavailable.");
                        }
                        return;
                    }
                    log.Information("[ChocoboRace] Dismissing RaceChocoboResult screen");
                    GameHelpers.FireAddonCallback("RaceChocoboResult", true, 1);
                    SetState(ChocoboState.DismissingResult);
                }
                else if (chocoboResult)
                {
                    log.Information("[ChocoboRace] Dismissing ChocoboResult screen");
                    GameHelpers.FireAddonCallback("ChocoboResult", true, 1);
                    SetState(ChocoboState.DismissingResult);
                }
                else
                {
                    SetState(ChocoboState.WaitingForPlayerAvailable);
                }
                break;

            case ChocoboState.DismissingResult:
                if (elapsed < 1) return;
                bool raceResultCheck = GameHelpers.IsAddonVisible("RaceChocoboResult");
                bool chocoboResultCheck = GameHelpers.IsAddonVisible("ChocoboResult");
                if (targetCycleEnabledForBatch && raceResultCheck)
                {
                    if (elapsed > 10)
                    {
                        NextContinuationUtc = DateTime.MaxValue;
                        Defer("The native racing result Leave action has not closed its window.");
                    }
                    return;
                }
                
                if (raceResultCheck)
                {
                    // Try again to dismiss RaceChocoboResult
                    GameHelpers.FireAddonCallback("RaceChocoboResult", true, 1);
                }
                else if (chocoboResultCheck)
                {
                    // Try again to dismiss ChocoboResult
                    GameHelpers.FireAddonCallback("ChocoboResult", true, 1);
                }
                SetState(ChocoboState.WaitingForPlayerAvailable);
                break;

            case ChocoboState.WaitingForPlayerAvailable:
                // Wait until player is available for next race
                if (elapsed < 2) return;
                if (GameHelpers.IsPlayerAvailable())
                {
                    currentAttempt++;
                    log.Information($"[ChocoboRace] Race {currentAttempt}/{maxAttempts} complete");

                    if (targetCycleEnabledForBatch)
                    {
                        var activeConfig = configManager.GetActiveConfig();
                        ChocoboDailyAllowance.Sample(activeConfig, DateTime.UtcNow, false);
                        configManager.SaveCurrentAccount();
                        if (activeConfig == null || activeConfig.ChocoboAutomationMode != ChocoboAutomationMode.TargetPedigree)
                        {
                            Defer("Target Pedigree mode changed before the post-race Choke-abo handoff.");
                            return;
                        }

                        HandleTargetCycleResult(
                            RequestTargetCycle(activeConfig));
                    }
                    else if (currentAttempt >= maxAttempts)
                    {
                        log.Information($"[ChocoboRace] All {maxAttempts} races complete!");
                        SetState(ChocoboState.Complete);
                    }
                    else
                    {
                        BeginNextRace();
                    }
                }
                else if (elapsed > 30)
                {
                    log.Error("[ChocoboRace] Timed out waiting for verified player availability after race");
                    SetState(ChocoboState.Failed);
                }
                break;

            case ChocoboState.TestingGoldSaucerOpen:
                if (elapsed < 0.5)
                    return;

                if (GameHelpers.IsAddonVisible("GoldSaucerInfo"))
                {
                    log.Information("[ChocoboRankTest] GoldSaucerInfo visible; reading node 21");
                    SetState(ChocoboState.TestingGoldSaucerRank);
                    return;
                }

                if (elapsed > 10)
                {
                    GoldSaucerRankTestStatus = "Failed: GoldSaucerInfo did not open.";
                    log.Warning("[ChocoboRankTest] GoldSaucerInfo did not open within 10 seconds");
                    Plugin.ChatGui.Print("[Vermaxion] Chocobo rank test failed: GoldSaucerInfo did not open.");
                    SetState(ChocoboState.Failed);
                }
                break;

            case ChocoboState.TestingGoldSaucerRank:
                if (elapsed < 0.2)
                    return;

                if (!GameHelpers.TryGetAddonText("GoldSaucerInfo", 21u, out var rawText))
                {
                    GoldSaucerRankTestStatus = "Failed: GoldSaucerInfo node 21 unavailable.";
                    log.Warning("[ChocoboRankTest] GoldSaucerInfo node 21 text was unavailable");
                    Plugin.ChatGui.Print("[Vermaxion] Chocobo rank test failed: GoldSaucerInfo node 21 unavailable.");
                    SetState(ChocoboState.Failed);
                    return;
                }

                if (!TryParseRaceChocoboRank(rawText, out var rank))
                {
                    GoldSaucerRankTestStatus = $"Failed: could not parse node 21 text '{rawText}'.";
                    log.Warning($"[ChocoboRankTest] Could not parse GoldSaucerInfo node 21 text '{rawText}'");
                    Plugin.ChatGui.Print($"[Vermaxion] Chocobo rank test failed: node 21 text '{rawText}' was not a number.");
                    SetState(ChocoboState.Failed);
                    return;
                }

                var maxRank = rank >= MaxRaceChocoboRank;
                GoldSaucerRankTestStatus = $"GoldSaucerInfo node 21 rank={rank}. Rank 50 skip={(maxRank ? "YES" : "NO")}.";
                log.Information($"[ChocoboRankTest] GoldSaucerInfo node 21 text='{rawText}', parsed rank={rank}, rank50={maxRank}");
                Plugin.ChatGui.Print($"[Vermaxion] Chocobo rank test: node 21 rank={rank}; rank 50 skip={(maxRank ? "YES" : "NO")}.");
                SetState(ChocoboState.Complete);
                break;
        }
    }

    private void SetState(ChocoboState newState)
    {
        if (state == ChocoboState.InDuty && newState != ChocoboState.InDuty) GameHelpers.KeyUp(VirtualKey.W);
        if (newState != ChocoboState.InDuty) ReleaseTutorialKey();
        TrackAllowance(forceSave: true);
        log.Information($"[ChocoboRace] {state} -> {newState}");
        state = newState;
        if (newState == ChocoboState.UnlockingPrerequisite) questInspectionCaptured = false;
        stateEnteredAt = DateTime.UtcNow;
        isActive = newState != ChocoboState.Idle &&
                   newState != ChocoboState.Complete &&
                   newState != ChocoboState.Failed &&
                   newState != ChocoboState.Deferred;
    }

    private void ReleaseTutorialKey()
    {
        tutorialNextKey = null;
        if (tutorialHeldKey is not { } key) return;
        GameHelpers.KeyUp(key);
        tutorialHeldKey = null;
    }

    private unsafe void ResetTutorialForAdmission()
    {
        ReleaseTutorialKey();
        raceResultInspectionCaptured = false;
        tutorialInspectionCaptured = false;
        var chat = FFXIVClientStructs.FFXIV.Client.UI.Misc.RaptureLogModule.Instance();
        tutorialPromptIndex = chat == null ? -1 : checked((int)chat->GetCurrentLogIndex());
    }

    private static unsafe VirtualKey? ReadTutorialKey(InputId inputId)
    {
        var input = FFXIVClientStructs.FFXIV.Client.UI.UIInputData.Instance();
        var binding = input == null ? null : input->GetKeybind(inputId);
        if (binding == null) return null;
        foreach (var setting in binding->KeySettings)
            if ((byte)setting.Key != 0 && (byte)setting.KeyModifier == 0)
                return (VirtualKey)setting.Key;
        return null;
    }

    private unsafe void UpdateTutorialInput()
    {
        if (!targetCycleEnabledForBatch || Plugin.PlayerState.ContentId != progressionContentId ||
            !condition[ConditionFlag.BoundByDuty] || condition[ConditionFlag.BetweenAreas] || condition[ConditionFlag.BetweenAreas51]) return;
        var now = DateTime.UtcNow;
        if (now - stateEnteredAt < TimeSpan.FromSeconds(3)) return;
        if (tutorialHeldKey != null && now >= tutorialKeyReleaseUtc)
        {
            var next = tutorialNextKey;
            ReleaseTutorialKey();
            if (next is { } nextKey)
            {
                tutorialHeldKey = nextKey;
                tutorialKeyReleaseUtc = now.AddSeconds(2);
                log.Information($"[ChocoboRace] Tutorial steering continuation; input={nextKey}");
                GameHelpers.KeyDown(nextKey);
            }
        }
        if (now < tutorialPromptCheckUtc) return;
        tutorialPromptCheckUtc = now.AddSeconds(1);
        if (GameHelpers.IsAddonVisible("SelectString") &&
            GameHelpers.TryGetAddonText("SelectString", 2, out var prompt) && prompt == "Leave the training course?")
        {
            ReleaseTutorialKey();
            tutorialPromptIndex = -1;
            GameHelpers.TrySelectNativeListEntry("SelectString", "No.");
            return;
        }
        var chat = FFXIVClientStructs.FFXIV.Client.UI.Misc.RaptureLogModule.Instance();
        if (chat == null) return;
        var newest = checked((int)chat->GetCurrentLogIndex());
        for (var index = newest; index >= Math.Max(0, newest - 100) && index > tutorialPromptIndex; --index)
        {
            if (!chat->GetLogMessageDetail(index, out _, out var bytes, out var kind, out _, out _, out _) ||
                kind != (ushort)Dalamud.Game.Text.XivChatType.SystemMessage) continue;
            var message = Dalamud.Game.Text.SeStringHandling.SeString.Parse(bytes).TextValue;
            InputId? inputId = message.Contains("Move Back", StringComparison.Ordinal) ? InputId.MOVE_BACK :
                message.Contains("Move Forward", StringComparison.Ordinal) ? InputId.MOVE_FORE :
                message.Contains("Move Left", StringComparison.Ordinal) ? InputId.MOVE_LEFT :
                message.Contains("Move Right", StringComparison.Ordinal) ? InputId.MOVE_RIGHT :
                message.Contains("Jump key", StringComparison.Ordinal) ? InputId.JUMP : null;
            if (inputId == null) continue;
            var key = ReadTutorialKey(inputId.Value);
            if (key == null)
            {
                NextContinuationUtc = DateTime.MaxValue;
                Defer($"Tutorial {inputId} has no unmodified keyboard binding available.");
                return;
            }
            tutorialPromptIndex = index;
            ReleaseTutorialKey();
            GameHelpers.KeyUp(VirtualKey.W);
            tutorialHeldKey = key;
            tutorialNextKey = inputId == InputId.MOVE_LEFT && message.Contains("Move Right", StringComparison.Ordinal)
                ? ReadTutorialKey(InputId.MOVE_RIGHT) : null;
            tutorialKeyReleaseUtc = now.AddSeconds(inputId == InputId.MOVE_FORE ? 25 : 2);
            log.Information($"[ChocoboRace] Tutorial instruction {index}: {message}; binding={inputId}; input={key}");
            GameHelpers.KeyDown(key.Value);
            return;
        }
    }

    private ChokeAboTargetCycleCallResult RequestTargetCycle(CharacterConfig config)
    {
        var result = chokeAboIpcClient.EnsureTargetCycle(Plugin.PlayerState.ContentId, config, resume: resumeTargetCycle);
        if (result.Succeeded) resumeTargetCycle = false;
        return result;
    }

    private bool HandleTargetCycleResult(ChokeAboTargetCycleCallResult result)
    {
        if (result.Succeeded && result.Status is { RacingRank: >= 40, Phase: ChokeAboTargetCyclePhase.Blocked } &&
            !QuestManager.IsQuestComplete(576))
        {
            resumeTargetCycle = true;
            if (BeginPrerequisiteIfNeeded()) return IsActive;
        }
        if (result.Status?.Phase == ChokeAboTargetCyclePhase.Paused)
        {
            configManager.GetActiveConfig().ChocoboProgressionPaused = true;
            configManager.SaveCurrentAccount();
        }
        var decision = ChocoboTargetCyclePolicy.DecideHandoff(result, currentAttempt, maxAttempts);
        targetCycleStatusReason = string.IsNullOrWhiteSpace(decision.Reason)
            ? "Choke-abo returned no target-cycle reason."
            : decision.Reason;

        switch (decision.Action)
        {
            case ChocoboTargetHandoffAction.Wait:
                if (state != ChocoboState.WaitingForTargetCycle)
                    SetState(ChocoboState.WaitingForTargetCycle);
                nextTargetStatusPollAt = DateTime.UtcNow.AddSeconds(1);
                return true;

            case ChocoboTargetHandoffAction.Race:
                targetReadyForBatch |= decision.TargetReady;
                log.Information($"[ChocoboRace] Choke-abo yielded racing: {targetCycleStatusReason}");
                BeginNextRace();
                return true;

            case ChocoboTargetHandoffAction.Complete:
                if (result.Status?.ProgressionComplete == true)
                {
                    configManager.GetActiveConfig().ChocoboProgressionPaused = true;
                    configManager.SaveCurrentAccount();
                }
                log.Information($"[ChocoboRace] Daily race batch completed after Choke-abo handoff: {targetCycleStatusReason}");
                SetState(ChocoboState.Complete);
                return true;

            default:
                waitingForChokeAbo = !result.Succeeded && !chokeAboIpcClient.IsV3Available;
                NextContinuationUtc = result.Status?.NextCoveringEligibilityUtc?.UtcDateTime ?? DateTime.MaxValue;
                return Defer(targetCycleStatusReason);
        }
    }

    private bool Defer(string reason)
    {
        if (state != ChocoboState.Deferred || targetCycleStatusReason != reason)
            log.Information($"[ChocoboRace] Deferred: {reason}");
        targetCycleStatusReason = reason;
        SetState(ChocoboState.Deferred);
        return false;
    }

    private void TrackAllowance(bool forceSave = false)
    {
        var now = DateTime.UtcNow;
        if (progressionContentId == 0)
        {
            if (!Plugin.ClientState.IsLoggedIn || !Plugin.PlayerState.IsLoaded ||
                string.IsNullOrEmpty(configManager.CurrentCharacterKey)) return;
            var current = configManager.GetActiveConfig();
            if (current.ChocoboAutomationMode != ChocoboAutomationMode.TargetPedigree || !current.ChocoboRacePending) return;
            progressionContentId = Plugin.PlayerState.ContentId;
            progressionAccount = configManager.CurrentAccountId;
            progressionCharacter = configManager.CurrentCharacterKey;
            allowanceDirty |= ChocoboDailyAllowance.Sample(current, now,
                !GameHelpers.IsAddonVisible("RaceChocoboResult") &&
                (IsRacingTerritory() && condition[ConditionFlag.BoundByDuty] || IsQueued() && HasOwnedNativeQueue()), afterReload: true);
        }
        if (!configManager.Accounts.TryGetValue(progressionAccount, out var account) ||
            !account.Characters.TryGetValue(progressionCharacter, out var config)) return;
        var activity = Plugin.ClientState.IsLoggedIn && Plugin.PlayerState.ContentId == progressionContentId &&
            !GameHelpers.IsAddonVisible("RaceChocoboResult") &&
            (IsRacingTerritory() && condition[ConditionFlag.BoundByDuty] || IsQueued() && HasOwnedNativeQueue());
        var activityChanged = config.ChocoboRacePending != activity;
        allowanceDirty |= ChocoboDailyAllowance.Sample(config, now, activity);
        if (activityChanged || allowanceDirty && (forceSave || now - allowanceSaveUtc >= TimeSpan.FromSeconds(5)))
        {
            configManager.SaveAccount(progressionAccount);
            allowanceSaveUtc = now;
            allowanceDirty = false;
        }
    }

    private void ReconcileReloadAllowance(CharacterConfig config)
    {
        var now = DateTime.UtcNow;
        ChocoboDailyAllowance.Sample(config, now, !GameHelpers.IsAddonVisible("RaceChocoboResult"), afterReload: true);
        configManager.SaveCurrentAccount();
    }

    private static bool IsRacingTerritory() => Plugin.ClientState.TerritoryType is 389 or 390 or 391 or 417;

    private static unsafe bool HasOnlySagoliiSelection()
    {
        var agent = AgentContentsFinder.Instance();
        if (agent == null || agent->SelectedDuty.ContentType != ContentsType.Roulette || agent->SelectedDuty.Id != SagoliiRouletteId) return false;
        var count = 0;
        foreach (var entry in agent->SelectedContent)
        {
            if (++count > 1 || entry.ContentType != ContentsType.Roulette || entry.Id != SagoliiRouletteId) return false;
        }
        return count == 1;
    }

    internal bool CanReconcileRacingActivity()
    {
        var config = configManager.GetActiveConfig();
        return config.Enabled && config.EnableChocoboRacing &&
            config.ChocoboAutomationMode == ChocoboAutomationMode.TargetPedigree &&
            (condition[ConditionFlag.BoundByDuty] && IsRacingTerritory() || IsQueued() && HasOwnedNativeQueue());
    }

    private bool IsQueued() => condition[ConditionFlag.InDutyQueue] || condition[ConditionFlag.WaitingForDutyFinder] || condition[ConditionFlag.WaitingForDuty] ||
        GameHelpers.IsAddonVisible("ContentsFinderConfirm");

    private static unsafe bool TryLeaveNativeRaceResult()
    {
        var handle = Plugin.GameGui.GetAddonByName("RaceChocoboResult");
        if (handle.IsNull || !handle.IsReady || !handle.IsVisible) return false;
        var addon = (FFXIVClientStructs.FFXIV.Client.UI.AddonRaceChocoboResult*)handle.Address;
        var button = addon->LeaveButton;
        return button != null && button->OwnerNode != null && button->IsEnabled &&
            GameHelpers.TryClickNativeButton("RaceChocoboResult", null, button->OwnerNode->NodeId);
    }

    private static unsafe bool HasOwnedNativeQueue()
    {
        var finder = ContentsFinder.Instance();
        if (finder == null) return false;
        // ContentType 19 includes other Gold Saucer duties. Check the actual
        // racing territories before cancelling a queue owned by this workflow.
        bool Racing(uint id) => Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.ContentFinderCondition>()
            .GetRowOrDefault(id)?.TerritoryType.RowId is 389 or 390 or 391 or 417;
        bool Owned(ContentsType type, uint id) => type == ContentsType.Regular && Racing(id) ||
            type == ContentsType.Roulette && id == SagoliiRouletteId;
        var queued = false;
        foreach (var entry in finder->QueueInfo.QueuedEntries)
        {
            if (entry.Id == 0) continue;
            if (!Owned(entry.ContentType, entry.Id)) return false;
            queued = true;
        }
        return queued || GameHelpers.IsAddonVisible("ContentsFinderConfirm") &&
            Owned(finder->QueueInfo.PoppedQueueEntry.ContentType, finder->QueueInfo.PoppedQueueEntry.Id);
    }

    private unsafe bool CancelOwnedQueue()
    {
        if (IsRacingTerritory() || !targetCycleEnabledForBatch || Plugin.PlayerState.ContentId != progressionContentId) return false;
        var waiting = IsQueued();
        if (!waiting) return true;
        if (!HasOwnedNativeQueue())
        {
            targetCycleStatusReason = "Queue ownership could not be reconciled; no new race will be admitted.";
            return false;
        }
        if (!queueCancellationRequested)
        {
            queueCancellationRequested = true;
            ContentsFinder.Instance()->QueueInfo.CancelQueue();
        }
        return false;
    }
}
