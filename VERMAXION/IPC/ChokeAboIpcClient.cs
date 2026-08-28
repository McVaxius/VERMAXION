using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using VERMAXION.Models;

namespace VERMAXION.IPC;

public sealed class ChokeAboIpcClient
{
    public const string ShouldBlockRacingChannel = "ChokeAbo.Breeding.ShouldBlockRacing.V1";
    public const string EnsureTargetCycleChannel = "ChokeAbo.Breeding.EnsureTargetCycle.V2";
    public const string GetTargetCycleStatusChannel = "ChokeAbo.Breeding.GetTargetCycleStatus.V2";
    public const string PauseTargetCycleChannel = "ChokeAbo.Breeding.PauseTargetCycle.V2";

    private readonly ICallGateSubscriber<bool> shouldBlockRacingSubscriber;
    private readonly ICallGateSubscriber<string, string> ensureTargetCycleSubscriber;
    private readonly ICallGateSubscriber<string, string> getTargetCycleStatusSubscriber;
    private readonly ICallGateSubscriber<string, string> pauseTargetCycleSubscriber;

    public ChokeAboIpcClient(IDalamudPluginInterface pluginInterface)
    {
        shouldBlockRacingSubscriber = pluginInterface.GetIpcSubscriber<bool>(ShouldBlockRacingChannel);
        ensureTargetCycleSubscriber = pluginInterface.GetIpcSubscriber<string, string>(EnsureTargetCycleChannel);
        getTargetCycleStatusSubscriber = pluginInterface.GetIpcSubscriber<string, string>(GetTargetCycleStatusChannel);
        pauseTargetCycleSubscriber = pluginInterface.GetIpcSubscriber<string, string>(PauseTargetCycleChannel);
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

    public ChokeAboTargetCycleCallResult EnsureTargetCycle(ulong contentId, CharacterConfig config)
    {
        if (!ChokeAboTargetCycleProtocol.TryCreateEnsureRequestJson(
                contentId,
                config.ChocoboTargetPedigree,
                config.ChocoboRetirementRank,
                config.ChocoboPreferredFeedGrade,
                out var request,
                out var error))
        {
            return ChokeAboTargetCycleCallResult.Failure(error);
        }

        return InvokeV2(ensureTargetCycleSubscriber, request, contentId, "EnsureTargetCycle");
    }

    public ChokeAboTargetCycleCallResult GetTargetCycleStatus(ulong contentId)
    {
        if (!ChokeAboTargetCycleProtocol.TryCreateIdentityRequestJson(contentId, out var request, out var error))
            return ChokeAboTargetCycleCallResult.Failure(error);

        return InvokeV2(getTargetCycleStatusSubscriber, request, contentId, "GetTargetCycleStatus");
    }

    public ChokeAboTargetCycleCallResult PauseTargetCycle(ulong contentId)
    {
        if (!ChokeAboTargetCycleProtocol.TryCreateIdentityRequestJson(contentId, out var request, out var error))
            return ChokeAboTargetCycleCallResult.Failure(error);

        return InvokeV2(pauseTargetCycleSubscriber, request, contentId, "PauseTargetCycle");
    }

    private static ChokeAboTargetCycleCallResult InvokeV2(
        ICallGateSubscriber<string, string> subscriber,
        string request,
        ulong contentId,
        string operation)
    {
        try
        {
            var response = subscriber.InvokeFunc(request);
            return ChokeAboTargetCycleProtocol.TryParseStatus(response, contentId, out var status, out var error) && status != null
                ? ChokeAboTargetCycleCallResult.Success(status)
                : ChokeAboTargetCycleCallResult.Failure(error);
        }
        catch (Exception ex)
        {
            return ChokeAboTargetCycleCallResult.Failure($"Choke-abo V2 {operation} is unavailable: {ex.Message}");
        }
    }
}
