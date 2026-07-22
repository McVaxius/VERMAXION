using System;
using System.Globalization;
using System.Numerics;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace VERMAXION.IPC;

public class VNavmeshIPC : IDisposable
{
    private const string PointOnFloorIpc = "vnavmesh.Query.Mesh.PointOnFloor";

    private readonly IPluginLog log;
    private readonly ICommandManager commandManager;
    private readonly ICallGateSubscriber<Vector3, bool, float, Vector3?> pointOnFloorSubscriber;
    private DateTime nextFloorQueryFailureLogAt = DateTime.MinValue;
    
    public bool IsReady { get; private set; } = true;
    public bool PathIsRunning { get; private set; }

    public VNavmeshIPC(IPluginLog log, ICommandManager commandManager)
    {
        this.log = log;
        this.commandManager = commandManager;
        pointOnFloorSubscriber = Plugin.PluginInterface
            .GetIpcSubscriber<Vector3, bool, float, Vector3?>(PointOnFloorIpc);
        log.Information("[VNavmeshIPC] VNavmesh IPC initialized (using command fallback)");
    }
    
    public bool PathfindAndMoveTo(Vector3 position, bool fly = false)
    {
        try
        {
            var x = position.X.ToString("F2", CultureInfo.InvariantCulture);
            var y = position.Y.ToString("F2", CultureInfo.InvariantCulture);
            var z = position.Z.ToString("F2", CultureInfo.InvariantCulture);
            var cmd = fly 
                ? $"/vnav flyto {x} {y} {z}"
                : $"/vnav moveto {x} {y} {z}";
            
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

    public bool TryFindReachablePointOnFloor(
        Vector3 probe,
        float halfExtentXZ,
        out Vector3 point)
    {
        point = default;
        if (!float.IsFinite(probe.X) ||
            !float.IsFinite(probe.Y) ||
            !float.IsFinite(probe.Z) ||
            !float.IsFinite(halfExtentXZ) ||
            halfExtentXZ <= 0)
        {
            return false;
        }

        try
        {
            var resolved = pointOnFloorSubscriber.InvokeFunc(probe, false, halfExtentXZ);
            if (resolved is not { } candidate ||
                !float.IsFinite(candidate.X) ||
                !float.IsFinite(candidate.Y) ||
                !float.IsFinite(candidate.Z))
            {
                return false;
            }

            point = candidate;
            return true;
        }
        catch (Exception ex)
        {
            var now = DateTime.UtcNow;
            if (now >= nextFloorQueryFailureLogAt)
            {
                nextFloorQueryFailureLogAt = now + TimeSpan.FromSeconds(5);
                log.Debug($"[VNavmeshIPC] Read-only PointOnFloor query failed: {ex.Message}");
            }

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
