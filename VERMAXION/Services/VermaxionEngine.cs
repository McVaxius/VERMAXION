using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using VERMAXION.IPC;
using VERMAXION.Models;
using static VERMAXION.Services.GameHelpers;

namespace VERMAXION.Services;

public class VermaxionEngine
{
    private static readonly TimeSpan HandoffQuietPeriod = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan HandoffBlockerLogThrottle = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan HandoffBlockerWarningThrottle = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan HenchmanTakeoverPollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan HenchmanTakeoverLogThrottle = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan NagYourMomLostStatusGrace = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan NagYourMomLostStatusLogThrottle = TimeSpan.FromSeconds(30);
    private static readonly string[] NagYourMomRouteOrder =
    [
        MomRunRoutes.CasualCc,
        MomRunRoutes.Frontline,
        MomRunRoutes.RivalWings,
    ];

    private static readonly string[] StartupMiscCommands =
    [
        "/rotation Cancel",
        "/at enable",
        "/vbmai off",
        "/bmrai off",
        "/wrath auto off",
		//"/vnavmesh stop",
		"/visland stop",
        "/ad stop",
        "/sice stop",
        "/ochillegal off",
        "/fr off",
		"/rotation Settings StartOnCountdown False",
    ];

    private static readonly string[] TaskOwnedAddonNames =
    [
        "SelectString",
        "SelectIconString",
        "SelectYesno",
        "Talk",
        "JournalAccept",
        "Request",
        "FreeCompany",
        "FreeCompanyAction",
        "FreeCompanyExchange",
        "ContentsFinder",
        "ContentsFinderConfirm",
        "GoldSaucerInfo",
        "RaceChocoboResult",
        "ChocoboResult",
        "LovmResult",
        "LotteryDaily",
        "LotteryWeeklyInput",
        "LotteryWeeklyRewardList",
        "FashionCheck",
        "FashionCheckScoreGauge",
        "Shop",
        "RetainerList",
        "RetainerSellList",
        "RetainerSell",
        "RetainerItemTransferList",
        "InventoryRetainerLarge",
        "InventoryRetainer",
        "RetainerGrid0",
        "RetainerGrid1",
        "RetainerGrid2",
        "RetainerGrid3",
        "RetainerGrid4",
        "RetainerCrystalGrid",
        "RetainerTaskAsk",
        "RetainerTaskResult",
        "ContextMenu",
        "RecommendEquip",
    ];

    private static readonly Dictionary<string, EngineState> TaskStateById = new()
    {
        [PostProcessTaskOrder.RefillListings] = EngineState.RunningRetainerListingRefill,
        [PostProcessTaskOrder.FCBuffRefill] = EngineState.RunningFCBuff,
        [PostProcessTaskOrder.VendorStock] = EngineState.RunningVendorStock,
        [PostProcessTaskOrder.Fishing] = EngineState.RunningFishing,
        [PostProcessTaskOrder.RegisterRegistrables] = EngineState.RunningRegisterRegistrables,
        [PostProcessTaskOrder.VerminionQueue] = EngineState.RunningVerminion,
        [PostProcessTaskOrder.MiniCactpot] = EngineState.RunningMiniCactpot,
        [PostProcessTaskOrder.JumboCactpot] = EngineState.RunningJumboCactpot,
        [PostProcessTaskOrder.FashionReport] = EngineState.RunningFashionReport,
        [PostProcessTaskOrder.ChocoboRacing] = EngineState.RunningChocoboRacing,
        [PostProcessTaskOrder.LootGoblinMapGather] = EngineState.RunningLootGoblinMapGather,
        [PostProcessTaskOrder.NagYourMom] = EngineState.RunningNagYourMom,
        [PostProcessTaskOrder.NagYourDad] = EngineState.RunningNagYourDad,
    };

    private static readonly Dictionary<EngineState, string> TaskIdByState =
        TaskStateById.ToDictionary(pair => pair.Value, pair => pair.Key);

    private readonly IPluginLog log;
    private readonly Configuration configuration;
    private readonly ConfigManager configManager;
    private readonly ResetDetectionService resetService;
    private readonly HenchmanService henchmanService;
    private readonly FCBuffService fcBuffService;
    private readonly FCBuffInventoryService fcBuffInventoryService;
    private readonly VerminionService verminionService;
    private readonly CactpotService cactpotService;
    private readonly ChocoboRaceService chocoboRaceService;
    private readonly FashionReportService fashionReportService;
    private readonly VendorStockService vendorStockService;
    private readonly FishingService fishingService;
    private readonly RegisterRegistrablesService registerRegistrablesService;
    private readonly RetainerListingRefillService retainerListingRefillService;
    private readonly WorkshopBellService workshopBellService;
    private readonly ARPostProcessService arService;
    private readonly YesAlreadyIPC yesAlreadyIPC;
    private readonly IClientState clientState;
    private readonly MomIPCClient momIPCClient;
    private readonly DadIPCClient dadIPCClient;
    private readonly LootGoblinMapGatherService lootGoblinMapGatherService;
    private readonly AutoRetainerIPC autoRetainerIPC;
    private readonly VNavmeshIPC vNavmeshIPC;
    private readonly LifestreamIPC lifestreamIPC;
    private readonly VermaxionIncidentWriter incidentWriter;

    private EngineState state = EngineState.Idle;
    private RunTaskPhaseFilter activePhaseFilter = RunTaskPhaseFilter.All;
    private DateTime stateEnteredAt = DateTime.MinValue;
    private CharacterConfig? activeConfig = null;
    private bool weeklyResetDetected = false;
    private bool dailyResetDetected = false;
    private bool nagYourMomRequestIssued = false;
    private string nagYourMomActiveRequestId = string.Empty;
    private string nagYourMomActiveRoute = MomRunRoutes.CasualCc;
    private int nagYourMomRequestedRuns = 0;
    private int nagYourMomLastCompletedRuns = 0;
    private int nagYourMomRouteCursor = 0;
    private DateTime nagYourMomLostStatusSince = DateTime.MinValue;
    private DateTime nagYourMomLostStatusLastLoggedAt = DateTime.MinValue;
    private bool nagYourDadRequestIssued = false;
    private bool taskStartHoldLogged = false;
    private EngineState taskStartHoldState = EngineState.Idle;
    private bool? activeJumboCactpotPayoutRoute = null;
    private DateTime handoffQuietSince = DateTime.MinValue;
    private DateTime handoffBlockerLastLoggedAt = DateTime.MinValue;
    private DateTime handoffMovementStopLastIssuedAt = DateTime.MinValue;
    private string handoffBlockerReason = string.Empty;
    private DateTime handoffBlockerSince = DateTime.MinValue;
    private DateTime handoffBlockerLastWarningAt = DateTime.MinValue;
    private readonly List<EngineState> runQueue = [];
    private int runQueueIndex = -1;
    private bool currentTaskOwnedWorkStarted;
    private bool runOwnedWorkStarted;
    private bool runHadFailure;
    private RunOutcome pendingRunOutcome = RunOutcome.None;
    private string pendingRunSummary = string.Empty;
    private bool requireEnabledConfig;
    private DateTime watchdogLastProgressAt = DateTime.MinValue;
    private string watchdogLastSignature = string.Empty;
    private string watchdogPauseReason = string.Empty;
    private bool gateHenchmanTakeover;
    private DateTime henchmanTakeoverLastCheckedAt = DateTime.MinValue;
    private DateTime henchmanTakeoverLastLoggedAt = DateTime.MinValue;
    private HenchmanTakeoverReadiness henchmanTakeoverReadiness;

    public enum EngineState
    {
        Idle,
        Starting,
        DisablingHenchman,
        CheckingResets,
        RunningFCBuff,
        RunningVendorStock,
        RunningFishing,
        RunningRegisterRegistrables,
        RunningRetainerListingRefill,
        RunningVerminion,
        RunningMiniCactpot,
        RunningJumboCactpot,
        RunningFashionReport,
        RunningChocoboRacing,
        RunningLootGoblinMapGather,
        RunningNagYourMom,
        RunningNagYourDad,
        SettlingTask,
        SettlingFinalHandoff,
        EnablingHenchman,
        SignalingARDone,
        Complete,
        Error,
    }

    private enum RunTaskPhaseFilter
    {
        All,
        BeforeAR,
        AfterAR,
    }

    private sealed record NagYourMomRoutePlan(
        string Route,
        string Label,
        int RemainingRuns,
        bool StopAtSeriesRank25);

    public EngineState State => state;
    public bool IsRunning => state != EngineState.Idle;
    public string StatusText { get; private set; } = "Idle";
    public string NagYourMomStatusText { get; private set; } = "Idle";
    public string NagYourDadStatusText { get; private set; } = "Idle";
    public RunOutcome LastRunOutcome { get; private set; } = RunOutcome.None;
    public string LastRunSummary { get; private set; } = "No run recorded";
    public DateTime? LastRunCompletedAtUtc { get; private set; }
    public string ActiveHandoffBlocker => handoffBlockerReason;
    public bool OwnsActiveWork => IsRunning || runOwnedWorkStarted || arService.IsProcessing;
    public bool OwnsLiveWork => OwnsActiveWork || autoRetainerIPC.SuppressionOwnedByVermaxion;
    
    public bool IsRunningDebug => IsRunning; // For debugging

    public VermaxionEngine(
        IPluginLog log,
        Configuration configuration,
        ConfigManager configManager,
        ResetDetectionService resetService,
        HenchmanService henchmanService,
        FCBuffService fcBuffService,
        FCBuffInventoryService fcBuffInventoryService,
        VerminionService verminionService,
        CactpotService cactpotService,
        ChocoboRaceService chocoboRaceService,
        FashionReportService fashionReportService,
        VendorStockService vendorStockService,
        FishingService fishingService,
        RegisterRegistrablesService registerRegistrablesService,
        RetainerListingRefillService retainerListingRefillService,
        WorkshopBellService workshopBellService,
        ARPostProcessService arService,
        YesAlreadyIPC yesAlreadyIPC,
        IClientState clientState,
        MomIPCClient momIPCClient,
        DadIPCClient dadIPCClient,
        LootGoblinMapGatherService lootGoblinMapGatherService,
        AutoRetainerIPC autoRetainerIPC,
        VNavmeshIPC vNavmeshIPC,
        LifestreamIPC lifestreamIPC,
        VermaxionIncidentWriter incidentWriter)
    {
        this.log = log;
        this.configuration = configuration;
        this.configManager = configManager;
        this.resetService = resetService;
        this.henchmanService = henchmanService;
        this.fcBuffService = fcBuffService;
        this.fcBuffInventoryService = fcBuffInventoryService;
        this.verminionService = verminionService;
        this.cactpotService = cactpotService;
        this.chocoboRaceService = chocoboRaceService;
        this.fashionReportService = fashionReportService;
        this.vendorStockService = vendorStockService;
        this.fishingService = fishingService;
        this.registerRegistrablesService = registerRegistrablesService;
        this.retainerListingRefillService = retainerListingRefillService;
        this.workshopBellService = workshopBellService;
        this.arService = arService;
        this.yesAlreadyIPC = yesAlreadyIPC;
        this.clientState = clientState;
        this.momIPCClient = momIPCClient;
        this.dadIPCClient = dadIPCClient;
        this.lootGoblinMapGatherService = lootGoblinMapGatherService;
        this.autoRetainerIPC = autoRetainerIPC;
        this.vNavmeshIPC = vNavmeshIPC;
        this.lifestreamIPC = lifestreamIPC;
        this.incidentWriter = incidentWriter;

        // Subscribe to territory change events to close menus after teleporting
        clientState.TerritoryChanged += OnTerritoryChanged;
    }

    public bool StartPostProcess()
    {
        return TryBeginRun(RunTaskPhaseFilter.AfterAR, requireEnabled: true, requireWorldReady: false, automatedRun: true, "post-processing");
    }

    public bool StartBeforeAutoRetainer()
    {
        return TryBeginRun(RunTaskPhaseFilter.BeforeAR, requireEnabled: true, requireWorldReady: true, automatedRun: true, "before-AR");
    }

