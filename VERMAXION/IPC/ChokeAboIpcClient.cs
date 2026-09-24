using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using VERMAXION.Models;

namespace VERMAXION.IPC;

public sealed class ChokeAboIpcClient
{
    public const string ShouldBlockRacingChannel = "ChokeAbo.Breeding.ShouldBlockRacing.V1";
    public const string EnsureTargetCycleChannel = "ChokeAbo.Breeding.EnsureTargetCycle.V3";
    public const string GetTargetCycleStatusChannel = "ChokeAbo.Breeding.GetTargetCycleStatus.V3";
    public const string PauseTargetCycleChannel = "ChokeAbo.Breeding.PauseTargetCycle.V3";

    private readonly ICallGateSubscriber<bool> shouldBlockRacingSubscriber;
    private readonly ICallGateSubscriber<string, string> ensureTargetCycleSubscriber;
    private readonly ICallGateSubscriber<string, string> getTargetCycleStatusSubscriber;
    private readonly ICallGateSubscriber<string, string> pauseTargetCycleSubscriber;
    private readonly ICallGateSubscriber<string, string> resumeTargetCycleSubscriber;
    private readonly ICallGateSubscriber<string, string> suspendTargetCycleSubscriber;
    public bool IsV3Available => ensureTargetCycleSubscriber.HasFunction && resumeTargetCycleSubscriber.HasFunction;

    public ChokeAboIpcClient(IDalamudPluginInterface pluginInterface)
    {
        shouldBlockRacingSubscriber = pluginInterface.GetIpcSubscriber<bool>(ShouldBlockRacingChannel);
        ensureTargetCycleSubscriber = pluginInterface.GetIpcSubscriber<string, string>(EnsureTargetCycleChannel);
        getTargetCycleStatusSubscriber = pluginInterface.GetIpcSubscriber<string, string>(GetTargetCycleStatusChannel);
        pauseTargetCycleSubscriber = pluginInterface.GetIpcSubscriber<string, string>(PauseTargetCycleChannel);
        resumeTargetCycleSubscriber = pluginInterface.GetIpcSubscriber<string, string>("ChokeAbo.Breeding.ResumeTargetCycle.V3");
        suspendTargetCycleSubscriber = pluginInterface.GetIpcSubscriber<string, string>("ChokeAbo.Breeding.SuspendTargetCycle.V3");
    }

    public bool ShouldBlockRacing()
    {
        try
        {
            return shouldBlockRacingSubscriber.InvokeFunc();
        }
        catch
        {
            return false;
        }
    }

    public ChokeAboTargetCycleCallResult EnsureTargetCycle(ulong contentId, CharacterConfig config, bool resume = false)
    {
        if (!ChokeAboTargetCycleProtocol.TryCreateEnsureRequestJson(
                contentId,
                config,
                out var request,
                out var error))
        {
            return ChokeAboTargetCycleCallResult.Failure(error);
        }

        return InvokeV3(resume ? resumeTargetCycleSubscriber : ensureTargetCycleSubscriber, request, contentId,
            resume ? "ResumeTargetCycle" : "EnsureTargetCycle");
    }

    public ChokeAboTargetCycleCallResult GetTargetCycleStatus(ulong contentId)
    {
        if (!ChokeAboTargetCycleProtocol.TryCreateIdentityRequestJson(contentId, out var request, out var error, 3))
            return ChokeAboTargetCycleCallResult.Failure(error);

        return InvokeV3(getTargetCycleStatusSubscriber, request, contentId, "GetTargetCycleStatus");
    }

    public ChokeAboTargetCycleCallResult PauseTargetCycle(ulong contentId)
    {
        if (!ChokeAboTargetCycleProtocol.TryCreateIdentityRequestJson(contentId, out var request, out var error, 3))
            return ChokeAboTargetCycleCallResult.Failure(error);

        return InvokeV3(pauseTargetCycleSubscriber, request, contentId, "PauseTargetCycle");
    }

    public ChokeAboTargetCycleCallResult SuspendTargetCycle(ulong contentId)
    {
        if (!ChokeAboTargetCycleProtocol.TryCreateIdentityRequestJson(contentId, out var request, out var error, 3))
            return ChokeAboTargetCycleCallResult.Failure(error);
        return InvokeV3(suspendTargetCycleSubscriber, request, contentId, "SuspendTargetCycle");
    }

    private static ChokeAboTargetCycleCallResult InvokeV3(
        ICallGateSubscriber<string, string> subscriber,
        string request,
        ulong contentId,
        string operation)
    {
        try
        {
            var response = subscriber.InvokeFunc(request);
            return ChokeAboTargetCycleProtocol.TryParseStatus(response, contentId, out var status, out var error, 3) && status != null
                ? ChokeAboTargetCycleCallResult.Success(status)
                : ChokeAboTargetCycleCallResult.Failure(error);
        }
        catch (Exception ex)
        {
            return ChokeAboTargetCycleCallResult.Failure($"Choke-abo V3 {operation} is unavailable: {ex.Message}");
        }
    }
}
