using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using VERMAXION.Models;

namespace VERMAXION.IPC;

public sealed class DadIPCClient
{
    private static readonly TimeSpan IsReadyCacheDuration = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan IsReadyFailureLogThrottle = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CatalogCacheDuration = TimeSpan.FromSeconds(5);

    private readonly IPluginLog log;
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ICallGateSubscriber<bool> isReadySubscriber;
    private readonly ICallGateSubscriber<string> statusSubscriber;
    private readonly ICallGateSubscriber<string> lanPartyPresetsSubscriber;
    private readonly ICallGateSubscriber<string, string> startTasksSubscriber;
    private readonly ICallGateSubscriber<string> cancelSubscriber;
    private readonly ICallGateSubscriber<string> plannerGroupsSubscriber;
    private readonly ICallGateSubscriber<string> schedulesSubscriber;
    private readonly ICallGateSubscriber<string, string> startSchedulerPresetSubscriber;
    private readonly ICallGateSubscriber<string> schedulerQueueSubscriber;
    private readonly ICallGateSubscriber<string, string> cancelScheduledJobSubscriber;
    private readonly ICallGateSubscriber<string, string> startScheduleSubscriber;
    private readonly ICallGateSubscriber<string, string> cancelScheduleSubscriber;
    private bool cachedIsReady;
    private DateTime cachedIsReadyAtUtc = DateTime.MinValue;
    private string lastIsReadyFailureMessage = string.Empty;
    private DateTime lastIsReadyFailureLoggedAtUtc = DateTime.MinValue;
    private DadSelectionCatalog cachedCatalog = new();
    private DateTime cachedCatalogAtUtc = DateTime.MinValue;

    public string LastSubmissionStatus { get; private set; } = string.Empty;

    public DadIPCClient(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.log = log;
        isReadySubscriber = pluginInterface.GetIpcSubscriber<bool>("dad.IsReady");
        statusSubscriber = pluginInterface.GetIpcSubscriber<string>("dad.GetStatus");
        lanPartyPresetsSubscriber = pluginInterface.GetIpcSubscriber<string>("dad.GetLanPartyPresets");
        startTasksSubscriber = pluginInterface.GetIpcSubscriber<string, string>("dad.StartTasks");
        cancelSubscriber = pluginInterface.GetIpcSubscriber<string>("dad.CancelActiveRun");
        plannerGroupsSubscriber = pluginInterface.GetIpcSubscriber<string>("dad.GetPlannerGroups");
        schedulesSubscriber = pluginInterface.GetIpcSubscriber<string>("dad.GetSchedules");
        startSchedulerPresetSubscriber = pluginInterface.GetIpcSubscriber<string, string>("dad.StartSchedulerPreset");
        schedulerQueueSubscriber = pluginInterface.GetIpcSubscriber<string>("dad.GetSchedulerQueue");
        cancelScheduledJobSubscriber = pluginInterface.GetIpcSubscriber<string, string>("dad.CancelScheduledJob");
        startScheduleSubscriber = pluginInterface.GetIpcSubscriber<string, string>("dad.StartSchedule");
        cancelScheduleSubscriber = pluginInterface.GetIpcSubscriber<string, string>("dad.CancelSchedule");
    }

    public bool IsReady(bool useCache = true)
    {
        var now = DateTime.UtcNow;
        if (useCache &&
            cachedIsReadyAtUtc != DateTime.MinValue &&
            now - cachedIsReadyAtUtc < IsReadyCacheDuration)
        {
            return cachedIsReady;
        }

        try
        {
            cachedIsReady = isReadySubscriber.InvokeFunc();
            cachedIsReadyAtUtc = now;
            ClearIsReadyFailure();
            return cachedIsReady;
        }
        catch (Exception ex)
        {
            cachedIsReady = false;
            cachedIsReadyAtUtc = now;
            LogIsReadyFailure(ex.Message, now);
            return false;
        }
    }

