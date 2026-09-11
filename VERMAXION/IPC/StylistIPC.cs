using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace VERMAXION.IPC;

public sealed class StylistIPC
{
    private readonly IPluginLog log;
    private readonly ICallGateSubscriber<int, bool?, bool?, object> updateGearsetSubscriber;
    private readonly ICallGateSubscriber<bool> isBusySubscriber;
    internal bool UpdateMayBeActive { get; private set; }

    public StylistIPC(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.log = log;
        updateGearsetSubscriber = pluginInterface.GetIpcSubscriber<int, bool?, bool?, object>(
            "Stylist.UpdateGearsetIfNeededEx");
        isBusySubscriber = pluginInterface.GetIpcSubscriber<bool>("Stylist.IsBusy");
    }

    public bool TryStartUpdate(int gearsetId, out string error)
    {
        try
        {
            UpdateMayBeActive = isBusySubscriber.InvokeFunc();
            if (UpdateMayBeActive)
            {
                error = "Stylist is already busy.";
                return false;
            }

            // Keep uncertain dispatches owned until a successful idle read, including cancellation.
            UpdateMayBeActive = true;
            updateGearsetSubscriber.InvokeAction(gearsetId, null, true);
            error = string.Empty;
            log.Information($"[Stylist] Requested UpdateGearsetIfNeededEx({gearsetId}, null, true).");
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public bool TryReadBusy(out bool busy, out string error)
    {
        try
        {
            busy = isBusySubscriber.InvokeFunc();
            UpdateMayBeActive = busy;
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            busy = false;
            error = ex.Message;
            return false;
        }
    }
}
