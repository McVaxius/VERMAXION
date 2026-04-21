using System;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using VERMAXION.IPC;
using VERMAXION.Models;
using static VERMAXION.Services.GameHelpers;

namespace VERMAXION.Services;

public class VermaxionEngine
{
    private static readonly string[] RunShutdownCommands =
    [
        "/rotation cancel",
        "/vbmai off",
        "/bmrai off",
        "/wrath auto off",
		"/vnavmesh stop",
		"/visland stop",
        "/ad stop",
        "/sice stop",
        "/ochillegal off",
        "/fr off",
    ];

    private readonly IPluginLog log;
    private readonly ConfigManager configManager;
    private readonly ResetDetectionService resetService;
    private readonly HenchmanService henchmanService;
    private readonly FCBuffService fcBuffService;
    private readonly VerminionService verminionService;
    private readonly CactpotService cactpotService;
    private readonly ChocoboRaceService chocoboRaceService;
    private readonly FashionReportService fashionReportService;
    private readonly VendorStockService vendorStockService;
    private readonly RegisterRegistrablesService registerRegistrablesService;
    private readonly ARPostProcessService arService;
    private readonly YesAlreadyIPC yesAlreadyIPC;
    private readonly IClientState clientState;
    private readonly MomIPCClient momIPCClient;
    private readonly DadIPCClient dadIPCClient;

    private EngineState state = EngineState.Idle;
    private DateTime stateEnteredAt = DateTime.MinValue;
    private CharacterConfig? activeConfig = null;
    private bool weeklyResetDetected = false;
    private bool dailyResetDetected = false;
    private bool nagYourMomRequestIssued = false;
    private bool nagYourDadRequestIssued = false;
    private bool taskStartHoldLogged = false;
    private EngineState taskStartHoldState = EngineState.Idle;
    private bool? activeJumboCactpotPayoutRoute = null;

    public enum EngineState
    {
        Idle,
        Starting,
        DisablingHenchman,
        CheckingResets,
        RunningFCBuff,
        RunningVendorStock,
        RunningRegisterRegistrables,
        RunningVerminion,
        RunningMiniCactpot,
        RunningJumboCactpot,
        RunningFashionReport,
        RunningChocoboRacing,
        RunningNagYourMom,
        RunningNagYourDad,
        EnablingHenchman,
        SignalingARDone,
        Complete,
        Error,
    }

    public EngineState State => state;
    public bool IsRunning => state != EngineState.Idle && state != EngineState.Complete && state != EngineState.Error;
    public string StatusText { get; private set; } = "Idle";
    public string NagYourMomStatusText { get; private set; } = "Idle";
    public string NagYourDadStatusText { get; private set; } = "Idle";
    
    public bool IsRunningDebug => IsRunning; // For debugging

    public VermaxionEngine(
        IPluginLog log,
        ConfigManager configManager,
        ResetDetectionService resetService,
        HenchmanService henchmanService,
        FCBuffService fcBuffService,
        VerminionService verminionService,
        CactpotService cactpotService,
        ChocoboRaceService chocoboRaceService,
        FashionReportService fashionReportService,
        VendorStockService vendorStockService,
        RegisterRegistrablesService registerRegistrablesService,
        ARPostProcessService arService,
        YesAlreadyIPC yesAlreadyIPC,
        IClientState clientState,
        MomIPCClient momIPCClient,
        DadIPCClient dadIPCClient)
    {
        this.log = log;
        this.configManager = configManager;
        this.resetService = resetService;
        this.henchmanService = henchmanService;
        this.fcBuffService = fcBuffService;
        this.verminionService = verminionService;
        this.cactpotService = cactpotService;
        this.chocoboRaceService = chocoboRaceService;
        this.fashionReportService = fashionReportService;
        this.vendorStockService = vendorStockService;
        this.registerRegistrablesService = registerRegistrablesService;
        this.arService = arService;
        this.yesAlreadyIPC = yesAlreadyIPC;
        this.clientState = clientState;
        this.momIPCClient = momIPCClient;
        this.dadIPCClient = dadIPCClient;

        // Subscribe to territory change events to close menus after teleporting
        clientState.TerritoryChanged += OnTerritoryChanged;
    }