    private bool TryGetBeforeArWorldReady(out string reason)
    {
        var localPlayer = Plugin.ObjectTable.LocalPlayer;
        var charName = localPlayer?.Name.ToString() ?? "";
        var worldName = localPlayer?.HomeWorld.Value.Name.ToString() ?? "";
        var contentId = Plugin.PlayerState.ContentId;
        var loggedIn = clientState.IsLoggedIn;
        var hasLocalPlayer = localPlayer != null;
        var betweenAreas = Plugin.Condition[ConditionFlag.BetweenAreas];
        var betweenAreas51 = Plugin.Condition[ConditionFlag.BetweenAreas51];
        var playerAvailable = IsPlayerAvailable();

        if (loggedIn &&
            hasLocalPlayer &&
            !string.IsNullOrEmpty(charName) &&
            !string.IsNullOrEmpty(worldName) &&
            contentId != 0 &&
            !betweenAreas &&
            !betweenAreas51 &&
            playerAvailable)
        {
            reason = $"character={charName}@{worldName}, contentId={contentId:X16}";
            return true;
        }

        reason = $"loggedIn={loggedIn}, hasLocalPlayer={hasLocalPlayer}, character='{charName}', world='{worldName}', contentId={contentId:X16}, BetweenAreas={betweenAreas}, BetweenAreas51={betweenAreas51}, playerAvailable={playerAvailable}";
        return false;
    }

    public bool ManualStart()
    {
        return TryBeginRun(RunTaskPhaseFilter.All, requireEnabled: false, requireWorldReady: false, automatedRun: false, "manual");
    }

    public void RecordSkippedOpportunity(string summary)
    {
        if (!IsRunning)
            RecordRunCompletion(RunOutcome.Skipped, summary);
    }

    private bool TryBeginRun(RunTaskPhaseFilter phaseFilter, bool requireEnabled, bool requireWorldReady, bool automatedRun, string source)
    {
        if (!LifecyclePolicy.CanStart(IsRunning))
        {
            log.Warning($"[Engine] Rejected overlapping {source} start while state={state}");
            return false;
        }

        ResetRunTracking();
        activePhaseFilter = phaseFilter;
        requireEnabledConfig = requireEnabled;
        gateHenchmanTakeover = LifecyclePolicy.ShouldGateHenchmanTakeover(
            automatedRun,
            afterArPostprocess: phaseFilter == RunTaskPhaseFilter.AfterAR);
        activeConfig = configManager.GetActiveConfig();
        NagYourMomStatusText = "Idle";
        NagYourDadStatusText = "Idle";

        if (requireWorldReady && !TryGetBeforeArWorldReady(out var worldReadyReason))
        {
            log.Warning($"[Engine] Skipping before-AR start because world is not ready: {worldReadyReason}");
            BeginImmediateFinalization(RunOutcome.Skipped, "Before-AR world was not ready");
            return true;
        }

        if (activeConfig == null || (requireEnabled && !activeConfig.Enabled))
        {
            log.Information($"[Engine] Skipping {source}: config missing or disabled");
            BeginImmediateFinalization(RunOutcome.Skipped, $"{source} config missing or disabled");
            return true;
        }

        log.Information($"[Engine] === Starting Vermaxion {source} run ===");
        SetState(EngineState.CheckingResets);
        return true;
    }

    public void Cancel()
    {
        log.Warning("[Engine] Cancelled by user");
        CancelForSettling("Cancelled");
    }

    public void Stop()
    {
        log.Information("[Engine] Stopped by user");
        CancelForSettling("Stopped");
    }

    public void ForceStop()
    {
        log.Warning("[Engine] Full Stop force-releasing ownership");
        momIPCClient.CancelActiveRun();
        dadIPCClient.CancelActiveRun();
        lootGoblinMapGatherService.Cancel();
        ResetTaskServices();
        vNavmeshIPC.Stop();
        TryCloseOwnedUiBestEffort();
        // Henchman stop/restart management is deprecated. Keep the service for
        // readiness only; do not restart Henchman from Full Stop.
        if (arService.IsProcessing)
            arService.FinishPostProcess(force: true);
        yesAlreadyIPC.Unpause();
        autoRetainerIPC.ReleaseSuppressionIfOwned(force: true);
        ResetHandoffTracking();
        RecordRunCompletion(RunOutcome.ForceStopped, "Full Stop force-released ownership");
        ResetRunTracking();
        SetState(EngineState.Idle);
    }

    public void SendRunShutdownCommandBundle()
    {
        foreach (var command in StartupMiscCommands)
            CommandHelper.SendCommand(command);

        log.Information("[Engine] Sent Misc Cmd startup bundle");
    }

    private void SendStartupMiscCommandBundleIfEnabled()
    {
        if (activeConfig?.EnableMiscCmd != true)
        {
            log.Information("[Engine] Misc Cmd startup bundle disabled for this character");
            return;
        }

        SendRunShutdownCommandBundle();
    }

    public int GetPendingTaskCount()
    {
        return Math.Max(0, runQueue.Count - Math.Max(0, runQueueIndex + 1));
    }

    public void Update()
    {
        if (state == EngineState.Idle)
            return;

        try
        {
            if (TickTaskWatchdog())
                return;
            UpdateCore();
        }
        catch (Exception ex)
        {
            log.Error($"[Engine] Unhandled engine exception while state={state}: {ex}");
            WriteIncident("engine-exception", ex.Message);
            CleanupFaultingWork(state, "engine exception");
            runHadFailure = true;
            pendingRunOutcome = RunOutcome.Failed;
            pendingRunSummary = $"Unhandled exception in {state}: {ex.Message}";
            if (LifecyclePolicy.RequiresSettling(runOwnedWorkStarted))
                BeginFinalHandoffSettling();
            else
                SetState(EngineState.SignalingARDone);
        }
    }

