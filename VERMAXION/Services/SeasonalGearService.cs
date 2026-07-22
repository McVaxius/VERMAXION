using System;
using Dalamud.Plugin.Services;
using VERMAXION.Models;

namespace VERMAXION.Services;

public sealed class SeasonalGearService : IDisposable
{
    // IDs are intentionally kept as the curated source list. The policy layer
    // deduplicates them and Lumina, rather than this list, determines each slot.
    internal static readonly uint[] CuratedItemIds =
    [
        43471, 43472, 43473, 43474, 43475,
        38261, 38262, 38263, 38264, 38260,
        36837, 36838, 36839, 36840, 36841,
        47924, 47925, 47926, 47927,
        41565, 41566,
        38233, 38234, 38235, 38236, 38237,
        39245, 39246,
        38257, 38258, 38259, 38260,
        43154, 43155, 43156, 43157,
        43149, 43150, 43151, 43152,
        43189, 43190, 43191, 43192, 43193,
        36832, 36833, 36834, 36835, 36836,
        35857,
        43198, 43199, 43200, 43201,
        38265, 38266,
        43149, 43150, 43151, 43152, 43153,
        28556, 28557, 33653, 33654, 32803,
    ];

    private readonly SeasonalGearStateMachine machine;
    private readonly IPluginLog log;

    internal SeasonalGearService(IEquipmentAutomationRuntime runtime, IPluginLog log)
    {
        machine = new SeasonalGearStateMachine(runtime, CuratedItemIds);
        this.log = log;
    }

    public bool IsComplete => machine.IsComplete;
    public bool IsFailed => machine.IsFailed;
    public bool IsIdle => machine.CurrentState == SeasonalGearStateMachine.State.Idle;
    public bool IsActive => machine.IsActive;
    public string StatusText => machine.Status;

    public void Start()
    {
        if (!machine.Start(out var reason))
            log.Warning($"[SeasonalGear] {reason}");
        else
            log.Information($"[SeasonalGear] {reason}");
    }

    public void RunTask() => Start();
    public void Update() => machine.Tick();
    public void Cancel(string reason = "Seasonal Gear cancelled") => machine.Cancel(reason);
    public void Reset() => machine.Reset();
    public void Dispose() => Cancel("Seasonal Gear disposed");
}
