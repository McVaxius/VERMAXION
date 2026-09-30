using System.Linq;
using Newtonsoft.Json;
using VERMAXION.Models;
using VERMAXION.Services;
using Xunit;

namespace VERMAXION.Tests;

public sealed class VerminionLifecycleTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(24)]
    public void SelectedMissionRepeatsRequireFreshMatchingVictoriesAndResumeTheirOwnCount(int mission)
    {
        var week = new DateTime(2026, 9, 29, 9, 0, 0, DateTimeKind.Utc);
        var legacy = JsonConvert.DeserializeObject<CharacterConfig>("{\"VerminionMode\":1,\"VerminionVictoryTarget\":99}")!;
        Assert.Equal(VerminionMode.MissionRepeat, legacy.VerminionMode); // Preserve the saved mode value.
        Assert.Equal(2, legacy.VerminionMission);
        Assert.Equal(5, legacy.VerminionRepeatCount); // The former weekly target is not a mission number.
        Assert.Equal(VerminionMode.Participation, new CharacterConfig().VerminionMode);

        var character = new CharacterConfig
        {
            VerminionMode = VerminionMode.MissionRepeat, VerminionMission = mission, VerminionRepeatCount = 2,
        };
        var progress = character.VerminionProgress;
        progress.ObserveParticipation(week, 5);
        progress.WeeklyWins = 99;
        progress.ClearedChallengeMask = (1u << 24) - 1; // Permanent progress cannot satisfy replays.
        progress.EnsureRun(character.VerminionMode, character.VerminionRepeatCount, mission: mission);
        Assert.Equal(2, progress.Remaining(character.VerminionMode, 2, mission));
        Assert.Equal(6, progress.RunAttemptLimit);
        Assert.False(progress.WeeklyGoalReached(character.VerminionMode, 2, week, mission: mission));

        var duty = (uint)(551 + mission);
        var wrong = progress.BeginMatch(duty == 553 ? 554u : 553u);
        Assert.True(progress.RecordResult(wrong, VerminionBattleOutcome.Victory, week));
        Assert.Equal(0, progress.RunWins); // Even a positively verified win elsewhere cannot count.
        var cancelled = progress.BeginMatch(duty);
        progress.AbandonMatch(); // FULL STOP / timeout abandons attribution.
        Assert.False(mission == 1 ? progress.RecordTutorialCompletion(cancelled, VerminionBattleOutcome.Victory)
            : progress.RecordResult(cancelled, VerminionBattleOutcome.Victory, week));

        var match = progress.BeginMatch(duty);
        character = JsonConvert.DeserializeObject<CharacterConfig>(JsonConvert.SerializeObject(character))!;
        progress = character.VerminionProgress;
        progress.EnsureRun(character.VerminionMode, 2, mission: mission); // Reload does not restart the run.
        Assert.True(progress.PendingMissionRepeat);
        Assert.False(mission == 1 ? progress.RecordTutorialCompletion(match, VerminionBattleOutcome.Unknown)
            : progress.RecordResult(match, VerminionBattleOutcome.Unknown, week));
        Assert.Equal(0, progress.RunWins);
        var weeklyBefore = progress.WeeklyMatches;
        var winsBefore = progress.WeeklyWins;
        Assert.True(mission == 1 ? progress.RecordTutorialCompletion(match, VerminionBattleOutcome.Victory)
            : progress.RecordResult(match, VerminionBattleOutcome.Victory, week));
        Assert.False(mission == 1 ? progress.RecordTutorialCompletion(match, VerminionBattleOutcome.Victory)
            : progress.RecordResult(match, VerminionBattleOutcome.Victory, week));
        Assert.Equal(1, progress.RunWins);
        Assert.Equal(weeklyBefore + (mission == 1 ? 0 : 1), progress.WeeklyMatches);
        Assert.Equal(winsBefore + (mission == 1 ? 0 : 1), progress.WeeklyWins);

        character.VerminionPaused = true;
        character = JsonConvert.DeserializeObject<CharacterConfig>(JsonConvert.SerializeObject(character))!;
        progress = character.VerminionProgress;
        Assert.True(character.VerminionPaused);
        progress.RestartAttemptBudget(); // Explicit Resume renews attempts, retaining successful clears.
        Assert.Equal(1, progress.RunWins);
        Assert.Equal(3, progress.RunAttemptLimit);
        progress.ObserveWeek(week.AddDays(7));
        progress.EnsureRun(character.VerminionMode, 2, mission: mission);
        Assert.Equal(1, progress.RunWins); // Weekly reset only clears weekly counters.

        match = progress.BeginMatch(duty);
        Assert.True(mission == 1 ? progress.RecordTutorialCompletion(match, VerminionBattleOutcome.Victory)
            : progress.RecordResult(match, VerminionBattleOutcome.Victory, week.AddDays(7)));
        foreach (var restored in new[]
        {
            JsonConvert.DeserializeObject<CharacterConfig>(JsonConvert.SerializeObject(character))!,
            System.Text.Json.JsonSerializer.Deserialize<CharacterConfig>(System.Text.Json.JsonSerializer.Serialize(character))!,
        })
        {
            restored.VerminionProgress.EnsureRun(restored.VerminionMode, 2, mission: mission);
            Assert.Equal(2, restored.VerminionProgress.RunWins);
            Assert.True(restored.VerminionProgress.WeeklyGoalReached(restored.VerminionMode, 2, week.AddDays(14), mission: mission));
        }
        var other = new CharacterConfig();
        other.CopyVerminionSettingsFrom(character);
        Assert.Equal(mission, other.VerminionMission);
        Assert.Equal(2, other.VerminionRepeatCount);
        Assert.Equal(0, other.VerminionProgress.RunWins);
        Assert.Equal(mission, character.Clone().VerminionMission);
        Assert.Equal(2, character.Clone().VerminionProgress.RunWins);

        progress.EnsureRun(character.VerminionMode, 2, restart: true, mission: mission); // New manual Run.
        Assert.Equal(0, progress.RunWins);
        Assert.Equal(2, progress.Remaining(character.VerminionMode, 2, mission));
        progress.CampaignRequested = true;
        match = progress.BeginMatch(duty);
        Assert.True(mission == 1 ? progress.RecordTutorialCompletion(match, VerminionBattleOutcome.Victory)
            : progress.RecordResult(match, VerminionBattleOutcome.Victory, week.AddDays(7)));
        Assert.Equal(0, progress.RunWins); // Campaign results never satisfy the manual replay request.
        progress.CampaignRequested = false;
        progress.RunIsCampaign = true;
        progress = JsonConvert.DeserializeObject<VerminionProgress>(JsonConvert.SerializeObject(progress))!;
        Assert.True(progress.RunIsCampaign); // Reload remembers the finished campaign; weekly scheduling remains independent.
        Assert.Equal(progress.WeeklyMatches >= 5, progress.WeeklyGoalReached(VerminionMode.Participation, 2, week.AddDays(7)));
        match = progress.BeginMatch(duty);
        var nextMission = mission == 24 ? 2 : mission + 1;
        progress.EnsureRun(character.VerminionMode, 2, mission: nextMission);
        Assert.True(mission == 1 ? progress.RecordTutorialCompletion(match, VerminionBattleOutcome.Victory)
            : progress.RecordResult(match, VerminionBattleOutcome.Victory, week.AddDays(7)));
        Assert.Equal(0, progress.RunWins); // Changing the goal cannot adopt an old pending admission.
    }

    [Fact]
    public void TournamentPrizeTextSeparatesRewardIdentityTotalAndEntryConfirmation()
    {
        const string title = "The 820th Lord of Verminion Tournament";
        const string rankings = title + " has come to a close! Let's have a look at the final rankings, shall we?";
        const string placed = "Congratulations, TEST! You've finished 3rd on the board! Along with an added bonus of 10,000 points, your total winnings come to 25,500 MGP!";
        const string participation = "...Ah, I'm afraid you didn't quite earn a winning rank this time around. You are, however, eligible to receive a participation gift of 3,000 MGP.";
        Assert.True(VerminionTournamentRewardRules.TryReadTitle(rankings, out var actual));
        Assert.Equal(title, actual);
        Assert.Equal(VerminionTournamentDialogue.PrizeRankings, VerminionTournamentEntryRules.Dialogue(rankings));
        Assert.False(VerminionTournamentRewardRules.TryReadTitle(title, out _));
        Assert.False(VerminionTournamentRewardRules.IsTournamentTitle("The 820th Triple Triad Tournament"));
        Assert.False(VerminionTournamentRewardRules.IsTournamentTitle(title + "\n"));
        Assert.True(VerminionTournamentRewardRules.TryReadOffer(placed, out var prize));
        Assert.Equal(25500u, prize); // Total winnings, not the placement bonus.
        Assert.True(VerminionTournamentRewardRules.TryReadOffer(participation, out prize));
        Assert.Equal(3000u, prize);
        Assert.Equal(VerminionTournamentDialogue.PrizeOffer, VerminionTournamentEntryRules.Dialogue(participation));
        foreach (var invalid in new[] { "0", "-1", "3,00", "10,000,000", "4294967296", "unknown", "3.000" })
        {
            Assert.False(VerminionTournamentRewardRules.TryReadOffer(participation.Replace("3,000", invalid), out _));
            Assert.Equal(VerminionTournamentDialogue.Unknown, VerminionTournamentEntryRules.Dialogue(participation.Replace("3,000", invalid)));
        }
        Assert.False(VerminionTournamentRewardRules.TryReadOffer("You received 3,000 MGP.", out _));
        Assert.False(VerminionTournamentEntryRules.CanConfirm(VerminionTournamentRewardRules.Confirmation, true, false));
        Assert.Equal("Accept your prize?", VerminionTournamentRewardRules.Confirmation);
        Assert.Equal(VerminionTournamentDialogue.PrizeThanks, VerminionTournamentEntryRules.Dialogue(
            "Thank you for entering, TEST, and I hope to see you return for our next Lord of Verminion tournament!"));
        Assert.Equal(VerminionTournamentDialogue.PrizeNone, VerminionTournamentEntryRules.Dialogue(
            "Unfortunately, you didn't qualify for any prizes. But that just gives you a goal to aim for, right, TEST?"));
        Assert.Equal(VerminionTournamentDialogue.PrizeExpired, VerminionTournamentEntryRules.Dialogue(
            "Oh dear... The reward period for the tournament in question seems to have expired. Please remember to collect any prizes before the beginning of the next scheduled tournament!"));
        Assert.Equal(VerminionTournamentDialogue.PrizeRejected, VerminionTournamentEntryRules.Dialogue(
            "You cannot accept your prize at this time. The gaming gods decree that it is time you took a break."));
    }

    [Fact]
    public void TournamentPrizeIntentSurvivesReloadStopAndResetWithoutInventingRewardOrMatchCredit()
    {
        const string title = "The 820th Lord of Verminion Tournament";
        var week = new DateTime(2026, 9, 22, 9, 0, 0, DateTimeKind.Utc);
        var requested = week.AddDays(6);
        var character = new CharacterConfig { VerminionMode = VerminionMode.CpuRewards };
        var progress = character.VerminionProgress;
        progress.ObserveParticipation(week, 5);
        progress.WeeklyWins = 2;
        progress.RecordChallengeClear(2);
        progress.GilSpent = 2400;
        progress.MgpSpent = 10000;
        progress.CertificatesSpent = 4;
        progress.LastTournamentInfo = new("The 821st Lord of Verminion Tournament", "Matches begin at tomorrow", null, null, null);
        Assert.True(progress.BeginTournamentReward(title, 3000, 50000, requested));
        character.VerminionPaused = true; // FULL STOP preserves the unresolved intent.
        progress.AbandonMatch();
        character.ResetVerminionState();
        foreach (var restored in new[]
        {
            JsonConvert.DeserializeObject<CharacterConfig>(JsonConvert.SerializeObject(character))!,
            System.Text.Json.JsonSerializer.Deserialize<CharacterConfig>(System.Text.Json.JsonSerializer.Serialize(character))!,
        })
        {
            var saved = restored.VerminionProgress;
            Assert.True(restored.VerminionPaused);
            Assert.Equal(progress.PendingTournamentReward, saved.PendingTournamentReward);
            Assert.False(saved.ConfirmTournamentReward(53000, requested.AddMinutes(1))); // Balance alone is insufficient.
            Assert.False(saved.BeginTournamentReward(title, 3000, 53000, requested));
            Assert.Equal(0, saved.BeginMatch(579));
            Assert.False(saved.ReservePurchase(7561, 76, 0, 0, 50000, 50000, 0, false, 2400, 10000,
                certificates: 2, certificatesBefore: 6, certificateCap: 6));
            Assert.False(saved.AcknowledgeTournamentReward(progress.LastTournamentInfo.Title));
            Assert.Null(saved.LastTournamentReward);
            Assert.True(saved.AcknowledgeTournamentReward(title));
            saved = JsonConvert.DeserializeObject<VerminionProgress>(JsonConvert.SerializeObject(saved))!;
            Assert.False(saved.ConfirmTournamentReward(50000, requested.AddMinutes(1))); // Acknowledgement alone is insufficient.
            Assert.False(saved.ConfirmTournamentReward(53001, requested.AddMinutes(1)));
            Assert.False(saved.ConfirmTournamentReward(53000, requested.AddSeconds(-1)));
            Assert.False(saved.RejectTournamentReward(50000)); // An acknowledged claim cannot be cleared as rejected.
            Assert.True(saved.ConfirmTournamentReward(53000, requested.AddMinutes(1)));
            Assert.Null(saved.PendingTournamentReward);
            Assert.Equal(title, saved.LastTournamentReward!.Title);
            Assert.Equal(requested.AddMinutes(1), saved.LastTournamentReward.ConfirmedUtc);
            Assert.False(saved.ConfirmTournamentReward(53000, requested.AddMinutes(2)));
            Assert.False(saved.BeginTournamentReward(title, 3000, 53000, requested.AddMinutes(2)));
            Assert.Equal(5, saved.WeeklyMatches);
            Assert.Equal(2, saved.WeeklyWins);
            Assert.Equal(2u, saved.ClearedChallengeMask);
            Assert.Equal(2400ul, saved.GilSpent);
            Assert.Equal(10000ul, saved.MgpSpent);
            Assert.Equal(4ul, saved.CertificatesSpent);
            Assert.Equal(0, saved.MatchSequence);
            Assert.Equal(progress.LastTournamentInfo, saved.LastTournamentInfo); // The next period's display is separate.
            Assert.False(saved.WeeklyGoalReached(VerminionMode.CpuRewards, 5, week));
            saved.ObserveWeek(week.AddDays(7));
            Assert.NotNull(saved.LastTournamentReward);
            Assert.False(saved.BeginTournamentReward(title, 3000, 53000, week.AddDays(7)));
        }
        var clone = character.Clone();
        Assert.True(clone.VerminionProgress.AcknowledgeTournamentReward(title));
        Assert.False(progress.PendingTournamentReward!.Acknowledged);
        progress.ObserveWeek(week.AddDays(7));
        Assert.NotNull(progress.PendingTournamentReward);
        var other = new CharacterConfig();
        other.CopyVerminionSettingsFrom(character);
        Assert.Null(other.VerminionProgress.PendingTournamentReward);
        Assert.Null(other.VerminionProgress.LastTournamentReward);
    }

    [Fact]
    public void TournamentPrizeRequiresCapacityAndExplicitUnchangedBalanceRejection()
    {
        const string title = "The 820th Lord of Verminion Tournament";
        var now = new DateTime(2026, 9, 28, 20, 0, 0, DateTimeKind.Utc);
        var progress = new VerminionProgress();
        Assert.False(progress.BeginTournamentReward(title, 1, 0, default));
        Assert.False(progress.BeginTournamentReward("unidentified", 1, 0, now));
        Assert.False(progress.BeginTournamentReward(title, 0, 0, now));
        Assert.False(progress.BeginTournamentReward(title, 3000, 9_997_000, now));
        Assert.False(progress.BeginTournamentReward(title, uint.MaxValue, uint.MaxValue, now));
        Assert.True(progress.BeginTournamentReward(title, 3000, 9_996_999, now));
        Assert.False(progress.RejectTournamentReward(9_999_999));
        Assert.NotNull(progress.PendingTournamentReward);
        Assert.True(progress.RejectTournamentReward(9_996_999));
        Assert.Null(progress.LastTournamentReward);
        Assert.False(progress.RejectTournamentReward(9_996_999));
        Assert.True(progress.BeginTournamentReward(title, 3000, 9_996_999, now));
        Assert.True(progress.AcknowledgeTournamentReward(title));
        Assert.True(progress.ConfirmTournamentReward(9_999_999, now.AddSeconds(1)));
        var busy = new VerminionProgress();
        Assert.NotEqual(0, busy.BeginMatch(579));
        Assert.False(busy.BeginTournamentReward(title, 3000, 50000, now));
        busy.AbandonMatch();
        Assert.True(busy.ReservePurchase(7561, 76, 0, 0, 50000, 50000, 0, false, 0, 0,
            certificates: 2, certificatesBefore: 6, certificateCap: 2));
        Assert.False(busy.BeginTournamentReward(title, 3000, 50000, now));
        Assert.False(new VerminionProgress { MinionAcquisition = new("test", 7561, 76) }
            .BeginTournamentReward(title, 3000, 50000, now));
        Assert.False(new VerminionProgress { QuestAcquisition = new(123, 21, "WigglyQuest", [204]) }
            .BeginTournamentReward(title, 3000, 50000, now));
    }

    [Fact]
    public void CertificateCapAndReceiptSurviveReloadWithoutSpendingOtherBudgets()
    {
        var character = new CharacterConfig { VerminionCertificatePurchaseCap = 2 };
        var progress = character.VerminionProgress;
        Assert.False(progress.ReservePurchase(7561, 76, 0, 0, 50000, 100000, 0, false, 0, 0,
            certificates: 2, certificatesBefore: 6)); // Zero cap.
        Assert.False(progress.ReservePurchase(7561, 76, 0, 0, 50000, 100000, 1, false, 0, 0,
            certificates: 2, certificatesBefore: 6, certificateCap: 2)); // Already in inventory.
        Assert.True(progress.ReservePurchase(7561, 76, 0, 0, 50000, 100000, 0, false, 0, 0,
            certificates: 2, certificatesBefore: 6, certificateCap: 2));
        var restored = Newtonsoft.Json.JsonConvert.DeserializeObject<CharacterConfig>(Newtonsoft.Json.JsonConvert.SerializeObject(character))!;
        Assert.Equal(2u, restored.VerminionCertificatePurchaseCap);
        var receipt = restored.VerminionProgress;
        Assert.False(receipt.ConfirmPurchase(50000, 100000, 0, false, 4)); // Currency alone.
        Assert.False(receipt.ConfirmPurchase(50000, 100000, 1, false, 6)); // Item alone.
        Assert.False(receipt.ConfirmPurchase(50000, 100000, 1, false, 3)); // Unexplained spending.
        Assert.True(receipt.ConfirmPurchase(50000, 100000, 1, false, 4));
        Assert.False(receipt.ConfirmPurchase(50000, 100000, 1, false, 4));
        receipt.ObserveWeek(new DateTime(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc));
        Assert.Equal(2ul, receipt.CertificatesSpent);
        Assert.Equal(0ul, receipt.GilSpent);
        Assert.Equal(0ul, receipt.MgpSpent);
        Assert.False(receipt.CanSpend(0, 0, 0, 0, 2, 2));
        Assert.True(new VerminionProgress().CanSpend(0, 0, 0, 0, 2, 2)); // Separate character.
        Assert.NotNull(progress.PendingPurchase); // Deserialization did not mutate the source.
    }

    [Fact]
    public void TutorialCompletionNeedsFreshAdmittedVictoryAndDoesNotCreditWeeklyResults()
    {
        var character = new CharacterConfig();
        var progress = character.VerminionProgress;
        progress.WeeklyMatches = 3;
        progress.WeeklyWins = 1;
        progress.GilSpent = 2400;
        progress.MgpSpent = 10000;
        var abandoned = progress.BeginMatch(552);
        progress.AbandonMatch();
        Assert.False(progress.RecordTutorialCompletion(abandoned, VerminionBattleOutcome.Victory));

        var admitted = progress.BeginMatch(552);
        character = JsonConvert.DeserializeObject<CharacterConfig>(JsonConvert.SerializeObject(character))!;
        progress = character.VerminionProgress;
        Assert.False(progress.RecordTutorialCompletion(abandoned, VerminionBattleOutcome.Victory));
        Assert.False(progress.RecordTutorialCompletion(admitted, VerminionBattleOutcome.Unknown));
        Assert.False(progress.RecordTutorialCompletion(admitted, VerminionBattleOutcome.Defeat));
        Assert.Equal(admitted, progress.PendingMatch);
        Assert.Equal(0u, progress.ClearedChallengeMask);

        Assert.True(progress.RecordTutorialCompletion(admitted, VerminionBattleOutcome.Victory));
        Assert.False(progress.RecordTutorialCompletion(admitted, VerminionBattleOutcome.Victory));
        Assert.Equal(0, progress.PendingMatch);
        Assert.Equal(1u, progress.ClearedChallengeMask);
        Assert.Equal(3, progress.WeeklyMatches);
        Assert.Equal(1, progress.WeeklyWins);
        Assert.Equal(2400u, progress.GilSpent);
        Assert.Equal(10000u, progress.MgpSpent);
        Assert.Equal(0u, new CharacterConfig().VerminionProgress.ClearedChallengeMask);

        var ordinary = progress.BeginMatch(553);
        Assert.False(progress.RecordTutorialCompletion(ordinary, VerminionBattleOutcome.Victory));
        Assert.Equal(ordinary, progress.PendingMatch);
    }

    [Fact]
    public void NativeAcquisitionRetainsOwnershipAndCancellationWithoutSharingCharactersOrCreditingBattles()
    {
        var character = new CharacterConfig { VerminionPaused = true };
        var progress = character.VerminionProgress;
        progress.CampaignRequested = true;
        progress.CampaignStage = 23;
        progress.CampaignStageAttempts = 3;
        progress.ClearedChallengeMask = (1u << 22) - 1;
        progress.QuestAcquisition = new(123, 21, "WigglyQuest", [204, 490, 491, 492, 493, 502]);
        Assert.False(progress.QuestAcquisition.OwnsQuest(123, "204")); // Prepared, not submitted.
        progress.QuestAcquisition = progress.QuestAcquisition with { DispatchAttempted = true, CancellationRequested = true };
        character = JsonConvert.DeserializeObject<CharacterConfig>(JsonConvert.SerializeObject(character))!;
        progress = character.VerminionProgress;
        var handoff = progress.QuestAcquisition!;
        Assert.True(character.VerminionPaused);
        Assert.True(handoff.CancellationRequested);
        Assert.True(handoff.OwnsQuest(123, "490")); // Q advances the chain itself.
        Assert.False(handoff.OwnsQuest(456, "490"));
        Assert.False(handoff.OwnsQuest(123, "1431"));
        Assert.False(handoff.OwnsQuest(123, null));
        Assert.False((handoff with { OwnershipReleased = true }).OwnsQuest(123, "490"));
        var copy = character.Clone();
        copy.VerminionProgress.QuestAcquisition!.Quests[0] = 999;
        Assert.Equal((ushort)204, handoff.Quests[0]);
        var other = new CharacterConfig();
        other.CopyVerminionSettingsFrom(character);
        Assert.Null(other.VerminionProgress.QuestAcquisition);
        character.ResetVerminionState();
        progress.ObserveWeek(new System.DateTime(2026, 9, 29, 9, 0, 0, System.DateTimeKind.Utc));
        Assert.Same(handoff, progress.QuestAcquisition);
        Assert.Equal(3, progress.CampaignStageAttempts);
        Assert.Equal(23, progress.NextUnclearedChallenge);
        Assert.Equal(0, progress.WeeklyWins);
        Assert.Equal(0, progress.WeeklyMatches);
        Assert.Equal((ushort)502, handoff.RewardQuest);
        Assert.Equal(handoff.RewardQuest, VerminionRoster.AcquisitionQuests(21)[^1]);
        Assert.Empty(VerminionRoster.AcquisitionQuests(52)); // No general MSQ progression.
    }

    [Fact]
    public void StageSevenCountersCrittersAndLeavesCapacityForReplacements()
    {
        Assert.True(VerminionBattleStrategy.IsStoneStage(22));
        Assert.False(VerminionBattleStrategy.IsStoneStage(9)); // Bomb mechanics.
        Assert.False(VerminionBattleStrategy.IsStoneStage(23)); // Twintania boss.
        Assert.False(VerminionBattleStrategy.IsStoneStage(24)); // Bahamut/towers.
        Assert.Equal(1, VerminionBattleStrategy.ChooseRemainingStone([0, 95, 0]));
        Assert.Equal(2, VerminionBattleStrategy.ChooseRemainingStone([0, 5000, 120]));
        Assert.Equal(-1, VerminionBattleStrategy.ChooseRemainingStone([0, 0, 0]));
        Assert.Equal(-1, VerminionBattleStrategy.ChooseRemainingStone([0, null, 5000]));
        var strategy = new VerminionBattleStrategy(7);
        Assert.Equal(VerminionBattleStrategy.Minion, new VerminionBattleStrategy(2).CurrentMinion);
        Assert.Equal(VerminionBattleStrategy.CritterCounter, strategy.CurrentMinion);
        Assert.Equal(VerminionBattleStrategy.Action.Wait, strategy.Decide(true, 0, 60, 60));
        var used = 0;
        var targets = new System.Collections.Generic.List<string>();
        while (!strategy.OpeningComplete)
        {
            targets.Add(strategy.TargetName);
            var size = strategy.GroupSize;
            var cost = strategy.MinionCost;
            for (var request = 0; request < size; ++request)
            {
                Assert.Equal(VerminionBattleStrategy.Action.Summon, strategy.Decide(true, 0, used, 240));
                strategy.Dispatched(VerminionBattleStrategy.Action.Summon);
            }
            Assert.Equal(VerminionBattleStrategy.Action.Wait, strategy.Decide(true, 0, used, 240));
            used += size * cost;
            Assert.InRange(used, 0, 240);
            Assert.Equal(VerminionBattleStrategy.Action.AttackObjective, strategy.Decide(true, size, used, 240));
            strategy.Dispatched(VerminionBattleStrategy.Action.AttackObjective);
        }
        Assert.Equal(180, used);
        Assert.Equal(new[] { "Arcana Stone B", "Arcana Stone C", "Arcana Stone A" }, targets);
        var replacements = strategy.Reinforcements;
        Assert.False(replacements.CanRequest(0, 240)); // Read the existing roster first.
        replacements.ObserveUnits([10, 11, 12]);
        Assert.False(replacements.CanRequest(240, 240));
        Assert.True(replacements.CanRequest(220, 240)); // Two casualties free capacity.
        replacements.Requested();
        replacements.Requested();
        Assert.False(replacements.CanRequest(220, 240)); // Both slots are reserved.
        Assert.Equal(0, replacements.ObserveUnits([10, 12, 0]));
        Assert.Equal(2, replacements.ObserveUnits([20, 21]));
        Assert.False(replacements.CanRequest(240, 240));
        Assert.True(replacements.CanRequest(230, 240));
    }

    [Theory]
    [InlineData(12)]
    [InlineData(15)]
    [InlineData(23)]
    [InlineData(24)]
    public void FinalBossReinforcementsDoNotWaitForAnImpossibleFullWave(int stage)
    {
        var strategy = new VerminionBossStrategy(stage);
        strategy.ObserveUnits([]);
        Assert.Equal((ushort)21, strategy.CurrentMinion);
        Assert.Equal(30, strategy.MinionCost);
        Assert.Equal(8, strategy.ArmySize);
        Assert.False(strategy.DefendsStone);
        if (stage is 12 or 24) Assert.False(strategy.ShouldUseSpecial(1, 18000, 8));
        Assert.Equal(VerminionBossStrategy.Action.SendWave, strategy.Decide(true, 1, 7, 240, 240, 2, 2, false));
        // A moving boss must not starve the production queue. Refill two slots
        // before spending another several seconds selecting and moving units.
        Assert.Equal(VerminionBossStrategy.Action.Summon, strategy.Decide(true, 0, 1, 30, 240, 0, 5, true));
        strategy.Dispatched(VerminionBossStrategy.Action.Summon);
        Assert.Equal(VerminionBossStrategy.Action.SelectGate, strategy.Decide(false, 0, 1, 30, 240, 0, 5, true));
        Assert.Equal(VerminionBossStrategy.Action.Summon, strategy.Decide(true, 0, 1, 30, 240, 0, 5, true));
        strategy.Dispatched(VerminionBossStrategy.Action.Summon);
        Assert.Equal(VerminionBossStrategy.Action.SendWave, strategy.Decide(true, 2, 2, 120, 240, 15, 2, false));
        Assert.Equal(VerminionBossStrategy.Action.FollowBoss, strategy.Decide(true, 0, 1, 30, 240, 0, 5, true));
        Assert.Equal(VerminionBossStrategy.Action.Wait, strategy.Decide(true, 1, 5, 180, 240, 2, 2, false));
        Assert.Equal(VerminionBossStrategy.Action.Wait, strategy.Decide(true, 4, 2, 180, 240, 60, 60, true, assemble: true));
        Assert.Equal(VerminionBossStrategy.Action.FollowBoss, strategy.Decide(true, 0, 1, 10, 240, 0, 5, true, gateAvailable: false));
    }

    [Fact]
    public void DemonBrickRearDestinationStaysStableAndOnlyFrontApproachesDetour()
    {
        var boss = new System.Numerics.Vector3(16, 0, 7);
        foreach (var rotation in new[] { 0f, MathF.PI / 2, MathF.PI, -MathF.PI / 2 })
        {
            var facing = new System.Numerics.Vector3(MathF.Sin(rotation), 0, MathF.Cos(rotation));
            var side = new System.Numerics.Vector3(facing.Z, 0, -facing.X);
            var rear = boss - facing * 0.8f;
            Assert.Equal(rear, VerminionBossStrategy.DemonBrickDestination(boss, rotation, null));
            Assert.Equal(rear, VerminionBossStrategy.DemonBrickDestination(boss, rotation, rear));
            Assert.Equal(rear, VerminionBossStrategy.DemonBrickDestination(boss, rotation, boss - facing * 0.2f));
            foreach (var sign in new[] { -1, 1 })
            {
                var approach = boss + facing * 3 + side * sign * 2;
                var detour = boss + side * sign * 2 - facing * 2;
                Assert.Equal(detour, VerminionBossStrategy.DemonBrickDestination(boss, rotation, approach));
                Assert.Equal(rear, VerminionBossStrategy.DemonBrickDestination(boss, rotation, detour));
            }
        }
    }

    [Theory]
    [InlineData(12)]
    [InlineData(24)]
    public void BossPursuitRejoinsSeparatedUnitsWithoutInterruptingAnArmyInTransit(int stage)
    {
        var strategy = new VerminionBossStrategy(stage);
        var target = new System.Numerics.Vector3(16, 0, 7);
        (ulong Id, System.Numerics.Vector3 Position)[] units =
        [
            (1, new(15, 0, 5)), (2, new(16, 0, 4)),
            (3, new(-2, 0, -8)), (4, new(-1, 0, -9)),
        ];
        Assert.Contains(strategy.ChooseStraggler(units, target), new ulong[] { 3, 4 });
        Assert.Equal(0ul, strategy.ChooseStraggler(units, new(40, 0, 40)));
        Assert.Equal(0ul, strategy.ChooseStraggler(units[..2], target));
        Assert.Equal(0ul, strategy.ChooseStraggler([], target));
        Assert.Equal(0ul, new VerminionBossStrategy(19).ChooseStraggler(units, target));
        Assert.Equal(0ul, new VerminionBossStrategy(23).ChooseStraggler(units, target));
        units[2] = (3, new(7, 0, 7));
        units[3] = (4, new(8, 0, 7));
        if (stage == 24) Assert.Contains(strategy.ChooseStraggler(units, target), new ulong[] { 3, 4 });
        else Assert.Equal(0ul, strategy.ChooseStraggler(units, target));
        units[2] = (3, new(16, 0, 6));
        units[3] = (4, new(17, 0, 6));
        Assert.Equal(0ul, strategy.ChooseStraggler(units, target));
    }

    [Fact]
    public void OdinRecoveryReturnsTheSeparatedAttackerAfterTheMainPartyReachesItsGate()
    {
        var strategy = new VerminionBossStrategy(15);
        var gate = new System.Numerics.Vector3(0, 0, 24);
        var units = Enumerable.Range(1, 8)
            .Select(id => ((ulong)id, gate + new System.Numerics.Vector3(id * 0.1f, 0, -2))).ToArray();
        Assert.Equal(0ul, strategy.ChooseOdinRecoveryUnit(units, gate));
        units[7] = (8, new System.Numerics.Vector3(6, 0, -7));
        Assert.Equal(8ul, strategy.ChooseOdinRecoveryUnit(units, gate));
        Assert.Equal(0ul, new VerminionBossStrategy(19).ChooseOdinRecoveryUnit(units, gate));
        Assert.Equal(0ul, strategy.ChooseOdinRecoveryUnit(units[7..], gate)); // Main party has not arrived.
        units[7] = (8, gate + new System.Numerics.Vector3(1, 0, -1));
        Assert.Equal(0ul, strategy.ChooseOdinRecoveryUnit(units, gate));
    }

    [Fact]
    public void BossDeploymentBoundsQueueAndKeepsSurvivorsFollowingAtCapacity()
    {
        Assert.True(VerminionBossStrategy.IsInvulnerabilityAdd(6, "Infant Imp"));
        Assert.True(VerminionBossStrategy.IsInvulnerabilityAdd(6, "Imp"));
        Assert.False(VerminionBossStrategy.IsInvulnerabilityAdd(6, "Smallshell"));
        Assert.False(VerminionBossStrategy.IsInvulnerabilityAdd(4, "Imp"));
        foreach (var stage in new[] { 4, 6, 9, 19 })
        {
            var strategy = new VerminionBossStrategy(stage);
            strategy.ObserveUnits([100]); // Reload baseline cannot confirm a request.
            Assert.Equal((ushort)52, strategy.CurrentMinion);
            Assert.Equal(25, strategy.MinionCost);
            Assert.Equal(9, strategy.ArmySize);
            Assert.Equal(VerminionBossStrategy.Action.SelectGate, strategy.Decide(false, 0, 0, 0, 60, 0, 100, true));
            for (var request = 0; request < 2; ++request)
            {
                Assert.Equal(VerminionBossStrategy.Action.Summon, strategy.Decide(true, 0, 0, 0, 60, 0, 100, true));
                strategy.Dispatched(VerminionBossStrategy.Action.Summon);
            }
            Assert.Equal(VerminionBossStrategy.Action.Wait, strategy.Decide(true, 0, 0, 0, 60, 0, 100, true));
            Assert.Equal(0, strategy.ObserveUnits([100]));
            Assert.Equal(1, strategy.ObserveUnits([100, 101]));
            Assert.Equal(0, strategy.ObserveUnits([101]));
            Assert.Equal(1, strategy.PendingSummons);
            Assert.Equal(1, strategy.ObserveUnits([102]));
            Assert.Equal(0, strategy.PendingSummons);
            Assert.Equal(VerminionBossStrategy.Action.SendWave, strategy.Decide(true, 4, 0, 100, 240, 0, 0, false));
            Assert.Equal(VerminionBossStrategy.Action.SendWave, strategy.Decide(true, 2, 0, 50, 60, 60, 0, false));
            Assert.Equal(VerminionBossStrategy.Action.FollowBoss, strategy.Decide(true, 0, 9, 225, 240, 0, 10, true));
            Assert.Equal(VerminionBossStrategy.Action.Wait, strategy.Decide(true, 0, 9, 225, 240, 0, 20, false));
            Assert.Equal(VerminionBossStrategy.Action.Summon, strategy.Decide(true, 0, 8, 200, 240, 0, 0, false));
            Assert.Equal(VerminionBossStrategy.Action.Wait, strategy.Decide(false, 0, 0, 0, 240, 0, 100, false, false));
        }

        var defended = new VerminionBossStrategy(6);
        defended.ObserveUnits([]);
        defended.Defenders.ObserveUnits([]);
        Assert.Equal(60, defended.AttackCapacity(60, 0));
        Assert.Equal(200, defended.AttackCapacity(240, 0));
        for (var guard = 0; guard < 4; ++guard)
        {
            Assert.True(defended.CanRequestDefender(0, 175, 240));
            defended.Defenders.Requested();
        }
        Assert.False(defended.CanRequestDefender(0, 175, 240));
        Assert.Equal(4, defended.Defenders.ObserveUnits([1, 2, 3, 4]));
        Assert.False(defended.CanRequestDefender(4, 240, 240));
        Assert.Equal(230, defended.AttackCapacity(240, 3));
        Assert.True(defended.CanRequestDefender(3, 230, 240));
        // Reserve a missing defender before considering another 25-cost Airship.
        Assert.Equal(VerminionBossStrategy.Action.Wait,
            defended.Decide(true, 0, 8, 230, defended.AttackCapacity(240, 3), 0, 0, false));
        var gilgamesh = new VerminionBossStrategy(19);
        gilgamesh.ObserveUnits([]);
        gilgamesh.Defenders.ObserveUnits([]);
        Assert.Equal(0, gilgamesh.CurrentDefenderCount);
        Assert.False(gilgamesh.CanRequestDefender(0, 0, 240));
        Assert.Equal(240, gilgamesh.AttackCapacity(240, 0));

        var odin = new VerminionBossStrategy(15);
        odin.ObserveUnits([]);
        for (var request = 0; request < 6; ++request)
        {
            Assert.Equal(VerminionBossStrategy.Action.Summon,
                odin.Decide(true, 0, 0, 0, 240, 90, 90, true, assemble: true));
            odin.Dispatched(VerminionBossStrategy.Action.Summon);
        }
        Assert.Equal(VerminionBossStrategy.Action.Wait, odin.Decide(true, 0, 0, 0, 240, 90, 90, true, assemble: true));
        Assert.Equal(6, odin.ObserveUnits(Enumerable.Range(1, 6).Select(id => (ulong)id)));
        for (var request = 0; request < 2; ++request)
        {
            Assert.Equal(VerminionBossStrategy.Action.Summon,
                odin.Decide(true, 6, 0, 180, 240, 90, 90, true, assemble: true));
            odin.Dispatched(VerminionBossStrategy.Action.Summon);
        }
        Assert.Equal(VerminionBossStrategy.Action.Wait, odin.Decide(true, 6, 0, 180, 240, 90, 90, true, assemble: true));
        Assert.Equal(2, odin.ObserveUnits(Enumerable.Range(1, 8).Select(id => (ulong)id)));
        Assert.Equal(VerminionBossStrategy.Action.SendWave, odin.Decide(true, 8, 0, 240, 240, 90, 90, true));
        Assert.Equal(VerminionBossStrategy.Action.Summon, odin.Decide(true, 0, 7, 210, 240, 0, 0, false));
    }

    [Fact]
    public void AccessibleRosterChecksOwnershipBeforeAdmissionWithoutGatingIntentionalLosses()
    {
        Assert.Empty(VerminionRoster.Required(1));
        for (var stage = 2; stage <= 24; ++stage)
        {
            var roster = VerminionRoster.Required(stage);
            Assert.NotEmpty(roster);
            Assert.All(roster, minion => Assert.Contains(minion.Id, new ushort[] { 2, 3, 21, 52, 83 }));
            Assert.Null(VerminionRoster.Missing(stage, _ => true));
            Assert.Contains("ownership is unavailable", VerminionRoster.Missing(stage, _ => null));
            Assert.Contains(roster[0].Name, VerminionRoster.Missing(stage, _ => false));
            Assert.Contains(roster[0].Acquisition, VerminionRoster.Missing(stage, _ => false));
        }
        Assert.Contains("Zu Hatchling", VerminionRoster.Missing(6, id => id == 52));
        Assert.Null(VerminionRoster.Missing(19, id => id == 52));
        Assert.True(VerminionRoster.NeedsBattleRoster(2, false, VerminionMode.Participation, 1));
        Assert.False(VerminionRoster.NeedsBattleRoster(2, false, VerminionMode.Participation, 3));
        Assert.False(VerminionRoster.NeedsBattleRoster(2, false, VerminionMode.CpuRewards, 3));
        Assert.True(VerminionRoster.NeedsBattleRoster(2, false, VerminionMode.MissionRepeat, 3));
        Assert.True(VerminionRoster.NeedsBattleRoster(19, true, VerminionMode.Participation, 262143));
    }

    [Fact]
    public void OdinPursuitDoesNotStopWhenOnlyTheSelectedAttackerHasReachedMelee()
    {
        var boss = System.Numerics.Vector3.Zero;
        var units = Enumerable.Range(0, 8).Select(_ => new System.Numerics.Vector3(3.5f, 0, 0)).ToArray();
        units[0] = new(1.5f, 0, 0); // Selected unit is near, seven others are stranded.
        Assert.False(VerminionBossStrategy.OdinArmyInMelee(units, boss));
        for (var index = 1; index < 4; index++) units[index] = new(1.5f, 0, 0);
        Assert.True(VerminionBossStrategy.OdinArmyInMelee(units, boss));
        units[3] = new(2, 0, 0); // The boundary does not establish arrival.
        Assert.False(VerminionBossStrategy.OdinArmyInMelee(units, boss));
        Assert.False(VerminionBossStrategy.OdinArmyInMelee([], boss));
    }

    [Theory]
    [InlineData(15, 4500u)]
    [InlineData(23, 3600u)]
    public void GentlemanGuideRosterBlocksMissingQuestMinionAndPreservesTheLastAttackParty(int stage, uint finalPhaseHp)
    {
        var ownedVendors = new ushort[] { 2, 3, 52, 83, 174 };
        Assert.Contains("Her Last Vow", VerminionRoster.Missing(stage, id => ownedVendors.Contains(id)));
        Assert.Null(VerminionRoster.Missing(stage, id => id == 21));
        var strategy = new VerminionBossStrategy(stage);
        Assert.False(strategy.ShouldUseSpecial(finalPhaseHp + 1, 18000, 8));
        Assert.True(strategy.ShouldUseSpecial(finalPhaseHp, 18000, 8));
        Assert.False(strategy.ShouldUseSpecial(finalPhaseHp, 18000, 7));
        Assert.False(strategy.ShouldUseSpecial(100, 18000, 4));
        Assert.False(strategy.ShouldUseSpecial(0, 0, 8));
        Assert.True(new VerminionBossStrategy(19).ShouldUseSpecial(6000, 18000, 4));
    }

    [Fact]
    public void FinalCoilRequiresTheGuideRosterAndKeepsUnitsForTowers()
    {
        var ownedVendors = new ushort[] { 2, 3, 52, 83, 174 };
        Assert.Contains("Her Last Vow", VerminionRoster.Missing(24, id => ownedVendors.Contains(id)));
        Assert.Null(VerminionRoster.Missing(24, id => id == 21));
        var strategy = new VerminionBossStrategy(24);
        Assert.False(strategy.ShouldUseSpecial(18000, 18000, 8));
        Assert.False(strategy.ShouldUseSpecial(3600, 18000, 8));
        Assert.False(strategy.ShouldUseSpecial(1, 18000, 8));
        Assert.False(strategy.ShouldUseSpecial(1, 18000, 4));
        Assert.False(strategy.ShouldUseSpecial(0, 0, 8));
        strategy.ObserveUnits([]);
        // Two thirty-point requests exhaust the opening allowance exactly.
        for (var i = 0; i < 2; ++i)
        {
            Assert.Equal(VerminionBossStrategy.Action.Summon,
                strategy.Decide(true, 0, 0, 0, 60, 0, 0, false));
            strategy.Dispatched(VerminionBossStrategy.Action.Summon);
        }
        Assert.Equal(VerminionBossStrategy.Action.Wait,
            strategy.Decide(true, 0, 0, 0, 60, 0, 0, false));
        Assert.Equal(2, strategy.ObserveUnits([1, 2]));
        Assert.Equal(VerminionBossStrategy.Action.SendWave,
            strategy.Decide(true, 2, 0, 60, 60, 0, 0, false));
    }

    [Fact]
    public void FinalCoilEscapesCirclesWithinArenaAndHoldsMovementDuringWarnings()
    {
        foreach (var x in new[] { -23f, -20f, 0f, 20f, 23f })
        foreach (var z in new[] { -23f, -20f, 0f, 20f, 23f })
        foreach (var radius in new[] { 0.1f, 3f, 10f })
        {
            var circle = new VerminionGroundOmen(11, false, new(x, 0, z), radius);
            var candidate = VerminionBossStrategy.FinalCoilDodgeDestination(circle, circle.Position);
            Assert.True(candidate.HasValue);
            var point = candidate.Value;
            Assert.InRange(point.X, -23, 23);
            Assert.InRange(point.Z, -23, 23);
            Assert.True(System.Numerics.Vector3.Distance(point, circle.Position) >= radius + 2.99f);
        }
        var strategy = new VerminionBossStrategy(24);
        strategy.ObserveUnits([1, 2, 3, 4, 5, 6, 7, 8]);
        // A warning centered on the army must not choose a direction from tiny
        // formation offsets and kite the boss away. Keep the safe point near it.
        var center = new VerminionGroundOmen(11, false, System.Numerics.Vector3.Zero, 3);
        var towardsBoss = VerminionBossStrategy.FinalCoilDodgeDestination(center, new(0, 0, -10))!.Value;
        Assert.InRange(towardsBoss.X, -0.001f, 0.001f);
        Assert.InRange(towardsBoss.Z, -6.001f, -5.999f);
        var overlapping = center with { Slot = 12, Position = new(0, 0, -6) };
        var alternative = VerminionBossStrategy.FinalCoilDodgeDestination(center, new(0, 0, -10), [center, overlapping]);
        Assert.True(alternative.HasValue);
        Assert.True(System.Numerics.Vector3.Distance(alternative.Value, overlapping.Position) > overlapping.Radius + 1);
        Assert.True(VerminionBossStrategy.CanContinueFinalCoilDodgeOrder([center], towardsBoss));
        Assert.False(VerminionBossStrategy.CanContinueFinalCoilDodgeOrder([center, overlapping], towardsBoss));
        Assert.False(VerminionBossStrategy.CanContinueFinalCoilDodgeOrder([], towardsBoss));
        Assert.Null(VerminionBossStrategy.FinalCoilDodgeDestination(center, new(0, 0, -10),
            [center with { Radius = 10 }])); // No sampled endpoint is safe.
        var tower = new VerminionGroundOmen(14, true, new(10, 0, 0), 3);
        var remoteCircle = new VerminionGroundOmen(11, false, new(5, 0, 8), 3);
        Assert.True(VerminionBossStrategy.CanContinueFinalCoilDodgeOrder([center, tower], towardsBoss));
        Assert.True(VerminionBossStrategy.CanContinueFinalCoilTowerOrder([tower, remoteCircle],
            System.Numerics.Vector3.Zero, tower.Position));
        Assert.False(VerminionBossStrategy.CanContinueFinalCoilTowerOrder([remoteCircle],
            System.Numerics.Vector3.Zero, tower.Position)); // Expired tower.
        foreach (var x in new[] { 0f, 5f, 10f })
            Assert.False(VerminionBossStrategy.CanContinueFinalCoilTowerOrder(
                [tower, remoteCircle with { Position = new(x, 0, 0) }],
                System.Numerics.Vector3.Zero, tower.Position)); // Unsafe origin, route or destination.
        Assert.Equal(VerminionBossStrategy.Action.Wait,
            strategy.Decide(true, 4, 4, 240, 240, 60, 60, true, assemble: true));
        Assert.Equal(VerminionBossStrategy.Action.Summon,
            strategy.Decide(true, 4, 3, 210, 240, 60, 60, true, assemble: true));
        strategy.Dispatched(VerminionBossStrategy.Action.Summon);
        Assert.Equal(VerminionBossStrategy.Action.Wait,
            strategy.Decide(true, 4, 3, 210, 240, 60, 60, true, assemble: true));
    }

    [Fact]
    public void GroundWarningsFollowNativeRemovalInsteadOfVisualLifetime()
    {
        var warnings = new VerminionGroundWarnings();
        var tower = new VerminionGroundOmen(14, true, new(10, 0, 0), 3);
        warnings.Command(14, created: true);
        Assert.True(warnings.HasUnreadableWarning);
        warnings.ObserveSnapshot([tower]);
        Assert.False(warnings.HasUnreadableWarning);
        warnings.ObserveSnapshot([]);
        Assert.Equal(tower, Assert.Single(warnings.Omens));
        Assert.True(VerminionBossStrategy.CanContinueFinalCoilTowerOrder(warnings.Omens,
            System.Numerics.Vector3.Zero, tower.Position));

        warnings.Command(14, created: false);
        Assert.Empty(warnings.Omens);
        warnings.ObserveSnapshot([tower]); // A removed slot cannot be resurrected by a stale snapshot.
        Assert.Empty(warnings.Omens);
        warnings.Command(14, created: true);
        warnings.ObserveSnapshot([tower]);
        warnings.Command(14, created: true);
        warnings.ObserveSnapshot([]);
        Assert.Empty(warnings.Omens);
        Assert.True(warnings.HasUnreadableWarning);
        warnings.ObserveSnapshot([tower with { Position = new(-10, 0, 0) }]);
        Assert.Equal(new System.Numerics.Vector3(-10, 0, 0), Assert.Single(warnings.Omens).Position);
        warnings.Command(11, created: true); // An unreadable new warning remains unknown.
        warnings.ObserveSnapshot([]);
        Assert.True(warnings.HasUnreadableWarning);
        warnings.Command(10, created: true);
        warnings.Command(30, created: true);
        Assert.Single(warnings.Omens);
        warnings.Clear();
        Assert.Empty(warnings.Omens);
        Assert.False(warnings.HasUnreadableWarning);
        warnings.ObserveSnapshot([tower], seedExisting: true); // Attach during an existing warning.
        warnings.ObserveSnapshot([]);
        Assert.Equal(tower, Assert.Single(warnings.Omens));
        warnings.Command(14, created: false);
        Assert.Empty(warnings.Omens);
    }

    [Fact]
    public void FinalCoilReplacesWoundedTowerCandidateAfterInterruptedMovement()
    {
        var tower = new System.Numerics.Vector3(-8.22f, 0, 0.65f);
        (ulong Id, System.Numerics.Vector3 Position, uint Hp, uint MaxHp)[] units =
            [(1, new(-4.94f, 0, 2.05f), 172, 400), (2, new(-0.61f, 0, 17.61f), 400, 400),
             (3, new(0, 0, 30), 400, 400), (4, tower, 0, 400)];
        Assert.Equal(2ul, VerminionBossStrategy.ChooseFinalCoilTowerOccupant(units, tower, 1, false));
        Assert.Equal(1ul, VerminionBossStrategy.ChooseFinalCoilTowerOccupant(units, tower, 1, true));
        units[0] = (1, tower, 42, 400);
        Assert.Equal(1ul, VerminionBossStrategy.ChooseFinalCoilTowerOccupant(units, tower, 1, false));
        units[0] = (1, new(-4.94f, 0, 2.05f), 210, 400);
        Assert.Equal(1ul, VerminionBossStrategy.ChooseFinalCoilTowerOccupant(units, tower, 1, false));
        Assert.Equal(0ul, VerminionBossStrategy.ChooseFinalCoilTowerOccupant(
            [(1, new(0, 0, 0), 172, 400), (4, tower, 0, 400)], tower, 1, false));
    }

    [Fact]
    public void FinalCoilAttacksDuringTowersWithoutMovingOccupantOrRepeatingTravellingOrders()
    {
        var strategy = new VerminionBossStrategy(24);
        var now = new DateTime(2026, 9, 28, 11, 0, 0, DateTimeKind.Utc);
        (ulong, System.Numerics.Vector3)[] units =
            [(1, new(2, 0, 0)), (2, new(3, 0, 0)), (3, new(1, 0, 0)), (4, new(12, 0, 0))];
        var target = System.Numerics.Vector3.Zero;
        Assert.Equal(0ul, strategy.ChooseFinalCoilAttacker(units, target, 0, now));
        Assert.Equal(2ul, strategy.ChooseFinalCoilAttacker(units, target, 1, now));
        strategy.FinalCoilAttackerDispatched(2, now);
        // Native hit readback can select a different overlapping unit. Apply
        // the same protection and in-flight guard to that actual individual.
        Assert.False(strategy.CanOrderFinalCoilAttacker(1, new(2, 0, 0), target, 1, now));
        Assert.False(strategy.CanOrderFinalCoilAttacker(2, new(3, 0, 0), target, 1, now.AddSeconds(1)));
        Assert.Equal(4ul, strategy.ChooseFinalCoilAttacker(units, target, 1, now.AddSeconds(1)));
        strategy.FinalCoilAttackerDispatched(4, now.AddSeconds(1));
        Assert.Equal(0ul, strategy.ChooseFinalCoilAttacker(units, target, 1, now.AddSeconds(9)));
        Assert.Equal(2ul, strategy.ChooseFinalCoilAttacker(units, target, 1, now.AddSeconds(10)));
        Assert.Equal(0ul, new VerminionBossStrategy(23).ChooseFinalCoilAttacker(units, target, 1, now));
    }

    [Fact]
    public void FinalCoilFocusesOneAddWithoutTreatingMissingObjectsAsDefeats()
    {
        var strategy = new VerminionBossStrategy(24);
        Assert.True(VerminionBossStrategy.IsFinalCoilBoss("wind-up Bahamut"));
        Assert.False(VerminionBossStrategy.IsFinalCoilBoss("Clockwork Twintania"));
        Assert.Equal(99ul, strategy.ChooseFinalCoilTarget(99, []));
        Assert.Equal(99ul, strategy.ChooseFinalCoilTarget(99, [(1, "Clockwork Twintania", 500), (2, "Imp", 100)]));
        Assert.Equal(2ul, strategy.ChooseFinalCoilTarget(99, [(1, "Clockwork Twintania", 500), (2, "clockwork Twintania", 400)]));
        // Other add becoming weaker cannot split the army's damage.
        Assert.Equal(2ul, strategy.ChooseFinalCoilTarget(99, [(1, "Clockwork Twintania", 100), (2, "Clockwork Twintania", 300)]));
        Assert.Equal(99ul, strategy.ChooseFinalCoilTarget(99, [(1, "Clockwork Twintania", 100)]));
        Assert.Equal(2ul, strategy.ChooseFinalCoilTarget(99, [(1, "Clockwork Twintania", 100), (2, "Clockwork Twintania", 200)]));
        Assert.Equal(99ul, strategy.ChooseFinalCoilTarget(99, [(1, "Clockwork Twintania", 100), (2, "Clockwork Twintania", 0)]));
        Assert.Equal(99ul, strategy.ChooseFinalCoilTarget(99, [(1, "Clockwork Twintania", 100), (3, "Clockwork Twintania", 100)]));
        // A fresh strategy seeing only a survivor must continue the boss.
        Assert.Equal(99ul, new VerminionBossStrategy(24).ChooseFinalCoilTarget(99, [(1, "Clockwork Twintania", 100)]));
        Assert.Equal(99ul, new VerminionBossStrategy(23).ChooseFinalCoilTarget(99, [(1, "Clockwork Twintania", 100), (2, "Clockwork Twintania", 100)]));
        Assert.Equal(0ul, new VerminionBossStrategy(24).ChooseFinalCoilTarget(0, [(1, "Clockwork Twintania", 100), (2, "Clockwork Twintania", 100)]));
    }

    [Fact]
    public void StageSixteenReservesCritterDefenseWhileMammetsAttackStones()
    {
        var strategy = new VerminionBattleStrategy(16);
        var defense = new VerminionBossStrategy(16);
        Assert.Equal(new ushort[] { 2, 83 }, VerminionRoster.Required(16).Select(minion => minion.Id));
        Assert.Contains("Zu Hatchling", VerminionRoster.Missing(16, id => id == 2));
        Assert.Equal((ushort)2, strategy.CurrentMinion);
        Assert.Equal(6, strategy.GroupSize);
        Assert.Equal(200, defense.AttackCapacity(240, 0));
        defense.Defenders.ObserveUnits([]);
        for (var i = 0; i < 4; ++i)
        {
            Assert.True(defense.CanRequestDefender(0, 180, 240));
            defense.Defenders.Requested();
        }
        Assert.False(defense.CanRequestDefender(0, 180, 240));
        Assert.Equal(4, defense.Defenders.ObserveUnits([1, 2, 3, 4]));
        Assert.Equal(240, defense.AttackCapacity(240, 4));
        // With one guard and one attacker lost from a full army, an attacker
        // request must not spend the ten points reserved for the missing guard.
        strategy.Reinforcements.ObserveUnits([]);
        strategy.Reinforcements.Requested();
        Assert.False(strategy.Reinforcements.CanRequest(220, defense.AttackCapacity(240, 3), strategy.MinionCost));
        Assert.True(defense.CanRequestDefender(3, 220 + strategy.Reinforcements.Pending * strategy.MinionCost, 240));
    }

    [Fact]
    public void StageTwentyReservesHatchlingDefenseWithoutDisplacingStoneAttackers()
    {
        var strategy = new VerminionBattleStrategy(20);
        var defense = new VerminionBossStrategy(20);
        Assert.Equal((ushort)2, strategy.CurrentMinion);
        Assert.Equal(6, strategy.GroupSize);
        Assert.Equal(10, strategy.MinionCost);
        Assert.Equal((ushort)3, defense.CurrentDefenderMinion);
        Assert.Equal(15, defense.CurrentDefenderCost);
        Assert.Equal(180, defense.AttackCapacity(240, 0));
        Assert.Equal(new ushort[] { 2, 3 }, VerminionRoster.Required(20).Select(minion => minion.Id));
        for (var lane = 0; lane < 3; ++lane)
        {
            for (var count = 0; count < 6; ++count)
            {
                Assert.Equal(VerminionBattleStrategy.Action.Summon,
                    strategy.Decide(true, count, lane * 60 + count * 10, defense.AttackCapacity(240, 0)));
                strategy.Dispatched(VerminionBattleStrategy.Action.Summon);
            }
            Assert.Equal(VerminionBattleStrategy.Action.Wait, strategy.Decide(true, 5, (lane + 1) * 60, 240));
            Assert.Equal(VerminionBattleStrategy.Action.AttackObjective, strategy.Decide(true, 6, (lane + 1) * 60, 240));
            strategy.Dispatched(VerminionBattleStrategy.Action.AttackObjective);
        }
        Assert.True(strategy.OpeningComplete);
        strategy.Reinforcements.ObserveUnits([]);
        Assert.False(strategy.Reinforcements.CanRequest(180, defense.AttackCapacity(240, 0), strategy.MinionCost));
        defense.Defenders.ObserveUnits([]);
        for (var i = 0; i < 4; ++i)
        {
            Assert.True(defense.CanRequestDefender(0, 180, 240));
            defense.Defenders.Requested();
        }
        Assert.False(defense.CanRequestDefender(0, 180, 240));
        Assert.Equal(4, defense.Defenders.ObserveUnits([1, 2, 3, 4]));
        Assert.Equal(240, defense.AttackCapacity(240, 4));
        // Two lost attackers and one lost guard leave 35 points, but 15 stay
        // reserved for the guard even before its replacement request is sent.
        strategy.Reinforcements.Requested();
        strategy.Reinforcements.Requested();
        Assert.False(strategy.Reinforcements.CanRequest(205, defense.AttackCapacity(240, 3), strategy.MinionCost));
        Assert.True(defense.CanRequestDefender(3, 205 + strategy.Reinforcements.Pending * strategy.MinionCost, 240));
        Assert.Equal(VerminionRoster.HatchlingOffer, VerminionRoster.VendorOffer(6005, defense.CurrentDefenderMinion));
    }

    [Fact]
    public void VerminionGoalsDoNotConflateModesOrCopyCharacterFacts()
    {
        var week = new System.DateTime(2026, 9, 22, 9, 0, 0, System.DateTimeKind.Utc);
        var character = new CharacterConfig { VerminionPaused = true };
        var progress = character.VerminionProgress;
        progress.ObserveParticipation(week, 5);
        Assert.True(progress.WeeklyGoalReached(VerminionMode.Participation, 2, week));
        Assert.False(progress.WeeklyGoalReached(VerminionMode.MissionRepeat, 2, week));
        Assert.False(progress.WeeklyGoalReached(VerminionMode.CpuRewards, 2, week));
        Assert.False(progress.WeeklyGoalReached(VerminionMode.Participation, 2, week.AddDays(7)));
        progress.EnsureRun(VerminionMode.MissionRepeat, 2);
        progress.RecordResult(progress.BeginMatch(553), VerminionBattleOutcome.Victory, week);
        Assert.False(progress.WeeklyGoalReached(VerminionMode.MissionRepeat, 2, week));
        progress.RecordResult(progress.BeginMatch(553), VerminionBattleOutcome.Victory, week);
        Assert.True(progress.WeeklyGoalReached(VerminionMode.MissionRepeat, 2, week));
        Assert.False(progress.WeeklyGoalReached(VerminionMode.MissionRepeat, 3, week));
        Assert.False(progress.WeeklyGoalReached(VerminionMode.CpuRewards, 2, week));

        var defaults = new CharacterConfig
        {
            EnableVerminionQueue = true, VerminionMode = VerminionMode.MissionRepeat,
            VerminionMission = 7, VerminionRepeatCount = 3, VerminionGilPurchaseCap = 100,
            VerminionMgpPurchaseCap = 200,
        };
        character.CopyVerminionSettingsFrom(defaults);
        Assert.True(character.EnableVerminionQueue);
        Assert.Equal(VerminionMode.MissionRepeat, character.VerminionMode);
        Assert.Equal(3, character.VerminionRepeatCount);
        Assert.Equal(7, character.VerminionMission);
        Assert.Equal(100u, character.VerminionGilPurchaseCap);
        Assert.Equal(200u, character.VerminionMgpPurchaseCap);
        Assert.True(character.VerminionPaused);
        Assert.Same(progress, character.VerminionProgress);
        Assert.Equal(2, progress.WeeklyWins);
        Assert.Equal(0, defaults.VerminionProgress.WeeklyWins);
        Assert.True(progress.ObserveAvailableChallenges(3));
        Assert.Equal(3u, progress.ClearedChallengeMask);
        Assert.False(progress.ObserveAvailableChallenges(25));
        Assert.True(progress.ObserveAvailableChallenges(24));
        Assert.Equal((1u << 23) - 1, progress.ClearedChallengeMask);
        Assert.True(progress.RecordChallengeClear(24));
        Assert.Equal(25, progress.NextUnclearedChallenge);

        var campaign = new VerminionProgress { CampaignRequested = true };
        campaign.ObserveParticipation(week, 5);
        Assert.False(campaign.WeeklyGoalReached(VerminionMode.Participation, 2, week));
        campaign.ObserveAvailableChallenges(3);
        Assert.Equal(3, campaign.NextUnclearedChallenge);
        for (var attempt = 0; attempt < 3; ++attempt)
        {
            campaign.BeginMatch(554);
            campaign.AbandonMatch();
            campaign = JsonConvert.DeserializeObject<VerminionProgress>(JsonConvert.SerializeObject(campaign))!;
        }
        Assert.True(campaign.CampaignStageLimitReached(3));
        Assert.False(campaign.CampaignStageLimitReached(4));
        Assert.Equal(3, campaign.NextUnclearedChallenge);
        Assert.Equal(0, campaign.WeeklyWins);
        campaign.RecordChallengeClear(3);
        Assert.False(campaign.CampaignStageLimitReached(3)); // Closure failure cannot pause an already verified clear.
    }

    [Fact]
    public void ResultsRequireOwnedAdmissionAndRemainDistinctAcrossReloadCancellationAndReset()
    {
        var week = new System.DateTime(2026, 9, 22, 9, 0, 0, System.DateTimeKind.Utc);
        var config = JsonConvert.DeserializeObject<CharacterConfig>("{\"EnableVerminionQueue\":true}")!;
        Assert.Equal(VerminionMode.Participation, config.VerminionMode);
        Assert.False(config.VerminionProgress.CanSpend(1, 0, config.VerminionGilPurchaseCap, config.VerminionMgpPurchaseCap));
        var progress = config.VerminionProgress;
        progress.ObserveParticipation(week, 3); // Matches played manually.
        progress.EnsureRun(VerminionMode.MissionRepeat, 2);
        var match = progress.BeginMatch(553);
        Assert.False(progress.RecordResult(match, VerminionBattleOutcome.Unknown, week));
        Assert.Equal(0, progress.WeeklyWins);
        config = JsonConvert.DeserializeObject<CharacterConfig>(JsonConvert.SerializeObject(config))!;
        progress = config.VerminionProgress;
        Assert.True(progress.RecordResult(match, VerminionBattleOutcome.Defeat, week));
        Assert.False(progress.RecordResult(match, VerminionBattleOutcome.Victory, week));
        Assert.Equal(4, progress.WeeklyMatches);
        Assert.Equal(0u, progress.ClearedChallengeMask);
        Assert.Equal(1, progress.Remaining(VerminionMode.Participation, 5));
        Assert.Equal(2, progress.Remaining(VerminionMode.MissionRepeat, 2));

        match = progress.BeginMatch(553);
        progress.AbandonMatch(); // FULL STOP or an unresolved exit.
        Assert.False(progress.RecordResult(match, VerminionBattleOutcome.Victory, week));
        match = progress.BeginMatch(553);
        progress.ObserveParticipation(week, 5); // The game can refresh before the result window.
        Assert.True(progress.RecordResult(match, VerminionBattleOutcome.Victory, week));
        Assert.Equal(5, progress.WeeklyMatches);
        Assert.Equal(2u, progress.ClearedChallengeMask); // Stage 2 only.
        Assert.Equal(0, progress.Remaining(VerminionMode.Participation, 5));
        Assert.Equal(1, progress.Remaining(VerminionMode.MissionRepeat, 2));
        match = progress.BeginMatch(553);
        Assert.True(progress.RecordResult(match, VerminionBattleOutcome.Victory, week));
        Assert.Equal(0, progress.Remaining(VerminionMode.MissionRepeat, 2));

        var cloned = config.Clone();
        cloned.VerminionProgress.WeeklyWins = 99;
        Assert.Equal(2, progress.WeeklyWins);
        progress.GilSpent = 2400;
        Assert.True(progress.CanSpend(100, 0, 2500, 0));
        Assert.False(progress.CanSpend(101, 0, 2500, 0));
        Assert.False(progress.CanSpend(0, 1, 2500, 0));
        progress.ObserveWeek(week.AddDays(7));
        Assert.False(progress.ObserveParticipation(week, 5));
        Assert.Equal(0, progress.WeeklyWins);
        Assert.Equal(0, progress.WeeklyMatches);
        Assert.Equal(2u, progress.ClearedChallengeMask);
        Assert.Equal(2400ul, progress.GilSpent);

        progress.EnsureRun(VerminionMode.MissionRepeat, 2, restart: true);
        Assert.Equal(6, progress.RunAttemptLimit);
        for (var loss = 0; loss < 3; ++loss)
        {
            progress.RecordResult(progress.BeginMatch(553), VerminionBattleOutcome.Defeat, week.AddDays(7));
            progress = JsonConvert.DeserializeObject<VerminionProgress>(JsonConvert.SerializeObject(progress))!;
            progress.EnsureRun(VerminionMode.MissionRepeat, 2);
        }
        Assert.Equal(3, progress.RunAttempts);
        Assert.True(progress.WinningRunLimitReached);
        Assert.Equal(2, progress.Remaining(VerminionMode.MissionRepeat, 2));
        progress.EnsureRun(VerminionMode.MissionRepeat, 2, restart: true);
        for (var unresolved = 0; unresolved < 6; ++unresolved)
        {
            progress.BeginMatch(553);
            progress.AbandonMatch();
        }
        Assert.True(progress.WinningRunLimitReached);
        progress.EnsureRun(VerminionMode.Participation, 2);
        Assert.False(progress.WinningRunLimitReached);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnavailableTournamentEntryFinishesOnlyRemainingParticipationAndDoesNotSuppressFutureInspection(bool exhausted)
    {
        var week = new DateTime(2026, 9, 22, 9, 0, 0, DateTimeKind.Utc);
        var unavailableThisRun = exhausted
            ? VerminionTournamentEntryRules.AllowanceExhausted("Tournament",
                VerminionTournamentInfo.FromDisplay("Tournament", "Matches available until tomorrow", true, 15, 8, 800))
            : VerminionTournamentEntryRules.Menu("What will you do?",
                ["Ask about the upcoming tournament.", "Ask about Lord of Verminion tournaments.", "Nothing."]) == VerminionTournamentMenu.Upcoming;
        Assert.True(unavailableThisRun);
        var progress = new VerminionProgress();
        progress.ObserveParticipation(week, 4); // Includes manual matches.
        progress.WeeklyWins = 2;
        progress.RecordChallengeClear(2);
        progress.EnsureRun(VerminionMode.CpuRewards, 5);
        Assert.False(progress.WeeklyGoalReached(VerminionMode.CpuRewards, 5, week, tournamentEntryUnavailableThisRun: unavailableThisRun));
        var cancelled = progress.BeginMatch(553);
        progress.AbandonMatch();
        Assert.False(progress.RecordResult(cancelled, VerminionBattleOutcome.Defeat, week));
        Assert.Equal(1, progress.Remaining(VerminionMode.CpuRewards, 5));
        var match = progress.BeginMatch(553);
        progress = JsonConvert.DeserializeObject<VerminionProgress>(JsonConvert.SerializeObject(progress))!;
        Assert.False(progress.RecordResult(match, VerminionBattleOutcome.Unknown, week));
        Assert.True(progress.RecordResult(match, VerminionBattleOutcome.Defeat, week));
        Assert.False(progress.RecordResult(match, VerminionBattleOutcome.Defeat, week));
        Assert.Equal(5, progress.WeeklyMatches);
        Assert.Equal(2, progress.WeeklyWins);
        Assert.True(progress.WeeklyGoalReached(VerminionMode.CpuRewards, 5, week, tournamentEntryUnavailableThisRun: unavailableThisRun));
        // Scheduling and a later run still inspect independent tournament/reward availability.
        Assert.False(progress.WeeklyGoalReached(VerminionMode.CpuRewards, 5, week));
        Assert.False(progress.WeeklyGoalReached(VerminionMode.CpuRewards, 5, week.AddDays(7), tournamentEntryUnavailableThisRun: unavailableThisRun));
        Assert.Equal(0, new VerminionProgress().WeeklyMatches);
    }

    [Fact]
    public void TournamentEntryRejectsUnownedPromptsClosedPeriodsAndNonCpuSelections()
    {
        var upcoming = new[] { "Ask about the upcoming tournament.", "Ask about Lord of Verminion tournaments.", "Nothing." };
        var current = new[] { "Enter the current tournament.", "Ask about the current tournament.", "Ask about Lord of Verminion tournaments.", "Nothing." };
        Assert.Equal(VerminionTournamentMenu.Upcoming, VerminionTournamentEntryRules.Menu("What will you do?", upcoming));
        Assert.Equal(VerminionTournamentMenu.Current, VerminionTournamentEntryRules.Menu("What will you do?", current));
        Assert.Equal(VerminionTournamentMenu.Unknown, VerminionTournamentEntryRules.Menu("Another event", current));
        Assert.Equal(VerminionTournamentMenu.Unknown, VerminionTournamentEntryRules.Menu("What will you do?", ["Enter the current tournament.", "Purchase", "Nothing."]));
        Assert.False(VerminionTournamentEntryRules.CanConfirm("Enter the current tournament?", false, false));
        Assert.False(VerminionTournamentEntryRules.CanConfirm("Enter the current tournament?", true, true));
        Assert.False(VerminionTournamentEntryRules.CanConfirm("Accept your prize?", true, false));
        Assert.False(VerminionTournamentEntryRules.CanConfirm("You will be granted another opportunity to accept the Returner's Bounty upon your next login. Proceed?", true, false));
        Assert.True(VerminionTournamentEntryRules.CanConfirm("Enter the current tournament?", true, false));
        Assert.Equal(VerminionTournamentDialogue.Registered, VerminionTournamentEntryRules.Dialogue(
            "There we are, TEST! You are now officially registered for the 821st Lord of Verminion Tournament. Best of luck!"));
        Assert.Equal(VerminionTournamentDialogue.Registered, VerminionTournamentEntryRules.Dialogue("...Oh. You seem to have registered for this tournament already."));
        Assert.Equal(VerminionTournamentDialogue.Unknown, VerminionTournamentEntryRules.Dialogue("You have registered for a match. Please remain within Chocobo Square until the match begins."));
        Assert.Equal(VerminionTournamentDialogue.EntriesClosed, VerminionTournamentEntryRules.Dialogue("I'm terribly sorry, sir, but we are no longer accepting entries for this tournament. Not to worry, though! There'll be another competition starting soon!"));
        Assert.Equal(VerminionTournamentDialogue.Unknown, VerminionTournamentEntryRules.Dialogue("I'm terribly sorry, sir, but we are no longer accepting entries for this tournament. Not to worry, though!"));
        Assert.Equal(VerminionTournamentDialogue.Rejected, VerminionTournamentEntryRules.Dialogue("My apologies, sir, but I cannot register your entry at this time."));
        Assert.Equal(VerminionTournamentDialogue.Rejected, VerminionTournamentEntryRules.Dialogue("I'm afraid you don't meet the prerequisite..."));
        var open = VerminionTournamentInfo.FromDisplay("Tournament", "Matches available until tomorrow", true, 14, 8, 800)!;
        Assert.False(VerminionTournamentEntryRules.CanPrepare(null, open));
        Assert.True(VerminionTournamentEntryRules.CanPrepare("Tournament", open));
        Assert.False(VerminionTournamentEntryRules.CanPrepare("Tournament", open with { Matches = 15 }));
        Assert.False(VerminionTournamentEntryRules.CanPrepare("Tournament", open with { Matches = null }));
        Assert.False(VerminionTournamentEntryRules.CanPrepare("Tournament", open with { Notice = "Matches begin at tomorrow" }));
        Assert.True(VerminionTournamentEntryRules.AllowanceExhausted("Tournament", open with { Matches = 15 }));
        Assert.False(VerminionTournamentEntryRules.AllowanceExhausted(null, open with { Matches = 15 }));
        Assert.False(VerminionTournamentEntryRules.AllowanceExhausted("Tournament", open));
        Assert.False(VerminionTournamentEntryRules.AllowanceExhausted("Tournament", null));
        Assert.False(VerminionTournamentEntryRules.AllowanceExhausted("Tournament", open with { Matches = null }));
        Assert.False(VerminionTournamentEntryRules.AllowanceExhausted("Tournament", open with { Matches = 15, Notice = "Matches begin at tomorrow" }));
        Assert.False(VerminionTournamentEntryRules.OnlyCpuSelection(579, []));
        Assert.False(VerminionTournamentEntryRules.OnlyCpuSelection(579, [198])); // Player tournament.
        Assert.False(VerminionTournamentEntryRules.OnlyCpuSelection(579, [579, 553]));
        Assert.False(VerminionTournamentEntryRules.OnlyCpuSelection(553, [579]));
        Assert.True(VerminionTournamentEntryRules.OnlyCpuSelection(579, [579]));
    }

    [Fact]
    public void TournamentRegistrationCannotAuthorizeAnotherPeriodsEntryOrAllowanceExhaustion()
    {
        const string previousTitle = "The 820th Lord of Verminion Tournament";
        const string currentTitle = "The 821st Lord of Verminion Tournament";
        const string registered = "There we are, TEST! You are now officially registered for the 820th Lord of Verminion Tournament. Best of luck!";
        const string alreadyRegistered = "...Oh. You seem to have registered for this tournament already.";
        var previous = new VerminionTournamentInfo(previousTitle, "Matches available until tomorrow", 14, 8, 800);
        Assert.True(VerminionTournamentEntryRules.ConfirmsRegistration(registered, previousTitle));
        Assert.False(VerminionTournamentEntryRules.ConfirmsRegistration(registered, currentTitle));
        Assert.False(VerminionTournamentEntryRules.ConfirmsRegistration(registered, null));
        Assert.False(VerminionTournamentEntryRules.ConfirmsRegistration(registered, "Tournament"));
        Assert.False(VerminionTournamentEntryRules.ConfirmsRegistration(
            "You are now officially registered for the 820th Lord of Verminion Tournament.", previousTitle));
        Assert.True(VerminionTournamentEntryRules.CanPrepare(previousTitle, previous));
        Assert.True(VerminionTournamentEntryRules.AllowanceExhausted(previousTitle, previous with { Matches = 15 }));

        // A fresh display can change periods while the owned dialogue closes.
        // Even identical counters cannot transfer registration to the next title.
        var current = previous with { Title = currentTitle };
        Assert.False(VerminionTournamentEntryRules.CanPrepare(previousTitle, current));
        Assert.False(VerminionTournamentEntryRules.AllowanceExhausted(previousTitle, current with { Matches = 15 }));
        Assert.False(VerminionTournamentEntryRules.CanPrepare(previousTitle,
            previous with { Notice = "Matches begin at tomorrow" }));
        Assert.False(VerminionTournamentEntryRules.AllowanceExhausted(previousTitle,
            previous with { Notice = "Matches begin at tomorrow", Matches = 15 }));

        // The already-registered response is usable only with a known fresh title;
        // its subsequent refresh must still refer to that same period.
        Assert.False(VerminionTournamentEntryRules.ConfirmsRegistration(alreadyRegistered, null));
        Assert.False(VerminionTournamentEntryRules.ConfirmsRegistration(alreadyRegistered, ""));
        Assert.True(VerminionTournamentEntryRules.ConfirmsRegistration(alreadyRegistered, currentTitle));
        Assert.True(VerminionTournamentEntryRules.CanPrepare(currentTitle, current));
        Assert.False(VerminionTournamentEntryRules.CanPrepare(currentTitle, current with { Matches = null }));
        Assert.False(VerminionTournamentEntryRules.AllowanceExhausted(currentTitle, current with { Matches = null }));
        Assert.False(VerminionTournamentEntryRules.CanPrepare(null, current)); // A reload requires a new observation.
        Assert.False(VerminionTournamentEntryRules.CanPrepare("", current));
        Assert.False(VerminionTournamentEntryRules.AllowanceExhausted("", current with { Matches = 15 }));
    }

    [Fact]
    public void CpuQueueOwnershipAndTournamentResultsKeepDutyIdentityAcrossReloadAndCancellation()
    {
        foreach (var duty in new uint[] { 552, 553, 575, 576, 577, 578, 579 })
        {
            Assert.True(VerminionDutyRules.OnlyExpectedQueue(duty, [0, duty, 0], 0));
            Assert.True(VerminionDutyRules.OnlyExpectedQueue(duty, [], duty));
            Assert.True(VerminionDutyRules.OnlyExpectedQueue(duty, [duty], duty));
            Assert.False(VerminionDutyRules.OnlyExpectedQueue(duty, [duty, 198], 0));
            Assert.False(VerminionDutyRules.OnlyExpectedQueue(duty, [duty], 198));
            Assert.False(VerminionDutyRules.OnlyExpectedQueue(duty, [uint.MaxValue], duty));
            Assert.False(VerminionDutyRules.OnlyExpectedQueue(duty, [duty], uint.MaxValue));
            Assert.False(VerminionDutyRules.OnlyExpectedQueue(duty, [0, 0], 0));
        }
        Assert.False(VerminionDutyRules.OnlyExpectedQueue(198, [198], 198)); // Player tournament.
        Assert.False(VerminionDutyRules.OnlyExpectedQueue(0, [], 0));
        Assert.False(VerminionDutyRules.OnlyExpectedQueue(579, [553], 0));
        Assert.Equal(1, VerminionDutyRules.ChallengeStage(552));
        Assert.Equal(24, VerminionDutyRules.ChallengeStage(575));
        Assert.Equal(0, VerminionDutyRules.ChallengeStage(579));
        Assert.Equal(0, VerminionDutyRules.ChallengeStage(198));

        var week = new DateTime(2026, 9, 22, 9, 0, 0, DateTimeKind.Utc);
        var progress = new VerminionProgress { CampaignRequested = true, CampaignStage = 24, CampaignStageAttempts = 2 };
        progress.ObserveParticipation(week, 4);
        progress.RecordChallengeClear(2);
        var cancelled = progress.BeginMatch(579);
        Assert.Equal(0, progress.BeginMatch(553)); // Never replace an owned tournament admission.
        progress.AbandonMatch();
        Assert.False(progress.RecordResult(cancelled, VerminionBattleOutcome.Victory, week));
        var admitted = progress.BeginMatch(579);
        progress = JsonConvert.DeserializeObject<VerminionProgress>(JsonConvert.SerializeObject(progress))!;
        Assert.Equal(579u, progress.PendingDuty);
        Assert.Equal(admitted, progress.PendingMatch);
        Assert.Equal(2, progress.CampaignStageAttempts); // A tournament is not Stage 28.
        Assert.False(progress.RecordResult(admitted, VerminionBattleOutcome.Unknown, week));
        Assert.False(progress.RecordTutorialCompletion(admitted, VerminionBattleOutcome.Victory));
        Assert.True(progress.RecordResult(admitted, VerminionBattleOutcome.Victory, week));
        Assert.False(progress.RecordResult(admitted, VerminionBattleOutcome.Victory, week));
        Assert.Equal(5, progress.WeeklyMatches);
        Assert.Equal(1, progress.WeeklyWins);
        Assert.Equal(2u, progress.ClearedChallengeMask);
        Assert.Null(progress.LastTournamentInfo); // A result cannot invent native tournament counters.
        Assert.Equal(0, new VerminionProgress().WeeklyMatches);
    }

    [Fact]
    public void SavedTournamentDisplaySurvivesReloadWithoutAuthorizingEntryOrCreditingProgress()
    {
        var week = new DateTime(2026, 9, 22, 9, 0, 0, DateTimeKind.Utc);
        var observed = week.AddDays(6);
        var character = new CharacterConfig { VerminionMode = VerminionMode.CpuRewards };
        var progress = character.VerminionProgress;
        progress.ObserveParticipation(week, 5);
        progress.WeeklyWins = 2;
        progress.RecordChallengeClear(2);
        progress.LastTournamentInfo = VerminionTournamentInfo.FromDisplay("Previous tournament",
            "Matches available until tomorrow", true, 14, 8, 800);
        progress.TournamentObservedUtc = observed;
        foreach (var restored in new[]
        {
            JsonConvert.DeserializeObject<CharacterConfig>(JsonConvert.SerializeObject(character))!,
            System.Text.Json.JsonSerializer.Deserialize<CharacterConfig>(System.Text.Json.JsonSerializer.Serialize(character))!,
        })
        {
            var saved = restored.VerminionProgress;
            Assert.Equal(progress.LastTournamentInfo, saved.LastTournamentInfo);
            Assert.Equal(observed, saved.TournamentObservedUtc);
            Assert.Equal(5, saved.WeeklyMatches);
            Assert.Equal(2, saved.WeeklyWins);
            Assert.Equal(2u, saved.ClearedChallengeMask);
            Assert.Equal(0, saved.PendingMatch);
            Assert.False(saved.WeeklyGoalReached(VerminionMode.CpuRewards, 5, week));
            Assert.False(VerminionTournamentEntryRules.CanPrepare(null, saved.LastTournamentInfo));
            saved.ObserveWeek(week.AddDays(7));
            Assert.Equal(progress.LastTournamentInfo, saved.LastTournamentInfo); // Tournament periods are independent.
            Assert.Equal(observed, saved.TournamentObservedUtc);
            Assert.Equal(0, saved.WeeklyMatches);
            Assert.Equal(0, saved.WeeklyWins);
        }
        var copy = progress.Clone();
        copy.LastTournamentInfo = VerminionTournamentInfo.FromDisplay("Next tournament",
            "Matches begin at tomorrow", false, 14, 8, 800);
        copy.TournamentObservedUtc = observed.AddHours(1);
        Assert.Null(copy.LastTournamentInfo!.Matches); // Do not carry the previous period's allowance forward.
        Assert.Null(copy.LastTournamentInfo.Wins);
        Assert.Null(copy.LastTournamentInfo.Points);
        Assert.Equal(14, progress.LastTournamentInfo!.Matches);
        Assert.Equal(observed, progress.TournamentObservedUtc);
        Assert.Null(new CharacterConfig().VerminionProgress.LastTournamentInfo);
        Assert.Equal(default, new CharacterConfig().VerminionProgress.TournamentObservedUtc);
    }

    [Fact]
    public void TournamentDisplayDoesNotTurnHiddenCountersIntoAnAllowanceOrBattleCredit()
    {
        var hidden = VerminionTournamentInfo.FromDisplay("The 820th Lord of Verminion Tournament",
            "Matches available until 10:45 a.m. 9/28/2026", false, 0, 0, 0)!;
        Assert.Null(hidden.Matches);
        Assert.Null(hidden.Wins);
        Assert.Null(hidden.Points);
        Assert.Null(VerminionTournamentInfo.FromDisplay("Receiving data...", "Waiting", true, 0, 0, 0));
        Assert.Null(VerminionTournamentInfo.FromDisplay("Prior tournament", "Old notice", true, 15, 10, 500, receivingData: true));
        Assert.Null(VerminionTournamentInfo.FromDisplay("Tournament", "Notice", true, 16, 0, 0));
        Assert.Null(VerminionTournamentInfo.FromDisplay("Tournament", "Notice", true, 5, 6, 100));
        Assert.Null(VerminionTournamentInfo.FromDisplay("Tournament", "Notice", true, 5, 2, -1));
        var shown = VerminionTournamentInfo.FromDisplay("Tournament", "Notice", true, 5, 2, 100)!;
        Assert.Equal(5, shown.Matches);
        Assert.Equal(2, shown.Wins);
        Assert.Equal(100, shown.Points);
        Assert.Null(VerminionTournamentInfo.FromDisplay("Tournament", "Notice", false, 99, 99, 99)!.Matches);
    }

    [Fact]
    public void MatchAcrossWeeklyResetCreditsTheNewWeekOnceAfterReload()
    {
        var priorWeek = new System.DateTime(2026, 9, 22, 9, 0, 0, System.DateTimeKind.Utc);
        var newWeek = priorWeek.AddDays(7);
        var progress = new VerminionProgress { GilSpent = 2400, CampaignStage = 23, CampaignStageAttempts = 3 };
        progress.ObserveParticipation(priorWeek, 4);
        progress.RecordChallengeClear(1);
        progress.EnsureRun(VerminionMode.MissionRepeat, 2);
        var match = progress.BeginMatch(553);

        // The game updates participation first; the result is still pending
        // when the plugin reloads across the weekly boundary.
        Assert.True(progress.ObserveParticipation(newWeek, 1));
        progress = JsonConvert.DeserializeObject<VerminionProgress>(JsonConvert.SerializeObject(progress))!;
        progress.EnsureRun(VerminionMode.MissionRepeat, 2);
        Assert.Equal(1, progress.RunAttempts);
        Assert.False(progress.RecordResult(match, VerminionBattleOutcome.Unknown, newWeek));
        Assert.False(progress.RecordResult(match, VerminionBattleOutcome.Victory, priorWeek));
        Assert.Equal(match, progress.PendingMatch);
        Assert.Equal(0, progress.WeeklyWins);

        Assert.True(progress.RecordResult(match, VerminionBattleOutcome.Victory, newWeek));
        progress = JsonConvert.DeserializeObject<VerminionProgress>(JsonConvert.SerializeObject(progress))!;
        Assert.False(progress.RecordResult(match, VerminionBattleOutcome.Victory, newWeek));
        Assert.Equal(1, progress.WeeklyMatches); // Already included by the game's refresh.
        Assert.Equal(1, progress.WeeklyWins);
        Assert.Equal(1, progress.Remaining(VerminionMode.MissionRepeat, 2));
        Assert.Equal(3u, progress.ClearedChallengeMask);
        Assert.Equal(2400ul, progress.GilSpent);
        Assert.Equal(3, progress.CampaignStageAttempts);

        // A later abandoned admission cannot add a victory after another reload.
        var cancelled = progress.BeginMatch(553);
        progress.AbandonMatch();
        progress = JsonConvert.DeserializeObject<VerminionProgress>(JsonConvert.SerializeObject(progress))!;
        Assert.False(progress.RecordResult(cancelled, VerminionBattleOutcome.Victory, newWeek));
        Assert.Equal(1, progress.WeeklyMatches);
        Assert.Equal(1, progress.WeeklyWins);
    }

    [Fact]
    public void FullStopPauseSurvivesReloadAndCloneWithoutPausingAnotherCharacter()
    {
        var stopped = new CharacterConfig { EnableVerminionQueue = true, VerminionPaused = true };
        var reloaded = JsonConvert.DeserializeObject<CharacterConfig>(JsonConvert.SerializeObject(stopped))!;
        var cloned = reloaded.Clone();
        var otherCharacter = JsonConvert.DeserializeObject<CharacterConfig>("{\"EnableVerminionQueue\":true}")!;

        Assert.True(reloaded.VerminionPaused);
        Assert.True(cloned.VerminionPaused);
        Assert.False(otherCharacter.VerminionPaused);
        cloned.VerminionPaused = false;
        Assert.True(reloaded.VerminionPaused);
        reloaded.ResetVerminionState();
        Assert.True(reloaded.VerminionPaused);
    }

    [Fact]
    public void EntryPurchasesPreferOwnedMinionsAndStayWithinOneCumulativeBudgetAcrossReload()
    {
        Assert.Empty(VerminionRoster.EntryPurchases([26, 52, 99]));
        Assert.Equal(new ushort[] { 2 }, VerminionRoster.EntryPurchases([26, 52]).Select(offer => offer.MinionId));
        Assert.Equal(new ushort[] { 3 }, VerminionRoster.EntryPurchases([2, 52, 2, 0]).Select(offer => offer.MinionId));
        Assert.Equal(new ushort[] { 1 }, VerminionRoster.EntryPurchases([2, 3]).Select(offer => offer.MinionId));
        var registered = new System.Collections.Generic.List<ushort>();
        var progress = new VerminionProgress();
        var fullPlan = VerminionRoster.EntryPurchases(registered);
        var total = (uint)fullPlan.Sum(offer => (long)offer.Gil);
        Assert.Equal(7200u, total);
        Assert.False(progress.CanSpend(total, 0, 0, 0));
        Assert.False(progress.CanSpend(total, 0, 7199, 0));
        Assert.True(progress.CanSpend(total, 0, 7200, 0));
        uint gil = 7200;
        foreach (var expected in fullPlan)
        {
            var remaining = VerminionRoster.EntryPurchases(registered);
            Assert.Equal(expected, remaining[0]);
            Assert.True(progress.CanSpend((uint)remaining.Sum(offer => (long)offer.Gil), 0, 7200, 0));
            Assert.True(progress.ReservePurchase(expected.ItemId, expected.MinionId, expected.Gil, 0,
                gil, 0, 0, false, 7200, 0));
            progress = JsonConvert.DeserializeObject<VerminionProgress>(JsonConvert.SerializeObject(progress))!;
            Assert.False(progress.CanSpend(expected.Gil, 0, 7200, 0)); // Reload cannot submit the reservation again.
            gil -= expected.Gil;
            Assert.True(progress.ConfirmPurchase(gil, 0, 1, false));
            registered.Add(expected.MinionId); // Registration must be verified before continuing.
            Assert.DoesNotContain(VerminionRoster.EntryPurchases(registered), offer => offer.MinionId == expected.MinionId);
        }
        Assert.Empty(VerminionRoster.EntryPurchases(registered));
        Assert.Equal(7200ul, progress.GilSpent);
        Assert.Equal(0u, gil);
        Assert.Null(progress.PendingPurchase);
        Assert.Equal(0, progress.WeeklyMatches);
        Assert.Equal(0, progress.WeeklyWins);
    }

    [Fact]
    public void PurchasesPreserveTheConfiguredGilBalanceAcrossReloadAndSettingsCopies()
    {
        var character = new CharacterConfig { VerminionGilPurchaseCap = 999999999, VerminionGilReserve = 50000 };
        character = JsonConvert.DeserializeObject<CharacterConfig>(JsonConvert.SerializeObject(character))!;
        Assert.Equal(50000u, character.Clone().VerminionGilReserve);
        var other = new CharacterConfig();
        other.CopyVerminionSettingsFrom(character);
        Assert.Equal(50000u, other.VerminionGilReserve);
        Assert.Equal(0ul, other.VerminionProgress.GilSpent);
        var progress = character.VerminionProgress;
        Assert.False(progress.ReservePurchase(6004, 2, 2400, 0, 52399, 0, 0, false, character.VerminionGilPurchaseCap, 0, character.VerminionGilReserve));
        Assert.Null(progress.PendingPurchase);
        Assert.False(VerminionProgress.PreservesGilReserve(49999, 2400, 50000));
        Assert.False(VerminionProgress.PreservesGilReserve(uint.MaxValue, 1, uint.MaxValue));
        Assert.True(VerminionProgress.PreservesGilReserve(0, 0, 50000)); // MGP purchases do not spend gil.
        Assert.True(progress.ReservePurchase(6004, 2, 2400, 0, 52400, 0, 0, false, character.VerminionGilPurchaseCap, 0, character.VerminionGilReserve));
        var pending = progress.PendingPurchase!;
        Assert.False(VerminionProgress.PreservesGilReserve(52400, pending.Gil, 50001)); // Reserve raised before confirmation.
        Assert.False(VerminionProgress.PreservesGilReserve(52399, pending.Gil, 50000)); // Funds changed before confirmation.
        Assert.True(progress.ConfirmPurchase(50000, 0, 1, false));
        Assert.Equal(2400ul, progress.GilSpent);
        Assert.False(progress.ReservePurchase(6003, 1, 2400, 0, 50000, 0, 0, false, character.VerminionGilPurchaseCap, 0, character.VerminionGilReserve));
        Assert.Equal(0, progress.WeeklyMatches);
        Assert.Equal(0, progress.WeeklyWins);
    }

    [Fact]
    public void ReservedPurchaseRecoveryNeedsUnchangedEvidenceAndRetainsItsBudgetReservation()
    {
        var progress = new VerminionProgress();
        Assert.True(progress.ReservePurchase(6005, 3, 2400, 0, 10000, 20000, 0, false, 2400, 0, 5000));
        progress.MinionAcquisition = new("reserved-recovery", 6005, 3, true);
        progress = System.Text.Json.JsonSerializer.Deserialize<VerminionProgress>(System.Text.Json.JsonSerializer.Serialize(progress))!;
        var reservation = progress.PendingPurchase;
        Assert.True(progress.CanResumeReservedPurchase(10000, 20000, 0, false, 6, 2400, 0, 0, 5000));
        Assert.False(progress.CanSpend(2400, 0, 4800, 0));
        Assert.False(progress.CanResumeReservedPurchase(7600, 20000, 0, false, 6, 2400, 0, 0, 5000));
        Assert.False(progress.CanResumeReservedPurchase(10000, 20001, 0, false, 6, 2400, 0, 0, 5000));
        Assert.False(progress.CanResumeReservedPurchase(10000, 20000, 1, false, 6, 2400, 0, 0, 5000));
        Assert.False(progress.CanResumeReservedPurchase(10000, 20000, 0, true, 6, 2400, 0, 0, 5000));
        Assert.False(progress.CanResumeReservedPurchase(10000, 20000, 0, null, 6, 2400, 0, 0, 5000));
        Assert.False(progress.CanResumeReservedPurchase(10000, 20000, 0, false, 6, 2399, 0, 0, 5000));
        Assert.False(progress.CanResumeReservedPurchase(10000, 20000, 0, false, 6, 2400, 0, 0, 7601));
        Assert.Equal(reservation, progress.PendingPurchase);
        Assert.Equal(0ul, progress.GilSpent);
        Assert.Equal(0, progress.WeeklyMatches);
        progress.MinionAcquisition = progress.MinionAcquisition! with { ItemId = 6004 };
        Assert.False(progress.CanResumeReservedPurchase(10000, 20000, 0, false, 6, 2400, 0, 0, 5000));
        progress.MinionAcquisition = new("reserved-recovery", 6005, 3, false);
        Assert.True(progress.CanResumeReservedPurchase(10000, 20000, 0, false, 6, 2400, 0, 0, 5000));
        progress.MinionAcquisition = progress.MinionAcquisition with { DispatchAttempted = true };
        Assert.True(progress.ConfirmPurchase(7600, 20000, 1, false));
        Assert.Equal(2400ul, progress.GilSpent);
        Assert.False(progress.CanResumeReservedPurchase(7600, 20000, 1, false, 6, 4800, 0, 0, 5000));
        Assert.False(progress.ConfirmPurchase(7600, 20000, 1, false));
    }

    [Fact]
    public void PurchaseReservationSurvivesStopReloadAndResetAndNeedsBothReceiptFacts()
    {
        var character = new CharacterConfig { VerminionGilPurchaseCap = 2400 };
        var progress = character.VerminionProgress;
        Assert.False(progress.ReservePurchase(6004, 2, 2400, 0, 5000, 0, 0, false, 0, 0));
        Assert.False(progress.ReservePurchase(6004, 2, 2400, 0, 2399, 0, 0, false, 2400, 0));
        Assert.False(progress.ReservePurchase(6004, 2, 2400, 0, 5000, 0, 1, false, 2400, 0));
        Assert.False(progress.ReservePurchase(6004, 2, 2400, 0, 5000, 0, 0, true, 2400, 0));
        Assert.True(progress.ReservePurchase(6004, 2, 2400, 0, 5000, 0, 0, false, 2400, 0));
        progress.MinionAcquisition = new("ads-minion-test", 6004, 2, true);
        Assert.Equal(0ul, progress.GilSpent); // Submission is not a receipt.
        Assert.False(progress.CanSpend(1, 0, 10000, 0));
        Assert.False(progress.ReservePurchase(6187, 26, 2400, 0, 5000, 0, 0, false, 10000, 0));
        character.VerminionPaused = true;
        character.ResetVerminionState();
        character = System.Text.Json.JsonSerializer.Deserialize<CharacterConfig>(System.Text.Json.JsonSerializer.Serialize(character))!;
        progress = character.VerminionProgress;
        progress.ObserveWeek(new System.DateTime(2026, 9, 29, 9, 0, 0, System.DateTimeKind.Utc));
        Assert.True(character.VerminionPaused);
        Assert.NotNull(progress.PendingPurchase);
        Assert.Equal(new VerminionMinionAcquisition("ads-minion-test", 6004, 2, true), progress.MinionAcquisition);
        Assert.False(progress.ConfirmPurchase(5000, 0, 0, false)); // No receipt; no repeated purchase.
        Assert.False(progress.ConfirmPurchase(2600, 0, 0, false)); // Currency alone.
        Assert.False(progress.ConfirmPurchase(5000, 0, 1, false)); // Item alone.
        Assert.False(progress.ConfirmPurchase(2500, 0, 1, false)); // Unknown additional spending.
        var clone = character.Clone();
        clone.VerminionProgress.MinionAcquisition = null;
        Assert.NotNull(progress.MinionAcquisition);
        Assert.Null(new CharacterConfig().VerminionProgress.MinionAcquisition);
        Assert.True(clone.VerminionProgress.ConfirmPurchase(2600, 0, 0, true)); // Registered during interruption.
        Assert.NotNull(progress.PendingPurchase); // Character clones cannot clear each other's reservation.
        Assert.True(progress.ConfirmPurchase(2600, 0, 1, false));
        Assert.Equal(2400ul, progress.GilSpent);
        Assert.Null(progress.PendingPurchase);
        Assert.False(progress.ConfirmPurchase(2600, 0, 1, false)); // Exactly once.
        Assert.False(progress.CanSpend(1, 0, 2400, 0));
        Assert.Equal(0, progress.WeeklyMatches);
        Assert.Equal(0, progress.WeeklyWins);
        Assert.Equal(0ul, new CharacterConfig().VerminionProgress.GilSpent);

        var mgp = new VerminionProgress();
        var offer = VerminionRoster.NeroOffer;
        Assert.Equal(offer, VerminionRoster.VendorOffer(14096, 174));
        Assert.DoesNotContain(offer, VerminionRoster.EntryPurchases([]));
        Assert.False(mgp.ReservePurchase(offer.ItemId, offer.MinionId, offer.Gil, offer.Mgp, 50000, 60000, 0, false, 0, 0, 50000));
        Assert.False(mgp.ReservePurchase(offer.ItemId, offer.MinionId, offer.Gil, offer.Mgp, 50000, 29999, 0, false, 0, 30000, 50000));
        Assert.True(mgp.ReservePurchase(offer.ItemId, offer.MinionId, offer.Gil, offer.Mgp, 50000, 60000, 0, false, 0, 30000, 50000));
        mgp = JsonConvert.DeserializeObject<VerminionProgress>(JsonConvert.SerializeObject(mgp))!;
        Assert.False(mgp.ConfirmPurchase(50000, 60000, 1, false));
        Assert.False(mgp.ConfirmPurchase(49999, 30000, 1, false));
        Assert.True(mgp.ConfirmPurchase(50000, 30000, 1, false));
        Assert.False(mgp.CanSpend(0, 1, 0, 30000));
        Assert.Equal(30000ul, mgp.MgpSpent);
        Assert.Equal(0ul, mgp.GilSpent);
        var zu = VerminionRoster.ZuOffer;
        Assert.Equal(zu, VerminionRoster.VendorOffer(7565, 83));
        Assert.DoesNotContain(zu, VerminionRoster.EntryPurchases([]));
        Assert.False(mgp.ReservePurchase(zu.ItemId, zu.MinionId, 0, zu.Mgp, 50000, 30000, 0, false, 0, 39999, 50000));
        Assert.True(mgp.ReservePurchase(zu.ItemId, zu.MinionId, 0, zu.Mgp, 50000, 30000, 0, false, 0, 40000, 50000));
        Assert.True(mgp.ConfirmPurchase(50000, 20000, 1, false));
        Assert.Equal(40000ul, mgp.MgpSpent);
    }
}
