using System;
using VERMAXION.Models;
using Xunit;

namespace VERMAXION.Tests;

public sealed class OceanFishingResultClosePolicyTests
{
    [Fact]
    public void ResultHandlingWaitsOneSecondBeforePolling()
    {
        var early = Decide(
            elapsed: TimeSpan.FromMilliseconds(999),
            sinceLastPoll: TimeSpan.MaxValue,
            addonVisible: true,
            addonReady: true);
        var ready = Decide(
            elapsed: TimeSpan.FromSeconds(1),
            sinceLastPoll: TimeSpan.MaxValue,
            addonVisible: true,
            addonReady: true);

        Assert.Equal(OceanFishingResultCloseAction.WaitInitialDelay, early.Action);
        Assert.Equal(OceanFishingResultCloseAction.FireCallback, ready.Action);
    }

    [Fact]
    public void ResultPollingUsesTwoHundredFiftyMillisecondCadence()
    {
        var early = Decide(
            elapsed: TimeSpan.FromSeconds(1),
            sinceLastPoll: TimeSpan.FromMilliseconds(249),
            addonVisible: true,
            addonReady: true);
        var due = Decide(
            elapsed: TimeSpan.FromSeconds(1),
            sinceLastPoll: TimeSpan.FromMilliseconds(250),
            addonVisible: true,
            addonReady: true);

        Assert.Equal(OceanFishingResultCloseAction.WaitForPollInterval, early.Action);
        Assert.Equal(OceanFishingResultCloseAction.FireCallback, due.Action);
    }

    [Fact]
    public void MissingAddonWithoutRealTransitionKeepsPollingInsteadOfClosing()
    {
        var decision = Decide(
            elapsed: OceanFishingResultClosePolicy.Timeout + TimeSpan.FromSeconds(10),
            sinceLastPoll: TimeSpan.FromMilliseconds(250),
            addonFound: false,
            addonVisible: false,
            addonReady: false);

        Assert.False(decision.ResultClosed);
        Assert.Equal(OceanFishingResultCloseAction.WaitPostVoyageTransition, decision.Action);
    }

    [Fact]
    public void RealTransitionWithoutAddonClosesAndWaitsForPlayerSettlement()
    {
        var decision = Decide(
            elapsed: TimeSpan.FromSeconds(1),
            sinceLastPoll: TimeSpan.FromMilliseconds(250),
            addonFound: false,
            addonVisible: false,
            addonReady: false,
            postVoyageTransitionObserved: true,
            postVoyageSettled: false);

        Assert.True(decision.ResultClosed);
        Assert.Equal(OceanFishingResultCloseAction.WaitPlayerSettlement, decision.Action);
        Assert.Equal("post-voyage transition started", decision.Reason);
    }

    [Fact]
    public void VisibleButNotReadyKeepsPollingPastDiagnosticTimeout()
    {
        var waiting = Decide(
            elapsed: TimeSpan.FromSeconds(1),
            sinceLastPoll: TimeSpan.FromMilliseconds(250),
            addonVisible: true,
            addonReady: false);
        var timedOut = Decide(
            elapsed: OceanFishingResultClosePolicy.Timeout,
            sinceLastPoll: TimeSpan.FromMilliseconds(250),
            addonVisible: true,
            addonReady: false);

        Assert.Equal(OceanFishingResultCloseAction.WaitForReadyAddon, waiting.Action);
        Assert.Equal(OceanFishingResultCloseAction.WaitForReadyAddon, timedOut.Action);
        Assert.False(timedOut.ResultClosed);
    }

    [Fact]
    public void ReadyAddonStillFiresAfterDiagnosticTimeout()
    {
        var decision = Decide(
            elapsed: OceanFishingResultClosePolicy.Timeout + TimeSpan.FromMinutes(1),
            sinceLastPoll: TimeSpan.FromMilliseconds(250),
            addonVisible: true,
            addonReady: true);

        Assert.Equal(OceanFishingResultCloseAction.FireCallback, decision.Action);
        Assert.False(decision.ResultClosed);
    }

    [Fact]
    public void VisibleReadyAddonFiresCallback()
    {
        var decision = Decide(
            elapsed: TimeSpan.FromSeconds(1),
            sinceLastPoll: TimeSpan.FromMilliseconds(250),
            addonVisible: true,
            addonReady: true);

        Assert.False(decision.ResultClosed);
        Assert.Equal(OceanFishingResultCloseAction.FireCallback, decision.Action);
    }

