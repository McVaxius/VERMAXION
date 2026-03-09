using System;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;

namespace VERMAXION.Services;

public class ChocoboRaceService
{
    private readonly ICommandManager commandManager;
    private readonly IPluginLog log;

    private ChocoboState state = ChocoboState.Idle;
    private DateTime stateEnteredAt = DateTime.MinValue;
    private int racesRequested = 0;

    public enum ChocoboState
    {
        Idle,
        StartingRaces,
        WaitingForChocoholic,
        Complete,
        Failed,
    }

    public ChocoboState State => state;
    public bool IsActive => state != ChocoboState.Idle && state != ChocoboState.Complete && state != ChocoboState.Failed;
    public bool IsComplete => state == ChocoboState.Complete;
    public bool IsFailed => state == ChocoboState.Failed;

    public ChocoboRaceService(ICommandManager commandManager, IPluginLog log)
    {
        this.commandManager = commandManager;
        this.log = log;
    }

    public void StartRaces(int count)
    {
        racesRequested = count;
        SetState(ChocoboState.StartingRaces);
        log.Information($"[ChocoboRace] Starting {count} races via Chocoholic");
    }

    public void Reset()
    {
        SetState(ChocoboState.Idle);
    }

    public void RunTask()
    {
        log.Information($"[VERMAXION] Manual Chocobo Racing triggered");
        // TODO: Implement Chocobo Racing logic via Chocoholic
        log.Information("[VERMAXION] Chocobo Racing: Stub - not implemented yet");
    }

    public void Dispose()
    {
        // Cleanup if needed
    }

    public void Update()
    {
        if (state == ChocoboState.Idle || state == ChocoboState.Complete || state == ChocoboState.Failed)
            return;

        var elapsed = (DateTime.UtcNow - stateEnteredAt).TotalSeconds;

        switch (state)
        {
            case ChocoboState.StartingRaces:
                // Queue for chocobo races and let Chocoholic handle the actual racing
                // Chocoholic from Punish handles the race automation
                // We just need to queue in and tell it how many to do
                log.Information($"[ChocoboRace] Invoking Chocoholic for {racesRequested} races");
                try
                {
                    // TODO: Verify exact Chocoholic command syntax
                    // This may need to be /chocoholic start {count} or similar
                    commandManager.ProcessCommand("/chocoholic");
                }
                catch (Exception ex)
                {
                    log.Warning($"[ChocoboRace] Chocoholic command failed: {ex.Message}");
                    log.Warning("[ChocoboRace] Chocoholic may not be installed.");
                }
                SetState(ChocoboState.WaitingForChocoholic);
                break;

            case ChocoboState.WaitingForChocoholic:
                // TODO: Detect when Chocoholic has finished all races
                // For now, wait a reasonable timeout
                // Each race takes ~3-5 minutes, so timeout = racesRequested * 5 min + buffer
                var timeout = racesRequested * 300 + 60;
                if (elapsed > 10) // Using short timeout for now until proper detection
                {
                    log.Information("[ChocoboRace] Chocobo racing assumed complete (timeout)");
                    log.Warning("[ChocoboRace] TODO: Implement proper Chocoholic completion detection");
                    SetState(ChocoboState.Complete);
                }
                break;
        }
    }

    private void SetState(ChocoboState newState)
    {
        log.Debug($"[ChocoboRace] State: {state} -> {newState}");
        state = newState;
        stateEnteredAt = DateTime.UtcNow;
    }
}
