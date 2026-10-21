using System;
using System.Collections.Generic;
using System.Linq;
using VERMAXION.Beastmaster;
using Xunit;

namespace VERMAXION.Tests;

public sealed class BeastmasterWeeklyTests
{
    [Fact]
    public void NativeCompletionBitsUseRowMinusOneAndRejectUnsupportedIndices()
    {
        var flags = new byte[13];
        flags[0] = 1; flags[11] = 0b0001_1111; flags[12] = 0b1000_0000;
        Assert.True(BeastmasterWeeklyPolicy.TryReadCompletion(flags, 104, 1, out var first));
        Assert.True(first);
        foreach (var row in new uint[] { 89, 90, 91, 92, 93 })
        {
            Assert.True(BeastmasterWeeklyPolicy.TryReadCompletion(flags, 104, row, out var complete));
            Assert.True(complete);
        }
        Assert.True(BeastmasterWeeklyPolicy.TryReadCompletion(flags, 104, 104, out var last));
        Assert.True(last);
        Assert.False(BeastmasterWeeklyPolicy.TryReadCompletion(flags, 104, 0, out _));
        Assert.False(BeastmasterWeeklyPolicy.TryReadCompletion(flags, 104, 105, out _));
        Assert.False(BeastmasterWeeklyPolicy.TryReadCompletion(flags, 105, 104, out _));
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(99, false)]
    public void CurrentNativeLoadedStateDoesNotRequireThePluginToWitnessLoading(int state, bool expected)
    {
        var freshness = new BeastmasterWeeklyFreshness();
        Assert.Equal(expected, freshness.Observe(17, true, state, 2000, 1000));
        Assert.Equal(expected, freshness.Fresh);
        if (expected)
        {
            Assert.Equal(17UL, freshness.Character);
            Assert.Equal(2000L, freshness.ResetTimestamp);
        }
    }

    [Theory]
    [InlineData(0, true, 2000, 1000)]
    [InlineData(17, false, 2000, 1000)]
    [InlineData(17, true, 0, 1000)]
    [InlineData(17, true, 1000, 1000)]
    [InlineData(17, true, 999, 1000)]
    [InlineData(17, true, 605801, 1000)]
    [InlineData(17, true, 1000, 0)]
    [InlineData(17, true, 1000, -1)]
    public void LoadedStateDoesNotOverrideAnUnavailableCharacterOrInvalidReset(
        ulong character, bool ready, long reset, long now)
    {
        var freshness = new BeastmasterWeeklyFreshness();
        Assert.True(freshness.Observe(17, true, 2, 2000, 1000));
        Assert.False(freshness.Observe(character, ready, 2, reset, now));
        Assert.False(freshness.Fresh);
    }

    [Fact]
    public void FreshnessFollowsTheCurrentNativeCharacterResetAndLoadState()
    {
        var freshness = new BeastmasterWeeklyFreshness();
        Assert.True(freshness.Observe(17, true, 2, 2000, 1000));
        Assert.True(freshness.Observe(23, true, 2, 3000, 1001));
        Assert.Equal(23UL, freshness.Character);
        Assert.Equal(3000L, freshness.ResetTimestamp);

        Assert.False(freshness.Observe(23, true, 1, 3000, 1002));
        Assert.False(freshness.Fresh);
        Assert.True(freshness.Observe(23, true, 2, 3000, 1003));
        Assert.False(freshness.Observe(23, true, 99, 3000, 1004));
        Assert.False(freshness.Fresh);
        Assert.True(freshness.Observe(23, true, 2, 3000, 1005));

        Assert.False(freshness.Observe(23, true, 2, 3000, 3000));
        Assert.True(freshness.Observe(23, true, 2, 4000, 3001));
        Assert.Equal(4000L, freshness.ResetTimestamp);
    }