    private void UpdateCore()
    {
        var elapsed = (DateTime.UtcNow - stateEnteredAt).TotalSeconds;

        switch (state)
        {
            case EngineState.Starting:
                if (elapsed < 1.5) return; // AR settle delay
                RevalidatePlannedQueue();
                if (runQueue.Count == 0)
                {
                    BeginImmediateFinalization(RunOutcome.Skipped, "Planned tasks were no longer runnable before dispatch");
                    break;
                }

                if (ShouldHoldForHenchmanTakeover())
                    break;

                SendStartupMiscCommandBundleIfEnabled();
                yesAlreadyIPC.Pause();
                // Henchman stop/start management is deprecated.
                DispatchNextQueuedTask();
                break;

            case EngineState.DisablingHenchman:
                DispatchNextQueuedTask();
                break;

            case EngineState.CheckingResets:
                activeConfig = GetLiveActiveConfig();
                if (activeConfig == null || (requireEnabledConfig && !activeConfig.Enabled))
                {
                    BeginImmediateFinalization(RunOutcome.Skipped, "Config became unavailable or disabled before planning");
                    break;
                }

                weeklyResetDetected = resetService.CheckWeeklyReset(activeConfig!);
                dailyResetDetected = resetService.CheckDailyReset(activeConfig!);
                
                // NEW: Migrate from legacy flags if needed
                resetService.MigrateFromLegacyFlags(activeConfig!);
                
                configManager.SaveCurrentAccount();

                log.Information($"[Engine] Weekly reset: {weeklyResetDetected}, Daily reset: {dailyResetDetected}, Saturday: {resetService.IsSaturday()}");
                BuildRunQueue(activeConfig);
                if (runQueue.Count == 0)
                {
                    BeginImmediateFinalization(RunOutcome.Skipped, "No enabled and due tasks");
                    break;
                }

                log.Information($"[Engine] Runnable queue: [{string.Join(", ", runQueue.Select(taskState => TaskIdByState[taskState]))}]");
                SetState(EngineState.Starting);
                break;

            case EngineState.RunningFCBuff:
                if (activeConfig!.EnableFCBuffRefill)
                {
                    if (!fcBuffService.IsActive && !fcBuffService.IsComplete && !fcBuffService.IsFailed)
                    {
                        // Clean slate before starting FC Buff
                        log.Information("[Engine] Clean slate: clearing open UI before FC Buff");
                        ResetInteractionState();

                        MarkCurrentTaskWorkStarted();
                        fcBuffService.Start(activeConfig.FCBuffPurchaseAttempts);
                        return;
                    }

                    fcBuffService.Update();

                    if (fcBuffService.IsComplete || fcBuffService.IsFailed)
                    {
                        if (fcBuffService.IsFailed)
                        {
                            log.Warning("[Engine] FC buff refill failed - continuing");
                            runHadFailure = true;
                        }
                        fcBuffService.Reset();
                        AdvanceToNextTask(EngineState.RunningFCBuff);
                    }
                }
                else
                {
                    AdvanceToNextTask(EngineState.RunningFCBuff);
                }
                break;

            case EngineState.RunningVendorStock:
                if (activeConfig!.EnableVendorStock)
                {
                    if (!vendorStockService.IsActive && !vendorStockService.IsComplete && !vendorStockService.IsFailed)
                    {
                        log.Information("[Engine] Starting Vendor Stock");
                        ResetInteractionState();
                        MarkCurrentTaskWorkStarted();
                        vendorStockService.Start();
                        return;
                    }

                    vendorStockService.Update();

                    if (vendorStockService.IsComplete)
                    {
                        log.Information("[Engine] Vendor Stock completed");
                        vendorStockService.Reset();
                        AdvanceToNextTask(EngineState.RunningVendorStock);
                    }
                    else if (vendorStockService.IsFailed)
                    {
                        log.Warning("[Engine] Vendor Stock failed - continuing");
                        runHadFailure = true;
                        vendorStockService.Reset();
                        AdvanceToNextTask(EngineState.RunningVendorStock);
                    }
                }
                else
                {
                    AdvanceToNextTask(EngineState.RunningVendorStock);
                }
                break;

            case EngineState.RunningFishing:
                activeConfig = GetLiveActiveConfig();
                if (!fishingService.IsActive && !fishingService.IsComplete && !fishingService.IsFailed &&
                    !ShouldRunFishing(activeConfig!))
                {
                    AdvanceToNextTask(EngineState.RunningFishing);
                    break;
                }

                if (!fishingService.IsActive && !fishingService.IsComplete && !fishingService.IsFailed)
                {
                    log.Information("[Engine] Starting Fishing");
                    ResetInteractionState();
                    MarkCurrentTaskWorkStarted();
                    fishingService.Start();
                    return;
                }

                fishingService.Update();

                if (fishingService.IsComplete)
                {
                    log.Information("[Engine] Fishing completed");
                    fishingService.Reset();
                    AdvanceToNextTask(EngineState.RunningFishing);
                }
                else if (fishingService.IsFailed)
                {
                    log.Warning($"[Engine] Fishing failed - continuing: {fishingService.StatusText}");
                    runHadFailure = true;
                    fishingService.Reset();
                    AdvanceToNextTask(EngineState.RunningFishing);
                }
                break;

            case EngineState.RunningRegisterRegistrables:
                if (activeConfig!.EnableRegisterRegistrables)
                {
                    if (!registerRegistrablesService.IsActive && !registerRegistrablesService.IsComplete && !registerRegistrablesService.IsFailed)
                    {
                        log.Information("[Engine] Starting Register Registrables");
                        MarkCurrentTaskWorkStarted();
                        registerRegistrablesService.Start();
                        return;
                    }

                    registerRegistrablesService.Update();

                    if (registerRegistrablesService.IsComplete)
                    {
                        log.Information("[Engine] Register Registrables completed");
                        registerRegistrablesService.Reset();
                        AdvanceToNextTask(EngineState.RunningRegisterRegistrables);
                    }
                    else if (registerRegistrablesService.IsFailed)
                    {
                        log.Warning("[Engine] Register Registrables failed - continuing");
                        runHadFailure = true;
                        registerRegistrablesService.Reset();
                        AdvanceToNextTask(EngineState.RunningRegisterRegistrables);
                    }
                }
                else
                {
                    AdvanceToNextTask(EngineState.RunningRegisterRegistrables);
                }
                break;

            case EngineState.RunningRetainerListingRefill:
                activeConfig = GetLiveActiveConfig();
                if (ShouldRunRefillFromListings(activeConfig!))
                {
                    if (!retainerListingRefillService.IsActive && !retainerListingRefillService.IsComplete && !retainerListingRefillService.IsFailed)
                    {
                        log.Information("[Engine] Starting Retainer Listing Refill");
                        ResetInteractionState();
                        MarkCurrentTaskWorkStarted();
                        retainerListingRefillService.Start(activeConfig!);
                        return;
                    }

                    retainerListingRefillService.Update();

                    if (retainerListingRefillService.IsComplete)
                    {
                        log.Information("[Engine] Retainer Listing Refill completed");
                        PersistRefillFromListingsCompletion(activeConfig!.RefillFromListingsFrequency);
                        retainerListingRefillService.Reset();
                        AdvanceToNextTask(EngineState.RunningRetainerListingRefill);
                    }
                    else if (retainerListingRefillService.IsFailed)
                    {
                        log.Warning($"[Engine] Retainer Listing Refill failed - continuing: {retainerListingRefillService.LastError}");
                        runHadFailure = true;
                        retainerListingRefillService.Reset();
                        AdvanceToNextTask(EngineState.RunningRetainerListingRefill);
                    }
                }
                else
                {
                    AdvanceToNextTask(EngineState.RunningRetainerListingRefill);
                }
                break;

            case EngineState.RunningVerminion:
                activeConfig = GetLiveActiveConfig();
                if (activeConfig!.EnableVerminionQueue &&
                    ResetDetectionService.TaskNeedsRun(activeConfig.VerminionLastCompleted, activeConfig.VerminionNextReset))
                {
                    if (!verminionService.IsActive && !verminionService.IsComplete && !verminionService.IsFailed)
                    {
                        // Clean slate before starting Verminion
                        log.Information("[Engine] Clean slate: clearing open UI before Verminion");
                        ResetInteractionState();

                        MarkCurrentTaskWorkStarted();
                        verminionService.Start();
                        return;
                    }

                    verminionService.Update();

                    if (verminionService.IsComplete)
                    {
                        var completedAt = DateTime.UtcNow;
                        PersistCurrentCharacterConfig(config =>
                        {
                            config.VerminionLastCompleted = completedAt;
                            config.VerminionNextReset = ResetDetectionService.GetNextWeeklyReset(completedAt);
                            config.VerminionCompletedThisWeek = true;
                        }, "Verminion completion");
                        verminionService.Reset();
                        AdvanceToNextTask(EngineState.RunningVerminion);
                    }
                    else if (verminionService.IsFailed)
                    {
                        log.Warning("[Engine] Verminion failed - continuing");
                        runHadFailure = true;
                        MarkWeeklyTaskFailed(
                            taskName: "Verminion",
                            clearLegacyFlag: config => config.VerminionCompletedThisWeek = false);
                        verminionService.Reset();
                        AdvanceToNextTask(EngineState.RunningVerminion);
                    }
                }
                else
                {
                    AdvanceToNextTask(EngineState.RunningVerminion);
                }
                break;

            case EngineState.RunningMiniCactpot:
                activeConfig = GetLiveActiveConfig();
                if (activeConfig!.EnableMiniCactpot &&
                    ResetDetectionService.TaskNeedsRun(activeConfig.MiniCactpotLastCompleted, activeConfig.MiniCactpotNextReset))
                {
                    if (!cactpotService.IsActive && !cactpotService.IsComplete && !cactpotService.IsFailed)
                    {
                        if (ShouldHoldNonDutyTaskStart("Mini Cactpot"))
                            return;

                        // Clean slate before starting Mini Cactpot
                        log.Information("[Engine] Clean slate: clearing open UI before Mini Cactpot");
                        ResetInteractionState();

                        MarkCurrentTaskWorkStarted();
                        cactpotService.StartMiniCactpot();
                        return;
                    }

                    cactpotService.Update();

                    if (cactpotService.IsComplete)
                    {
                        var completedAt = DateTime.UtcNow;
                        PersistCurrentCharacterConfig(config =>
                        {
                            config.MiniCactpotLastCompleted = completedAt;
                            config.MiniCactpotNextReset = ResetDetectionService.GetNextDailyReset(completedAt);
                            config.MiniCactpotCompletedToday = true;
                            config.MiniCactpotTicketsToday = Math.Max(config.MiniCactpotTicketsToday, 3);
                        }, "Mini Cactpot completion");
                        cactpotService.Reset();
                        AdvanceToNextTask(EngineState.RunningMiniCactpot);
                    }
                    else if (cactpotService.IsFailed)
                    {
                        log.Warning("[Engine] Mini Cactpot failed - continuing");
                        runHadFailure = true;
                        PersistCurrentCharacterConfig(config =>
                        {
                            config.MiniCactpotCompletedToday = false;
                            config.MiniCactpotTicketsToday = Math.Clamp(config.MiniCactpotTicketsToday, 0, 3);
                        }, "Mini Cactpot partial failure");
                        cactpotService.Reset();
                        AdvanceToNextTask(EngineState.RunningMiniCactpot);
                    }
                }
                else
                {
                    AdvanceToNextTask(EngineState.RunningMiniCactpot);
                }
                break;

            case EngineState.RunningJumboCactpot:
                activeConfig = GetLiveActiveConfig();
                if (activeConfig!.EnableJumboCactpot &&
                    ResetDetectionService.TaskNeedsRun(activeConfig.JumboCactpotLastCompleted, activeConfig.JumboCactpotNextReset))
                {
                    var now = DateTime.UtcNow;

                    if (!cactpotService.IsActive && !cactpotService.IsComplete && !cactpotService.IsFailed)
                    {
                        if (ShouldHoldNonDutyTaskStart("Jumbo Cactpot"))
                            return;

                        var runSaturdayPayout = ResetDetectionService.IsJumboCactpotPayoutAvailable(now);
                        activeJumboCactpotPayoutRoute = runSaturdayPayout;
                        log.Information($"[Engine] Jumbo Cactpot routing decision: now={now:u}, dc={ResetDetectionService.GetCurrentCharacterJumboDataCenterName()}, payoutAvailable={runSaturdayPayout}");

                        // Clean slate before starting Jumbo Cactpot
                        log.Information("[Engine] Clean slate: clearing open UI before Jumbo Cactpot");
                        ResetInteractionState();
                        MarkCurrentTaskWorkStarted();

                        if (runSaturdayPayout)
                        {
                            log.Information("[Engine] Starting Jumbo Cactpot payout check");
                            cactpotService.StartJumboCactpotCheck();
                        }
                        else
                        {
                            log.Information("[Engine] Starting Jumbo Cactpot ticket purchase");
                            cactpotService.StartJumboCactpot();
                        }
                        return;
                    }

                    var routeWasPayout = activeJumboCactpotPayoutRoute ?? ResetDetectionService.IsJumboCactpotPayoutAvailable(now);
                    cactpotService.Update();

                    if (cactpotService.IsComplete)
                    {
                        var completedAt = DateTime.UtcNow;
                        PersistCurrentCharacterConfig(config =>
                        {
                            config.JumboCactpotLastCompleted = completedAt;
                            config.JumboCactpotNextReset = routeWasPayout
                                ? ResetDetectionService.GetNextWeeklyReset(completedAt)
                                : ResetDetectionService.GetNextJumboCactpotPayoutAvailability(completedAt);
                            config.JumboCactpotCompletedThisWeek = routeWasPayout;
                        }, "Jumbo Cactpot completion");
                        cactpotService.Reset();
                        activeJumboCactpotPayoutRoute = null;
                        AdvanceToNextTask(EngineState.RunningJumboCactpot);
                    }
                    else if (cactpotService.IsFailed)
                    {
                        log.Warning("[Engine] Jumbo Cactpot failed - continuing");
                        runHadFailure = true;
                        MarkJumboCactpotFailed(routeWasPayout);
                        cactpotService.Reset();
                        activeJumboCactpotPayoutRoute = null;
                        AdvanceToNextTask(EngineState.RunningJumboCactpot);
                    }
                }
                else
                {
                    AdvanceToNextTask(EngineState.RunningJumboCactpot);
                }
                break;

            case EngineState.RunningFashionReport:
                activeConfig = GetLiveActiveConfig();
                if (activeConfig!.EnableFashionReport &&
                    ResetDetectionService.IsFashionReportAvailable(DateTime.UtcNow) &&
                    ResetDetectionService.TaskNeedsRun(activeConfig.FashionReportLastCompleted, activeConfig.FashionReportNextReset))
                {
                    if (!fashionReportService.IsActive && !fashionReportService.IsComplete && !fashionReportService.IsFailed)
                    {
                        // Clean slate before starting Fashion Report
                        log.Information("[Engine] Clean slate: clearing open UI before Fashion Report");
                        ResetInteractionState();

                        log.Information("[Engine] Starting Fashion Report (Friday 09:00 UTC through weekly reset)");
                        MarkCurrentTaskWorkStarted();
                        fashionReportService.Start();
                        return;
                    }

                    fashionReportService.Update();

                    if (fashionReportService.IsComplete)
                    {
                        var completedAt = DateTime.UtcNow;
                        PersistCurrentCharacterConfig(config =>
                        {
                            config.FashionReportLastCompleted = completedAt;
                            config.FashionReportNextReset = ResetDetectionService.GetNextWeeklyReset(completedAt);
                            config.FashionReportCompletedThisWeek = true;
                        }, "Fashion Report completion");
                        fashionReportService.Reset();
                        AdvanceToNextTask(EngineState.RunningFashionReport);
                    }
                    else if (fashionReportService.IsFailed)
                    {
                        log.Warning("[Engine] Fashion Report failed - continuing");
                        runHadFailure = true;
                        MarkWeeklyTaskFailed(
                            taskName: "Fashion Report",
                            clearLegacyFlag: config => config.FashionReportCompletedThisWeek = false);
                        fashionReportService.Reset();
                        AdvanceToNextTask(EngineState.RunningFashionReport);
                    }
                }
                else
                {
                    AdvanceToNextTask(EngineState.RunningFashionReport);
                }
                break;

            case EngineState.RunningChocoboRacing:
                activeConfig = GetLiveActiveConfig();
                if (activeConfig!.EnableChocoboRacing &&
                    ResetDetectionService.TaskNeedsRun(activeConfig.ChocoboRacingLastCompleted, activeConfig.ChocoboRacingNextReset))
                {
                    if (!chocoboRaceService.IsActive && !chocoboRaceService.IsComplete && !chocoboRaceService.IsFailed)
                    {
                        // Clean slate before starting Chocobo Racing
                        log.Information("[Engine] Clean slate: clearing open UI before Chocobo Racing");
                        ResetInteractionState();

                        MarkCurrentTaskWorkStarted();
                        chocoboRaceService.Start();
                        return;
                    }

                    chocoboRaceService.Update();

                    if (chocoboRaceService.IsComplete)
                    {
                        var completedAt = DateTime.UtcNow;
                        PersistCurrentCharacterConfig(config =>
                        {
                            config.ChocoboRacingLastCompleted = completedAt;
                            config.ChocoboRacingNextReset = ResetDetectionService.GetNextDailyReset(completedAt);
                            config.ChocoboRacingCompletedToday = true;
                        }, "Chocobo Racing completion");
                        chocoboRaceService.Reset();
                        AdvanceToNextTask(EngineState.RunningChocoboRacing);
                    }
                    else if (chocoboRaceService.IsFailed)
                    {
                        log.Warning("[Engine] Chocobo Racing failed - continuing");
                        runHadFailure = true;
                        MarkDailyTaskFailed(
                            taskName: "Chocobo Racing",
                            clearLegacyFlag: config => config.ChocoboRacingCompletedToday = false);
                        chocoboRaceService.Reset();
                        AdvanceToNextTask(EngineState.RunningChocoboRacing);
                    }
                }
                else
                {
                    AdvanceToNextTask(EngineState.RunningChocoboRacing);
                }
                break;

            case EngineState.RunningLootGoblinMapGather:
                activeConfig = GetLiveActiveConfig();
                if (activeConfig!.EnableLootGoblinMapGather &&
                    ResetDetectionService.TaskNeedsRun(activeConfig.LootGoblinMapGatherLastCompleted, activeConfig.LootGoblinMapGatherNextReset))
                {
                    if (!lootGoblinMapGatherService.IsActive && !lootGoblinMapGatherService.IsComplete && !lootGoblinMapGatherService.IsFailed)
                    {
                        log.Information("[Engine] Starting LootGoblin Map Gather");
                        ResetInteractionState();
                        MarkCurrentTaskWorkStarted();
                        lootGoblinMapGatherService.Start(activeConfig);
                        return;
                    }

                    lootGoblinMapGatherService.Update();

                    if (lootGoblinMapGatherService.IsComplete)
                    {
                        var completedAt = DateTime.UtcNow;
                        PersistCurrentCharacterConfig(config =>
                        {
                            config.LootGoblinMapGatherLastCompleted = completedAt;
                            config.LootGoblinMapGatherNextReset = ResetDetectionService.GetNextDailyReset(completedAt);
                        }, "LootGoblin Map Gather completion");
                        lootGoblinMapGatherService.Reset();
                        AdvanceToNextTask(EngineState.RunningLootGoblinMapGather);
                    }
                    else if (lootGoblinMapGatherService.IsFailed)
                    {
                        log.Warning($"[Engine] LootGoblin Map Gather failed - will retry later: {lootGoblinMapGatherService.StatusText}");
                        runHadFailure = true;
                        lootGoblinMapGatherService.Reset();
                        AdvanceToNextTask(EngineState.RunningLootGoblinMapGather);
                    }
                }
                else
                {
                    AdvanceToNextTask(EngineState.RunningLootGoblinMapGather);
                }
                break;

            case EngineState.RunningNagYourMom:
                activeConfig = GetLiveActiveConfig();
                RollNagYourMomLocalDay(activeConfig!);
                var nagRoutePlan = new NagYourMomRoutePlan(MomRunRoutes.CasualCc, "Casual CC", 0, false);
                if (!nagYourMomRequestIssued && !TryGetNextNagYourMomRoute(activeConfig!, out nagRoutePlan, out var nagSkipReason))
                {
                    NagYourMomStatusText = nagSkipReason;
                    AdvanceToNextTask(EngineState.RunningNagYourMom);
                    break;
                }

                if (!nagYourMomRequestIssued)
                {
                    if (nagRoutePlan.Route == MomRunRoutes.RivalWings)
                    {
                        var gate = momIPCClient.GetRivalWingsAchievementGate();
                        if (gate.DisableRouteRecommended || gate.BothComplete)
                        {
                            DisableNagYourMomRoute(MomRunRoutes.RivalWings, string.IsNullOrWhiteSpace(gate.DisableRouteReason) ? gate.Summary : gate.DisableRouteReason);
                            nagYourMomRouteCursor++;
                            break;
                        }
                    }

                    var momReadiness = momIPCClient.GetReadiness(useCache: false);
                    if (!momReadiness.IpcRegistered)
                    {
                        NagYourMomStatusText = momReadiness.Summary;
                        log.Warning($"[Engine] nag your mom unavailable - mom IPC missing: {momReadiness.Summary}");
                        AdvanceToNextTask(EngineState.RunningNagYourMom);
                        break;
                    }

                    var stopAtSeriesRank25 = nagRoutePlan.StopAtSeriesRank25;
                    var startResult = momIPCClient.StartRun(nagRoutePlan.RemainingRuns, activeConfig!.NagYourMomJob, stopAtSeriesRank25, nagRoutePlan.Route);
                    NagYourMomStatusText = startResult.Summary;

                    if (ApplyMomDisableRouteRecommendation(startResult))
                    {
                        nagYourMomRouteCursor++;
                        break;
                    }

                    if (startResult.Status is not (MomRunStatus.Queued or MomRunStatus.Running or MomRunStatus.Completed))
                    {
                        var rejectionReadiness = momIPCClient.GetReadiness(useCache: false);
                        log.Warning(
                            $"[Engine] nag your mom start rejected: status={startResult.Status}, summary={startResult.Summary}, route={startResult.Route}, pluginEnabled={rejectionReadiness.PluginEnabled}, ipcReady={rejectionReadiness.IpcReady}, canStart={rejectionReadiness.CanStart}, blockReason={rejectionReadiness.BlockReason}, startupSummary={rejectionReadiness.StartupSummary}");
                        nagYourMomRouteCursor++;
                        break;
                    }

                    TrackAcceptedNagYourMomRequest(startResult, nagRoutePlan);
                    MarkCurrentTaskWorkStarted();
                    log.Information($"[Engine] nag your mom accepted: route={startResult.Route}, job={activeConfig!.NagYourMomJob}, requestedRuns={nagYourMomRequestedRuns}, stopAtSeriesRank25={stopAtSeriesRank25}, status={startResult.Status}");

                    if (startResult.Status == MomRunStatus.Completed)
                    {
                        CreditNagYourMomTerminalResult(startResult);
                        ClearNagYourMomTracking();
                        nagYourMomRouteCursor++;
                        log.Information("[Engine] nag your mom completed immediately");
                        break;
                    }

                    nagYourMomRequestIssued = true;
                    return;
                }

                if (!momIPCClient.TryGetStatus(out var currentMomStatus))
                {
                    NagYourMomStatusText = currentMomStatus.Summary;
                    if (ShouldWaitForLostNagYourMomStatus(currentMomStatus.Summary))
                        return;

                    var creditedAfterLostStatus = CreditNagYourMomRunCount(nagYourMomActiveRoute, nagYourMomLastCompletedRuns, currentMomStatus.Summary);
                    runHadFailure = true;
                    log.Warning($"[Engine] nag your mom status lost after active request; advancing as failed after grace. reason={currentMomStatus.FailureReason}, creditedRuns={creditedAfterLostStatus}, requestedRuns={nagYourMomRequestedRuns}");
                    ClearNagYourMomTracking();
                    nagYourMomRouteCursor++;
                    break;
                }

                NagYourMomStatusText = currentMomStatus.Summary;
                ApplyMomDisableRouteRecommendation(currentMomStatus);
                nagYourMomLostStatusSince = DateTime.MinValue;
                nagYourMomLastCompletedRuns = Math.Max(nagYourMomLastCompletedRuns, currentMomStatus.CompletedRunCount);
                if (currentMomStatus.Status is MomRunStatus.Queued or MomRunStatus.Running)
                    return;

                if (currentMomStatus.Status == MomRunStatus.Idle)
                {
                    if (ShouldWaitForLostNagYourMomStatus("mom returned Idle during an active Nag Mom request."))
                        return;

                    var creditedAfterIdle = CreditNagYourMomRunCount(nagYourMomActiveRoute, nagYourMomLastCompletedRuns, currentMomStatus.Summary);
                    runHadFailure = true;
                    log.Warning($"[Engine] nag your mom returned Idle during active request; advancing as failed after grace. creditedRuns={creditedAfterIdle}, requestedRuns={nagYourMomRequestedRuns}");
                    ClearNagYourMomTracking();
                    nagYourMomRouteCursor++;
                    break;
                }

                if (currentMomStatus.Status == MomRunStatus.Completed)
                {
                    var creditedRuns = CreditNagYourMomTerminalResult(currentMomStatus);
                    ClearNagYourMomTracking();
                    nagYourMomRouteCursor++;
                    log.Information($"[Engine] nag your mom completed successfully: route={currentMomStatus.Route}, creditedRuns={creditedRuns}, completedRuns={currentMomStatus.CompletedRunCount}, requestedRuns={currentMomStatus.RequestedRunCount}");
                }
                else
                {
                    var creditedRuns = CreditNagYourMomTerminalResult(currentMomStatus);
                    runHadFailure = true;
                    ClearNagYourMomTracking();
                    nagYourMomRouteCursor++;
                    log.Warning($"[Engine] nag your mom ended with status {currentMomStatus.Status}: {currentMomStatus.Summary}; creditedRuns={creditedRuns}, completedRuns={currentMomStatus.CompletedRunCount}, requestedRuns={currentMomStatus.RequestedRunCount}");
                }

                break;

            case EngineState.RunningNagYourDad:
                activeConfig = GetLiveActiveConfig();
                if (!ShouldRunNagYourDadNow(activeConfig!, out var dadSkipReason))
                {
                    NagYourDadStatusText = dadSkipReason;
                    AdvanceToNextTask(EngineState.RunningNagYourDad);
                    break;
                }

                if (!nagYourDadRequestIssued)
                {
                    if (!dadIPCClient.IsReady())
                    {
                        NagYourDadStatusText = "dad IPC is not ready.";
                        log.Warning("[Engine] dad IPC is not ready - deferring until the next AR opportunity");
                        AdvanceToNextTask(EngineState.RunningNagYourDad);
                        break;
                    }

                    var dadRequest = BuildDadRunRequest(activeConfig!);
                    if (dadRequest.GetConfiguredTaskCount() == 0)
                    {
                        NagYourDadStatusText = "No dad tasks configured.";
                        AdvanceToNextTask(EngineState.RunningNagYourDad);
                        break;
                    }

                    var dadStartResult = dadIPCClient.StartTasks(dadRequest);
                    NagYourDadStatusText = dadStartResult.Summary;

                    if (dadStartResult.Status is DadRunStatus.Rejected or DadRunStatus.Failed or DadRunStatus.Cancelled)
                    {
                        log.Warning($"[Engine] nag your dad start failed: {dadStartResult.Summary}");
                        AdvanceToNextTask(EngineState.RunningNagYourDad);
                        break;
                    }

                    if (dadStartResult.Status == DadRunStatus.Completed)
                    {
                        MarkCurrentTaskWorkStarted();
                        log.Information("[Engine] nag your dad completed immediately");
                        AdvanceToNextTask(EngineState.RunningNagYourDad);
                        break;
                    }

                    MarkCurrentTaskWorkStarted();
                    nagYourDadRequestIssued = true;
                    return;
                }

                var currentDadStatus = dadIPCClient.GetStatus();
                NagYourDadStatusText = currentDadStatus.Summary;
                if (currentDadStatus.Status is DadRunStatus.Queued or DadRunStatus.WaitingForParticipants or DadRunStatus.Running)
                    return;

                nagYourDadRequestIssued = false;
                if (currentDadStatus.Status == DadRunStatus.Completed)
                    log.Information("[Engine] nag your dad completed successfully");
                else
                {
                    runHadFailure = true;
                    log.Warning($"[Engine] nag your dad ended with status {currentDadStatus.Status}: {currentDadStatus.Summary}");
                }

                AdvanceToNextTask(EngineState.RunningNagYourDad);
                break;

            case EngineState.SettlingTask:
                if (!TickHandoffSettling("task handoff"))
                    break;

                ResetHandoffTracking();
                DispatchNextQueuedTask();
                break;

            case EngineState.SettlingFinalHandoff:
                if (!TickHandoffSettling("final handoff"))
                    break;

                ResetHandoffTracking();
                SetState(EngineState.SignalingARDone);
                break;

            case EngineState.EnablingHenchman:
                BeginFinalHandoffSettling();
                break;

            case EngineState.SignalingARDone:
                var finalBlocker = runOwnedWorkStarted ? GetHandoffBlocker() : null;
                if (finalBlocker != null)
                {
                    log.Information($"[Engine] Final handoff blocker appeared after quiet period: {finalBlocker}");
                    BeginFinalHandoffSettling();
                    break;
                }

                if (arService.IsProcessing)
                {
                    if (!arService.FinishPostProcess())
                    {
                        StatusText = "Waiting to signal AutoRetainer";
                        break;
                    }

                    log.Information("[Engine] Signaled AR to continue");
                }
                if (activePhaseFilter == RunTaskPhaseFilter.BeforeAR)
                {
                    if (!autoRetainerIPC.ReleaseSuppressionIfOwned())
                    {
                        StatusText = "Waiting to release AutoRetainer suppression";
                        break;
                    }
                }

                // Henchman stop/start management is deprecated.

                yesAlreadyIPC.Unpause();
                CompleteRun();
                break;
        }
    }

