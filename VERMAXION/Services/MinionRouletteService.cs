using System;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;

namespace VERMAXION.Services;

public class MinionRouletteService : IDisposable
{
    private readonly ICommandManager commandManager;
    private readonly IPluginLog log;

    private enum MinionState { Idle, Executing, WaitingForResult, Complete, Failed }
    private MinionState state = MinionState.Idle;
    private DateTime stateEnteredAt;
    private bool successDetected = false;

    public bool IsComplete => state == MinionState.Complete;
    public bool IsFailed => state == MinionState.Failed;
    public bool IsIdle => state == MinionState.Idle;
    public string StatusText => state.ToString();

    public MinionRouletteService(ICommandManager commandManager, IPluginLog log)
    {
        this.commandManager = commandManager;
        this.log = log;
    }

    private void SetState(MinionState newState)
    {
        log.Information($"[MinionRoulette] {state} -> {newState}");
        state = newState;
        stateEnteredAt = DateTime.UtcNow;
    }

    public void Start()
    {
        SetState(MinionState.Executing);
        successDetected = false;
        log.Information("[MinionRoulette] Executing minion roulette command");
    }

    public void RunTask()
    {
        log.Information("[VERMAXION] Manual Minion Roulette triggered");
        Start();
    }

    public void Reset()
    {
        SetState(MinionState.Idle);
    }

    public void OnXLLogMessage(string message)
    {
        if (state != MinionState.WaitingForResult) return;
        
        // Check for success messages in XL logs
        if (message.Contains("summon the minion") || message.Contains("Minion Roulette"))
        {
            successDetected = true;
            log.Information($"[MinionRoulette] Success detected in XL log: {message}");
        }
    }

    public void Update()
    {
        if (state == MinionState.Idle || state == MinionState.Complete || state == MinionState.Failed)
            return;

        var elapsed = (DateTime.UtcNow - stateEnteredAt).TotalSeconds;

        switch (state)
        {
            case MinionState.Executing:
                commandManager.ProcessCommand("/generalaction \"Minion Roulette\"");
                log.Information("[MinionRoulette] Minion roulette command executed, waiting for result");
                SetState(MinionState.WaitingForResult);
                break;

            case MinionState.WaitingForResult:
                if (elapsed > 3.0) // Wait 3 seconds for XL log message
                {
                    if (successDetected)
                    {
                        log.Information("[MinionRoulette] Minion roulette complete (success detected)");
                        SetState(MinionState.Complete);
                    }
                    else
                    {
                        log.Warning("[MinionRoulette] Minion roulette failed - no success detected in XL logs");
                        SetState(MinionState.Failed);
                    }
                }
                break;
        }
    }

    public void Dispose() { }
}
