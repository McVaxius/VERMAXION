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
public sealed partial class VerminionService : IDisposable
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
    private bool ownsMinionShop;
    private bool ownsTournamentInspection;
    private VerminionVendorMinion? purchasingMinion;
    private Vector3? minionVendorDestination;
    private bool minionPurchaseSent;
    private bool minionPurchaseConfirmationSent;
    private bool minionShopCaptured;
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
    private int battlefieldZoomSteps;
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
    private DateTime bossDefenseOrderUtc;
    private DateTime bossDefenderWaveUtc;
    private ulong bossDefenseTarget;
    private Vector3? bossOrderPosition;
    private ulong bossOrderTarget;
    private float bossOrderRotation;
    private DateTime bossSpecialReadbackUtc;
    private DateTime nextBossSpecialUtc;
    private DateTime bombLeverOrderUtc;
    private DateTime bombCarrierOrderUtc;
    private DateTime bombDropUtc;
    private DateTime bombTriggerUtc;
    private int bombCycles;
    private readonly HashSet<uint> observedFinalStageCasts = new();
    private readonly HashSet<uint> observedFinalStageObjects = new();
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
    private bool groupSelectionOnly;
    private bool preserveObscuredGroup;
    private readonly HashSet<ulong> bossSpecialProbes = new();
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
        Idle, UnlockingPrerequisite, RegisteringMinions, ShoppingForMinion, ClosingMinionShop, TravellingToMinionVendor, TravellingToSaucer, TravellingToMinionSquare, ApproachingTable, InspectingTournament,
        InspectingControls, SelectingTutorial, WaitingForTutorial, InDuty, LeavingResult, InspectingProgress, Complete, Failed,
    }

    public VerminionState State { get; private set; }
    public int CurrentAttempt => configManager.GetActiveConfig().VerminionProgress.RunAttempts;
    public bool IsActive => State is not (VerminionState.Idle or VerminionState.Complete or VerminionState.Failed);
    public bool IsComplete => State == VerminionState.Complete;
    public bool IsFailed => State == VerminionState.Failed;
    private bool IsStoneStage => VerminionBattleStrategy.IsStoneStage(challengeStage);
    private bool IsBossStage => challengeStage is 4 or 6 or 9 or 12 or 15 or 19 or 23 or 24;
    private bool HasBossDefenders => VerminionRoster.HasDefenders(challengeStage);
    private bool VerifyBattleGroup => IsBossStage || IsStoneStage;
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
        VerminionGameInteraction.ConfigureBackgroundInput(() => IsActive && owner != 0 &&
            owner == Plugin.PlayerState.ContentId && !configManager.GetActiveConfig().VerminionPaused);
        Plugin.DutyState.DutyCompleted += OnDutyCompleted;
    }

    public void Start()
    {
        if (IsActive) return;
        if (HasQuestAcquisition)
        { reason = "Questionable acquisition owns the handoff; wait for it or use FULL STOP."; return; }
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
        if (progress.PendingPurchase != null && !ReconcileMinionPurchase())
        {
            if (TryResumeMinionPurchase()) return;
            Fail("A previous minion purchase is unresolved. Item and currency evidence must agree before another purchase or run.");
            return;
        }
        progress.ObserveWeek(ResetDetectionService.GetLastWeeklyReset(DateTime.UtcNow));
        progress.EnsureRun(config.VerminionMode, config.VerminionVictoryTarget);
        var pendingQueue = progress.PendingDuty is >= 552 and <= 575 &&
            VerminionGameInteraction.HasChallengeQueue((int)progress.PendingDuty - 551);
        if (progress.PendingMatch != 0 && currentDuty != progress.PendingDuty && !pendingQueue)
            progress.AbandonMatch();
        configManager.SaveCurrentAccount();
        // A reload can leave the challenge menu open after the target is met.
        // Do not bypass the goal check by resuming that menu. Active admissions
        // still reconcile their existing result before completion is considered.
        if (currentDuty == 0 && !pendingQueue && progress.PendingMatch == 0 && TryCompleteWeeklyGoal()) return;
        if (!campaign && runMode == VerminionMode.CpuRewards && currentDuty == 0 && !pendingQueue &&
            TournamentWorldRequirement() is { } worldRequirement)
        { Fail(worldRequirement); return; }
        if (!campaign && runMode == VerminionMode.CpuRewards && currentDuty == 0 && !pendingQueue &&
            VerminionGameInteraction.IsSetupMenu)
        {
            // An open ordinary challenge menu must not bypass CPU-mode setup.
            VerminionGameInteraction.CloseChallengeMenu();
            Fail("CPU tournament registration and reward collection are not yet verified; no ordinary challenge substituted.");
            return;
        }
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
        tutorialPromptFloor = currentDuty == 552 ? -1 : VerminionGameInteraction.CurrentLogIndex();
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
            // Reload may observe a cancelled or manually created queue. Only
            // the saved admission authorizes resuming its Commence/result path.
            if (progress.PendingMatch == 0 || progress.PendingDuty != 551 + challengeStage)
            { Fail("Verminion queue has no matching saved admission; no result recorded."); return; }
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

    internal static int PlannedStage(CharacterConfig config, bool challenges) =>
        challenges ? config.VerminionProgress.NextUnclearedChallenge :
        (config.VerminionProgress.ClearedChallengeMask & 1) == 0 ? 1 : 2;

    private bool CheckRoster(int stage)
    {
        if (stage == 24)
        {
            var observe = false;
#if DEBUG
            // The existing character-bound reload selection opts into bounded
            // development observation; ordinary/release runs retain the gate.
            var selection = Plugin.PluginInterface.GetPluginConfig() as Configuration;
            observe = selection?.DebugTaskId == "verminion_queue" &&
                selection.DebugTaskCharacterKey == configManager.CurrentCharacterKey;
#endif
            if (!observe)
            { Fail("Stage 24 tower assignment and dangerous-attack handling are not implemented; no admission requested."); return false; }
            log.Information("[Verminion] Selected Stage 24 debug observation: bounded to 120 seconds; tower/attack strategy remains unverified");
        }
        var config = configManager.GetActiveConfig();
        if (!VerminionRoster.NeedsBattleRoster(stage, campaign, runMode, config.VerminionProgress.ClearedChallengeMask)) return true;
        var missing = VerminionRoster.Missing(stage, VerminionGameInteraction.OwnsMinion);
        if (missing != null)
        {
            var requirements = VerminionRoster.Required(stage);
            var requirement = requirements.FirstOrDefault(minion => VerminionGameInteraction.OwnsMinion(minion.Id) != true);
            var offer = requirement?.Id switch
            {
                2 => VerminionRoster.MammetOffer,
                3 => VerminionRoster.HatchlingOffer,
                26 => VerminionRoster.BatOffer,
                83 => VerminionRoster.ZuOffer,
                174 => VerminionRoster.NeroOffer,
                _ => null,
            };
            var missingVendorMinion = offer != null && VerminionGameInteraction.OwnsMinion(offer.MinionId) == false;
            var canBuy = missingVendorMinion &&
                config.VerminionProgress.CanSpend(offer!.Gil, offer.Mgp,
                    config.VerminionGilPurchaseCap, config.VerminionMgpPurchaseCap);
            if (canBuy && Plugin.ClientState.TerritoryType == 388)
            {
                if (!GameHelpers.TrySelectStringExact("Return", out _))
                { Fail("Could not leave the challenge list for the Minion Trader; no purchase submitted."); return false; }
                BeginMinionPurchase(offer!);
                return false;
            }
            if (missingVendorMinion && !canBuy)
                missing += offer!.Mgp > 0
                    ? $" Cumulative MGP cap: {config.VerminionMgpPurchaseCap:N0}; spent: {config.VerminionProgress.MgpSpent:N0}. Register an owned item or set a cap of at least {config.VerminionProgress.MgpSpent + offer.Mgp:N0} MGP."
                    : $" Cumulative gil cap: {config.VerminionGilPurchaseCap:N0}; spent: {config.VerminionProgress.GilSpent:N0}. Register an owned item or set a cap of at least {config.VerminionProgress.GilSpent + offer.Gil:N0} gil.";
            Fail(missing);
            return false;
        }
        foreach (var minion in VerminionRoster.Required(stage))
            if (!VerminionGameInteraction.PrepareOwnedMinion(minion.Id))
            { Fail($"Stage {stage}: add {minion.Name} to the Verminion palette or free a slot. No admission requested."); return false; }
        return true;
    }

    public void RunTask() => RunGoal(false);
    public void RunChallenges() => RunGoal(true);
    public void ResumeTask() => RunGoal(configManager.GetActiveConfig().VerminionProgress.CampaignRequested);

    private void RunGoal(bool clearChallenges)
    {
        if (IsActive || HasQuestAcquisition) return;
        var config = configManager.GetActiveConfig();
        if (config.VerminionPaused)
        {
            config.VerminionProgress.EnsureRun(config.VerminionMode, config.VerminionVictoryTarget, restart: true);
            if (clearChallenges) config.VerminionProgress.CampaignStageAttempts = 0;
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
        VerminionGameInteraction.DisposeBackgroundInput();
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
        UpdateQuestAcquisition();
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
                    if (DateTime.UtcNow < nextInteractionUtc) return;
                    nextInteractionUtc = DateTime.UtcNow.AddSeconds(1);
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
                    var itemStatus = GameHelpers.GetItemActionStatus(minionItem);
                    if (itemStatus != 0)
                    { reason = $"Waiting for minion item {minionItem} to become usable (game status {itemStatus})"; return; }
                    if (!VerminionGameInteraction.TryRegisterInventoryMinion(minionItem))
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
                case VerminionState.ShoppingForMinion:
                    if (elapsed >= 10 && !minionShopCaptured)
                    { minionShopCaptured = true; VerminionGameInteraction.CaptureMinionShop(); }
                    if (elapsed > 180) { Fail("Minion vendor setup or purchase timed out; any pending reservation is retained."); return; }
                    UpdateMinionShop();
                    return;
                case VerminionState.TravellingToMinionVendor:
                    if (elapsed > 120) { Fail("Camp Bronze Lake travel did not complete; existing aetheryte access is required. No purchase submitted."); return; }
                    if (elapsed < 3 || !TravelSettled() || Plugin.ClientState.TerritoryType != purchasingMinion?.Territory) return;
                    ownsTravel = false;
                    SetState(VerminionState.ShoppingForMinion, "Approaching Junkmonger Nonoroon at Poor Maid's Mill");
                    return;
                case VerminionState.ClosingMinionShop:
                    if (elapsed > 10) { Fail("The owned minion shop did not close."); return; }
                    if (!CloseOwnedMinionShop()) return;
                    registeringItem = 0;
                    SetState(VerminionState.RegisteringMinions, "Registering the acquired minion");
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
                case VerminionState.InspectingTournament:
                    UpdateTournamentInspection(elapsed);
                    return;
                case VerminionState.InspectingControls:
                    if (elapsed < 3) return;
                    if (GameHelpers.TrySelectStringExact("Verminion Challenge", out _))
                        SetState(VerminionState.SelectingTutorial, $"Selecting Stage {challengeStage}");
                    else if (elapsed > 10) Fail("The verified Verminion Challenge menu is unavailable.");
                    return;
                case VerminionState.SelectingTutorial:
                    if (elapsed < 2) return;
                    if (TryCompleteWeeklyGoal()) return;
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
                    // Refresh the game's sequential unlocks before applying the roster gate.
                    if (!CheckRoster(challengeStage)) return;
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
                        var resultDuty = VerminionGameInteraction.CurrentCpuDutyId();
                        lastResultCredited = resultProgress.PendingDuty == resultDuty &&
                            (resultDuty == 552
                                ? resultProgress.RecordTutorialCompletion(resultProgress.PendingMatch, lastBattleOutcome)
                                : resultProgress.RecordResult(resultProgress.PendingMatch, lastBattleOutcome,
                                    ResetDetectionService.GetLastWeeklyReset(DateTime.UtcNow)));
                        if (lastResultCredited)
                        {
                            configManager.SaveCurrentAccount();
                        }
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
                        if (challengeStage == 24)
                        {
                            // Bounded development observation of Bahamut's native
                            // casts and tower objects; no inferred mechanics yet.
                            if (elapsed > 120)
                            { Fail("Stage 24 phase observation ended; tower and attack handling still need verification. No result recorded."); return; }
                            var newCast = false;
                            foreach (var unit in Plugin.ObjectTable.OfType<Dalamud.Game.ClientState.Objects.Types.IBattleNpc>())
                                if (unit.IsCasting && unit.CastActionId != 0 && observedFinalStageCasts.Count < 8)
                                    newCast |= observedFinalStageCasts.Add(unit.CastActionId);
                            var newObject = false;
                            foreach (var obj in Plugin.ObjectTable.Where(obj => obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventObj))
                                if (observedFinalStageObjects.Count < 24) newObject |= observedFinalStageObjects.Add(obj.BaseId);
                            if (newCast || newObject || DateTime.UtcNow >= nextInteractionUtc)
                            {
                                nextInteractionUtc = DateTime.UtcNow.AddSeconds(15);
                                VerminionGameInteraction.CaptureBattle(newCast ? "Stage 24 new native cast" :
                                    newObject ? "Stage 24 new field object" : "Stage 24 bounded phase observation");
                            }
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
                                    .Where(obj => VerminionGameInteraction.IsFriendlyMinion(obj) && obj.Name.TextValue == bossStrategy.CurrentDefenderName)
                                    .Select(obj => obj.GameObjectId));
                                if (guards > 0) bossDefenderSpawnUtc = DateTime.UtcNow;
                                if (bossStrategy.Defenders.Pending > 0 && (DateTime.UtcNow - bossDefenderSpawnUtc).TotalSeconds > 90)
                                { Fail($"Stage {challengeStage} defenders did not spawn within 90 seconds; no result recorded."); return; }
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
                            if (HasBossDefenders)
                            {
                                var guards = bossStrategy.Defenders.ObserveUnits(Plugin.ObjectTable
                                    .OfType<Dalamud.Game.ClientState.Objects.Types.IBattleNpc>()
                                    .Where(obj => VerminionGameInteraction.IsFriendlyMinion(obj) && obj.Name.TextValue == bossStrategy.CurrentDefenderName)
                                    .Select(obj => obj.GameObjectId));
                                if (guards > 0) bossDefenderSpawnUtc = DateTime.UtcNow;
                                if (bossStrategy.Defenders.Pending > 0 && (DateTime.UtcNow - bossDefenderSpawnUtc).TotalSeconds > 90)
                                { Fail($"Stage {challengeStage} defenders did not spawn within 90 seconds; no result recorded."); return; }
                            }
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
                        battlefieldZoomSteps = 0;
                        nextPromptCheckUtc = DateTime.UtcNow.AddSeconds(1);
                        return;
                    }
                    if (challengeStage == 19) ObserveGilgameshPhase();
                    if (bossSpecialReadbackUtc != DateTime.MinValue && DateTime.UtcNow >= bossSpecialReadbackUtc)
                    {
                        bossSpecialReadbackUtc = DateTime.MinValue;
                        VerminionGameInteraction.CaptureBattle($"Stage {challengeStage} special effect readback", detailed: false);
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
        // Older development saves used this field for Hildibrand. A matching
        // quest ID alone does not authorize taking over a native chain.
        if (VerminionRoster.GentlemanQuests.Contains(progress.UnlockQuestId)) return false;
        return progress.UnlockQuestId != 0 && progress.UnlockQuestProvider is "WigglyQuest" or "Questionable" &&
            Plugin.PluginInterface.GetIpcSubscriber<string?>($"{progress.UnlockQuestProvider}.GetCurrentQuestId")
                .InvokeFunc() == progress.UnlockQuestId.ToString();
    }

    private bool BeginPrerequisiteIfNeeded()
    {
        var progress = configManager.GetActiveConfig().VerminionProgress;
        var next = new ushort[] { 434, 435, 1431 }.FirstOrDefault(id => !QuestManager.IsQuestComplete(id));
        if (VerminionRoster.GentlemanQuests.Contains(progress.UnlockQuestId))
        {
            try { ReconcileLegacyAcquisitionMetadata(); }
            catch (Exception ex) { Fail(ex.Message); return true; }
        }
        if (progress.UnlockQuestId != 0 && progress.UnlockQuestId != next)
        {
            StopOwnedUnlockQuest();
            if (progress.UnlockPriorityInserted)
            { Fail("The previous unlock priority entry must be released before continuing."); return true; }
            progress.UnlockQuestId = 0;
            progress.UnlockQuestProvider = string.Empty;
            configManager.SaveCurrentAccount();
        }
        if (next == 0)
        {
            try
            {
                if (ChocoboRaceService.QuestIpcPrefix is { } provider && QuestCall<bool>(provider, "IsRunning"))
                { Fail("Questionable already owns the character; finish or stop its run before resuming Verminion."); return true; }
            }
            catch (Exception ex) { Fail($"Questionable ownership is unavailable: {ex.Message}"); return true; }
            return false;
        }
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
            var unsyncedLevel = VerminionGameInteraction.ReadUnsyncedJobLevel();
            if (unsyncedLevel == null)
            { Fail("Current job level is unavailable; no unlock quest started."); return true; }
            if (unsyncedLevel < quest.ClassJobLevel[0])
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
        if (progress.UnlockQuestId == 0 || VerminionRoster.GentlemanQuests.Contains(progress.UnlockQuestId)) return;
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
        if (TryCompleteWeeklyGoal()) return;
        if (!campaign && runMode == VerminionMode.CpuRewards)
        { BeginTournamentInspection(); return; }
        if (!campaign && progress.WinningRunLimitReached)
        {
            config.VerminionPaused = true;
            configManager.SaveCurrentAccount();
            Fail("Winning attempt limit reached; use Resume after reviewing the strategy.");
            return;
        }
        if (!VerminionGameInteraction.TryReadOwnedMinions(out var ownedMinions))
        { Fail("Registered minion data is unavailable; no entry purchase or admission requested."); return; }
        var entryPurchases = VerminionRoster.EntryPurchases(ownedMinions);
        if (entryPurchases.Length > 0)
        {
            var entryGil = (uint)entryPurchases.Sum(offer => (long)offer.Gil);
            if (!progress.CanSpend(entryGil, 0, config.VerminionGilPurchaseCap, config.VerminionMgpPurchaseCap) ||
                !VerminionGameInteraction.TryReadPurchaseInventory(entryPurchases[0].ItemId, out var gil, out _, out _) ||
                !VerminionProgress.PreservesGilReserve(gil, entryGil, config.VerminionGilReserve))
            {
                Fail($"Entry requires three registered minions; {ownedMinions.Length} owned. " +
                    $"Register owned inventory items or allow {entryGil:N0} gil within the remaining cumulative cap for " +
                    $"{string.Join(", ", entryPurchases.Select(offer => offer.Name))}, leaving at least {config.VerminionGilReserve:N0} gil. No purchase submitted.");
                return;
            }
            BeginMinionPurchase(entryPurchases[0]);
            return;
        }
        if (!VerminionGameInteraction.PrepareOwnedPalette())
        { Fail("Three registered minions exist, but their palette slots could not be prepared. Free a slot or add them manually. No admission requested."); return; }
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
        var missing = VerminionRoster.Missing(challengeStage, VerminionGameInteraction.OwnsMinion);
        Fail($"Stage {challengeStage} reached three attempts without a verified clear; use Resume after reviewing its strategy." +
            (missing == null ? string.Empty : " " + missing));
    }

    private void BeginTournamentInspection()
    {
        if (TournamentWorldRequirement() is { } worldRequirement)
        { Fail(worldRequirement); return; }
#if DEBUG
        var selection = Plugin.PluginInterface.GetPluginConfig() as Configuration;
        if (selection?.DebugTaskId == "verminion_queue" &&
            selection.DebugTaskCharacterKey == configManager.CurrentCharacterKey &&
            Plugin.ClientState.TerritoryType == 388)
        {
            ownsTournamentInspection = false;
            SetState(VerminionState.InspectingTournament, "Inspecting the Tournament Recordkeeper; registration and rewards remain unverified");
            return;
        }
#endif
        Fail("CPU tournament registration and reward collection are not yet verified.");
    }

    internal static string? TournamentWorldRequirement()
    {
        var player = Plugin.ObjectTable.LocalPlayer;
        if (player == null || player.HomeWorld.RowId == 0 || player.CurrentWorld.RowId == 0)
            return "Current and Home World information is unavailable; tournament eligibility cannot be verified.";
        return player.HomeWorld.RowId == player.CurrentWorld.RowId ? null :
            "CPU tournaments require your Home World. Return there, then use Run weekly goal; no tournament interaction requested.";
    }

    private void UpdateTournamentInspection(double elapsed)
    {
        // ENpcBase1011594 uses Verminion event2949121. The similarly named
        //1010479 belongs to Triple Triad and must never be used here.
        const uint recordkeeperId = 1011594;
        if (TournamentWorldRequirement() is { } worldRequirement)
        { Fail(worldRequirement); return; }
        if (elapsed > 60 || Plugin.ClientState.TerritoryType != 388)
        {
            VerminionGameInteraction.CaptureNearbyObjects();
            Fail("Tournament menu inspection ended without readable evidence; no registration or reward claim made.");
            return;
        }
        if (ownsTournamentInspection)
        {
            if (DateTime.UtcNow < nextInteractionUtc) return;
            VerminionGameInteraction.CaptureNearbyObjects();
            VerminionGameInteraction.CaptureAddon("SelectYesno");
            VerminionGameInteraction.CaptureAddon("LovmRanking");
            var dialogVisible = Plugin.TargetManager.Target?.BaseId == recordkeeperId &&
                new[] { "SelectString", "SelectIconString", "Talk", "SelectYesno", "LovmRanking" }.Any(GameHelpers.IsAddonVisible);
            Fail(dialogVisible
                ? "Tournament Recordkeeper dialog observed for control verification; no registration, battle or reward claim made."
                : "Tournament Recordkeeper interaction produced no visible dialog; registration, battles and rewards remain unverified.");
            return;
        }
        var player = Plugin.ObjectTable.LocalPlayer;
        var npc = Plugin.ObjectTable.FirstOrDefault(obj => obj.BaseId == recordkeeperId && obj.IsTargetable);
        if (player == null || !GameHelpers.IsPlayerAvailable()) return;
        if (npc == null) { Fail("The Verminion Tournament Recordkeeper is not loaded."); return; }
        if (Vector3.Distance(player.Position, npc.Position) > GameHelpers.GetValidInteractionDistance(npc))
        {
            if (!navigationRequested) navigationRequested = navigation.PathfindAndMoveTo(npc.Position);
            return;
        }
        if (navigationRequested) { navigation.Stop(); navigationRequested = false; }
        if (!GameHelpers.TargetAndInteractByDataId(recordkeeperId, "Tournament Recordkeeper")) return;
        ownsTournamentInspection = true;
        nextInteractionUtc = DateTime.UtcNow.AddSeconds(3);
    }

    private bool TryCompleteWeeklyGoal()
    {
        var config = configManager.GetActiveConfig();
        if (campaign || !WeeklyGoalReached(config)) return false;
        StopOwnedMovement();
        VerminionGameInteraction.CloseChallengeMenu();
        config.VerminionCompletedThisWeek = config.VerminionProgress.WeeklyMatches >= 5;
        config.VerminionLastCompleted = DateTime.UtcNow;
        config.VerminionNextReset = ResetDetectionService.GetNextWeeklyReset(DateTime.UtcNow);
        configManager.SaveCurrentAccount();
        SetState(VerminionState.Complete, runMode == VerminionMode.WinTarget ? "Weekly victory target reached" : "Weekly participation complete");
        return true;
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
        if (ownsTournamentInspection && owner != 0 && owner == Plugin.PlayerState.ContentId &&
            Plugin.TargetManager.Target?.BaseId == 1011594 && GameHelpers.IsAddonVisible("SelectString"))
            GameHelpers.TryCloseAddonByCallback("SelectString");
        ownsTournamentInspection = false;
        CloseOwnedMinionShop();
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

    private bool ReconcileMinionPurchase()
    {
        var progress = configManager.GetActiveConfig().VerminionProgress;
        var pending = progress.PendingPurchase;
        if (pending == null) return true;
        if (!VerminionGameInteraction.TryReadPurchaseInventory(pending.ItemId, out var gil, out var mgp, out var count) ||
            !progress.ConfirmPurchase(gil, mgp, count, VerminionGameInteraction.OwnsMinion(pending.MinionId) == true)) return false;
        if (!configManager.TrySaveAccount(ownerAccount))
        {
            progress.GilSpent -= pending.Gil;
            progress.MgpSpent -= pending.Mgp;
            progress.PendingPurchase = pending;
            return false;
        }
        log.Information($"[Verminion] Minion purchase verified once: item={pending.ItemId}; gil={pending.Gil}; mgp={pending.Mgp}");
        return true;
    }

    private void BeginMinionPurchase(VerminionVendorMinion offer)
    {
        purchasingMinion = offer;
        minionVendorDestination = null;
        minionPurchaseSent = minionPurchaseConfirmationSent = false;
        minionShopCaptured = false;
        SetState(VerminionState.ShoppingForMinion, $"Preparing a capped {offer.Name} purchase at {offer.VendorName}");
    }

    private bool TryResumeMinionPurchase()
    {
        var pending = configManager.GetActiveConfig().VerminionProgress.PendingPurchase;
        if (pending == null || VerminionRoster.VendorOffer(pending.ItemId, pending.MinionId) is not { } offer ||
            pending.Gil != offer.Gil || pending.Mgp != offer.Mgp ||
            !VerminionGameInteraction.IsMinionPurchaseConfirmation(offer) ||
            !VerminionGameInteraction.TryReadMinionOffer(offer, out _, out _) ||
            !VerminionGameInteraction.TryReadPurchaseInventory(offer.ItemId, out var gil, out var mgp, out var items) ||
            gil != pending.GilBefore || mgp != pending.MgpBefore || items != pending.ItemsBefore ||
            VerminionGameInteraction.OwnsMinion(offer.MinionId) != false) return false;
        // Adopt only the already visible, exactly matched confirmation. Never
        // issue another Shop callback for this saved reservation.
        BeginMinionPurchase(offer);
        ownsMinionShop = minionPurchaseSent = true;
        log.Information("[Verminion] Resuming the visible reserved minion purchase confirmation; no repeat shop submission");
        return true;
    }

    private void UpdateMinionShop()
    {
        if (DateTime.UtcNow < nextInteractionUtc) return;
        nextInteractionUtc = DateTime.UtcNow.AddSeconds(1);
        var offer = purchasingMinion;
        if (offer == null) { Fail("No minion purchase was selected; no shop command issued."); return; }
        var config = configManager.GetActiveConfig();
        var progress = config.VerminionProgress;
        if (progress.PendingPurchase != null)
        {
            if (Plugin.ClientState.TerritoryType != offer.Territory)
            { Fail("The reserved minion purchase left its vendor territory; no further shop commands issued."); return; }
            if (ReconcileMinionPurchase())
            { SetState(VerminionState.ClosingMinionShop, "Closing the verified minion purchase"); return; }
            if (!minionPurchaseSent)
            { Fail("Unresolved purchase will not be submitted again."); return; }
            if (!minionPurchaseConfirmationSent && GameHelpers.IsAddonVisible("SelectYesno"))
            {
                if (!VerminionGameInteraction.IsMinionPurchaseConfirmation(offer))
                { VerminionGameInteraction.CaptureMinionShop(); Fail($"Minion purchase confirmation did not match {offer.Name} and its verified {offer.Currency} price."); return; }
                // A cap may have been lowered while the confirmation was open.
                var pending = progress.PendingPurchase;
                if (progress.GilSpent + pending.Gil > config.VerminionGilPurchaseCap ||
                    progress.MgpSpent + pending.Mgp > config.VerminionMgpPurchaseCap)
                { Fail("Purchase cap changed before confirmation; reservation retained for reconciliation."); return; }
                if (!VerminionGameInteraction.TryReadPurchaseInventory(offer.ItemId, out var remainingGil, out var remainingMgp, out var remainingItems) ||
                    remainingGil != pending.GilBefore || remainingMgp != pending.MgpBefore || remainingItems != pending.ItemsBefore ||
                    !VerminionProgress.PreservesGilReserve(remainingGil, pending.Gil, config.VerminionGilReserve))
                { Fail("Purchase inventory or gil reserve changed before confirmation; reservation retained for reconciliation."); return; }
                minionPurchaseConfirmationSent = GameHelpers.TryClickNativeButton("SelectYesno", offer.Mgp > 0 ? "Yes" : "OK", 8);
            }
            reason = "Waiting for both minion acquisition and the exact currency deduction; purchase will not repeat";
            return;
        }
        if (minionPurchaseSent) { Fail("Purchase state changed unexpectedly; no repeat submitted."); return; }
        if (!VerminionGameInteraction.TryReadPurchaseInventory(offer.ItemId, out var gil, out var mgp, out var items))
        { Fail("Inventory or currencies are unavailable; no purchase submitted."); return; }
        var owned = VerminionGameInteraction.OwnsMinion(offer.MinionId);
        if (owned == null) { Fail("Minion ownership is unavailable; no purchase submitted."); return; }
        if (owned == true || items > 0)
        { SetState(VerminionState.ClosingMinionShop, "Minion already available; purchase skipped"); return; }
        if (!progress.CanSpend(offer.Gil, offer.Mgp, config.VerminionGilPurchaseCap, config.VerminionMgpPurchaseCap) ||
            gil < offer.Gil || mgp < offer.Mgp ||
            !VerminionProgress.PreservesGilReserve(gil, offer.Gil, config.VerminionGilReserve))
        { Fail($"{offer.Name} needs {offer.Price:N0} {offer.Currency} within the remaining cumulative cap and funds, leaving the configured gil reserve. No purchase submitted."); return; }
        if (VerminionGameInteraction.IsSetupMenu)
        {
            var exit = VerminionGameInteraction.IsChallengeMenu ? "Return" : "Cancel";
            if (!GameHelpers.TrySelectStringExact(exit, out _))
            { VerminionGameInteraction.CaptureMinionShop(); Fail("Could not close the Verminion table menu before shopping; no purchase submitted."); }
            return;
        }
        if (Plugin.ClientState.TerritoryType != offer.Territory)
        {
            if (offer.MinionId != 26)
            { Fail("Minion purchase left Minion Square; no further shop commands issued."); return; }
            if (!VerminionGameInteraction.HasNonoroonFlightAccess())
            { Fail("Automatic Baby Bat travel needs existing flight access in Upper La Noscea. Acquire it from Nonoroon and Resume; no character progression started."); return; }
            if (!TravelSettled()) return;
            ownsTravel = lifestream.ExecuteCommand("/li Camp Bronze Lake");
            if (!ownsTravel) { Fail("Camp Bronze Lake travel was rejected; no purchase submitted."); return; }
            SetState(VerminionState.TravellingToMinionVendor, "Travelling to Camp Bronze Lake for Baby Bat");
            return;
        }
        if (GameHelpers.IsAddonVisible(offer.Shop))
        {
            if (!ownsMinionShop || !VerminionGameInteraction.TryReadMinionOffer(offer, out var index, out var price))
            { VerminionGameInteraction.CaptureMinionShop(); Fail("Minion vendor item, price or shop ownership could not be verified; no purchase submitted."); return; }
            if (!progress.ReservePurchase(offer.ItemId, offer.MinionId,
                    offer.Gil, offer.Mgp, gil, mgp, items, false, config.VerminionGilPurchaseCap, config.VerminionMgpPurchaseCap, config.VerminionGilReserve))
            { Fail("Minion purchase could not reserve its budget."); return; }
            var reserved = progress.PendingPurchase;
            if (!configManager.TrySaveAccount(ownerAccount))
            { Fail("Purchase reservation could not be saved; no purchase submitted."); return; }
            var saved = configManager.GetActiveConfig(); // Saving can merge an external Stop or cap change.
            if (saved.VerminionPaused || saved.VerminionProgress.PendingPurchase != reserved ||
                saved.VerminionProgress.GilSpent + offer.Gil > saved.VerminionGilPurchaseCap ||
                saved.VerminionProgress.MgpSpent + offer.Mgp > saved.VerminionMgpPurchaseCap ||
                !VerminionGameInteraction.TryReadPurchaseInventory(offer.ItemId, out var currentGil, out var currentMgp, out var currentItems) ||
                currentGil != gil || currentMgp != mgp || currentItems != items ||
                !VerminionProgress.PreservesGilReserve(currentGil, offer.Gil, saved.VerminionGilReserve))
            { Fail("Purchase reservation, pause or cap changed while saving; no purchase submitted."); return; }
            minionPurchaseSent = true;
            GameHelpers.FireAddonCallback(offer.Shop, true, 0, index, 1);
            log.Information($"[Verminion] Reserved and submitted one {offer.Name} purchase: gil={offer.Gil}; mgp={offer.Mgp}; awaiting item/currency evidence");
            return;
        }
        if (ownsMinionShop && (GameHelpers.IsAddonVisible("SelectString") || GameHelpers.IsAddonVisible("SelectIconString")))
        {
            var category = offer.MinionId == 26 ? "Purchase Items" : offer.Mgp > 0 ? "Purchase Minions (MGP)" : "Purchase Minions (Gil)";
            if (!GameHelpers.TrySelectStringExact(category, out _))
                GameHelpers.TrySelectNativeListEntry("SelectIconString", category);
            return;
        }
        var player = Plugin.ObjectTable.LocalPlayer;
        if (player == null) return;
        var vendor = VerminionGameInteraction.FindMinionVendor(offer);
        if (offer.MinionId == 26 && vendor == null && minionVendorDestination == null)
        {
            if (!navigation.TryGetNavReady(out var ready) || !ready) return;
            // Map (11.8, 24.7), size factor 100. Resolve the actual ground height
            // before approaching; replace this map point with the loaded NPC.
            if (!navigation.TryFindReachablePointOnFloor(new Vector3(-484, 0, 160), 10, out var destination))
            { Fail("Nonoroon's vendor area could not be resolved on the navmesh; no purchase submitted."); return; }
            minionVendorDestination = destination;
        }
        var position = vendor?.Position ?? minionVendorDestination ?? new Vector3(82.4139f, 0.411926f, 44.9377f);
        var distance = Vector3.Distance(player.Position, position);
        if (offer.MinionId == 26 && vendor == null && distance < 25)
        { Fail("Junkmonger Nonoroon is currently unavailable near Poor Maid's Mill, possibly during a nearby FATE. No purchase submitted; Resume when he returns."); return; }
        var interactionDistance = vendor == null ? 2.5f : GameHelpers.GetValidInteractionDistance(vendor);
        if (distance > interactionDistance)
        {
            if (!navigationRequested)
            {
                var fly = offer.MinionId == 26 && distance > 20;
                if (fly)
                {
                    if (!VerminionGameInteraction.HasNonoroonFlightAccess())
                    { Fail("Reaching Nonoroon automatically requires existing flight access; no purchase submitted."); return; }
                    if (!condition[ConditionFlag.Mounted]) { VerminionGameInteraction.TryUseVendorTravelAction(9); return; }
                    if (!condition[ConditionFlag.InFlight]) { VerminionGameInteraction.TryUseVendorTravelAction(2); return; }
                }
                navigationRequested = navigation.PathfindAndMoveTo(position, fly);
                if (!navigationRequested) Fail("Minion vendor navigation was rejected; no purchase submitted.");
            }
            return;
        }
        if (navigationRequested) { navigation.Stop(); navigationRequested = false; }
        if (condition[ConditionFlag.Mounted]) { VerminionGameInteraction.TryUseVendorTravelAction(23); return; }
        if (vendor != null)
        {
            Plugin.TargetManager.Target = vendor;
            if (GameHelpers.InteractWithObject(vendor)) ownsMinionShop = true;
        }
    }

    private bool CloseOwnedMinionShop()
    {
        if (!ownsMinionShop) return true;
        // Never close another character's or another vendor's UI.
        if (owner == 0 || Plugin.PlayerState.ContentId != owner || purchasingMinion is not { } selectedOffer ||
            !VerminionGameInteraction.IsMinionVendor(selectedOffer, Plugin.TargetManager.Target))
        { ownsMinionShop = false; return true; }
        if (purchasingMinion is { } offer && VerminionGameInteraction.IsMinionPurchaseConfirmation(offer))
        { GameHelpers.TryClickNativeButton("SelectYesno", offer.Mgp > 0 ? "No" : "Cancel", 11); return false; }
        foreach (var addon in new[] { "Shop", "ShopExchangeCurrency", "SelectString", "SelectIconString" })
            if (GameHelpers.IsAddonVisible(addon)) { GameHelpers.TryCloseAddonByCallback(addon); return false; }
        ownsMinionShop = false;
        return true;
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
        // The native device consumes position, press and release on separate
        // frames. Elapsed wall time alone cannot prove the click has finished,
        // particularly while background frame rates are limited.
        if (pendingBattlefieldClick != null || VerminionGameInteraction.BattlefieldClickPending)
        {
            if (now > battlefieldClickReleaseUtc.AddSeconds(2))
                Fail("Background battlefield input did not settle; no result recorded.");
            return;
        }
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
        // A reload resumes the existing camera, so six more wheel events may
        // overshoot the framing that was already verified for this battle.
        if (challengeStage == 19 && battlefieldZoomSteps < 6 &&
            VerminionGameInteraction.ReadBattlefieldCameraDistance() is >= 10.5f)
            battlefieldZoomSteps = 6;
        if (challengeStage == 19 && battlefieldZoomSteps < 6)
        {
            if (!VerminionGameInteraction.TryZoomBattlefieldOut(out var zoomPoint))
            { Fail("Stage 19 background camera zoom unavailable; no result recorded."); return; }
            pendingBattlefieldClick = zoomPoint;
            pendingBattlefieldRightClick = false;
            battlefieldClickReleaseUtc = now.AddMilliseconds(150);
            ++battlefieldZoomSteps;
            log.Information($"[VerminionControl] Stage 19 camera zoom-out step={battlefieldZoomSteps}/6; awaiting native projection");
            return;
        }
        if (challengeStage == 19 && battlefieldZoomSteps == 6)
        {
            ++battlefieldZoomSteps;
            VerminionGameInteraction.ReleaseBattlefieldInput(restoreRendering: false);
            VerminionGameInteraction.CaptureBattle("Stage 19 camera zoom readback");
        }
        if (pendingGroupDestination is { } groupDestination)
        {
            var hatchlings = Plugin.ObjectTable.OfType<Dalamud.Game.ClientState.Objects.Types.IBattleNpc>()
                .Where(obj => obj.CurrentHp > 0 && obj.Name.TextValue == tutorialGroupName &&
                VerminionGameInteraction.IsFriendlyMinion(obj) && (groupOrigin == null || Vector3.DistanceSquared(obj.Position, groupOrigin.Value) < 100)).ToArray();
            if (hatchlings.Length == 0 && VerifyBattleGroup)
            {
                if (IsBossStage || preserveObscuredGroup) CancelLostBossGroup();
                else Fail("The opening battle group is unavailable; no result recorded.");
                return;
            }
            if (VerifyBattleGroup) groupMinimum = Math.Min(groupMinimum, hatchlings.Length);
            if (hatchlings.Length < groupMinimum) { Fail("The battle group is unavailable; no result recorded."); return; }
            // Select inside the action party so the nearby-type click includes
            // its members. Reject obscured candidates using native hit readback.
            var first = singleUnitSelection is { } single
                ? hatchlings.FirstOrDefault(unit => unit.GameObjectId == single)
                : VerifyBattleGroup
                ? hatchlings.Where(obj => !rejectedGroupSelections.Contains(obj.GameObjectId) &&
                        (!groupSelectionOnly || !bossSpecialProbes.Contains(obj.GameObjectId)))
                    .OrderBy(candidate => (groupSelectionOnly || challengeStage == 19) && groupOrigin != null ? Vector3.DistanceSquared(candidate.Position, groupOrigin.Value) : 0)
                    .ThenByDescending(candidate => hatchlings.Count(unit => Vector3.DistanceSquared(unit.Position, candidate.Position) < 16))
                    .ThenBy(candidate => groupOrigin == null ? 0 : Vector3.DistanceSquared(candidate.Position, groupOrigin.Value))
                    .ThenByDescending(obj => obj.Position.Z).FirstOrDefault()
                : hatchlings.OrderBy(obj => obj.Position.X).FirstOrDefault();
            if (first == null)
            {
                if ((IsBossStage || preserveObscuredGroup) && singleUnitSelection == null)
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
            if (groupSelectionOnly) bossSpecialProbes.Add(first.GameObjectId);
            pendingGroupDestination = null;
            pendingBattlefieldClick = point;
            pendingBattlefieldRightClick = false;
            battlefieldClickReleaseUtc = now.AddMilliseconds(150);
            tutorialDestination = groupDestination;
            groupSelectionAnchor = first.GameObjectId;
            pendingSameTypeSelection = first.GameObjectId;
            return;
        }
        if (pendingTutorialAction is { } action && pendingSameTypeSelection == null &&
            !pendingTutorialMove && movementSnapshotUtc == DateTime.MinValue)
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
            var selectedId = (VerifyBattleGroup || groupSelectionOnly || singleUnitSelection != null) && !groupSingleSelectionVerified
                ? VerminionGameInteraction.ReadBattlefieldClickHit() : objectId;
            var selected = Plugin.ObjectTable.FirstOrDefault(obj => obj.GameObjectId ==
                selectedId && obj.Name.TextValue == tutorialGroupName && VerminionGameInteraction.IsFriendlyMinion(obj) &&
                (singleUnitSelection == null || !requireExactSingleSelection || obj.GameObjectId == singleUnitSelection) &&
                (groupOrigin == null || Vector3.DistanceSquared(obj.Position, groupOrigin.Value) < 100));
            if (singleUnitSelection != null && !groupSelectionOnly)
            {
                pendingSameTypeSelection = null;
                if (selected == null) { Fail("The requested individual minion selection could not be verified; no result recorded."); return; }
                groupSelectionAnchor = selected.GameObjectId;
                groupSelectionVerified = true;
                pendingTutorialMove = true;
                cameraAdjustments = 0;
                return;
            }
            if (VerifyBattleGroup && selected == null) { pendingSameTypeSelection = null; RetryBossGroupSelection(); return; }
            if (groupSelectionOnly && selected != null)
            {
                pendingSameTypeSelection = null;
                groupSelectionAnchor = selected.GameObjectId;
                groupSelectionVerified = true;
                bossSpecialProbes.Add(selected.GameObjectId);
                ReleaseKey();
                VerminionGameInteraction.ReleaseBattlefieldInput(restoreRendering: false);
                log.Information("[VerminionControl] selected another native action-party candidate without changing its orders");
                return;
            }
            if (selected == null || !VerminionGameInteraction.TryGetObjectCenter(selected, out var center))
            { Fail("The selected tutorial unit is unavailable; no result recorded."); return; }
            var framed = TryFrameTutorialPoint(center, out var point);
            if (framed == null)
            { pendingSameTypeSelection = selected.GameObjectId; groupSingleSelectionVerified = true; return; }
            if (framed == false || !VerminionGameInteraction.SendBattlefieldClick(point, release: false, doubleClick: true))
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
                var selectedId = VerminionGameInteraction.ReadBattlefieldClickHit();
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
                if (challengeStage == 19)
                {
                    var lane = VerminionGameInteraction.ReadStoneHp(false, 1) > 0 ? 1 :
                        Enumerable.Range(0, 3).FirstOrDefault(index => VerminionGameInteraction.ReadStoneHp(false, index) > 0, -1);
                    var stone = Plugin.ObjectTable.FirstOrDefault(obj => obj.BaseId == 2006533 &&
                        obj.Name.TextValue == $"Arcana Stone {(char)('A' + lane)}");
                    if (stone == null || Vector3.DistanceSquared(enemy.Position, stone.Position) >= 36)
                    {
                        // A fast enemy can leave the bunker during selection.
                        // Do not turn a local interception into cross-field pursuit.
                        pendingTutorialMove = false;
                        groupAttackTarget = null;
                        bossOrderUtc = DateTime.MinValue;
                        ReleaseKey();
                        VerminionGameInteraction.ReleaseBattlefieldInput(restoreRendering: false);
                        log.Information("[VerminionControl] Stage 19 target left the defended stone; reassessing local threats before movement");
                        return;
                    }
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
            if (!VerminionGameInteraction.TrySummonPaletteSlot(VerminionGameInteraction.FindTutorialSummonSlot()))
                Fail("Gate selection sent but summon unavailable; no result recorded.");
            return;
        }
        if (pendingTutorialSummons > 0)
        {
            if (!VerminionGameInteraction.TrySummonPaletteSlot(VerminionGameInteraction.FindTutorialSummonSlot()))
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
                VerminionGameInteraction.TrySummonPaletteSlot(VerminionGameInteraction.FindTutorialSummonSlot()),
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
        var hatchlings = Plugin.ObjectTable.OfType<Dalamud.Game.ClientState.Objects.Types.IBattleNpc>()
            .Where(obj => obj.CurrentHp > 0 && obj.Name.TextValue == "Wayward Hatchling" &&
                VerminionGameInteraction.IsFriendlyMinion(obj))
            .OrderByDescending(obj => foes.Length == 0 ? obj.Position.X :
                foes.Min(foe => Vector3.DistanceSquared(obj.Position, foe.Position)));
        foreach (var hatchling in hatchlings)
        {
            Vector2 screen;
            if (!(move ? VerminionGameInteraction.ProjectBattlefield(hatchling.Position + new Vector3(0, 0.1f, 0), out screen) :
                VerminionGameInteraction.ProjectObjectCenter(hatchling, out screen)) ||
                !VerminionGameInteraction.IsBattlefieldPointVisible(screen)) continue;
            if (!move)
                return TrySelectGroup(hatchling.Position, "Wayward Hatchling", 1, hatchling.Position,
                    singleUnit: hatchling.GameObjectId, requireExactSingle: false, selectionOnly: true);
            if (!VerminionGameInteraction.SendBattlefieldClick(screen, release: false)) return false;
            pendingBattlefieldClick = screen;
            pendingBattlefieldRightClick = false;
            battlefieldClickReleaseUtc = DateTime.UtcNow.AddMilliseconds(150);
            pendingTutorialMove = move;
            tutorialDestination = new Vector3(0, 0, 12);
            return true;
        }
        return false;
    }

    private bool TryTutorialSpecial()
    {
        var hatchlings = Plugin.ObjectTable.OfType<Dalamud.Game.ClientState.Objects.Types.IBattleNpc>()
            .Where(unit => unit.CurrentHp > 0 && unit.Name.TextValue == "Wayward Hatchling" &&
                VerminionGameInteraction.IsFriendlyMinion(unit)).ToArray();
        if (hatchlings.Length < 4) return false;
        // Specials require four charged minions nearby. The preceding fights
        // can leave one defending the stone while the other three move on.
        var rally = hatchlings.OrderByDescending(candidate => hatchlings.Count(unit =>
            Vector3.DistanceSquared(unit.Position, candidate.Position) < 16)).First();
        var straggler = hatchlings.OrderByDescending(unit => Vector3.DistanceSquared(unit.Position, rally.Position)).First();
        if (Vector3.DistanceSquared(straggler.Position, rally.Position) >= 16)
        {
            if (!TrySelectGroup(rally.Position, "Wayward Hatchling", 1, straggler.Position,
                singleUnit: straggler.GameObjectId)) return false;
            log.Information("[VerminionControl] regrouping the isolated tutorial hatchling before checking its special");
        }
        else if (!TrySelectTutorialHatchling(move: false)) return false;
        tutorialActionPanelsRestored = false;
        pendingTutorialAction = 82;
        return true;
    }

    private bool TrySelectTutorialGroup(Vector3 destination, string name = "Wayward Hatchling")
        => TrySelectGroup(destination, name, 4);

    private bool TrySelectGroup(Vector3 destination, string name, int minimum, Vector3? origin = null, ulong? attackTarget = null,
        ulong? singleUnit = null, bool requireExactSingle = true, bool selectionOnly = false, bool preserveExistingOrder = false)
    {
        tutorialGroupName = name;
        groupSelectionOnly = selectionOnly;
        preserveObscuredGroup = preserveExistingOrder;
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
            if ((IsBossStage || preserveObscuredGroup) && singleUnitSelection == null)
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
        // Gilgamesh can cover every member of the party at the far edge of
        // the view. Reframe the observed group once before trying another hit.
        if (challengeStage == 19 && rejectedGroupSelections.Count == 1 && groupOrigin is { } origin &&
            VerminionGameInteraction.TryFocusMiniMap(origin, out var point))
        {
            pendingBattlefieldClick = point;
            pendingBattlefieldRightClick = false;
            battlefieldClickReleaseUtc = DateTime.UtcNow.AddMilliseconds(150);
            nextPromptCheckUtc = DateTime.UtcNow.AddSeconds(0.5);
            cameraAdjustments = 1;
        }
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
        if (IsStoneStage) nextObjectiveOrderUtc = DateTime.UtcNow.AddSeconds(20);
        log.Information("[VerminionControl] group unavailable for this order; preserve existing attacks and result observation until the next strategy interval");
    }

    private void UpdateStoneOpening()
    {
        var specialMinion = challengeStage == 20 ? bossStrategy.CurrentDefenderName : opening.CurrentMinionName;
        if ((challengeStage is 7 or 16 or 20) && groupSelectionVerified && DateTime.UtcNow >= nextBossSpecialUtc &&
            Plugin.ObjectTable.Any(unit => unit.GameObjectId == groupSelectionAnchor &&
                unit.Name.TextValue == specialMinion && VerminionGameInteraction.IsFriendlyMinion(unit) &&
                Plugin.ObjectTable.Any(target => (VerminionGameInteraction.IsEnemyMinion(target) || challengeStage != 20 && target.BaseId == 2006537) &&
                    Vector3.DistanceSquared(unit.Position, target.Position) < 25)) &&
            VerminionGameInteraction.IsPaletteActionReady(82))
        {
            nextBossSpecialUtc = DateTime.UtcNow.AddSeconds(3);
            if (VerminionGameInteraction.TryClickPaletteIcon(82))
            {
                movementSnapshotUtc = DateTime.UtcNow.AddSeconds(1);
                log.Information($"[VerminionControl] Stage {challengeStage} {specialMinion} special near objective/enemy; observing native status next");
                return;
            }
        }
        if (challengeStage == 20 && opening.HasDeployedGroup &&
            VerminionGameInteraction.TryReadSummoningCapacity(out var totalUsed, out var totalCapacity))
        {
            var guardGate = Plugin.ObjectTable.FirstOrDefault(obj => obj.BaseId == 2006534 && obj.Name.TextValue == "Gate B");
            var guards = Plugin.ObjectTable.OfType<Dalamud.Game.ClientState.Objects.Types.IBattleNpc>()
                .Where(obj => VerminionGameInteraction.IsFriendlyMinion(obj) && obj.CurrentHp > 0 &&
                    obj.Name.TextValue == bossStrategy.CurrentDefenderName).ToArray();
            var attackers = Plugin.ObjectTable.Count(obj => VerminionGameInteraction.IsFriendlyMinion(obj) &&
                obj.Name.TextValue == opening.CurrentMinionName);
            if (guardGate != null && UpdateBossDefenders(guardGate.Position, guards, attackers,
                totalUsed + opening.Reinforcements.Pending * opening.MinionCost, totalCapacity, 1,
                !reconcilingSummonQueue && VerminionGameInteraction.IsSummoningGateAvailable(1))) return;
        }
        if (opening.OpeningComplete) { ReinforceRemainingStone(); return; }
        if ((DateTime.UtcNow - openingStepUtc).TotalSeconds > 90)
        { Fail($"Stage {challengeStage} opening did not produce its expected units; no result recorded."); return; }
        var gate = Plugin.ObjectTable.FirstOrDefault(obj => obj.BaseId == 2006534 && obj.Name.TextValue == $"Gate {(char)('A' + opening.Gate)}");
        var stone = Plugin.ObjectTable.FirstOrDefault(obj => obj.BaseId == opening.TargetBaseId && obj.Name.TextValue == opening.TargetName);
        if (gate == null || stone == null || !VerminionGameInteraction.TryReadSummoningCapacity(out var used, out var capacity)) return;
        if (HasBossDefenders) capacity = bossStrategy.AttackCapacity(capacity, Plugin.ObjectTable.Count(obj =>
            VerminionGameInteraction.IsFriendlyMinion(obj) && obj.Name.TextValue == bossStrategy.CurrentDefenderName));
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
        if (!VerminionGameInteraction.TryReadSummoningCapacity(out var used, out var capacity)) return;
        if (HasBossDefenders) capacity = bossStrategy.AttackCapacity(capacity, Plugin.ObjectTable.Count(obj =>
            VerminionGameInteraction.IsFriendlyMinion(obj) && obj.Name.TextValue == bossStrategy.CurrentDefenderName));
        var canRequest = !reconcilingSummonQueue && opening.Reinforcements.CanRequest(used, capacity, opening.MinionCost);
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
            TrySelectGroup(target.Position, opening.CurrentMinionName, 1, group.Object.Position, preserveExistingOrder: true);
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
        bossSpecialProbes.Clear();
        odinRegrouping = odinRegrouped = false;
        odinRegroupUtc = DateTime.MinValue;
        bossStrategy = new(challengeStage);
        deployedBossUnits.Clear();
        bossOrderUtc = DateTime.MinValue;
        bossWaveUtc = DateTime.UtcNow;
        bossSpawnUtc = DateTime.UtcNow;
        bossDefenderSpawnUtc = DateTime.UtcNow;
        bossDefenseOrderUtc = DateTime.MinValue;
        bossDefenderWaveUtc = DateTime.MinValue;
        bossDefenseTarget = 0;
        bossOrderPosition = null;
        bossOrderTarget = 0;
        bossOrderRotation = 0;
        nextBossSpecialUtc = DateTime.MinValue;
        bossSpecialReadbackUtc = DateTime.MinValue;
        bombLeverOrderUtc = DateTime.MinValue;
        bombCarrierOrderUtc = DateTime.MinValue;
        bombDropUtc = DateTime.MinValue;
        bombTriggerUtc = DateTime.MinValue;
        bombCycles = 0;
        observedFinalStageCasts.Clear();
        observedFinalStageObjects.Clear();
    }

    private void ObserveGilgameshPhase()
    {
        var boss = Plugin.ObjectTable.OfType<Dalamud.Game.ClientState.Objects.Types.IBattleNpc>()
            .FirstOrDefault(unit => VerminionGameInteraction.IsEnemyMinion(unit) &&
                unit.CurrentHp > 0 && unit.Name.TextValue == "Wind-up Gilgamesh");
        if (boss == null) return;
        // Native timers show these arrive about three seconds apart. ATK Up
        // starts the warning; waiting for all three loses the escape window.
        var enraged = boss.StatusList.Any(status => status.StatusId is 962 or 964 or 966);
        if (enraged == bossEnraged) return;
        bossEnraged = enraged;
        bossOrderUtc = DateTime.MinValue;
        bossSpecialProbes.Clear();
        // Selection/camera steps can span several seconds. A new phase must
        // interrupt their old destination before the next movement click.
        pendingGroupDestination = null;
        pendingSameTypeSelection = null;
        pendingTutorialMove = false;
        pendingTutorialAction = null;
        pendingBattlefieldClick = null;
        groupAttackTarget = null;
        movementSnapshotUtc = DateTime.MinValue;
        nextPromptCheckUtc = DateTime.MinValue;
        ReleaseKey();
        VerminionGameInteraction.ReleaseBattlefieldInput(restoreRendering: false);
        VerminionGameInteraction.CaptureBattle($"Stage 19 {(enraged ? "buffed fallen phase" : "buffed phase ended")}");
        VerminionGameInteraction.CaptureTutorialImage();
    }

    private void UpdateBossBattle()
    {
        var now = DateTime.UtcNow;
        var enemies = Plugin.ObjectTable.OfType<Dalamud.Game.ClientState.Objects.Types.IBattleNpc>()
            .Where(VerminionGameInteraction.IsEnemyMinion).ToArray();
        var boss = enemies.Where(obj => obj.CurrentHp > 0)
            // Stage 6's living Imps sustain the boss's invulnerability. Clear
            // them first; Smallshells are not a reason to leave the boss.
            .OrderByDescending(obj => VerminionBossStrategy.IsInvulnerabilityAdd(challengeStage, obj.Name.TextValue))
            .ThenByDescending(obj => obj.MaxHp).FirstOrDefault();
        if (challengeStage == 24)
        {
            // Identify Bahamut separately from his adds: their maximum HP must
            // not cause the generic boss selector to abandon the final target.
            var bahamut = enemies.FirstOrDefault(enemy => enemy.CurrentHp > 0 &&
                VerminionBossStrategy.IsFinalCoilBoss(enemy.Name.TextValue));
            if (bahamut == null)
            { reason = "Stage 24: waiting for Wind-up Bahamut or an explicit result"; return; }
            var target = bossStrategy.ChooseFinalCoilTarget(bahamut.GameObjectId,
                enemies.Select(enemy => (enemy.GameObjectId, enemy.Name.TextValue, enemy.CurrentHp)));
            boss = enemies.FirstOrDefault(enemy => enemy.GameObjectId == target && enemy.CurrentHp > 0) ?? bahamut;
        }
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
                var target = challengeStage == 19
                    ? invaders.FirstOrDefault(enemy => enemy.GameObjectId == boss.GameObjectId) ?? adds.FirstOrDefault()
                    : adds.Length >= 3
                    ? adds.FirstOrDefault(enemy => enemy.GameObjectId == bossOrderTarget) ?? adds[0]
                    : invaders.FirstOrDefault(enemy => enemy.GameObjectId == bossOrderTarget) ??
                        invaders.FirstOrDefault(enemy => enemy.GameObjectId == boss.GameObjectId) ?? adds.FirstOrDefault();
                // Both stone models obstruct ground clicks: the friendly B is
                // at z=2 and its enemy counterpart at z=-2. Rally beside them.
                destination = target?.Position ?? (challengeStage == 19
                    ? stone.Position + new Vector3(3, 0, 0) : stone.Position);
                combatTarget = target?.GameObjectId;
                commandTargetName = target?.Name.TextValue ?? stone.Name.TextValue;
                bunkerStone = stone.Name.TextValue;
            }
        }
        if (challengeStage == 19)
        {
            if (bossEnraged)
            {
                // The gate heals the party during the observed buff window.
                // Returning there also avoids sending it across the field to a
                // safe point relative to a boss that is already far away.
                destination = gate.Position;
                combatTarget = null;
                commandTargetName = "heal at gate during Gilgamesh's buffed attack";
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
                obj.Name.TextValue == bossStrategy.CurrentDefenderName).ToArray();
        if (challengeStage == 15 && !bossArmyReady)
        {
            // Cheap monsters need the complete army for Odin's final damage check.
            // Reconcile an already engaged army after a reload.
            var gates = Plugin.ObjectTable.Where(obj => obj.BaseId == 2006534).ToArray();
            bossArmyReady = units.Length >= bossStrategy.ArmySize || gates.Length > 0 && units.Any(unit =>
                gates.All(spawn => Vector3.DistanceSquared(unit.Position, spawn.Position) >= 100));
        }
        if (challengeStage == 15 && capacity > 60)
        {
            if (!odinRegrouped && !odinRegrouping && boss.CurrentHp <= boss.MaxHp * 0.6f && boss.CurrentHp > boss.MaxHp / 3)
            {
                odinRegrouping = true;
                odinRegroupUtc = now;
                bossOrderUtc = DateTime.MinValue;
                VerminionGameInteraction.CaptureBattle($"Stage {challengeStage} recover army at gate before losses");
            }
            if (odinRegrouping && (now - odinRegroupUtc).TotalSeconds >= 20 && units.Length >= bossStrategy.ArmySize &&
                units.All(unit => unit.CurrentHp >= unit.MaxHp * 0.95f && Vector3.DistanceSquared(unit.Position, gate.Position) < 36))
            {
                odinRegrouping = false;
                odinRegrouped = true;
                deployedBossUnits.Clear();
                bossOrderUtc = DateTime.MinValue;
                bossWaveUtc = now.AddSeconds(-60);
                VerminionGameInteraction.CaptureBattle($"Stage {challengeStage} full army healed and ready");
            }
            if (odinRegrouping)
            {
                if ((now - odinRegroupUtc).TotalSeconds > 90)
                { Fail($"Stage {challengeStage} full army recovery exceeded 90 seconds; no result recorded."); return; }
                destination = gate.Position;
                combatTarget = null;
                commandTargetName = "heal and assemble at gate";
            }
        }
        var selected = groupSelectionVerified
            ? units.Concat(defenders).FirstOrDefault(obj => obj.GameObjectId == groupSelectionAnchor) : null;
        var selectedDefender = HasBossDefenders && selected?.Name.TextValue == bossStrategy.CurrentDefenderName;
        if (challengeStage == 12 && !bossContactCaptured && selected != null &&
            Vector3.DistanceSquared(selected.Position, boss.Position) < 36)
        {
            bossContactCaptured = VerminionGameInteraction.CaptureTutorialImage();
            log.Information($"[VerminionControl] Stage 12 contact; selected={tutorialGroupName}; verified={groupSelectionVerified}; actionReady={VerminionGameInteraction.IsPaletteActionReady(82)}; image={bossContactCaptured}");
        }
        // Odin destroys every stone after his final cast. Concentrate the
        // monster army so its special supports the final attack.
        if (challengeStage == 15 && !odinRegrouping && !bossBurstStarted && capacity > 60 &&
            boss.CurrentHp <= boss.MaxHp / 3 && units.Length >= 4)
        {
            VerminionGameInteraction.CaptureBattle("Stage 15 final damage phase");
            var anchor = units.OrderByDescending(candidate => units.Count(unit =>
                    Vector3.DistanceSquared(unit.Position, candidate.Position) < 25))
                .ThenBy(unit => Vector3.DistanceSquared(unit.Position, boss.Position)).First();
            bossSpecialProbes.Clear();
            TrySelectGroup(anchor.Position, bossStrategy.CurrentMinionName, 1, anchor.Position, selectionOnly: true);
            bossBurstStarted = true;
            log.Information($"[VerminionControl] Stage 15 final burst; targetHp={boss.CurrentHp}/{boss.MaxHp}; attackers={units.Length}");
            return;
        }
        var savingOdinBuff = challengeStage == 15 && odinRegrouped && boss.CurrentHp > boss.MaxHp / 3;
        if (!odinRegrouping && !savingOdinBuff && !(challengeStage == 19 && bossEnraged) &&
            bossStrategy.ShouldUseSpecial(boss.CurrentHp, boss.MaxHp, units.Length) &&
            now >= nextBossSpecialUtc && selected != null &&
            (selectedDefender ? Plugin.ObjectTable.Any(enemy => VerminionGameInteraction.IsEnemyMinion(enemy) &&
                Vector3.DistanceSquared(selected.Position, enemy.Position) < 36) :
                Vector3.DistanceSquared(selected.Position, boss.Position) < 25 &&
                !boss.StatusList.Any(status => status.StatusId == 981)) &&
            VerminionGameInteraction.IsPaletteActionReady(82))
        {
            nextBossSpecialUtc = now.AddSeconds(challengeStage == 15 ? 6.5 : 3);
            if (VerminionGameInteraction.TryClickPaletteIcon(82))
            {
                if (challengeStage is 15 or 19 or 23)
                {
                    bossSpecialProbes.Clear();
                    bossSpecialProbes.Add(selected.GameObjectId);
                }
                if (challengeStage == 19) bossSpecialReadbackUtc = now.AddSeconds(1);
                log.Information($"[VerminionControl] Stage {challengeStage} {(selectedDefender ? "defensive" : "offensive")} special; party={selected.Name}; target={boss.Name}; targetHp={boss.CurrentHp}/{boss.MaxHp}");
                return;
            }
        }
        // An older build may already be fighting when this strategy is loaded.
        // Finish observing that battle with its existing palette; every new
        // Defender stages prepare their palette outside battle first.
        var useDefenders = HasBossDefenders && VerminionGameInteraction.FindPaletteMinion(bossStrategy.CurrentDefenderMinion) >= 0;
        if (useDefenders && challengeStage != 19 &&
            UpdateBossDefenders(gate.Position, defenders, units.Length, used, capacity, gateIndex, gateAvailable)) return;
        var rally = Plugin.ObjectTable.Where(obj => obj.BaseId == 2006534)
            .Select(spawnGate => new { spawnGate.Position, Units = units.Where(unit =>
                (!deployedBossUnits.Contains(unit.GameObjectId) ||
                    challengeStage == 19 && (now - bossWaveUtc).TotalSeconds >= 15) &&
                Vector3.DistanceSquared(unit.Position, spawnGate.Position) < 100).ToArray() })
            .OrderByDescending(group => group.Units.Length).FirstOrDefault();
        var ready = rally?.Units ?? Array.Empty<Dalamud.Game.ClientState.Objects.Types.IBattleNpc>();
        var deployed = units.Except(ready).ToArray();
        var waveSeconds = (now - bossWaveUtc).TotalSeconds;
        // A dispatched selection is not proof the entire group left its gate.
        // At full capacity, send stranded survivors without waiting for a fourth
        // unit that cannot be summoned. Give the prior order time to move first.
        if (challengeStage == 19 && waveSeconds >= 15 && used + bossStrategy.MinionCost > capacity && bossStrategy.PendingSummons == 0)
            waveSeconds = Math.Max(60, waveSeconds);
        var orderSeconds = (now - bossOrderUtc).TotalSeconds;
        var turned = challengeStage == 12 && combatTarget == boss.GameObjectId && bossOrderPosition != null &&
            MathF.Cos(boss.Rotation - bossOrderRotation) < 0.7f;
        var moved = bossOrderTarget != (combatTarget ?? 0) || bossOrderPosition == null ||
            turned ||
            Vector3.DistanceSquared(destination, bossOrderPosition.Value) >= (challengeStage is 23 or 24 ? 1 : 16) ||
            // Reconcile actual arrival, including units stranded by a prior
            // command. Most of the deployed army should be near its target.
            orderSeconds >= 15 && deployed.Count(unit => Vector3.DistanceSquared(unit.Position, destination) <
                (challengeStage == 19 ? 4 : challengeStage is 23 or 24 ? 2.25f : 36)) <
                (challengeStage is 19 or 23 or 24 ? (deployed.Length + 1) / 2 : deployed.Length / 2);
        // Let a party already in melee finish its attacks. Repeated ground
        // orders and switching between nearby adds interrupt that engagement.
        if (bossStrategy.DefendsStone && !turned && combatTarget == bossOrderTarget && deployed.Length > 0 &&
            deployed.Count(unit => Vector3.DistanceSquared(unit.Position, destination) < 4) >= (deployed.Length + 1) / 2)
            moved = false;
        var action = bossStrategy.Decide(VerminionGameInteraction.IsSummoningGateSelected(gateIndex), ready.Length,
            deployed.Length, used, useDefenders ? bossStrategy.AttackCapacity(capacity, defenders.Length) : capacity,
            waveSeconds, (bossStrategy.DefendsStone || challengeStage == 19) && orderSeconds >= 3 ? Math.Max(10, orderSeconds) : orderSeconds, moved, gateAvailable,
            assemble: challengeStage == 15 && !bossArmyReady || challengeStage == 19 && bossEnraged);
        // Move the exposed attackers first when Gilgamesh buffs himself;
        // deploying fresh units must not postpone their escape.
        if (challengeStage == 19 && bossEnraged && deployed.Length > 0 && orderSeconds >= 3 && moved)
            action = VerminionBossStrategy.Action.FollowBoss;
        if (challengeStage == 15 && !odinRegrouping && boss.CurrentHp <= boss.MaxHp / 3 &&
            action == VerminionBossStrategy.Action.FollowBoss && deployed.Count(unit =>
                Vector3.DistanceSquared(unit.Position, boss.Position) < 25) >= (deployed.Length + 1) / 2)
            action = VerminionBossStrategy.Action.Wait;
        if (now >= nextBossSpecialUtc && bossStrategy.ShouldUseSpecial(boss.CurrentHp, boss.MaxHp, units.Length) &&
            (challengeStage == 15 && !odinRegrouping && boss.CurrentHp <= boss.MaxHp / 3 ||
             challengeStage is 19 or 23 && !bossEnraged && !odinRegrouping && units.Length >= 4 &&
                (challengeStage == 23 || used + bossStrategy.MinionCost > capacity &&
                    bossStrategy.PendingSummons == 0 && bossStrategy.Defenders.Pending == 0) &&
                action == VerminionBossStrategy.Action.Wait))
        {
            // Readiness belongs to the selected minion's nearby action party.
            // Selecting another living minion can expose another charged party;
            // the native button must still confirm readiness before casting.
            // Twintania attrition can prevent a cheap roster ever reaching full
            // capacity. Probe during queue waits without delaying its summons.
            var candidate = units.Where(unit => !bossSpecialProbes.Contains(unit.GameObjectId) &&
                unit.GameObjectId != groupSelectionAnchor &&
                (challengeStage is not (19 or 23) || units.Count(ally => Vector3.DistanceSquared(ally.Position, unit.Position) < 16) >= 4) &&
                Vector3.DistanceSquared(unit.Position, boss.Position) < 25)
                .OrderByDescending(unit => unit.CurrentHp).FirstOrDefault();
            if (candidate != null)
            {
                nextBossSpecialUtc = now.AddSeconds(2);
                TrySelectGroup(candidate.Position, candidate.Name.TextValue,
                    1, candidate.Position, selectionOnly: true);
                return;
            }
            if (challengeStage is 19 or 23)
            {
                // Revisit charging parties at the ability cadence. Defender
                // selections must not leave ready attacker specials unattended.
                bossSpecialProbes.Clear();
                nextBossSpecialUtc = now.AddSeconds(10);
            }
        }
        // Selection/camera work can consume the entire defender refresh period.
        // Alternate pending movement orders so it cannot strand a ready attack
        // wave at the gate indefinitely. Retreat still takes precedence.
        var attackerMovement = action is VerminionBossStrategy.Action.SendWave or VerminionBossStrategy.Action.FollowBoss;
        if (challengeStage == 19 && useDefenders &&
            !(bossEnraged && units.Any(unit => Vector3.DistanceSquared(unit.Position, gate.Position) >= 36) &&
                bossDefenseOrderUtc >= bossOrderUtc) &&
            (!attackerMovement || bossDefenseOrderUtc < bossOrderUtc) &&
            UpdateBossDefenders(gate.Position, defenders, units.Length, used, capacity, gateIndex, gateAvailable)) return;
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
                // A reserve wave's destination says nothing about where the
                // existing army is attacking. Do not postpone its pursuit each
                // time a replacement leaves the gate during either final boss.
                if (challengeStage is not (23 or 24) || deployed.Length == 0)
                {
                    bossOrderUtc = now;
                    bossOrderPosition = destination;
                    bossOrderTarget = combatTarget ?? 0;
                }
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
                if ((bossStrategy.DefendsStone || challengeStage == 19) && groupSelectionVerified &&
                    !(challengeStage == 19 && bossEnraged) && !groupSelectionOnly && singleUnitSelection == null && tutorialGroupName == bossStrategy.CurrentMinionName &&
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
        if (challengeStage == 15 && !bossArmyReady) reason = $"Stage 15: assembling {bossStrategy.CurrentMinionName} army ({units.Length}/{bossStrategy.ArmySize})";
        if (odinRegrouping) reason = $"Stage {challengeStage}: recovering {bossStrategy.CurrentMinionName} army at gate ({units.Length}/{bossStrategy.ArmySize})";
    }

    private bool UpdateBossDefenders(Vector3 gate, Dalamud.Game.ClientState.Objects.Types.IBattleNpc[] defenders,
        int attackers, int used, int capacity, int gateIndex, bool gateAvailable)
    {
        if (challengeStage != 19 && capacity <= 60 ||
            challengeStage is not (19 or 20) && attackers < bossStrategy.WaveSize && defenders.Length == 0) return false;
        if (VerminionGameInteraction.FindPaletteMinion(bossStrategy.CurrentDefenderMinion) < 0)
        { Fail($"Stage {challengeStage} defender palette must be prepared outside the current battle; no result recorded."); return true; }
        var now = DateTime.UtcNow;
        if (challengeStage == 19 && bossEnraged)
        {
            // Send the second party after the attacker's retreat command,
            // without waiting for the attackers to finish travelling to safety.
            var exposed = defenders.Where(unit => Vector3.DistanceSquared(unit.Position, gate) >= 36).ToArray();
            if (exposed.Length > 0 && (now - bossDefenseOrderUtc).TotalSeconds >= 3)
            {
                var anchor = exposed.OrderByDescending(unit => Vector3.DistanceSquared(unit.Position, gate)).First();
                TrySelectGroup(gate, bossStrategy.CurrentDefenderName, 1, anchor.Position);
                bossDefenseOrderUtc = now;
                bossDefenseTarget = 0;
                log.Information($"[VerminionControl] Stage 19 retreat defenders during Gilgamesh's buff; exposed={exposed.Length}");
                return true;
            }
            return false;
        }
        var lane = VerminionGameInteraction.ReadStoneHp(false, 1) > 0 ? 1 :
            Enumerable.Range(0, 3).FirstOrDefault(index => VerminionGameInteraction.ReadStoneHp(false, index) > 0, -1);
        var stone = Plugin.ObjectTable.FirstOrDefault(obj => obj.BaseId == 2006533 && obj.Name.TextValue == $"Arcana Stone {(char)('A' + lane)}");
        if (lane < 0 || stone == null) return false;
        var threats = Plugin.ObjectTable.OfType<Dalamud.Game.ClientState.Objects.Types.IBattleNpc>()
            .Where(enemy => VerminionGameInteraction.IsEnemyMinion(enemy) && enemy.CurrentHp > 0 &&
                (challengeStage != 19 || enemy.Name.TextValue != "Wind-up Gilgamesh") &&
                Vector3.DistanceSquared(enemy.Position, stone.Position) < (challengeStage == 19 ? 36 : 100)).ToArray();
        var threat = threats.FirstOrDefault(enemy => enemy.GameObjectId == bossDefenseTarget) ??
            threats.OrderBy(enemy => Vector3.DistanceSquared(enemy.Position, stone.Position)).FirstOrDefault();
        // Gather separate waves on exposed ground. Idle groups left at an old
        // interception point otherwise never return to defend the stone.
        var defensePosition = threat?.Position ?? stone.Position +
            (challengeStage == 19 ? new Vector3(3, 0, 0) : new Vector3(4, 0, 0));
        var ready = defenders.Where(unit => Vector3.DistanceSquared(unit.Position, gate) < 100).ToArray();
        var canSummon = gateAvailable && bossStrategy.CanRequestDefender(defenders.Length, used, capacity);
        if (ready.Length > 0 && (now - bossDefenderWaveUtc).TotalSeconds >= 15 &&
            (ready.Length >= 4 || bossStrategy.Defenders.Pending == 0 && !canSummon))
        {
            TrySelectGroup(challengeStage == 19 ? defensePosition : threat?.Position ?? stone.Position,
                bossStrategy.CurrentDefenderName, 1, gate, threat?.GameObjectId);
            bossDefenderWaveUtc = now;
            if (challengeStage == 19) bossDefenseOrderUtc = now;
            log.Information($"[VerminionControl] Stage {challengeStage} send {bossStrategy.CurrentDefenderName} to {threat?.Name ?? stone.Name}; ready={ready.Length}");
            return true;
        }
        if (canSummon)
        {
            if (!VerminionGameInteraction.IsSummoningGateSelected(gateIndex))
            {
                if (!VerminionGameInteraction.TryClickPaletteIcon((uint)(73 + gateIndex))) Fail($"Stage {challengeStage} defender gate unavailable; no result recorded.");
                return true;
            }
            if (!VerminionGameInteraction.TrySummonPaletteSlot(VerminionGameInteraction.FindPaletteMinion(bossStrategy.CurrentDefenderMinion)))
            { Fail($"Stage {challengeStage} defender summon unavailable; no result recorded."); return true; }
            if (bossStrategy.Defenders.Pending == 0) bossDefenderSpawnUtc = now;
            bossStrategy.Defenders.Requested();
            log.Information($"[VerminionControl] Stage {challengeStage} summon defender; living={defenders.Length}; pending={bossStrategy.Defenders.Pending}; capacity={used}/{capacity}");
            return true;
        }
        // Refresh the defensive group's position and select it for its native
        // defensive special when enemies threaten the remaining stone.
        var deployed = defenders.Except(ready).ToArray();
        var regroup = challengeStage == 19 && threat == null &&
            deployed.Any(unit => Vector3.DistanceSquared(unit.Position, defensePosition) >= 16);
        if (deployed.Length > 0 && (threat != null || regroup) &&
            (now - bossDefenseOrderUtc).TotalSeconds >= (challengeStage == 19 ? 5 : threat?.GameObjectId == bossDefenseTarget ? 30 : 8) &&
            (regroup || challengeStage != 19 || deployed.Count(unit => Vector3.DistanceSquared(unit.Position, defensePosition) < 4) < (deployed.Length + 1) / 2))
        {
            var anchor = regroup
                ? deployed.OrderByDescending(unit => Vector3.DistanceSquared(unit.Position, defensePosition)).First()
                : deployed.OrderBy(unit => Vector3.DistanceSquared(unit.Position, stone.Position)).First();
            TrySelectGroup(challengeStage == 19 ? defensePosition : threat!.Position,
                bossStrategy.CurrentDefenderName, 1, anchor.Position, threat?.GameObjectId);
            bossDefenseOrderUtc = now;
            bossDefenseTarget = threat?.GameObjectId ?? 0;
            log.Information($"[VerminionControl] Stage {challengeStage} defend {stone.Name}; intercept={(regroup ? "regroup idle defenders" : threat?.Name.TextValue)}; defenders={defenders.Length}");
            return true;
        }
        return false;
    }

    // null means a bounded camera adjustment is in flight; false is unavailable.
    private bool? TryFrameTutorialPoint(Vector3 world, out Vector2 point)
    {
        var projected = VerminionGameInteraction.ProjectBattlefield(world, out point);
        if (projected && VerminionGameInteraction.IsBattlefieldPointVisible(point)) return true;
        if (cameraAdjustments == 0 && (IsStoneStage || challengeStage is 9 or 12 or 15 or 19 or 23 or 24) &&
            VerminionGameInteraction.TryFocusMiniMap(world, out var mapClick))
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
        var duty = progress.PendingDuty;
        // Persist cancellation before the native request, so a reload cannot
        // adopt the still-visible queue while its withdrawal is settling.
        progress.AbandonMatch();
        configManager.SaveAccount(ownerAccount);
        if (Plugin.PlayerState.ContentId == owner && duty is >= 552 and <= 575)
            VerminionGameInteraction.CancelChallengeQueue((int)duty - 551);
    }

    private void SetState(VerminionState state, string message)
    {
        log.Information($"[Verminion] {State} -> {state}: {message}");
        State = state;
        reason = message;
        stateEnteredAt = DateTime.UtcNow;
    }
}
