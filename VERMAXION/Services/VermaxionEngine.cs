using System;
using Dalamud.Plugin.Services;
using VERMAXION.Models;

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
    private readonly ARPostProcessService arService;

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
        RunningVerminion,
        RunningMiniCactpot,
        RunningJumboCactpot,
        RunningChocoboRacing,
        EnablingHenchman,
        SignalingARDone,
        Complete,
        Error,
    }

    public EngineState State => state;
    public bool IsRunning => state != EngineState.Idle && state != EngineState.Complete && state != EngineState.Error;
    public string StatusText { get; private set; } = "Idle";

    public VermaxionEngine(
        IPluginLog log,
        ConfigManager configManager,
        ResetDetectionService resetService,
        HenchmanService henchmanService,
        FCBuffService fcBuffService,
        VerminionService verminionService,
        CactpotService cactpotService,
        ChocoboRaceService chocoboRaceService,
        ARPostProcessService arService)
    {
        this.log = log;
        this.configManager = configManager;
        this.resetService = resetService;
        this.henchmanService = henchmanService;
        this.fcBuffService = fcBuffService;
        this.verminionService = verminionService;
        this.cactpotService = cactpotService;
        this.chocoboRaceService = chocoboRaceService;
        this.arService = arService;
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
        SetState(EngineState.Idle);
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
                if (activeConfig!.EnableJumboCactpot && weeklyResetDetected && resetService.IsSaturday() && !activeConfig.JumboCactpotCompletedThisWeek)
                {
                    if (!cactpotService.IsActive && !cactpotService.IsComplete && !cactpotService.IsFailed)
                    {
                        cactpotService.StartJumboCactpot();
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

            case EngineState.RunningChocoboRacing:
                if (activeConfig!.EnableChocoboRacing && dailyResetDetected && !activeConfig.ChocoboRacingCompletedToday)
                {
                    if (!chocoboRaceService.IsActive && !chocoboRaceService.IsComplete && !chocoboRaceService.IsFailed)
                    {
                        chocoboRaceService.StartRaces(activeConfig.ChocoboRacesPerDay);
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
                SetState(EngineState.Complete);
                log.Information("[Engine] === Vermaxion post-processing complete ===");
                break;
        }
    }

    private void AdvanceToNextTask(EngineState currentTask)
    {
        var next = currentTask switch
        {
            EngineState.RunningFCBuff => EngineState.RunningVerminion,
            EngineState.RunningVerminion => EngineState.RunningMiniCactpot,
            EngineState.RunningMiniCactpot => EngineState.RunningJumboCactpot,
            EngineState.RunningJumboCactpot => EngineState.RunningChocoboRacing,
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
            EngineState.RunningVerminion => "Verminion Queue",
            EngineState.RunningMiniCactpot => "Mini Cactpot",
            EngineState.RunningJumboCactpot => "Jumbo Cactpot",
            EngineState.RunningChocoboRacing => "Chocobo Racing",
            EngineState.EnablingHenchman => "Enabling Henchman",
            EngineState.SignalingARDone => "Signaling AR",
            EngineState.Complete => "Complete",
            EngineState.Error => "Error",
            _ => "Unknown",
        };
    }
}
