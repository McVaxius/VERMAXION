using System;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;

namespace VERMAXION.Services;

public class CactpotService
{
    private readonly ICommandManager commandManager;
    private readonly IPluginLog log;

    private CactpotState state = CactpotState.Idle;
    private DateTime stateEnteredAt = DateTime.MinValue;
    private bool isMini = false;

    public enum CactpotState
    {
        Idle,
        StartingMiniCactpot,
        WaitingForSaucy,
        MiniCactpotComplete,
        StartingJumboCactpot,
        JumboCactpotComplete,
        Complete,
        Failed,
    }

    public CactpotState State => state;
    public bool IsActive => state != CactpotState.Idle && state != CactpotState.Complete && state != CactpotState.Failed;
    public bool IsComplete => state == CactpotState.Complete;
    public bool IsFailed => state == CactpotState.Failed;

    public CactpotService(ICommandManager commandManager, IPluginLog log)
    {
        this.commandManager = commandManager;
        this.log = log;
    }

    public void StartMiniCactpot()
    {
        isMini = true;
        SetState(CactpotState.StartingMiniCactpot);
        log.Information("[Cactpot] Starting Mini Cactpot via Saucy");
    }

    public void StartJumboCactpot()
    {
        isMini = false;
        SetState(CactpotState.StartingJumboCactpot);
        log.Information("[Cactpot] Starting Jumbo Cactpot");
    }

    public void RunMiniCactpot()
    {
        log.Information($"[VERMAXION] Manual Mini Cactpot triggered");
        // TODO: Implement Mini Cactpot logic via Saucy
        log.Information("[VERMAXION] Mini Cactpot: Stub - not implemented yet");
    }

    public void RunJumboCactpot()
    {
        log.Information($"[VERMAXION] Manual Jumbo Cactpot triggered");
        // TODO: Implement Jumbo Cactpot logic
        log.Information("[VERMAXION] Jumbo Cactpot: Stub - not implemented yet");
    }

    public void Reset()
    {
        SetState(CactpotState.Idle);
    }

    public void Update()
    {
        if (state == CactpotState.Idle || state == CactpotState.Complete || state == CactpotState.Failed)
            return;

        var elapsed = (DateTime.UtcNow - stateEnteredAt).TotalSeconds;

        switch (state)
        {
            case CactpotState.StartingMiniCactpot:
                // Saucy handles Mini Cactpot - just invoke it
                // /saucy is the command to open Saucy's UI
                // The actual automation command needs verification but Saucy handles the game interaction
                log.Information("[Cactpot] Invoking Saucy for Mini Cactpot");
                try
                {
                    commandManager.ProcessCommand("/saucy");
                }
                catch (Exception ex)
                {
                    log.Warning($"[Cactpot] Saucy command failed: {ex.Message}");
                    log.Warning("[Cactpot] Saucy may not be installed. Mini Cactpot requires Saucy plugin.");
                }
                SetState(CactpotState.WaitingForSaucy);
                break;

            case CactpotState.WaitingForSaucy:
                // TODO: Detect when Saucy has finished Mini Cactpot
                // For now, wait a reasonable timeout and assume done
                // This needs Saucy IPC research or chat message detection
                if (elapsed > 5)
                {
                    log.Information("[Cactpot] Mini Cactpot assumed complete (Saucy timeout)");
                    log.Warning("[Cactpot] TODO: Implement proper Saucy completion detection");
                    SetState(CactpotState.MiniCactpotComplete);
                }
                break;

            case CactpotState.MiniCactpotComplete:
                SetState(CactpotState.Complete);
                break;

            case CactpotState.StartingJumboCactpot:
                // TODO: Jumbo Cactpot requires:
                // 1. Navigate to Gold Saucer (teleport)
                // 2. Find Jumbo Cactpot NPC
                // 3. Interact and buy ticket
                // This needs in-game research for addon names and interaction flow
                log.Information("[Cactpot] TODO: Jumbo Cactpot automation not yet implemented");
                log.Warning("[Cactpot] Jumbo Cactpot needs addon research - marking as done for now");
                SetState(CactpotState.JumboCactpotComplete);
                break;

            case CactpotState.JumboCactpotComplete:
                SetState(CactpotState.Complete);
                break;
        }
    }

    private void SetState(CactpotState newState)
    {
        log.Debug($"[Cactpot] State: {state} -> {newState}");
        state = newState;
        stateEnteredAt = DateTime.UtcNow;
    }
}
