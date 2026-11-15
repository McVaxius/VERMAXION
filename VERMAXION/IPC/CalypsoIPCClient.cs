using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;

namespace VERMAXION.IPC;

// Calypso IPC v1, status schema 1. Only its existing single-task gates are used.
public sealed class CalypsoIPCClient
{
    private readonly ICallGateSubscriber<int> version;
    private readonly ICallGateSubscriber<string> status;
    private readonly ICallGateSubscriber<string, bool> run;
    private readonly ICallGateSubscriber<object?> stop;

    public CalypsoIPCClient(IDalamudPluginInterface pluginInterface)
    {
        version = pluginInterface.GetIpcSubscriber<int>("Calypso.GetVersion");
        status = pluginInterface.GetIpcSubscriber<string>("Calypso.GetStatusJson");
        run = pluginInterface.GetIpcSubscriber<string, bool>("Calypso.RunTask");
        stop = pluginInterface.GetIpcSubscriber<object?>("Calypso.Stop");
    }

    public CalypsoIslandStatus? ReadStatus()
    {
        try
        {
            return version.HasFunction && status.HasFunction && run.HasFunction && version.InvokeFunc() == 1
                ? CalypsoIslandStatus.Parse(status.InvokeFunc()) : null;
        }
        catch { return null; }
    }

    public bool RunTask(string taskId)
    {
        try { return run.InvokeFunc(taskId); }
        catch { return false; }
    }

    public void Stop() => stop.InvokeAction();
}

public sealed class CalypsoIslandStatus
{
    public int Schema { get; set; }
    public string Character { get; set; } = "";
    [JsonRequired]
    public bool Busy { get; set; }
    public string Mode { get; set; } = "";
    public string CurrentTaskId { get; set; } = "";
    public string Detail { get; set; } = "";
    public string LastResult { get; set; } = "";
    public bool InSanctuary { get; set; }
    public string Cycle { get; set; } = "";
    public List<CalypsoIslandTask> Tasks { get; set; } = [];

    public CalypsoIslandTask? Task(string id) => Tasks.FirstOrDefault(task => task.Id == id);

    public static CalypsoIslandStatus? Parse(string json)
    {
        try
        {
            var result = JsonSerializer.Deserialize<CalypsoIslandStatus>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return result is { Schema: 1, Tasks: not null } && ValidCycle(result.Cycle) &&
                   !string.IsNullOrWhiteSpace(result.Character) &&
                   result.Tasks.All(task => task != null && !string.IsNullOrWhiteSpace(task.Id) && !string.IsNullOrWhiteSpace(task.Name) && task.Failures >= 0) &&
                   result.Tasks.Select(task => task.Id).Distinct(StringComparer.Ordinal).Count() == result.Tasks.Count
                ? result : null;
        }
        catch (JsonException) { return null; }
    }

    public static bool ValidCycle(string? cycle)
    {
        var parts = cycle?.Split('.');
        return parts is { Length: 2 } && long.TryParse(parts[0], out _) &&
               int.TryParse(parts[1], out var day) && day is >= 0 and <= 6;
    }
}

public sealed class CalypsoIslandTask
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string State { get; set; } = "";
    public string Reason { get; set; } = "";
    public bool Enabled { get; set; }
    [JsonRequired]
    public int Failures { get; set; }

    // TaskRunner IPC 1 emits either "Name: done" or "Name: done (detail)".
    public bool HasSuccessfulResult(string? result)
    {
        var success = Name + ": done";
        return string.Equals(result, success, StringComparison.Ordinal) ||
               result != null && result.Length > success.Length + 3 &&
               result.StartsWith(success + " (", StringComparison.Ordinal) &&
               result.EndsWith(")", StringComparison.Ordinal);
    }

    // Schema 1 also uses Done for an OFF workshop or a logged-out profile.
    // These exact reasons instead come from the task's live state/ledger checks.
    public bool HasCompletionEvidence => State == "done" && (Id switch
    {
        "workshop.observe" => Reason == "read today",
        "workshop.agenda" => Reason is "planned today (suggest only)" or "tomorrow is set",
        "exports.sell" => Reason is "sold today" or "nothing at its cap",
        _ => false,
    });
}
