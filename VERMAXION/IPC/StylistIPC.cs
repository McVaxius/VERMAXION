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
            if (isBusySubscriber.InvokeFunc())
            {
                error = "Stylist is already busy.";
                return false;
            }

            updateGearsetSubscriber.InvokeAction(gearsetId, null, false);
            error = string.Empty;
            log.Information($"[Stylist] Requested UpdateGearsetIfNeededEx({gearsetId}, null, false).");
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
