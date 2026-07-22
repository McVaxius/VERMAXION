using System;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;

namespace VERMAXION.Services;

// [OK] - Complete implementation using CommandHelper for proper chat command execution
public class MinionRouletteService : IDisposable
{
    private readonly IPluginLog log;
    private readonly ConfigManager configManager;
    private bool commandSent;

    private enum MinionState { Idle, Executing, Complete, Failed }
    private MinionState state = MinionState.Idle;
    private DateTime stateEnteredAt;

    public bool IsComplete => state == MinionState.Complete;
    public bool IsFailed => state == MinionState.Failed;
    public bool IsIdle => state == MinionState.Idle;
    public bool IsActive => !IsIdle && !IsComplete && !IsFailed;
    public string StatusText => state.ToString();

    public MinionRouletteService(IPluginLog log, ConfigManager configManager)
    {
        this.log = log;
        this.configManager = configManager;
    }

    private void SetState(MinionState newState)
    {
        log.Information($"[MinionRoulette] {state} -> {newState}");
        state = newState;
        stateEnteredAt = DateTime.UtcNow;
    }

    public void Start()
    {
        if (IsActive)
            return;

        commandSent = false;
        SetState(MinionState.Executing);
        log.Information("[MinionRoulette] Executing minion roulette command");
    }

    public void RunTask()
    {
        log.Information("[VERMAXION] Manual Minion Roulette triggered");
        Start();
    }

    public void Reset()
    {
        commandSent = false;
        SetState(MinionState.Idle);
    }

    public void Update()
    {
        if (state == MinionState.Idle || state == MinionState.Complete || state == MinionState.Failed)
            return;

        var elapsed = (DateTime.UtcNow - stateEnteredAt).TotalSeconds;

        switch (state)
        {
            case MinionState.Executing:
                if (commandSent)
                {
                    SetState(MinionState.Complete);
                    break;
                }

                commandSent = true;
                CommandHelper.SendCommand("/generalaction \"Minion Roulette\"");
                RecordInformationalAttempt();
                log.Information("[MinionRoulette] Minion roulette command executed");
                SetState(MinionState.Complete);
                break;
        }
    }

    private void RecordInformationalAttempt()
    {
        var config = configManager.GetActiveConfig();
        if (config == null)
            return;

        var now = DateTime.UtcNow;
        if (config.LastMinionRouletteReset.Date != now.Date)
            config.MinionRouletteAttemptsToday = 0;

        config.MinionRouletteAttemptsToday++;
        config.LastMinionRouletteReset = now;
        configManager.SaveCurrentAccount();
    }

    public void Dispose() { }
}
