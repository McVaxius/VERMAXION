using System;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;

namespace VERMAXION.Services;

public class MinionRouletteService : IDisposable
{
    private readonly ICommandManager commandManager;
    private readonly IPluginLog log;

    private enum MinionState { Idle, Summoning, WaitingForCast, Complete, Failed }
    private MinionState state = MinionState.Idle;
    private DateTime stateEnteredAt;

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
        SetState(MinionState.Summoning);
        log.Information("[MinionRoulette] Firing minion roulette");
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

    public void Update()
    {
        if (state == MinionState.Idle || state == MinionState.Complete || state == MinionState.Failed)
            return;

        var elapsed = (DateTime.UtcNow - stateEnteredAt).TotalSeconds;

        switch (state)
        {
            case MinionState.Summoning:
                commandManager.ProcessCommand("/minion");
                SetState(MinionState.WaitingForCast);
                break;

            case MinionState.WaitingForCast:
                if (elapsed > 5.0)
                {
                    log.Information("[MinionRoulette] Minion roulette complete (waited 5s for cast)");
                    SetState(MinionState.Complete);
                }
                break;
        }
    }

    public void Dispose() { }
}
