using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using VERMAXION.Models;

namespace VERMAXION.Services;

public sealed class FisherGearsetRuntime : IFisherGearsetRuntime
{
    public int GetCurrentClassJobId()
        => (int)Plugin.PlayerState.ClassJob.RowId;

    public unsafe FisherGearsetLookupResult FindFirstSavedFisherGearset()
    {
        try
        {
            var module = RaptureGearsetModule.Instance();
            if (module == null)
                return FisherGearsetLookupResult.ModuleUnavailable(
                    "RaptureGearsetModule is unavailable.");

            var snapshots = new List<SavedGearsetSnapshot>(100);
            for (var slot = 0; slot < 100; slot++)
            {
                ref var entry = ref module->Entries[slot];
                snapshots.Add(new SavedGearsetSnapshot(
                    slot,
                    entry.Id,
                    entry.ClassJob,
                    (entry.Flags & RaptureGearsetModule.GearsetFlag.Exists) != 0));
            }

            var selection = FisherGearsetSelectionPolicy.SelectLowestSavedFisher(snapshots);
            return selection.HasValue
                ? FisherGearsetLookupResult.Found(selection.Value)
                : FisherGearsetLookupResult.Missing();
        }
        catch (Exception ex)
        {
            return FisherGearsetLookupResult.ModuleUnavailable(
                $"Could not inspect RaptureGearsetModule: {ex.Message}");
        }
    }

    public unsafe FisherGearsetEquipRequestResult EquipGearset(int gearsetId)
    {
        try
        {
            var module = RaptureGearsetModule.Instance();
            return module == null
                ? FisherGearsetEquipRequestResult.ModuleUnavailable(
                    "RaptureGearsetModule is unavailable for the equip request.")
                : FisherGearsetEquipRequestResult.Completed(module->EquipGearset(gearsetId));
        }
        catch (Exception ex)
        {
            return FisherGearsetEquipRequestResult.ModuleUnavailable(
                $"Could not equip Fisher gearset ID {gearsetId}: {ex.Message}");
        }
    }
}

public sealed class FisherGearsetTestService
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(45);

    private readonly IFisherGearsetRuntime runtime;
    private readonly IPluginLog log;
    private readonly Action<string> print;
    private FisherGearsetEquipOperation? operation;

    public FisherGearsetTestService(
        IFisherGearsetRuntime runtime,
        IPluginLog log,
        Action<string> print)
    {
        this.runtime = runtime;
        this.log = log;
        this.print = print;
    }

    public bool IsActive => operation is { IsComplete: false };
    public string StatusText { get; private set; } = "Idle";

    public bool Start()
    {
        if (IsActive)
            return false;

        var now = DateTimeOffset.UtcNow;
        operation = new FisherGearsetEquipOperation(runtime, now, now + TestTimeout);
        Publish($"Starting class-job ID: {operation.StartingClassJobId}.");
        StatusText = "Equipping and verifying Fisher";
        return true;
    }

    public void Update()
    {
        if (!IsActive || operation == null)
            return;

        foreach (var entry in operation.Tick(DateTimeOffset.UtcNow))
            Publish(entry.Message);

        if (!operation.IsComplete)
            return;

        StatusText = operation.Succeeded
            ? $"Verified Fisher ({operation.FinalClassJobId})"
            : operation.State.ToString();
    }

    public void Cancel()
    {
        operation = null;
        StatusText = "Idle";
    }

    private void Publish(string message)
    {
        var line = $"[Fishing][GearsetTest] {message}";
        log.Information(line);
        print($"[Vermaxion] {line}");
    }
}