    [Fact]
    public void UnregisteredSessionCannotRetainTheCurrentCharactersWeeklyObservation()
    {
        var freshness = new BeastmasterWeeklyFreshness();
        Assert.False(freshness.Observe(17, false, 1, 2000, 1000));
        Assert.Equal(0UL, freshness.Character);
        Assert.True(freshness.Observe(17, true, 2, 2000, 1001));
        Assert.False(freshness.Observe(17, false, 2, 2000, 1003));
        Assert.False(freshness.Fresh);
        Assert.Equal(0UL, freshness.Character);
        Assert.Equal(0L, freshness.ResetTimestamp);
        Assert.True(freshness.Observe(17, true, 2, 2000, 1004));
    }

    [Fact]
    public void StoppedEntryRetainsUnknownOutcomeAndDoesNotImmediatelyRepeatNativeDispatch()
    {
        var attempt = new BeastmasterEntryAttempt(7, 2);
        Assert.False(attempt.TryCommence(exactQueue: false));
        attempt.RequestStop();
        Assert.True(attempt.Pending);
        Assert.False(attempt.TryCommence(exactQueue: true));
        Assert.False(attempt.TryConfirmSmallTeam(matchingPrompt: true));
        Assert.False(attempt.TryCancel(exactQueue: false));
        Assert.True(attempt.TryCancel(exactQueue: true));
        Assert.False(attempt.TryCancel(exactQueue: true));
        attempt.ObserveWithdrawal(exactQueue: false, sameCharacter: true);
        attempt.ObserveWithdrawal(exactQueue: true, sameCharacter: false);
        attempt.ObserveSession(loggedIn: true, characterLoaded: false, observedCharacter: 0);
        attempt.ObserveEntry(1339);
        Assert.True(attempt.Pending);
        attempt.ObserveWithdrawal(exactQueue: true, sameCharacter: true);
        Assert.Equal(BeastmasterEntryOutcome.Withdrawn, attempt.Outcome);

        var entering = new BeastmasterEntryAttempt(7, 3);
        Assert.True(entering.TryCommence(exactQueue: true));
        Assert.False(entering.TryCommence(exactQueue: true));
        entering.RequestStop();
        Assert.True(entering.TryCancel(exactQueue: true));
        Assert.False(entering.TryCancel(exactQueue: true));
        entering.ObserveWithdrawal(exactQueue: true, sameCharacter: true);
        Assert.Equal(BeastmasterEntryOutcome.Withdrawn, entering.Outcome);

        var declined = new BeastmasterEntryAttempt(7, 3);
        Assert.False(declined.TryConfirmSmallTeam(matchingPrompt: false));
        Assert.False(declined.TryDeclinePrompt(matchingPrompt: true));
        declined.RequestStop();
        declined.ObserveDeclinedPrompt(promptVisible: false, queueIdle: true);
        Assert.True(declined.Pending);
        Assert.False(declined.TryDeclinePrompt(matchingPrompt: false));
        Assert.True(declined.TryDeclinePrompt(matchingPrompt: true));
        Assert.False(declined.TryDeclinePrompt(matchingPrompt: true));
        declined.ObserveDeclinedPrompt(promptVisible: true, queueIdle: true);
        declined.ObserveDeclinedPrompt(promptVisible: false, queueIdle: false);
        Assert.True(declined.Pending);
        declined.ObserveDeclinedPrompt(promptVisible: false, queueIdle: true);
        Assert.Equal(BeastmasterEntryOutcome.Withdrawn, declined.Outcome);

        var accepted = new BeastmasterEntryAttempt(7, 3);
        Assert.True(accepted.TryConfirmSmallTeam(matchingPrompt: true));
        Assert.False(accepted.TryConfirmSmallTeam(matchingPrompt: true));
        accepted.RequestStop();
        Assert.False(accepted.TryDeclinePrompt(matchingPrompt: true));
        accepted.ObserveEntry(1341);
        Assert.Equal(BeastmasterEntryOutcome.Entered, accepted.Outcome);

        var switched = new BeastmasterEntryAttempt(7, 1);
        switched.RequestStop();
        switched.ObserveSession(loggedIn: true, characterLoaded: true, observedCharacter: 8);
        Assert.Equal(BeastmasterEntryOutcome.SessionEnded, switched.Outcome);
    }

