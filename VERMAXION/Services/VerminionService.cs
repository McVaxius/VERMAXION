using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.Command;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using Dalamud.Game.ClientState.Keys;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.System.Input;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.Game;
using VERMAXION.IPC;
using VERMAXION.Models;

namespace VERMAXION.Services;

/// <summary>Coordinates CPU setup, verified results and the selected Verminion goal.</summary>
public sealed class VerminionService : IDisposable
{
    private readonly ICommandManager commandManager;
    private readonly ICondition condition;
    private readonly IPluginLog log;
    private readonly ConfigManager configManager;
    private readonly LifestreamIPC lifestream;
    private readonly VNavmeshIPC navigation;
    private DateTime stateEnteredAt;
    private ulong owner;
    private string ownerAccount = string.Empty;
    private string ownerCharacter = string.Empty;
    private int challengeStage = 1;
    private bool navigationRequested;
    private bool ownsTravel;
    private DateTime nextInteractionUtc;
    private bool admissionConfirmed;
    private bool admissionSnapshot;
    private int tutorialPromptFloor;
    private string handledPrompt = string.Empty;
    private DateTime promptHandledUtc;
    private DateTime nextPromptCheckUtc;
    private VirtualKey? heldKey;
    private DateTime releaseKeyUtc;
    private bool pendingGateSummon;
    private int pendingTutorialSummons;
    private bool pendingTutorialMove;
    private uint? pendingTutorialAction;
    private bool tutorialActionPanelsRestored;
    private Vector3? pendingGroupDestination;
    private string tutorialGroupName = "Wayward Hatchling";
    private int cameraAdjustments;
    private ulong? pendingSameTypeSelection;
    private Vector3 tutorialDestination;
    private ulong? groupAttackTarget;
    private Vector2? pendingBattlefieldClick;
    private DateTime battlefieldClickReleaseUtc;
    private bool pendingBattlefieldRightClick;
    private DateTime movementSnapshotUtc;
    private string? pendingUnhandledPrompt;
    private DateTime unhandledImageUtc;
    private bool battlefieldRenderingPrepared;
    private bool ordinaryBattleObserved;
    private bool reconcilingSummonQueue;
    private bool resultExitRequested;
    private bool ownsChallengeLog;
    private bool challengeLogCategorySelected;
    private DateTime challengeLogSelectedAt;
    private VerminionBattleStrategy opening = new();
    private VerminionBossStrategy bossStrategy = new();
    private readonly HashSet<ulong> deployedBossUnits = new();
    private DateTime bossOrderUtc;
    private DateTime bossWaveUtc;
    private DateTime bossSpawnUtc;
    private DateTime bossDefenderSpawnUtc;
    private DateTime bossSupportSpawnUtc;
    private DateTime bossSupportOrderUtc;
    private DateTime bossSupportWaveUtc;
    private DateTime bossDefenseOrderUtc;
    private DateTime bossDefenderWaveUtc;
    private ulong bossDefenseTarget;
    private Vector3? bossOrderPosition;
    private ulong bossOrderTarget;
    private float bossOrderRotation;
    private DateTime nextBossSpecialUtc;
    private DateTime bombLeverOrderUtc;
    private DateTime bombCarrierOrderUtc;
    private DateTime bombDropUtc;
    private DateTime bombTriggerUtc;
    private int bombCycles;
    private bool bossBurstStarted;
    private bool bossContactCaptured;
    private bool bossEnraged;
    private bool bossArmyReady;
    private bool odinRegrouping;
    private bool odinRegrouped;
    private DateTime odinRegroupUtc;
    private DateTime openingStepUtc;
    private DateTime nextObjectiveOrderUtc;
    private DateTime ordinarySpawnUtc;
    private readonly Dictionary<ulong, DateTime> stoneUnitOrders = new();
    private int groupMinimum = 4;
    private Vector3? groupOrigin;
    private bool groupSelectionCleared;
    private readonly HashSet<ulong> rejectedGroupSelections = new();
    private ulong groupSelectionAnchor;
    private bool groupSelectionVerified;
    private bool groupSingleSelectionVerified;
    private ulong? singleUnitSelection;
    private bool requireExactSingleSelection;
    private VerminionBattleOutcome lastBattleOutcome;
    private bool lastResultCredited;
    private bool verifyingTutorialExit;
    private VerminionMode runMode;
    private int victoryTarget;
    private bool campaign;
    private uint registeringItem;
    private DateTime registrationSentUtc;
    private string reason = "Idle";

    public enum VerminionState
    {
        Idle, UnlockingPrerequisite, RegisteringMinions, TravellingToSaucer, TravellingToMinionSquare, ApproachingTable,
        InspectingControls, SelectingTutorial, WaitingForTutorial, InDuty, LeavingResult, InspectingProgress, Complete, Failed,
    }

    public VerminionState State { get; private set; }
    public int CurrentAttempt => configManager.GetActiveConfig().VerminionProgress.RunAttempts;
    public bool IsActive => State is not (VerminionState.Idle or VerminionState.Complete or VerminionState.Failed);
    public bool IsComplete => State == VerminionState.Complete;
    public bool IsFailed => State == VerminionState.Failed;
    private bool IsStoneStage => VerminionBattleStrategy.IsStoneStage(challengeStage);
    private bool IsBossStage => challengeStage is 4 or 6 or 9 or 12 or 15 or 19 or 23;
    private bool HasBossDefenders => challengeStage is 6 or 15;
    private bool VerifyBattleGroup => IsBossStage || challengeStage == 7;
    public string StatusText => $"{(IsComplete ? "Complete: " : string.Empty)}{reason} | {ProgressSummary(configManager.GetActiveConfig())}";

    public static string ProgressSummary(CharacterConfig config) => config.VerminionProgress.Summary(
        config.VerminionMode, config.VerminionVictoryTarget, ResetDetectionService.GetLastWeeklyReset(DateTime.UtcNow));

    public static bool WeeklyGoalReached(CharacterConfig config) => config.VerminionProgress.WeeklyGoalReached(
        config.VerminionMode, config.VerminionVictoryTarget, ResetDetectionService.GetLastWeeklyReset(DateTime.UtcNow));

    public VerminionService(ICommandManager commandManager, ICondition condition, IPluginLog log,
        ConfigManager configManager, LifestreamIPC lifestream, VNavmeshIPC navigation)
    {
        this.commandManager = commandManager;
        this.condition = condition;
        this.log = log;
        this.configManager = configManager;
        this.lifestream = lifestream;
        this.navigation = navigation;
        Plugin.DutyState.DutyCompleted += OnDutyCompleted;
    }

    public void Start()
    {
        if (IsActive) return;
        if (configManager.GetActiveConfig().VerminionPaused)
        { Fail("Paused by FULL STOP; use Run/Resume."); return; }
        owner = Plugin.PlayerState.ContentId;
        if (owner == 0) { Fail("No current character."); return; }
        ownerAccount = configManager.CurrentAccountId;
        ownerCharacter = configManager.CurrentCharacterKey;
        VerminionGameInteraction.CaptureSetup();
        VerminionGameInteraction.CaptureBattle("start observation");
        var currentDuty = VerminionGameInteraction.CurrentCpuDutyId();
        var config = configManager.GetActiveConfig();
        campaign = config.VerminionProgress.CampaignRequested;
        runMode = config.VerminionMode;
        victoryTarget = Math.Clamp(config.VerminionVictoryTarget, 1, 1000);
        var progress = config.VerminionProgress;
        progress.ObserveWeek(ResetDetectionService.GetLastWeeklyReset(DateTime.UtcNow));
        progress.EnsureRun(config.VerminionMode, config.VerminionVictoryTarget);
        var pendingQueue = progress.PendingDuty is >= 552 and <= 575 &&
            VerminionGameInteraction.HasChallengeQueue((int)progress.PendingDuty - 551);
        if (progress.PendingMatch != 0 && currentDuty != progress.PendingDuty && !pendingQueue)
            progress.AbandonMatch();
        configManager.SaveCurrentAccount();
        if (!campaign && progress.PendingMatch == 0 && progress.WinningRunLimitReached &&
            progress.Remaining(config.VerminionMode, config.VerminionVictoryTarget) > 0)
        {
            config.VerminionPaused = true;
            configManager.SaveCurrentAccount();
            Fail("Winning attempt limit reached; use Resume after reviewing the strategy.");
            return;
        }
        challengeStage = currentDuty is >= 552 and <= 575 ? (int)currentDuty - 551 :
            progress.SelectedChallengeStage is >= 1 and <= 24 ? progress.SelectedChallengeStage : 2;
        reconcilingSummonQueue = currentDuty != 0;
        admissionConfirmed = false;
        admissionSnapshot = false;
        tutorialPromptFloor = VerminionGameInteraction.CurrentLogIndex();
        handledPrompt = string.Empty;
        pendingGateSummon = false;
        pendingTutorialSummons = 0;
        pendingTutorialMove = false;
        pendingTutorialAction = null;
        pendingGroupDestination = null;
        groupAttackTarget = null;
        groupSelectionVerified = false;
        cameraAdjustments = 0;
        pendingSameTypeSelection = null;
        movementSnapshotUtc = DateTime.MinValue;
        pendingUnhandledPrompt = null;
        battlefieldRenderingPrepared = false;
        ordinaryBattleObserved = false;
        opening = new();
        stoneUnitOrders.Clear();
        ResetBossStrategy();
        nextObjectiveOrderUtc = DateTime.MinValue;
        openingStepUtc = DateTime.UtcNow;
        resultExitRequested = false;
        lastBattleOutcome = VerminionBattleOutcome.Unknown;
        lastResultCredited = false;
        verifyingTutorialExit = false;
        nextPromptCheckUtc = DateTime.MinValue;
        if (VerminionGameInteraction.HasChallengeQueue(challengeStage))
        {
            admissionConfirmed = true;
            SetState(VerminionState.WaitingForTutorial, $"Resuming Stage {challengeStage} queue");
            return;
        }
        if (VerminionGameInteraction.IsAdmissionPrompt)
        { SetState(VerminionState.WaitingForTutorial, "Resuming tutorial admission"); return; }
        if (VerminionGameInteraction.IsChallengeMenu)
        { SetState(VerminionState.SelectingTutorial, "Resuming tutorial selection"); return; }
        if (VerminionGameInteraction.IsSetupMenu)
        { SetState(VerminionState.InspectingControls, "Resuming the verified Verminion setup menu"); return; }
        if (condition[ConditionFlag.PlayingLordOfVerminion])
        {
            SetState(VerminionState.InDuty, $"Resuming Stage {challengeStage}");
            return;
        }
        if (!GameHelpers.IsPlayerAvailable() || condition[ConditionFlag.BoundByDuty] ||
            condition[ConditionFlag.InDutyQueue] || condition[ConditionFlag.WaitingForDutyFinder] ||
            !lifestream.TryReadBusy(out var busy) || busy)
        { Fail("Character, duty queue, or travel is busy/unavailable."); return; }
        if (BeginPrerequisiteIfNeeded()) return;
        registeringItem = 0;
        SetState(VerminionState.RegisteringMinions, "Inspecting inventory minions");
    }

    private void BeginTravel()
    {
        if (Plugin.ClientState.TerritoryType == 388)
        { InspectChallengeProgress(); return; }
        if (Plugin.ClientState.TerritoryType == 144)
        { TravelToMinionSquare(); return; }
        ownsTravel = lifestream.ExecuteCommand("/li saucer");
        if (!ownsTravel) { Fail("Gold Saucer travel was rejected."); return; }
        SetState(VerminionState.TravellingToSaucer, "Travelling to the Gold Saucer");
    }

    public void RunTask() => RunGoal(false);
    public void RunChallenges() => RunGoal(true);
    public void ResumeTask() => RunGoal(configManager.GetActiveConfig().VerminionProgress.CampaignRequested);

    private void RunGoal(bool clearChallenges)
    {
        if (IsActive) return;
        var config = configManager.GetActiveConfig();
        if (config.VerminionPaused)
        {
            config.VerminionProgress.EnsureRun(config.VerminionMode, config.VerminionVictoryTarget, restart: true);
            config.VerminionProgress.CampaignStageAttempts = 0;
        }
        config.VerminionProgress.CampaignRequested = clearChallenges;
        config.VerminionPaused = false;
        configManager.SaveCurrentAccount();
        Start();
    }

    public void Reset()
    {
        StopOwnedMovement();
        if (owner != 0 && (Plugin.PlayerState.ContentId != owner || configManager.GetActiveConfig().VerminionPaused))
            AbandonPendingMatch();
        owner = 0;
        ownerAccount = ownerCharacter = string.Empty;
        navigationRequested = false;
        SetState(VerminionState.Idle, "Idle");
    }

    public void Dispose()
    {
        Plugin.DutyState.DutyCompleted -= OnDutyCompleted;
        Reset();
    }

    private void OnDutyCompleted(Dalamud.Game.DutyState.IDutyStateEventArgs args)
    {
        if (!IsActive || owner == 0 || Plugin.PlayerState.ContentId != owner ||
            args.TerritoryType.RowId != 506) return;
        log.Information($"[VerminionControl] duty completion event; duty={args.ContentFinderCondition.RowId}; no victory inferred");
        VerminionGameInteraction.CaptureBattle("duty completion event");
        VerminionGameInteraction.CaptureSetup();
    }

