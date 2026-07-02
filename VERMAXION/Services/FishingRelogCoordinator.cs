using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using VERMAXION.IPC;
using VERMAXION.Models;

namespace VERMAXION.Services;

public sealed class FishingRelogCoordinator
{
    private readonly IPluginLog log;
    private readonly ARPostProcessService arPostProcessService;
    private readonly AutoRetainerIPC autoRetainerIPC;

    private IReadOnlyList<FishingRelogPrepStep> steps = Array.Empty<FishingRelogPrepStep>();
    private int stepIndex;
    private DateTime stepStartedAt = DateTime.MinValue;
    private string targetCharacterKey = string.Empty;

    public bool IsActive { get; private set; }
    public string StatusText { get; private set; } = "Idle";

    public FishingRelogCoordinator(
        IPluginLog log,
        ARPostProcessService arPostProcessService,
        AutoRetainerIPC autoRetainerIPC)
    {
        this.log = log;
        this.arPostProcessService = arPostProcessService;
        this.autoRetainerIPC = autoRetainerIPC;
    }

    public bool RequestRelog(string characterKey)
    {
        if (IsActive)
            return false;

        targetCharacterKey = characterKey.Trim();
        if (string.IsNullOrWhiteSpace(targetCharacterKey))
            return false;

        steps = FishingRelogPrepPolicy.BuildReleaseSequence(targetCharacterKey);
        stepIndex = 0;
        stepStartedAt = DateTime.MinValue;
        IsActive = true;
        StatusText = $"Preparing relog to {targetCharacterKey}";
        log.Information($"[Fishing][Relog] Starting AR release sequence for {targetCharacterKey}");
        return true;
    }

    public void Reset()
    {
        steps = Array.Empty<FishingRelogPrepStep>();
        stepIndex = 0;
        stepStartedAt = DateTime.MinValue;
        targetCharacterKey = string.Empty;
        IsActive = false;
        StatusText = "Idle";
    }

    public void Update()
    {
        if (!IsActive)
            return;

        if (stepIndex >= steps.Count)
        {
            log.Information($"[Fishing][Relog] Release sequence complete for {targetCharacterKey}");
            Reset();
            return;
        }

        var step = steps[stepIndex];
        switch (step.Action)
        {
            case FishingRelogPrepAction.FinishVermaxionPostprocess:
                StatusText = "Finishing Vermaxion AR postprocess";
                if (arPostProcessService.IsProcessing &&
                    !arPostProcessService.FinishPostProcess(mode: ARPostProcessFinishMode.ReleaseOnly))
                {
                    return;
                }

                AdvanceStep();
                break;

            case FishingRelogPrepAction.ReleaseVermaxionSuppression:
                StatusText = "Releasing Vermaxion AutoRetainer suppression";
                if (autoRetainerIPC.SuppressionOwnedByVermaxion &&
                    !autoRetainerIPC.ReleaseSuppressionIfOwned())
                {
                    return;
                }

                AdvanceStep();
                break;

            case FishingRelogPrepAction.SendCommand:
                StatusText = step.Command;
                log.Information($"[Fishing][Relog] Sending {step.Command}");
                CommandHelper.SendCommand(step.Command);
                AdvanceStep();
                break;

            case FishingRelogPrepAction.Wait:
                if (stepStartedAt == DateTime.MinValue)
                {
                    stepStartedAt = DateTime.UtcNow;
                    StatusText = $"Waiting {step.DelayMilliseconds}ms before relog step";
                    return;
                }

                if ((DateTime.UtcNow - stepStartedAt).TotalMilliseconds < step.DelayMilliseconds)
                    return;

                AdvanceStep();
                break;
        }
    }

    private void AdvanceStep()
    {
        stepIndex++;
        stepStartedAt = DateTime.MinValue;
    }
}
