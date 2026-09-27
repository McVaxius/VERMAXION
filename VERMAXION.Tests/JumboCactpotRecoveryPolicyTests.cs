using System;
using System.Linq;
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
    public void RewardListWithoutPurchaseInputDoesNotEstablishCurrentOwnership()
    {
        var evidence = JumboCactpotRecoveryPolicy.ClassifyPurchaseUi(
            purchaseInputVisible: false,
            rewardListVisible: true);

        Assert.Equal(JumboPurchaseUiEvidence.RewardList, evidence);
        Assert.False(JumboCactpotRecoveryPolicy.CanCompletePurchase(null, 0));
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
    public void VerifiedPayoutContinuesIntoPurchaseWhenPurchaseIsDue()
    {
        var action = JumboCactpotRecoveryPolicy.GetPayoutCompletionAction(
            purchaseDue: true,
            payoutUiObserved: true,
            verifiedClaims: 3,
            expectedClaims: 3);

        Assert.Equal(JumboPayoutCompletionAction.ContinueToPurchase, action);
    }

    [Fact]
    public void VerifiedPayoutDoesNotContinueIntoPurchaseWhenPurchaseIsNotDue()
    {
        var action = JumboCactpotRecoveryPolicy.GetPayoutCompletionAction(
            purchaseDue: false,
            payoutUiObserved: true,
            verifiedClaims: 3,
            expectedClaims: 3);

        Assert.Equal(JumboPayoutCompletionAction.CompleteScheduledPayout, action);
    }

    [Fact]
    public void ExistingTicketsCompletePurchaseWithoutCashier()
    {
        Assert.True(JumboCactpotRecoveryPolicy.CanCompletePurchase(
            startingTicketCount: 3,
            verifiedPurchases: 0));
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 0)]
    [InlineData(null, 3)]
    [InlineData(4, 0)]
    [InlineData(-1, 4)]
    [InlineData(0, 4)]
    public void IncompletePurchaseNeverCompletes(int? existingTickets, int verifiedPurchases)
    {
        Assert.False(JumboCactpotRecoveryPolicy.CanCompletePurchase(
            existingTickets,
            verifiedPurchases));
    }

    [Theory]
    [InlineData("", new int[0])]
    [InlineData("Your Number: [0000]", new[] { 0 })]
    [InlineData("Your Number: [0001]", new[] { 1 })]
    [InlineData("Your Numbers: [0508] [0508]", new[] { 508, 508 })]
    [InlineData("Your Numbers: [0001] [9999]", new[] { 1, 9999 })]
    public void PurchaseInputReportsCurrentTicketMultiset(string text, int[] expected)
    {
        Assert.True(JumboCactpotPurchaseProgress.TryReadOwnedNumberText(text, out var numbers));
        Assert.Equal(expected, numbers);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("Retrieving data...")]
    [InlineData("Ticket Redeemable")]
    [InlineData("Your Number: [508]")]
    [InlineData("Your Number: [0508] [1234]")]
    [InlineData("Your Numbers: [0508]")]
    [InlineData("Your Numbers: [0508] [1234] [9999]")]
    [InlineData("Winning Number: 0508")]
    public void UnreadableOrNonPurchaseOwnershipDoesNotBecomeZero(string? text)
    {
        Assert.False(JumboCactpotPurchaseProgress.TryReadOwnedNumberText(text, out _));
    }

    [Fact]
    public void OverduePurchaseDuringPayoutWindowResumesAtBrokerAndCompletesDuplicateTicketBatch()
    {
        var now = new DateTime(2026, 7, 11, 18, 0, 0, DateTimeKind.Utc);
        var decision = JumboCactpotRoutingPolicy.Decide(
            now,
            scheduledPayoutWindow: true,
            unclaimedTickets: 0,
            payoutAvailableAt: DateTime.MinValue,
            purchaseDue: true);
        Assert.Equal(JumboCactpotRoute.Broker, decision.Route);

        Assert.True(JumboCactpotPurchaseProgress.TryReadOwnedNumberText("Your Numbers: [0508] [0508]", out var owned));
        var progress = new JumboCactpotPurchaseProgress(owned);
        Assert.Equal(2, progress.StartingTicketCount);
        Assert.Equal(1, progress.ExpectedPurchases);
        // Force a random selection at the excluded number's position.
        var number = progress.PrepareNumber(JumboCactpotNumberMode.Random, 508, new SelectedRandom(508));
        Assert.Equal(509, number);
        Assert.False(progress.IsComplete);
        Assert.Equal(0, progress.VerifiedPurchases);
        Assert.True(JumboCactpotPurchaseMessagePolicy.TryParsePurchasedNumber(
            "SystemMessage", "", "You use 200 MGP to purchase a Jumbo Cactpot ticket with the numbers 0509.",
            out var purchased));
        Assert.Equal(number, purchased);
        Assert.True(progress.RecordPurchase(purchased));
        Assert.True(progress.IsComplete);
        Assert.Equal(1, progress.VerifiedPurchases);
        Assert.True(progress.MatchesOwnedNumbers([508, 508, 509]));
        Assert.Null(progress.PrepareNumber(JumboCactpotNumberMode.Random, 508, new Random(1)));
        Assert.False(progress.RecordPurchase(purchased));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RestartBuysOnlyTheMissingTickets(int startingCount)
    {
        var owned = Enumerable.Repeat(508, startingCount).ToList();
        var progress = new JumboCactpotPurchaseProgress(owned);
        var missing = 3 - startingCount;
        Assert.Equal(missing, progress.ExpectedPurchases);

        for (var i = 0; i < missing; i++)
        {
            Assert.False(progress.IsComplete);
            var number = progress.PrepareNumber(JumboCactpotNumberMode.Random, 0, new SelectedRandom(508))!.Value;
            Assert.DoesNotContain(number, owned);
            Assert.True(progress.RecordPurchase(number));
            owned.Add(number);
            Assert.True(progress.MatchesOwnedNumbers(owned));
            Assert.False(progress.RecordPurchase(number));
        }

        Assert.True(progress.IsComplete);
        Assert.Equal(missing, progress.VerifiedPurchases);
        Assert.Null(progress.PrepareNumber(JumboCactpotNumberMode.Random, 0, new Random(1)));
    }

    [Fact]
    public void PendingNumberStaysStableUntilMatchingReceipt()
    {
        var progress = new JumboCactpotPurchaseProgress([]);
        var first = progress.PrepareNumber(JumboCactpotNumberMode.Random, 0, new SelectedRandom(9999));
        Assert.Equal(9999, first);
        Assert.Equal(first, progress.PrepareNumber(JumboCactpotNumberMode.Fixed, 508, new Random(1)));
        Assert.False(progress.RecordPurchase(508));
        Assert.Equal(first, progress.PendingNumber);
        Assert.Equal(0, progress.VerifiedPurchases);
        Assert.False(progress.IsComplete);
        Assert.True(progress.RecordPurchase(9999));
        Assert.Equal(0, progress.PrepareNumber(JumboCactpotNumberMode.Random, 0, new SelectedRandom(0)));
    }

    [Fact]
    public void ThreeReceiptsAtDifferentPricesAdvanceThroughTheWholeBatch()
    {
        var progress = new JumboCactpotPurchaseProgress([]);
        var owned = new System.Collections.Generic.List<int>();
        foreach (var price in new[] { 100, 150, 200 })
        {
            Assert.True(progress.MatchesOwnedNumbers(owned));
            var chosen = progress.PrepareNumber(JumboCactpotNumberMode.Random, 0, new SelectedRandom(0))!.Value;
            Assert.DoesNotContain(chosen, owned);
            Assert.True(JumboCactpotPurchaseMessagePolicy.TryParsePurchasedNumber(
                "SystemMessage", "", $"You use {price} MGP to purchase a Jumbo Cactpot ticket with the numbers {chosen:0000}.",
                out var purchased));
            Assert.True(progress.RecordPurchase(purchased));
            owned.Add(purchased);
            Assert.Equal(owned.Count, progress.VerifiedPurchases);
            if (!progress.IsComplete)
                Assert.True(JumboCactpotPurchaseConfirmationPolicy.ShouldConfirmNextTicketPrompt(
                    $"Buy another ticket?\nIt'll cost you {price + 50} MGP."));
        }
        Assert.Equal(new[] { 0, 1, 2 }, owned);
        Assert.True(progress.IsComplete);
    }

    [Fact]
    public void FixedModeMayRepeatOwnedAndNewNumbers()
    {
        var progress = new JumboCactpotPurchaseProgress([508]);
        for (var i = 0; i < 2; i++)
        {
            Assert.Equal(508, progress.PrepareNumber(JumboCactpotNumberMode.Fixed, 508, new Random(1)));
            Assert.True(progress.RecordPurchase(508));
        }
        Assert.True(progress.IsComplete);
        Assert.True(progress.MatchesOwnedNumbers([508, 508, 508]));
        Assert.False(progress.MatchesOwnedNumbers([508]));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(10000, 9999)]
    public void FixedModeStillClampsToFourDigits(int configured, int expected)
    {
        var progress = new JumboCactpotPurchaseProgress([]);
        Assert.Equal(expected, progress.PrepareNumber(JumboCactpotNumberMode.Fixed, configured, new Random(1)));
    }

    [Fact]
    public void InvalidOwnershipSnapshotIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new JumboCactpotPurchaseProgress([-1]));
        Assert.Throws<ArgumentException>(() => new JumboCactpotPurchaseProgress([10000]));
        Assert.Throws<ArgumentException>(() => new JumboCactpotPurchaseProgress([1, 2, 3, 4]));
    }

    private sealed class SelectedRandom(int selection) : Random
    {
        public override int Next(int maxValue)
        {
            Assert.InRange(selection, 0, maxValue - 1);
            return selection;
        }
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