    [Fact]
    public void DelayedAndChainedBoardPromptsAreAcceptedOncePerVisibleGeneration()
    {
        var attempt = new BeastmasterEntryAttempt(7, 1);
        attempt.ObservePrompt(true);
        Assert.False(attempt.TryConfirmBoardPrompt(string.Empty));
        Assert.True(attempt.TryConfirmBoardPrompt("Small team\nProceed?"));
        Assert.False(attempt.TryConfirmBoardPrompt("Small team\nProceed?"));
        Assert.True(attempt.TryConfirmBoardPrompt("Lower rank\nProceed?"));
        Assert.False(attempt.TryConfirmBoardPrompt("Lower rank\nProceed?"));
        attempt.ObservePrompt(false);
        attempt.ObservePrompt(true);
        Assert.True(attempt.TryConfirmBoardPrompt("Lower rank\nProceed?"));
        attempt.RequestStop();
        Assert.False(attempt.TryConfirmBoardPrompt("Another warning\nProceed?"));
        Assert.True(attempt.TryDeclineBoardPrompt("Another warning\nProceed?"));
        Assert.False(attempt.TryDeclineBoardPrompt("Another warning\nProceed?"));
    }

    [Fact]
    public void ExistingBoardWarningIsRecognizedAfterReloadAndRetriedUntilStopped()
    {
        const string english = "Proceed to challenge this board?";
        const string localizedQuestion = "\u3053\u306e\u76e4\u9762\u306b\u6311\u6226\u3057\u307e\u3059\u304b\uff1f";
        const string localizedRow = "Conditional warning\u2028" + localizedQuestion + "\u2029  ";
        var accepted = new (string Actual, string Localized)[]
        {
            (english, english),
            ("Your team contains fewer familiars.\n" + english, english),
            ("Lower ranks detected.\r\nPROCEED TO CHALLENGE THIS BOARD!", english),
            ("\u201cPROCEED\u201d\u00a0TO\u202fCHALLENGE\r\nTHIS\u3000BOARD\uff1f", english),
            ("Different warning.\n" + localizedQuestion, localizedRow),
            ("\u3053\u306e\u76e4\u9762\u306b\r\n\u6311\u6226\u3057\u307e\u3059\u304b?", localizedRow),
            ("Already-open warning.\n" + english, string.Empty),
        };
        foreach (var prompt in accepted)
            Assert.True(BeastmasterEntryPrompt.Matches(prompt.Actual, prompt.Localized));

        foreach (var unrelated in new[]
        {
            string.Empty, "Remove all familiars from your team?", "Accept this trade?",
            "Proceed?", "Challenge this board?", "Proceed to challenge?",
            "Proceed to change this board?",
        })
        {
            Assert.False(BeastmasterEntryPrompt.Matches(unrelated, english));
            Assert.False(BeastmasterEntryPrompt.Matches(unrelated, localizedRow));
            Assert.False(BeastmasterEntryPrompt.Matches(unrelated, string.Empty));
        }
        Assert.False(BeastmasterEntryPrompt.Matches("Remove all familiars?", "\r\n ? \r\n"));

        var now = new DateTime(2026, 10, 18, 21, 0, 0, DateTimeKind.Utc);
        var attempt = new BeastmasterEntryAttempt(7, 1);
        var alreadyOpen = accepted[3].Actual;
        bool Confirm(DateTime observedAt) => BeastmasterEntryPrompt.Matches(alreadyOpen, string.Empty) &&
            attempt.TryConfirmBoardPrompt(alreadyOpen, observedAt);

        // A fresh attempt can adopt a visible prompt without receiving its original setup event.
        Assert.True(Confirm(now));
        Assert.False(Confirm(now.AddSeconds(5).AddTicks(-1)));
        Assert.True(Confirm(now.AddSeconds(5)));
        attempt.RequestStop();
        Assert.False(Confirm(now.AddSeconds(10)));
        Assert.True(attempt.TryDeclineBoardPrompt(alreadyOpen, now.AddSeconds(10)));
    }

