using System.Linq;
using Newtonsoft.Json;
using VERMAXION.Models;
using VERMAXION.Services;
using Xunit;

namespace VERMAXION.Tests;

public sealed class VerminionLifecycleTests
{
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
    [InlineData(23)]
    [InlineData(24)]
    public void FinalBossReinforcementsDoNotWaitForAnImpossibleFullWave(int stage)
    {
        var strategy = new VerminionBossStrategy(stage);
        strategy.ObserveUnits([]);
        Assert.Equal((ushort)21, strategy.CurrentMinion);
        Assert.Equal(30, strategy.MinionCost);
        Assert.Equal(8, strategy.ArmySize);
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
    public void BossDeploymentBoundsQueueAndKeepsSurvivorsFollowingAtCapacity()
    {
        Assert.True(VerminionBossStrategy.IsInvulnerabilityAdd(6, "Infant Imp"));
        Assert.True(VerminionBossStrategy.IsInvulnerabilityAdd(6, "Imp"));
        Assert.False(VerminionBossStrategy.IsInvulnerabilityAdd(6, "Smallshell"));
        Assert.False(VerminionBossStrategy.IsInvulnerabilityAdd(4, "Imp"));
        foreach (var stage in new[] { 4, 6, 9, 12, 19 })
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
        for (var wave = 0; wave < 4; ++wave)
        {
            for (var request = 0; request < 6; ++request)
            {
                Assert.Equal(VerminionBossStrategy.Action.Summon,
                    odin.Decide(true, wave * 6, 0, wave * 60, 240, 90, 90, true, assemble: true));
                odin.Dispatched(VerminionBossStrategy.Action.Summon);
            }
            Assert.Equal(VerminionBossStrategy.Action.Wait,
                odin.Decide(true, wave * 6, 0, wave * 60, 240, 90, 90, true, assemble: true));
            Assert.Equal(6, odin.ObserveUnits(Enumerable.Range(wave * 6 + 1, 6).Select(id => (ulong)id)));
        }
        Assert.Equal(VerminionBossStrategy.Action.SendWave, odin.Decide(true, 24, 0, 240, 240, 90, 90, true));
        Assert.Equal(VerminionBossStrategy.Action.Summon, odin.Decide(true, 0, 23, 230, 240, 0, 0, false));
    }

    [Fact]
    public void AccessibleRosterChecksOwnershipBeforeAdmissionWithoutGatingIntentionalLosses()
    {
        Assert.Empty(VerminionRoster.Required(1));
        for (var stage = 2; stage <= 24; ++stage)
        {
            var roster = VerminionRoster.Required(stage);
            Assert.NotEmpty(roster);
            Assert.All(roster, minion => Assert.Contains(minion.Id, new ushort[] { 2, 3, 21, 26, 52 }));
            Assert.Null(VerminionRoster.Missing(stage, _ => true));
            Assert.Contains("ownership is unavailable", VerminionRoster.Missing(stage, _ => null));
            Assert.Contains(roster[0].Name, VerminionRoster.Missing(stage, _ => false));
            Assert.Contains(roster[0].Acquisition, VerminionRoster.Missing(stage, _ => false));
        }
        Assert.Contains("Baby Bat", VerminionRoster.Missing(6, id => id == 52));
        Assert.Null(VerminionRoster.Missing(19, id => id == 52));
        Assert.True(VerminionRoster.NeedsBattleRoster(2, false, VerminionMode.Participation, 1));
        Assert.False(VerminionRoster.NeedsBattleRoster(2, false, VerminionMode.Participation, 3));
        Assert.True(VerminionRoster.NeedsBattleRoster(2, false, VerminionMode.WinTarget, 3));
        Assert.True(VerminionRoster.NeedsBattleRoster(19, true, VerminionMode.Participation, 262143));
    }

    [Fact]
    public void TwintaniaGuideRosterBlocksMissingQuestMinionAndPreservesTheLastAttackParty()
    {
        var ownedVendors = new ushort[] { 2, 3, 52, 83, 174 };
        Assert.Contains("Her Last Vow", VerminionRoster.Missing(23, id => ownedVendors.Contains(id)));
        Assert.Null(VerminionRoster.Missing(23, id => id == 21));
        var strategy = new VerminionBossStrategy(23);
        Assert.False(strategy.ShouldUseSpecial(3601, 18000, 8));
        Assert.True(strategy.ShouldUseSpecial(3600, 18000, 8));
        Assert.False(strategy.ShouldUseSpecial(3600, 18000, 7));
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
    public void WeeklyGoalsDoNotConflateModesOrCopyCharacterFacts()
    {
        var week = new System.DateTime(2026, 9, 22, 9, 0, 0, System.DateTimeKind.Utc);
        var character = new CharacterConfig { VerminionPaused = true };
        var progress = character.VerminionProgress;
        progress.ObserveParticipation(week, 5);
        Assert.True(progress.WeeklyGoalReached(VerminionMode.Participation, 2, week));
        Assert.False(progress.WeeklyGoalReached(VerminionMode.WinTarget, 2, week));
        Assert.False(progress.WeeklyGoalReached(VerminionMode.CpuRewards, 2, week));
        Assert.False(progress.WeeklyGoalReached(VerminionMode.Participation, 2, week.AddDays(7)));
        progress.EnsureRun(VerminionMode.WinTarget, 2);
        progress.RecordResult(progress.BeginMatch(553), VerminionBattleOutcome.Victory, week);
        Assert.False(progress.WeeklyGoalReached(VerminionMode.WinTarget, 2, week));
        progress.RecordResult(progress.BeginMatch(553), VerminionBattleOutcome.Victory, week);
        Assert.True(progress.WeeklyGoalReached(VerminionMode.WinTarget, 2, week));
        Assert.False(progress.WeeklyGoalReached(VerminionMode.WinTarget, 3, week));
        Assert.False(progress.WeeklyGoalReached(VerminionMode.CpuRewards, 2, week));

        var defaults = new CharacterConfig
        {
            EnableVerminionQueue = true, VerminionMode = VerminionMode.WinTarget,
            VerminionVictoryTarget = 3, VerminionGilPurchaseCap = 100,
            VerminionMgpPurchaseCap = 200,
        };
        character.CopyVerminionSettingsFrom(defaults);
        Assert.True(character.EnableVerminionQueue);
        Assert.Equal(VerminionMode.WinTarget, character.VerminionMode);
        Assert.Equal(3, character.VerminionVictoryTarget);
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
        Assert.Equal(2, progress.Remaining(VerminionMode.WinTarget, 2));

        match = progress.BeginMatch(553);
        progress.AbandonMatch(); // FULL STOP or an unresolved exit.
        Assert.False(progress.RecordResult(match, VerminionBattleOutcome.Victory, week));
        match = progress.BeginMatch(553);
        progress.ObserveParticipation(week, 5); // The game can refresh before the result window.
        Assert.True(progress.RecordResult(match, VerminionBattleOutcome.Victory, week));
        Assert.Equal(5, progress.WeeklyMatches);
        Assert.Equal(2u, progress.ClearedChallengeMask); // Stage 2 only.
        Assert.Equal(0, progress.Remaining(VerminionMode.Participation, 5));
        Assert.Equal(1, progress.Remaining(VerminionMode.WinTarget, 2));
        match = progress.BeginMatch(553);
        Assert.True(progress.RecordResult(match, VerminionBattleOutcome.Victory, week));
        Assert.Equal(0, progress.Remaining(VerminionMode.WinTarget, 2));

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

        progress.EnsureRun(VerminionMode.WinTarget, 2);
        Assert.Equal(6, progress.RunAttemptLimit);
        for (var loss = 0; loss < 3; ++loss)
        {
            progress.RecordResult(progress.BeginMatch(553), VerminionBattleOutcome.Defeat, week.AddDays(7));
            progress = JsonConvert.DeserializeObject<VerminionProgress>(JsonConvert.SerializeObject(progress))!;
            progress.EnsureRun(VerminionMode.WinTarget, 2);
        }
        Assert.Equal(3, progress.RunAttempts);
        Assert.True(progress.WinningRunLimitReached);
        Assert.Equal(2, progress.Remaining(VerminionMode.WinTarget, 2));
        progress.EnsureRun(VerminionMode.WinTarget, 2, restart: true);
        for (var unresolved = 0; unresolved < 6; ++unresolved)
        {
            progress.BeginMatch(553);
            progress.AbandonMatch();
        }
        Assert.True(progress.WinningRunLimitReached);
        progress.EnsureRun(VerminionMode.Participation, 2);
        Assert.False(progress.WinningRunLimitReached);
    }

    [Fact]
    public void MatchAcrossWeeklyResetCreditsTheNewWeekOnceAfterReload()
    {
        var priorWeek = new System.DateTime(2026, 9, 22, 9, 0, 0, System.DateTimeKind.Utc);
        var newWeek = priorWeek.AddDays(7);
        var progress = new VerminionProgress { GilSpent = 2400, CampaignStage = 23, CampaignStageAttempts = 3 };
        progress.ObserveParticipation(priorWeek, 4);
        progress.RecordChallengeClear(1);
        progress.EnsureRun(VerminionMode.WinTarget, 2);
        var match = progress.BeginMatch(553);

        // The game updates participation first; the result is still pending
        // when the plugin reloads across the weekly boundary.
        Assert.True(progress.ObserveParticipation(newWeek, 1));
        progress = JsonConvert.DeserializeObject<VerminionProgress>(JsonConvert.SerializeObject(progress))!;
        progress.EnsureRun(VerminionMode.WinTarget, 2);
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
        Assert.Equal(1, progress.Remaining(VerminionMode.WinTarget, 2));
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
    public void PurchaseReservationSurvivesStopReloadAndResetAndNeedsBothReceiptFacts()
    {
        var character = new CharacterConfig { VerminionGilPurchaseCap = 2400 };
        var progress = character.VerminionProgress;
        Assert.False(progress.ReservePurchase(6004, 2, 2400, 0, 5000, 0, 0, false, 0, 0));
        Assert.False(progress.ReservePurchase(6004, 2, 2400, 0, 2399, 0, 0, false, 2400, 0));
        Assert.False(progress.ReservePurchase(6004, 2, 2400, 0, 5000, 0, 1, false, 2400, 0));
        Assert.False(progress.ReservePurchase(6004, 2, 2400, 0, 5000, 0, 0, true, 2400, 0));
        Assert.True(progress.ReservePurchase(6004, 2, 2400, 0, 5000, 0, 0, false, 2400, 0));
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
        Assert.False(progress.ConfirmPurchase(5000, 0, 0, false)); // No receipt; no repeated purchase.
        Assert.False(progress.ConfirmPurchase(2600, 0, 0, false)); // Currency alone.
        Assert.False(progress.ConfirmPurchase(5000, 0, 1, false)); // Item alone.
        Assert.False(progress.ConfirmPurchase(2500, 0, 1, false)); // Unknown additional spending.
        var clone = character.Clone();
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