    public DadRunResult GetStatus()
        => InvokeJson(statusSubscriber, DadRunResult.Idle(), "[dad IPC] GetStatus failed");

    public string[] GetLanPartyPresets()
        => InvokeJson(lanPartyPresetsSubscriber, DadRunRequestOptions.LanPartyPresetStubs, "[dad IPC] GetLanPartyPresets failed");

    public DadRunResult StartTasks(DadRunRequest request)
    {
        try
        {
            var payload = JsonSerializer.Serialize(request, jsonOptions);
            var json = startTasksSubscriber.InvokeFunc(payload);
            return Deserialize(json, new DadRunResult
            {
                Status = DadRunStatus.Failed,
                Summary = "dad returned an unreadable start result.",
                FailureReason = "Unreadable start result.",
                Request = request,
                RequestedTaskCount = request.GetConfiguredTaskCount(),
                RequestedBy = request.RequestedBy,
            });
        }
        catch (Exception ex)
        {
            log.Warning($"[dad IPC] StartTasks failed: {ex.Message}");
            return new DadRunResult
            {
                Status = DadRunStatus.Failed,
                Summary = $"dad start failed: {ex.Message}",
                FailureReason = ex.Message,
                Request = request,
                RequestedTaskCount = request.GetConfiguredTaskCount(),
                RequestedBy = request.RequestedBy,
            };
        }
    }

    public DadRunResult CancelActiveRun()
        => InvokeJson(cancelSubscriber, new DadRunResult
        {
            Status = DadRunStatus.Cancelled,
            Summary = "dad cancel result unavailable.",
            FailureReason = "Cancel result unavailable.",
        }, "[dad IPC] CancelActiveRun failed");

