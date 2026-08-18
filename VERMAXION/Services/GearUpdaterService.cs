using System;
using Dalamud.Plugin.Services;
using VERMAXION.Models;

namespace VERMAXION.Services;

public sealed class GearUpdaterService : IDisposable
{
    private readonly GearUpdaterStateMachine machine;
    private readonly GearsetBootstrapStateMachine bootstrap;
    private readonly IEquipmentAutomationRuntime runtime;
    private readonly IPluginLog log;
    private RunMode mode;

    private enum RunMode
    {
        None,
        GearUpdater,
        BootstrapThenGearUpdater,
        BootstrapOnly,
    }

    internal GearUpdaterService(IEquipmentAutomationRuntime runtime, IPluginLog log)
    {
        this.runtime = runtime;
        machine = new GearUpdaterStateMachine(runtime);
        bootstrap = new GearsetBootstrapStateMachine(runtime);
        this.log = log;
    }

    public bool IsComplete => mode == RunMode.BootstrapOnly
        ? bootstrap.IsComplete
        : machine.TerminalState == EquipmentTaskTerminalState.Complete;
    public bool IsFailed => bootstrap.IsFailed ||
                            machine.TerminalState is EquipmentTaskTerminalState.Failed or EquipmentTaskTerminalState.Cancelled;
    public bool IsIdle => mode == RunMode.None &&
                          machine.CurrentState == GearUpdaterStateMachine.State.Idle &&
                          bootstrap.CurrentState == GearsetBootstrapStateMachine.State.Idle;
    public bool IsActive => machine.IsActive || bootstrap.IsActive;
    public string StatusText => mode is RunMode.BootstrapOnly or RunMode.BootstrapThenGearUpdater
        ? $"{bootstrap.Status} ({bootstrap.CreatedTargetCount} created, {bootstrap.SkippedTargetCount} skipped)"
        : $"{machine.Status} ({machine.CompletedTargetCount}/{machine.TargetCount})";

    public void Start()
    {
        Reset();
        if (EquipmentAutomationPolicy.BuildGearUpdaterTargets(runtime.GetValidGearsets()).Count == 0)
        {
            mode = RunMode.BootstrapThenGearUpdater;
            if (!bootstrap.Start(out var bootstrapReason))
                log.Warning($"[GearUpdater][Bootstrap] {bootstrapReason}");
            else
                log.Information($"[GearUpdater][Bootstrap] {bootstrapReason}");
            return;
        }

        mode = RunMode.GearUpdater;
        if (!machine.Start(out var reason))
            log.Warning($"[GearUpdater] {reason}");
        else
            log.Information($"[GearUpdater] {reason}");
    }

    public void StartBootstrap()
    {
        Reset();
        mode = RunMode.BootstrapOnly;
        if (!bootstrap.Start(out var reason))
            log.Warning($"[GearUpdater][Bootstrap] {reason}");
        else
            log.Information($"[GearUpdater][Bootstrap] {reason}");
    }

    public void RunTask() => Start();

    public void Update()
    {
        if (bootstrap.IsActive)
            bootstrap.Tick();

        if (mode == RunMode.BootstrapThenGearUpdater && bootstrap.IsComplete)
        {
            bootstrap.Reset();
            mode = RunMode.GearUpdater;
            if (!machine.Start(out var reason))
                log.Warning($"[GearUpdater] Bootstrap completed but Gear Updater could not start: {reason}");
            else
                log.Information($"[GearUpdater] {reason}");
        }
        else if (mode == RunMode.GearUpdater)
        {
            machine.Tick();
        }
    }

    public void Cancel(string reason = "Gear Updater cancelled")
    {
        bootstrap.Cancel(reason);
        machine.Cancel(reason);
    }

    public void Reset()
    {
        bootstrap.Reset();
        machine.Reset();
        mode = RunMode.None;
    }

    public void Dispose() => Cancel("Gear Updater disposed");
}