    // Shared with other Gold Saucer tasks; opening is not selection or admission.
    public static unsafe bool OpenDutyFinder(uint contentFinderConditionId)
    {
        var agent = AgentContentsFinder.Instance();
        if (agent == null) return false;
        agent->OpenRegularDuty(contentFinderConditionId);
        return true;
    }

    public void Update()
    {
        if (!IsActive) return;
        if (owner == 0 || Plugin.PlayerState.ContentId != owner)
        { Fail("Character changed; no result recorded."); return; }
        if (configManager.GetActiveConfig().VerminionPaused)
        { Fail("Paused; no result recorded."); return; }
        var elapsed = (DateTime.UtcNow - stateEnteredAt).TotalSeconds;
        try
        {
            switch (State)
            {
                case VerminionState.UnlockingPrerequisite:
                    if (elapsed > 900) { Fail("The owned unlock quest timed out; use Resume after reviewing its progress."); return; }
                    if (DateTime.UtcNow < nextInteractionUtc) return;
                    nextInteractionUtc = DateTime.UtcNow.AddSeconds(2);
                    UpdateUnlockQuest();
                    return;
                case VerminionState.RegisteringMinions:
                    if (elapsed > 300) { Fail("Minion registration timed out."); return; }
                    if (!GameHelpers.IsPlayerAvailable()) return;
                    if (registeringItem != 0)
                    {
                        if (DateTime.UtcNow - registrationSentUtc < RegistrableRegistrationPolicy.VerificationDelay) return;
                        if (!VerminionGameInteraction.IsMinionItemRegistered(registeringItem))
                        { Fail($"Minion item {registeringItem} registration was not confirmed."); return; }
                        log.Information($"[Verminion] Minion item {registeringItem} registration verified");
                        registeringItem = 0;
                    }
                    if (!VerminionGameInteraction.TryFindUnregisteredMinion(out var minionItem))
                    { Fail("Minion inventory or unlock state is unavailable."); return; }
                    if (minionItem == 0) { BeginTravel(); return; }
                    if (!GameHelpers.UseItem(minionItem))
                    { Fail($"Minion item {minionItem} use was rejected."); return; }
                    registeringItem = minionItem;
                    registrationSentUtc = DateTime.UtcNow;
                    reason = $"Registering minion item {minionItem}";
                    return;
                case VerminionState.TravellingToSaucer:
                    if (elapsed > 120) { Fail("Gold Saucer travel timed out."); return; }
                    if (elapsed < 3 || !TravelSettled() || Plugin.ClientState.TerritoryType != 144) return;
                    ownsTravel = false;
                    TravelToMinionSquare();
                    return;
                case VerminionState.TravellingToMinionSquare:
                    if (elapsed > 90) { Fail("Minion Square travel timed out."); return; }
                    if (elapsed < 3 || !TravelSettled() || Plugin.ClientState.TerritoryType != 388) return;
                    ownsTravel = false;
                    InspectChallengeProgress();
                    return;
                case VerminionState.ApproachingTable:
                    if (elapsed > 60) { Fail("Could not reach the Verminion table."); return; }
                    var player = Plugin.ObjectTable.LocalPlayer;
                    if (player == null || !GameHelpers.IsPlayerAvailable()) return;
                    var table = Plugin.ObjectTable.Where(obj => obj.BaseId == 2006529 && obj.IsTargetable)
                        .OrderBy(obj => Vector3.DistanceSquared(player.Position, obj.Position)).FirstOrDefault();
                    if (table == null) { Fail("No Verminion table is loaded."); return; }
                    if (Vector3.Distance(player.Position, table.Position) > 3.8f)
                    {
                        var direction = Vector3.Normalize(new Vector3(player.Position.X - table.Position.X, 0, player.Position.Z - table.Position.Z));
                        var approach = table.Position + direction * 2.4f;
                        approach.Y = player.Position.Y;
                        if (!navigationRequested) navigationRequested = navigation.PathfindAndMoveTo(approach);
                        return;
                    }
                    if (navigationRequested) { navigation.Stop(); navigationRequested = false; }
                    if (DateTime.UtcNow < nextInteractionUtc) return;
                    nextInteractionUtc = DateTime.UtcNow.AddSeconds(2);
                    if (!VerminionGameInteraction.InteractWithTable(table)) return;
                    SetState(VerminionState.InspectingControls, "Reading the Verminion setup menu");
                    return;
                case VerminionState.InspectingControls:
                    if (elapsed < 3) return;
                    if (GameHelpers.TrySelectStringExact("Verminion Challenge", out _))
                        SetState(VerminionState.SelectingTutorial, $"Selecting Stage {challengeStage}");
                    else if (elapsed > 10) Fail("The verified Verminion Challenge menu is unavailable.");
                    return;
                case VerminionState.SelectingTutorial:
                    if (elapsed < 2) return;
                    VerminionGameInteraction.CaptureAddon("SelectString");
                    VerminionGameInteraction.CaptureAddon("SelectIconString");
                    var highestStage = VerminionGameInteraction.ReadHighestAvailableChallenge();
                    if (highestStage == 0) { Fail("The available challenge list is unreadable."); return; }
                    var challengeProgress = configManager.GetActiveConfig().VerminionProgress;
                    if (challengeProgress.ObserveAvailableChallenges(highestStage))
                    {
                        configManager.SaveCurrentAccount();
                        log.Information($"[Verminion] Sequential challenge unlock verified through Stage {highestStage}");
                    }
                    if (verifyingTutorialExit && highestStage < 2)
                    { Fail("Tutorial exit did not unlock Stage 2; no completion recorded."); return; }
                    verifyingTutorialExit = false;
                    challengeStage = campaign ? challengeProgress.NextUnclearedChallenge : highestStage >= 2 ? 2 : 1;
                    if (campaign && challengeStage == 25) { CompleteCampaign(); return; }
                    if (campaign && challengeProgress.CampaignStageLimitReached(challengeStage))
                    { PauseCampaignAtLimit(); return; }
                    if (challengeStage != 1 && !IsStoneStage && !IsBossStage)
                    { Fail($"Stage {challengeStage} needs its battle strategy implemented and verified before admission."); return; }
                    opening = new(challengeStage);
                    bossStrategy = new(challengeStage);
                    if (IsStoneStage && (campaign || runMode == VerminionMode.WinTarget || (challengeProgress.ClearedChallengeMask & 2) == 0) &&
                        !VerminionGameInteraction.PrepareOwnedMinion(opening.CurrentMinion))
                    { Fail($"The Stage {challengeStage} opening requires owned {opening.CurrentMinionName} and a palette slot. No purchase made."); return; }
                    if (IsBossStage && !VerminionGameInteraction.PrepareOwnedMinion(bossStrategy.CurrentMinion))
                    { Fail($"Stage {challengeStage} requires owned {bossStrategy.CurrentMinionName} and a palette slot. No purchase made."); return; }
                    if (HasBossDefenders && !VerminionGameInteraction.PrepareOwnedMinion(VerminionBossStrategy.DefenderMinion))
                    { Fail($"Stage {challengeStage} defense requires owned Wind-up Haurchefant and a palette slot. No purchase made."); return; }
                    if (challengeStage == 15 && !VerminionGameInteraction.PrepareOwnedMinion(VerminionBossStrategy.Minion))
                    { Fail("Stage 15 requires owned Wind-up Alphinaud support and a palette slot. No purchase made."); return; }
                    challengeProgress.SelectedChallengeStage = challengeStage;
                    configManager.SaveCurrentAccount();
                    if (GameHelpers.TrySelectStringExact(VerminionGameInteraction.ChallengeName(challengeStage), out _))
                        SetState(VerminionState.WaitingForTutorial, $"Waiting for Stage {challengeStage} admission");
                    else Fail($"Stage {challengeStage} is unavailable in the challenge menu.");
                    return;
                case VerminionState.WaitingForTutorial:
                    if (elapsed > 60) { VerminionGameInteraction.CaptureBattle("admission timeout"); Fail("Tutorial admission timed out; no result recorded."); return; }
                    if (elapsed < 2) return;
                    if (!admissionConfirmed && VerminionGameInteraction.IsAdmissionPrompt)
                        admissionConfirmed = GameHelpers.TryClickNativeButton("SelectYesno", "Yes", 8);
                    // Save the verified CPU queue before Commence can change
                    // territory or unload the plugin. Queueing is an attempt,
                    // while participation/victory still require the result UI.
                    var queuedProgress = configManager.GetActiveConfig().VerminionProgress;
                    if (admissionConfirmed && queuedProgress.PendingMatch == 0 && VerminionGameInteraction.HasChallengeQueue(challengeStage))
                    {
                        queuedProgress.BeginMatch((uint)(551 + challengeStage));
                        configManager.SaveCurrentAccount();
                        log.Information($"[Verminion] Verified CPU queue; match={queuedProgress.PendingMatch}; stage={challengeStage}");
                    }
                    if (admissionConfirmed && DateTime.UtcNow >= nextInteractionUtc && VerminionGameInteraction.TryCommenceChallenge(challengeStage))
                        nextInteractionUtc = DateTime.UtcNow.AddSeconds(5);
                    if (elapsed > 10 && !admissionSnapshot)
                    {
                        admissionSnapshot = true;
                        VerminionGameInteraction.CaptureBattle("ten seconds after admission selection");
                    }
                    if (condition[ConditionFlag.PlayingLordOfVerminion] && admissionConfirmed &&
                        VerminionGameInteraction.CurrentCpuDutyId() == 551 + challengeStage)
                    {
                        var admitted = configManager.GetActiveConfig().VerminionProgress;
                        if (admitted.PendingMatch == 0)
                        {
                            admitted.BeginMatch((uint)(551 + challengeStage));
                            configManager.SaveCurrentAccount();
                            log.Information($"[Verminion] Verified admission; match={admitted.PendingMatch}; stage={challengeStage}");
                        }
                        SetState(VerminionState.InDuty, $"Observing Stage {challengeStage} battlefield");
                    }
                    return;
                case VerminionState.InDuty:
                    if (elapsed < 5) return;
                    if (VerminionGameInteraction.HasBattleResult())
                    {
                        VerminionGameInteraction.CaptureBattle("CPU result UI available");
                        VerminionGameInteraction.CaptureSetup();
                        lastBattleOutcome = VerminionGameInteraction.ReadBattleOutcome();
                        if (lastBattleOutcome == VerminionBattleOutcome.Unknown)
                        { Fail("Unknown battle result; no result recorded."); return; }
                        var resultProgress = configManager.GetActiveConfig().VerminionProgress;
                        lastResultCredited = resultProgress.PendingDuty == VerminionGameInteraction.CurrentCpuDutyId() &&
                            resultProgress.RecordResult(resultProgress.PendingMatch, lastBattleOutcome,
                                ResetDetectionService.GetLastWeeklyReset(DateTime.UtcNow));
                        if (lastResultCredited) configManager.SaveCurrentAccount();
                        log.Information($"[Verminion] Verified outcome={lastBattleOutcome}; credited={lastResultCredited}; matches={resultProgress.WeeklyMatches}; wins={resultProgress.WeeklyWins}");
                        StopOwnedMovement();
                        SetState(VerminionState.LeavingResult, $"CPU {lastBattleOutcome} verified; leaving the result screen");
                        return;
                    }
                    if ((IsStoneStage || IsBossStage) && VerminionGameInteraction.CurrentCpuDutyId() == 551 + challengeStage)
                    {
                        if (reconcilingSummonQueue)
                        {
                            if (VerminionGameInteraction.ReadQueuedSummonCount() == 0)
                            {
                                reconcilingSummonQueue = false;
                                log.Information("[VerminionControl] existing summon queue drained after reload; new requests enabled");
                            }
                            else if (elapsed > 90)
                            { Fail("Existing summon queue did not drain after reload; no result recorded."); return; }
                        }
                        if (!ordinaryBattleObserved)
                        {
                            ordinaryBattleObserved = true;
                            VerminionGameInteraction.CaptureBattle($"Stage {challengeStage} opening started");
                            openingStepUtc = DateTime.UtcNow;
                            opening = new(challengeStage);
                            if (IsBossStage) ResetBossStrategy();
                            reason = IsBossStage ? $"Stage {challengeStage}: follow the boss with damage groups and reinforcements"
                                : $"Stage {challengeStage}: split owned stone attackers between objectives";
                        }
                        if (IsBossStage)
                        {
                            var confirmed = bossStrategy.ObserveUnits(Plugin.ObjectTable
                                .OfType<Dalamud.Game.ClientState.Objects.Types.IBattleNpc>()
                                .Where(obj => VerminionGameInteraction.IsFriendlyMinion(obj) &&
                                    obj.Name.TextValue == bossStrategy.CurrentMinionName)
                                .Select(obj => obj.GameObjectId));
                            if (confirmed > 0) bossSpawnUtc = DateTime.UtcNow;
                            if (bossStrategy.WaitingForWave && (DateTime.UtcNow - bossSpawnUtc).TotalSeconds > 90)
                            { Fail($"Stage {challengeStage} summon requests produced no new minions within 90 seconds; no result recorded."); return; }
                            if (HasBossDefenders)
                            {
                                var guards = bossStrategy.Defenders.ObserveUnits(Plugin.ObjectTable
                                    .OfType<Dalamud.Game.ClientState.Objects.Types.IBattleNpc>()
                                    .Where(obj => VerminionGameInteraction.IsFriendlyMinion(obj) && obj.Name.TextValue == VerminionBossStrategy.DefenderName)
                                    .Select(obj => obj.GameObjectId));
                                if (guards > 0) bossDefenderSpawnUtc = DateTime.UtcNow;
                                if (bossStrategy.Defenders.Pending > 0 && (DateTime.UtcNow - bossDefenderSpawnUtc).TotalSeconds > 90)
                                { Fail($"Stage {challengeStage} defenders did not spawn within 90 seconds; no result recorded."); return; }
                            }
                            if (challengeStage == 15)
                            {
                                var supports = bossStrategy.Support.ObserveUnits(Plugin.ObjectTable
                                    .Where(obj => VerminionGameInteraction.IsFriendlyMinion(obj) && obj.Name.TextValue == VerminionBossStrategy.MinionName)
                                    .Select(obj => obj.GameObjectId));
                                if (supports > 0) bossSupportSpawnUtc = DateTime.UtcNow;
                                if (bossStrategy.Support.Pending > 0 && (DateTime.UtcNow - bossSupportSpawnUtc).TotalSeconds > 90)
                                { Fail("Stage 15 support did not spawn within 90 seconds; no result recorded."); return; }
                            }
                        }
                        if (IsStoneStage)
                        {
                            var confirmed = opening.Reinforcements.ObserveUnits(Plugin.ObjectTable
                                .OfType<Dalamud.Game.ClientState.Objects.Types.IBattleNpc>()
                                .Where(obj => VerminionGameInteraction.IsFriendlyMinion(obj) &&
                                    obj.Name.TextValue == opening.CurrentMinionName)
                                .Select(obj => obj.GameObjectId));
                            if (confirmed > 0) ordinarySpawnUtc = DateTime.UtcNow;
                            if (opening.Reinforcements.Pending > 0 && (DateTime.UtcNow - ordinarySpawnUtc).TotalSeconds > 90)
                            { Fail($"Stage {challengeStage} reinforcements produced no new minions within 90 seconds; no result recorded."); return; }
                        }
                        if (elapsed > 600)
                        { Fail("CPU result observation timed out; no result recorded."); return; }
                        if (!campaign && runMode == VerminionMode.Participation &&
                            (configManager.GetActiveConfig().VerminionProgress.ClearedChallengeMask & 2) != 0)
                        {
                            reason = "Stage 2: waiting for an intentional CPU loss";
                            return;
                        }
                    }
                    if (VerminionGameInteraction.CurrentCpuDutyId() != 551 + challengeStage)
                    {
                        if (challengeStage == 1 && Plugin.ClientState.TerritoryType == 388 &&
                            !condition[ConditionFlag.BetweenAreas] && !condition[ConditionFlag.BetweenAreas51])
                        {
                            AbandonPendingMatch(); // Tutorials do not count as weekly results.
                            StopOwnedMovement();
                            verifyingTutorialExit = true;
                            admissionConfirmed = false;
                            admissionSnapshot = false;
                            battlefieldRenderingPrepared = false;
                            pendingTutorialMove = false;
                            pendingTutorialAction = null;
                            pendingGroupDestination = null;
                            pendingSameTypeSelection = null;
                            movementSnapshotUtc = DateTime.MinValue;
                            SetState(VerminionState.ApproachingTable, "Verifying the tutorial unlocked Stage 2");
                            return;
                        }
                        VerminionGameInteraction.CaptureBattle("tutorial ended without an observed result");
                        Fail($"Stage {challengeStage} ended without an observed result. No result recorded.");
                        return;
                    }
                    if (!battlefieldRenderingPrepared)
                    {
                        VerminionGameInteraction.PrepareBattlefieldRendering();
                        battlefieldRenderingPrepared = true;
                        nextPromptCheckUtc = DateTime.UtcNow.AddSeconds(1);
                        return;
                    }
                    UpdateTutorialProof();
                    return;
                case VerminionState.LeavingResult:
                    if (elapsed > 30) { Fail("Result closure timed out; no weekly completion recorded."); return; }
                    if (Plugin.ClientState.TerritoryType == 388 && !condition[ConditionFlag.BetweenAreas] &&
                        !condition[ConditionFlag.BetweenAreas51])
                    {
                        log.Information("[VerminionControl] CPU result closure verified");
                        if (!lastResultCredited)
                        { Fail("Result closed without a matching saved admission; no additional credit."); return; }
                        InspectChallengeProgress();
                        return;
                    }
                    if (!resultExitRequested && elapsed >= 1)
                        resultExitRequested = VerminionGameInteraction.TryLeaveBattleResult();
                    return;
                case VerminionState.InspectingProgress:
                    if (elapsed < 3) return;
                    if (!challengeLogCategorySelected)
                    {
                        challengeLogCategorySelected = GameHelpers.TrySelectNativeListEntry("ContentsNote", "Gold Saucer");
                        challengeLogSelectedAt = DateTime.UtcNow;
                        if (!challengeLogCategorySelected) Fail("Gold Saucer Challenge Log category unavailable.");
                        return;
                    }
                    if ((DateTime.UtcNow - challengeLogSelectedAt).TotalSeconds < 2) return;
                    VerminionGameInteraction.CaptureChallengeLog();
                    var participation = VerminionGameInteraction.ReadWeeklyParticipation();
                    if (participation == null) { Fail("Weekly participation is unreadable."); return; }
                    var progress = configManager.GetActiveConfig().VerminionProgress;
                    if (progress.ObserveParticipation(ResetDetectionService.GetLastWeeklyReset(DateTime.UtcNow), participation.Value))
                        configManager.SaveCurrentAccount();
                    log.Information($"[VerminionControl] existing weekly participation={participation.Value}; victories unchanged");
                    VerminionGameInteraction.CloseChallengeLog();
                    ownsChallengeLog = false;
                    ContinueWeeklyGoal();
                    return;
            }
        }
        catch (Exception ex) { Fail($"Interaction failed: {ex.Message}. No result recorded."); }
    }