    public void StartPostProcess()
    {
        activeConfig = configManager.GetActiveConfig();
        if (activeConfig == null || !activeConfig.Enabled)
        {
            log.Information("[Engine] Config not found or disabled - skipping");
            SetState(EngineState.SignalingARDone);
            return;
        }

        SendRunShutdownCommandBundle();
        NagYourMomStatusText = "Idle";
        NagYourDadStatusText = "Idle";
        log.Information("[Engine] === Starting Vermaxion post-processing ===");
        SetState(EngineState.Starting);
    }

    public void ManualStart()
    {
        activeConfig = configManager.GetActiveConfig();
        if (activeConfig == null)
        {
            log.Warning("[Engine] No active config for manual start");
            return;
        }

        SendRunShutdownCommandBundle();
        NagYourMomStatusText = "Idle";
        NagYourDadStatusText = "Idle";
        log.Information("[Engine] === Manual start ===");
        SetState(EngineState.Starting);
    }

    public void Cancel()
    {
        log.Warning("[Engine] Cancelled by user");
        if (henchmanService.IsManaging)
            henchmanService.StartHenchman();
        if (arService.IsProcessing)
            arService.FinishPostProcess();
        fcBuffService.Reset();
        vendorStockService.Reset();
        verminionService.Reset();
        cactpotService.Reset();
        chocoboRaceService.Reset();
        momIPCClient.CancelActiveRun();
        dadIPCClient.CancelActiveRun();
        NagYourMomStatusText = "Cancelled";
        NagYourDadStatusText = "Cancelled";
        yesAlreadyIPC.Unpause();
        SetState(EngineState.Idle);
    }

    public void Stop()
    {
        log.Information("[Engine] Stopped by user");
        momIPCClient.CancelActiveRun();
        dadIPCClient.CancelActiveRun();
        NagYourMomStatusText = "Stopped";
        NagYourDadStatusText = "Stopped";
        SetState(EngineState.Idle);
    }

    public void SendRunShutdownCommandBundle()
    {
        foreach (var command in RunShutdownCommands)
            CommandHelper.SendCommand(command);

        log.Information("[Engine] Sent run startup shutdown bundle");
    }

    public int GetPendingTaskCount()
    {
        if (activeConfig == null) return 0;
        
        int count = 0;
        if (activeConfig.EnableFCBuffRefill && !fcBuffService.IsComplete) count++;
        if (activeConfig.EnableVendorStock && !vendorStockService.IsComplete) count++;
        if (activeConfig.EnableVerminionQueue && !verminionService.IsComplete) count++;
        if (activeConfig.EnableMiniCactpot && !cactpotService.IsComplete) count++;
        if (activeConfig.EnableJumboCactpot && !cactpotService.IsComplete) count++;
        if (activeConfig.EnableChocoboRacing && !chocoboRaceService.IsComplete) count++;
        if (ShouldCountNagYourMom(activeConfig)) count++;
        if (ShouldCountNagYourDad(activeConfig)) count++;
        
        return count;
    }

