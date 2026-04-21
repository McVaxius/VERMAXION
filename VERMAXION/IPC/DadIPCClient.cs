using System;
using System.Text.Json;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using VERMAXION.Models;

namespace VERMAXION.IPC;

public sealed class DadIPCClient
{
    private readonly IPluginLog log;
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ICallGateSubscriber<bool> isReadySubscriber;
    private readonly ICallGateSubscriber<string> statusSubscriber;
    private readonly ICallGateSubscriber<string, string> startTasksSubscriber;
    private readonly ICallGateSubscriber<string> cancelSubscriber;

    public DadIPCClient(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.log = log;
        isReadySubscriber = pluginInterface.GetIpcSubscriber<bool>("dad.IsReady");
        statusSubscriber = pluginInterface.GetIpcSubscriber<string>("dad.GetStatus");
        startTasksSubscriber = pluginInterface.GetIpcSubscriber<string, string>("dad.StartTasks");
        cancelSubscriber = pluginInterface.GetIpcSubscriber<string>("dad.CancelActiveRun");
    }

    public bool IsReady()
    {
        try
        {
            return isReadySubscriber.InvokeFunc();
        }
        catch (Exception ex)
        {
            log.Debug($"[dad IPC] IsReady failed: {ex.Message}");
            return false;
        }
    }

    public DadRunResult GetStatus()
        => InvokeJson(statusSubscriber, DadRunResult.Idle(), "[dad IPC] GetStatus failed");

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
            log.Warning($"[dad IPC] Failed to deserialize payload: {ex.Message}");
            return fallback;
        }
    }
}
