using System;
using Dalamud.Plugin.Services;
using VERMAXION.IPC;
using VERMAXION.Models;
using static VERMAXION.Services.GameHelpers;

namespace VERMAXION.Services;

public class VermaxionEngine
{
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

    private EngineState state = EngineState.Idle;
    private DateTime stateEnteredAt = DateTime.MinValue;
    private CharacterConfig? activeConfig = null;
    private bool weeklyResetDetected = false;
    private bool dailyResetDetected = false;

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
        EnablingHenchman,
        SignalingARDone,
        Complete,
        Error,
    }

    public EngineState State => state;
    public bool IsRunning => state != EngineState.Idle && state != EngineState.Complete && state != EngineState.Error;
    public string StatusText { get; private set; } = "Idle";
    
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
        IClientState clientState)
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
        yesAlreadyIPC.Unpause();
        SetState(EngineState.Idle);
    }

    public void Stop()
    {
        log.Information("[Engine] Stopped by user");
        SetState(EngineState.Idle);
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
                        // NEW: Update new DateTime system
                        activeConfig.VerminionLastCompleted = DateTime.UtcNow;
                        activeConfig.VerminionNextReset = ResetDetectionService.GetNextWeeklyReset(DateTime.UtcNow);
                        
                        // Keep legacy flags for compatibility
                        activeConfig.VerminionCompletedThisWeek = true;
                        configManager.SaveCurrentAccount();
                        verminionService.Reset();
                        AdvanceToNextTask(EngineState.RunningVerminion);
                    }
                    else if (verminionService.IsFailed)
                    {
                        log.Warning("[Engine] Verminion failed - continuing");
                        MarkWeeklyTaskFailed(
                            taskName: "Verminion",
                            setLastCompleted: value => activeConfig.VerminionLastCompleted = value,
                            setNextReset: value => activeConfig.VerminionNextReset = value,
                            clearLegacyFlag: () => activeConfig.VerminionCompletedThisWeek = false);
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
                if (activeConfig!.EnableMiniCactpot &&
                    ResetDetectionService.TaskNeedsRun(activeConfig.MiniCactpotLastCompleted, activeConfig.MiniCactpotNextReset))
                {
                    if (!cactpotService.IsActive && !cactpotService.IsComplete && !cactpotService.IsFailed)
                    {
                        // Clean slate before starting Mini Cactpot
                        log.Information("[Engine] Clean slate: clearing open UI before Mini Cactpot");
                        ResetInteractionState();
                        
                        cactpotService.StartMiniCactpot();
                        return;
                    }

                    cactpotService.Update();

                    if (cactpotService.IsComplete)
                    {
                        // NEW: Update new DateTime system
                        activeConfig.MiniCactpotLastCompleted = DateTime.UtcNow;
                        activeConfig.MiniCactpotNextReset = ResetDetectionService.GetNextDailyReset(DateTime.UtcNow);
                        
                        // Keep legacy flags for compatibility
                        activeConfig.MiniCactpotCompletedToday = true;
                        configManager.SaveCurrentAccount();
                        cactpotService.Reset();
                        AdvanceToNextTask(EngineState.RunningMiniCactpot);
                    }
                    else if (cactpotService.IsFailed)
                    {
                        log.Warning("[Engine] Mini Cactpot failed - continuing");
                        MarkDailyTaskFailed(
                            taskName: "Mini Cactpot",
                            setLastCompleted: value => activeConfig.MiniCactpotLastCompleted = value,
                            setNextReset: value => activeConfig.MiniCactpotNextReset = value,
                            clearLegacyFlag: () => activeConfig.MiniCactpotCompletedToday = false);
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
                if (activeConfig!.EnableJumboCactpot &&
                    ResetDetectionService.TaskNeedsRun(activeConfig.JumboCactpotLastCompleted, activeConfig.JumboCactpotNextReset))
                {
                    var now = DateTime.UtcNow;
                    var runSaturdayPayout = ResetDetectionService.IsJumboCactpotPayoutAvailable(now);

                    if (!cactpotService.IsActive && !cactpotService.IsComplete && !cactpotService.IsFailed)
                    {
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

                    cactpotService.Update();

                    if (cactpotService.IsComplete)
                    {
                        activeConfig.JumboCactpotLastCompleted = now;
                        activeConfig.JumboCactpotNextReset = runSaturdayPayout
                            ? ResetDetectionService.GetNextWeeklyReset(now)
                            : ResetDetectionService.GetNextSaturdayAvailability(now);
                        activeConfig.JumboCactpotCompletedThisWeek = runSaturdayPayout;
                        configManager.SaveCurrentAccount();
                        cactpotService.Reset();
                        AdvanceToNextTask(EngineState.RunningJumboCactpot);
                    }
                    else if (cactpotService.IsFailed)
                    {
                        log.Warning("[Engine] Jumbo Cactpot failed - continuing");
                        MarkJumboCactpotFailed(runSaturdayPayout);
                        cactpotService.Reset();
                        AdvanceToNextTask(EngineState.RunningJumboCactpot);
                    }
                }
                else
                {
                    AdvanceToNextTask(EngineState.RunningJumboCactpot);
                }
                break;

            case EngineState.RunningFashionReport:
                if (activeConfig!.EnableFashionReport &&
                    ResetDetectionService.IsFashionReportAvailable(DateTime.UtcNow) &&
                    ResetDetectionService.TaskNeedsRun(activeConfig.FashionReportLastCompleted, activeConfig.FashionReportNextReset))
                {
                    if (!fashionReportService.IsActive && !fashionReportService.IsComplete && !fashionReportService.IsFailed)
                    {
                        // Clean slate before starting Fashion Report
                        log.Information("[Engine] Clean slate: clearing open UI before Fashion Report");
                        ResetInteractionState();
                        
                        log.Information("[Engine] Starting Fashion Report (Friday)");
                        fashionReportService.Start();
                        return;
                    }

                    fashionReportService.Update();

                    if (fashionReportService.IsComplete)
                    {
                        // NEW: Update new DateTime system
                        activeConfig.FashionReportLastCompleted = DateTime.UtcNow;
                        activeConfig.FashionReportNextReset = ResetDetectionService.GetNextWeeklyReset(DateTime.UtcNow);
                        
                        // Keep legacy flags for compatibility
                        activeConfig.FashionReportCompletedThisWeek = true;
                        configManager.SaveCurrentAccount();
                        fashionReportService.Reset();
                        AdvanceToNextTask(EngineState.RunningFashionReport);
                    }
                    else if (fashionReportService.IsFailed)
                    {
                        log.Warning("[Engine] Fashion Report failed - continuing");
                        MarkWeeklyTaskFailed(
                            taskName: "Fashion Report",
                            setLastCompleted: value => activeConfig.FashionReportLastCompleted = value,
                            setNextReset: value => activeConfig.FashionReportNextReset = value,
                            clearLegacyFlag: () => activeConfig.FashionReportCompletedThisWeek = false);
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
                        // NEW: Update new DateTime system
                        activeConfig.ChocoboRacingLastCompleted = DateTime.UtcNow;
                        activeConfig.ChocoboRacingNextReset = ResetDetectionService.GetNextDailyReset(DateTime.UtcNow);
                        
                        // Keep legacy flags for compatibility
                        activeConfig.ChocoboRacingCompletedToday = true;
                        configManager.SaveCurrentAccount();
                        chocoboRaceService.Reset();
                        AdvanceToNextTask(EngineState.RunningChocoboRacing);
                    }
                    else if (chocoboRaceService.IsFailed)
                    {
                        log.Warning("[Engine] Chocobo Racing failed - continuing");
                        MarkDailyTaskFailed(
                            taskName: "Chocobo Racing",
                            setLastCompleted: value => activeConfig.ChocoboRacingLastCompleted = value,
                            setNextReset: value => activeConfig.ChocoboRacingNextReset = value,
                            clearLegacyFlag: () => activeConfig.ChocoboRacingCompletedToday = false);
                        chocoboRaceService.Reset();
                        AdvanceToNextTask(EngineState.RunningChocoboRacing);
                    }
                }
                else
                {
                    AdvanceToNextTask(EngineState.RunningChocoboRacing);
                }
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

    private void MarkWeeklyTaskFailed(string taskName, Action<DateTime> setLastCompleted, Action<DateTime> setNextReset, Action clearLegacyFlag)
    {
        var now = DateTime.UtcNow;
        setLastCompleted(now);
        setNextReset(ResetDetectionService.GetNextWeeklyReset(now));
        clearLegacyFlag();
        configManager.SaveCurrentAccount();
        log.Warning($"[Engine] {taskName} failed and will be suppressed until the next weekly reset.");
    }

    private void MarkDailyTaskFailed(string taskName, Action<DateTime> setLastCompleted, Action<DateTime> setNextReset, Action clearLegacyFlag)
    {
        var now = DateTime.UtcNow;
        setLastCompleted(now);
        setNextReset(ResetDetectionService.GetNextDailyReset(now));
        clearLegacyFlag();
        configManager.SaveCurrentAccount();
        log.Warning($"[Engine] {taskName} failed and will be suppressed until the next daily reset.");
    }

    private void MarkJumboCactpotFailed(bool runSaturdayPayout)
    {
        var now = DateTime.UtcNow;
        activeConfig!.JumboCactpotLastCompleted = now;
        activeConfig.JumboCactpotNextReset = runSaturdayPayout
            ? ResetDetectionService.GetNextWeeklyReset(now)
            : ResetDetectionService.GetNextSaturdayAvailability(now);
        activeConfig.JumboCactpotCompletedThisWeek = false;
        configManager.SaveCurrentAccount();
        log.Warning("[Engine] Jumbo Cactpot failed and will be suppressed until its next reset window.");
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
            EngineState.RunningChocoboRacing => EngineState.EnablingHenchman,
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
            EngineState.EnablingHenchman => "Enabling Henchman",
            EngineState.SignalingARDone => "Signaling AR",
            EngineState.Complete => "Complete",
            EngineState.Error => "Error",
            _ => "Unknown",
        };
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
