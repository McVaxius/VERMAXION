using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

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
    RewardList,
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
                return new JumboCactpotRouteDecision(
                    purchaseDue && unclaimedTickets < 3 ? JumboCactpotRoute.Broker : JumboCactpotRoute.Wait,
                    null,
                    purchaseDue);

            return new JumboCactpotRouteDecision(
                scheduledPayoutWindow
                    ? JumboCactpotRoute.ScheduledCashier
                    : JumboCactpotRoute.RecoveryCashier,
                unclaimedTickets,
                purchaseDue);
        }

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
            ? JumboPurchaseUiEvidence.RewardList
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

    public static bool CanCompletePurchase(int? startingTicketCount, int verifiedPurchases)
    {
        return startingTicketCount is >= 0 and <= 3 &&
               verifiedPurchases >= 0 && startingTicketCount + verifiedPurchases == 3;
    }
}

// Run-local progress. Saved payout counts do not establish current-drawing ownership.
internal sealed class JumboCactpotPurchaseProgress
{
    private static readonly Regex OwnedNumberTextRegex = new(
        @"^(?:Your Number: \[(?<number>[0-9]{4})\]|Your Numbers: \[(?<number>[0-9]{4})\] \[(?<number>[0-9]{4})\])$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly List<int> ownedNumbers;
    public int StartingTicketCount { get; }
    public int ExpectedPurchases => 3 - StartingTicketCount;
    public int VerifiedPurchases => ownedNumbers.Count - StartingTicketCount;
    public int? PendingNumber { get; private set; }
    public bool IsComplete => JumboCactpotRecoveryPolicy.CanCompletePurchase(StartingTicketCount, VerifiedPurchases);

    // Addon 9307, LotteryWeeklyInput value 6. A ready input has room for another
    // ticket, so this field contains zero, one or two current-drawing numbers.
    // A null/unreadable field must never be interpreted as an empty ticket list.
    public static bool TryReadOwnedNumberText(string? text, out int[] numbers)
    {
        numbers = [];
        if (text == null)
            return false;
        if (text.Length == 0)
            return true;

        var match = OwnedNumberTextRegex.Match(text.Trim());
        if (!match.Success)
            return false;

        numbers = match.Groups["number"].Captures
            .Select(capture => int.Parse(capture.Value, CultureInfo.InvariantCulture)).ToArray();
        return true;
    }

    public bool MatchesOwnedNumbers(IReadOnlyList<int> numbers)
        => ownedNumbers.Order().SequenceEqual(numbers.Order());

    public JumboCactpotPurchaseProgress(IReadOnlyList<int> currentCycleNumbers)
    {
        if (currentCycleNumbers.Count > 3 || currentCycleNumbers.Any(number => number is < 0 or > 9999))
            throw new ArgumentException("Invalid current-cycle Jumbo tickets.", nameof(currentCycleNumbers));

        ownedNumbers = new List<int>(currentCycleNumbers);
        StartingTicketCount = currentCycleNumbers.Count;
    }

    public int? PrepareNumber(JumboCactpotNumberMode mode, int fixedNumber, Random random)
    {
        if (IsComplete || PendingNumber.HasValue)
            return PendingNumber;

        if (mode == JumboCactpotNumberMode.Fixed)
            return PendingNumber = Math.Clamp(fixedNumber, 0, 9999);

        // Pick uniformly from the unused numbers without repeatedly drawing duplicates.
        var excluded = ownedNumbers.Distinct().Order().ToArray();
        var number = random.Next(10000 - excluded.Length);
        foreach (var owned in excluded)
        {
            if (owned <= number)
                number++;
        }

        return PendingNumber = number;
    }

    public bool RecordPurchase(int number)
    {
        if (IsComplete || PendingNumber != number)
            return false;

        ownedNumbers.Add(number);
        PendingNumber = null;
        return true;
    }
}
