using System;
using Dalamud.Plugin.Services;
using VERMAXION.Models;

namespace VERMAXION.Services;

public sealed class HighestCombatJobService : IDisposable
{
    private readonly HighestCombatJobStateMachine machine;
    private readonly IPluginLog log;

    internal HighestCombatJobService(IEquipmentAutomationRuntime runtime, IPluginLog log)
    {
        machine = new HighestCombatJobStateMachine(runtime);
        this.log = log;
    }

    public bool IsActive => machine.IsActive;
    public bool IsComplete => machine.IsComplete;
    public bool IsFailed => machine.IsFailed;
    public string StatusText => machine.Status;

    public void Start()
    {
        if (!machine.Start(out var reason))
            log.Warning($"[HighestCombatJob] {reason}");
        else
            log.Information($"[HighestCombatJob] {reason}");
    }

    public void RunTask() => Start();
    public void Update() => machine.Tick();
    public void Cancel(string reason = "Highest Combat Job cancelled") => machine.Cancel(reason);
    public void Reset() => machine.Reset();
    public void Dispose() => Cancel("Highest Combat Job disposed");
}
