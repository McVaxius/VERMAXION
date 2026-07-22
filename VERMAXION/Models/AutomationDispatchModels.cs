namespace VERMAXION.Models;

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
