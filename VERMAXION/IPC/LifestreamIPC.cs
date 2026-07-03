using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using VERMAXION.Models;

namespace VERMAXION.IPC;

public sealed class LifestreamIPC
{
    private readonly IPluginLog log;
    private readonly ICommandManager commandManager;
    private readonly ICallGateSubscriber<bool> isBusySubscriber;
    private readonly ICallGateSubscriber<string, object> executeCommandSubscriber;
    private readonly ICallGateSubscriber<uint, bool> aethernetTeleportSubscriber;

    public LifestreamIPC(IDalamudPluginInterface pluginInterface, IPluginLog log, ICommandManager commandManager)
    {
        this.log = log;
        this.commandManager = commandManager;
        isBusySubscriber = pluginInterface.GetIpcSubscriber<bool>("Lifestream.IsBusy");
        executeCommandSubscriber = pluginInterface.GetIpcSubscriber<string, object>("Lifestream.ExecuteCommand");
        aethernetTeleportSubscriber = pluginInterface.GetIpcSubscriber<uint, bool>("Lifestream.AethernetTeleportById");
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
        var ipcCommand = LifestreamCommandPolicy.NormalizeForIpc(normalized);
        try
        {
            executeCommandSubscriber.InvokeAction(ipcCommand);
            log.Information($"[Lifestream] Executed command via IPC: {ipcCommand}");
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

    public bool AethernetTeleportById(uint aethernetId)
    {
        try
        {
            var accepted = aethernetTeleportSubscriber.InvokeFunc(aethernetId);
            log.Information($"[Lifestream] Aethernet teleport id={aethernetId}, accepted={accepted}");
            return accepted;
        }
        catch (Exception ex)
        {
            log.Warning($"[Lifestream] Aethernet teleport id={aethernetId} failed: {ex.Message}");
            return false;
        }
    }
}