    public void Update()
    {
        if (state == EngineState.Idle || state == EngineState.Complete || state == EngineState.Error)
            return;

        var elapsed = (DateTime.UtcNow - stateEnteredAt).TotalSeconds;

        switch (state)
        {
            case EngineState.Starting:
                if (elapsed < 1.5) return; // AR settle delay
                yesAlreadyIPC.Pause();
                SetState(EngineState.DisablingHenchman);
                break;

            case EngineState.DisablingHenchman:
                if (activeConfig!.EnableHenchmanManagement)
                {
                    henchmanService.StopHenchman();
                    log.Information("[Engine] Henchman disabled");
                }
                SetState(EngineState.CheckingResets);
                break;

            case EngineState.CheckingResets:
                activeConfig = GetLiveActiveConfig();
                weeklyResetDetected = resetService.CheckWeeklyReset(activeConfig!);
                dailyResetDetected = resetService.CheckDailyReset(activeConfig!);
                
                // NEW: Migrate from legacy flags if needed
                resetService.MigrateFromLegacyFlags(activeConfig!);
                
                configManager.SaveCurrentAccount();

                log.Information($"[Engine] Weekly reset: {weeklyResetDetected}, Daily reset: {dailyResetDetected}, Saturday: {resetService.IsSaturday()}");
                SetState(EngineState.RunningFCBuff);
                break;

            case EngineState.RunningFCBuff:
                if (activeConfig!.EnableFCBuffRefill)
                {
                    if (!fcBuffService.IsActive && !fcBuffService.IsComplete && !fcBuffService.IsFailed)
                    {
                        // Clean slate before starting FC Buff
                        log.Information("[Engine] Clean slate: clearing open UI before FC Buff");
                        ResetInteractionState();
                        
                        fcBuffService.Start(activeConfig.FCBuffPurchaseAttempts);
                        return;
                    }

                    fcBuffService.Update();

                    if (fcBuffService.IsComplete || fcBuffService.IsFailed)
                    {
                        if (fcBuffService.IsFailed)
                            log.Warning("[Engine] FC buff refill failed - continuing");
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
                        vendorStockService.Reset();
                        AdvanceToNextTask(EngineState.RunningVendorStock);
                    }
                }
                else
                {
                    AdvanceToNextTask(EngineState.RunningVendorStock);
                }
                break;

            case EngineState.RunningRegisterRegistrables:
                if (activeConfig!.EnableRegisterRegistrables)
                {
                    if (!registerRegistrablesService.IsActive && !registerRegistrablesService.IsComplete && !registerRegistrablesService.IsFailed)
                    {
                        log.Information("[Engine] Starting Register Registrables");
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
                        registerRegistrablesService.Reset();
                        AdvanceToNextTask(EngineState.RunningRegisterRegistrables);
                    }
                }
                else
                {
                    AdvanceToNextTask(EngineState.RunningRegisterRegistrables);
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
                        MarkWeeklyTaskFailed(
                            taskName: "Verminion",
                            setLastCompleted: (config, value) => config.VerminionLastCompleted = value,
                            setNextReset: (config, value) => config.VerminionNextReset = value,
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
                    activeConfig.MiniCactpotTicketsToday < 3 &&
                    ResetDetectionService.TaskNeedsRun(activeConfig.MiniCactpotLastCompleted, activeConfig.MiniCactpotNextReset))
                {
                    if (!cactpotService.IsActive && !cactpotService.IsComplete && !cactpotService.IsFailed)
                    {
                        if (ShouldHoldNonDutyTaskStart("Mini Cactpot"))
                            return;

                        // Clean slate before starting Mini Cactpot
                        log.Information("[Engine] Clean slate: clearing open UI before Mini Cactpot");
                        ResetInteractionState();
                        
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
                        MarkDailyTaskFailed(
                            taskName: "Mini Cactpot",
                            setLastCompleted: (config, value) => config.MiniCactpotLastCompleted = value,
                            setNextReset: (config, value) => config.MiniCactpotNextReset = value,
                            clearLegacyFlag: config => config.MiniCactpotCompletedToday = false);
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
                        
                        log.Information("[Engine] Starting Fashion Report (Friday 01:00 UTC window)");
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
                        MarkWeeklyTaskFailed(
                            taskName: "Fashion Report",
                            setLastCompleted: (config, value) => config.FashionReportLastCompleted = value,
                            setNextReset: (config, value) => config.FashionReportNextReset = value,
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
                        MarkDailyTaskFailed(
                            taskName: "Chocobo Racing",
                            setLastCompleted: (config, value) => config.ChocoboRacingLastCompleted = value,
                            setNextReset: (config, value) => config.ChocoboRacingNextReset = value,
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

            case EngineState.RunningNagYourMom:
                activeConfig = GetLiveActiveConfig();
                RollNagYourMomLocalDay(activeConfig!);
                if (!ShouldRunNagYourMomNow(activeConfig!, out var nagSkipReason))
                {
                    NagYourMomStatusText = nagSkipReason;
                    AdvanceToNextTask(EngineState.RunningNagYourMom);
                    break;
                }

                if (!nagYourMomRequestIssued)
                {
                    if (!momIPCClient.IsReady())
                    {
                        ConsumeNagYourMomAttempt(activeConfig!, "mom IPC is not ready.");
                        log.Warning("[Engine] mom IPC is not ready - deferring until the next AR opportunity");
                        AdvanceToNextTask(EngineState.RunningNagYourMom);
                        break;
                    }

                    if (activeConfig!.NagYourMomStopAtSeriesRank25)
                    {
                        var rankSnapshot = momIPCClient.GetSeriesRank();
                        if (!rankSnapshot.Success)
                        {
                            ConsumeNagYourMomAttempt(activeConfig!, $"Series rank read failed: {rankSnapshot.FailureReason}");
                            log.Warning($"[Engine] nag your mom rank read failed: {rankSnapshot.FailureReason}");
                            AdvanceToNextTask(EngineState.RunningNagYourMom);
                            break;
                        }

                        if (rankSnapshot.Rank >= 25)
                        {
                            NagYourMomStatusText = "Series rank 25 reached";
                            log.Information("[Engine] nag your mom skipped because series rank is already 25");
                            AdvanceToNextTask(EngineState.RunningNagYourMom);
                            break;
                        }
                    }

                    ConsumeNagYourMomAttempt(activeConfig!, $"Requested 1 mom run on {activeConfig!.NagYourMomJob}.");
                    var startResult = momIPCClient.StartCcRuns(1, activeConfig!.NagYourMomJob);
                    NagYourMomStatusText = startResult.Summary;

                    if (startResult.Status is MomRunStatus.Rejected or MomRunStatus.Failed or MomRunStatus.Cancelled)
                    {
                        log.Warning($"[Engine] nag your mom start failed: {startResult.Summary}");
                        AdvanceToNextTask(EngineState.RunningNagYourMom);
                        break;
                    }

                    if (startResult.Status == MomRunStatus.Completed)
                    {
                        log.Information("[Engine] nag your mom completed immediately");
                        AdvanceToNextTask(EngineState.RunningNagYourMom);
                        break;
                    }

                    nagYourMomRequestIssued = true;
                    return;
                }

                var currentMomStatus = momIPCClient.GetStatus();
                NagYourMomStatusText = currentMomStatus.Summary;
                if (currentMomStatus.Status is MomRunStatus.Queued or MomRunStatus.Running)
                    return;

                nagYourMomRequestIssued = false;
                if (currentMomStatus.Status == MomRunStatus.Completed)
                    log.Information("[Engine] nag your mom completed successfully");
                else
                    log.Warning($"[Engine] nag your mom ended with status {currentMomStatus.Status}: {currentMomStatus.Summary}");

                AdvanceToNextTask(EngineState.RunningNagYourMom);
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
                        log.Information("[Engine] nag your dad completed immediately");
                        AdvanceToNextTask(EngineState.RunningNagYourDad);
                        break;
                    }

                    nagYourDadRequestIssued = true;
                    return;
                }

                var currentDadStatus = dadIPCClient.GetStatus();
                NagYourDadStatusText = currentDadStatus.Summary;
                if (currentDadStatus.Status is DadRunStatus.Queued or DadRunStatus.Running)
                    return;

                nagYourDadRequestIssued = false;
                if (currentDadStatus.Status == DadRunStatus.Completed)
                    log.Information("[Engine] nag your dad completed successfully");
                else
                    log.Warning($"[Engine] nag your dad ended with status {currentDadStatus.Status}: {currentDadStatus.Summary}");

                AdvanceToNextTask(EngineState.RunningNagYourDad);
                break;

            case EngineState.EnablingHenchman:
                if (activeConfig!.EnableHenchmanManagement)
                {
                    henchmanService.StartHenchman();
                    log.Information("[Engine] Henchman re-enabled");
                }
                SetState(EngineState.SignalingARDone);
                break;

            case EngineState.SignalingARDone:
                if (arService.IsProcessing)
                {
                    arService.FinishPostProcess();
                    log.Information("[Engine] Signaled AR to continue");
                }
                yesAlreadyIPC.Unpause();
                SetState(EngineState.Complete);
                log.Information("[Engine] === Vermaxion post-processing complete ===");
                break;
        }
    }

    private void MarkWeeklyTaskFailed(string taskName, Action<CharacterConfig, DateTime> setLastCompleted, Action<CharacterConfig, DateTime> setNextReset, Action<CharacterConfig> clearLegacyFlag)
    {
        var now = DateTime.UtcNow;
        PersistCurrentCharacterConfig(config =>
        {
            setLastCompleted(config, now);
            setNextReset(config, ResetDetectionService.GetNextWeeklyReset(now));
            clearLegacyFlag(config);
        }, $"{taskName} failure suppression");
        log.Warning($"[Engine] {taskName} failed and will be suppressed until the next weekly reset.");
    }

    private void MarkDailyTaskFailed(string taskName, Action<CharacterConfig, DateTime> setLastCompleted, Action<CharacterConfig, DateTime> setNextReset, Action<CharacterConfig> clearLegacyFlag)
    {
        var now = DateTime.UtcNow;
        PersistCurrentCharacterConfig(config =>
        {
            setLastCompleted(config, now);
            setNextReset(config, ResetDetectionService.GetNextDailyReset(now));
            clearLegacyFlag(config);
        }, $"{taskName} failure suppression");
        log.Warning($"[Engine] {taskName} failed and will be suppressed until the next daily reset.");
    }

    private void MarkJumboCactpotFailed(bool runSaturdayPayout)
    {
        var now = DateTime.UtcNow;
        PersistCurrentCharacterConfig(config =>
        {
            config.JumboCactpotLastCompleted = now;
            config.JumboCactpotNextReset = runSaturdayPayout
                ? ResetDetectionService.GetNextWeeklyReset(now)
                : ResetDetectionService.GetNextJumboCactpotPayoutAvailability(now);
            config.JumboCactpotCompletedThisWeek = false;
        }, "Jumbo Cactpot failure suppression");
        log.Warning("[Engine] Jumbo Cactpot failed and will be suppressed until its next reset window.");
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
        var next = currentTask switch
        {
            EngineState.RunningFCBuff => EngineState.RunningVendorStock,
            EngineState.RunningVendorStock => EngineState.RunningRegisterRegistrables,
            EngineState.RunningRegisterRegistrables => EngineState.RunningVerminion,
            EngineState.RunningVerminion => EngineState.RunningMiniCactpot,
            EngineState.RunningMiniCactpot => EngineState.RunningJumboCactpot,
            EngineState.RunningJumboCactpot => EngineState.RunningFashionReport,
            EngineState.RunningFashionReport => EngineState.RunningChocoboRacing,
            EngineState.RunningChocoboRacing => EngineState.RunningNagYourMom,
            EngineState.RunningNagYourMom => EngineState.RunningNagYourDad,
            EngineState.RunningNagYourDad => EngineState.EnablingHenchman,
            _ => EngineState.EnablingHenchman,
        };

        SetState(next);
    }

    private void SetState(EngineState newState)
    {
        log.Debug($"[Engine] State: {state} -> {newState}");
        state = newState;
        stateEnteredAt = DateTime.UtcNow;

        StatusText = newState switch
        {
            EngineState.Idle => "Idle",
            EngineState.Starting => "Starting...",
            EngineState.DisablingHenchman => "Disabling Henchman",
            EngineState.CheckingResets => "Checking resets",
            EngineState.RunningFCBuff => "FC Buff Refill",
            EngineState.RunningVendorStock => "Vendor Stock",
            EngineState.RunningRegisterRegistrables => "Register Registrables",
            EngineState.RunningVerminion => "Verminion Queue",
            EngineState.RunningMiniCactpot => "Mini Cactpot",
            EngineState.RunningJumboCactpot => "Jumbo Cactpot",
            EngineState.RunningFashionReport => "Fashion Report",
            EngineState.RunningChocoboRacing => "Chocobo Racing",
            EngineState.RunningNagYourMom => "nag your mom",
            EngineState.RunningNagYourDad => "nag your dad",
            EngineState.EnablingHenchman => "Enabling Henchman",
            EngineState.SignalingARDone => "Signaling AR",
            EngineState.Complete => "Complete",
            EngineState.Error => "Error",
            _ => "Unknown",
        };

        if (newState != EngineState.RunningNagYourMom)
            nagYourMomRequestIssued = false;
        if (newState != EngineState.RunningNagYourDad)
            nagYourDadRequestIssued = false;
        if (newState != EngineState.RunningJumboCactpot)
            activeJumboCactpotPayoutRoute = null;
        if (newState != EngineState.RunningMiniCactpot && newState != EngineState.RunningJumboCactpot)
        {
            taskStartHoldLogged = false;
            taskStartHoldState = EngineState.Idle;
        }
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
        if (config.NagYourMomLastLocalDate.Date == localToday)
            return;

        PersistCurrentCharacterConfig(current =>
        {
            current.NagYourMomAttemptsToday = 0;
            current.NagYourMomLastLocalDate = localToday;
        }, "nag your mom local-day rollover");
    }

    private bool ShouldCountNagYourMom(CharacterConfig config)
    {
        if (!config.EnableNagYourMom || config.NagYourMomRunsPerDay <= 0 || string.IsNullOrWhiteSpace(config.NagYourMomJob))
            return false;

        RollNagYourMomLocalDay(config);
        return config.NagYourMomAttemptsToday < config.NagYourMomRunsPerDay;
    }

    private bool ShouldRunNagYourMomNow(CharacterConfig config, out string reason)
    {
        reason = "nag your mom disabled";

        if (!config.EnableNagYourMom)
            return false;

        if (config.NagYourMomRunsPerDay <= 0)
        {
            reason = "mom runs/day is 0";
            return false;
        }

        if (string.IsNullOrWhiteSpace(config.NagYourMomJob))
        {
            reason = "Set a mom job";
            return false;
        }

        if (config.NagYourMomAttemptsToday >= config.NagYourMomRunsPerDay)
        {
            reason = $"mom daily cap hit ({config.NagYourMomAttemptsToday}/{config.NagYourMomRunsPerDay})";
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

        reason = "Ready";
        return true;
    }

    private void ConsumeNagYourMomAttempt(CharacterConfig config, string statusText)
    {
        PersistCurrentCharacterConfig(current =>
        {
            current.NagYourMomAttemptsToday++;
            current.NagYourMomLastLocalDate = DateTime.Now.Date;
        }, "nag your mom attempt");

        NagYourMomStatusText = statusText;
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

        if (config.NagYourDadDungeonCount > 0 && string.IsNullOrWhiteSpace(config.NagYourDadDungeonName))
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
        if (config.NagYourDadDungeonCount > 0 && !string.IsNullOrWhiteSpace(config.NagYourDadDungeonName))
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

        if (config.NagYourDadDungeonCount > 0 && !string.IsNullOrWhiteSpace(config.NagYourDadDungeonName))
        {
            request.Dungeon = new DadDungeonTask
            {
                Count = Math.Max(1, config.NagYourDadDungeonCount),
                Frequency = string.IsNullOrWhiteSpace(config.NagYourDadDungeonFrequency) ? "per AR" : config.NagYourDadDungeonFrequency.Trim(),
                SelectedDungeon = config.NagYourDadDungeonName.Trim(),
                SelectedJob = config.NagYourDadDungeonJob.Trim().ToUpperInvariant(),
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
                CoordinateWithAuraFarmer = config.NagYourDadCoordinateWithAuraFarmer,
                ValidLocalTimeWindow = new DadTimeWindow
                {
                    StartLocal = config.NagYourDadWindowStartLocal,
                    EndLocal = config.NagYourDadWindowEndLocal,
                },
            };
        }

        return request;
    }

    /// <summary>
    /// Handle territory changes to close menus that might be stuck after teleporting.
    /// This prevents pathing issues when services try to navigate after area changes.
    /// </summary>
    private void OnTerritoryChanged(ushort territoryType)
    {
        try
        {
            log.Information($"[Engine] Territory changed to {territoryType} - clearing open UI");
            ResetInteractionState();
        }
        catch (Exception ex)
        {
            log.Error($"[Engine] Error handling territory change: {ex.Message}");
        }
    }
}
