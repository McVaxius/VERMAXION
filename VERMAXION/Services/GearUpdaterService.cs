using System;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;

namespace VERMAXION.Services;

public class GearUpdaterService : IDisposable
{
    private readonly ICommandManager commandManager;
    private readonly IPluginLog log;

    private enum UpdaterState { Idle, SwitchingJob, WaitingForSwitch, AutoEquipping, WaitingForEquip, SavingGearset, WaitingForSave, Complete, Failed }
    private UpdaterState state = UpdaterState.Idle;
    private DateTime stateEnteredAt;
    private int currentGearsetIndex;
    private int maxGearsets = 30;

    public bool IsComplete => state == UpdaterState.Complete;
    public bool IsFailed => state == UpdaterState.Failed;
    public bool IsIdle => state == UpdaterState.Idle;
    public string StatusText => state == UpdaterState.Idle ? "Idle" : $"{state} ({currentGearsetIndex}/{maxGearsets})";

    public GearUpdaterService(ICommandManager commandManager, IPluginLog log)
    {
        this.commandManager = commandManager;
        this.log = log;
    }

    private void SetState(UpdaterState newState)
    {
        log.Information($"[GearUpdater] {state} -> {newState}");
        state = newState;
        stateEnteredAt = DateTime.UtcNow;
    }

    public void Start()
    {
        currentGearsetIndex = 1;
        SetState(UpdaterState.SwitchingJob);
        log.Information("[GearUpdater] Starting gear updater - cycling through all gearsets");
    }

    public void RunTask()
    {
        log.Information("[VERMAXION] Manual Gear Updater triggered");
        Start();
    }

    public void Reset()
    {
        currentGearsetIndex = 0;
        SetState(UpdaterState.Idle);
    }

    public void Update()
    {
        if (state == UpdaterState.Idle || state == UpdaterState.Complete || state == UpdaterState.Failed)
            return;

        var elapsed = (DateTime.UtcNow - stateEnteredAt).TotalSeconds;

        switch (state)
        {
            case UpdaterState.SwitchingJob:
                if (currentGearsetIndex > maxGearsets)
                {
                    log.Information("[GearUpdater] All gearsets processed, complete");
                    SetState(UpdaterState.Complete);
                    return;
                }
                log.Information($"[GearUpdater] Switching to gearset {currentGearsetIndex}");
                commandManager.ProcessCommand($"/gearset change {currentGearsetIndex}");
                SetState(UpdaterState.WaitingForSwitch);
                break;

            case UpdaterState.WaitingForSwitch:
                if (elapsed > 2.0)
                {
                    SetState(UpdaterState.AutoEquipping);
                }
                break;

            case UpdaterState.AutoEquipping:
                log.Information($"[GearUpdater] Auto-equipping recommended gear for gearset {currentGearsetIndex}");
                commandManager.ProcessCommand("/gearset equip");
                SetState(UpdaterState.WaitingForEquip);
                break;

            case UpdaterState.WaitingForEquip:
                if (elapsed > 2.0)
                {
                    SetState(UpdaterState.SavingGearset);
                }
                break;

            case UpdaterState.SavingGearset:
                log.Information($"[GearUpdater] Saving gearset {currentGearsetIndex}");
                commandManager.ProcessCommand($"/gearset save {currentGearsetIndex}");
                SetState(UpdaterState.WaitingForSave);
                break;

            case UpdaterState.WaitingForSave:
                if (elapsed > 2.0)
                {
                    currentGearsetIndex++;
                    SetState(UpdaterState.SwitchingJob);
                }
                break;
        }
    }

    public void Dispose() { }
}