    private bool TravelSettled() => GameHelpers.IsPlayerAvailable() && lifestream.TryReadBusy(out var busy) && !busy;

    private bool OwnsUnlockQuest()
    {
        var progress = configManager.GetActiveConfig().VerminionProgress;
        return progress.UnlockQuestId != 0 && progress.UnlockQuestProvider is "WigglyQuest" or "Questionable" &&
            Plugin.PluginInterface.GetIpcSubscriber<string?>($"{progress.UnlockQuestProvider}.GetCurrentQuestId")
                .InvokeFunc() == progress.UnlockQuestId.ToString();
    }

    private bool BeginPrerequisiteIfNeeded()
    {
        var progress = configManager.GetActiveConfig().VerminionProgress;
        var next = new ushort[] { 434, 435, 1431 }.FirstOrDefault(id => !QuestManager.IsQuestComplete(id));
        if (progress.UnlockQuestId != 0 && progress.UnlockQuestId != next)
        {
            StopOwnedUnlockQuest();
            if (progress.UnlockPriorityInserted)
            { Fail("The previous unlock priority entry must be released before continuing."); return true; }
            progress.UnlockQuestId = 0;
            progress.UnlockQuestProvider = string.Empty;
            configManager.SaveCurrentAccount();
        }
        if (next == 0) return false;
        if (!Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Quest>().TryGetRow((uint)next + 65536, out var quest))
        { Fail("Required Verminion unlock quest data is unavailable."); return true; }
        var prefix = ChocoboRaceService.QuestIpcPrefix;
        if (prefix == null)
        { Fail($"{quest.Name} requires a loaded WigglyQuest or Questionable single-quest route."); return true; }
        try
        {
            if (Plugin.PluginInterface.GetIpcSubscriber<bool>($"{prefix}.IsRunning").InvokeFunc())
            {
                if (progress.UnlockQuestProvider == prefix && progress.UnlockQuestId == next && OwnsUnlockQuest())
                    SetState(VerminionState.UnlockingPrerequisite, $"Completing {quest.Name} through {prefix}");
                else Fail("Another quest automation owns the character.");
                return true;
            }
            if (Plugin.ObjectTable.LocalPlayer?.Level < quest.ClassJobLevel[0])
            { Fail($"{quest.Name} requires level {quest.ClassJobLevel[0]} on the current job."); return true; }
            if (Plugin.PluginInterface.GetIpcSubscriber<string, bool>($"{prefix}.IsQuestLocked").InvokeFunc(next.ToString()))
            {
                var missing = string.Join(", ", quest.PreviousQuest.Where(previous => previous.RowId != 0 &&
                    !QuestManager.IsQuestComplete((ushort)(previous.RowId & 0xFFFF)))
                    .Select(previous => previous.Value.Name.ToString()));
                Fail($"{quest.Name} is locked. " + (missing.Length == 0 ? "Additional game prerequisites are unmet." : $"Unfinished prerequisites: {missing}."));
                return true;
            }
            if (progress.UnlockPriorityInserted && progress.UnlockQuestProvider != prefix)
            { Fail("The previous quest provider must be loaded to release its owned priority entry."); return true; }
            progress.UnlockQuestId = next;
            progress.UnlockQuestProvider = prefix;
            configManager.SaveCurrentAccount();
            if (prefix == "WigglyQuest" && !Plugin.PluginInterface
                .GetIpcSubscriber<string, bool>("WigglyQuest.IsQuestInPriority").InvokeFunc(next.ToString()))
            {
                progress.UnlockPriorityInserted = true;
                configManager.SaveCurrentAccount();
                if (!Plugin.PluginInterface.GetIpcSubscriber<int, string, bool>("WigglyQuest.InsertQuestPriority").InvokeFunc(0, next.ToString()))
                {
                    progress.UnlockPriorityInserted = false;
                    configManager.SaveCurrentAccount();
                    Fail($"Could not retain {quest.Name} as the selected single quest.");
                    return true;
                }
            }
            if (!Plugin.PluginInterface.GetIpcSubscriber<string, bool>($"{prefix}.StartSingleQuest").InvokeFunc(next.ToString()))
            { Fail($"{prefix} could not start the existing route for {quest.Name}."); return true; }
            SetState(VerminionState.UnlockingPrerequisite, $"Completing {quest.Name} through {prefix}");
            nextInteractionUtc = DateTime.UtcNow.AddSeconds(2);
        }
        catch (Exception ex) { Fail($"Verminion unlock route unavailable: {ex.Message}"); }
        return true;
    }

    private void UpdateUnlockQuest()
    {
        var progress = configManager.GetActiveConfig().VerminionProgress;
        if (progress.UnlockQuestId == 0) { Fail("Unlock quest ownership is missing."); return; }
        if (QuestManager.IsQuestComplete(progress.UnlockQuestId))
        {
            log.Information($"[Verminion] Unlock quest {progress.UnlockQuestId} completion verified");
            StopOwnedUnlockQuest();
            if (progress.UnlockPriorityInserted)
            { Fail("The owned unlock priority entry could not be released."); return; }
            progress.UnlockQuestId = 0;
            progress.UnlockQuestProvider = string.Empty;
            configManager.SaveCurrentAccount();
            if (!BeginPrerequisiteIfNeeded())
            {
                registeringItem = 0;
                SetState(VerminionState.RegisteringMinions, "Inspecting inventory minions");
            }
            return;
        }
        if (!OwnsUnlockQuest() || !Plugin.PluginInterface
            .GetIpcSubscriber<bool>($"{progress.UnlockQuestProvider}.IsRunning").InvokeFunc())
            Fail("The owned unlock quest stopped before completion; use Resume to continue.");
    }

    private void StopOwnedUnlockQuest()
    {
        if (owner == 0 || owner != Plugin.PlayerState.ContentId) return;
        var progress = configManager.GetActiveConfig().VerminionProgress;
        if (progress.UnlockQuestId == 0) return;
        try
        {
            if (OwnsUnlockQuest())
                commandManager.ProcessCommand(progress.UnlockQuestProvider == "WigglyQuest" ? "/wqst stop" : "/qst stop");
            if (progress.UnlockPriorityInserted && progress.UnlockQuestProvider == "WigglyQuest")
            {
                var present = Plugin.PluginInterface.GetIpcSubscriber<string, bool>("WigglyQuest.IsQuestInPriority")
                    .InvokeFunc(progress.UnlockQuestId.ToString());
                if (present && !Plugin.PluginInterface.GetIpcSubscriber<string, bool>("WigglyQuest.RemovePriorityQuest")
                    .InvokeFunc(progress.UnlockQuestId.ToString())) return;
                progress.UnlockPriorityInserted = false;
                configManager.SaveCurrentAccount();
            }
        }
        catch (Exception ex) { log.Warning($"[Verminion] Owned quest cleanup remains pending: {ex.Message}"); }
    }

