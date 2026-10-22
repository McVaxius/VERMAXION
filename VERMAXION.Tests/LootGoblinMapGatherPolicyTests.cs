using System.Collections.Generic;
using System.Linq;
using VERMAXION.Models;
using Xunit;

namespace VERMAXION.Tests;

public sealed class LootGoblinMapGatherPolicyTests
{
    [Fact]
    public void DefaultOrderKeepsBeastmasterAndWeeklyBetweenRacingAndMapGathering()
    {
        var normalized = PostProcessTaskOrder.Normalize([]);

        var chocoboIndex = normalized.IndexOf(PostProcessTaskOrder.ChocoboRacing);
        Assert.True(chocoboIndex >= 0);
        Assert.Equal(new[] { PostProcessTaskOrder.ChocoboRacing, PostProcessTaskOrder.Beastmaster,
            PostProcessTaskOrder.BeastmasterWeekly, PostProcessTaskOrder.LootGoblinMapGather }, normalized.Skip(chocoboIndex).Take(4));
        Assert.Equal(PostProcessTaskOrder.DefaultOrder, normalized);
        Assert.Equal(normalized, PostProcessTaskOrder.Normalize(normalized));
    }

    [Fact]
    public void ExistingMapGatherPlacementSurvivesBeastmasterMigrationAndRepeatedNormalization()
    {
        var original = new[] { PostProcessTaskOrder.LootGoblinMapGather, PostProcessTaskOrder.ChocoboRacing,
            PostProcessTaskOrder.RegisterRegistrables, PostProcessTaskOrder.NagYourDad };
        var normalized = PostProcessTaskOrder.Normalize(original);
        Assert.Equal(original, normalized.Where(original.Contains));
        Assert.Single(normalized, id => id == PostProcessTaskOrder.Beastmaster);
        Assert.Single(normalized, id => id == PostProcessTaskOrder.BeastmasterWeekly);
        Assert.Equal(normalized.IndexOf(PostProcessTaskOrder.Beastmaster) + 1,
            normalized.IndexOf(PostProcessTaskOrder.BeastmasterWeekly));
        Assert.Equal(normalized, PostProcessTaskOrder.Normalize(normalized));
    }

    [Fact]
    public void NewTaskDefaultPhaseIsAfterAR()
    {
        Assert.Equal(PostProcessTaskPhase.AfterAR, PostProcessTaskOrder.GetDefaultPhase(PostProcessTaskOrder.LootGoblinMapGather));
    }

    [Fact]
    public void NormalizeAddsDefaultPlacementForNewTask()
    {
        var config = new Configuration
        {
            PostProcessTaskOrder = new List<string> { PostProcessTaskOrder.ChocoboRacing },
            PostProcessTaskPlacement = new Dictionary<string, PostProcessTaskPhase>
            {
                [PostProcessTaskOrder.ChocoboRacing] = PostProcessTaskPhase.AfterAR,
            },
        };

        Assert.True(PostProcessTaskOrder.Normalize(config));
        Assert.Equal(PostProcessTaskPhase.AfterAR, config.PostProcessTaskPlacement[PostProcessTaskOrder.LootGoblinMapGather]);
    }

    [Fact]
    public void SoloOutdoorMapIsSafeForRunAfter()
    {
        var map = new LootGoblinMapInfo
        {
            Tier = "Solo",
            Category = "Outdoor",
            HasDungeon = false,
        };

        Assert.True(LootGoblinMapSafetyPolicy.IsSoloOutdoorSafe(map));
    }

    [Theory]
    [InlineData("Party", "Outdoor", false)]
    [InlineData("Solo", "Dungeon", true)]
    [InlineData("Solo", "Outdoor", true)]
    public void PartyOrDungeonMapsWarnForRunAfter(string tier, string category, bool hasDungeon)
    {
        var map = new LootGoblinMapInfo
        {
            Tier = tier,
            Category = category,
            HasDungeon = hasDungeon,
        };

        Assert.False(LootGoblinMapSafetyPolicy.IsSoloOutdoorSafe(map));
    }
}
