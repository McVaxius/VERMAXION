using System;

namespace VERMAXION.Models;

internal enum JumboDialogueEvidence
{
    None,
    StaleRedeemablePayout,
}

internal enum JumboPurchaseUiEvidence
{
    None,
    PurchaseInput,
    CurrentTicketsAlreadyOwned,
}

internal enum JumboPayoutCompletionAction
{
    Fail,
    CompleteScheduledPayout,
    ContinueToPurchase,
}

internal enum JumboCactpotRoute
{
    Wait,
    Broker,
    ScheduledCashier,
    RecoveryCashier,
    DiscoveryCashier,
}

internal enum JumboCactpotCompletionKind
{
    None,
    PurchaseBatchEstablished,
    ScheduledPayoutComplete,
    PreservedExistingCompletion,
}

internal readonly record struct JumboCactpotRouteDecision(
    JumboCactpotRoute Route,
    int? ExpectedClaims,
    bool PurchaseDue)
{
    public bool UsesCashier => Route is JumboCactpotRoute.ScheduledCashier
        or JumboCactpotRoute.RecoveryCashier
        or JumboCactpotRoute.DiscoveryCashier;

    public bool IsDiscovery => UsesCashier && ExpectedClaims == null;

    public bool ContinueToBrokerAfterClaims =>
        Route is JumboCactpotRoute.RecoveryCashier or JumboCactpotRoute.DiscoveryCashier ||
        Route == JumboCactpotRoute.ScheduledCashier && PurchaseDue;

    public bool ContinueToBrokerAfterZero =>
        UsesCashier && PurchaseDue;
}

internal static class JumboCactpotRoutingPolicy
{
    public static JumboCactpotRouteDecision Decide(
        DateTime now,
        bool scheduledPayoutWindow,
        int? unclaimedTickets,
        DateTime payoutAvailableAt,
        bool purchaseDue)
    {
        if (!IsValidCount(unclaimedTickets))
            return new JumboCactpotRouteDecision(JumboCactpotRoute.DiscoveryCashier, null, purchaseDue);

        if (unclaimedTickets > 0)
        {
            if (payoutAvailableAt != DateTime.MinValue && now < payoutAvailableAt)
                return new JumboCactpotRouteDecision(JumboCactpotRoute.Wait, null, purchaseDue);

            return new JumboCactpotRouteDecision(
                scheduledPayoutWindow
                    ? JumboCactpotRoute.ScheduledCashier
                    : JumboCactpotRoute.RecoveryCashier,
                unclaimedTickets,
                purchaseDue);
        }

        if (scheduledPayoutWindow)
            return new JumboCactpotRouteDecision(JumboCactpotRoute.Wait, null, purchaseDue);

        return new JumboCactpotRouteDecision(
            purchaseDue ? JumboCactpotRoute.Broker : JumboCactpotRoute.Wait,
            null,
            purchaseDue);
    }

    private static bool IsValidCount(int? count) => count is >= 0 and <= 3;
}

internal static class JumboCactpotPayoutProgressPolicy
{
    public static int? RemainingAfterVerifiedClaims(int? knownStartingCount, int verifiedClaims)
    {
        if (knownStartingCount is null or < 0 or > 3)
            return null;

        return Math.Max(0, knownStartingCount.Value - Math.Max(0, verifiedClaims));
    }

    public static bool CanAcceptZeroResult(
        bool cashierDialogueObserved,
        bool payoutUiObserved,
        int verifiedClaims,
        double elapsedSeconds,
        double fullTimeoutSeconds)
        => cashierDialogueObserved &&
           !payoutUiObserved &&
           verifiedClaims == 0 &&
           elapsedSeconds >= fullTimeoutSeconds;

    public static bool CanAcceptPartialBatchExhaustion(
        int? expectedClaims,
        bool payoutUiObserved,
        int verifiedClaims,
        double elapsedSeconds,
        double fullTimeoutSeconds)
        => expectedClaims is { } expected &&
           expected > 0 &&
           payoutUiObserved &&
           verifiedClaims >= 1 &&
           verifiedClaims < expected &&
           elapsedSeconds >= fullTimeoutSeconds;

    public static bool CanCompleteClaims(int? expectedClaims, int verifiedClaims, bool cashierExhaustionConfirmed)
        => expectedClaims is { } expected
            ? expected > 0 &&
              (verifiedClaims == expected ||
               cashierExhaustionConfirmed && verifiedClaims >= 1 && verifiedClaims < expected)
            : verifiedClaims == 3 ||
              cashierExhaustionConfirmed && verifiedClaims is >= 1 and < 3;

    public static bool IsStableDiscoveryExhaustion(
        bool discovery,
        int verifiedClaims,
        bool cashierReturnVisible,
        double stableSeconds,
        double requiredStableSeconds)
        => discovery &&
           verifiedClaims is >= 1 and < 3 &&
           cashierReturnVisible &&
           stableSeconds >= requiredStableSeconds;
}

internal static class JumboCactpotRecoveryPolicy
{
    internal const string BrokerSpeaker = "Jumbo Cactpot Broker";
    internal const string RedeemablePayoutDialogue =
        "Why, we've already drawn the winning number for your ticket, sir. Why not claim your prize from the cashier over there?";

    public static JumboDialogueEvidence ClassifyDialogue(string chatType, string speaker, string text)
    {
        return string.Equals(chatType, "NPCDialogue", StringComparison.Ordinal) &&
               string.Equals(speaker, BrokerSpeaker, StringComparison.Ordinal) &&
               string.Equals(text, RedeemablePayoutDialogue, StringComparison.Ordinal)
            ? JumboDialogueEvidence.StaleRedeemablePayout
            : JumboDialogueEvidence.None;
    }

    public static bool IsCashierDialogue(string chatType, string speaker)
        => string.Equals(chatType, "NPCDialogue", StringComparison.Ordinal) &&
           string.Equals(speaker, "Cactpot Cashier", StringComparison.Ordinal);

    public static JumboPurchaseUiEvidence ClassifyPurchaseUi(bool purchaseInputVisible, bool rewardListVisible)
    {
        if (purchaseInputVisible)
            return JumboPurchaseUiEvidence.PurchaseInput;

        return rewardListVisible
            ? JumboPurchaseUiEvidence.CurrentTicketsAlreadyOwned
            : JumboPurchaseUiEvidence.None;
    }

    public static JumboPayoutCompletionAction GetPayoutCompletionAction(
        bool purchaseDue,
        bool payoutUiObserved,
        int verifiedClaims,
        int expectedClaims)
    {
        if (!payoutUiObserved || expectedClaims <= 0 || verifiedClaims < expectedClaims)
            return JumboPayoutCompletionAction.Fail;

        return purchaseDue
            ? JumboPayoutCompletionAction.ContinueToPurchase
            : JumboPayoutCompletionAction.CompleteScheduledPayout;
    }

    public static bool CanCompletePurchase(bool currentTicketsAlreadyOwned, int verifiedPurchases, int expectedPurchases)
    {
        return currentTicketsAlreadyOwned ||
               (expectedPurchases > 0 && verifiedPurchases >= expectedPurchases);
    }
}
