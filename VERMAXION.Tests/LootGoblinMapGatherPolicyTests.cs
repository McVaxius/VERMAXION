using System.Collections.Generic;
using VERMAXION.Models;
using Xunit;

namespace VERMAXION.Tests;

public sealed class LootGoblinMapGatherPolicyTests
{
    [Fact]
    public void NewTaskNormalizesIntoDefaultOrderAfterChocoboRacing()
    {
        var normalized = PostProcessTaskOrder.Normalize([]);

        var chocoboIndex = normalized.IndexOf(PostProcessTaskOrder.ChocoboRacing);
        Assert.True(chocoboIndex >= 0);
        Assert.Equal(PostProcessTaskOrder.LootGoblinMapGather, normalized[chocoboIndex + 1]);
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
