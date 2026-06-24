#nullable enable

using System;

namespace VERMAXION.Models;

public readonly record struct HenchmanTakeoverReadiness(
    bool Loaded,
    bool Busy,
    string TaskName,
    string TaskDescription,
    bool AllowTakeover,
    string Reason)
{
    public string DisplayDescription => !string.IsNullOrWhiteSpace(TaskDescription)
        ? TaskDescription
        : Reason;
}

internal static class HenchmanTakeoverPolicy
{
    public const string SafeTaskName = "On A Boat";
    public const string SafeTaskDescription = "Waiting for Ocean Fishing time window";

    public static HenchmanTakeoverReadiness Evaluate(
        bool loaded,
        bool busyReadSucceeded,
        bool busy,
        bool stateReadSucceeded,
        string? taskName,
        string? taskDescription,
        string? failureReason = null)
    {
        var normalizedTaskName = taskName ?? string.Empty;
        var normalizedTaskDescription = taskDescription ?? string.Empty;

        if (!loaded)
        {
            return new HenchmanTakeoverReadiness(
                false,
                false,
                normalizedTaskName,
                normalizedTaskDescription,
                true,
                "Henchman is not loaded or enabled.");
        }

        if (!busyReadSucceeded)
        {
            return new HenchmanTakeoverReadiness(
                true,
                false,
                normalizedTaskName,
                normalizedTaskDescription,
                false,
                failureReason ?? "Henchman busy state could not be read.");
        }

        if (!busy)
        {
            return new HenchmanTakeoverReadiness(
                true,
                false,
                normalizedTaskName,
                normalizedTaskDescription,
                true,
                "Henchman is idle.");
        }

        if (!stateReadSucceeded)
        {
            return new HenchmanTakeoverReadiness(
                true,
                true,
                normalizedTaskName,
                normalizedTaskDescription,
                false,
                failureReason ?? "Henchman task state could not be read.");
        }

        if (string.Equals(normalizedTaskName, SafeTaskName, StringComparison.Ordinal) &&
            string.Equals(normalizedTaskDescription, SafeTaskDescription, StringComparison.Ordinal))
        {
            return new HenchmanTakeoverReadiness(
                true,
                true,
                normalizedTaskName,
                normalizedTaskDescription,
                true,
                "Henchman On A Boat is in its safe waiting state.");
        }

        var reason = string.IsNullOrWhiteSpace(normalizedTaskDescription)
            ? $"Henchman is busy with {FormatTaskName(normalizedTaskName)} but reported no task description."
            : $"Henchman is busy with {FormatTaskName(normalizedTaskName)}: {normalizedTaskDescription}";

        return new HenchmanTakeoverReadiness(
            true,
            true,
            normalizedTaskName,
            normalizedTaskDescription,
            false,
            reason);
    }

    private static string FormatTaskName(string taskName)
        => string.IsNullOrWhiteSpace(taskName) ? "an unknown task" : taskName;
}
