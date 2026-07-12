using VERMAXION.Models;
using Xunit;

namespace VERMAXION.Tests;

public sealed class JumboCactpotRecoveryPolicyTests
{
    [Fact]
    public void BrokerRedeemableDialogueClassifiesStalePayout()
    {
        var evidence = JumboCactpotRecoveryPolicy.ClassifyDialogue(
            "NPCDialogue",
            "Jumbo Cactpot Broker",
            "Why, we've already drawn the winning number for your ticket, sir. Why not claim your prize from the cashier over there?");

        Assert.Equal(JumboDialogueEvidence.StaleRedeemablePayout, evidence);
    }

    [Fact]
    public void EarlyDrawingDialogueDoesNotClassifyStalePayout()
    {
        var evidence = JumboCactpotRecoveryPolicy.ClassifyDialogue(
            "NPCDialogue",
            "Jumbo Cactpot Broker",
            "I'm afraid you're early for the drawing, sir. Can I help you with anything while you wait?");

        Assert.Equal(JumboDialogueEvidence.None, evidence);
    }

    [Theory]
    [InlineData("SystemMessage", "Jumbo Cactpot Broker")]
    [InlineData("NPCDialogue", "Cactpot Cashier")]
    [InlineData("NPCDialogue", "Mini Cactpot Broker")]
    public void UnrelatedChatMetadataDoesNotClassifyStalePayout(string chatType, string speaker)
    {
        var evidence = JumboCactpotRecoveryPolicy.ClassifyDialogue(
            chatType,
            speaker,
            JumboCactpotRecoveryPolicy.RedeemablePayoutDialogue);

        Assert.Equal(JumboDialogueEvidence.None, evidence);
    }

    [Theory]
    [InlineData("NPCDialogue", "Cactpot Cashier", true)]
    [InlineData("SystemMessage", "Cactpot Cashier", false)]
    [InlineData("NPCDialogue", "Jumbo Cactpot Broker", false)]
    public void CashierDialogueRequiresExactNpcMetadata(string chatType, string speaker, bool expected)
    {
        Assert.Equal(expected, JumboCactpotRecoveryPolicy.IsCashierDialogue(chatType, speaker));
    }

    [Fact]
    public void RewardListWithoutPurchaseInputClassifiesExistingTickets()
    {
        var evidence = JumboCactpotRecoveryPolicy.ClassifyPurchaseUi(
            purchaseInputVisible: false,
            rewardListVisible: true);

        Assert.Equal(JumboPurchaseUiEvidence.CurrentTicketsAlreadyOwned, evidence);
    }

    [Fact]
    public void PurchaseInputRetainsNormalPurchasePath()
    {
        var evidence = JumboCactpotRecoveryPolicy.ClassifyPurchaseUi(
            purchaseInputVisible: true,
            rewardListVisible: true);

        Assert.Equal(JumboPurchaseUiEvidence.PurchaseInput, evidence);
    }

    [Fact]
    public void MissingPurchaseUiRemainsAmbiguous()
    {
        var evidence = JumboCactpotRecoveryPolicy.ClassifyPurchaseUi(
            purchaseInputVisible: false,
            rewardListVisible: false);

        Assert.Equal(JumboPurchaseUiEvidence.None, evidence);
    }

    [Fact]
    public void VerifiedStalePayoutContinuesIntoPurchase()
    {
        var action = JumboCactpotRecoveryPolicy.GetPayoutCompletionAction(
            stalePayoutRecovery: true,
            payoutUiObserved: true,
            verifiedClaims: 3,
            expectedClaims: 3);

        Assert.Equal(JumboPayoutCompletionAction.ContinueToPurchase, action);
    }

    [Fact]
    public void VerifiedScheduledPayoutDoesNotContinueIntoPurchase()
    {
        var action = JumboCactpotRecoveryPolicy.GetPayoutCompletionAction(
            stalePayoutRecovery: false,
            payoutUiObserved: true,
            verifiedClaims: 3,
            expectedClaims: 3);

        Assert.Equal(JumboPayoutCompletionAction.CompleteScheduledPayout, action);
    }

    [Fact]
    public void ExistingTicketsCompletePurchaseWithoutCashier()
    {
        Assert.True(JumboCactpotRecoveryPolicy.CanCompletePurchase(
            currentTicketsAlreadyOwned: true,
            verifiedPurchases: 0,
            expectedPurchases: 3));
    }

    [Theory]
    [InlineData(false, 2, 3)]
    [InlineData(false, 0, 3)]
    public void IncompletePurchaseNeverCompletes(bool existingTickets, int verifiedPurchases, int expectedPurchases)
    {
        Assert.False(JumboCactpotRecoveryPolicy.CanCompletePurchase(
            existingTickets,
            verifiedPurchases,
            expectedPurchases));
    }

    [Theory]
    [InlineData(true, false, 3, 3)]
    [InlineData(true, true, 2, 3)]
    [InlineData(false, false, 3, 3)]
    [InlineData(false, true, 2, 3)]
    public void UnverifiedPayoutNeverCompletesOrChains(
        bool recovery,
        bool uiObserved,
        int verifiedClaims,
        int expectedClaims)
    {
        var action = JumboCactpotRecoveryPolicy.GetPayoutCompletionAction(
            recovery,
            uiObserved,
            verifiedClaims,
            expectedClaims);

        Assert.Equal(JumboPayoutCompletionAction.Fail, action);
    }
}
