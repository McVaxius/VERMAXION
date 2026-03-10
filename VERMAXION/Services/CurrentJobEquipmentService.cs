using System;
using Dalamud.Logging;
using Dalamud.Plugin.Services;

namespace VERMAXION.Services;

// [WIP] - Implementation for current job equipment updater, needs SimpleTweaks testing
public class CurrentJobEquipmentService : IDisposable
{
    private readonly ICommandManager commandManager;
    private readonly IPluginLog log;
    private readonly IPlayerState playerState;
    
    private DateTime lastAction = DateTime.MinValue;
    private bool isRunning = false;

    public enum EquipmentState
    {
        Idle,
        EquippingRecommended,
        WaitingForEquip,
        OpeningCharacter,
        WaitingForCharacter,
        UpdatingGearset,
        WaitingForUpdate,
        Complete,
        Failed
    }

    private EquipmentState state = EquipmentState.Idle;
    private DateTime stateStartTime = DateTime.UtcNow;

    public CurrentJobEquipmentService(ICommandManager commandManager, IPluginLog log, IPlayerState playerState)
    {
        this.commandManager = commandManager;
        this.log = log;
        this.playerState = playerState;
    }

    public void RunTask()
    {
        if (isRunning)
        {
            log.Information("[CurrentJobEquipment] Already running");
            return;
        }

        log.Information("[CurrentJobEquipment] Starting current job equipment update");
        isRunning = true;
        SetState(EquipmentState.EquippingRecommended);
    }

    public void Update()
    {
        if (!isRunning) return;

        var elapsed = (DateTime.UtcNow - stateStartTime).TotalSeconds;

        switch (state)
        {
            case EquipmentState.EquippingRecommended:
                log.Information("[CurrentJobEquipment] Equipping recommended gear");
                CommandHelper.SendCommand("/equiprecommended");
                SetState(EquipmentState.WaitingForEquip);
                break;

            case EquipmentState.WaitingForEquip:
                if (elapsed > 1.0)
                {
                    log.Information("[CurrentJobEquipment] Opening character window");
                    CommandHelper.SendCommand("/character");
                    SetState(EquipmentState.WaitingForCharacter);
                }
                break;

            case EquipmentState.WaitingForCharacter:
                if (elapsed > 1.0)
                {
                    log.Information("[CurrentJobEquipment] Updating current gearset");
                    CommandHelper.SendCommand("/updategearset");
                    SetState(EquipmentState.WaitingForUpdate);
                }
                break;

            case EquipmentState.WaitingForUpdate:
                if (elapsed > 2.0) // Increased wait time from 1s to 2s
                {
                    log.Information("[CurrentJobEquipment] Equipment update complete");
                    SetState(EquipmentState.Complete);
                }
                break;

            case EquipmentState.Complete:
            case EquipmentState.Failed:
                isRunning = false;
                break;
        }
    }

    private void SetState(EquipmentState newState)
    {
        if (state != newState)
        {
            log.Information($"[CurrentJobEquipment] State: {state} -> {newState}");
            state = newState;
            stateStartTime = DateTime.UtcNow;
        }
    }

    public void Dispose()
    {
        isRunning = false;
    }
}
