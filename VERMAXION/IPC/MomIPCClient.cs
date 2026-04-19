using System;
using System.Text.Json;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using VERMAXION.Models;

namespace VERMAXION.IPC;

public sealed class MomIPCClient
{
    private readonly IPluginLog log;
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ICallGateSubscriber<bool> isReadySubscriber;
    private readonly ICallGateSubscriber<string> statusSubscriber;
    private readonly ICallGateSubscriber<int, string, string> startRunsSubscriber;
    private readonly ICallGateSubscriber<string> cancelSubscriber;
    private readonly ICallGateSubscriber<string> seriesRankSubscriber;

    public MomIPCClient(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.log = log;
        isReadySubscriber = pluginInterface.GetIpcSubscriber<bool>("mom.IsReady");
        statusSubscriber = pluginInterface.GetIpcSubscriber<string>("mom.GetStatus");
        startRunsSubscriber = pluginInterface.GetIpcSubscriber<int, string, string>("mom.StartCcRuns");
        cancelSubscriber = pluginInterface.GetIpcSubscriber<string>("mom.CancelActiveRun");
        seriesRankSubscriber = pluginInterface.GetIpcSubscriber<string>("mom.GetSeriesRank");
    }

    public bool IsReady()
    {
        try
        {
            return isReadySubscriber.InvokeFunc();
        }
        catch (Exception ex)
        {
            log.Debug($"[mom IPC] IsReady failed: {ex.Message}");
            return false;
        }
    }

    public MomRunResult GetStatus()
        => InvokeJson(statusSubscriber, MomRunResult.Idle(), "[mom IPC] GetStatus failed");

    public MomRunResult StartCcRuns(int runCount, string job)
    {
        try
        {
            var json = startRunsSubscriber.InvokeFunc(runCount, job);
            return Deserialize(json, new MomRunResult
            {
                Status = MomRunStatus.Failed,
                Summary = "mom returned an unreadable start result.",
                FailureReason = "Unreadable start result.",
            });
        }
        catch (Exception ex)
        {
            log.Warning($"[mom IPC] StartCcRuns failed: {ex.Message}");
            return new MomRunResult
            {
                Status = MomRunStatus.Failed,
                Summary = $"mom start failed: {ex.Message}",
                FailureReason = ex.Message,
                RequestedRunCount = runCount,
                RequestedJob = job ?? string.Empty,
            };
        }
    }

    public MomRunResult CancelActiveRun()
        => InvokeJson(cancelSubscriber, new MomRunResult
        {
            Status = MomRunStatus.Cancelled,
            Summary = "mom cancel result unavailable.",
            FailureReason = "Cancel result unavailable.",
        }, "[mom IPC] CancelActiveRun failed");

    public SeriesRankSnapshot GetSeriesRank()
        => InvokeJson(seriesRankSubscriber, new SeriesRankSnapshot
        {
            FailureReason = "Series rank result unavailable.",
            Source = "mom IPC",
            CapturedAtUtc = DateTime.UtcNow,
        }, "[mom IPC] GetSeriesRank failed");

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
            log.Warning($"[mom IPC] Failed to deserialize payload: {ex.Message}");
            return fallback;
        }
    }
}
