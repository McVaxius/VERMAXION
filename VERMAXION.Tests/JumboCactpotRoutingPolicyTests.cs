using System;
using System.Numerics;
using System.Text.Json;
using VERMAXION.Models;
using Xunit;

namespace VERMAXION.Tests;

public sealed class JumboCactpotRoutingPolicyTests
{
    private static readonly DateTime Now = new(2026, 7, 11, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AetheryteApproachRequiresBothClearingPointsBeforeEitherNpcAndDoesNotRepeatForHandoffs()
    {
        var route = new JumboCactpotApproachRoute();
        var broker = new Vector3(121.13345f, 13.0013f, -11.01155f);
        var cashier = new Vector3(124.05116f, 13.00253f, -19.59053f);
        route.BeginAfterAetheryteTravel();

        Assert.True(route.IsClearingPlanter);
        Assert.Equal(JumboCactpotApproachRoute.FirstClearingPoint, route.Destination(broker));
        Assert.Equal(JumboCactpotApproachRoute.FirstClearingPoint, route.Destination(cashier));
        Assert.False(route.TryAdvance(new Vector3(112.02f, 13, -37.43f), 3));
        Assert.False(route.TryAdvance(JumboCactpotApproachRoute.SecondClearingPoint, 3));
        Assert.False(route.TryAdvance(broker, 3));
        Assert.True(route.TryAdvance(JumboCactpotApproachRoute.FirstClearingPoint, 3));

        Assert.True(route.IsClearingPlanter);
        Assert.Equal(JumboCactpotApproachRoute.SecondClearingPoint, route.Destination(broker));
        Assert.False(route.TryAdvance(JumboCactpotApproachRoute.FirstClearingPoint, 3));
        Assert.False(route.TryAdvance(cashier, 3));
        Assert.True(route.TryAdvance(JumboCactpotApproachRoute.SecondClearingPoint, 3));

        Assert.False(route.IsClearingPlanter);
        Assert.Equal(broker, route.Destination(broker));
        Assert.Equal(cashier, route.Destination(cashier));
        Assert.False(route.TryAdvance(broker, 3));

        route.BeginAfterAetheryteTravel();
        Assert.True(route.IsClearingPlanter);
        Assert.False(route.TryAdvance(new Vector3(float.NaN, 13, -24.21f), 3));
        Assert.False(route.TryAdvance(JumboCactpotApproachRoute.FirstClearingPoint, float.NaN));
        route.Reset();
        Assert.False(route.IsClearingPlanter);
        Assert.Equal(cashier, route.Destination(cashier));
    }

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
        Assert.True(JumboCactpotPayoutProgressPolicy.CanCompleteClaims(2, 2, cashierExhaustionConfirmed: false));
        Assert.False(JumboCactpotPayoutProgressPolicy.CanCompleteClaims(2, 1, cashierExhaustionConfirmed: false));
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
    [InlineData(1)]
    [InlineData(2)]
    public void FuturePartialBatchResumesAtBrokerWhenPurchaseIsDue(int ownedCount)
    {
        var decision = JumboCactpotRoutingPolicy.Decide(Now, false, ownedCount, Now.AddDays(2), purchaseDue: true);
        Assert.Equal(JumboCactpotRoute.Broker, decision.Route);
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

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void KnownZeroRoutesToBrokerOnlyWhenPurchaseIsDue(bool scheduledPayoutWindow, bool purchaseDue)
    {
        var decision = JumboCactpotRoutingPolicy.Decide(
            Now,
            scheduledPayoutWindow,
            unclaimedTickets: 0,
            payoutAvailableAt: DateTime.MinValue,
            purchaseDue);

        Assert.Equal(purchaseDue ? JumboCactpotRoute.Broker : JumboCactpotRoute.Wait, decision.Route);
        Assert.False(decision.UsesCashier);
        Assert.Null(decision.ExpectedClaims);
        Assert.Equal(purchaseDue, decision.PurchaseDue);
    }

    [Fact]
    public void UnknownDiscoveryPersistedAsZeroPreventsRepeatCashierVisit()
    {
        var discovery = JumboCactpotRoutingPolicy.Decide(
            Now,
            scheduledPayoutWindow: true,
            unclaimedTickets: null,
            payoutAvailableAt: DateTime.MinValue,
            purchaseDue: false);
        var persisted = JumboCactpotRoutingPolicy.Decide(
            Now.AddMinutes(1),
            scheduledPayoutWindow: true,
            unclaimedTickets: 0,
            payoutAvailableAt: DateTime.MinValue,
            purchaseDue: false);

        Assert.Equal(JumboCactpotRoute.DiscoveryCashier, discovery.Route);
        Assert.Equal(JumboCactpotRoute.Wait, persisted.Route);
    }

    [Fact]
    public void ScheduledWindowStillWaitsForKnownFutureBatch()
    {
        var decision = JumboCactpotRoutingPolicy.Decide(
            Now,
            scheduledPayoutWindow: true,
            unclaimedTickets: 3,
            payoutAvailableAt: Now.AddHours(1),
            purchaseDue: false);

        Assert.Equal(JumboCactpotRoute.Wait, decision.Route);
        Assert.False(decision.UsesCashier);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void ScheduledTwoTicketPayoutReturnsToBrokerOnlyWhenPurchaseIsDue(
        bool purchaseDue,
        bool expectedContinue)
    {
        var decision = JumboCactpotRoutingPolicy.Decide(
            Now,
            scheduledPayoutWindow: true,
            unclaimedTickets: 2,
            payoutAvailableAt: Now,
            purchaseDue);

        Assert.Equal(JumboCactpotRoute.ScheduledCashier, decision.Route);
        Assert.Equal(2, decision.ExpectedClaims);
        Assert.Equal(expectedContinue, decision.ContinueToBrokerAfterClaims);
    }

    [Fact]
    public void RecoveryTwoTicketPayoutReturnsToBrokerForCurrentCycleTickets()
    {
        var decision = JumboCactpotRoutingPolicy.Decide(Now, false, 2, Now.AddDays(-1), purchaseDue: false);

        Assert.Equal(JumboCactpotRoute.RecoveryCashier, decision.Route);
        Assert.True(decision.ContinueToBrokerAfterClaims);
    }

    [Fact]
    public void ZeroResultRequiresCashierDialogueNoPayoutUiAndTheFullTimeout()
    {
        Assert.False(JumboCactpotPayoutProgressPolicy.CanAcceptZeroResult(false, false, 0, 10, 10));
        Assert.False(JumboCactpotPayoutProgressPolicy.CanAcceptZeroResult(true, false, 0, 9.9, 10));
        Assert.False(JumboCactpotPayoutProgressPolicy.CanAcceptZeroResult(true, true, 0, 10, 10));
        Assert.False(JumboCactpotPayoutProgressPolicy.CanAcceptZeroResult(true, false, 1, 10, 10));
        Assert.True(JumboCactpotPayoutProgressPolicy.CanAcceptZeroResult(true, false, 0, 10, 10));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void StalePositiveCountCanContinueAfterAuthoritativeZeroOnlyWhenPurchaseIsDue(
        bool purchaseDue,
        bool expectedContinue)
    {
        var decision = JumboCactpotRoutingPolicy.Decide(
            Now,
            scheduledPayoutWindow: true,
            unclaimedTickets: 3,
            payoutAvailableAt: Now,
            purchaseDue);

        Assert.Equal(3, decision.ExpectedClaims);
        Assert.Equal(expectedContinue, decision.ContinueToBrokerAfterZero);
        Assert.True(JumboCactpotPayoutProgressPolicy.CanAcceptZeroResult(true, false, 0, 10, 10));
    }

    [Fact]
    public void TwoVerifiedClaimsSatisfyStaleExpectedThreeOnlyAfterFullBatchExhaustion()
    {
        Assert.False(JumboCactpotPayoutProgressPolicy.CanAcceptPartialBatchExhaustion(3, false, 2, 10, 10));
        Assert.False(JumboCactpotPayoutProgressPolicy.CanAcceptPartialBatchExhaustion(3, true, 2, 9.9, 10));
        Assert.True(JumboCactpotPayoutProgressPolicy.CanAcceptPartialBatchExhaustion(3, true, 2, 10, 10));

        Assert.False(JumboCactpotPayoutProgressPolicy.CanCompleteClaims(3, 2, cashierExhaustionConfirmed: false));
        Assert.True(JumboCactpotPayoutProgressPolicy.CanCompleteClaims(3, 2, cashierExhaustionConfirmed: true));
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
