using System;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace VERMAXION.Services;

public class MinionRouletteService : IDisposable
{
    private readonly ICommandManager commandManager;
    private readonly IPluginLog log;

    private enum MinionState { Idle, Executing, Complete, Failed }
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
        SetState(MinionState.Executing);
        log.Information("[MinionRoulette] Executing minion roulette action");
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
            case MinionState.Executing:
                unsafe
                {
                    try
                    {
                        var am = ActionManager.Instance();
                        if (am == null)
                        {
                            log.Warning("[MinionRoulette] ActionManager is null");
                            SetState(MinionState.Failed);
                            break;
                        }

                        // Use General Action for Minion Roulette (ID 39)
                        var result = am->UseAction(ActionType.GeneralAction, 39);
                        log.Information($"[MinionRoulette] UseAction result={result}");
                        SetState(MinionState.Complete);
                    }
                    catch (Exception ex)
                    {
                        log.Error($"[MinionRoulette] UseAction failed: {ex.Message}");
                        SetState(MinionState.Failed);
                    }
                }
                break;
        }
    }

    public void Dispose() { }
}
