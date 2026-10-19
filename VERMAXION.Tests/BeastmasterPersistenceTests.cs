using Newtonsoft.Json;
using System;
using VERMAXION.Beastmaster;
using VERMAXION.Models;
using Xunit;

namespace VERMAXION.Tests;

public sealed class BeastmasterPersistenceTests
{
    [Fact]
    public void DefaultSettingsPreserveLiveObservationsAndTheActiveRunSnapshot()
    {
        var observedAt = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
        var character = new CharacterConfig();
        var liveData = character.Beastmaster;
        var familiar = new CrucibleFamiliar(13) { Rank = 7, Exp = "15/20", ObservedAtUtc = observedAt };
        liveData.Familiars[13] = familiar;
        liveData.Owned.Add(13);
        liveData.Team.Add(13);
        liveData.OwnershipObservedAtUtc = liveData.RanksObservedAtUtc = observedAt;
        var manual = liveData.GetSettings(new BeastmasterSettings { Board = 2, CustomTeam = [13] });
        var activeRun = liveData.ScheduledSetup!.ApplyTo(manual);
        var defaults = new CharacterConfig { EnableBeastmaster = true, EnableBeastmasterWeekly = true };
        defaults.Beastmaster.GetSettings(new BeastmasterSettings { Board = 4, CustomTeam = [20] });
        defaults.Beastmaster.ScheduledSetup!.Board = 5;
        defaults.Beastmaster.Owned.Add(20);

        character.CopyBeastmasterSettingsFrom(defaults);

        Assert.Same(liveData, character.Beastmaster);
        Assert.Same(familiar, character.Beastmaster.Familiars[13]);
        Assert.Equal(new uint[] { 13 }, character.Beastmaster.Owned);
        Assert.Equal(new uint[] { 13 }, character.Beastmaster.Team);
        Assert.Equal(observedAt, character.Beastmaster.OwnershipObservedAtUtc);
        Assert.Equal(observedAt, character.Beastmaster.RanksObservedAtUtc);
        Assert.True(character.EnableBeastmaster);
        Assert.True(character.EnableBeastmasterWeekly);
        Assert.Equal(4u, liveData.ManualSettings!.Board);
        Assert.Equal(5u, liveData.ScheduledSetup!.Board);
        Assert.Equal(2u, activeRun.Board);
        Assert.Equal(new uint[] { 13 }, activeRun.CustomTeam);

        liveData.ManualSettings.CustomTeam.Add(27);
        liveData.ScheduledSetup.Board = 3;
        Assert.Equal(new uint[] { 20 }, defaults.Beastmaster.ManualSettings!.CustomTeam);
        Assert.Equal(5u, defaults.Beastmaster.ScheduledSetup.Board);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefaultCadenceChangesResetOnlyTheAffectedTask(bool weekly)
    {
        var completed = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
        var next = completed.AddDays(7);
        var character = new CharacterConfig
        {
            BeastmasterLastCompleted = completed, BeastmasterNextReset = next,
            BeastmasterWeeklyLastCompleted = completed, BeastmasterWeeklyNextReset = next,
        };
        var defaults = new CharacterConfig();
        character.CopyBeastmasterSettingsFrom(defaults);
        Assert.Equal(completed, character.BeastmasterLastCompleted);
        Assert.Equal(next, character.BeastmasterNextReset);
        Assert.Equal(completed, character.BeastmasterWeeklyLastCompleted);
        Assert.Equal(next, character.BeastmasterWeeklyNextReset);

        if (weekly) defaults.BeastmasterWeeklyFrequency = BeastmasterTaskFrequency.Monthly;
        else defaults.BeastmasterFrequency = BeastmasterTaskFrequency.Monthly;
        character.CopyBeastmasterSettingsFrom(defaults);

        Assert.Equal(weekly ? completed : DateTime.MinValue, character.BeastmasterLastCompleted);
        Assert.Equal(weekly ? next : DateTime.MinValue, character.BeastmasterNextReset);
        Assert.Equal(weekly ? DateTime.MinValue : completed, character.BeastmasterWeeklyLastCompleted);
        Assert.Equal(weekly ? DateTime.MinValue : next, character.BeastmasterWeeklyNextReset);
    }

    [Fact]
    public void ResetAllTaskStateClearsBothCadencesAndKeepsBeastObservationsAndSettings()
    {
        var observedAt = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
        var character = new CharacterConfig
        {
            EnableBeastmaster = true, EnableBeastmasterWeekly = true,
            BeastmasterFrequency = BeastmasterTaskFrequency.Monthly,
            BeastmasterLastCompleted = observedAt, BeastmasterNextReset = observedAt.AddDays(14),
            BeastmasterWeeklyLastCompleted = observedAt, BeastmasterWeeklyNextReset = observedAt.AddDays(7),
        };
        var data = character.Beastmaster;
        var settings = data.GetSettings(new BeastmasterSettings { Board = 4 });
        var setup = data.ScheduledSetup;
        data.Owned.Add(13);
        data.Familiars[13] = new CrucibleFamiliar(13) { Rank = 7, Exp = "15/20", ObservedAtUtc = observedAt };
        data.OwnershipObservedAtUtc = data.RanksObservedAtUtc = observedAt;

        character.ResetAllTaskState();

        Assert.Equal(DateTime.MinValue, character.BeastmasterLastCompleted);
        Assert.Equal(DateTime.MinValue, character.BeastmasterNextReset);
        Assert.Equal(DateTime.MinValue, character.BeastmasterWeeklyLastCompleted);
        Assert.Equal(DateTime.MinValue, character.BeastmasterWeeklyNextReset);
        Assert.True(character.EnableBeastmaster);
        Assert.True(character.EnableBeastmasterWeekly);
        Assert.Equal(BeastmasterTaskFrequency.Monthly, character.BeastmasterFrequency);
        Assert.Same(data, character.Beastmaster);
        Assert.Same(settings, data.ManualSettings);
        Assert.Same(setup, data.ScheduledSetup);
        Assert.Equal(new uint[] { 13 }, data.Owned);
        Assert.Equal(7, data.Familiars[13].Rank);
        Assert.Equal(observedAt, data.OwnershipObservedAtUtc);
        Assert.Equal(observedAt, data.RanksObservedAtUtc);
    }

    [Fact]
    public void LegacySettingsMigrateOnceAndManualEditsDoNotChangeTheScheduledRun()
    {
        var legacy = new BeastmasterSettings { Board = 2, RunCount = 3, CustomTeam = [13, 20, 27] };
        var data = new BeastmasterCharacterData();
        var manual = data.GetSettings(legacy);
        manual.Board = 4;
        manual.CustomTeam.Add(19);
        data.ScheduledSetup!.RunCount = 2;
        var scheduled = data.ScheduledSetup.ApplyTo(manual);

        Assert.Equal(2u, scheduled.Board);
        Assert.Equal(2, scheduled.RunCount);
        Assert.Equal(4u, manual.Board);
        Assert.Equal(3, manual.RunCount);
        Assert.Equal(new uint[] { 13, 20, 27, 19 }, scheduled.CustomTeam);
        Assert.Equal(new uint[] { 13, 20, 27 }, legacy.CustomTeam);
        Assert.Same(manual, data.GetSettings(new BeastmasterSettings { Board = 5 }));

        var restored = System.Text.Json.JsonSerializer.Deserialize<BeastmasterCharacterData>(
            System.Text.Json.JsonSerializer.Serialize(data))!;
        Assert.Equal(4u, restored.ManualSettings!.Board);
        Assert.Equal(2u, restored.ScheduledSetup!.Board);
        Assert.Equal(2, restored.ScheduledSetup.RunCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacyTrainingTargetsFollowTheirBoardAndManualEditsSurviveReopening(bool useNewtonsoft)
    {
        for (uint board = 1; board <= 5; board++)
        {
            var json = "{\"Board\":" + board + "}";
            var legacy = useNewtonsoft ? JsonConvert.DeserializeObject<BeastmasterSettings>(json)!
                : System.Text.Json.JsonSerializer.Deserialize<BeastmasterSettings>(json)!;
            Assert.Equal((int)board * 5, BeastmasterSelection.TrainingTarget(legacy));
        }

        var data = new BeastmasterCharacterData();
        var manual = data.GetSettings(new BeastmasterSettings { Board = 2 });
        manual.TrainingTargetRank = 7;
        var activeRun = manual.Clone();
        var otherCharacter = new BeastmasterCharacterData();
        Assert.Equal(10, BeastmasterSelection.TrainingTarget(otherCharacter.GetSettings(new BeastmasterSettings { Board = 2 })));
        var restored = useNewtonsoft
            ? JsonConvert.DeserializeObject<BeastmasterCharacterData>(JsonConvert.SerializeObject(data))!
            : System.Text.Json.JsonSerializer.Deserialize<BeastmasterCharacterData>(System.Text.Json.JsonSerializer.Serialize(data))!;
        Assert.Equal(7, BeastmasterSelection.TrainingTarget(restored.GetSettings(new BeastmasterSettings())));
        Assert.Equal(10, BeastmasterSelection.TrainingTarget(restored.ScheduledSetup!.ApplyTo(restored.ManualSettings!)));

        manual.TrainingTargetRank = 12;
        Assert.Equal(7, BeastmasterSelection.TrainingTarget(activeRun));
        data.ScheduledSetup!.Board = 4;
        var scheduledRun = data.ScheduledSetup.ApplyTo(manual);
        Assert.Equal(20, BeastmasterSelection.TrainingTarget(scheduledRun));
        Assert.Equal(12, BeastmasterSelection.TrainingTarget(manual));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WeeklyBoardPreferencesPersistNullEmptyAndExplicitChoices(bool useNewtonsoft)
    {
        const string legacyJson = "{\"AutoExitDuty\":false,\"RunCount\":2}";
        var legacy = useNewtonsoft ? JsonConvert.DeserializeObject<BeastmasterSettings>(legacyJson)!
            : System.Text.Json.JsonSerializer.Deserialize<BeastmasterSettings>(legacyJson)!;
        Assert.Equal(2, legacy.RunCount);
        Assert.Null(legacy.WeeklyBoards);
        Assert.Empty(legacy.CompletionCommand);
        BeastmasterCharacterData RoundTrip(BeastmasterCharacterData data) => useNewtonsoft
            ? JsonConvert.DeserializeObject<BeastmasterCharacterData>(JsonConvert.SerializeObject(data))!
            : System.Text.Json.JsonSerializer.Deserialize<BeastmasterCharacterData>(System.Text.Json.JsonSerializer.Serialize(data))!;
        var data = new BeastmasterCharacterData();
        var settings = data.GetSettings(new BeastmasterSettings());
        Assert.Null(RoundTrip(data).ManualSettings!.WeeklyBoards);

        settings.WeeklyBoards = [];
        Assert.Empty(RoundTrip(data).ManualSettings!.WeeklyBoards!);
        settings.WeeklyBoards = [1, 3];
        settings.CompletionCommand = "/vmx off";
        var restored = RoundTrip(data);
        Assert.True(restored.ManualSettings!.WeeklyBoards!.SetEquals([1u, 3u]));
        Assert.Equal("/vmx off", restored.ManualSettings.CompletionCommand);
        Assert.True(restored.ScheduledSetup!.ApplyTo(restored.ManualSettings).WeeklyBoards!.SetEquals([1u, 3u]));
    }

    [Fact]
    public void WeeklyChoicesClonePerCharacterAndDoNotShareTheActiveRunSnapshot()
    {
        var first = new BeastmasterCharacterData();
        var settings = first.GetSettings(new BeastmasterSettings { WeeklyBoards = [1, 2] });
        var active = first.ScheduledSetup!.ApplyTo(settings);
        var second = first.Clone();
        second.ManualSettings!.WeeklyBoards!.Remove(2);
        settings.WeeklyBoards!.Add(3);

        Assert.True(second.ManualSettings.WeeklyBoards.SetEquals([1u]));
        Assert.True(settings.WeeklyBoards.SetEquals([1u, 2u, 3u]));
        Assert.True(active.WeeklyBoards!.SetEquals([1u, 2u]));
        Assert.Null(new BeastmasterCharacterData().GetSettings(new BeastmasterSettings()).WeeklyBoards);
    }

    [Fact]
    public void OnlyObservedBeastsQualifyAndObservationsCloneWithoutSharingMutableState()
    {
        var data = new BeastmasterCharacterData();
        data.EnsureFamiliars([13, 20, 27]);
        Assert.False(data.HasRecordedBeasts);
        data.Owned.Add(13);
        data.OwnershipObservedAtUtc = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(data.HasRecordedBeasts);
        Assert.Equal(0, data.Familiars[13].Rank); // Owned is valid even before its rank is read.

        var clone = data.Clone();
        clone.Owned.Add(20);
        clone.Familiars[13].Rank = 5;
        Assert.DoesNotContain(20u, data.Owned);
        Assert.Equal(0, data.Familiars[13].Rank);
        Assert.Equal(data.OwnershipObservedAtUtc, clone.OwnershipObservedAtUtc);
    }

    [Fact]
    public void BeastmasterSettingsAndCharacterDataKeepTheirSeparatePersistenceBoundaries()
    {
        var defaults = new Configuration();
        Assert.Equal(1u, defaults.Beastmaster.Board);
        Assert.Equal(CrucibleTeamMode.Recommended, defaults.Beastmaster.TeamMode);
        Assert.Equal(CrucibleLevelingMode.Minus_3, defaults.Beastmaster.LevelingMode);
        Assert.Equal(15, defaults.Beastmaster.RecommendedTeamCap);

        var tier = new BeastTierList { Name = "Training" };
        tier.Add(new BeastTierList.Entry { ID = 13, PickBlock = true });
        tier.Add(new BeastTierList.Entry { ID = 20, BattleBlock = true });
        defaults.Beastmaster.Board = 5;
        defaults.Beastmaster.RunCount = 3;
        defaults.Beastmaster.CustomTeam = [13, 20];
        defaults.Beastmaster.BeastTierLists = [tier];
        defaults.Beastmaster.ShopLists = [new BeastmasterShopList
        {
            Name = "Supplies",
            Order = [new BeastmasterShopList.Entry { ID = 140, Blocked = true }],
        }];

        var restored = JsonConvert.DeserializeObject<Configuration>(JsonConvert.SerializeObject(defaults))!;
        Assert.Equal(5u, restored.Beastmaster.Board);
        Assert.Equal(3, restored.Beastmaster.RunCount);
        Assert.Equal(new uint[] { 13, 20 }, restored.Beastmaster.CustomTeam);
        var restoredTier = Assert.Single(restored.Beastmaster.BeastTierLists);
        Assert.Equal("Training", restoredTier.Name);
        Assert.True(restoredTier[0].PickBlock);
        Assert.True(restoredTier[1].BattleBlock);
        Assert.True(Assert.Single(Assert.Single(restored.Beastmaster.ShopLists).Order).Blocked);

        var character = CharacterConfig.CreateNew();
        character.Beastmaster.Familiars[13] = new CrucibleFamiliar(13) { Rank = 7, Hp = 250, Exp = "15/20" };
        character.Beastmaster.Team.Add(13);
        var clone = character.Clone();
        clone.Beastmaster.Familiars[13].Hp = 1;
        clone.Beastmaster.Team.Clear();
        Assert.Equal(250, character.Beastmaster.Familiars[13].Hp);
        Assert.Equal(13u, Assert.Single(character.Beastmaster.Team));

        var savedCharacter = System.Text.Json.JsonSerializer.Serialize(character);
        var restoredCharacter = System.Text.Json.JsonSerializer.Deserialize<CharacterConfig>(savedCharacter)!;
        Assert.Equal(13u, Assert.Single(restoredCharacter.Beastmaster.Team));
        Assert.Equal(13u, restoredCharacter.Beastmaster.Familiars[13].Number);
        Assert.Equal(7, restoredCharacter.Beastmaster.Familiars[13].Rank);
        Assert.Equal("15/20", restoredCharacter.Beastmaster.Familiars[13].Exp);
    }
}
