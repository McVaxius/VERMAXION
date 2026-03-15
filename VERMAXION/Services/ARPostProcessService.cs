using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace VERMAXION.Services;

public class ARPostProcessService : IDisposable
{
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IPluginLog log;
    private readonly Action<string> onCharacterReady;

    private Dalamud.Plugin.Ipc.ICallGateSubscriber<object>? onAdditionalTaskSub;
    private Dalamud.Plugin.Ipc.ICallGateSubscriber<string, object>? onReadyForPostprocessSub;
    private Dalamud.Plugin.Ipc.ICallGateSubscriber<string, object>? requestPostprocessSub;
    private Dalamud.Plugin.Ipc.ICallGateSubscriber<object>? finishPostprocessSub;

    private const string PluginName = "Vermaxion";
    private bool isRegistered = false;

    public bool IsProcessing { get; private set; } = false;

    public ARPostProcessService(IDalamudPluginInterface pluginInterface, IPluginLog log, Action<string> onCharacterReady)
    {
        this.pluginInterface = pluginInterface;
        this.log = log;
        this.onCharacterReady = onCharacterReady;

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

            isRegistered = true;
            log.Information("[AR] AutoRetainer IPC registered successfully");
        }
        catch (Exception ex)
        {
            log.Warning($"[AR] Failed to register AutoRetainer IPC: {ex.Message}");
            log.Warning("[AR] AutoRetainer may not be installed. Plugin will work in manual mode only.");
            isRegistered = false;
        }
    }

    private void OnCharacterAdditionalTask()
    {
        try
        {
            log.Information($"[AR] OnCharacterAdditionalTask fired - requesting postprocess for {PluginName}");
            requestPostprocessSub?.InvokeAction(PluginName);
        }
        catch (Exception ex)
        {
            log.Error($"[AR] Failed to request postprocess: {ex.Message}");
        }
    }

    private void OnCharacterReadyForPostprocess(string pluginName)
    {
        if (pluginName != PluginName) return;

        log.Information($"[AR] Character ready for postprocess — {PluginName}");
        IsProcessing = true;

        // Force enable textadvance at the start of every PostARprocess
        CommandHelper.SendCommand("/at enable");
        log.Information("[AR] Textadvance enabled for postprocess");

        try
        {
            onCharacterReady(pluginName);
        }
        catch (Exception ex)
        {
            log.Error($"[AR] Error in postprocess callback: {ex.Message}");
            FinishPostProcess();
        }
    }

    public void FinishPostProcess()
    {
        try
        {
            log.Information("[AR] Signaling AR to continue (FinishCharacterPostprocessRequest)");
            finishPostprocessSub?.InvokeAction();
        }
        catch (Exception ex)
        {
            log.Error($"[AR] Failed to signal finish: {ex.Message}");
        }
        finally
        {
            IsProcessing = false;
        }
    }

    public void Dispose()
    {
        // CRITICAL: Always finish if we're processing to prevent AR from hanging
        if (IsProcessing)
        {
            log.Warning("[AR] Plugin unloading while processing - signaling AR to continue");
            FinishPostProcess();
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

        isRegistered = false;
    }
}