    [Fact]
    public void CallbackRetriesOnlyAfterVisibleAddonSettlementDelay()
    {
        var settling = Decide(
            elapsed: TimeSpan.FromSeconds(2),
            sinceLastPoll: TimeSpan.FromMilliseconds(250),
            sinceLastCallback: TimeSpan.FromMilliseconds(999),
            addonVisible: true,
            addonReady: true,
            callbackDispatched: true);
        var retry = Decide(
            elapsed: TimeSpan.FromSeconds(2),
            sinceLastPoll: TimeSpan.FromMilliseconds(250),
            sinceLastCallback: TimeSpan.FromSeconds(1),
            addonVisible: true,
            addonReady: true,
            callbackDispatched: true);

        Assert.Equal(OceanFishingResultCloseAction.WaitCallbackSettlement, settling.Action);
        Assert.Equal(OceanFishingResultCloseAction.FireCallback, retry.Action);
    }

    [Fact]
    public void CallbackDispatchedAndAddonDisappearsMarksResultClosed()
    {
        var decision = Decide(
            elapsed: TimeSpan.FromSeconds(1),
            sinceLastPoll: TimeSpan.FromMilliseconds(250),
            addonFound: false,
            addonVisible: false,
            addonReady: false,
            callbackDispatched: true);

        Assert.True(decision.ResultClosed);
        Assert.Equal(OceanFishingResultCloseAction.WaitPostVoyageTransition, decision.Action);
        Assert.Equal("result addon disappeared after close callback", decision.Reason);
    }

    [Fact]
    public void HiddenButStillFoundAddonMarksResultClosed()
    {
        var decision = Decide(
            elapsed: TimeSpan.FromSeconds(1),
            sinceLastPoll: TimeSpan.FromMilliseconds(250),
            addonFound: true,
            addonVisible: false,
            addonReady: false);

        Assert.True(decision.ResultClosed);
        Assert.Equal(OceanFishingResultCloseAction.WaitPostVoyageTransition, decision.Action);
        Assert.Equal("result addon is hidden", decision.Reason);
    }

    [Fact]
    public void PostCallbackTransitionWaitsForPlayerSettlement()
    {
        var waiting = Decide(
            elapsed: TimeSpan.FromSeconds(2),
            sinceLastPoll: TimeSpan.FromMilliseconds(250),
            addonFound: false,
            addonVisible: false,
            addonReady: false,
            callbackDispatched: true,
            postVoyageTransitionObserved: true,
            postVoyageSettled: false);
        var settled = Decide(
            elapsed: TimeSpan.FromSeconds(2),
            sinceLastPoll: TimeSpan.FromMilliseconds(250),
            addonFound: false,
            addonVisible: false,
            addonReady: false,
            callbackDispatched: true,
            postVoyageTransitionObserved: true,
            postVoyageSettled: true);

        Assert.True(waiting.ResultClosed);
        Assert.Equal(OceanFishingResultCloseAction.WaitPlayerSettlement, waiting.Action);
        Assert.Equal(OceanFishingResultCloseAction.Complete, settled.Action);
    }

    [Fact]
    public void VisibleAddonOverridesTransitionAndEarlierClosureEvidence()
    {
        var decision = Decide(
            elapsed: TimeSpan.FromSeconds(3),
            sinceLastPoll: TimeSpan.FromMilliseconds(250),
            addonVisible: true,
            addonReady: true,
            resultClosed: true,
            postVoyageTransitionObserved: true,
            postVoyageSettled: true);

        Assert.Equal(OceanFishingResultCloseAction.FireCallback, decision.Action);
        Assert.False(decision.ResultClosed);
    }

    private static OceanFishingResultCloseDecision Decide(
        TimeSpan elapsed,
        TimeSpan sinceLastPoll,
        bool addonVisible,
        bool addonReady,
        TimeSpan? sinceLastCallback = null,
        bool addonFound = true,
        bool callbackDispatched = false,
        bool resultClosed = false,
        bool postVoyageTransitionObserved = false,
        bool postVoyageSettled = false)
        => OceanFishingResultClosePolicy.Decide(new OceanFishingResultCloseSnapshot(
            elapsed,
            sinceLastPoll,
            sinceLastCallback ?? TimeSpan.MaxValue,
            addonFound,
            addonVisible,
            addonReady,
            callbackDispatched,
            resultClosed,
            postVoyageTransitionObserved,
            postVoyageSettled));
}
