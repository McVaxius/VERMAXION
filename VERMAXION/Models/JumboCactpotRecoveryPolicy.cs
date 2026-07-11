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

    public static JumboPurchaseUiEvidence ClassifyPurchaseUi(bool purchaseInputVisible, bool rewardListVisible)
    {
        if (purchaseInputVisible)
            return JumboPurchaseUiEvidence.PurchaseInput;

        return rewardListVisible
            ? JumboPurchaseUiEvidence.CurrentTicketsAlreadyOwned
            : JumboPurchaseUiEvidence.None;
    }

    public static JumboPayoutCompletionAction GetPayoutCompletionAction(
        bool stalePayoutRecovery,
        bool payoutUiObserved,
        int verifiedClaims,
        int expectedClaims)
    {
        if (!payoutUiObserved || expectedClaims <= 0 || verifiedClaims < expectedClaims)
            return JumboPayoutCompletionAction.Fail;

        return stalePayoutRecovery
            ? JumboPayoutCompletionAction.ContinueToPurchase
            : JumboPayoutCompletionAction.CompleteScheduledPayout;
    }

    public static bool CanCompletePurchase(bool currentTicketsAlreadyOwned, int verifiedPurchases, int expectedPurchases)
    {
        return currentTicketsAlreadyOwned ||
               (expectedPurchases > 0 && verifiedPurchases >= expectedPurchases);
    }
}
