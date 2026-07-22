using System;
using Dalamud.Plugin.Services;
using VERMAXION.Models;

namespace VERMAXION.Services;

public sealed class CurrentJobEquipmentService : IDisposable
{
    private readonly CurrentJobEquipmentStateMachine machine;
    private readonly IPluginLog log;

    internal CurrentJobEquipmentService(IEquipmentAutomationRuntime runtime, IPluginLog log)
    {
        machine = new CurrentJobEquipmentStateMachine(runtime);
        this.log = log;
    }

    public bool IsActive => machine.IsActive;
    public CurrentJobEquipmentStateMachine.State State => machine.CurrentState;
    public bool IsComplete => machine.IsComplete;
    public bool IsFailed => machine.IsFailed;
    public string StatusText => machine.Status;

    public void Start()
    {
        if (!machine.Start(out var reason))
            log.Warning($"[CurrentJobEquipment] {reason}");
        else
            log.Information($"[CurrentJobEquipment] {reason}");
    }

    public void RunTask() => Start();
    public void Update() => machine.Tick();
    public void Cancel(string reason = "Current Job Equipment cancelled") => machine.Cancel(reason);
    public void Reset() => machine.Reset();
    public void Dispose() => Cancel("Current Job Equipment disposed");
}
