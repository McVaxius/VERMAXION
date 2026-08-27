using System.Collections.Generic;
using System.Linq;

namespace VERMAXION.Models;

public enum TaskDependencyState
{
    Ready = 0,
    Missing = 1,
    NeedsSetup = 2,
}

public readonly record struct TaskDependencyCheck(
    string Name,
    TaskDependencyState State,
    string Detail)
{
    public static TaskDependencyCheck Loaded(string name, bool loaded)
        => loaded
            ? new TaskDependencyCheck(name, TaskDependencyState.Ready, $"{name} is loaded.")
            : new TaskDependencyCheck(name, TaskDependencyState.Missing, $"{name} is not loaded.");

    public static TaskDependencyCheck Configured(
        string name,
        bool loaded,
        bool configured,
        string detail)
        => !loaded
            ? new TaskDependencyCheck(name, TaskDependencyState.Missing, $"{name} is not loaded.")
            : configured
                ? new TaskDependencyCheck(name, TaskDependencyState.Ready, detail)
                : new TaskDependencyCheck(name, TaskDependencyState.NeedsSetup, detail);
}

public sealed record TaskDependencySummary(
    TaskDependencyState State,
    IReadOnlyList<TaskDependencyCheck> Checks)
{
    public int MissingCount => Checks.Count(check => check.State == TaskDependencyState.Missing);
    public int NeedsSetupCount => Checks.Count(check => check.State == TaskDependencyState.NeedsSetup);

    public string Label => State switch
    {
        TaskDependencyState.Missing => $"Missing {MissingCount}",
        TaskDependencyState.NeedsSetup => $"Needs setup {NeedsSetupCount}",
        _ => "Ready",
    };

    public string Tooltip
    {
        get
        {
            var required = Checks.Count == 0
                ? "None"
                : string.Join(", ", Checks.Select(check => check.Name));
            var missing = Checks
                .Where(check => check.State == TaskDependencyState.Missing)
                .Select(check => check.Name)
                .ToList();
            var setup = Checks
                .Where(check => check.State == TaskDependencyState.NeedsSetup)
                .Select(check => $"{check.Name}: {check.Detail}")
                .ToList();
            return $"Required: {required}\n" +
                   $"Missing: {(missing.Count == 0 ? "None" : string.Join(", ", missing))}\n" +
                   $"Needs setup: {(setup.Count == 0 ? "None" : string.Join("; ", setup))}";
        }
    }
}

public static class TaskDependencyPolicy
{
    public static TaskDependencyCheck FishingProviderAlignment(
        OceanFishingProvider provider,
        bool autoHookLoaded,
        bool settingReadable,
        bool? autoOceanFishEnabled,
        string readStatus)
    {
        if (!autoHookLoaded)
            return TaskDependencyCheck.Loaded("AutoHook", false);

        if (!settingReadable || !autoOceanFishEnabled.HasValue)
            return TaskDependencyCheck.Configured("AutoHook", true, false, readStatus);

        var expected = OceanFishingProviderPolicy.ExpectedAutoOceanFish(provider);
        var aligned = autoOceanFishEnabled.Value == expected;
        var providerLabel = provider == OceanFishingProvider.AutoHookAutoOceanFish
            ? "AutoHook AutoOceanFish"
            : "VerMAXION + AutoHook";
        var detail = aligned
            ? $"AutoOceanFish is aligned ({(expected ? "enabled" : "disabled")}) for {providerLabel}."
            : $"AutoOceanFish is {(autoOceanFishEnabled.Value ? "enabled" : "disabled")}, but {providerLabel} requires it {(expected ? "enabled" : "disabled")}.";
        return TaskDependencyCheck.Configured("AutoHook", true, aligned, detail);
    }

    public static TaskDependencyCheck Alternative(
        string name,
        params TaskDependencyCheck[] alternatives)
    {
        if (alternatives.Any(check => check.State == TaskDependencyState.Ready))
        {
            var ready = alternatives.First(check => check.State == TaskDependencyState.Ready);
            return new TaskDependencyCheck(
                name,
                TaskDependencyState.Ready,
                $"Satisfied by {ready.Name}. {ready.Detail}");
        }

        if (alternatives.All(check => check.State == TaskDependencyState.Missing))
        {
            return new TaskDependencyCheck(
                name,
                TaskDependencyState.Missing,
                string.Join(" ", alternatives.Select(check => check.Detail)));
        }

        return new TaskDependencyCheck(
            name,
            TaskDependencyState.NeedsSetup,
            string.Join(" ", alternatives.Select(check => $"{check.Name}: {check.Detail}")));
    }

    public static TaskDependencySummary Aggregate(IEnumerable<TaskDependencyCheck> checks)
    {
        var materialized = checks.ToList();
        var state = materialized.Any(check => check.State == TaskDependencyState.Missing)
            ? TaskDependencyState.Missing
            : materialized.Any(check => check.State == TaskDependencyState.NeedsSetup)
                ? TaskDependencyState.NeedsSetup
                : TaskDependencyState.Ready;
        return new TaskDependencySummary(state, materialized);
    }
}