    public DadSelectionCatalog GetSelectionCatalog(bool useCache = true)
    {
        var now = DateTime.UtcNow;
        if (useCache && now - cachedCatalogAtUtc < CatalogCacheDuration)
            return cachedCatalog;

        if (!IsReady())
        {
            cachedCatalog = new DadSelectionCatalog { Summary = "DAD IPC is unavailable." };
            cachedCatalogAtUtc = now;
            return cachedCatalog;
        }

        var groupSnapshot = InvokeJson(plannerGroupsSubscriber, new DadPlannerGroupsSnapshot(), "[dad IPC] GetPlannerGroups failed");
        var groups = groupSnapshot.Groups;
        var schedules = InvokeJson(schedulesSubscriber, new DadScheduleSnapshot(), "[dad IPC] GetSchedules failed");
        cachedCatalog = new DadSelectionCatalog
        {
            GeneratedAtUtc = now,
            Available = true,
            Summary = $"{groups.Count} preset(s), {schedules.Schedules.Count} schedule(s).",
            Presets = groups
                .Where(static group => !group.IsTemplate && !string.IsNullOrWhiteSpace(group.GroupId))
                .Select(static group => new DadSelectionCatalogItem
                {
                    Kind = DadSelectionKind.Preset,
                    Id = group.GroupId,
                    DisplayName = string.IsNullOrWhiteSpace(group.DisplayName) ? group.GroupId : group.DisplayName,
                })
                .OrderBy(static item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Schedules = schedules.Schedules
                .Where(static schedule => !string.IsNullOrWhiteSpace(schedule.ScheduleId))
                .Select(static schedule => new DadSelectionCatalogItem
                {
                    Kind = DadSelectionKind.Schedule,
                    Id = schedule.ScheduleId,
                    DisplayName = string.IsNullOrWhiteSpace(schedule.DisplayName) ? schedule.ScheduleId : schedule.DisplayName,
                })
                .OrderBy(static item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList(),
        };
        cachedCatalogAtUtc = now;
        return cachedCatalog;
    }

    public DadSelectionExecution StartSelection(DadSelectionKind kind, string selectionId, string displayName)
    {
        var token = Guid.NewGuid().ToString("N");
        var prepared = DadSelectionSubmissionRules.TryPrepare(
            kind,
            selectionId,
            token,
            out var exactSelectionId,
            out var requestedBy,
            out var endpoint,
            out var rejection);
        var execution = new DadSelectionExecution
        {
            OperationToken = token,
            Kind = kind,
            SelectionId = exactSelectionId,
            DisplayName = displayName?.Trim() ?? string.Empty,
            RequestedBy = requestedBy,
            Endpoint = endpoint,
            Summary = "Submitting DAD selection.",
        };

        if (!prepared)
        {
            execution.State = DadSelectionExecutionState.Failed;
            execution.Summary = rejection;
            return RecordSubmission(execution);
        }

        log.Information($"[dad IPC] Submitting {kind} id={execution.SelectionId} via {endpoint} requestedBy={requestedBy}.");

        if (kind == DadSelectionKind.Preset)
        {
            var start = InvokeJson(
                startSchedulerPresetSubscriber,
                new { groupId = execution.SelectionId, requestedBy },
                new DadRunResult { Status = DadRunStatus.Failed, Summary = "DAD preset start result unavailable." },
                "[dad IPC] StartSchedulerPreset failed");
            execution.PlannerRequestId = start.RequestId;
            execution.DadResponseStatus = start.Status.ToString();
            execution.Summary = start.Summary;
            BindPresetIdentifiers(execution, GetSchedulerQueue());
            execution.SubmissionAccepted = DadSelectionSubmissionRules.IsPresetAccepted(
                start.Status,
                execution.RequestedBy,
                start.RequestedBy,
                execution.SchedulerJobId);
            if (!execution.SubmissionAccepted)
            {
                execution.State = DadSelectionExecutionState.Failed;
                execution.Summary =
                    $"DAD did not accept exact preset ID '{execution.SelectionId}' for {execution.RequestedBy}; " +
                    $"response status={start.Status}, requestedBy={Text(start.RequestedBy)}, schedulerJobId={Text(execution.SchedulerJobId)}. " +
                    start.Summary;
                return RecordSubmission(execution);
            }

            execution.State = DadSelectionResultRules.FromDadRun(start.Status);
            if (execution.State == DadSelectionExecutionState.Completed &&
                (start.Summary?.Contains("Skipped", StringComparison.OrdinalIgnoreCase) ?? false))
            {
                execution.State = DadSelectionExecutionState.Skipped;
            }
            return RecordSubmission(execution);
        }

        if (kind == DadSelectionKind.Schedule)
        {
            var run = InvokeJson(
                startScheduleSubscriber,
                new { scheduleId = execution.SelectionId, dryRun = false, requestedBy },
                new DadScheduleRunState { Status = DadScheduleRunStatus.Blocked, Summary = "DAD schedule start result unavailable." },
                "[dad IPC] StartSchedule failed");
            execution.ScheduleRunId = run.RunId;
            execution.DadResponseStatus = run.Status.ToString();
            execution.Summary = run.Summary;
            execution.SubmissionAccepted = DadSelectionSubmissionRules.IsScheduleAccepted(
                run.Status,
                execution.SelectionId,
                run.ScheduleId,
                execution.RequestedBy,
                run.RequestedBy,
                run.RunId);
            if (!execution.SubmissionAccepted)
            {
                execution.State = DadSelectionExecutionState.Failed;
                execution.Summary =
                    $"DAD did not accept exact schedule ID '{execution.SelectionId}' for {execution.RequestedBy}; " +
                    $"response status={run.Status}, scheduleId={Text(run.ScheduleId)}, requestedBy={Text(run.RequestedBy)}, runId={Text(run.RunId)}. " +
                    run.Summary;
                return RecordSubmission(execution);
            }

            execution.State = DadSelectionResultRules.FromSchedule(run.Status, run.CompletedEntryExecutions, run.SkippedEntryExecutions);
            return RecordSubmission(execution);
        }

        execution.State = DadSelectionExecutionState.Failed;
        execution.Summary = "No DAD preset or schedule is selected.";
        return RecordSubmission(execution);
    }

    public DadSelectionExecution PollSelection(DadSelectionExecution execution)
    {
        if (execution.IsTerminal)
            return TrackStatus(execution);

        if (execution.Kind == DadSelectionKind.Schedule)
        {
            var snapshot = GetSchedules();
            var active = string.Equals(snapshot.ActiveRun.RunId, execution.ScheduleRunId, StringComparison.OrdinalIgnoreCase)
                ? snapshot.ActiveRun
                : null;
            if (active != null)
            {
                execution.Summary = active.Summary;
                execution.State = DadSelectionResultRules.FromSchedule(active.Status, active.CompletedEntryExecutions, active.SkippedEntryExecutions);
                return TrackStatus(execution);
            }

            var result = snapshot.RecentResults.FirstOrDefault(candidate =>
                string.Equals(candidate.RunId, execution.ScheduleRunId, StringComparison.OrdinalIgnoreCase));
            if (result != null)
            {
                execution.Summary = result.Summary;
                execution.State = DadSelectionResultRules.FromSchedule(result.Status, result.CompletedEntryExecutions, result.SkippedEntryExecutions);
            }
            else
            {
                execution.Summary = $"Waiting for exact DAD schedule run {execution.ScheduleRunId}.";
            }
            return TrackStatus(execution);
        }

        var queue = GetSchedulerQueue();
        BindPresetIdentifiers(execution, queue);
        var terminal = queue.RecentResults.FirstOrDefault(result =>
            !string.IsNullOrWhiteSpace(execution.SchedulerJobId) &&
            string.Equals(result.JobId, execution.SchedulerJobId, StringComparison.OrdinalIgnoreCase));
        if (terminal != null)
        {
            execution.State = DadSelectionResultRules.FromSchedulerPhase(terminal.FinalPhase, terminal.Success);
            execution.Summary = terminal.Summary;
            if (execution.State != DadSelectionExecutionState.Running)
                return TrackStatus(execution);
        }

        if (!string.IsNullOrWhiteSpace(execution.PlannerRequestId))
        {
            var run = GetStatus();
            if (string.Equals(run.RequestId, execution.PlannerRequestId, StringComparison.OrdinalIgnoreCase))
            {
                execution.State = DadSelectionResultRules.FromDadRun(run.Status);
                execution.Summary = run.Summary;
            }
            else
            {
                execution.State = DadSelectionExecutionState.Running;
                execution.Summary = $"Waiting for exact DAD planner request {execution.PlannerRequestId}.";
            }
        }
        else
        {
            execution.State = DadSelectionExecutionState.Running;
            execution.Summary = queue.Summary;
        }

        return TrackStatus(execution);
    }

    public bool CancelSelection(DadSelectionExecution execution)
    {
        if (execution.Kind == DadSelectionKind.Schedule && !string.IsNullOrWhiteSpace(execution.ScheduleRunId))
        {
            var result = InvokeJson(
                cancelScheduleSubscriber,
                new { runId = execution.ScheduleRunId, reason = "Cancelled by VERMAXION." },
                new DadScheduleCancelResult(),
                "[dad IPC] CancelSchedule failed");
            return result.Cancelled;
        }

        var cancelled = false;
        if (!string.IsNullOrWhiteSpace(execution.SchedulerJobId))
        {
            var snapshot = InvokeJson(
                cancelScheduledJobSubscriber,
                new { jobId = execution.SchedulerJobId, reason = "Cancelled by VERMAXION." },
                new DadSchedulerQueueSnapshot(),
                "[dad IPC] CancelScheduledJob failed");
            cancelled = snapshot.RecentResults.Any(result =>
                string.Equals(result.JobId, execution.SchedulerJobId, StringComparison.OrdinalIgnoreCase) &&
                result.FinalPhase == DadSchedulerPresetPhase.Cancelled);
        }

        if (!string.IsNullOrWhiteSpace(execution.PlannerRequestId))
        {
            var current = GetStatus();
            if (string.Equals(current.RequestId, execution.PlannerRequestId, StringComparison.OrdinalIgnoreCase) && !current.IsTerminal)
            {
                CancelActiveRun();
                cancelled = true;
            }
        }

        return cancelled;
    }

    public DadScheduleSnapshot GetSchedules()
        => InvokeJson(schedulesSubscriber, new DadScheduleSnapshot(), "[dad IPC] GetSchedules failed");

    public DadSchedulerQueueSnapshot GetSchedulerQueue()
        => InvokeJson(schedulerQueueSubscriber, new DadSchedulerQueueSnapshot(), "[dad IPC] GetSchedulerQueue failed");

    private static void BindPresetIdentifiers(DadSelectionExecution execution, DadSchedulerQueueSnapshot snapshot)
    {
        var state = snapshot.ActiveState;
        if (string.Equals(state.RequestedBy, execution.RequestedBy, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(state.GroupId, execution.SelectionId, StringComparison.OrdinalIgnoreCase))
        {
            execution.SchedulerJobId = state.JobId;
            if (!string.IsNullOrWhiteSpace(state.PlannerRequestId))
                execution.PlannerRequestId = state.PlannerRequestId;
        }

        var result = snapshot.RecentResults.FirstOrDefault(candidate =>
            string.Equals(candidate.RequestedBy, execution.RequestedBy, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.GroupId, execution.SelectionId, StringComparison.OrdinalIgnoreCase));
        if (result != null)
            execution.SchedulerJobId = result.JobId;
    }

    private DadSelectionExecution RecordSubmission(DadSelectionExecution execution)
    {
        LastSubmissionStatus = execution.StatusText;
        if (execution.SubmissionAccepted)
            log.Information($"[dad IPC] {execution.StatusText}");
        else
            log.Warning($"[dad IPC] {execution.StatusText}");
        return execution;
    }

    private DadSelectionExecution TrackStatus(DadSelectionExecution execution)
    {
        LastSubmissionStatus = execution.StatusText;
        return execution;
    }

    private static string Text(string value)
        => string.IsNullOrWhiteSpace(value) ? "<empty>" : value;

    private T InvokeJson<T>(ICallGateSubscriber<string> subscriber, T fallback, string logMessage)
    {
        try
        {
            var json = subscriber.InvokeFunc();
            return Deserialize(json, fallback);
        }
        catch (Exception ex)
        {
            log.Debug($"{logMessage}: {ex.Message}");
            return fallback;
        }
    }

    private TResponse InvokeJson<TRequest, TResponse>(
        ICallGateSubscriber<string, string> subscriber,
        TRequest request,
        TResponse fallback,
        string logMessage)
    {
        try
        {
            var payload = JsonSerializer.Serialize(request, jsonOptions);
            return Deserialize(subscriber.InvokeFunc(payload), fallback);
        }
        catch (Exception ex)
        {
            log.Warning($"{logMessage}: {ex.Message}");
            return fallback;
        }
    }

    private T Deserialize<T>(string json, T fallback)
    {
        if (string.IsNullOrWhiteSpace(json))
            return fallback;

        try
        {
            return JsonSerializer.Deserialize<T>(json, jsonOptions) ?? fallback;
        }
        catch (Exception ex)
        {
            log.Warning($"[dad IPC] Failed to deserialize payload: {ex.Message}");
            return fallback;
        }
    }

    private void LogIsReadyFailure(string message, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        if (message == lastIsReadyFailureMessage &&
            now - lastIsReadyFailureLoggedAtUtc < IsReadyFailureLogThrottle)
        {
            return;
        }

        log.Debug($"[dad IPC] IsReady failed: {message}");
        lastIsReadyFailureMessage = message;
        lastIsReadyFailureLoggedAtUtc = now;
    }

    private void ClearIsReadyFailure()
    {
        lastIsReadyFailureMessage = string.Empty;
        lastIsReadyFailureLoggedAtUtc = DateTime.MinValue;
    }
}
