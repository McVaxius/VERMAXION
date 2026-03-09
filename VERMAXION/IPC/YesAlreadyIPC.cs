using System;
using System.Collections.Generic;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace VERMAXION.IPC;

public class YesAlreadyIPC : IDisposable
{
    private const string StopRequestsKey = "YesAlready.StopRequests";
    private const string LockName = "VERMAXION";

    private readonly IPluginLog log;
    private bool isPaused;

    public bool IsPaused => isPaused;

    public YesAlreadyIPC(IPluginLog log)
    {
        this.log = log;
    }

    public void Pause()
    {
        if (isPaused) return;

        try
        {
            var stopRequests = Plugin.PluginInterface.GetOrCreateData<HashSet<string>>(StopRequestsKey, () => []);
            stopRequests.Add(LockName);
            isPaused = true;
            log.Information("[YesAlready] Paused (added VERMAXION to StopRequests)");
        }
        catch (Exception ex)
        {
            log.Warning($"[YesAlready] Failed to pause: {ex.Message}");
        }
    }

    public void Unpause()
    {
        if (!isPaused) return;

        try
        {
            var stopRequests = Plugin.PluginInterface.GetOrCreateData<HashSet<string>>(StopRequestsKey, () => []);
            stopRequests.Remove(LockName);
            isPaused = false;
            log.Information("[YesAlready] Unpaused (removed VERMAXION from StopRequests)");
        }
        catch (Exception ex)
        {
            log.Warning($"[YesAlready] Failed to unpause: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (isPaused)
        {
            try
            {
                var stopRequests = Plugin.PluginInterface.GetOrCreateData<HashSet<string>>(StopRequestsKey, () => []);
                stopRequests.Remove(LockName);
                isPaused = false;
                log.Information("[YesAlready] Unpaused on dispose");
            }
            catch (Exception ex)
            {
                log.Warning($"[YesAlready] Failed to unpause on dispose: {ex.Message}");
            }
        }
    }
}
