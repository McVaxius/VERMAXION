using System;
using Dalamud.Plugin.Services;
using VERMAXION.Models;

namespace VERMAXION.Services;

public sealed class GearUpdaterService : IDisposable
{
    private readonly GearUpdaterStateMachine machine;
    private readonly IPluginLog log;

    internal GearUpdaterService(IEquipmentAutomationRuntime runtime, IPluginLog log)
    {
        machine = new GearUpdaterStateMachine(runtime);
        this.log = log;
    }

    public bool IsComplete => machine.TerminalState == EquipmentTaskTerminalState.Complete;
    public bool IsFailed => machine.TerminalState is EquipmentTaskTerminalState.Failed or EquipmentTaskTerminalState.Cancelled;
    public bool IsIdle => machine.CurrentState == GearUpdaterStateMachine.State.Idle;
    public bool IsActive => machine.IsActive;
    public string StatusText => $"{machine.Status} ({machine.CompletedTargetCount}/{machine.TargetCount})";

    public void Start()
    {
        if (!machine.Start(out var reason))
            log.Warning($"[GearUpdater] {reason}");
        else
            log.Information($"[GearUpdater] {reason}");
    }

    public void RunTask() => Start();

    public void Update() => machine.Tick();

    public void Cancel(string reason = "Gear Updater cancelled") => machine.Cancel(reason);

    public void Reset() => machine.Reset();

    public void Dispose() => Cancel("Gear Updater disposed");
}