    private void ContinueWeeklyGoal()
    {
        var config = configManager.GetActiveConfig();
        var progress = config.VerminionProgress;
        if (campaign && progress.NextUnclearedChallenge == 25) { CompleteCampaign(); return; }
        if (campaign) challengeStage = progress.NextUnclearedChallenge;
        if (config.VerminionMode != runMode || Math.Clamp(config.VerminionVictoryTarget, 1, 1000) != victoryTarget)
        { Fail("Verminion settings changed; use Run with the new goal."); return; }
        progress.EnsureRun(runMode, victoryTarget);
        if (WeeklyGoalReached(config))
        {
            StopOwnedMovement();
            config.VerminionCompletedThisWeek = progress.WeeklyMatches >= 5;
            config.VerminionLastCompleted = DateTime.UtcNow;
            config.VerminionNextReset = ResetDetectionService.GetNextWeeklyReset(DateTime.UtcNow);
            configManager.SaveCurrentAccount();
            SetState(VerminionState.Complete, runMode == VerminionMode.WinTarget ? "Weekly victory target reached" : "Weekly participation complete");
            return;
        }
        if (!campaign && runMode == VerminionMode.CpuRewards)
        { Fail("CPU tournament registration and reward collection are not yet verified."); return; }
        if (!campaign && progress.WinningRunLimitReached)
        {
            config.VerminionPaused = true;
            configManager.SaveCurrentAccount();
            Fail("Winning attempt limit reached; use Resume after reviewing the strategy.");
            return;
        }
        if (!VerminionGameInteraction.PrepareOwnedPalette())
        { Fail("Three owned minions could not be verified on the palette. No purchases made."); return; }
        admissionConfirmed = false;
        admissionSnapshot = false;
        resultExitRequested = false;
        reconcilingSummonQueue = false;
        lastResultCredited = false;
        pendingTutorialMove = false;
        pendingTutorialAction = null;
        pendingGroupDestination = null;
        pendingSameTypeSelection = null;
        movementSnapshotUtc = DateTime.MinValue;
        pendingUnhandledPrompt = null;
        ordinaryBattleObserved = false;
        battlefieldRenderingPrepared = false;
        opening = new();
        stoneUnitOrders.Clear();
        ResetBossStrategy();
        nextObjectiveOrderUtc = DateTime.MinValue;
        openingStepUtc = DateTime.UtcNow;
        tutorialPromptFloor = VerminionGameInteraction.CurrentLogIndex();
        handledPrompt = string.Empty;
        SetState(VerminionState.ApproachingTable, $"Preparing Stage {challengeStage}; {ProgressSummary(config)}");
        configManager.SaveCurrentAccount();
    }

    private void PauseCampaignAtLimit()
    {
        configManager.GetActiveConfig().VerminionPaused = true;
        configManager.SaveCurrentAccount();
        Fail($"Stage {challengeStage} reached three attempts without a verified clear; use Resume after reviewing its strategy.");
    }

    private void CompleteCampaign()
    {
        configManager.GetActiveConfig().VerminionProgress.CampaignRequested = false;
        configManager.SaveCurrentAccount();
        VerminionGameInteraction.CloseChallengeMenu();
        StopOwnedMovement();
        SetState(VerminionState.Complete, "All 24 challenges are verified complete");
    }

    private void InspectChallengeProgress()
    {
        challengeLogCategorySelected = false;
        ownsChallengeLog = VerminionGameInteraction.OpenChallengeLog();
        if (!ownsChallengeLog) { Fail("Challenge Log unavailable."); return; }
        SetState(VerminionState.InspectingProgress, "Reading existing Challenge Log progress");
    }

    private void TravelToMinionSquare()
    {
        ownsTravel = lifestream.AethernetTeleportById(89);
        if (!ownsTravel) { Fail("Minion Square aethernet travel was rejected."); return; }
        SetState(VerminionState.TravellingToMinionSquare, "Travelling to Minion Square");
    }

    private void StopOwnedMovement()
    {
        StopOwnedUnlockQuest();
        ReleaseKey();
        if (pendingBattlefieldClick is { } screen)
            VerminionGameInteraction.SendBattlefieldClick(screen, release: true, pendingBattlefieldRightClick);
        pendingBattlefieldClick = null;
        VerminionGameInteraction.ReleaseBattlefieldInput();
        if (owner != 0 && owner == Plugin.PlayerState.ContentId)
        {
            if (ownsChallengeLog) VerminionGameInteraction.CloseChallengeLog();
            if (navigationRequested) navigation.Stop();
            if (ownsTravel) commandManager.ProcessCommand("/li stop");
        }
        navigationRequested = false;
        ownsTravel = false;
        ownsChallengeLog = false;
    }

    private void ReleaseKey()
    {
        if (heldKey is not { } key) return;
        heldKey = null;
        GameHelpers.KeyUp(key);
    }

    private unsafe bool TryMoveCamera(InputId direction = InputId.MOVE_FORE, double seconds = 1)
    {
        var input = UIInputData.Instance();
        var binding = input == null ? null : input->GetKeybind(direction);
        if (binding == null) return false;
        foreach (var setting in binding->KeySettings)
        {
            if ((byte)setting.Key == 0 || (byte)setting.KeyModifier != 0) continue;
            heldKey = (VirtualKey)setting.Key;
            releaseKeyUtc = DateTime.UtcNow.AddSeconds(seconds);
            GameHelpers.KeyDown(heldKey.Value);
            log.Information($"[VerminionControl] camera input dispatched using {direction} binding; awaiting tutorial readback");
            return true;
        }
        return false;
    }

    private void UpdateTutorialProof()
    {
        var now = DateTime.UtcNow;
        if (pendingBattlefieldClick is { } screen && now >= battlefieldClickReleaseUtc)
        {
            VerminionGameInteraction.SendBattlefieldClick(screen, release: true, pendingBattlefieldRightClick);
            pendingBattlefieldClick = null;
        }
        if (heldKey != null && now >= releaseKeyUtc) ReleaseKey();
        if (pendingUnhandledPrompt is { } unknown)
        {
            if (now < unhandledImageUtc) return;
            var captured = VerminionGameInteraction.CaptureTutorialImage();
            Fail($"Control proof stopped at tutorial instruction: {unknown}. Image {(captured ? "captured" : "unavailable")}; no result recorded.");
            return;
        }
        if (condition[ConditionFlag.BetweenAreas] || condition[ConditionFlag.BetweenAreas51])
        { ReleaseKey(); return; }
        if (now < nextPromptCheckUtc) return;
        nextPromptCheckUtc = now.AddSeconds(VerifyBattleGroup &&
            (pendingGroupDestination != null || pendingSameTypeSelection != null || pendingTutorialMove) ? 0.25 : 1);
        if (pendingGroupDestination is { } groupDestination)
        {
            var hatchlings = Plugin.ObjectTable.OfType<Dalamud.Game.ClientState.Objects.Types.IBattleNpc>()
                .Where(obj => obj.CurrentHp > 0 && obj.Name.TextValue == tutorialGroupName &&
                VerminionGameInteraction.IsFriendlyMinion(obj) && (groupOrigin == null || Vector3.DistanceSquared(obj.Position, groupOrigin.Value) < 100)).ToArray();
            if (hatchlings.Length == 0 && VerifyBattleGroup) { CancelLostBossGroup(); return; }
            if (VerifyBattleGroup) groupMinimum = Math.Min(groupMinimum, hatchlings.Length);
            if (hatchlings.Length < groupMinimum) { Fail("The battle group is unavailable; no result recorded."); return; }
            // Select inside the action party so the nearby-type click includes
            // its members. Reject obscured candidates using native hit readback.
            var first = singleUnitSelection is { } single
                ? hatchlings.FirstOrDefault(unit => unit.GameObjectId == single)
                : VerifyBattleGroup
                ? hatchlings.Where(obj => !rejectedGroupSelections.Contains(obj.GameObjectId))
                    .OrderByDescending(candidate => hatchlings.Count(unit => Vector3.DistanceSquared(unit.Position, candidate.Position) < 16))
                    .ThenBy(candidate => groupOrigin == null ? 0 : Vector3.DistanceSquared(candidate.Position, groupOrigin.Value))
                    .ThenByDescending(obj => obj.Position.Z).FirstOrDefault()
                : hatchlings.OrderBy(obj => obj.Position.X).FirstOrDefault();
            if (first == null)
            {
                if (IsBossStage && singleUnitSelection == null)
                {
                    CancelLostBossGroup();
                    log.Information("[VerminionControl] group obscured for this order; continue its existing attack and reassess at the strategy cadence");
                }
                else Fail("No selectable unit remains in the battle group; no result recorded.");
                return;
            }
            if (!VerminionGameInteraction.TryGetObjectCenter(first, out var center))
            { Fail("The tutorial unit's center is unavailable; no result recorded."); return; }
            var framed = TryFrameTutorialPoint(center, out var point);
            if (framed == null) return;
            if (framed == false) { Fail("Cannot frame the battle group; no result recorded."); return; }
            if (!groupSelectionCleared)
            {
                if (!VerminionGameInteraction.FindEmptyBattlefieldPoint(first.Position, out var empty) ||
                    !VerminionGameInteraction.SendBattlefieldClick(empty, release: false))
                { Fail("Cannot clear the previous selection; no result recorded."); return; }
                pendingBattlefieldClick = empty;
                pendingBattlefieldRightClick = false;
                battlefieldClickReleaseUtc = now.AddMilliseconds(150);
                groupSelectionCleared = true;
                return;
            }
            if (!VerminionGameInteraction.SendBattlefieldClick(point, release: false))
            { Fail("Cannot select the tutorial group; no result recorded."); return; }
            pendingGroupDestination = null;
            pendingBattlefieldClick = point;
            pendingBattlefieldRightClick = false;
            battlefieldClickReleaseUtc = now.AddMilliseconds(150);
            tutorialDestination = groupDestination;
            groupSelectionAnchor = first.GameObjectId;
            pendingSameTypeSelection = first.GameObjectId;
            return;
        }
        if (pendingTutorialAction is { } action)
        {
            if (!tutorialActionPanelsRestored)
            {
                VerminionGameInteraction.CaptureBattle("selection before palette restore");
                VerminionGameInteraction.ReleaseBattlefieldInput(restoreRendering: false);
                tutorialActionPanelsRestored = true;
                return;
            }
            pendingTutorialAction = null;
            VerminionGameInteraction.CaptureBattle("selection readback before special action");
            VerminionGameInteraction.CaptureTutorialImage();
            if (!VerminionGameInteraction.TryClickPaletteIcon(action))
            {
                Fail("Tutorial action control is unavailable; no result recorded.");
                return;
            }
            movementSnapshotUtc = now.AddSeconds(6);
            return;
        }
        if (pendingSameTypeSelection is { } objectId)
        {
            var selectedId = VerifyBattleGroup && !groupSingleSelectionVerified ? VerminionGameInteraction.ReadMinionInfoId() : objectId;
            var selected = Plugin.ObjectTable.FirstOrDefault(obj => obj.GameObjectId ==
                selectedId && obj.Name.TextValue == tutorialGroupName && VerminionGameInteraction.IsFriendlyMinion(obj) &&
                (singleUnitSelection == null || !requireExactSingleSelection || obj.GameObjectId == singleUnitSelection) &&
                (groupOrigin == null || Vector3.DistanceSquared(obj.Position, groupOrigin.Value) < 100));
            if (singleUnitSelection != null)
            {
                pendingSameTypeSelection = null;
                if (selected == null) { Fail("The bomb carrier selection could not be verified; no result recorded."); return; }
                groupSelectionAnchor = selected.GameObjectId;
                groupSelectionVerified = true;
                pendingTutorialMove = true;
                cameraAdjustments = 0;
                return;
            }
            if (VerifyBattleGroup && selected == null) { pendingSameTypeSelection = null; RetryBossGroupSelection(); return; }
            if (selected == null || !VerminionGameInteraction.TryGetObjectCenter(selected, out var center))
            { Fail("The selected tutorial unit is unavailable; no result recorded."); return; }
            var framed = TryFrameTutorialPoint(center, out var point);
            if (framed == null)
            { pendingSameTypeSelection = selected.GameObjectId; groupSingleSelectionVerified = true; return; }
            if (framed == false || !VerminionGameInteraction.SendBattlefieldClick(point, release: false))
            { Fail("Cannot frame or click the selected battle unit; no result recorded."); return; }
            pendingSameTypeSelection = null;
            pendingBattlefieldClick = point;
            groupSelectionAnchor = selected.GameObjectId;
            pendingBattlefieldRightClick = false;
            battlefieldClickReleaseUtc = now.AddMilliseconds(150);
            pendingTutorialMove = true;
            cameraAdjustments = 0;
            return;
        }
        if (pendingTutorialMove)
        {
            if (VerifyBattleGroup && !groupSelectionVerified)
            {
                var selectedId = VerminionGameInteraction.ReadMinionInfoId();
                if (!Plugin.ObjectTable.Any(obj => obj.GameObjectId == selectedId &&
                    obj.Name.TextValue == tutorialGroupName && VerminionGameInteraction.IsFriendlyMinion(obj)))
                { pendingTutorialMove = false; RetryBossGroupSelection(); return; }
                groupSelectionAnchor = selectedId;
                groupSelectionVerified = true;
            }
            if (groupAttackTarget is { } enemyId)
            {
                var enemy = Plugin.ObjectTable.OfType<Dalamud.Game.ClientState.Objects.Types.IBattleNpc>()
                    .FirstOrDefault(obj => obj.GameObjectId == enemyId && obj.CurrentHp > 0 && VerminionGameInteraction.IsEnemyMinion(obj));
                if (enemy == null)
                {
                    pendingTutorialMove = false;
                    groupAttackTarget = null;
                    ReleaseKey();
                    VerminionGameInteraction.ReleaseBattlefieldInput(restoreRendering: false);
                    log.Information("[VerminionControl] movement target died; restored battle controls before choosing another target");
                    return;
                }
                // Commands specify a ground destination; update it from the
                // live enemy after selection and camera movement.
                if (challengeStage == 12 && enemy.Name.TextValue == "Demon Brick")
                {
                    var facing = new Vector3(MathF.Sin(enemy.Rotation), 0, MathF.Cos(enemy.Rotation));
                    // LoVM orders use a ground destination. The elevated center
                    // of this tall model projects several yalms beyond its feet.
                    tutorialDestination = enemy.Position - facing * 0.8f;
                    var anchor = Plugin.ObjectTable.FirstOrDefault(unit => unit.GameObjectId == groupSelectionAnchor);
                    if (anchor != null && Vector3.Dot(anchor.Position - enemy.Position, facing) > -1)
                    {
                        // Route around its frontal crush before closing to melee.
                        var side = new Vector3(facing.Z, 0, -facing.X);
                        var sign = Vector3.Dot(anchor.Position - enemy.Position, side) < 0 ? -1 : 1;
                        tutorialDestination = enemy.Position + side * sign * 2 - facing * 2;
                    }
                }
                else tutorialDestination = enemy.Position;
                if (challengeStage == 15 && Plugin.ObjectTable.Any(unit => unit.GameObjectId == groupSelectionAnchor &&
                    Vector3.DistanceSquared(unit.Position, tutorialDestination) < 4))
                {
                    // Selecting the other party exposes its special. Do not
                    // interrupt an attack already in range just to select it.
                    pendingTutorialMove = false;
                    ReleaseKey();
                    VerminionGameInteraction.ReleaseBattlefieldInput(restoreRendering: false);
                    return;
                }
            }
            var framed = TryFrameTutorialPoint(tutorialDestination, out var destination);
            if (framed == null) return;
            pendingTutorialMove = false;
            VerminionGameInteraction.CaptureBattle("selection readback before movement", detailed: challengeStage == 1);
            // Destinations come from tutorial image readback, not EventObj 2005110.
            if (framed == false ||
                !VerminionGameInteraction.SendBattlefieldClick(destination, release: false, rightClick: true))
            { Fail("Tutorial destination unavailable; no result recorded."); return; }
            pendingBattlefieldClick = destination;
            pendingBattlefieldRightClick = true;
            battlefieldClickReleaseUtc = now.AddMilliseconds(150);
            if (challengeStage == 12 && groupAttackTarget != null)
                log.Information($"[VerminionControl] Stage 12 ground pursuit dispatched; position={tutorialDestination}; group={tutorialGroupName}");
            if (IsStoneStage && tutorialGroupName == opening.CurrentMinionName)
            {
                var anchor = Plugin.ObjectTable.FirstOrDefault(unit => unit.GameObjectId == groupSelectionAnchor);
                if (anchor != null)
                    foreach (var unit in Plugin.ObjectTable.Where(unit => VerminionGameInteraction.IsFriendlyMinion(unit) &&
                        unit.Name.TextValue == tutorialGroupName && Vector3.DistanceSquared(unit.Position, anchor.Position) < 100))
                        stoneUnitOrders[unit.GameObjectId] = now;
            }
            // Battle decisions continue while the group travels. Boss specials
            // are checked separately when the selected minion reaches an enemy.
            movementSnapshotUtc = now.AddSeconds(IsStoneStage || IsBossStage ? 1 : 6);
            return;
        }
        if (movementSnapshotUtc != DateTime.MinValue)
        {
            if (now < movementSnapshotUtc) return;
            movementSnapshotUtc = DateTime.MinValue;
            VerminionGameInteraction.ReleaseBattlefieldInput(restoreRendering: false);
            VerminionGameInteraction.CaptureBattle("after tutorial movement command", detailed: challengeStage == 1);
            return;
        }
        if (IsStoneStage) { UpdateStoneOpening(); return; }
        if (IsBossStage) { UpdateBossBattle(); return; }
        if (pendingGateSummon)
        {
            pendingGateSummon = false;
            VerminionGameInteraction.CaptureAddon("LovmPalette");
            if (!VerminionGameInteraction.TrySummonPaletteSlot(0))
                Fail("Gate selection sent but summon unavailable; no result recorded.");
            return;
        }
        if (pendingTutorialSummons > 0)
        {
            if (!VerminionGameInteraction.TrySummonPaletteSlot(0))
            { Fail("Tutorial summon queue is unavailable; no result recorded."); return; }
            --pendingTutorialSummons;
            return;
        }
        var prompt = VerminionGameInteraction.ReadTutorialPrompt(tutorialPromptFloor);
        if (string.IsNullOrEmpty(prompt) || prompt == handledPrompt)
        {
            var timeout = handledPrompt.Contains("Arcana Stone B", StringComparison.Ordinal) ? 180 : 60;
            if ((now - (handledPrompt.Length == 0 ? stateEnteredAt : promptHandledUtc)).TotalSeconds > timeout)
            {
                VerminionGameInteraction.CaptureBattle("tutorial instruction/readback timeout");
                Fail($"No new tutorial instruction after {timeout} seconds; no result recorded.");
            }
            return;
        }
        ReleaseKey();
        log.Information($"[VerminionControl] fresh tutorial instruction: {prompt}");
        var dispatched = prompt switch
        {
            "Use the movement keys to shift your viewpoint around." => TryMoveCamera(),
            "Select a minion from the minion hotbar." =>
                GameHelpers.TryGetAddonText("LovmPalette", 67, out var capacity) && capacity == "0/60" &&
                VerminionGameInteraction.TrySummonPaletteSlot(0),
            "Left-click on the “A” found on the display above the hotbar, and try summoning a minion from Gate A." =>
                pendingGateSummon = VerminionGameInteraction.TryClickPaletteIcon(73),
            "Summon several minions, and view them in the summoning queue." =>
                (pendingTutorialSummons = 3) > 0,
            "Select the hatchling, and move it inside the yellow circle. Left-click to select a minion, and right-click to select the destination." =>
                TrySelectTutorialHatchling(),
            "Left-click and drag the cursor to select all the hatchlings, then use right-click to move them to the yellow circle." =>
                TrySelectTutorialGroup(new Vector3(3, 0, 8)),
            "Select the wayward hatchlings and send them against your foes." => TryAttackTutorialFoes(),
            "Move the wayward hatchlings to the center of the field, and defeat the minions that threaten Arcana Stone B." => TryAttackTutorialFoes(),
            "Send the wayward hatchlings to defeat the behemoths." => TryAttackTutorialFoes("Baby Behemoth"),
            "Select an individual hatchling, then click the Execute Action button on the minion hotbar." => TryTutorialSpecial(),
            "Select all the wayward hatchlings, and return them to a gate to restore their HP." =>
                TrySendTutorialGroupToGate("Gate B"),
            "Select all the wayward hatchlings, then send them to another gate." =>
                TrySendTutorialGroupToGate("Gate A"),
            "Select the hatchling action party, and move them to the enemy's Arcana Stone B in the center of the field." =>
                TryAttackTutorialStone(),
            "Attack the enemy's Arcana Stone B." => TryAttackTutorialStone(),
            // Existing attackers continue damaging the stone during this narration.
            "Shatter Arcana Stone B completely." => true,
            "Destroy your opponent's Shield using the cherry bombs." => TryAttackTutorialShield(),
            _ => false,
        };
        if (!dispatched)
        {
            VerminionGameInteraction.CaptureBattle("unhandled tutorial instruction");
            VerminionGameInteraction.RequestTutorialImage();
            pendingUnhandledPrompt = prompt;
            unhandledImageUtc = now.AddSeconds(5);
            reason = "Inspecting the next tutorial instruction";
            return;
        }
        handledPrompt = prompt;
        promptHandledUtc = now;
        reason = $"Tutorial: {prompt}";
    }