    private void MarkWeeklyTaskFailed(string taskName, Action<CharacterConfig> clearLegacyFlag)
    {
        PersistCurrentCharacterConfig(config =>
        {
            clearLegacyFlag(config);
        }, $"{taskName} failure");
        log.Warning($"[Engine] {taskName} failed and remains unstamped for a future retry.");
    }

    private void MarkDailyTaskFailed(string taskName, Action<CharacterConfig> clearLegacyFlag)
    {
        PersistCurrentCharacterConfig(config =>
        {
            clearLegacyFlag(config);
        }, $"{taskName} failure");
        log.Warning($"[Engine] {taskName} failed and remains unstamped for a future retry.");
    }

    private void MarkJumboCactpotFailed(bool runSaturdayPayout)
    {
        PersistCurrentCharacterConfig(config =>
        {
            config.JumboCactpotCompletedThisWeek = false;
        }, "Jumbo Cactpot failure");
        log.Warning($"[Engine] Jumbo Cactpot {(runSaturdayPayout ? "payout" : "purchase")} failed and remains unstamped for a future retry.");
    }

    private void PersistRefillFromListingsCompletion(RefillFromListingsFrequency frequency)
    {
        if (frequency == RefillFromListingsFrequency.EveryAR)
            return;

        var completedAt = DateTime.UtcNow;
        PersistCurrentCharacterConfig(config =>
        {
            config.RefillFromListingsLastCompleted = completedAt;
            config.RefillFromListingsNextReset = GetNextRefillFromListingsReset(frequency, completedAt);
        }, "Retainer Listing Refill completion");
    }