    [Fact]
    public void UserNegativeActionAndSettledNativeStateReleaseEntryButIdleAloneDoesNot()
    {
        var attempt = new BeastmasterEntryAttempt(7, 1);
        attempt.ObservePrompt(true);
        attempt.ObservePrompt(false);
        attempt.RequestStop();
        attempt.ObserveDeclinedPrompt(promptVisible: false, queueIdle: true);
        Assert.True(attempt.Pending);
        attempt.ObserveUserDecline(matchingPrompt: false, negativeButton: true);
        attempt.ObserveUserDecline(matchingPrompt: true, negativeButton: false);
        attempt.ObserveDeclinedPrompt(promptVisible: false, queueIdle: true);
        Assert.True(attempt.Pending);
        attempt.ObserveUserDecline(matchingPrompt: true, negativeButton: true);
        attempt.ObserveDeclinedPrompt(promptVisible: true, queueIdle: true);
        attempt.ObserveDeclinedPrompt(promptVisible: false, queueIdle: false);
        Assert.True(attempt.Pending);
        attempt.ObserveDeclinedPrompt(promptVisible: false, queueIdle: true);
        Assert.Equal(BeastmasterEntryOutcome.Withdrawn, attempt.Outcome);

        var commencing = new BeastmasterEntryAttempt(7, 2);
        Assert.True(commencing.TryCommence(exactQueue: true));
        commencing.ObserveUserDecline(matchingPrompt: true, negativeButton: true);
        commencing.ObserveDeclinedPrompt(promptVisible: false, queueIdle: true);
        Assert.True(commencing.Pending);
    }

    [Fact]
    public void ObservedUserNoBlocksPositiveDispatchUntilNativeSettlement()
    {
        var ignored = new BeastmasterEntryAttempt(7, 1);
        ignored.ObserveUserDecline(matchingPrompt: false, negativeButton: true);
        ignored.ObserveUserDecline(matchingPrompt: true, negativeButton: false);
        Assert.False(ignored.UserDeclineObserved);
        Assert.True(ignored.TryConfirmSmallTeam(matchingPrompt: true));
        Assert.True(ignored.TryConfirmBoardPrompt("Warning\nProceed?"));
        Assert.True(ignored.TryCommence(exactQueue: true));

        var declined = new BeastmasterEntryAttempt(7, 1);
        declined.ObservePrompt(true);
        declined.ObserveUserDecline(matchingPrompt: true, negativeButton: true);
        Assert.True(declined.UserDeclineObserved);
        Assert.True(declined.Pending);
        Assert.False(declined.StopRequested);
        Assert.False(declined.TryConfirmBoardPrompt("Warning\nProceed?"));
        Assert.False(declined.TryConfirmSmallTeam(matchingPrompt: true));
        Assert.False(declined.TryCommence(exactQueue: true));
        declined.ObservePrompt(false);
        declined.ObservePrompt(true);
        Assert.False(declined.TryConfirmBoardPrompt("Another warning\nProceed?"));
        Assert.False(declined.SmallTeamSubmitted);
        Assert.False(declined.CommenceSubmitted);
        declined.ObserveDeclinedPrompt(promptVisible: true, queueIdle: true);
        declined.ObserveDeclinedPrompt(promptVisible: false, queueIdle: false);
        Assert.True(declined.Pending);
        declined.ObserveDeclinedPrompt(promptVisible: false, queueIdle: true);
        Assert.Equal(BeastmasterEntryOutcome.Withdrawn, declined.Outcome);
    }