    private bool TrySelectTutorialHatchling(bool move = true)
    {
        var foes = Plugin.ObjectTable.Where(obj => obj.Name.TextValue == "Baby Behemoth").ToArray();
        var hatchling = Plugin.ObjectTable.Where(obj => obj.Name.TextValue == "Wayward Hatchling")
            .OrderByDescending(obj => foes.Length == 0 ? obj.Position.X :
                foes.Min(foe => Vector3.DistanceSquared(obj.Position, foe.Position))).FirstOrDefault();
        if (hatchling == null) return false;
        Vector2 screen;
        if (!(move ? VerminionGameInteraction.ProjectBattlefield(hatchling.Position + new Vector3(0, 0.1f, 0), out screen) :
            VerminionGameInteraction.ProjectObjectCenter(hatchling, out screen))) return false;
        if (!VerminionGameInteraction.SendBattlefieldClick(screen, release: false)) return false;
        pendingBattlefieldClick = screen;
        pendingBattlefieldRightClick = false;
        battlefieldClickReleaseUtc = DateTime.UtcNow.AddMilliseconds(150);
        pendingTutorialMove = move;
        tutorialDestination = new Vector3(0, 0, 12);
        return true;
    }

    private bool TryTutorialSpecial()
    {
        if (!TrySelectTutorialHatchling(move: false)) return false;
        tutorialActionPanelsRestored = false;
        pendingTutorialAction = 82;
        return true;
    }

    private bool TrySelectTutorialGroup(Vector3 destination, string name = "Wayward Hatchling")
        => TrySelectGroup(destination, name, 4);

    private bool TrySelectGroup(Vector3 destination, string name, int minimum, Vector3? origin = null, ulong? attackTarget = null,
        ulong? singleUnit = null, bool requireExactSingle = true)
    {
        tutorialGroupName = name;
        groupMinimum = minimum;
        groupOrigin = origin;
        groupAttackTarget = attackTarget;
        singleUnitSelection = singleUnit;
        requireExactSingleSelection = requireExactSingle;
        groupSelectionCleared = false;
        rejectedGroupSelections.Clear();
        groupSelectionAnchor = 0;
        groupSelectionVerified = false;
        groupSingleSelectionVerified = false;
        pendingGroupDestination = destination;
        cameraAdjustments = 0;
        // The official controls also select nearby minions of the same type when
        // the already selected unit is clicked again.
        return true;
    }

    private void RetryBossGroupSelection()
    {
        rejectedGroupSelections.Add(groupSelectionAnchor);
        if (rejectedGroupSelections.Count >= 6)
        {
            if (IsBossStage && singleUnitSelection == null)
            {
                CancelLostBossGroup();
                log.Information("[VerminionControl] six obscured group candidates; leave existing orders active for this strategy interval");
            }
            else Fail("Could not verify a friendly selection after six group candidates; no result recorded.");
            return;
        }
        log.Information("[VerminionControl] boss group click selected no friendly minion; selecting another observed group member");
        VerminionGameInteraction.CaptureBattle("boss selection mismatch", detailed: rejectedGroupSelections.Count == 1);
        if (rejectedGroupSelections.Count == 1) VerminionGameInteraction.CaptureTutorialImage();
        groupSelectionCleared = false;
        groupSelectionVerified = false;
        groupSingleSelectionVerified = false;
        pendingGroupDestination = tutorialDestination;
        cameraAdjustments = 0;
    }

    private void CancelLostBossGroup()
    {
        pendingGroupDestination = null;
        pendingSameTypeSelection = null;
        pendingTutorialMove = false;
        groupAttackTarget = null;
        groupSelectionVerified = false;
        ReleaseKey();
        VerminionGameInteraction.ReleaseBattlefieldInput(restoreRendering: false);
        log.Information("[VerminionControl] boss group lost before its order; regrouping while preserving result observation");
    }

