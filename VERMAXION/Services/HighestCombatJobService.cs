using System;
using Dalamud.Plugin.Services;
using VERMAXION.Models;

namespace VERMAXION.Services;

public sealed class HighestCombatJobService : IDisposable
{
    private readonly HighestCombatJobStateMachine machine;
    private readonly GearsetBootstrapStateMachine bootstrap;
    private readonly IEquipmentAutomationRuntime runtime;
    private readonly IPluginLog log;
    private bool bootstrapFirst;

    internal HighestCombatJobService(IEquipmentAutomationRuntime runtime, IPluginLog log)
    {
        this.runtime = runtime;
        machine = new HighestCombatJobStateMachine(runtime);
        bootstrap = new GearsetBootstrapStateMachine(runtime);
        this.log = log;
    }

    public bool IsActive => machine.IsActive || bootstrap.IsActive;
    public bool IsComplete => machine.IsComplete;
    public bool IsFailed => machine.IsFailed || bootstrap.IsFailed;
    public string StatusText => bootstrapFirst
        ? $"{bootstrap.Status} ({bootstrap.CreatedTargetCount} created, {bootstrap.SkippedTargetCount} skipped)"
        : machine.Status;

    public void Start()
    {
        Reset();
        if (EquipmentAutomationPolicy.SelectHighestCombatJob(runtime.GetValidGearsets(), runtime.CurrentJobId) == null)
        {
            bootstrapFirst = true;
            if (!bootstrap.Start(out var bootstrapReason))
                log.Warning($"[HighestCombatJob][Bootstrap] {bootstrapReason}");
            else
                log.Information($"[HighestCombatJob][Bootstrap] {bootstrapReason}");
            return;
        }

        if (!machine.Start(out var reason))
            log.Warning($"[HighestCombatJob] {reason}");
        else
            log.Information($"[HighestCombatJob] {reason}");
    }

    public void RunTask() => Start();
    public void Update()
    {
        if (bootstrap.IsActive)
            bootstrap.Tick();

        if (bootstrapFirst && bootstrap.IsComplete)
        {
            bootstrap.Reset();
            bootstrapFirst = false;
            if (!machine.Start(out var reason))
                log.Warning($"[HighestCombatJob] Bootstrap completed but no valid combat gearset is available: {reason}");
            else
                log.Information($"[HighestCombatJob] {reason}");
        }
        else if (!bootstrapFirst)
        {
            machine.Tick();
        }
    }
    public void Cancel(string reason = "Highest Combat Job cancelled")
    {
        bootstrap.Cancel(reason);
        machine.Cancel(reason);
    }
    public void Reset()
    {
        bootstrap.Reset();
        machine.Reset();
        bootstrapFirst = false;
    }
    public void Dispose() => Cancel("Highest Combat Job disposed");
}
