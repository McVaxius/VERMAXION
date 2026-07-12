using System;
using System.Text.Json;
using VERMAXION.Models;
using Xunit;

namespace VERMAXION.Tests;

public sealed class JumboCactpotRoutingPolicyTests
{
    private static readonly DateTime Now = new(2026, 7, 11, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void KnownTwoRedeemableTicketsRouteDirectlyToCashierForExactlyTwoClaims()
    {
        var decision = JumboCactpotRoutingPolicy.Decide(
            Now,
            scheduledPayoutWindow: false,
            unclaimedTickets: 2,
            payoutAvailableAt: Now.AddHours(-1),
            purchaseDue: false);

        Assert.Equal(JumboCactpotRoute.RecoveryCashier, decision.Route);
        Assert.Equal(2, decision.ExpectedClaims);
        Assert.True(decision.ContinueToBrokerAfterClaims);
        Assert.True(JumboCactpotPayoutProgressPolicy.CanCompleteClaims(2, 2, discoveryExhausted: false));
        Assert.False(JumboCactpotPayoutProgressPolicy.CanCompleteClaims(2, 1, discoveryExhausted: false));
    }

    [Fact]
    public void OneOfTwoClaimsPersistsOneRemainingAndRetriesCashierFirst()
    {
        var remaining = JumboCactpotPayoutProgressPolicy.RemainingAfterVerifiedClaims(2, 1);
        var retry = JumboCactpotRoutingPolicy.Decide(
            Now,
            scheduledPayoutWindow: false,
            unclaimedTickets: remaining,
            payoutAvailableAt: Now.AddHours(-1),
            purchaseDue: true);

        Assert.Equal(1, remaining);
        Assert.Equal(JumboCactpotRoute.RecoveryCashier, retry.Route);
        Assert.Equal(1, retry.ExpectedClaims);
    }

    [Fact]
    public void FuturePurchasedTicketsWaitWithoutVisitingEitherNpc()
    {
        var decision = JumboCactpotRoutingPolicy.Decide(
            Now,
            scheduledPayoutWindow: false,
            unclaimedTickets: 3,
            payoutAvailableAt: Now.AddDays(2),
            purchaseDue: true);

        Assert.Equal(JumboCactpotRoute.Wait, decision.Route);
        Assert.False(decision.UsesCashier);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacyUnknownStateDiscoversAtCashierEvenWhenOldStampIsComplete(bool purchaseDue)
    {
        var decision = JumboCactpotRoutingPolicy.Decide(
            Now,
            scheduledPayoutWindow: false,
            unclaimedTickets: null,
            payoutAvailableAt: DateTime.MinValue,
            purchaseDue: purchaseDue);

        Assert.Equal(JumboCactpotRoute.DiscoveryCashier, decision.Route);
        Assert.True(decision.IsDiscovery);
        Assert.Equal(purchaseDue, decision.ContinueToBrokerAfterZero);
    }

    [Fact]
    public void KnownZeroRoutesToBrokerOnlyWhenPurchaseIsDue()
    {
        var due = JumboCactpotRoutingPolicy.Decide(Now, false, 0, DateTime.MinValue, purchaseDue: true);
        var complete = JumboCactpotRoutingPolicy.Decide(Now, false, 0, DateTime.MinValue, purchaseDue: false);

        Assert.Equal(JumboCactpotRoute.Broker, due.Route);
        Assert.Equal(JumboCactpotRoute.Wait, complete.Route);
    }

    [Fact]
    public void ScheduledTwoTicketPayoutFinishesWithoutReturningToBroker()
    {
        var decision = JumboCactpotRoutingPolicy.Decide(
            Now,
            scheduledPayoutWindow: true,
            unclaimedTickets: 2,
            payoutAvailableAt: Now,
            purchaseDue: true);

        Assert.Equal(JumboCactpotRoute.ScheduledCashier, decision.Route);
        Assert.Equal(2, decision.ExpectedClaims);
        Assert.False(decision.ContinueToBrokerAfterClaims);
    }

    [Fact]
    public void RecoveryTwoTicketPayoutReturnsToBrokerForCurrentCycleTickets()
    {
        var decision = JumboCactpotRoutingPolicy.Decide(Now, false, 2, Now.AddDays(-1), purchaseDue: false);

        Assert.Equal(JumboCactpotRoute.RecoveryCashier, decision.Route);
        Assert.True(decision.ContinueToBrokerAfterClaims);
    }

    [Fact]
    public void ZeroDiscoveryRequiresDialogueAndTheFullTimeout()
    {
        Assert.False(JumboCactpotPayoutProgressPolicy.CanAcceptZeroResult(true, false, false, 0, 10, 10));
        Assert.False(JumboCactpotPayoutProgressPolicy.CanAcceptZeroResult(true, true, false, 0, 9.9, 10));
        Assert.False(JumboCactpotPayoutProgressPolicy.CanAcceptZeroResult(true, true, true, 0, 10, 10));
        Assert.True(JumboCactpotPayoutProgressPolicy.CanAcceptZeroResult(true, true, false, 0, 10, 10));
    }

    [Fact]
    public void UnknownDiscoveryRequiresAStableCashierReturnBeforeTreatingClaimsAsExhausted()
    {
        Assert.False(JumboCactpotPayoutProgressPolicy.IsStableDiscoveryExhaustion(true, 1, false, 2, 1));
        Assert.False(JumboCactpotPayoutProgressPolicy.IsStableDiscoveryExhaustion(true, 1, true, 0.9, 1));
        Assert.True(JumboCactpotPayoutProgressPolicy.IsStableDiscoveryExhaustion(true, 1, true, 1, 1));
    }

    [Fact]
    public void LegacyJsonIsUnknownButNewCharactersStartAtKnownZero()
    {
        var legacy = JsonSerializer.Deserialize<CharacterConfig>("{}");
        var created = CharacterConfig.CreateNew();

        Assert.NotNull(legacy);
        Assert.Null(legacy!.JumboCactpotUnclaimedTickets);
        Assert.Equal(0, created.JumboCactpotUnclaimedTickets);
    }

    [Fact]
    public void ManualJumboResetReturnsTicketEvidenceToUnknown()
    {
        var config = CharacterConfig.CreateNew();
        config.JumboCactpotUnclaimedTickets = 2;
        config.JumboCactpotPayoutAvailableAt = Now;

        config.ResetJumboCactpotState();

        Assert.Null(config.JumboCactpotUnclaimedTickets);
        Assert.Equal(DateTime.MinValue, config.JumboCactpotPayoutAvailableAt);
    }
}