    private void UpdateStoneOpening()
    {
        if (challengeStage == 7 && groupSelectionVerified && DateTime.UtcNow >= nextBossSpecialUtc &&
            Plugin.ObjectTable.Any(unit => unit.GameObjectId == groupSelectionAnchor &&
                unit.Name.TextValue == opening.CurrentMinionName && VerminionGameInteraction.IsFriendlyMinion(unit) &&
                Plugin.ObjectTable.Any(target => (VerminionGameInteraction.IsEnemyMinion(target) || target.BaseId == 2006537) &&
                    Vector3.DistanceSquared(unit.Position, target.Position) < 25)) &&
            VerminionGameInteraction.IsPaletteActionReady(82))
        {
            nextBossSpecialUtc = DateTime.UtcNow.AddSeconds(3);
            if (VerminionGameInteraction.TryClickPaletteIcon(82))
            { log.Information("[VerminionControl] Stage 7 monster attack buff near objective/enemy"); return; }
        }
        if (opening.OpeningComplete) { ReinforceRemainingStone(); return; }
        if ((DateTime.UtcNow - openingStepUtc).TotalSeconds > 90)
        { Fail($"Stage {challengeStage} opening did not produce its expected units; no result recorded."); return; }
        var gate = Plugin.ObjectTable.FirstOrDefault(obj => obj.BaseId == 2006534 && obj.Name.TextValue == $"Gate {(char)('A' + opening.Gate)}");
        var stone = Plugin.ObjectTable.FirstOrDefault(obj => obj.BaseId == opening.TargetBaseId && obj.Name.TextValue == opening.TargetName);
        if (gate == null || stone == null || !VerminionGameInteraction.TryReadSummoningCapacity(out var used, out var capacity)) return;
        if (reconcilingSummonQueue) capacity = used;
        if (VerminionGameInteraction.ReadStoneHp(enemy: true, opening.EnemyLane) == 0 ||
            Plugin.ObjectTable.OfType<Dalamud.Game.ClientState.Objects.Types.IBattleNpc>()
                .Count(obj => obj.CurrentHp > 0 && VerminionGameInteraction.IsFriendlyMinion(obj) &&
                obj.Name.TextValue == opening.CurrentMinionName && Vector3.DistanceSquared(obj.Position, stone.Position) < 25) > 0)
        {
            // After reload the already-deployed group may have taken casualties.
            // Its surviving attackers prove this opening lane was dispatched;
            // replacement decisions follow once the other lanes are reconciled.
            log.Information($"[VerminionControl] Stage {challengeStage} opening reconciled existing attack or destroyed {opening.TargetName}");
            opening.Dispatched(VerminionBattleStrategy.Action.AttackObjective);
            openingStepUtc = DateTime.UtcNow;
            return;
        }
        var count = Plugin.ObjectTable.Count(obj => VerminionGameInteraction.IsFriendlyMinion(obj) &&
            obj.Name.TextValue == opening.CurrentMinionName && Vector3.DistanceSquared(obj.Position, gate.Position) < 100);
        var action = opening.Decide(VerminionGameInteraction.IsSummoningGateSelected(opening.Gate), count, used, capacity);
        var sent = action switch
        {
            VerminionBattleStrategy.Action.SelectGate => VerminionGameInteraction.TryClickPaletteIcon((uint)(73 + opening.Gate)),
            VerminionBattleStrategy.Action.Summon => VerminionGameInteraction.TrySummonPaletteSlot(VerminionGameInteraction.FindPaletteMinion(opening.CurrentMinion)),
            VerminionBattleStrategy.Action.AttackObjective => TrySelectGroup(stone.Position, opening.CurrentMinionName, opening.GroupSize, gate.Position),
            _ => true,
        };
        if (!sent) { Fail($"Stage {challengeStage} {action} unavailable; no result recorded."); return; }
        if (action != VerminionBattleStrategy.Action.Wait)
            log.Information($"[VerminionControl] Stage {challengeStage} order={action}; gate={opening.Gate}; units={count}; capacity={used}/{capacity}");
        if (action == VerminionBattleStrategy.Action.AttackObjective) openingStepUtc = DateTime.UtcNow;
        opening.Dispatched(action);
    }

    private void ReinforceRemainingStone()
    {
        reason = $"Stage {challengeStage}: observing objective attacks and waiting for an explicit result";
        var now = DateTime.UtcNow;
        var hp = Enumerable.Range(0, 3).Select(lane => VerminionGameInteraction.ReadStoneHp(true, lane)).ToArray();
        var remaining = VerminionBattleStrategy.ChooseRemainingStone(hp);
        if (remaining < 0) return;
        var targetName = $"Arcana Stone {(char)('A' + remaining)}";
        var target = Plugin.ObjectTable.FirstOrDefault(obj => obj.BaseId == 2006537 && obj.Name.TextValue == targetName);
        if (target == null) return;
        var canRequest = VerminionGameInteraction.TryReadSummoningCapacity(out var used, out var capacity) &&
            !reconcilingSummonQueue && opening.Reinforcements.CanRequest(used, capacity);
        var units = Plugin.ObjectTable.OfType<Dalamud.Game.ClientState.Objects.Types.IBattleNpc>()
            .Where(obj => VerminionGameInteraction.IsFriendlyMinion(obj) && obj.CurrentHp > 0 &&
                obj.Name.TextValue == opening.CurrentMinionName).ToArray();
        // A reload can leave late queue spawns at a gate after the opening has
        // moved on. Use those units as well as survivors at destroyed stones.
        var origins = Plugin.ObjectTable.Where(obj => obj.BaseId == 2006534 ||
            obj.BaseId == 2006537 && Enumerable.Range(0, 3).Any(lane => hp[lane] == 0 &&
                obj.Name.TextValue == $"Arcana Stone {(char)('A' + lane)}"))
            .Select(obj => new { Object = obj, Count = units.Count(unit =>
                (!stoneUnitOrders.TryGetValue(unit.GameObjectId, out var ordered) || (now - ordered).TotalSeconds >= 30) &&
                Vector3.DistanceSquared(unit.Position, obj.Position) < (obj.BaseId == 2006534 ? 100 : 25)) })
            .Where(group => group.Count > 0 && (group.Object.BaseId != 2006534 || group.Count >= 4 ||
                hp[remaining] <= 300 || !canRequest && opening.Reinforcements.Pending == 0))
            .OrderByDescending(group => group.Count);
        foreach (var group in origins)
        {
            if (now < nextObjectiveOrderUtc) break;
            TrySelectGroup(target.Position, opening.CurrentMinionName, 1, group.Object.Position);
            nextObjectiveOrderUtc = DateTime.UtcNow.AddSeconds(20);
            reason = $"Stage {challengeStage}: reinforce {targetName} from {group.Object.Name}";
            log.Information($"[VerminionControl] {reason}; survivors={group.Count}; targetHp={hp[remaining]}");
            return;
        }
        if (!canRequest) return;
        var gate = 2 - remaining;
        if (!VerminionGameInteraction.IsSummoningGateSelected(gate))
        {
            if (!VerminionGameInteraction.TryClickPaletteIcon((uint)(73 + gate)))
                Fail($"Stage {challengeStage} reinforcement gate unavailable; no result recorded.");
            return;
        }
        if (!VerminionGameInteraction.TrySummonPaletteSlot(VerminionGameInteraction.FindPaletteMinion(opening.CurrentMinion)))
        { Fail($"Stage {challengeStage} reinforcement summon unavailable; no result recorded."); return; }
        if (opening.Reinforcements.Pending == 0) ordinarySpawnUtc = now;
        opening.Reinforcements.Requested();
        reason = $"Stage {challengeStage}: summon replacements for {targetName}";
        log.Information($"[VerminionControl] {reason}; gate={gate}; pending={opening.Reinforcements.Pending}; capacity={used}/{capacity}");
    }

    private void ResetBossStrategy()
    {
        bossBurstStarted = false;
        bossContactCaptured = false;
        bossEnraged = false;
        bossArmyReady = false;
        odinRegrouping = odinRegrouped = false;
        odinRegroupUtc = DateTime.MinValue;
        bossStrategy = new(challengeStage);
        deployedBossUnits.Clear();
        bossOrderUtc = DateTime.MinValue;
        bossWaveUtc = DateTime.UtcNow;
        bossSpawnUtc = DateTime.UtcNow;
        bossDefenderSpawnUtc = DateTime.UtcNow;
        bossSupportSpawnUtc = DateTime.UtcNow;
        bossSupportOrderUtc = DateTime.MinValue;
        bossSupportWaveUtc = DateTime.MinValue;
        bossDefenseOrderUtc = DateTime.MinValue;
        bossDefenderWaveUtc = DateTime.MinValue;
        bossDefenseTarget = 0;
        bossOrderPosition = null;
        bossOrderTarget = 0;
        bossOrderRotation = 0;
        nextBossSpecialUtc = DateTime.MinValue;
        bombLeverOrderUtc = DateTime.MinValue;
        bombCarrierOrderUtc = DateTime.MinValue;
        bombDropUtc = DateTime.MinValue;
        bombTriggerUtc = DateTime.MinValue;
        bombCycles = 0;
    }

