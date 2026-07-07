using System;

namespace VERMAXION.Models;

internal readonly record struct OceanFishingResultAddonSnapshot(
    bool Found,
    bool Visible,
    bool Ready,
    string Detail)
{
    public static OceanFishingResultAddonSnapshot NotPolled { get; } =
        new(false, false, false, "not polled");
}

internal enum OceanFishingResultCloseAction
{
    WaitInitialDelay,
    WaitForPollInterval,
    WaitForReadyAddon,
    FireCallback,
    WaitCallbackSettlement,
    WaitPostVoyageTransition,
    WaitPlayerSettlement,
    Complete,
    Timeout,
}

internal readonly record struct OceanFishingResultCloseSnapshot(
    TimeSpan Elapsed,
    TimeSpan SinceLastPoll,
    TimeSpan SinceLastCallback,
    bool AddonFound,
    bool AddonVisible,
    bool AddonReady,
    bool CallbackDispatched,
    bool ResultClosed,
    bool PostVoyageTransitionObserved,
    bool PostVoyageSettled);

internal readonly record struct OceanFishingResultCloseDecision(
    OceanFishingResultCloseAction Action,
    bool ResultClosed,
    string Reason);

internal static class OceanFishingResultClosePolicy
{
    public static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan CallbackSettlementDelay = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    public static OceanFishingResultCloseDecision Decide(OceanFishingResultCloseSnapshot snapshot)
    {
        if (snapshot.Elapsed < InitialDelay)
            return Wait(OceanFishingResultCloseAction.WaitInitialDelay, "waiting for initial result delay");

        if (snapshot.ResultClosed ||
            snapshot.PostVoyageTransitionObserved ||
            snapshot.CallbackDispatched && !snapshot.AddonFound ||
            snapshot.AddonFound && !snapshot.AddonVisible)
        {
            return DecideAfterResultClosed(snapshot, GetCloseReason(snapshot));
        }

        if (!snapshot.AddonFound)
        {
            if (snapshot.SinceLastPoll < PollInterval)
                return Wait(OceanFishingResultCloseAction.WaitForPollInterval, "waiting for result poll interval");

            return Wait(OceanFishingResultCloseAction.WaitPostVoyageTransition, "waiting for result addon or post-voyage transition");
        }

        if (snapshot.Elapsed >= Timeout)
            return new(
                OceanFishingResultCloseAction.Timeout,
                ResultClosed: false,
                "result addon stayed visible through the close timeout");

        if (snapshot.CallbackDispatched && snapshot.SinceLastCallback < CallbackSettlementDelay)
            return Wait(OceanFishingResultCloseAction.WaitCallbackSettlement, "waiting for close callback settlement");

        if (snapshot.SinceLastPoll < PollInterval)
            return Wait(OceanFishingResultCloseAction.WaitForPollInterval, "waiting for result poll interval");

        if (!snapshot.AddonReady)
            return Wait(OceanFishingResultCloseAction.WaitForReadyAddon, "result addon visible but not ready");

        return Wait(OceanFishingResultCloseAction.FireCallback, "result addon is visible and ready");
    }

    private static OceanFishingResultCloseDecision DecideAfterResultClosed(
        OceanFishingResultCloseSnapshot snapshot,
        string reason)
    {
        if (snapshot.PostVoyageSettled)
            return new(OceanFishingResultCloseAction.Complete, ResultClosed: true, reason);

        return new(
            snapshot.PostVoyageTransitionObserved
                ? OceanFishingResultCloseAction.WaitPlayerSettlement
                : OceanFishingResultCloseAction.WaitPostVoyageTransition,
            ResultClosed: true,
            reason);
    }

    private static string GetCloseReason(OceanFishingResultCloseSnapshot snapshot)
    {
        if (snapshot.ResultClosed)
            return "result window already closed";
        if (snapshot.PostVoyageTransitionObserved)
            return "post-voyage transition started";
        if (snapshot.CallbackDispatched && !snapshot.AddonFound)
            return "result addon disappeared after close callback";
        return "result addon is hidden";
    }

    private static OceanFishingResultCloseDecision Wait(
        OceanFishingResultCloseAction action,
        string reason)
        => new(action, ResultClosed: false, reason);
}