    [Fact]
    public void MatchingCommenceAndWarningAreRecheckedAtFiveSecondsWithoutReplayingEveryFrame()
    {
        var now = new DateTime(2026, 10, 18, 20, 0, 0, DateTimeKind.Utc);
        var attempt = new BeastmasterEntryAttempt(7, 2);
        Assert.True(attempt.TryConfirmBoardPrompt("Proceed?", now));
        Assert.False(attempt.TryConfirmBoardPrompt("Proceed?", now.AddSeconds(5).AddTicks(-1)));
        Assert.True(attempt.TryConfirmBoardPrompt("Proceed?", now.AddSeconds(5)));
        Assert.True(attempt.TryCommence(true, now));
        Assert.False(attempt.TryCommence(true, now.AddSeconds(5).AddTicks(-1)));
        Assert.False(attempt.TryCommence(false, now.AddSeconds(5)));
        Assert.True(attempt.TryCommence(true, now.AddSeconds(5)));
        attempt.RequestStop();
        Assert.False(attempt.TryCommence(true, now.AddSeconds(10)));
        Assert.False(attempt.TryConfirmBoardPrompt("Proceed?", now.AddSeconds(10)));
        Assert.True(attempt.TryCancel(true, now.AddSeconds(10)));
        Assert.False(attempt.TryCancel(true, now.AddSeconds(15).AddTicks(-1)));
        Assert.True(attempt.TryCancel(true, now.AddSeconds(15)));
    }

    [Fact]
    public void ExplicitStartCanResumeStoppedEntryAndClearOldNegativeDispatchFlags()
    {
        var now = new DateTime(2026, 10, 18, 20, 0, 0, DateTimeKind.Utc);
        var attempt = new BeastmasterEntryAttempt(7, 1);
        attempt.TryCommence(true, now);
        attempt.RequestStop();
        attempt.TryCancel(true, now);
        Assert.True(attempt.TryDeclineBoardPrompt("Proceed?", now));
        Assert.False(attempt.TryDeclineBoardPrompt("Proceed?", now.AddSeconds(5).AddTicks(-1)));
        Assert.True(attempt.TryDeclineBoardPrompt("Proceed?", now.AddSeconds(5)));
        attempt.Resume();
        Assert.False(attempt.StopRequested);
        Assert.False(attempt.CancelSubmitted);
        Assert.False(attempt.PromptDeclineSubmitted);
        Assert.True(attempt.TryConfirmBoardPrompt("Proceed?", now.AddSeconds(6)));
        Assert.True(attempt.TryCommence(true, now.AddSeconds(6)));
    }

    [Fact]
    public void FiveSecondsOfObservedIdleReleaseStaleEntryAndNewActivityResetsThatClock()
    {
        var now = new DateTime(2026, 10, 18, 20, 0, 0, DateTimeKind.Utc);
        var attempt = new BeastmasterEntryAttempt(7, 1);
        attempt.RequestStop();
        attempt.ObserveIdle(true, now);
        attempt.ObserveIdle(true, now.AddSeconds(5).AddTicks(-1));
        Assert.True(attempt.Pending);
        attempt.ObserveIdle(false, now.AddSeconds(5));
        attempt.ObserveIdle(true, now.AddSeconds(6));
        attempt.ObserveIdle(true, now.AddSeconds(11).AddTicks(-1));
        Assert.True(attempt.Pending);
        attempt.ObserveIdle(true, now.AddSeconds(11));
        Assert.Equal(BeastmasterEntryOutcome.Withdrawn, attempt.Outcome);
    }

    [Fact]
    public void ManualCompletionAndChangingEligibilitySkipDoneBoardsWithoutRepeatingAttempts()
    {
        BeastmasterWeeklyObjective Row(uint board, BeastmasterWeeklyState state) => new(board, 88 + board, "", "", state, "", 0, 0);
        BeastmasterWeeklyObjective[] rows = [Row(1, BeastmasterWeeklyState.Complete), Row(2, BeastmasterWeeklyState.Unfinished),
            Row(3, BeastmasterWeeklyState.Locked), Row(4, BeastmasterWeeklyState.GearBlocked), Row(5, BeastmasterWeeklyState.Unavailable)];
        var attempted = new HashSet<uint>();
        Assert.Equal(2u, BeastmasterWeeklyPolicy.Next(rows, attempted)!.Board);
        attempted.Add(2);
        Assert.Null(BeastmasterWeeklyPolicy.Next(rows, attempted));
        rows[1] = rows[1] with { State = BeastmasterWeeklyState.Complete };
        rows[2] = rows[2] with { State = BeastmasterWeeklyState.Unfinished };
        Assert.Equal(3u, BeastmasterWeeklyPolicy.Next(rows, attempted)!.Board);
        attempted.Add(3);
        Assert.Null(BeastmasterWeeklyPolicy.Next(rows, attempted));
    }

