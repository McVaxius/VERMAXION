using System;
using Newtonsoft.Json;
using VERMAXION.Beastmaster;
using VERMAXION.Models;
using Xunit;

namespace VERMAXION.Tests;

public sealed class BeastmasterRepairTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SuppliesDefaultExtractionOnAndPersistExactFoodQuality(bool useNewtonsoft)
    {
        BeastmasterSettings Read(string text) => useNewtonsoft
            ? JsonConvert.DeserializeObject<BeastmasterSettings>(text)!
            : System.Text.Json.JsonSerializer.Deserialize<BeastmasterSettings>(text)!;
        Assert.True(Read("{}").ExtractMateria);
        Assert.Equal(0u, Read("{}").FoodItemId);
        var selected = Read("{\"FoodItemId\":1001234,\"ExtractMateria\":false}");
        foreach (var copy in new[] { selected.Clone(), BeastmasterRunSetup.From(selected).ApplyTo(selected),
            BeastmasterWeeklyPolicy.CopySettings(selected) })
        {
            Assert.Equal(1001234u, copy.FoodItemId);
            Assert.False(copy.ExtractMateria);
        }
        var serialized = useNewtonsoft ? JsonConvert.SerializeObject(selected)
            : System.Text.Json.JsonSerializer.Serialize(selected);
        Assert.Equal(1001234u, Read(serialized).FoodItemId);
        Assert.False(Read(serialized).ExtractMateria);
    }

    [Theory]
    [InlineData(0u, 0f, false)]
    [InlineData(1234u, 91f, false)]
    [InlineData(1234u, 90f, true)]
    [InlineData(1001234u, 90f, true)]
    [InlineData(1001234u, 0f, true)]
    public void FoodIsOptionalAndRefreshesAtTheAgreedThreshold(uint selected, float seconds, bool expected)
        => Assert.Equal(expected, BeastmasterRepairPolicy.ShouldRefreshFood(selected, seconds));

    [Theory]
    [InlineData(1u)]
    [InlineData(2u)]
    [InlineData(3u)]
    [InlineData(4u)]
    [InlineData(5u)]
    public void CheckboxOnlySetupNeverCarriesFoodOrExtraction(uint board)
    {
        var shared = new BeastmasterSettings { FoodItemId = 1001234, ExtractMateria = true };
        var checkbox = BeastmasterRunSetup.ForBoardTest(shared, board);
        Assert.Equal(0u, checkbox.FoodItemId);
        Assert.False(checkbox.ExtractMateria);
        Assert.Equal(1001234u, shared.FoodItemId);
        Assert.True(shared.ExtractMateria);
    }

    [Fact]
    public void ExtractionCancellationRejectsUnrelatedStaleOrReplacedUtilities()
    {
        var at = new DateTime(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);
        bool Owns(string task = "materia extraction", string mode = "extract-materia",
            bool readable = true, bool running = true, DateTime? complete = null)
            => BeastmasterRepairPolicy.CanCancelOwnedRequest(true, at, "extract-materia", readable,
                running, task, mode, complete);
        Assert.True(Owns());
        Assert.False(Owns(task: "NPC repair"));
        Assert.False(Owns(mode: "self"));
        Assert.False(Owns(readable: false));
        Assert.False(Owns(running: false));
        Assert.False(Owns(complete: at));
        Assert.False(Owns(complete: at.AddSeconds(1)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OlderProfilesDefaultToSelfAtSeventyFiveAndThresholdsAreBounded(bool useNewtonsoft)
    {
        BeastmasterSettings Read(string json) => useNewtonsoft
            ? JsonConvert.DeserializeObject<BeastmasterSettings>(json)!
            : System.Text.Json.JsonSerializer.Deserialize<BeastmasterSettings>(json)!;

        var older = Read("{\"Board\":2}");
        Assert.Equal(BeastmasterRepairMode.Self, older.RepairMode);
        Assert.Equal(75, older.RepairThreshold);
        Assert.Equal(0, Read("{\"RepairThreshold\":-1}").RepairThreshold);
        Assert.Equal(100, Read("{\"RepairThreshold\":101}").RepairThreshold);
        Assert.Equal(BeastmasterRepairMode.Disabled, Read("{\"RepairMode\":0}").RepairMode);
        Assert.Equal(BeastmasterRepairMode.Self, Read("{\"RepairMode\":99}").RepairMode);
        Assert.Equal(0, Read("{\"RepairThreshold\":0}").RepairThreshold);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepairPersistsPerCharacterAndIsSharedWithScheduledAndWeeklySnapshots(bool useNewtonsoft)
    {
        var character = new CharacterConfig();
        var shared = character.Beastmaster.GetSettings(new BeastmasterSettings { Board = 1 });
        shared.RepairMode = BeastmasterRepairMode.NpcNearby;
        shared.RepairThreshold = 40;
        character.Beastmaster.ScheduledSetup!.Board = 4;

        var restored = useNewtonsoft
            ? JsonConvert.DeserializeObject<CharacterConfig>(JsonConvert.SerializeObject(character))!
            : System.Text.Json.JsonSerializer.Deserialize<CharacterConfig>(
                System.Text.Json.JsonSerializer.Serialize(character))!;
        var restoredShared = restored.Beastmaster.GetSettings(new BeastmasterSettings());
        Assert.Equal(BeastmasterRepairMode.NpcNearby, restoredShared.RepairMode);
        Assert.Equal(40, restoredShared.RepairThreshold);

        var manual = restoredShared.Clone();
        var scheduled = restored.Beastmaster.ScheduledSetup!.ApplyTo(restoredShared);
        var weekly = BeastmasterWeeklyPolicy.CopySettings(restoredShared);
        restoredShared.RepairMode = BeastmasterRepairMode.Disabled;
        restoredShared.RepairThreshold = 0;
        foreach (var active in new[] { manual, scheduled, weekly })
        {
            Assert.Equal(BeastmasterRepairMode.NpcNearby, active.RepairMode);
            Assert.Equal(40, active.RepairThreshold);
        }
        Assert.Equal(4u, scheduled.Board);
        Assert.Equal(1, weekly.RunCount);

        var nextScheduled = restored.Beastmaster.ScheduledSetup.ApplyTo(restoredShared);
        Assert.Equal(BeastmasterRepairMode.Disabled, nextScheduled.RepairMode);
        Assert.Equal(0, nextScheduled.RepairThreshold);
        var other = new CharacterConfig().Beastmaster.GetSettings(new BeastmasterSettings());
        Assert.Equal(BeastmasterRepairMode.Self, other.RepairMode);
        Assert.Equal(75, other.RepairThreshold);
    }

    [Fact]
    public void CopyingCharacterDefaultsCopiesRepairWithoutSharingSettingsOrChangingFishingDefaults()
    {
        var defaults = new CharacterConfig();
        defaults.Beastmaster.GetSettings(new BeastmasterSettings
        {
            RepairMode = BeastmasterRepairMode.NpcInn, RepairThreshold = 60,
        });
        var character = new CharacterConfig();
        character.CopyBeastmasterSettingsFrom(defaults);
        var copied = character.Beastmaster.ManualSettings!;
        Assert.Equal(BeastmasterRepairMode.NpcInn, copied.RepairMode);
        Assert.Equal(60, copied.RepairThreshold);
        copied.RepairThreshold = 10;
        Assert.Equal(60, defaults.Beastmaster.ManualSettings!.RepairThreshold);
        Assert.Equal(FishingRepairMode.NpcNoInn, FishingDefaults.RepairMode);
        Assert.Equal(50, FishingDefaults.RepairThresholdPercent);
    }

    [Theory]
    [InlineData(BeastmasterRepairMode.Disabled, "")]
    [InlineData(BeastmasterRepairMode.Self, "self")]
    [InlineData(BeastmasterRepairMode.NpcNoInn, "npc-no-inn")]
    [InlineData(BeastmasterRepairMode.NpcNearby, "npc-no-teleport-no-inn")]
    [InlineData(BeastmasterRepairMode.NpcInn, "npc-yes-inn")]
    [InlineData((BeastmasterRepairMode)99, "")]
    public void SelectedModePreservesAdsTravelRestrictions(BeastmasterRepairMode mode, string expected)
        => Assert.Equal(expected, BeastmasterRepairPolicy.ToAdsMode(mode));

    [Theory]
    [InlineData(BeastmasterRepairMode.Self, 75, 74, true)]
    [InlineData(BeastmasterRepairMode.Self, 75, 75, false)]
    [InlineData(BeastmasterRepairMode.Self, 75, 76, false)]
    [InlineData(BeastmasterRepairMode.Self, 75, 0, true)]
    [InlineData(BeastmasterRepairMode.Self, 75, null, false)]
    [InlineData(BeastmasterRepairMode.Self, 75, -1, false)]
    [InlineData(BeastmasterRepairMode.Self, 0, 0, false)]
    [InlineData(BeastmasterRepairMode.Self, -1, 0, false)]
    [InlineData(BeastmasterRepairMode.Self, 1, 0, true)]
    [InlineData(BeastmasterRepairMode.Self, 1, 1, false)]
    [InlineData(BeastmasterRepairMode.NpcNoInn, 100, 99, true)]
    [InlineData(BeastmasterRepairMode.NpcNearby, 100, 100, false)]
    [InlineData(BeastmasterRepairMode.Self, 75, 175, false)]
    [InlineData(BeastmasterRepairMode.NpcInn, 101, 99, true)]
    [InlineData(BeastmasterRepairMode.Disabled, 75, 1, false)]
    [InlineData((BeastmasterRepairMode)99, 75, 1, false)]
    public void OnlyKnownRepairableDurabilityBelowAnEnabledThresholdRequestsRepair(
        BeastmasterRepairMode mode, int threshold, int? durability, bool expected)
        => Assert.Equal(expected, BeastmasterRepairPolicy.ShouldRepair(mode, threshold, durability));

    [Fact]
    public void SheetRepairMetadataExcludesSoulCrystalsButRetainsBrokenGear()
    {
        Assert.Null(BeastmasterRepairPolicy.ConditionPercent(0, 0, 0));
        Assert.Null(BeastmasterRepairPolicy.ConditionPercent(8, 0, 0));
        Assert.Null(BeastmasterRepairPolicy.ConditionPercent(0, 5, 0));
        Assert.Equal(0, BeastmasterRepairPolicy.ConditionPercent(8, 5, 0));
        Assert.Equal(74, BeastmasterRepairPolicy.ConditionPercent(8, 5, 22499));
        Assert.Equal(75, BeastmasterRepairPolicy.ConditionPercent(8, 5, 22500));
        Assert.Equal(100, BeastmasterRepairPolicy.ConditionPercent(8, 5, 30000));
        Assert.Equal(175, BeastmasterRepairPolicy.ConditionPercent(8, 5, 52500));
    }

    [Fact]
    public void CancellationRequiresAnAcceptedStillMatchingActiveRepair()
    {
        var request = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
        bool Owns(bool accepted = true, string requestedMode = "self", bool readable = true,
            bool running = true, string task = "self-repair", string mode = "self", DateTime? completion = null)
            => BeastmasterRepairPolicy.CanCancelOwnedRequest(accepted, request, requestedMode,
                readable, running, task, mode, completion);

        Assert.True(Owns());
        Assert.True(Owns(completion: DateTime.MinValue));
        Assert.True(Owns(completion: request.AddSeconds(-1)));
        Assert.False(Owns(accepted: false));
        Assert.False(Owns(readable: false));
        Assert.False(Owns(running: false));
        Assert.False(Owns(task: "shop purchasing"));
        Assert.False(Owns(task: "NPC repair"));
        Assert.False(Owns(mode: "npc-no-inn"));
        Assert.False(Owns(completion: request));
        Assert.False(Owns(completion: request.AddSeconds(1)));
        Assert.False(Owns(requestedMode: "unknown", mode: "unknown", task: "NPC repair"));
        Assert.False(BeastmasterRepairPolicy.CanCancelOwnedRequest(true, default, "self",
            true, true, "self-repair", "self", null));
        foreach (var mode in new[] { "npc-no-inn", "npc-no-teleport-no-inn", "npc-yes-inn" })
        {
            Assert.True(Owns(requestedMode: mode, task: "NPC repair", mode: mode));
            Assert.False(Owns(requestedMode: mode, task: "self-repair", mode: mode));
        }
    }

    [Fact]
    public void DispatchAndStaleOrUnreadCompletionNeverEstablishRepairSuccess()
    {
        var request = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
        Assert.False(BeastmasterRepairPolicy.HasFreshCompletion(request, true, true, null));
        Assert.False(BeastmasterRepairPolicy.HasFreshCompletion(request, true, false, null));
        Assert.False(BeastmasterRepairPolicy.HasFreshCompletion(request, false, false, request.AddSeconds(1)));
        Assert.False(BeastmasterRepairPolicy.HasFreshCompletion(request, true, true, request.AddSeconds(1)));
        Assert.False(BeastmasterRepairPolicy.HasFreshCompletion(request, true, false, request.AddTicks(-1)));
        Assert.False(BeastmasterRepairPolicy.HasFreshCompletion(default, true, false, request));
        Assert.True(BeastmasterRepairPolicy.HasFreshCompletion(request, true, false, request));
        Assert.True(BeastmasterRepairPolicy.HasFreshCompletion(request, true, false, request.AddSeconds(1)));

        // A fresh ADS completion is settlement, not proof that self-repair had
        // suitable levels/materials. The same low durability still needs repair.
        Assert.True(BeastmasterRepairPolicy.ShouldRepair(BeastmasterRepairMode.Self, 75, 40));
        Assert.False(BeastmasterRepairPolicy.ShouldRepair(BeastmasterRepairMode.Self, 75, 75));
    }
}