    private void UpdateBossBattle()
    {
        var now = DateTime.UtcNow;
        var boss = Plugin.ObjectTable.OfType<Dalamud.Game.ClientState.Objects.Types.IBattleNpc>()
            .Where(obj => VerminionGameInteraction.IsEnemyMinion(obj) && obj.CurrentHp > 0)
            // Stage 6's living Imps sustain the boss's invulnerability. Clear
            // them first; Smallshells are not a reason to leave the boss.
            .OrderByDescending(obj => VerminionBossStrategy.IsInvulnerabilityAdd(challengeStage, obj.Name.TextValue))
            .ThenByDescending(obj => obj.MaxHp).FirstOrDefault();
        if (boss == null) { reason = $"Stage {challengeStage}: waiting for the boss or explicit result"; return; }
        var gateIndex = new[] { 1, 0, 2 }.FirstOrDefault(VerminionGameInteraction.IsSummoningGateAvailable, -1);
        var gateAvailable = gateIndex >= 0 && !reconcilingSummonQueue;
        // Destroyed gates prevent reinforcement, not control of living units.
        if (gateIndex < 0) gateIndex = 1;
        var gate = Plugin.ObjectTable.FirstOrDefault(obj => obj.BaseId == 2006534 && obj.Name.TextValue == $"Gate {(char)('A' + gateIndex)}");
        if (gate == null || !VerminionGameInteraction.TryReadSummoningCapacity(out var used, out var capacity)) return;
        var units = Plugin.ObjectTable.OfType<Dalamud.Game.ClientState.Objects.Types.IBattleNpc>()
            .Where(obj => VerminionGameInteraction.IsFriendlyMinion(obj) && obj.CurrentHp > 0 &&
                obj.Name.TextValue == bossStrategy.CurrentMinionName).ToArray();
        var destination = boss.Position;
        var commandTargetName = boss.Name.TextValue;
        ulong? combatTarget = boss.GameObjectId;
        var bunkerStone = string.Empty;
        if (bossStrategy.DefendsStone)
        {
            var lane = VerminionGameInteraction.ReadStoneHp(false, 1) > 0 ? 1 :
                Enumerable.Range(0, 3).FirstOrDefault(index => VerminionGameInteraction.ReadStoneHp(false, index) > 0, -1);
            var stone = Plugin.ObjectTable.FirstOrDefault(obj => obj.BaseId == 2006533 &&
                obj.Name.TextValue == $"Arcana Stone {(char)('A' + lane)}");
            if (stone != null && lane >= 0)
            {
                var invaders = Plugin.ObjectTable.OfType<Dalamud.Game.ClientState.Objects.Types.IBattleNpc>()
                    .Where(enemy => VerminionGameInteraction.IsEnemyMinion(enemy) && enemy.CurrentHp > 0 &&
                        Vector3.DistanceSquared(enemy.Position, stone.Position) < 36).ToArray();
                var adds = invaders.Where(enemy => enemy.GameObjectId != boss.GameObjectId)
                    .OrderBy(enemy => Vector3.DistanceSquared(enemy.Position, stone.Position)).ToArray();
                var target = adds.Length >= 3
                    ? adds.FirstOrDefault(enemy => enemy.GameObjectId == bossOrderTarget) ?? adds[0]
                    : invaders.FirstOrDefault(enemy => enemy.GameObjectId == bossOrderTarget) ??
                        invaders.FirstOrDefault(enemy => enemy.GameObjectId == boss.GameObjectId) ?? adds.FirstOrDefault();
                destination = target?.Position ?? stone.Position;
                combatTarget = target?.GameObjectId;
                commandTargetName = target?.Name.TextValue ?? stone.Name.TextValue;
                bunkerStone = stone.Name.TextValue;
            }
        }
        if (challengeStage == 19)
        {
            // The guide's fallen phase grants ATK, DEF and SPD Up together.
            // Use the native effects, not a timer, to leave its stand-up blast.
            var enraged = boss.StatusList.Any(status => status.StatusId == 962) &&
                boss.StatusList.Any(status => status.StatusId == 964) &&
                boss.StatusList.Any(status => status.StatusId == 966);
            if (enraged != bossEnraged)
            {
                bossEnraged = enraged;
                VerminionGameInteraction.CaptureBattle($"Stage 19 {(enraged ? "buffed fallen phase" : "buffed phase ended")}");
                VerminionGameInteraction.CaptureTutorialImage();
            }
            if (enraged)
            {
                var retreat = gate.Position - boss.Position;
                retreat.Y = 0;
                if (retreat.LengthSquared() < 1) retreat = Vector3.UnitZ;
                destination = boss.Position + Vector3.Normalize(retreat) * 10;
                combatTarget = null;
                commandTargetName = "retreat from Gilgamesh's buffed attack";
            }
        }
        if (challengeStage == 9 && capacity > 60 && units.Length > 0 &&
            Plugin.ObjectTable.OfType<Dalamud.Game.ClientState.Objects.Types.IBattleNpc>().Any(unit =>
                VerminionGameInteraction.IsEnemyMinion(unit) && unit.Name.TextValue == "Slime Puddle" &&
                unit.CurrentHp > 0 && unit.StatusList.Any(status => status.StatusId == 981)))
        {
            if (bombTriggerUtc != DateTime.MinValue)
            {
                if ((now - bombTriggerUtc).TotalSeconds < 5) return;
                VerminionGameInteraction.CaptureBattle("Stage 9 bomb detonation readback");
                bombTriggerUtc = bombDropUtc = bombCarrierOrderUtc = bombLeverOrderUtc = DateTime.MinValue;
                if (++bombCycles >= 6)
                    Fail("Stage 9 still has invulnerable slimes after six bomb cycles; no result recorded.");
                return;
            }
            if (VerminionGameInteraction.IsPaletteActionReady(81))
            {
                if (!VerminionGameInteraction.TryClickPaletteIcon(81))
                { Fail("Stage 9 trap trigger unavailable; no result recorded."); return; }
                bombTriggerUtc = now;
                log.Information("[VerminionControl] Stage 9 armed trap triggered; awaiting slime phase evidence");
                return;
            }
            if (bombDropUtc != DateTime.MinValue)
            {
                if ((now - bombDropUtc).TotalSeconds <= 15)
                {
                    // Fill the normal bounded queue while the bomb arms; these
                    // damage groups handle the vulnerable final slime phase.
                    if (reconcilingSummonQueue) return;
                    var preparation = bossStrategy.Decide(VerminionGameInteraction.IsSummoningGateSelected(1), 0, 0,
                        used, capacity, 0, 0, false);
                    if (preparation == VerminionBossStrategy.Action.SelectGate)
                        VerminionGameInteraction.TryClickPaletteIcon(74);
                    else if (preparation == VerminionBossStrategy.Action.Summon &&
                        VerminionGameInteraction.TrySummonPaletteSlot(VerminionGameInteraction.FindPaletteMinion(bossStrategy.CurrentMinion)))
                    {
                        if (bossStrategy.PendingSummons == 0) bossSpawnUtc = now;
                        bossStrategy.Dispatched(preparation);
                    }
                    return;
                }
                VerminionGameInteraction.CaptureBattle("Stage 9 trap did not arm");
                Fail("Stage 9 placed trap was not ready within 15 seconds; no result recorded.");
                return;
            }
            var carrier = units.FirstOrDefault(unit => unit.StatusList.Any(status => status.StatusId == 987 && status.Param == 515));
            if (carrier != null)
            {
                if (groupSelectionVerified && groupSelectionAnchor == carrier.GameObjectId &&
                    Vector3.DistanceSquared(carrier.Position, boss.Position) < 4 && VerminionGameInteraction.IsPaletteActionReady(82))
                {
                    if (!VerminionGameInteraction.TryClickPaletteIcon(82))
                    { Fail("Stage 9 bomb placement action unavailable; no result recorded."); return; }
                    bombDropUtc = now;
                    log.Information("[VerminionControl] Stage 9 carrier action dispatched near slime; awaiting bomb placement evidence");
                    return;
                }
                if (bombCarrierOrderUtc == DateTime.MinValue || (now - bombCarrierOrderUtc).TotalSeconds >= 20)
                {
                    TrySelectGroup(boss.Position, bossStrategy.CurrentMinionName, 1, attackTarget: boss.GameObjectId, singleUnit: carrier.GameObjectId);
                    bombCarrierOrderUtc = now;
                    log.Information("[VerminionControl] Stage 9 move the verified Trapper carrier to slime");
                }
                return;
            }
            var lever = Plugin.ObjectTable.FirstOrDefault(obj => obj.BaseId == 2005108);
            if (lever == null) { Fail("Stage 9 bomb lever is unavailable; no result recorded."); return; }
            if (bombLeverOrderUtc == DateTime.MinValue)
            {
                // The previous carrier can be obscured by the split slimes.
                // Use an exposed reserve; any verified friendly Alphinaud is
                // suitable before pickup, while an actual Trapper must match.
                var runner = units.OrderByDescending(unit => unit.Position.Z).First();
                TrySelectGroup(lever.Position, bossStrategy.CurrentMinionName, 1,
                    singleUnit: runner.GameObjectId, requireExactSingle: false);
                bombLeverOrderUtc = now;
                log.Information("[VerminionControl] Stage 9 move to observed bomb-lever object");
            }
            else if ((now - bombLeverOrderUtc).TotalSeconds > 65)
                Fail("Stage 9 group did not reach the bomb lever within 65 seconds; no result recorded.");
            return;
        }
        var defenders = Plugin.ObjectTable.OfType<Dalamud.Game.ClientState.Objects.Types.IBattleNpc>()
            .Where(obj => HasBossDefenders && VerminionGameInteraction.IsFriendlyMinion(obj) && obj.CurrentHp > 0 &&
                obj.Name.TextValue == VerminionBossStrategy.DefenderName).ToArray();
        var support = Plugin.ObjectTable.OfType<Dalamud.Game.ClientState.Objects.Types.IBattleNpc>()
            .Where(obj => challengeStage == 15 && VerminionGameInteraction.IsFriendlyMinion(obj) && obj.CurrentHp > 0 &&
                obj.Name.TextValue == VerminionBossStrategy.MinionName).ToArray();
        if (challengeStage == 15 && !bossArmyReady)
        {
            // The opening needs all three action parties together. Preserve
            // already-deployed armies when reloading an ongoing battle.
            var gates = Plugin.ObjectTable.Where(obj => obj.BaseId == 2006534).ToArray();
            bossArmyReady = units.Length >= 4 && defenders.Length >= 4 && support.Length >= 4 ||
                gates.Length > 0 && units.Concat(defenders).Concat(support).Any(unit =>
                    gates.All(spawn => Vector3.DistanceSquared(unit.Position, spawn.Position) >= 100));
        }
        if (challengeStage == 15 && capacity > 60)
        {
            if (!odinRegrouped && !odinRegrouping && boss.CurrentHp <= boss.MaxHp / 2 && boss.CurrentHp > boss.MaxHp / 4)
            {
                odinRegrouping = true;
                odinRegroupUtc = now;
                bossOrderUtc = bossDefenseOrderUtc = bossSupportOrderUtc = DateTime.MinValue;
                VerminionGameInteraction.CaptureBattle("Stage 15 regroup at gate before final damage check");
            }
            if (odinRegrouping && (now - odinRegroupUtc).TotalSeconds >= 20 &&
                units.Length >= 4 && defenders.Length >= 4 && support.Length >= 4 &&
                units.Concat(defenders).Concat(support).All(unit => unit.CurrentHp >= unit.MaxHp * 0.95f &&
                    Vector3.DistanceSquared(unit.Position, gate.Position) < 36))
            {
                odinRegrouping = false;
                odinRegrouped = true;
                deployedBossUnits.Clear();
                bossOrderUtc = bossDefenseOrderUtc = bossSupportOrderUtc = bossDefenderWaveUtc = bossSupportWaveUtc = DateTime.MinValue;
                bossWaveUtc = now.AddSeconds(-60);
                VerminionGameInteraction.CaptureBattle("Stage 15 twelve healed minions ready for final attack");
            }
            if (odinRegrouping)
            {
                if ((now - odinRegroupUtc).TotalSeconds > 90)
                { Fail("Stage 15 could not regroup and heal its action parties within 90 seconds; no result recorded."); return; }
                destination = gate.Position;
                combatTarget = null;
                commandTargetName = "heal and assemble at Gate B";
            }
        }
        var selected = groupSelectionVerified
            ? units.Concat(defenders).Concat(support).FirstOrDefault(obj => obj.GameObjectId == groupSelectionAnchor) : null;
        var selectedDefender = selected?.Name.TextValue == VerminionBossStrategy.DefenderName;
        if (challengeStage == 12 && !bossContactCaptured && selected != null &&
            Vector3.DistanceSquared(selected.Position, boss.Position) < 36)
        {
            bossContactCaptured = VerminionGameInteraction.CaptureTutorialImage();
            log.Information($"[VerminionControl] Stage 12 contact; selected={tutorialGroupName}; verified={groupSelectionVerified}; actionReady={VerminionGameInteraction.IsPaletteActionReady(82)}; image={bossContactCaptured}");
        }
        // Odin destroys every stone after his final cast. Concentrate the
        // attack-buff party so its special supports the Haurchefant attackers.
        if (challengeStage == 15 && !odinRegrouping && !bossBurstStarted && capacity > 60 &&
            boss.CurrentHp <= boss.MaxHp / 4 && units.Length >= 4)
        {
            VerminionGameInteraction.CaptureBattle("Stage 15 final damage phase");
            var anchor = units.OrderByDescending(candidate => units.Count(unit =>
                    Vector3.DistanceSquared(unit.Position, candidate.Position) < 25))
                .ThenBy(unit => Vector3.DistanceSquared(unit.Position, boss.Position)).First();
            TrySelectGroup(boss.Position, bossStrategy.CurrentMinionName, 1, anchor.Position, boss.GameObjectId);
            bossBurstStarted = true;
            log.Information($"[VerminionControl] Stage 15 final burst; targetHp={boss.CurrentHp}/{boss.MaxHp}; attackers={units.Length}");
            return;
        }
        var savingOdinBuff = challengeStage == 15 && selected?.Name.TextValue == bossStrategy.CurrentMinionName &&
            boss.CurrentHp <= boss.MaxHp / 2 && boss.CurrentHp > boss.MaxHp / 4;
        if (!odinRegrouping && !savingOdinBuff && now >= nextBossSpecialUtc && selected != null &&
            (selectedDefender ? Plugin.ObjectTable.Any(enemy => VerminionGameInteraction.IsEnemyMinion(enemy) &&
                Vector3.DistanceSquared(selected.Position, enemy.Position) < 36) :
                Vector3.DistanceSquared(selected.Position, boss.Position) < (bossStrategy.CurrentMinion == 546 ? 100 : 25) &&
                !boss.StatusList.Any(status => status.StatusId == 981)) &&
            VerminionGameInteraction.IsPaletteActionReady(82))
        {
            nextBossSpecialUtc = now.AddSeconds(3);
            if (VerminionGameInteraction.TryClickPaletteIcon(82))
            {
                log.Information($"[VerminionControl] Stage {challengeStage} {(selectedDefender ? "defensive" : "offensive")} special; party={selected.Name}; target={boss.Name}; targetHp={boss.CurrentHp}/{boss.MaxHp}");
                return;
            }
        }
        // An older build may already be fighting when this strategy is loaded.
        // Finish observing that battle with its existing palette; every new
        // Defender stages prepare their palette outside battle first.
        var useDefenders = HasBossDefenders && VerminionGameInteraction.FindPaletteMinion(VerminionBossStrategy.DefenderMinion) >= 0;
        if (challengeStage == 15 && units.Length >= 4 &&
            UpdateOdinSupport(boss, support, gate.Position, used, capacity, gateIndex, gateAvailable)) return;
        if (useDefenders && UpdateBossDefenders(gate.Position, defenders, units.Length, used, capacity, gateIndex, gateAvailable)) return;
        var rally = Plugin.ObjectTable.Where(obj => obj.BaseId == 2006534)
            .Select(spawnGate => new { spawnGate.Position, Units = units.Where(unit =>
                !deployedBossUnits.Contains(unit.GameObjectId) && Vector3.DistanceSquared(unit.Position, spawnGate.Position) < 100).ToArray() })
            .OrderByDescending(group => group.Units.Length).FirstOrDefault();
        var ready = rally?.Units ?? Array.Empty<Dalamud.Game.ClientState.Objects.Types.IBattleNpc>();
        var deployed = units.Except(ready).ToArray();
        var waveSeconds = (now - bossWaveUtc).TotalSeconds;
        var orderSeconds = (now - bossOrderUtc).TotalSeconds;
        var turned = challengeStage == 12 && combatTarget == boss.GameObjectId && bossOrderPosition != null &&
            MathF.Cos(boss.Rotation - bossOrderRotation) < 0.7f;
        var moved = bossOrderTarget != (combatTarget ?? 0) || bossOrderPosition == null ||
            turned ||
            Vector3.DistanceSquared(destination, bossOrderPosition.Value) >= 16 ||
            // Reconcile actual arrival, including units stranded by a prior
            // command. Most of the deployed army should be near its target.
            orderSeconds >= 15 && deployed.Count(unit => Vector3.DistanceSquared(unit.Position, destination) < 36) < deployed.Length / 2;
        // Let a party already in melee finish its attacks. Repeated ground
        // orders and switching between nearby adds interrupt that engagement.
        if (bossStrategy.DefendsStone && !turned && combatTarget == bossOrderTarget && deployed.Length > 0 &&
            deployed.Count(unit => Vector3.DistanceSquared(unit.Position, destination) < 4) >= (deployed.Length + 1) / 2)
            moved = false;
        var action = bossStrategy.Decide(VerminionGameInteraction.IsSummoningGateSelected(gateIndex), ready.Length,
            deployed.Length, used, useDefenders ? bossStrategy.AttackCapacity(capacity, defenders.Length, support.Length) : capacity,
            waveSeconds, bossStrategy.DefendsStone && orderSeconds >= 3 ? Math.Max(10, orderSeconds) : orderSeconds, moved, gateAvailable);
        if (challengeStage == 15 && !bossArmyReady && action is VerminionBossStrategy.Action.SendWave or VerminionBossStrategy.Action.FollowBoss)
            action = VerminionBossStrategy.Action.Wait;
        var sent = true;
        switch (action)
        {
            case VerminionBossStrategy.Action.SelectGate:
                sent = VerminionGameInteraction.TryClickPaletteIcon((uint)(73 + gateIndex));
                break;
            case VerminionBossStrategy.Action.Summon:
                sent = VerminionGameInteraction.TrySummonPaletteSlot(VerminionGameInteraction.FindPaletteMinion(bossStrategy.CurrentMinion));
                break;
            case VerminionBossStrategy.Action.SendWave:
                sent = TrySelectGroup(destination, bossStrategy.CurrentMinionName, Math.Min(bossStrategy.WaveSize, ready.Length), rally?.Position ?? gate.Position, combatTarget);
                foreach (var unit in ready) deployedBossUnits.Add(unit.GameObjectId);
                bossWaveUtc = now;
                bossOrderUtc = now;
                bossOrderPosition = destination;
                bossOrderTarget = combatTarget ?? 0;
                break;
            case VerminionBossStrategy.Action.FollowBoss:
                // Lead the main group to a new phase target. Selecting the unit
                // already nearest an add can leave the army attacking an
                // invincible boss while repeatedly ordering one isolated minion.
                var anchor = deployed.OrderByDescending(candidate => deployed.Count(unit =>
                        Vector3.DistanceSquared(unit.Position, candidate.Position) < 25))
                    .ThenBy(obj => Vector3.DistanceSquared(obj.Position, destination)).First();
                if (bossStrategy.DefendsStone && combatTarget == null)
                    anchor = deployed.OrderByDescending(unit => Vector3.DistanceSquared(unit.Position, destination)).First();
                var selectedAnchor = units.FirstOrDefault(unit => unit.GameObjectId == groupSelectionAnchor);
                if (bossStrategy.DefendsStone && groupSelectionVerified && tutorialGroupName == bossStrategy.CurrentMinionName &&
                    selectedAnchor != null && Vector3.DistanceSquared(selectedAnchor.Position, anchor.Position) < 9)
                {
                    groupAttackTarget = combatTarget;
                    tutorialDestination = destination;
                    pendingTutorialMove = true;
                    cameraAdjustments = 0;
                }
                else sent = TrySelectGroup(destination, bossStrategy.CurrentMinionName, 1, anchor.Position, combatTarget);
                bossOrderUtc = now;
                bossOrderPosition = destination;
                bossOrderTarget = combatTarget ?? 0;
                break;
        }
        if (!sent) { Fail($"Stage {challengeStage} {action} unavailable; no result recorded."); return; }
        if (action is VerminionBossStrategy.Action.SendWave or VerminionBossStrategy.Action.FollowBoss)
            bossOrderRotation = boss.Rotation;
        if (action != VerminionBossStrategy.Action.Wait)
            log.Information($"[VerminionControl] Stage {challengeStage} order={action}; target={commandTargetName}; ready={ready.Length}; deployed={deployed.Length}; bossHp={boss.CurrentHp}/{boss.MaxHp}; capacity={used}/{capacity}");
        if (action == VerminionBossStrategy.Action.Summon && !bossStrategy.WaitingForWave)
        { bossWaveUtc = now; bossSpawnUtc = now; }
        bossStrategy.Dispatched(action);
        reason = $"Stage {challengeStage}: {(bunkerStone.Length > 0 ? $"defend {bunkerStone}; " : string.Empty)}{boss.Name} {boss.CurrentHp}/{boss.MaxHp} HP; {units.Length} damage minions";
        if (odinRegrouping) reason = "Stage 15: regrouping and healing all three action parties at Gate B";
    }

