using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using VERMAXION.Models;

namespace VERMAXION.Services;

public class ARPostProcessService : IDisposable
{
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IPluginLog log;
    private readonly Action<string> onCharacterReady;
    private readonly Action beforeFinishPostprocess;
    private readonly Func<bool> canRequestPostprocess;

    private Dalamud.Plugin.Ipc.ICallGateSubscriber<object>? onAdditionalTaskSub;
    private Dalamud.Plugin.Ipc.ICallGateSubscriber<string, object>? onReadyForPostprocessSub;
    private Dalamud.Plugin.Ipc.ICallGateSubscriber<string, object>? requestPostprocessSub;
    private Dalamud.Plugin.Ipc.ICallGateSubscriber<object>? finishPostprocessSub;

    private const string PluginName = "Vermaxion";

    public bool IsProcessing { get; private set; } = false;
    public bool IsRequested { get; private set; } = false;
    public bool FinishSignaled { get; private set; } = false;
    private bool finishPreparationDone;
    private DateTime lastFinishAttemptAt = DateTime.MinValue;

    public ARPostProcessService(
        IDalamudPluginInterface pluginInterface,
        IPluginLog log,
        Action<string> onCharacterReady,
        Action beforeFinishPostprocess,
        Func<bool>? canRequestPostprocess = null)
    {
        this.pluginInterface = pluginInterface;
        this.log = log;
        this.onCharacterReady = onCharacterReady;
        this.beforeFinishPostprocess = beforeFinishPostprocess;
        this.canRequestPostprocess = canRequestPostprocess ?? (() => true);

        Initialize();
    }

    private void Initialize()
    {
        try
        {
            // Phase 1: Subscribe to OnCharacterAdditionalTask - AR fires this per-character
            onAdditionalTaskSub = pluginInterface.GetIpcSubscriber<object>("AutoRetainer.OnCharacterAdditionalTask");
            onAdditionalTaskSub.Subscribe(OnCharacterAdditionalTask);

            // Phase 2: Subscribe to OnCharacterReadyForPostprocess - AR fires when ready for us
            onReadyForPostprocessSub = pluginInterface.GetIpcSubscriber<string, object>("AutoRetainer.OnCharacterReadyForPostprocess");
            onReadyForPostprocessSub.Subscribe(OnCharacterReadyForPostprocess);

            // Outbound: Request and Finish channels
            requestPostprocessSub = pluginInterface.GetIpcSubscriber<string, object>("AutoRetainer.RequestCharacterPostprocess");
            finishPostprocessSub = pluginInterface.GetIpcSubscriber<object>("AutoRetainer.FinishCharacterPostprocessRequest");

            log.Information("[AR] AutoRetainer IPC registered successfully");
        }
        catch (Exception ex)
        {
            log.Warning($"[AR] Failed to register AutoRetainer IPC: {ex.Message}");
            log.Warning("[AR] AutoRetainer may not be installed. Plugin will work in manual mode only.");
        }
    }

    private void OnCharacterAdditionalTask()
    {
        try
        {
            if (!canRequestPostprocess())
            {
                log.Information("[AR] DAD handoff reservation owns the next cycle; VERMAXION did not request character postprocess.");
                return;
            }

            // Publish ownership intent before invoking AR. DAD may be subscribed earlier and must
            // still see VERMAXION busy when its own ready callback is delivered.
            IsRequested = true;
            FinishSignaled = false;
            finishPreparationDone = false;
            log.Information($"[AR] OnCharacterAdditionalTask fired - requesting postprocess for {PluginName}");
            requestPostprocessSub?.InvokeAction(PluginName);
        }
        catch (Exception ex)
        {
            IsRequested = false;
            log.Error($"[AR] Failed to request postprocess: {ex.Message}");
        }
    }

    private void OnCharacterReadyForPostprocess(string pluginName)
    {
        if (pluginName != PluginName) return;

        log.Information($"[AR] Character ready for postprocess — {PluginName}");
        IsProcessing = true;
        IsRequested = true;
        FinishSignaled = false;
        finishPreparationDone = false;
        lastFinishAttemptAt = DateTime.MinValue;

        try
        {
            onCharacterReady(pluginName);
        }
        catch (Exception ex)
        {
            log.Error($"[AR] Error in postprocess callback: {ex.Message}");
            log.Error("[AR] Retaining postprocess ownership after callback failure; use Full Stop to force release.");
        }
    }

    public bool FinishPostProcess(bool force = false, ARPostProcessFinishMode mode = ARPostProcessFinishMode.Normal)
    {
        if (FinishSignaled)
        {
            log.Debug("[AR] Ignoring duplicate finish signal.");
            return true;
        }

        var now = DateTime.UtcNow;
        if (!force &&
            lastFinishAttemptAt != DateTime.MinValue &&
            now - lastFinishAttemptAt < TimeSpan.FromSeconds(2))
        {
            return false;
        }

        lastFinishAttemptAt = now;
        try
        {
            if (!finishPreparationDone && ARPostProcessFinishPolicy.ShouldRunBeforeFinishCallback(mode))
            {
                finishPreparationDone = true;
                try
                {
                    beforeFinishPostprocess();
                }
                catch (Exception ex)
                {
                    log.Error($"[AR] Error before finish postprocess callback: {ex.Message}");
                }
            }

            log.Information($"[AR] Signaling AR to continue (FinishCharacterPostprocessRequest, mode={mode})");
            if (finishPostprocessSub == null)
                throw new InvalidOperationException("FinishCharacterPostprocessRequest IPC is unavailable.");

            finishPostprocessSub.InvokeAction();
            FinishSignaled = true;
            IsProcessing = false;
            IsRequested = false;
            return true;
        }
        catch (Exception ex)
        {
            log.Error($"[AR] Failed to signal finish: {ex.Message}");
            if (force)
            {
                IsProcessing = false;
                IsRequested = false;
                log.Warning("[AR] Full Stop cleared local postprocess ownership after finish-signal failure.");
            }

            return false;
        }
    }

    public void Dispose()
    {
        // CRITICAL: Always finish if we're processing to prevent AR from hanging
        if (IsProcessing || IsRequested)
        {
            log.Warning("[AR] Plugin unloading with requested/owned postprocess - signaling AR to continue");
            FinishPostProcess(force: true, mode: ARPostProcessFinishMode.ReleaseOnly);
        }

        try
        {
            onAdditionalTaskSub?.Unsubscribe(OnCharacterAdditionalTask);
            onReadyForPostprocessSub?.Unsubscribe(OnCharacterReadyForPostprocess);
        }
        catch (Exception ex)
        {
            log.Warning($"[AR] Error during IPC cleanup: {ex.Message}");
        }
    }
}
