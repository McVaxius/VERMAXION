using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace VERMAXION.IPC;

public sealed class LifestreamIPC
{
    private readonly IPluginLog log;
    private readonly ICommandManager commandManager;
    private readonly ICallGateSubscriber<bool> isBusySubscriber;
    private readonly ICallGateSubscriber<string, object> executeCommandSubscriber;

    public LifestreamIPC(IDalamudPluginInterface pluginInterface, IPluginLog log, ICommandManager commandManager)
    {
        this.log = log;
        this.commandManager = commandManager;
        isBusySubscriber = pluginInterface.GetIpcSubscriber<bool>("Lifestream.IsBusy");
        executeCommandSubscriber = pluginInterface.GetIpcSubscriber<string, object>("Lifestream.ExecuteCommand");
    }

    public bool IsBusy()
    {
        try
        {
            return isBusySubscriber.InvokeFunc();
        }
        catch
        {
            return false;
        }
    }

    public bool ExecuteCommand(string command)
    {
        var normalized = command.Trim();
        try
        {
            executeCommandSubscriber.InvokeAction(normalized);
            log.Information($"[Lifestream] Executed command via IPC: {normalized}");
            return true;
        }
        catch (Exception ex)
        {
            var slashCommand = normalized.StartsWith("/li ", StringComparison.OrdinalIgnoreCase)
                ? normalized
                : $"/li {normalized}";

            log.Warning($"[Lifestream] IPC execute failed ({ex.Message}); using chat fallback: {slashCommand}");
            return commandManager.ProcessCommand(slashCommand);
        }
    }
}
