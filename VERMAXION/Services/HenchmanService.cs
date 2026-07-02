using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using ECommons.Reflection;
using VERMAXION.Models;

namespace VERMAXION.Services;

public class HenchmanService
{
    private const string HenchmanInternalName = "Henchman";
    private const string HenchmanTaskManagerTypeName = "Henchman.TaskManager.TaskManager";

    private readonly ICommandManager commandManager;
    private readonly IPluginLog log;
    private readonly ICallGateSubscriber<bool> isBusySubscriber;
    private bool wasRunning = false;
    private object? cachedPlugin;
    private AssemblyLoadContext? cachedLoadContext;
    private PropertyInfo? cachedTaskNameProperty;
    private FieldInfo? cachedTaskDescriptionField;

    public bool IsManaging { get; private set; } = false;

    public HenchmanService(IDalamudPluginInterface pluginInterface, ICommandManager commandManager, IPluginLog log)
    {
        this.commandManager = commandManager;
        this.log = log;
        isBusySubscriber = pluginInterface.GetIpcSubscriber<bool>("Henchman.IsBusy");
    }

    public HenchmanTakeoverReadiness GetTakeoverReadiness()
    {
        object henchmanPlugin;
        AssemblyLoadContext? loadContext;
        try
        {
            if (!DalamudReflector.TryGetDalamudPlugin(
                    HenchmanInternalName,
                    out henchmanPlugin,
                    out loadContext,
                    true,
                    true) ||
                henchmanPlugin == null)
            {
                InvalidateReflectionCache();
                return HenchmanTakeoverPolicy.Evaluate(
                    loaded: false,
                    busyReadSucceeded: false,
                    busy: false,
                    stateReadSucceeded: false,
                    taskName: null,
                    taskDescription: null);
            }
        }
        catch (Exception ex)
        {
            InvalidateReflectionCache();
            return HenchmanTakeoverPolicy.Evaluate(
                loaded: true,
                busyReadSucceeded: false,
                busy: false,
                stateReadSucceeded: false,
                taskName: null,
                taskDescription: null,
                failureReason: $"Henchman loaded/enabled state could not be confirmed: {ex.Message}");
        }

        if (!ReferenceEquals(cachedPlugin, henchmanPlugin) ||
            !ReferenceEquals(cachedLoadContext, loadContext))
        {
            InvalidateReflectionCache();
            cachedPlugin = henchmanPlugin;
            cachedLoadContext = loadContext;
        }

        bool busy;
        try
        {
            busy = isBusySubscriber.InvokeFunc();
        }
        catch (Exception ex)
        {
            return HenchmanTakeoverPolicy.Evaluate(
                loaded: true,
                busyReadSucceeded: false,
                busy: false,
                stateReadSucceeded: false,
                taskName: null,
                taskDescription: null,
                failureReason: $"Henchman.IsBusy IPC failed: {ex.Message}");
        }

        if (!busy)
        {
            return HenchmanTakeoverPolicy.Evaluate(
                loaded: true,
                busyReadSucceeded: true,
                busy: false,
                stateReadSucceeded: false,
                taskName: null,
                taskDescription: null);
        }

        if (!TryCacheTaskManagerMembers(henchmanPlugin, out var cacheFailure))
        {
            return HenchmanTakeoverPolicy.Evaluate(
                loaded: true,
                busyReadSucceeded: true,
                busy: true,
                stateReadSucceeded: false,
                taskName: null,
                taskDescription: null,
                failureReason: cacheFailure);
        }

        try
        {
            var taskName = cachedTaskNameProperty!.GetValue(null) as string;
            var taskDescription = GetLastTaskDescription(cachedTaskDescriptionField!.GetValue(null));
            return HenchmanTakeoverPolicy.Evaluate(
                loaded: true,
                busyReadSucceeded: true,
                busy: true,
                stateReadSucceeded: true,
                taskName,
                taskDescription);
        }
        catch (Exception ex)
        {
            InvalidateReflectionMembers();
            return HenchmanTakeoverPolicy.Evaluate(
                loaded: true,
                busyReadSucceeded: true,
                busy: true,
                stateReadSucceeded: false,
                taskName: null,
                taskDescription: null,
                failureReason: $"Henchman task state reflection failed: {ex.Message}");
        }
    }

    public void StopHenchman()
    {
        wasRunning = true;
        IsManaging = true;
        log.Information("[Henchman] Stopping Henchman via /henchman Stop");
        commandManager.ProcessCommand("/henchman Stop");
    }

    public void StartHenchman()
    {
        if (wasRunning)
        {
            log.Information("[Henchman] Restarting Henchman via /henchman OnABoat");
            commandManager.ProcessCommand("/henchman OnABoat");
        }
        wasRunning = false;
        IsManaging = false;
    }

    public void ForceRestart()
    {
        log.Information("[Henchman] Force restarting Henchman via /henchman OnABoat");
        commandManager.ProcessCommand("/henchman OnABoat");
        wasRunning = false;
        IsManaging = false;
    }

    private bool TryCacheTaskManagerMembers(object henchmanPlugin, out string failureReason)
    {
        if (cachedTaskNameProperty != null && cachedTaskDescriptionField != null)
        {
            failureReason = string.Empty;
            return true;
        }

        var taskManagerType = henchmanPlugin.GetType().Assembly.GetType(HenchmanTaskManagerTypeName);
        if (taskManagerType == null)
        {
            failureReason = $"Henchman type {HenchmanTaskManagerTypeName} was not available.";
            return false;
        }

        cachedTaskNameProperty = taskManagerType.GetProperty("TaskName", BindingFlags.Public | BindingFlags.Static);
        cachedTaskDescriptionField = taskManagerType.GetField("TaskDescription", BindingFlags.Public | BindingFlags.Static);
        if (cachedTaskNameProperty == null || cachedTaskDescriptionField == null)
        {
            InvalidateReflectionMembers();
            failureReason = "Henchman TaskName or TaskDescription public member was not available.";
            return false;
        }

        failureReason = string.Empty;
        return true;
    }

    private static string GetLastTaskDescription(object? descriptions)
    {
        if (descriptions is not IEnumerable enumerable)
            throw new InvalidOperationException("Henchman TaskDescription was not enumerable.");

        var lastDescription = string.Empty;
        foreach (var description in enumerable)
        {
            if (description is string text)
                lastDescription = text;
        }

        return lastDescription;
    }

    private void InvalidateReflectionCache()
    {
        cachedPlugin = null;
        cachedLoadContext = null;
        InvalidateReflectionMembers();
    }

    private void InvalidateReflectionMembers()
    {
        cachedTaskNameProperty = null;
        cachedTaskDescriptionField = null;
    }
}