    [Fact]
    public void AutomaticWeeklySelectionUsesUnlocksIndependentlyOfTemporaryRestrictions()
    {
        BeastmasterWeeklyObjective Row(uint board, BeastmasterWeeklyState state, bool unlocked) =>
            new(board, 88 + board, "", "", state, "", 0, 0, unlocked);
        BeastmasterWeeklyObjective[] rows =
        [
            Row(1, BeastmasterWeeklyState.Unfinished, true),
            Row(2, BeastmasterWeeklyState.GearBlocked, true),
            Row(3, BeastmasterWeeklyState.Locked, true), // A temporary job or party restriction.
            Row(4, BeastmasterWeeklyState.Locked, false),
            Row(5, BeastmasterWeeklyState.Unavailable, false),
        ];

        Assert.Equal(new uint[] { 1, 2, 3 }, BeastmasterWeeklyPolicy.SelectedObjectives(new(), rows).Select(row => row.Board));
    }

    [Fact]
    public void ExplicitSelectionSurvivesNewUnlocksAndSkipsUnselectedUnavailableBoards()
    {
        var settings = new BeastmasterSettings { WeeklyBoards = [1, 3] };
        BeastmasterWeeklyObjective Row(uint board, BeastmasterWeeklyState state, bool unlocked = true) =>
            new(board, 88 + board, "", "", state, "", 0, 0, unlocked);
        BeastmasterWeeklyObjective[] rows =
        [
            Row(1, BeastmasterWeeklyState.Complete), Row(2, BeastmasterWeeklyState.Unavailable),
            Row(3, BeastmasterWeeklyState.Unfinished), Row(4, BeastmasterWeeklyState.Locked, false),
        ];
        var selected = BeastmasterWeeklyPolicy.SelectedObjectives(settings, rows);
        Assert.Equal(new uint[] { 1, 3 }, selected.Select(row => row.Board));
        Assert.Equal(3u, BeastmasterWeeklyPolicy.Next(selected, new HashSet<uint>())!.Board);
        Assert.Null(BeastmasterWeeklyPolicy.Next(selected, new HashSet<uint> { 3 }));

        rows[1] = rows[1] with { State = BeastmasterWeeklyState.Unfinished };
        rows[3] = rows[3] with { State = BeastmasterWeeklyState.Unfinished, Unlocked = true };
        Assert.Equal(new uint[] { 1, 3 }, BeastmasterWeeklyPolicy.SelectedObjectives(settings, rows).Select(row => row.Board));
        Assert.True(settings.WeeklyBoards.SetEquals([1u, 3u]));
    }

    [Fact]
    public void EmptyOrInvalidExplicitSelectionNeverFallsBackToAutomaticBoards()
    {
        BeastmasterWeeklyObjective[] rows = [new(1, 89, "", "", BeastmasterWeeklyState.Unfinished, "", 0, 0),
            new(0, 0, "", "", BeastmasterWeeklyState.Unfinished, "", 0, 0),
            new(6, 94, "", "", BeastmasterWeeklyState.Unfinished, "", 0, 0)];
        var settings = new BeastmasterSettings { WeeklyBoards = [] };
        Assert.Empty(BeastmasterWeeklyPolicy.SelectedObjectives(settings, rows));
        settings.WeeklyBoards = [0, 6];
        Assert.Empty(BeastmasterWeeklyPolicy.SelectedObjectives(settings, rows));
        Assert.Null(BeastmasterWeeklyPolicy.Next(BeastmasterWeeklyPolicy.SelectedObjectives(settings, rows), new HashSet<uint>()));
    }

