using System;
using System.Globalization;
using System.Numerics;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using VERMAXION.Models;
using VERMAXION.Services;

namespace VERMAXION.IPC;

public class VNavmeshIPC : IDisposable
{
    private const string PointOnFloorIpc = "vnavmesh.Query.Mesh.PointOnFloor";
    private const string PathIsRunningIpc = "vnavmesh.Path.IsRunning";
    private const string NavIsReadyIpc = "vnavmesh.Nav.IsReady";
    private const string PathfindInProgressIpc = "vnavmesh.SimpleMove.PathfindInProgress";

    private readonly IPluginLog log;
    private readonly ICommandManager commandManager;
    private readonly ICallGateSubscriber<Vector3, bool, float, Vector3?> pointOnFloorSubscriber;
    private readonly ICallGateSubscriber<bool> pathIsRunningSubscriber;
    private readonly ICallGateSubscriber<bool> navIsReadySubscriber;
    private readonly ICallGateSubscriber<bool> pathfindInProgressSubscriber;
    private readonly GroundNavigationRecoveryTracker groundRecovery = new();
    private DateTime nextFloorQueryFailureLogAt = DateTime.MinValue;
    private DateTime nextPathStatusFailureLogAt = DateTime.MinValue;
    private DateTime nextNavStatusFailureLogAt = DateTime.MinValue;
    
    public bool IsReady { get; private set; } = true;
    public bool PathIsRunning { get; private set; }

    public VNavmeshIPC(IPluginLog log, ICommandManager commandManager)
    {
        this.log = log;
        this.commandManager = commandManager;
        pointOnFloorSubscriber = Plugin.PluginInterface
            .GetIpcSubscriber<Vector3, bool, float, Vector3?>(PointOnFloorIpc);
        pathIsRunningSubscriber = Plugin.PluginInterface
            .GetIpcSubscriber<bool>(PathIsRunningIpc);
        navIsReadySubscriber = Plugin.PluginInterface
            .GetIpcSubscriber<bool>(NavIsReadyIpc);
        pathfindInProgressSubscriber = Plugin.PluginInterface
            .GetIpcSubscriber<bool>(PathfindInProgressIpc);
        log.Information("[VNavmeshIPC] VNavmesh IPC initialized (command movement with path-status verification)");
    }
    
    public bool PathfindAndMoveTo(Vector3 position, bool fly = false)
    {
        var action = GroundNavigationRecoveryAction.Suppress;
        try
        {
            Vector3? playerPosition = !fly && GameHelpers.IsPlayerAvailable()
                ? Plugin.ObjectTable.LocalPlayer?.Position
                : null;
            action = groundRecovery.Evaluate(position, fly, playerPosition, DateTime.UtcNow);
            if (action == GroundNavigationRecoveryAction.Suppress)
                return false;

            if (action == GroundNavigationRecoveryAction.Recover)
            {
                log.Warning($"[VNavmeshIPC] Ground navigation stalled for {GroundNavigationRecoveryTracker.StallTimeout.TotalSeconds:F0}s; jumping once and reissuing {position}");
                GameHelpers.SendJump();
            }

            var x = position.X.ToString("F2", CultureInfo.InvariantCulture);
            var y = position.Y.ToString("F2", CultureInfo.InvariantCulture);
            var z = position.Z.ToString("F2", CultureInfo.InvariantCulture);
            var cmd = fly 
                ? $"/vnav flyto {x} {y} {z}"
                : $"/vnav moveto {x} {y} {z}";
            
            log.Debug($"[VNavmeshIPC] Sending: {cmd}");
            var dispatched = commandManager.ProcessCommand(cmd);
            if (!dispatched && action == GroundNavigationRecoveryAction.Dispatch && !fly)
                groundRecovery.Reset();
            return dispatched;
        }
        catch (Exception ex)
        {
            if (action == GroundNavigationRecoveryAction.Dispatch && !fly)
                groundRecovery.Reset();
            log.Error($"[VNavmeshIPC] PathfindAndMoveTo failed: {ex.Message}");
            return false;
        }
    }
    
    public bool Stop()
    {
        groundRecovery.Reset();
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

    /// <summary>Nav.IsReady — true when the current zone's navmesh is BUILT. False right after a zone
    /// load/login while the mesh builds; navigation commands issued then are queued behind the build.
    /// FAIL-CLOSED default (false) so callers wait rather than dispatch onto an unbuilt mesh.</summary>
    public bool TryGetNavReady(out bool ready)
    {
        try
        {
            ready = navIsReadySubscriber.InvokeFunc();
            return true;
        }
        catch (Exception ex)
        {
            ready = false;
            LogNavStatusFailure(ex);
            return false;
        }
    }

    /// <summary>SimpleMove.PathfindInProgress — true while a moveto's pathfind task is pending (possibly
    /// queued behind a mesh build). A pending task will START MOVING the toon when it completes, and it
    /// also makes vnavmesh REJECT new moveto requests — never hand control to another navigator while
    /// this is true.</summary>
    public bool TryGetPathfindInProgress(out bool inProgress)
    {
        try
        {
            inProgress = pathfindInProgressSubscriber.InvokeFunc();
            return true;
        }
        catch (Exception ex)
        {
            inProgress = false;
            LogNavStatusFailure(ex);
            return false;
        }
    }

    private void LogNavStatusFailure(Exception ex)
    {
        var now = DateTime.UtcNow;
        if (now < nextNavStatusFailureLogAt)
            return;
        nextNavStatusFailureLogAt = now + TimeSpan.FromSeconds(5);
        log.Debug($"[VNavmeshIPC] Nav status query failed: {ex.Message}");
    }

    public bool TryGetPathIsRunning(out bool isRunning)
    {
        try
        {
            isRunning = pathIsRunningSubscriber.InvokeFunc();
            PathIsRunning = isRunning;
            return true;
        }
        catch (Exception ex)
        {
            isRunning = false;
            var now = DateTime.UtcNow;
            if (now >= nextPathStatusFailureLogAt)
            {
                nextPathStatusFailureLogAt = now + TimeSpan.FromSeconds(5);
                log.Debug($"[VNavmeshIPC] Path.IsRunning query failed: {ex.Message}");
            }

            return false;
        }
    }
    
    public void UpdateStatus()
    {
        IsReady = true;
        TryGetPathIsRunning(out _);
    }

    public void Dispose()
    {
        groundRecovery.Reset();
    }
}
