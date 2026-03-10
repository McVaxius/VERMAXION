using System;
using System.Numerics;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace VERMAXION.IPC;

public class VNavmeshIPC : IDisposable
{
    private readonly IPluginLog log;
    private readonly ICommandManager commandManager;
    
    public bool IsReady { get; private set; } = true;
    public bool PathIsRunning { get; private set; }

    public VNavmeshIPC(IPluginLog log, ICommandManager commandManager)
    {
        this.log = log;
        this.commandManager = commandManager;
        log.Information("[VNavmeshIPC] VNavmesh IPC initialized (using command fallback)");
    }
    
    public bool PathfindAndMoveTo(Vector3 position, bool fly = false)
    {
        try
        {
            var cmd = fly 
                ? $"/vnav flyto {position.X:F2} {position.Y:F2} {position.Z:F2}"
                : $"/vnav moveto {position.X:F2} {position.Y:F2} {position.Z:F2}";
            
            log.Debug($"[VNavmeshIPC] Sending: {cmd}");
            return commandManager.ProcessCommand(cmd);
        }
        catch (Exception ex)
        {
            log.Error($"[VNavmeshIPC] PathfindAndMoveTo failed: {ex.Message}");
            return false;
        }
    }
    
    public bool Stop()
    {
        try
        {
            log.Debug("[VNavmeshIPC] Sending: /vnav stop");
            return commandManager.ProcessCommand("/vnav stop");
        }
        catch (Exception ex)
        {
            log.Error($"[VNavmeshIPC] Stop failed: {ex.Message}");
            return false;
        }
    }
    
    public void UpdateStatus()
    {
        // Can't check status via commands, assume it's ready
        IsReady = true;
        // We can't check PathIsRunning without IPC, so we'll use distance-based detection
    }

    public void Dispose()
    {
    }
}
