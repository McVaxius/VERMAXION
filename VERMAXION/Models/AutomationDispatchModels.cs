namespace VERMAXION.Models;

using System;
using System.Collections.Generic;
using System.Linq;

public enum TaskEligibilityStatus
{
    Runnable,
    Disabled,
    NotDue,
    Blocked,
    Unsupported,
}

public readonly record struct TaskEligibility(TaskEligibilityStatus Status, string Reason)
{
    public bool IsRunnable => Status == TaskEligibilityStatus.Runnable;

    public static TaskEligibility Runnable(string reason = "Ready") => new(TaskEligibilityStatus.Runnable, reason);
    public static TaskEligibility Disabled(string reason) => new(TaskEligibilityStatus.Disabled, reason);
    public static TaskEligibility NotDue(string reason) => new(TaskEligibilityStatus.NotDue, reason);
    public static TaskEligibility Blocked(string reason) => new(TaskEligibilityStatus.Blocked, reason);
    public static TaskEligibility Unsupported(string reason) => new(TaskEligibilityStatus.Unsupported, reason);
}

public sealed record AutomationPlanEntry(
    string Id,
    string Label,
    string Owner,
    string Phase,
    TaskEligibilityStatus Status,
    string Reason)
{
    public override string ToString()
        => $"{Id} ({Label}) owner={Owner} phase={Phase} status={Status} reason={Reason}";
}

public sealed record AutomationRunScope(
    string? SingleTaskId,
    bool BypassSelectedScheduling,
    bool AllowRunHooks)
{
    public static AutomationRunScope Full { get; } = new(null, false, true);

    public static AutomationRunScope SingleTask(string taskId)
    {
        if (string.IsNullOrWhiteSpace(taskId))
            throw new ArgumentException("A single-task run requires a task ID.", nameof(taskId));

        return new AutomationRunScope(taskId, true, false);
    }
}

public static class AutomationRunScopePolicy
{
    public static IReadOnlyList<string> FilterOrderedIds(
        IEnumerable<string> orderedIds,
        AutomationRunScope scope)
    {
        var ordered = orderedIds.ToList();
        if (scope.SingleTaskId == null)
            return ordered;

        return ordered.Contains(scope.SingleTaskId, StringComparer.Ordinal)
            ? [scope.SingleTaskId]
            : [];
    }

    public static bool IncludesTask(AutomationRunScope scope, string taskId)
        => scope.SingleTaskId == null ||
           string.Equals(scope.SingleTaskId, taskId, StringComparison.Ordinal);

    public static bool IsTaskSchedulingEnabled(
        AutomationRunScope scope,
        string taskId,
        bool configuredEnabled)
        => configuredEnabled ||
           scope.BypassSelectedScheduling &&
           string.Equals(scope.SingleTaskId, taskId, StringComparison.Ordinal);

    public static bool ShouldRunMiscHook(
        AutomationRunScope scope,
        bool enabled,
        bool beforeArRun)
        => scope.AllowRunHooks &&
           AutomationRunHookPolicy.ShouldRunMiscHook(enabled, beforeArRun);
}

public static class AutomationRunHookPolicy
{
    public static bool ShouldRunMiscHook(bool enabled, bool beforeArRun)
        => enabled && !beforeArRun;

    public static bool HasApplicableWork(bool hasRunnableEngineTask, bool miscHookRunnable)
        => hasRunnableEngineTask || miscHookRunnable;
}

public static class AutomationDispatchPlanner
{
    public static IReadOnlyList<string> BuildRunnableQueue(
        IEnumerable<string> orderedIds,
        IReadOnlyDictionary<string, TaskEligibility> eligibilityById)
        => orderedIds
            .Where(id => eligibilityById.TryGetValue(id, out var eligibility) && eligibility.IsRunnable)
            .ToList();
}
