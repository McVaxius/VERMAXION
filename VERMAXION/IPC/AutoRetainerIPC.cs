using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace VERMAXION.IPC;

public sealed class AutoRetainerIPC
{
    private readonly IPluginLog log;
    private readonly ICallGateSubscriber<bool> getSuppressedSubscriber;
    private readonly ICallGateSubscriber<bool, object> setSuppressedSubscriber;
    private readonly ICallGateSubscriber<bool> isBusySubscriber;
    private bool suppressionOwnedByVermaxion;

    public bool SuppressionOwnedByVermaxion => suppressionOwnedByVermaxion;

    public AutoRetainerIPC(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.log = log;
        getSuppressedSubscriber = pluginInterface.GetIpcSubscriber<bool>("AutoRetainer.GetSuppressed");
        setSuppressedSubscriber = pluginInterface.GetIpcSubscriber<bool, object>("AutoRetainer.SetSuppressed");
        isBusySubscriber = pluginInterface.GetIpcSubscriber<bool>("AutoRetainer.PluginState.IsBusy");
    }

    public bool GetSuppressed()
    {
        try
        {
            return getSuppressedSubscriber.InvokeFunc();
        }
        catch (Exception ex)
        {
            log.Warning($"[AR] GetSuppressed failed: {ex.Message}");
            return false;
        }
    }

    public bool IsBusy()
    {
        try
        {
            return isBusySubscriber.InvokeFunc();
        }
        catch (Exception ex)
        {
            log.Warning($"[AR] PluginState.IsBusy failed: {ex.Message}");
            return false;
        }
    }

    public bool TryAcquireSuppression()
    {
        if (suppressionOwnedByVermaxion)
            return true;

        if (GetSuppressed())
        {
            log.Information("[AR] Suppression already active; VMX will not own release.");
            return false;
        }

        try
        {
            setSuppressedSubscriber.InvokeAction(true);
            suppressionOwnedByVermaxion = true;
            log.Information("[AR] Suppressed AutoRetainer for before-AR tasks.");
            return true;
        }
        catch (Exception ex)
        {
            log.Warning($"[AR] SetSuppressed(true) failed: {ex.Message}");
            return false;
        }
    }

    public void ReleaseSuppressionIfOwned()
    {
        if (!suppressionOwnedByVermaxion)
            return;

        try
        {
            setSuppressedSubscriber.InvokeAction(false);
            log.Information("[AR] Released VMX AutoRetainer suppression.");
        }
        catch (Exception ex)
        {
            log.Warning($"[AR] SetSuppressed(false) failed: {ex.Message}");
        }
        finally
        {
            suppressionOwnedByVermaxion = false;
        }
    }
}