    private bool UpdateOdinSupport(Dalamud.Game.ClientState.Objects.Types.IBattleNpc boss,
        Dalamud.Game.ClientState.Objects.Types.IBattleNpc[] support, Vector3 gate, int used, int capacity, int gateIndex, bool gateAvailable)
    {
        var now = DateTime.UtcNow;
        if (capacity <= 60) return false;
        if (gateAvailable && bossStrategy.CanRequestSupport(support.Length, used, capacity))
        {
            if (!VerminionGameInteraction.IsSummoningGateSelected(gateIndex))
            {
                if (!VerminionGameInteraction.TryClickPaletteIcon((uint)(73 + gateIndex))) Fail("Stage 15 support gate unavailable; no result recorded.");
                return true;
            }
            if (!VerminionGameInteraction.TrySummonPaletteSlot(VerminionGameInteraction.FindPaletteMinion(VerminionBossStrategy.Minion)))
            { Fail("Stage 15 support summon unavailable; no result recorded."); return true; }
            if (bossStrategy.Support.Pending == 0) bossSupportSpawnUtc = now;
            bossStrategy.Support.Requested();
            log.Information($"[VerminionControl] Stage 15 summon Alphinaud support; living={support.Length}; pending={bossStrategy.Support.Pending}; capacity={used}/{capacity}");
            return true;
        }
        if (!bossArmyReady || support.Length == 0) return false;
        var ready = support.Where(unit => Vector3.DistanceSquared(unit.Position, gate) < 100).ToArray();
        var sendWave = ready.Length > 0 && (now - bossSupportWaveUtc).TotalSeconds >= 15 &&
            (ready.Length >= 4 || bossStrategy.Support.Pending == 0);
        if (!sendWave && (now - bossSupportOrderUtc).TotalSeconds < 20) return false;
        var candidates = sendWave ? ready : support.Except(ready).ToArray();
        if (candidates.Length == 0) return false;
        var anchor = candidates.OrderByDescending(candidate => candidates.Count(unit =>
            Vector3.DistanceSquared(unit.Position, candidate.Position) < 25)).First();
        TrySelectGroup(odinRegrouping ? gate : boss.Position, VerminionBossStrategy.MinionName, 1, anchor.Position,
            odinRegrouping ? null : boss.GameObjectId);
        if (sendWave) bossSupportWaveUtc = now;
        else bossSupportOrderUtc = now;
        log.Information($"[VerminionControl] Stage 15 select Alphinaud damage/DEF-down party; living={support.Length}; ready={ready.Length}");
        return true;
    }

    private bool UpdateBossDefenders(Vector3 gate, Dalamud.Game.ClientState.Objects.Types.IBattleNpc[] defenders,
        int attackers, int used, int capacity, int gateIndex, bool gateAvailable)
    {
        if (capacity <= 60 || attackers < bossStrategy.WaveSize && defenders.Length == 0) return false;
        if (VerminionGameInteraction.FindPaletteMinion(VerminionBossStrategy.DefenderMinion) < 0)
        { Fail($"Stage {challengeStage} defender palette must be prepared outside the current battle; no result recorded."); return true; }
        var now = DateTime.UtcNow;
        var lane = VerminionGameInteraction.ReadStoneHp(false, 1) > 0 ? 1 :
            Enumerable.Range(0, 3).FirstOrDefault(index => VerminionGameInteraction.ReadStoneHp(false, index) > 0, -1);
        var stone = Plugin.ObjectTable.FirstOrDefault(obj => obj.BaseId == 2006533 && obj.Name.TextValue == $"Arcana Stone {(char)('A' + lane)}");
        if (lane < 0 || stone == null) return false;
        var threats = Plugin.ObjectTable.OfType<Dalamud.Game.ClientState.Objects.Types.IBattleNpc>()
            .Where(enemy => VerminionGameInteraction.IsEnemyMinion(enemy) && enemy.CurrentHp > 0 &&
                (challengeStage == 15 || Vector3.DistanceSquared(enemy.Position, stone.Position) < 100)).ToArray();
        var threat = threats.FirstOrDefault(enemy => enemy.GameObjectId == bossDefenseTarget) ??
            threats.OrderBy(enemy => Vector3.DistanceSquared(enemy.Position, stone.Position)).FirstOrDefault();
        var ready = defenders.Where(unit => Vector3.DistanceSquared(unit.Position, gate) < 100).ToArray();
        var canSummon = gateAvailable && bossStrategy.CanRequestDefender(defenders.Length, used, capacity);
        if ((challengeStage != 15 || bossArmyReady) && ready.Length > 0 && (now - bossDefenderWaveUtc).TotalSeconds >= 15 &&
            (ready.Length >= 4 || bossStrategy.Defenders.Pending == 0 && !canSummon))
        {
            TrySelectGroup(odinRegrouping ? gate : threat?.Position ?? stone.Position, VerminionBossStrategy.DefenderName, 1, gate,
                odinRegrouping ? null : threat?.GameObjectId);
            bossDefenderWaveUtc = now;
            log.Information($"[VerminionControl] Stage {challengeStage} send Haurchefants to {threat?.Name ?? stone.Name}; ready={ready.Length}");
            return true;
        }
        if (canSummon)
        {
            if (!VerminionGameInteraction.IsSummoningGateSelected(gateIndex))
            {
                if (!VerminionGameInteraction.TryClickPaletteIcon((uint)(73 + gateIndex))) Fail($"Stage {challengeStage} defender gate unavailable; no result recorded.");
                return true;
            }
            if (!VerminionGameInteraction.TrySummonPaletteSlot(VerminionGameInteraction.FindPaletteMinion(VerminionBossStrategy.DefenderMinion)))
            { Fail($"Stage {challengeStage} defender summon unavailable; no result recorded."); return true; }
            if (bossStrategy.Defenders.Pending == 0) bossDefenderSpawnUtc = now;
            bossStrategy.Defenders.Requested();
            log.Information($"[VerminionControl] Stage {challengeStage} summon defender; living={defenders.Length}; pending={bossStrategy.Defenders.Pending}; capacity={used}/{capacity}");
            return true;
        }
        // Refresh the defensive group's position and select it for its native
        // defensive special when enemies threaten the remaining stone.
        var deployed = defenders.Except(ready).ToArray();
        if ((challengeStage != 15 || bossArmyReady) && deployed.Length > 0 && threat != null &&
            (now - bossDefenseOrderUtc).TotalSeconds >= (threat.GameObjectId == bossDefenseTarget ? challengeStage == 15 ? 20 : 30 : 8))
        {
            var anchor = deployed.OrderBy(unit => Vector3.DistanceSquared(unit.Position, stone.Position)).First();
            if (challengeStage == 15)
                anchor = deployed.OrderByDescending(candidate => deployed.Count(unit =>
                    Vector3.DistanceSquared(unit.Position, candidate.Position) < 25)).First();
            TrySelectGroup(odinRegrouping ? gate : threat.Position, VerminionBossStrategy.DefenderName, 1, anchor.Position,
                odinRegrouping ? null : threat.GameObjectId);
            bossDefenseOrderUtc = now;
            bossDefenseTarget = threat.GameObjectId;
            log.Information($"[VerminionControl] Stage {challengeStage} defend {stone.Name}; intercept={threat.Name}; defenders={defenders.Length}");
            return true;
        }
        return false;
    }

    // null means a bounded camera adjustment is in flight; false is unavailable.
    private bool? TryFrameTutorialPoint(Vector3 world, out Vector2 point)
    {
        var projected = VerminionGameInteraction.ProjectBattlefield(world, out point);
        if (projected && VerminionGameInteraction.IsBattlefieldPointVisible(point)) return true;
        if (cameraAdjustments == 0 && (challengeStage is 9 or 12 or 19 or 23) && VerminionGameInteraction.TryFocusMiniMap(world, out var mapClick))
        {
            pendingBattlefieldClick = mapClick;
            pendingBattlefieldRightClick = false;
            battlefieldClickReleaseUtc = DateTime.UtcNow.AddMilliseconds(150);
            nextPromptCheckUtc = DateTime.UtcNow.AddSeconds(0.5);
            ++cameraAdjustments;
            return null;
        }
        var direction = VerminionGameInteraction.FramingDirection(point);
        if (direction == null || cameraAdjustments >= 16 || !TryMoveCamera(direction.Value, 0.5)) return false;
        ++cameraAdjustments;
        nextPromptCheckUtc = DateTime.UtcNow.AddSeconds(0.75);
        return null;
    }

    private bool TryAttackTutorialFoes(string name = "Kidragora")
    {
        var foe = Plugin.ObjectTable.FirstOrDefault(obj => obj.Name.TextValue == name);
        return foe != null && TrySelectTutorialGroup(foe.Position);
    }

    private bool TrySendTutorialGroupToGate(string name)
    {
        var gate = Plugin.ObjectTable.FirstOrDefault(obj => obj.BaseId == 2006534 && obj.Name.TextValue == name);
        return gate != null && VerminionGameInteraction.TryGetObjectCenter(gate, out var center) &&
            TrySelectTutorialGroup(center);
    }

    private bool TryAttackTutorialStone()
    {
        var stone = Plugin.ObjectTable.FirstOrDefault(obj => obj.BaseId == 2006537 && obj.Name.TextValue == "Arcana Stone B");
        return stone != null && (Plugin.ObjectTable.Any(obj => obj.Name.TextValue == "Wayward Hatchling" &&
            Vector3.DistanceSquared(obj.Position, stone.Position) < 9) || TrySelectTutorialGroup(stone.Position));
    }

    private bool TryAttackTutorialShield()
    {
        var shield = Plugin.ObjectTable.FirstOrDefault(obj => obj.BaseId == 2006539);
        return shield != null && TrySelectTutorialGroup(shield.Position + new Vector3(0, 0, 2), "Cherry Bomb");
    }

    private void Fail(string message)
    {
        AbandonPendingMatch();
        StopOwnedMovement();
        if (campaign && owner != 0 && owner == Plugin.PlayerState.ContentId)
        {
            var config = configManager.GetActiveConfig();
            if (config.VerminionProgress.CampaignStageLimitReached(challengeStage))
            {
                config.VerminionPaused = true;
                configManager.SaveCurrentAccount();
                message += " Campaign attempt limit reached; review the strategy before Resume.";
            }
        }
        SetState(VerminionState.Failed, message);
    }

    private void AbandonPendingMatch()
    {
        if (owner == 0 || !configManager.Accounts.TryGetValue(ownerAccount, out var account) ||
            !account.Characters.TryGetValue(ownerCharacter, out var config)) return;
        var progress = config.VerminionProgress;
        if (progress.PendingMatch == 0) return;
        progress.AbandonMatch();
        configManager.SaveAccount(ownerAccount);
    }

    private void SetState(VerminionState state, string message)
    {
        log.Information($"[Verminion] {State} -> {state}: {message}");
        State = state;
        reason = message;
        stateEnteredAt = DateTime.UtcNow;
    }
}
