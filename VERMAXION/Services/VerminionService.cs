using System;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;

namespace VERMAXION.Services;

public class VerminionService
{
    private readonly ICommandManager commandManager;
    private readonly ICondition condition;
    private readonly IPluginLog log;

    private int currentAttempt = 0;
    private const int MaxAttempts = 5;
    private DateTime stateEnteredAt = DateTime.MinValue;
    private VerminionState state = VerminionState.Idle;

    public enum VerminionState
    {
        Idle,
        QueueingForDuty,
        WaitingForDutyPop,
        InDuty,
        WaitingForDutyEnd,
        Complete,
        Failed,
    }

    public VerminionState State => state;
    public int CurrentAttempt => currentAttempt;
    public bool IsActive => state != VerminionState.Idle && state != VerminionState.Complete && state != VerminionState.Failed;
    public bool IsComplete => state == VerminionState.Complete;
    public bool IsFailed => state == VerminionState.Failed;

    public VerminionService(ICommandManager commandManager, ICondition condition, IPluginLog log)
    {
        this.commandManager = commandManager;
        this.condition = condition;
        this.log = log;
    }

    public void Start()
    {
        currentAttempt = 0;
        SetState(VerminionState.QueueingForDuty);
        log.Information($"[Verminion] Starting Verminion queue cycle (0/{MaxAttempts})");
    }

    public void Reset()
    {
        currentAttempt = 0;
        SetState(VerminionState.Idle);
    }

    public void Update()
    {
        if (state == VerminionState.Idle || state == VerminionState.Complete || state == VerminionState.Failed)
            return;

        var elapsed = (DateTime.UtcNow - stateEnteredAt).TotalSeconds;

        switch (state)
        {
            case VerminionState.QueueingForDuty:
                // TODO: Open ContentsFinder, select Lord of Verminion, queue
                // For now, this is a stub that needs the actual duty queue implementation
                // The duty queue requires ContentsFinder addon interaction which needs in-game research
                log.Information($"[Verminion] TODO: Queue for Lord of Verminion (attempt {currentAttempt + 1}/{MaxAttempts})");
                log.Warning("[Verminion] Duty queue not yet implemented - marking attempt as done");
                currentAttempt++;
                if (currentAttempt >= MaxAttempts)
                    SetState(VerminionState.Complete);
                else
                    SetState(VerminionState.QueueingForDuty);
                break;

            case VerminionState.WaitingForDutyPop:
                // Wait for BoundByDuty condition
                if (condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.BoundByDuty])
                {
                    log.Information("[Verminion] Duty started - waiting for fail");
                    SetState(VerminionState.InDuty);
                }
                else if (elapsed > 600) // 10 min timeout
                {
                    log.Warning("[Verminion] Duty queue timeout after 10 minutes");
                    SetState(VerminionState.Failed);
                }
                break;

            case VerminionState.InDuty:
                // Do nothing, wait for duty to end (intentional fail)
                if (!condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.BoundByDuty])
                {
                    log.Information($"[Verminion] Duty ended (attempt {currentAttempt + 1}/{MaxAttempts})");
                    SetState(VerminionState.WaitingForDutyEnd);
                }
                else if (elapsed > 900) // 15 min timeout
                {
                    log.Warning("[Verminion] Duty timeout after 15 minutes");
                    SetState(VerminionState.Failed);
                }
                break;

            case VerminionState.WaitingForDutyEnd:
                // Brief pause between attempts
                if (elapsed > 3)
                {
                    currentAttempt++;
                    if (currentAttempt >= MaxAttempts)
                    {
                        log.Information($"[Verminion] All {MaxAttempts} attempts complete!");
                        SetState(VerminionState.Complete);
                    }
                    else
                    {
                        log.Information($"[Verminion] Starting attempt {currentAttempt + 1}/{MaxAttempts}");
                        SetState(VerminionState.QueueingForDuty);
                    }
                }
                break;
        }
    }

    private void SetState(VerminionState newState)
    {
        log.Debug($"[Verminion] State: {state} -> {newState}");
        state = newState;
        stateEnteredAt = DateTime.UtcNow;
    }
}
