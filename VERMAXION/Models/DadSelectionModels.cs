using System;
using System.Collections.Generic;

namespace VERMAXION.Models;

public enum DadSelectionKind
{
    None = 0,
    Preset = 1,
    Schedule = 2,
}

public enum DadSelectionExecutionState
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Skipped = 3,
    Failed = 4,
    Cancelled = 5,
}

public enum DadSchedulerPresetPhase
{
    Idle = 0,
    Resolving = 1,
    LaunchingClients = 2,
    WaitingForHeartbeat = 3,
    LoadingCharacters = 4,
    ReadyToStart = 5,
    StartingPlanner = 6,
    StartedPlanner = 7,
    Completed = 8,
    Blocked = 9,
    TimedOut = 10,
    Cancelled = 11,
    Skipped = 12,
}

public enum DadScheduleRunStatus
{
    Idle = 0,
    Running = 1,
    Completed = 2,
    Blocked = 3,
    Cancelled = 4,
}

public sealed class DadSelectionCatalogItem
{
    public DadSelectionKind Kind { get; set; }
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public sealed class DadSelectionCatalog
{
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
    public bool Available { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<DadSelectionCatalogItem> Presets { get; set; } = [];
    public List<DadSelectionCatalogItem> Schedules { get; set; } = [];
}

public sealed class DadSelectionExecution
{
    public string OperationToken { get; set; } = string.Empty;
    public DadSelectionKind Kind { get; set; }
    public string SelectionId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public bool SubmissionAccepted { get; set; }
    public string DadResponseStatus { get; set; } = "NotSubmitted";
    public string SchedulerJobId { get; set; } = string.Empty;
    public string PlannerRequestId { get; set; } = string.Empty;
    public string ScheduleRunId { get; set; } = string.Empty;
    public DadSelectionExecutionState State { get; set; } = DadSelectionExecutionState.Pending;
    public string Summary { get; set; } = string.Empty;

    public bool IsTerminal => State is DadSelectionExecutionState.Completed
        or DadSelectionExecutionState.Skipped
        or DadSelectionExecutionState.Failed
        or DadSelectionExecutionState.Cancelled;
    public bool Success => State is DadSelectionExecutionState.Completed or DadSelectionExecutionState.Skipped;
    public string StatusText => DadSelectionSubmissionRules.BuildStatus(this);
}

public sealed class DadPlannerGroupCatalogRow
{
    public string GroupId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsTemplate { get; set; }
}

public sealed class DadPlannerGroupsSnapshot
{
    public List<DadPlannerGroupCatalogRow> Groups { get; set; } = [];
}

public sealed class DadScheduleCatalogRow
{
    public string ScheduleId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public sealed class DadScheduleSnapshot
{
    public string Summary { get; set; } = string.Empty;
    public List<DadScheduleCatalogRow> Schedules { get; set; } = [];
    public DadScheduleRunState ActiveRun { get; set; } = new();
    public List<DadScheduleRunResult> RecentResults { get; set; } = [];
}

public sealed class DadScheduleRunState
{
    public string RunId { get; set; } = string.Empty;
    public string ScheduleId { get; set; } = string.Empty;
    public string ScheduleName { get; set; } = string.Empty;
    public DadScheduleRunStatus Status { get; set; }
    public string RequestedBy { get; set; } = string.Empty;
    public string ActiveSchedulerJobId { get; set; } = string.Empty;
    public string ActivePlannerRequestId { get; set; } = string.Empty;
    public int TotalEntryExecutions { get; set; }
    public int CompletedEntryExecutions { get; set; }
    public int SkippedEntryExecutions { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string BlockedReason { get; set; } = string.Empty;
}

public sealed class DadScheduleRunResult
{
    public string RunId { get; set; } = string.Empty;
    public string ScheduleId { get; set; } = string.Empty;
    public DadScheduleRunStatus Status { get; set; }
    public bool Success { get; set; }
    public int TotalEntryExecutions { get; set; }
    public int CompletedEntryExecutions { get; set; }
    public int SkippedEntryExecutions { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string BlockedReason { get; set; } = string.Empty;
}

public sealed class DadSchedulerQueueSnapshot
{
    public string Summary { get; set; } = string.Empty;
    public DadScheduledCrewJob? ActiveJob { get; set; }
    public List<DadScheduledCrewJob> PendingJobs { get; set; } = [];
    public List<DadScheduledCrewJobResult> RecentResults { get; set; } = [];
    public DadSchedulerPresetState ActiveState { get; set; } = new();
}

public sealed class DadScheduledCrewJob
{
    public string JobId { get; set; } = string.Empty;
    public string GroupId { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = string.Empty;
}

public sealed class DadScheduledCrewJobResult
{
    public string JobId { get; set; } = string.Empty;
    public string GroupId { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = string.Empty;
    public DadSchedulerPresetPhase FinalPhase { get; set; }
    public bool Success { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string BlockedReason { get; set; } = string.Empty;
}

public sealed class DadSchedulerPresetState
{
    public string JobId { get; set; } = string.Empty;
    public string GroupId { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = string.Empty;
    public string PlannerRequestId { get; set; } = string.Empty;
    public DadSchedulerPresetPhase Phase { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string BlockedReason { get; set; } = string.Empty;
}

public sealed class DadScheduleCancelResult
{
    public string RunId { get; set; } = string.Empty;
    public bool Cancelled { get; set; }
    public string Summary { get; set; } = string.Empty;
}

public static class DadSelectionSubmissionRules
{
    public const string RequesterPrefix = "VERMAXION:";
    public const string StartPresetEndpoint = "dad.StartSchedulerPreset";
    public const string StartScheduleEndpoint = "dad.StartSchedule";

    public static bool TryPrepare(
        DadSelectionKind kind,
        string? selectionId,
        string operationToken,
        out string exactSelectionId,
        out string requestedBy,
        out string endpoint,
        out string rejection)
    {
        exactSelectionId = selectionId?.Trim() ?? string.Empty;
        requestedBy = $"{RequesterPrefix}{operationToken}";
        endpoint = kind switch
        {
            DadSelectionKind.Preset => StartPresetEndpoint,
            DadSelectionKind.Schedule => StartScheduleEndpoint,
            _ => string.Empty,
        };

        if (kind is not (DadSelectionKind.Preset or DadSelectionKind.Schedule))
        {
            rejection = "No DAD preset or schedule is selected.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(exactSelectionId))
        {
            rejection = $"The active character has no stable DAD {kind.ToString().ToLowerInvariant()} ID; nothing was submitted.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(operationToken))
        {
            rejection = "VERMAXION could not create an operation token; nothing was submitted.";
            return false;
        }

        rejection = string.Empty;
        return true;
    }

    public static bool IsPresetAccepted(
        DadRunStatus status,
        string expectedRequestedBy,
        string actualRequestedBy,
        string schedulerJobId)
        => status is DadRunStatus.Queued
            or DadRunStatus.WaitingForParticipants
            or DadRunStatus.Running
            or DadRunStatus.Completed &&
           string.Equals(expectedRequestedBy, actualRequestedBy, StringComparison.OrdinalIgnoreCase) &&
           !string.IsNullOrWhiteSpace(schedulerJobId);

    public static bool IsScheduleAccepted(
        DadScheduleRunStatus status,
        string expectedSelectionId,
        string actualSelectionId,
        string expectedRequestedBy,
        string actualRequestedBy,
        string runId)
        => status is DadScheduleRunStatus.Running or DadScheduleRunStatus.Completed &&
           string.Equals(expectedSelectionId, actualSelectionId, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(expectedRequestedBy, actualRequestedBy, StringComparison.OrdinalIgnoreCase) &&
           !string.IsNullOrWhiteSpace(runId);

    public static string BuildStatus(DadSelectionExecution execution)
    {
        var id = string.IsNullOrWhiteSpace(execution.SelectionId) ? "<missing>" : execution.SelectionId;
        var endpoint = string.IsNullOrWhiteSpace(execution.Endpoint) ? "not submitted" : execution.Endpoint;
        var response = execution.SubmissionAccepted ? "accepted" : "rejected";
        return $"{execution.Kind} id={id} via {endpoint} requestedBy={execution.RequestedBy} | DAD {response} ({execution.DadResponseStatus}) | {execution.State}: {execution.Summary}";
    }
}

public static class DadSelectionResultRules
{
    public static DadSelectionExecutionState FromSchedulerPhase(DadSchedulerPresetPhase phase, bool success)
        => phase switch
        {
            DadSchedulerPresetPhase.Skipped => DadSelectionExecutionState.Skipped,
            DadSchedulerPresetPhase.Completed when success => DadSelectionExecutionState.Completed,
            DadSchedulerPresetPhase.Cancelled => DadSelectionExecutionState.Cancelled,
            DadSchedulerPresetPhase.Blocked or DadSchedulerPresetPhase.TimedOut => DadSelectionExecutionState.Failed,
            DadSchedulerPresetPhase.StartedPlanner => DadSelectionExecutionState.Running,
            _ => DadSelectionExecutionState.Running,
        };

    public static DadSelectionExecutionState FromDadRun(DadRunStatus status)
        => status switch
        {
            DadRunStatus.Completed => DadSelectionExecutionState.Completed,
            DadRunStatus.Cancelled => DadSelectionExecutionState.Cancelled,
            DadRunStatus.Rejected or DadRunStatus.PartialFailure or DadRunStatus.TimedOut or DadRunStatus.Failed
                => DadSelectionExecutionState.Failed,
            _ => DadSelectionExecutionState.Running,
        };

    public static DadSelectionExecutionState FromSchedule(DadScheduleRunStatus status, int completed, int skipped)
        => status switch
        {
            DadScheduleRunStatus.Completed when completed > 0 && completed == skipped => DadSelectionExecutionState.Skipped,
            DadScheduleRunStatus.Completed => DadSelectionExecutionState.Completed,
            DadScheduleRunStatus.Cancelled => DadSelectionExecutionState.Cancelled,
            DadScheduleRunStatus.Blocked => DadSelectionExecutionState.Failed,
            _ => DadSelectionExecutionState.Running,
        };
}