    private static bool ShouldRunRefillFromListings(CharacterConfig config)
    {
        if (!config.EnableRefillFromListings)
            return false;

        var now = DateTime.UtcNow;
        return config.RefillFromListingsFrequency switch
        {
            RefillFromListingsFrequency.EveryAR => true,
            RefillFromListingsFrequency.Daily => ResetDetectionService.TaskNeedsRun(config.RefillFromListingsLastCompleted, config.RefillFromListingsNextReset),
            RefillFromListingsFrequency.Weekly => ResetDetectionService.TaskNeedsRun(config.RefillFromListingsLastCompleted, config.RefillFromListingsNextReset),
            RefillFromListingsFrequency.Monthly => ShouldRunMonthlyRefill(config, now),
            _ => ResetDetectionService.TaskNeedsRun(config.RefillFromListingsLastCompleted, config.RefillFromListingsNextReset),
        };
    }

    private static bool ShouldRunMonthlyRefill(CharacterConfig config, DateTime now)
    {
        if (config.RefillFromListingsLastCompleted == DateTime.MinValue)
            return true;

        var lastCompleted = config.RefillFromListingsLastCompleted.ToUniversalTime();
        if (lastCompleted.Year != now.Year || lastCompleted.Month != now.Month)
            return true;

        return config.RefillFromListingsNextReset != DateTime.MinValue && now >= config.RefillFromListingsNextReset;
    }

    private static DateTime GetNextRefillFromListingsReset(RefillFromListingsFrequency frequency, DateTime now)
    {
        return frequency switch
        {
            RefillFromListingsFrequency.Daily => ResetDetectionService.GetNextDailyReset(now),
            RefillFromListingsFrequency.Weekly => ResetDetectionService.GetNextWeeklyReset(now),
            RefillFromListingsFrequency.Monthly => GetFirstDayOfNextUtcMonth(now),
            _ => DateTime.MinValue,
        };
    }

