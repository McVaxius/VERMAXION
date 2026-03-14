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
                configManager.SaveCurrentAccount();

                log.Information($"[Engine] Weekly reset: {weeklyResetDetected}, Daily reset: {dailyResetDetected}, Saturday: {resetService.IsSaturday()}");
                SetState(EngineState.RunningFCBuff);
                break;

            case EngineState.RunningFCBuff:
                if (activeConfig!.EnableFCBuffRefill)
                {
                    if (!fcBuffService.IsActive && !fcBuffService.IsComplete && !fcBuffService.IsFailed)
                    {
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
                if (activeConfig!.EnableVerminionQueue && weeklyResetDetected && !activeConfig.VerminionCompletedThisWeek)
                {
                    if (!verminionService.IsActive && !verminionService.IsComplete && !verminionService.IsFailed)
                    {
                        verminionService.Start();
                        return;
                    }

                    verminionService.Update();

                    if (verminionService.IsComplete)
                    {
                        activeConfig.VerminionCompletedThisWeek = true;
                        configManager.SaveCurrentAccount();
                        verminionService.Reset();
                        AdvanceToNextTask(EngineState.RunningVerminion);
                    }
                    else if (verminionService.IsFailed)
                    {
                        log.Warning("[Engine] Verminion failed - continuing");
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
                if (activeConfig!.EnableMiniCactpot && dailyResetDetected && !activeConfig.MiniCactpotCompletedToday)
                {
                    if (!cactpotService.IsActive && !cactpotService.IsComplete && !cactpotService.IsFailed)
                    {
                        cactpotService.StartMiniCactpot();
                        return;
                    }

                    cactpotService.Update();

                    if (cactpotService.IsComplete)
                    {
                        activeConfig.MiniCactpotCompletedToday = true;
                        configManager.SaveCurrentAccount();
                        cactpotService.Reset();
                        AdvanceToNextTask(EngineState.RunningMiniCactpot);
                    }
                    else if (cactpotService.IsFailed)
                    {
                        log.Warning("[Engine] Mini Cactpot failed - continuing");
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
                if (activeConfig!.EnableJumboCactpot && weeklyResetDetected && resetService.IsSaturdayAfterReset() && !activeConfig.JumboCactpotCompletedThisWeek)
                {
                    if (!cactpotService.IsActive && !cactpotService.IsComplete && !cactpotService.IsFailed)
                    {
                        log.Information("[Engine] Starting Jumbo Cactpot (Saturday)");
                        cactpotService.StartJumboCactpotCheck();
                        return;
                    }

                    cactpotService.Update();

                    if (cactpotService.IsComplete)
                    {
                        activeConfig.JumboCactpotCompletedThisWeek = true;
                        configManager.SaveCurrentAccount();
                        cactpotService.Reset();
                        AdvanceToNextTask(EngineState.RunningJumboCactpot);
                    }
                    else if (cactpotService.IsFailed)
                    {
                        log.Warning("[Engine] Jumbo Cactpot failed - continuing");
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
                if (activeConfig!.EnableFashionReport && weeklyResetDetected && resetService.IsFriday() && !activeConfig.FashionReportCompletedThisWeek)
                {
                    if (!fashionReportService.IsActive && !fashionReportService.IsComplete && !fashionReportService.IsFailed)
                    {
                        log.Information("[Engine] Starting Fashion Report (Friday)");
                        fashionReportService.Start();
                        return;
                    }

                    fashionReportService.Update();

                    if (fashionReportService.IsComplete)
                    {
                        activeConfig.FashionReportCompletedThisWeek = true;
                        configManager.SaveCurrentAccount();
                        fashionReportService.Reset();
                        AdvanceToNextTask(EngineState.RunningFashionReport);
                    }
                    else if (fashionReportService.IsFailed)
                    {
                        log.Warning("[Engine] Fashion Report failed - continuing");
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
                if (activeConfig!.EnableChocoboRacing && dailyResetDetected && !activeConfig.ChocoboRacingCompletedToday)
                {
                    if (!chocoboRaceService.IsActive && !chocoboRaceService.IsComplete && !chocoboRaceService.IsFailed)
                    {
                        chocoboRaceService.Start();
                        return;
                    }

                    chocoboRaceService.Update();

                    if (chocoboRaceService.IsComplete)
                    {
                        activeConfig.ChocoboRacingCompletedToday = true;
                        configManager.SaveCurrentAccount();
                        chocoboRaceService.Reset();
                        AdvanceToNextTask(EngineState.RunningChocoboRacing);
                    }
                    else if (chocoboRaceService.IsFailed)
                    {
                        log.Warning("[Engine] Chocobo Racing failed - continuing");
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

    private void AdvanceToNextTask(EngineState currentTask)
    {
        var next = currentTask switch
        {
            EngineState.RunningFCBuff => EngineState.RunningRegisterRegistrables,
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
            log.Information($"[Engine] Territory changed to {territoryType} - pressing numpad+ to close menus");
            
            // Press numpad+ to close any menus that might be stuck after teleporting
            // This prevents pathing services from getting stuck when they try to start navigation
            SendNumpadPlus();
        }
        catch (Exception ex)
        {
            log.Error($"[Engine] Error handling territory change: {ex.Message}");
        }
    }
}
