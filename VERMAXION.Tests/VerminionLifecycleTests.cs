using Newtonsoft.Json;
using VERMAXION.Models;
using VERMAXION.Services;
using Xunit;

namespace VERMAXION.Tests;

public sealed class VerminionLifecycleTests
{
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

    [Fact]
    public void BossDeploymentBoundsQueueAndKeepsSurvivorsFollowingAtCapacity()
    {
        Assert.True(VerminionBossStrategy.IsInvulnerabilityAdd(6, "Infant Imp"));
        Assert.True(VerminionBossStrategy.IsInvulnerabilityAdd(6, "Imp"));
        Assert.False(VerminionBossStrategy.IsInvulnerabilityAdd(6, "Smallshell"));
        Assert.False(VerminionBossStrategy.IsInvulnerabilityAdd(4, "Imp"));
        var strategy = new VerminionBossStrategy();
        strategy.ObserveUnits([100]); // Reload baseline cannot confirm a new request.
        Assert.Equal(VerminionBossStrategy.Action.SelectGate, strategy.Decide(false, 0, 0, 0, 60, 0, 100, true));
        for (var request = 0; request < 6; ++request)
        {
            // Used capacity can lag queued summons; bound requests independently.
            Assert.Equal(VerminionBossStrategy.Action.Summon, strategy.Decide(true, 0, 0, 0, 60, 0, 100, true));
            strategy.Dispatched(VerminionBossStrategy.Action.Summon);
        }
        Assert.Equal(VerminionBossStrategy.Action.Wait, strategy.Decide(true, 0, 0, 0, 60, 10, 100, true));
        Assert.Equal(VerminionBossStrategy.Action.SendWave, strategy.Decide(true, 6, 0, 60, 60, 30, 100, true));
        strategy.Dispatched(VerminionBossStrategy.Action.SendWave);
        Assert.Equal(6, strategy.PendingSummons); // Moving a group does not prove spawns.
        Assert.Equal(0, strategy.ObserveUnits([100]));
        Assert.Equal(3, strategy.ObserveUnits([100, 101, 102, 103]));
        Assert.Equal(0, strategy.ObserveUnits([101, 102, 103])); // Same IDs cannot confirm twice.
        Assert.Equal(3, strategy.PendingSummons);
        // Prior units may already be fighting or dead; newly observed IDs free the
        // request budget without needing six minions waiting at the gate together.
        Assert.Equal(3, strategy.ObserveUnits([104, 105, 106]));
        Assert.Equal(0, strategy.PendingSummons);
        Assert.Equal(VerminionBossStrategy.Action.Summon, strategy.Decide(true, 0, 2, 20, 240, 90, 30, true));
        Assert.Equal(VerminionBossStrategy.Action.FollowBoss, strategy.Decide(true, 0, 6, 240, 240, 10, 10, true));
        Assert.Equal(VerminionBossStrategy.Action.Wait, strategy.Decide(true, 0, 6, 240, 240, 10, 5, true));
        Assert.Equal(VerminionBossStrategy.Action.Wait, strategy.Decide(true, 0, 6, 240, 240, 10, 20, false));
        Assert.Equal(VerminionBossStrategy.Action.Summon, strategy.Decide(true, 0, 6, 200, 240, 10, 45, false));
        Assert.Equal(VerminionBossStrategy.Action.FollowBoss, strategy.Decide(true, 0, 6, 240, 240, 10, 40, false));
        Assert.Equal(VerminionBossStrategy.Action.Wait, strategy.Decide(true, 0, 0, 55, 60, 10, 100, true));
        Assert.Equal(VerminionBossStrategy.Action.SendWave, strategy.Decide(true, 3, 0, 30, 60, 60, 100, true));

        var defended = new VerminionBossStrategy();
        defended.ObserveUnits([1, 2, 3, 4, 5, 6]);
        defended.Defenders.ObserveUnits([]);
        Assert.Equal(60, defended.AttackCapacity(60, 0)); // Initial six attackers remain possible.
        for (var guard = 0; guard < 4; ++guard)
        {
            Assert.True(defended.CanRequestDefender(0, 60, 240));
            defended.Defenders.Requested();
        }
        Assert.False(defended.CanRequestDefender(0, 60, 240));
        for (var attacker = 0; attacker < 6; ++attacker)
        {
            Assert.Equal(VerminionBossStrategy.Action.Summon,
                defended.Decide(true, 0, 6, 60, defended.AttackCapacity(240, 0), 0, 0, false));
            defended.Dispatched(VerminionBossStrategy.Action.Summon);
        }
        Assert.Equal(VerminionBossStrategy.Action.Wait,
            defended.Decide(true, 0, 6, 60, defended.AttackCapacity(240, 0), 0, 0, false));
        Assert.Equal(4, defended.Defenders.ObserveUnits([7, 8, 9, 10]));
        Assert.Equal(6, defended.ObserveUnits([11, 12, 13, 14, 15, 16]));
        Assert.False(defended.CanRequestDefender(4, 240, 240));
        Assert.Equal(VerminionBossStrategy.Action.Wait,
            defended.Decide(true, 0, 12, 210, defended.AttackCapacity(240, 3), 0, 0, false));
        Assert.True(defended.CanRequestDefender(3, 210, 240)); // The casualty's capacity stays reserved for its replacement.

        var brick = new VerminionBossStrategy(12);
        brick.ObserveUnits([]);
        brick.Defenders.ObserveUnits([]);
        for (var request = 0; request < 2; ++request)
        {
            Assert.Equal(VerminionBossStrategy.Action.Summon, brick.Decide(true, 0, 0, 0, 60, 0, 0, false));
            brick.Dispatched(VerminionBossStrategy.Action.Summon);
        }
        Assert.Equal(VerminionBossStrategy.Action.Wait, brick.Decide(true, 0, 0, 0, 60, 0, 0, false));
        Assert.False(brick.CanRequestDefender(0, 160, 240)); // 60 already reserved for attackers.
        Assert.Equal(VerminionBossStrategy.Action.FollowBoss, brick.Decide(false, 0, 4, 120, 240, 0, 10, true, false));
        Assert.Equal(VerminionBossStrategy.Action.Wait, brick.Decide(false, 0, 0, 0, 240, 0, 100, false, false));
        Assert.Equal(VerminionBossStrategy.Action.SendWave, brick.Decide(false, 4, 0, 120, 240, 0, 0, false, false));

        var gilgamesh = new VerminionBossStrategy(19);
        gilgamesh.ObserveUnits([]);
        for (var request = 0; request < 3; ++request)
        {
            Assert.Equal(VerminionBossStrategy.Action.Summon, gilgamesh.Decide(true, 0, 0, 0, 60, 0, 0, false));
            gilgamesh.Dispatched(VerminionBossStrategy.Action.Summon);
        }
        Assert.Equal(VerminionBossStrategy.Action.Wait, gilgamesh.Decide(true, 0, 0, 0, 60, 0, 0, false));
        Assert.Equal(3, gilgamesh.ObserveUnits([1, 2, 3]));
        Assert.Equal(VerminionBossStrategy.Action.Summon, gilgamesh.Decide(true, 3, 0, 60, 240, 0, 0, false));
        Assert.Equal(VerminionBossStrategy.Action.SendWave, gilgamesh.Decide(true, 4, 0, 80, 240, 0, 0, false));

        var odin = new VerminionBossStrategy(15);
        odin.ObserveUnits([]);
        odin.Defenders.ObserveUnits([]);
        odin.Support.ObserveUnits([]);
        for (var request = 0; request < 4; ++request)
        {
            Assert.Equal(VerminionBossStrategy.Action.Summon,
                odin.Decide(true, 0, 0, 0, odin.AttackCapacity(240, 0), 0, 0, false));
            odin.Dispatched(VerminionBossStrategy.Action.Summon);
        }
        Assert.Equal(VerminionBossStrategy.Action.Wait,
            odin.Decide(true, 0, 0, 0, odin.AttackCapacity(240, 0), 0, 0, false));
        for (var request = 0; request < 4; ++request)
        {
            Assert.True(odin.CanRequestDefender(0, 0, 240));
            odin.Defenders.Requested();
        }
        Assert.False(odin.CanRequestDefender(0, 0, 240));
        for (var request = 0; request < 4; ++request)
        {
            Assert.True(odin.CanRequestSupport(0, 0, 240));
            odin.Support.Requested();
        }
        Assert.False(odin.CanRequestSupport(0, 0, 240));
        Assert.False(odin.CanRequestDefender(0, 0, 240));
        Assert.Equal(4, odin.Support.ObserveUnits([21, 22, 23, 24]));
        Assert.False(odin.CanRequestSupport(4, 40, 240));
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
}