    [Fact]
    public void FrozenAutomaticSelectionKeepsTheActiveRunStableWithoutChangingSavedDefaults()
    {
        var saved = new BeastmasterSettings();
        BeastmasterWeeklyObjective[] rows = [new(1, 89, "", "", BeastmasterWeeklyState.Unfinished, "", 0, 0),
            new(2, 90, "", "", BeastmasterWeeklyState.GearBlocked, "", 0, 0),
            new(3, 91, "", "", BeastmasterWeeklyState.Locked, "", 0, 0, Unlocked: false)];
        var active = BeastmasterWeeklyPolicy.CopySettings(saved);
        active.WeeklyBoards = BeastmasterWeeklyPolicy.SelectedObjectives(active, rows).Select(row => row.Board).ToHashSet();

        rows[2] = rows[2] with { State = BeastmasterWeeklyState.Unfinished, Unlocked = true };
        Assert.Equal(new uint[] { 1, 2 }, BeastmasterWeeklyPolicy.SelectedObjectives(active, rows).Select(row => row.Board));
        Assert.Equal(new uint[] { 1, 2, 3 }, BeastmasterWeeklyPolicy.SelectedObjectives(saved, rows).Select(row => row.Board));
        Assert.Null(saved.WeeklyBoards);
    }

    [Fact]
    public void ResultAndDispatchNeverReplaceFreshSameCharacterSameResetCredit()
    {
        Assert.False(BeastmasterWeeklyPolicy.ConfirmsCredit(true, 1, 1, 2000, 2000, false, false, true, true));
        Assert.False(BeastmasterWeeklyPolicy.ConfirmsCredit(true, 1, 1, 2000, 2000, false, true, false, true));
        Assert.False(BeastmasterWeeklyPolicy.ConfirmsCredit(true, 1, 1, 2000, 2000, false, true, true, false));
        Assert.False(BeastmasterWeeklyPolicy.ConfirmsCredit(false, 1, 1, 2000, 2000, false, true, true, true));
        Assert.False(BeastmasterWeeklyPolicy.ConfirmsCredit(true, 2, 1, 2000, 2000, false, true, true, true));
        Assert.False(BeastmasterWeeklyPolicy.ConfirmsCredit(true, 1, 1, 3000, 2000, false, true, true, true));
        Assert.False(BeastmasterWeeklyPolicy.ConfirmsCredit(true, 1, 1, 2000, 2000, true, true, true, true));
        Assert.True(BeastmasterWeeklyPolicy.ConfirmsCredit(true, 1, 1, 2000, 2000, false, true, true, true));
    }

    [Fact]
    public void WeeklyOverridesNeverOverwriteOrdinarySettingsOrMutableLists()
    {
        var saved = new BeastmasterSettings
        {
            Board = 5, RunCount = 12, CompletionCommand = "/vmx off", WeeklyBoards = [1, 4], TeamMode = CrucibleTeamMode.Custom,
            CustomTeam = [13, 20, 27], ShopGearOrder = [140],
            ShopLists = [new BeastmasterShopList { Name = "Supplies", Order = [new BeastmasterShopList.Entry { ID = 140, Blocked = true }] }],
            BeastTierLists = [BeastTierList.Default()],
        };
        var transient = BeastmasterWeeklyPolicy.CopySettings(saved);
        Assert.Equal(1, transient.RunCount);
        Assert.Empty(transient.CompletionCommand);
        Assert.True(transient.WeeklyBoards!.SetEquals([1u, 4u]));
        transient.WeeklyBoards.Clear();
        transient.Board = 1; transient.CustomTeam.Clear(); transient.ShopGearOrder.Clear();
        transient.ShopLists[0].Order[0].Blocked = false;
        transient.BeastTierLists[0][0].PickBlock = true;
        Assert.Equal(5u, saved.Board); Assert.Equal(12, saved.RunCount);
        Assert.Equal("/vmx off", saved.CompletionCommand);
        Assert.True(saved.WeeklyBoards!.SetEquals([1u, 4u]));
        Assert.Equal(new uint[] { 13, 20, 27 }, saved.CustomTeam);
        Assert.Single(saved.ShopGearOrder);
        Assert.True(saved.ShopLists[0].Order[0].Blocked);
        Assert.False(saved.BeastTierLists[0][0].PickBlock);
    }
}