    private static DateTime GetFirstDayOfNextUtcMonth(DateTime now)
    {
        var utc = now.ToUniversalTime();
        var nextMonth = utc.Month == 12
            ? new DateTime(utc.Year + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            : new DateTime(utc.Year, utc.Month + 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return nextMonth;
    }

    private bool ShouldHoldNonDutyTaskStart(string taskName)
    {
        var blockReason = GetNonDutyTaskStartBlockReason();
        if (blockReason == null)
        {
            taskStartHoldLogged = false;
            taskStartHoldState = EngineState.Idle;
            return false;
        }

        StatusText = $"Waiting for duty/queue before {taskName}";
        if (!taskStartHoldLogged || taskStartHoldState != state)
        {
            log.Warning($"[Engine] Holding {taskName} start until duty/queue clears: {blockReason}");
            taskStartHoldLogged = true;
            taskStartHoldState = state;
        }

        return true;
    }

    private static string? GetNonDutyTaskStartBlockReason()
    {
        if (Plugin.Condition[ConditionFlag.BoundByDuty])
            return "BoundByDuty condition is active";
        if (Plugin.Condition[ConditionFlag.InDutyQueue])
            return "InDutyQueue condition is active";
        if (Plugin.Condition[ConditionFlag.WaitingForDuty])
            return "WaitingForDuty condition is active";
        if (Plugin.Condition[ConditionFlag.WaitingForDutyFinder])
            return "WaitingForDutyFinder condition is active";
        if (IsAddonVisible("ContentsFinderConfirm"))
            return "ContentsFinderConfirm is visible";
        if (IsAddonVisible("ContentsFinder"))
            return "ContentsFinder is visible";
        if (!IsPlayerAvailable())
            return "player is not available";

        return null;
    }

    private void AdvanceToNextTask(EngineState currentTask)
    {
        if (!currentTaskOwnedWorkStarted)
        {
            log.Information($"[Engine] {currentTask} did not start owned work; advancing without settling");
            DispatchNextQueuedTask();
            return;
        }

        currentTaskOwnedWorkStarted = false;
        SetState(EngineState.SettlingTask);
    }

    private void BeginFinalHandoffSettling()
    {
        if (LifecyclePolicy.RequiresSettling(runOwnedWorkStarted))
            SetState(EngineState.SettlingFinalHandoff);
        else
            SetState(EngineState.SignalingARDone);
    }

    private void BeginImmediateFinalization(RunOutcome outcome, string summary)
    {
        pendingRunOutcome = outcome;
        pendingRunSummary = summary;
        BeginFinalHandoffSettling();
    }

    private void CancelForSettling(string status)
    {
        momIPCClient.CancelActiveRun();
        dadIPCClient.CancelActiveRun();
        lootGoblinMapGatherService.Cancel();
        ResetTaskServices();
        vNavmeshIPC.Stop();
        TryCloseOwnedUiBestEffort();
        NagYourMomStatusText = status;
        NagYourDadStatusText = status;
        pendingRunOutcome = RunOutcome.Cancelled;
        pendingRunSummary = status;
        BeginFinalHandoffSettling();
    }

    private void MarkCurrentTaskWorkStarted()
    {
        currentTaskOwnedWorkStarted = true;
        runOwnedWorkStarted = true;
        RecordWatchdogProgress("owned work started");
    }

    private void ResetTaskServices()
    {
        fcBuffService.Reset();
        fcBuffInventoryService.Reset();
        vendorStockService.Reset();
        fishingService.Reset();
        registerRegistrablesService.Reset();
        retainerListingRefillService.Reset();
        workshopBellService.Reset();
        lootGoblinMapGatherService.Reset();
        verminionService.Reset();
        cactpotService.Reset();
        fashionReportService.Reset();
        chocoboRaceService.Reset();
    }

    private bool TickTaskWatchdog()
    {
        if (!TaskIdByState.ContainsKey(state))
        {
            ResetWatchdog();
            return false;
        }

        var now = DateTime.UtcNow;
        var pauseReason = GetTaskWatchdogPauseReason();
        if (pauseReason != null)
        {
            if (!string.Equals(watchdogPauseReason, pauseReason, StringComparison.Ordinal))
                log.Information($"[Engine][Watchdog] Suspended in {state}: {pauseReason}");
            watchdogPauseReason = pauseReason;
            watchdogLastProgressAt = now;
            watchdogLastSignature = BuildWatchdogSignature();
            return false;
        }

        if (!string.IsNullOrEmpty(watchdogPauseReason))
        {
            log.Information($"[Engine][Watchdog] Re-armed in {state} after {watchdogPauseReason}");
            watchdogPauseReason = string.Empty;
            watchdogLastProgressAt = now;
            watchdogLastSignature = BuildWatchdogSignature();
            return false;
        }

        var signature = BuildWatchdogSignature();
        if (!string.Equals(signature, watchdogLastSignature, StringComparison.Ordinal))
        {
            watchdogLastSignature = signature;
            watchdogLastProgressAt = now;
            return false;
        }

        if (watchdogLastProgressAt == DateTime.MinValue)
        {
            watchdogLastProgressAt = now;
            return false;
        }

        if (!TaskWatchdogPolicy.ShouldTimeout(now, watchdogLastProgressAt, paused: false))
            return false;

        var timedOutState = state;
        var taskId = TaskIdByState[timedOutState];
        var stalledFor = now - watchdogLastProgressAt;
        var summary = $"{taskId} stalled for {stalledFor.TotalMinutes:F1} minutes without observable progress";
        log.Error($"[Engine][Watchdog] {summary}; cleaning task and continuing queue");
        WriteIncident("task-watchdog-timeout", summary);
        CleanupFaultingWork(timedOutState, "watchdog timeout");
        runHadFailure = true;
        AdvanceToNextTask(timedOutState);
        return true;
    }

    private string? GetTaskWatchdogPauseReason()
    {
        if (!clientState.IsLoggedIn)
            return "client logged out";
        if (Plugin.ObjectTable.LocalPlayer == null)
            return "local player unavailable";
        if (Plugin.Condition[ConditionFlag.BoundByDuty] || Plugin.Condition[ConditionFlag.BoundByDuty56])
            return "duty-bound";
        if (Plugin.Condition[ConditionFlag.InDutyQueue]
            || Plugin.Condition[ConditionFlag.WaitingForDuty]
            || Plugin.Condition[ConditionFlag.WaitingForDutyFinder])
            return "duty queue/wait";
        if (Plugin.Condition[ConditionFlag.InCombat] || Plugin.Condition[ConditionFlag.Casting])
            return "combat/casting";
        if (Plugin.Condition[ConditionFlag.BetweenAreas] || Plugin.Condition[ConditionFlag.BetweenAreas51])
            return "zoning";
        if (Plugin.Condition[ConditionFlag.Occupied]
            || Plugin.Condition[ConditionFlag.OccupiedInQuestEvent]
            || Plugin.Condition[ConditionFlag.OccupiedInCutSceneEvent]
            || Plugin.Condition[ConditionFlag.WatchingCutscene])
            return "occupied/cutscene";
        if (!IsPlayerAvailable())
            return "player unavailable";
        return null;
    }

    private string BuildWatchdogSignature()
        => string.Join(
            '|',
            state,
            runQueueIndex,
            fcBuffService.State,
            vendorStockService.IsActive,
            fishingService.StatusText,
            registerRegistrablesService.State,
            retainerListingRefillService.IsActive,
            workshopBellService.IsActive,
            verminionService.StatusText,
            cactpotService.IsActive,
            fashionReportService.State,
            chocoboRaceService.StatusText,
            lootGoblinMapGatherService.StatusText,
            NagYourMomStatusText,
            NagYourDadStatusText);

    private void RecordWatchdogProgress(string reason)
    {
        watchdogLastProgressAt = DateTime.UtcNow;
        watchdogLastSignature = BuildWatchdogSignature();
        watchdogPauseReason = string.Empty;
        log.Debug($"[Engine][Watchdog] Progress: {reason}");
    }

    private void ResetWatchdog()
    {
        watchdogLastProgressAt = DateTime.MinValue;
        watchdogLastSignature = string.Empty;
        watchdogPauseReason = string.Empty;
    }

    private void CleanupFaultingWork(EngineState faultingState, string reason)
    {
        switch (faultingState)
        {
            case EngineState.RunningFCBuff: fcBuffService.Reset(); break;
            case EngineState.RunningVendorStock: vendorStockService.Reset(); break;
            case EngineState.RunningFishing: fishingService.Reset(); break;
            case EngineState.RunningRegisterRegistrables: registerRegistrablesService.Reset(); break;
            case EngineState.RunningRetainerListingRefill: retainerListingRefillService.Reset(); workshopBellService.Reset(); break;
            case EngineState.RunningVerminion: verminionService.Reset(); break;
            case EngineState.RunningMiniCactpot:
            case EngineState.RunningJumboCactpot: cactpotService.Reset(); break;
            case EngineState.RunningFashionReport: fashionReportService.Reset(); break;
            case EngineState.RunningChocoboRacing: chocoboRaceService.Reset(); break;
            case EngineState.RunningLootGoblinMapGather: lootGoblinMapGatherService.Cancel(); break;
            case EngineState.RunningNagYourMom: momIPCClient.CancelActiveRun(); break;
            case EngineState.RunningNagYourDad: dadIPCClient.CancelActiveRun(); break;
            default: ResetTaskServices(); break;
        }

        vNavmeshIPC.Stop();
        TryCloseOwnedUiBestEffort();
        log.Warning($"[Engine] Cleaned faulting work in {faultingState} after {reason}");
    }

    private void WriteIncident(string type, string summary)
    {
        try
        {
            var taskId = TaskIdByState.TryGetValue(state, out var id) ? id : string.Empty;
            incidentWriter.Write(new VermaxionIncident(
                DateTime.UtcNow,
                type,
                state.ToString(),
                taskId,
                summary,
                $"queueIndex={runQueueIndex}; queue=[{string.Join(',', runQueue)}]; ownedTask={currentTaskOwnedWorkStarted}; ownedRun={runOwnedWorkStarted}; blocker={GetHandoffBlocker() ?? "none"}; signature={BuildWatchdogSignature()}"));
        }
        catch (Exception ex)
        {
            log.Error($"[Engine] Failed to write Vermaxion incident: {ex.Message}");
        }
    }

    private bool TickHandoffSettling(string phase)
    {
        StopMovementForHandoff();
        yesAlreadyIPC.Pause();

        var blocker = GetHandoffBlocker();
        var now = DateTime.UtcNow;
        if (blocker != null)
        {
            var blockerChanged = !string.Equals(handoffBlockerReason, blocker, StringComparison.Ordinal);
            handoffQuietSince = DateTime.MinValue;
            handoffBlockerReason = blocker;
            StatusText = $"Settling: {blocker}";

            if (blockerChanged)
            {
                handoffBlockerSince = now;
                handoffBlockerLastWarningAt = DateTime.MinValue;
                log.Information($"[Engine] Waiting for {phase}: {blocker}");
                handoffBlockerLastLoggedAt = now;
            }
            else if (handoffBlockerLastLoggedAt == DateTime.MinValue ||
                     now - handoffBlockerLastLoggedAt >= HandoffBlockerLogThrottle)
            {
                log.Information($"[Engine] Waiting for {phase}: {blocker}");
                handoffBlockerLastLoggedAt = now;
            }

            if (handoffBlockerSince != DateTime.MinValue &&
                now - handoffBlockerSince >= HandoffBlockerWarningThrottle &&
                (handoffBlockerLastWarningAt == DateTime.MinValue ||
                 now - handoffBlockerLastWarningAt >= HandoffBlockerWarningThrottle))
            {
                log.Warning($"[Engine] Handoff remains blocked for {(now - handoffBlockerSince).TotalSeconds:F0}s: {blocker}. Ownership retained; Full Stop is the only forced release.");
                handoffBlockerLastWarningAt = now;
            }

            return false;
        }

        if (handoffQuietSince == DateTime.MinValue)
        {
            handoffQuietSince = now;
            handoffBlockerReason = string.Empty;
            handoffBlockerLastLoggedAt = DateTime.MinValue;
            log.Information($"[Engine] {phase} is quiet; holding ownership for {HandoffQuietPeriod.TotalSeconds:F0}s");
        }

        var quietFor = now - handoffQuietSince;
        StatusText = $"Settling: quiet {quietFor.TotalSeconds:F1}/{HandoffQuietPeriod.TotalSeconds:F1}s";
        if (quietFor < HandoffQuietPeriod)
            return false;

        log.Information($"[Engine] {phase} quiet-period complete");
        return true;
    }

    private string? GetHandoffBlocker()
    {
        if (fcBuffService.IsActive)
            return $"FC Buff service active ({fcBuffService.StatusText})";
        if (fcBuffInventoryService.IsActive)
            return "FC Buff Inventory service active";
        if (vendorStockService.IsActive)
            return "Vendor Stock service active";
        if (fishingService.IsActive)
            return $"Fishing service active ({fishingService.StatusText})";
        if (registerRegistrablesService.IsActive)
            return "Register Registrables service active";
        if (retainerListingRefillService.IsActive)
            return "Retainer Listing Refill service active";
        if (workshopBellService.IsActive)
            return "Workshop Bell service active";
        if (verminionService.IsActive)
            return $"Verminion service active ({verminionService.StatusText})";
        if (cactpotService.IsActive)
            return $"Cactpot service active ({cactpotService.StatusText})";
        if (fashionReportService.IsActive)
            return $"Fashion Report service active ({fashionReportService.State})";
        if (chocoboRaceService.IsActive)
            return $"Chocobo Racing service active ({chocoboRaceService.StatusText})";
        if (lifestreamIPC.IsBusy())
            return "Lifestream is busy";
        if (Plugin.Condition[ConditionFlag.BoundByDuty] || Plugin.Condition[ConditionFlag.BoundByDuty56])
            return "player is bound by duty";
        if (Plugin.Condition[ConditionFlag.InDutyQueue] ||
            Plugin.Condition[ConditionFlag.WaitingForDuty] ||
            Plugin.Condition[ConditionFlag.WaitingForDutyFinder])
        {
            return "duty queue is active";
        }
        if (Plugin.Condition[ConditionFlag.BetweenAreas] || Plugin.Condition[ConditionFlag.BetweenAreas51])
            return "area transition is active";
        if (Plugin.Condition[ConditionFlag.Occupied] ||
            Plugin.Condition[ConditionFlag.OccupiedInQuestEvent] ||
            Plugin.Condition[ConditionFlag.OccupiedInCutSceneEvent] ||
            Plugin.Condition[ConditionFlag.WatchingCutscene])
        {
            return "player is occupied";
        }
        if (Plugin.Condition[ConditionFlag.InCombat] || Plugin.Condition[ConditionFlag.Casting])
            return "player is in combat or casting";

        var visibleAddon = TaskOwnedAddonNames.FirstOrDefault(IsAddonVisible);
        if (visibleAddon != null)
            return $"{visibleAddon} addon is visible";
        if (!clientState.IsLoggedIn)
            return "client is not logged in";
        if (!IsPlayerAvailable())
            return "player is not available";

        return null;
    }

    private void TryCloseOwnedUiBestEffort(UiCloseFallbackMode fallbackMode = UiCloseFallbackMode.Always)
    {
        var knownAddonWasVisible = TaskOwnedAddonNames.Any(IsAddonVisible);

        foreach (var addonName in TaskOwnedAddonNames)
            TryCloseAddonByCallback(addonName);

        if (UiCloseFallbackPolicy.ShouldPressFallbackEscape(fallbackMode, knownAddonWasVisible))
            ResetInteractionState();
    }

    private void ResetHandoffTracking()
    {
        handoffQuietSince = DateTime.MinValue;
        handoffBlockerLastLoggedAt = DateTime.MinValue;
        handoffMovementStopLastIssuedAt = DateTime.MinValue;
        handoffBlockerReason = string.Empty;
        handoffBlockerSince = DateTime.MinValue;
        handoffBlockerLastWarningAt = DateTime.MinValue;
    }

    private void StopMovementForHandoff()
    {
        var now = DateTime.UtcNow;
        if (handoffMovementStopLastIssuedAt != DateTime.MinValue &&
            now - handoffMovementStopLastIssuedAt < TimeSpan.FromSeconds(1))
        {
            return;
        }

        handoffMovementStopLastIssuedAt = now;
        vNavmeshIPC.Stop();
    }

    private void BuildRunQueue(CharacterConfig config)
    {
        runQueue.Clear();
        runQueueIndex = -1;
        var runnableIds = LifecyclePolicy.BuildRunnableQueue(
            GetNormalizedTaskOrder(),
            _ => true,
            id => ShouldRunTask(id, config));
        runQueue.AddRange(runnableIds.Select(id => TaskStateById[id]));
    }

    private void DispatchNextQueuedTask()
    {
        currentTaskOwnedWorkStarted = false;
        while (++runQueueIndex < runQueue.Count)
        {
            var nextState = runQueue[runQueueIndex];
            var taskId = TaskIdByState[nextState];
            var liveConfig = GetLiveActiveConfig();
            if (!ShouldRunTask(taskId, liveConfig))
            {
                log.Information($"[Engine] Skipping queued task after dispatch revalidation: {taskId}");
                continue;
            }

            SetState(nextState);
            return;
        }

        BeginFinalHandoffSettling();
    }

    private void RevalidatePlannedQueue()
    {
        var liveConfig = GetLiveActiveConfig();
        runQueue.RemoveAll(taskState => !ShouldRunTask(TaskIdByState[taskState], liveConfig));
        runQueueIndex = -1;
    }

    private List<string> GetNormalizedTaskOrder()
    {
        if (PostProcessTaskOrder.Normalize(configuration))
            configuration.Save();

        if (activePhaseFilter == RunTaskPhaseFilter.All)
            return configuration.PostProcessTaskOrder;

        var phase = activePhaseFilter == RunTaskPhaseFilter.BeforeAR
            ? PostProcessTaskPhase.BeforeAR
            : PostProcessTaskPhase.AfterAR;

        return configuration.PostProcessTaskOrder
            .Where(id => configuration.PostProcessTaskPlacement.TryGetValue(id, out var placement) && placement == phase)
            .ToList();
    }

    private bool HasRunnableTaskForPhase(CharacterConfig config, PostProcessTaskPhase phase)
    {
        return GetRunnableTaskIdsForPhase(config, phase).Any();
    }

    public IReadOnlyList<string> GetRunnableTaskIdsForPhase(PostProcessTaskPhase phase)
    {
        return GetRunnableTaskIdsForPhase(configManager.GetActiveConfig(), phase);
    }

    private List<string> GetRunnableTaskIdsForPhase(CharacterConfig config, PostProcessTaskPhase phase)
    {
        if (PostProcessTaskOrder.Normalize(configuration))
            configuration.Save();

        return LifecyclePolicy.BuildRunnableQueue(
            configuration.PostProcessTaskOrder,
            id => configuration.PostProcessTaskPlacement.TryGetValue(id, out var placement) && placement == phase,
            id => ShouldRunTask(id, config));
    }

    private bool ShouldRunTask(string id, CharacterConfig config)
    {
        return id switch
        {
            PostProcessTaskOrder.RefillListings => ShouldRunRefillFromListings(config),
            PostProcessTaskOrder.FCBuffRefill => config.EnableFCBuffRefill,
            PostProcessTaskOrder.VendorStock => config.EnableVendorStock &&
                (config.VendorStockGysahlGreensTarget > 0 || config.VendorStockGrade8DarkMatterTarget > 0),
            PostProcessTaskOrder.Fishing => ShouldRunFishing(config),
            PostProcessTaskOrder.RegisterRegistrables => config.EnableRegisterRegistrables,
            PostProcessTaskOrder.VerminionQueue => config.EnableVerminionQueue &&
                ResetDetectionService.TaskNeedsRun(config.VerminionLastCompleted, config.VerminionNextReset),
            PostProcessTaskOrder.MiniCactpot => config.EnableMiniCactpot &&
                ResetDetectionService.TaskNeedsRun(config.MiniCactpotLastCompleted, config.MiniCactpotNextReset),
            PostProcessTaskOrder.JumboCactpot => config.EnableJumboCactpot &&
                ResetDetectionService.TaskNeedsRun(config.JumboCactpotLastCompleted, config.JumboCactpotNextReset),
            PostProcessTaskOrder.FashionReport => config.EnableFashionReport &&
                ResetDetectionService.IsFashionReportAvailable(DateTime.UtcNow) &&
                ResetDetectionService.TaskNeedsRun(config.FashionReportLastCompleted, config.FashionReportNextReset),
            PostProcessTaskOrder.ChocoboRacing => config.EnableChocoboRacing &&
                ResetDetectionService.TaskNeedsRun(config.ChocoboRacingLastCompleted, config.ChocoboRacingNextReset),
            PostProcessTaskOrder.LootGoblinMapGather => config.EnableLootGoblinMapGather &&
                ResetDetectionService.TaskNeedsRun(config.LootGoblinMapGatherLastCompleted, config.LootGoblinMapGatherNextReset),
            PostProcessTaskOrder.NagYourMom => ShouldRunNagYourMomNow(config) && momIPCClient.GetReadiness().CanStart,
            PostProcessTaskOrder.NagYourDad => ShouldRunNagYourDadNow(config, out _) && dadIPCClient.IsReady(),
            _ => false,
        };
    }

    private bool ShouldRunFishing(CharacterConfig _)
    {
        // Ocean Fishing startup is owned by FishingStartupCoordinator. Keeping
        // it out of the engine prevents unrelated postprocess tasks from running
        // before current-character fishing prep.
        return false;
    }

    private void CompleteRun()
    {
        var outcome = pendingRunOutcome != RunOutcome.None
            ? pendingRunOutcome
            : runHadFailure
                ? RunOutcome.PartialFailure
                : runOwnedWorkStarted
                    ? RunOutcome.Succeeded
                    : RunOutcome.Skipped;
        var summary = !string.IsNullOrWhiteSpace(pendingRunSummary)
            ? pendingRunSummary
            : outcome switch
            {
                RunOutcome.Succeeded => "All planned work completed",
                RunOutcome.PartialFailure => "Run completed with one or more task failures",
                RunOutcome.Skipped => "No runnable work",
                _ => outcome.ToString(),
            };

        RecordRunCompletion(outcome, summary);
        log.Information($"[Engine] === Vermaxion run complete: outcome={outcome}, summary={summary} ===");
        ResetRunTracking();
        SetState(EngineState.Idle);
    }

    private void RecordRunCompletion(RunOutcome outcome, string summary)
    {
        LastRunOutcome = outcome;
        LastRunSummary = summary;
        LastRunCompletedAtUtc = DateTime.UtcNow;
    }

    private void ResetRunTracking()
    {
        runQueue.Clear();
        runQueueIndex = -1;
        currentTaskOwnedWorkStarted = false;
        runOwnedWorkStarted = false;
        runHadFailure = false;
        pendingRunOutcome = RunOutcome.None;
        pendingRunSummary = string.Empty;
        requireEnabledConfig = false;
        gateHenchmanTakeover = false;
        ResetHenchmanTakeoverTracking();
        activeConfig = null;
        activePhaseFilter = RunTaskPhaseFilter.All;
        ResetWatchdog();
    }

    private void SetState(EngineState newState)
    {
        log.Debug($"[Engine] State: {state} -> {newState}");
        state = newState;
        stateEnteredAt = DateTime.UtcNow;
        RecordWatchdogProgress($"state entered {newState}");

        StatusText = newState switch
        {
            EngineState.Idle => "Idle",
            EngineState.Starting => "Starting...",
            EngineState.DisablingHenchman => "Disabling Henchman",
            EngineState.CheckingResets => "Checking resets",
            EngineState.RunningFCBuff => "FC Buff Refill",
            EngineState.RunningVendorStock => "Vendor Stock",
            EngineState.RunningFishing => "Fishing",
            EngineState.RunningRegisterRegistrables => "Register Registrables",
            EngineState.RunningRetainerListingRefill => "Retainer Listing Refill",
            EngineState.RunningVerminion => "Verminion Queue",
            EngineState.RunningMiniCactpot => "Mini Cactpot",
            EngineState.RunningJumboCactpot => "Jumbo Cactpot",
            EngineState.RunningFashionReport => "Fashion Report",
            EngineState.RunningChocoboRacing => "Chocobo Racing",
            EngineState.RunningLootGoblinMapGather => "LootGoblin Map Gather",
            EngineState.RunningNagYourMom => "nag your mom",
            EngineState.RunningNagYourDad => "nag your dad",
            EngineState.SettlingTask => "Settling task handoff",
            EngineState.SettlingFinalHandoff => "Settling final handoff",
            EngineState.EnablingHenchman => "Enabling Henchman",
            EngineState.SignalingARDone => "Signaling AR",
            EngineState.Complete => "Complete",
            EngineState.Error => "Error",
            _ => "Unknown",
        };

        if (newState != EngineState.RunningNagYourMom)
        {
            ClearNagYourMomTracking();
            nagYourMomRouteCursor = 0;
        }
        if (newState != EngineState.RunningNagYourDad)
            nagYourDadRequestIssued = false;
        if (newState != EngineState.RunningLootGoblinMapGather && lootGoblinMapGatherService.IsComplete)
            lootGoblinMapGatherService.Reset();
        if (newState != EngineState.RunningJumboCactpot)
            activeJumboCactpotPayoutRoute = null;
        if (newState != EngineState.RunningMiniCactpot && newState != EngineState.RunningJumboCactpot)
        {
            taskStartHoldLogged = false;
            taskStartHoldState = EngineState.Idle;
        }

        if (newState is EngineState.SettlingTask or EngineState.SettlingFinalHandoff)
        {
            ResetHandoffTracking();
            StopMovementForHandoff();
            yesAlreadyIPC.Pause();
        }
    }

    private bool ShouldHoldForHenchmanTakeover()
    {
        if (!gateHenchmanTakeover)
            return false;

        var now = DateTime.UtcNow;
        if (henchmanTakeoverLastCheckedAt == DateTime.MinValue ||
            now - henchmanTakeoverLastCheckedAt >= HenchmanTakeoverPollInterval)
        {
            henchmanTakeoverReadiness = henchmanService.GetTakeoverReadiness();
            henchmanTakeoverLastCheckedAt = now;

            if (henchmanTakeoverReadiness.AllowTakeover)
            {
                if (henchmanTakeoverLastLoggedAt != DateTime.MinValue)
                    log.Information($"[Engine] Henchman takeover is safe: {henchmanTakeoverReadiness.Reason}");
                ResetHenchmanTakeoverTracking();
                return false;
            }

            if (henchmanTakeoverLastLoggedAt == DateTime.MinValue ||
                now - henchmanTakeoverLastLoggedAt >= HenchmanTakeoverLogThrottle)
            {
                log.Warning($"[Engine] Holding automated run for Henchman: {henchmanTakeoverReadiness.Reason}");
                henchmanTakeoverLastLoggedAt = now;
            }
        }

        if (henchmanTakeoverReadiness.AllowTakeover)
            return false;

        StatusText = $"Waiting for Henchman: {henchmanTakeoverReadiness.DisplayDescription}";
        return true;
    }

    private void ResetHenchmanTakeoverTracking()
    {
        henchmanTakeoverLastCheckedAt = DateTime.MinValue;
        henchmanTakeoverLastLoggedAt = DateTime.MinValue;
        henchmanTakeoverReadiness = default;
    }

    private CharacterConfig GetLiveActiveConfig()
    {
        activeConfig = configManager.GetActiveConfig();
        return activeConfig;
    }

    private void PersistCurrentCharacterConfig(Action<CharacterConfig> update, string reason)
    {
        var liveConfig = GetLiveActiveConfig();
        update(liveConfig);
        configManager.SaveCurrentAccount();
        log.Information($"[Engine] Persisted {reason} for {configManager.CurrentCharacterKey}");
    }

    private static bool TryParseLocalTime(string value, out TimeSpan result)
    {
        return TimeSpan.TryParse(value, out result);
    }

    private static bool IsWithinLocalWindow(TimeSpan now, TimeSpan start, TimeSpan end)
    {
        return start <= end
            ? now >= start && now <= end
            : now >= start || now <= end;
    }

    private void RollNagYourMomLocalDay(CharacterConfig config)
    {
        var localToday = DateTime.Now.Date;
        var ccCurrent = config.NagYourMomLastLocalDate.Date == localToday;
        var frontlineCurrent = config.NagYourMomFrontlineLastLocalDate.Date == localToday;
        var rivalWingsCurrent = config.NagYourMomRivalWingsLastLocalDate.Date == localToday;
        if (ccCurrent && frontlineCurrent && rivalWingsCurrent)
            return;

        PersistCurrentCharacterConfig(current =>
        {
            if (!ccCurrent)
            {
                current.NagYourMomAttemptsToday = 0;
                current.NagYourMomLastLocalDate = localToday;
            }

            if (!frontlineCurrent)
            {
                current.NagYourMomFrontlineAttemptsToday = 0;
                current.NagYourMomFrontlineLastLocalDate = localToday;
            }

            if (!rivalWingsCurrent)
            {
                current.NagYourMomRivalWingsAttemptsToday = 0;
                current.NagYourMomRivalWingsLastLocalDate = localToday;
            }
        }, "nag your mom local-day rollover");
    }

    private bool ShouldCountNagYourMom(CharacterConfig config)
    {
        if (!config.EnableNagYourMom || string.IsNullOrWhiteSpace(config.NagYourMomJob))
            return false;

        RollNagYourMomLocalDay(config);
        return NagYourMomRouteOrder.Any(route => IsNagYourMomRouteDue(config, route));
    }

    private bool ShouldRunNagYourMomNow(CharacterConfig config)
    {
        if (!ShouldCountNagYourMom(config))
            return false;
        if (!TryParseLocalTime(config.NagYourMomWindowStartLocal, out var start) ||
            !TryParseLocalTime(config.NagYourMomWindowEndLocal, out var end))
        {
            return false;
        }

        return IsWithinLocalWindow(DateTime.Now.TimeOfDay, start, end);
    }

    private bool TryGetNextNagYourMomRoute(CharacterConfig config, out NagYourMomRoutePlan plan, out string reason)
    {
        plan = new NagYourMomRoutePlan(MomRunRoutes.CasualCc, "Casual CC", 0, false);
        reason = "nag your mom disabled";

        if (!config.EnableNagYourMom)
            return false;

        if (string.IsNullOrWhiteSpace(config.NagYourMomJob))
        {
            reason = "Set a mom job";
            return false;
        }

        if (!TryParseLocalTime(config.NagYourMomWindowStartLocal, out var start) || !TryParseLocalTime(config.NagYourMomWindowEndLocal, out var end))
        {
            reason = "Invalid mom local-time window";
            return false;
        }

        if (!IsWithinLocalWindow(DateTime.Now.TimeOfDay, start, end))
        {
            reason = $"Outside mom window ({config.NagYourMomWindowStartLocal}-{config.NagYourMomWindowEndLocal})";
            return false;
        }

        for (var index = nagYourMomRouteCursor; index < NagYourMomRouteOrder.Length; index++)
        {
            var route = NagYourMomRouteOrder[index];
            if (!IsNagYourMomRouteDue(config, route))
                continue;

            nagYourMomRouteCursor = index;
            plan = new NagYourMomRoutePlan(
                route,
                FormatNagYourMomRouteLabel(route),
                GetRemainingNagYourMomRuns(config, route),
                route == MomRunRoutes.CasualCc && config.NagYourMomStopAtSeriesRank25);
            reason = "Ready";
            return true;
        }

        reason = "mom route daily caps hit or routes disabled";
        return false;
    }

    private static bool IsNagYourMomRouteDue(CharacterConfig config, string route)
        => IsNagYourMomRouteEnabled(config, route)
           && GetNagYourMomRouteCap(config, route) > 0
           && GetNagYourMomRouteAttempts(config, route) < GetNagYourMomRouteCap(config, route);

    private static bool IsNagYourMomRouteEnabled(CharacterConfig config, string route)
        => route switch
        {
            MomRunRoutes.Frontline => config.EnableNagYourMomFrontline,
            MomRunRoutes.RivalWings => config.EnableNagYourMomRivalWings,
            _ => config.EnableNagYourMomCasualCc,
        };

    private static int GetNagYourMomRouteCap(CharacterConfig config, string route)
        => route switch
        {
            MomRunRoutes.Frontline => config.NagYourMomFrontlineRunsPerDay,
            MomRunRoutes.RivalWings => config.NagYourMomRivalWingsRunsPerDay,
            _ => config.NagYourMomRunsPerDay,
        };

    private static int GetNagYourMomRouteAttempts(CharacterConfig config, string route)
        => route switch
        {
            MomRunRoutes.Frontline => config.NagYourMomFrontlineAttemptsToday,
            MomRunRoutes.RivalWings => config.NagYourMomRivalWingsAttemptsToday,
            _ => config.NagYourMomAttemptsToday,
        };

    private static int GetRemainingNagYourMomRuns(CharacterConfig config, string route)
        => Math.Max(0, GetNagYourMomRouteCap(config, route) - GetNagYourMomRouteAttempts(config, route));

    private static string FormatNagYourMomRouteLabel(string route)
        => route switch
        {
            MomRunRoutes.Frontline => "Frontline",
            MomRunRoutes.RivalWings => "Rival Wings",
            _ => "Casual CC",
        };

    private void TrackAcceptedNagYourMomRequest(MomRunResult result, NagYourMomRoutePlan plan)
    {
        nagYourMomActiveRequestId = result.RequestId ?? string.Empty;
        nagYourMomActiveRoute = string.IsNullOrWhiteSpace(result.Route) ? plan.Route : result.Route;
        nagYourMomRequestedRuns = result.RequestedRunCount > 0 ? result.RequestedRunCount : plan.RemainingRuns;
        nagYourMomLastCompletedRuns = Math.Max(0, result.CompletedRunCount);
        nagYourMomLostStatusSince = DateTime.MinValue;
        nagYourMomLostStatusLastLoggedAt = DateTime.MinValue;
    }

    private void ClearNagYourMomTracking()
    {
        nagYourMomRequestIssued = false;
        nagYourMomActiveRequestId = string.Empty;
        nagYourMomActiveRoute = MomRunRoutes.CasualCc;
        nagYourMomRequestedRuns = 0;
        nagYourMomLastCompletedRuns = 0;
        nagYourMomLostStatusSince = DateTime.MinValue;
        nagYourMomLostStatusLastLoggedAt = DateTime.MinValue;
    }

    private int CreditNagYourMomTerminalResult(MomRunResult result)
    {
        var route = string.IsNullOrWhiteSpace(result.Route) ? nagYourMomActiveRoute : result.Route;
        var observedCompletedRuns = Math.Max(nagYourMomLastCompletedRuns, result.CompletedRunCount);
        var runsToCredit = result.Status == MomRunStatus.Completed
            ? Math.Max(result.RequestedRunCount > 0 ? result.RequestedRunCount : nagYourMomRequestedRuns, observedCompletedRuns)
            : observedCompletedRuns;

        return CreditNagYourMomRunCount(route, runsToCredit, result.Summary);
    }

    private int CreditNagYourMomRunCount(string route, int runCount, string statusText)
    {
        var creditableRuns = Math.Max(0, runCount);
        if (creditableRuns == 0)
        {
            NagYourMomStatusText = statusText;
            return 0;
        }

        var liveConfig = GetLiveActiveConfig();
        var runsToCredit = Math.Min(creditableRuns, GetRemainingNagYourMomRuns(liveConfig, route));
        if (runsToCredit <= 0)
        {
            NagYourMomStatusText = statusText;
            return 0;
        }

        PersistCurrentCharacterConfig(current =>
        {
            CreditNagYourMomRoute(current, route, runsToCredit);
        }, $"nag your mom {FormatNagYourMomRouteLabel(route)} run credit ({runsToCredit})");

        NagYourMomStatusText = statusText;
        return runsToCredit;
    }

    private static void CreditNagYourMomRoute(CharacterConfig config, string route, int runsToCredit)
    {
        var localDate = DateTime.Now.Date;
        switch (route)
        {
            case MomRunRoutes.Frontline:
                config.NagYourMomFrontlineAttemptsToday = Math.Min(
                    config.NagYourMomFrontlineRunsPerDay,
                    config.NagYourMomFrontlineAttemptsToday + runsToCredit);
                config.NagYourMomFrontlineLastLocalDate = localDate;
                break;

            case MomRunRoutes.RivalWings:
                config.NagYourMomRivalWingsAttemptsToday = Math.Min(
                    config.NagYourMomRivalWingsRunsPerDay,
                    config.NagYourMomRivalWingsAttemptsToday + runsToCredit);
                config.NagYourMomRivalWingsLastLocalDate = localDate;
                break;

            default:
                config.NagYourMomAttemptsToday = Math.Min(
                    config.NagYourMomRunsPerDay,
                    config.NagYourMomAttemptsToday + runsToCredit);
                config.NagYourMomLastLocalDate = localDate;
                break;
        }
    }

    private bool ApplyMomDisableRouteRecommendation(MomRunResult result)
    {
        if (!result.DisableRouteRecommended)
            return false;

        return DisableNagYourMomRoute(result.Route, string.IsNullOrWhiteSpace(result.DisableRouteReason) ? result.Summary : result.DisableRouteReason);
    }

    private bool DisableNagYourMomRoute(string route, string reason)
    {
        if (route != MomRunRoutes.RivalWings)
            return false;

        PersistCurrentCharacterConfig(current =>
        {
            current.EnableNagYourMomRivalWings = false;
        }, "nag your mom Rival Wings disable recommendation");

        NagYourMomStatusText = string.IsNullOrWhiteSpace(reason)
            ? "Rival Wings disabled by mom."
            : reason;
        return true;
    }

    private bool ShouldWaitForLostNagYourMomStatus(string reason)
    {
        var now = DateTime.UtcNow;
        var dutyOrQueueActive = IsNagYourMomDutyOrQueueActive();

        if (dutyOrQueueActive)
        {
            nagYourMomLostStatusSince = DateTime.MinValue;
            NagYourMomStatusText = $"{reason} Waiting for active duty/queue to clear.";
            LogNagYourMomLostStatusWait(reason, dutyOrQueueActive, now);
            return true;
        }

        if (nagYourMomLostStatusSince == DateTime.MinValue)
        {
            nagYourMomLostStatusSince = now;
            LogNagYourMomLostStatusWait(reason, dutyOrQueueActive, now);
        }

        if (now - nagYourMomLostStatusSince < NagYourMomLostStatusGrace)
        {
            NagYourMomStatusText = $"{reason} Waiting for mom status recovery.";
            return true;
        }

        return false;
    }

    private void LogNagYourMomLostStatusWait(string reason, bool dutyOrQueueActive, DateTime now)
    {
        if (nagYourMomLostStatusLastLoggedAt != DateTime.MinValue &&
            now - nagYourMomLostStatusLastLoggedAt < NagYourMomLostStatusLogThrottle)
        {
            return;
        }

        log.Warning($"[Engine] nag your mom status unavailable during active request; waiting. reason={reason}, dutyOrQueueActive={dutyOrQueueActive}, requestId={nagYourMomActiveRequestId}, completedRuns={nagYourMomLastCompletedRuns}, requestedRuns={nagYourMomRequestedRuns}");
        nagYourMomLostStatusLastLoggedAt = now;
    }

    private static bool IsNagYourMomDutyOrQueueActive()
    {
        if (Plugin.Condition[ConditionFlag.BoundByDuty] || Plugin.Condition[ConditionFlag.BoundByDuty56])
            return true;
        if (Plugin.Condition[ConditionFlag.InDutyQueue])
            return true;
        if (Plugin.Condition[ConditionFlag.WaitingForDuty])
            return true;
        if (Plugin.Condition[ConditionFlag.WaitingForDutyFinder])
            return true;
        if (Plugin.Condition[ConditionFlag.BetweenAreas] || Plugin.Condition[ConditionFlag.BetweenAreas51])
            return true;
        if (IsAddonVisible("ContentsFinderConfirm") || IsAddonVisible("ContentsFinder"))
            return true;

        return false;
    }

    private bool ShouldCountNagYourDad(CharacterConfig config)
    {
        if (!config.EnableNagYourDad)
            return false;

        return HasNagYourDadConfiguredWork(config);
    }

    private bool ShouldRunNagYourDadNow(CharacterConfig config, out string reason)
    {
        reason = "nag your dad disabled";

        if (!config.EnableNagYourDad)
            return false;

        if (config.NagYourDadDungeonCount > 0 &&
            (string.IsNullOrWhiteSpace(config.NagYourDadDungeonName) || config.NagYourDadDungeonContentFinderConditionId == 0))
        {
            reason = "Set a dad dungeon";
            return false;
        }

        if (config.NagYourDadDailyMsq && string.IsNullOrWhiteSpace(config.NagYourDadLanPartyPreset))
        {
            reason = "Set a Lan Party preset";
            return false;
        }

        if (!HasNagYourDadConfiguredWork(config))
        {
            reason = "Set dad tasks";
            return false;
        }

        if (config.NagYourDadAstropeAttempts > 0)
        {
            if (!TryParseLocalTime(config.NagYourDadWindowStartLocal, out var start) ||
                !TryParseLocalTime(config.NagYourDadWindowEndLocal, out var end))
            {
                reason = "Invalid dad Astrope local-time window";
                return false;
            }

            if (!IsWithinLocalWindow(DateTime.Now.TimeOfDay, start, end))
            {
                reason = $"Outside dad Astrope window ({config.NagYourDadWindowStartLocal}-{config.NagYourDadWindowEndLocal})";
                return false;
            }
        }

        reason = "Ready";
        return true;
    }

    private static bool HasNagYourDadConfiguredWork(CharacterConfig config)
    {
        if (config.NagYourDadDungeonCount > 0 &&
            !string.IsNullOrWhiteSpace(config.NagYourDadDungeonName) &&
            config.NagYourDadDungeonContentFinderConditionId != 0)
            return true;

        if (config.NagYourDadDailyMsq && !string.IsNullOrWhiteSpace(config.NagYourDadLanPartyPreset))
            return true;

        if (config.NagYourDadCommendationAttempts > 0)
            return true;

        if (config.NagYourDadAstropeAttempts > 0)
            return true;

        return false;
    }

    private static DadRunRequest BuildDadRunRequest(CharacterConfig config)
    {
        var request = new DadRunRequest
        {
            RequestedBy = "VERMAXION",
        };

        if (config.NagYourDadDungeonCount > 0 &&
            !string.IsNullOrWhiteSpace(config.NagYourDadDungeonName) &&
            config.NagYourDadDungeonContentFinderConditionId != 0)
        {
            request.Dungeon = new DadDungeonTask
            {
                Count = Math.Max(1, config.NagYourDadDungeonCount),
                Frequency = DadRunRequestOptions.NormalizeFrequency(config.NagYourDadDungeonFrequency),
                ContentFinderConditionId = config.NagYourDadDungeonContentFinderConditionId,
                SelectedDungeon = config.NagYourDadDungeonName.Trim(),
                SelectedJob = config.NagYourDadDungeonJob.Trim().ToUpperInvariant(),
                ExecutionPreference = DadRunRequestOptions.TrustThenDutySupport,
                QueueViaLanParty = config.NagYourDadQueueViaLanParty,
                Unsynced = config.NagYourDadDungeonUnsynced,
            };
        }

        if (config.NagYourDadDailyMsq && !string.IsNullOrWhiteSpace(config.NagYourDadLanPartyPreset))
        {
            request.DailyMsq = new DadDailyMsqTask
            {
                LanPartyPreset = config.NagYourDadLanPartyPreset.Trim(),
            };
        }

        if (config.NagYourDadCommendationAttempts > 0)
        {
            request.Commendation = new DadCommendationTask
            {
                Attempts = config.NagYourDadCommendationAttempts,
            };
        }

        if (config.NagYourDadAstropeAttempts > 0)
        {
            request.Astrope = new DadAstropeTask
            {
                Attempts = config.NagYourDadAstropeAttempts,
                ValidLocalTimeWindow = new DadTimeWindow
                {
                    StartLocal = config.NagYourDadWindowStartLocal,
                    EndLocal = config.NagYourDadWindowEndLocal,
                },
            };
        }

        request.ApplyOrchestrationDefaults();
        return request;
    }

    /// <summary>
    /// Handle territory changes to close menus that might be stuck after teleporting.
    /// This prevents pathing issues when services try to navigate after area changes.
    /// </summary>
    private void OnTerritoryChanged(ushort territoryType)
        => OnTerritoryChanged((uint)territoryType);

    private void OnTerritoryChanged(uint territoryType)
    {
        try
        {
            log.Information($"[Engine] Territory changed to {territoryType} - clearing known task UI");
            TryCloseOwnedUiBestEffort(UiCloseFallbackMode.OnlyWhenKnownAddonVisible);
        }
        catch (Exception ex)
        {
            log.Error($"[Engine] Error handling territory change: {ex.Message}");
        }
    }
}
