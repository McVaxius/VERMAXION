using System;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using Dalamud.Game.ClientState.JobGauge.Types;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects;

namespace VERMAXION.Services;

// [WIP] - Implementation following pseudocode with job change detection, needs SimpleTweaks testing
public class GearUpdaterService : IDisposable
{
    private readonly ICommandManager commandManager;
    private readonly IPluginLog log;
    private readonly IClientState clientState;
    private readonly IPlayerState playerState;

    private enum UpdaterState { Idle, SwitchingJob, WaitingForSwitch, AutoEquipping, WaitingForEquip, OpeningCharacter, WaitingForCharacter, UpdatingGearset, WaitingForUpdate, Complete, Failed }
    private UpdaterState state = UpdaterState.Idle;
    private DateTime stateEnteredAt;
    private int currentGearsetIndex;
    private int maxGearsets = 40;
    private uint? lastJobId;

    public bool IsComplete => state == UpdaterState.Complete;
    public bool IsFailed => state == UpdaterState.Failed;
    public bool IsIdle => state == UpdaterState.Idle;
    public string StatusText => state == UpdaterState.Idle ? "Idle" : $"{state} ({currentGearsetIndex}/{maxGearsets})";

    public GearUpdaterService(ICommandManager commandManager, IPluginLog log, IClientState clientState, IPlayerState playerState)
    {
        this.commandManager = commandManager;
        this.log = log;
        this.clientState = clientState;
        this.playerState = playerState;
    }

    private void SetState(UpdaterState newState)
    {
        log.Information($"[GearUpdater] {state} -> {newState}");
        state = newState;
        stateEnteredAt = DateTime.UtcNow;
    }

    private uint GetCurrentJobId()
    {
        try
        {
            var classJob = playerState?.ClassJob;
            return classJob?.RowId ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    public void Start()
    {
        currentGearsetIndex = 1;
        lastJobId = GetCurrentJobId();
        SetState(UpdaterState.SwitchingJob);
        log.Information("[GearUpdater] Starting gear updater - cycling through gearsets 1-40");
    }

    public void RunTask()
    {
        log.Information("[VERMAXION] Manual Gear Updater triggered");
        Start();
    }

    public void Reset()
    {
        currentGearsetIndex = 0;
        lastJobId = null;
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
                
                // Check if job changed (stop condition)
                if (currentGearsetIndex > 1 && lastJobId.HasValue)
                {
                    var currentJobId = GetCurrentJobId();
                    if (currentJobId != lastJobId.Value)
                    {
                        log.Information($"[GearUpdater] Job changed from {lastJobId} to {currentJobId}, stopping loop");
                        SetState(UpdaterState.Complete);
                        return;
                    }
                }
                
                log.Information($"[GearUpdater] Switching to gearset {currentGearsetIndex}");
                CommandHelper.SendCommand($"/gs change {currentGearsetIndex}");
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
                CommandHelper.SendCommand("/equiprecommended"); // SimpleTweaks required
                SetState(UpdaterState.WaitingForEquip);
                break;

            case UpdaterState.WaitingForEquip:
                if (elapsed > 1.0)
                {
                    SetState(UpdaterState.OpeningCharacter);
                }
                break;

            case UpdaterState.OpeningCharacter:
                log.Information($"[GearUpdater] Opening character window for gearset {currentGearsetIndex}");
                CommandHelper.SendCommand("/character");
                SetState(UpdaterState.WaitingForCharacter);
                break;

            case UpdaterState.WaitingForCharacter:
                if (elapsed > 1.0)
                {
                    SetState(UpdaterState.UpdatingGearset);
                }
                break;

            case UpdaterState.UpdatingGearset:
                log.Information($"[GearUpdater] Updating gearset {currentGearsetIndex}");
                CommandHelper.SendCommand("/updategearset"); // SimpleTweaks required
                SetState(UpdaterState.WaitingForUpdate);
                break;

            case UpdaterState.WaitingForUpdate:
                if (elapsed > 1.0)
                {
                    currentGearsetIndex++;
                    SetState(UpdaterState.SwitchingJob);
                }
                break;
        }
    }

    public void Dispose() { }
}
