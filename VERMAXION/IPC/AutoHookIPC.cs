using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using VERMAXION.Models;

namespace VERMAXION.IPC;

public sealed class AutoHookIPC
{
    private readonly IPluginLog log;
    private readonly ICallGateSubscriber<bool> getPluginStateSubscriber;
    private readonly ICallGateSubscriber<bool, object> setPluginStateSubscriber;

    public AutoHookIPC(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.log = log;
        getPluginStateSubscriber = pluginInterface.GetIpcSubscriber<bool>("AutoHook.GetPluginState");
        setPluginStateSubscriber = pluginInterface.GetIpcSubscriber<bool, object>("AutoHook.SetPluginState");
    }

    public PluginStateReadResult ReadPluginState()
    {
        try
        {
            return PluginStateReadResult.Known(getPluginStateSubscriber.InvokeFunc());
        }
        catch (Exception ex)
        {
            log.Warning($"[AutoHook] GetPluginState failed: {ex.Message}");
            return PluginStateReadResult.Failed(ex.Message);
        }
    }

    public bool TrySetPluginState(bool enabled, out string error)
    {
        error = string.Empty;
        try
        {
            setPluginStateSubscriber.InvokeAction(enabled);
            var verification = ReadPluginState();
            if (!verification.Success)
            {
                error = $"SetPluginState({enabled}) was sent but verification failed: {verification.Error}";
                return false;
            }

            if (verification.Enabled != enabled)
            {
                error = $"SetPluginState({enabled}) did not change the verified state.";
                return false;
            }

            log.Information($"[AutoHook] Enabled={enabled} via IPC and verified.");
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            log.Warning($"[AutoHook] SetPluginState({enabled}) failed: {ex.Message}");
            return false;
        }
    }
}
