using System;
using System.Text.Json;
using VERMAXION.CustomDeliveries;
using VERMAXION.Models;
using Xunit;

namespace VERMAXION.Tests;

public sealed class CustomDeliveriesConfigurationTests
{
    [Fact]
    public void RankQuestBoundaryStopsProductionUntilQuestCompletion()
    {
        // The final rank needs 1,080 satisfaction, at 180 per high-quality item.
        // Four delivered items leave two to produce even with six allowances.
        var beforeQuest = DeliveryPlanning.RankTurnins(6, 720, 1080, 180, false);
        Assert.Equal(2, beforeQuest);
        Assert.Equal(2, DeliveryPlanning.Count(DeliveryNpcPolicy.ClosestTo150, 12, beforeQuest, 18));
        Assert.Equal(1, DeliveryPlanning.MissingItems(beforeQuest, 1));
        Assert.Equal(1, DeliveryPlanning.RankTurnins(1, 720, 1080, 180, false));
        Assert.Equal(1, DeliveryPlanning.RankTurnins(6, 1000, 1080, 180, false));
        Assert.Equal(0, DeliveryPlanning.RankTurnins(6, 1080, 1080, 180, true));
        Assert.Equal(0, DeliveryPlanning.RankTurnins(6, 0, 0, 180, true));
        Assert.Equal(0, DeliveryPlanning.RankTurnins(6, 1080, 1080, 180, false));
        Assert.Equal(0, DeliveryPlanning.RankTurnins(6, 720, 1080, 0, false));
        Assert.Equal(6, DeliveryPlanning.RankTurnins(6, 0, 0, 0, false));
        Assert.Equal(0, DeliveryPlanning.RankTurnins(0, 0, 0, 180, false));
        Assert.Equal(4, DeliveryPlanning.AvailableQuestRank(4, 720, 1080));
        Assert.Equal(5, DeliveryPlanning.AvailableQuestRank(4, 1080, 1080));
        Assert.Equal(5, DeliveryPlanning.AvailableQuestRank(5, 0, 0));
        Assert.Equal(0, DeliveryPlanning.AvailableQuestRank(0, 0, 0));
    }

    [Fact]
    public void PlansOnlyRemainingTurninsAndMissingCollectibles()
    {
        var planned = DeliveryPlanning.Count(DeliveryNpcPolicy.ClosestTo150, 12, 6, 147);
        Assert.Equal(3, planned);
        Assert.Equal(3, DeliveryPlanning.MissingItems(planned, 0));
        Assert.Equal(1, DeliveryPlanning.MissingItems(planned, 2));
        Assert.Equal(0, DeliveryPlanning.MissingItems(planned, 4));
        Assert.Equal(2, DeliveryPlanning.Count(DeliveryNpcPolicy.ClosestTo150, 2, 6, 147));
        Assert.Equal(1, DeliveryPlanning.Count(DeliveryNpcPolicy.ClosestTo150, 12, 1, 147));
        Assert.Equal(0, DeliveryPlanning.Count(DeliveryNpcPolicy.ClosestTo150, 12, 6, null));
        Assert.Equal(0, DeliveryPlanning.Count(DeliveryNpcPolicy.ClosestTo150, 12, 6, 150));
        Assert.Equal(0, DeliveryPlanning.Count(DeliveryNpcPolicy.ClosestTo150, 12, 6, 151));
        Assert.Equal(6, DeliveryPlanning.Count(DeliveryNpcPolicy.BonusesOnly, 12, 6, 147));
        Assert.Equal(0, DeliveryPlanning.Count(DeliveryNpcPolicy.BonusesOnly, 0, 6, null));
    }

    [Fact]
    public void AcceptsOnlyTheCurrencyOverflowConfirmationText()
    {
        Assert.True(DeliveryPlanning.IsScripOverflowPrompt("Unable to receive the following items:  Purple Crafters' Scrip  Orange Crafters' Scrip Proceed?"));
        Assert.True(DeliveryPlanning.IsScripOverflowPrompt("Unable to receive the following items:"));
        Assert.True(DeliveryPlanning.IsScripOverflowPrompt("Warning: Unable to receive the following items:\nOrange Gatherers' Scrip\nContinue anyway?"));
        Assert.False(DeliveryPlanning.IsScripOverflowPrompt("Proceed with the transaction?"));
        Assert.False(DeliveryPlanning.IsScripOverflowPrompt("Discard all Purple Crafters' Scrips?"));
        Assert.False(DeliveryPlanning.IsScripOverflowPrompt(""));
    }

    [Fact]
    public void SavedCharacterSettingsRoundTripAndCopiesHaveIndependentJobSelections()
    {
        Assert.False(new CharacterConfig().EnableCustomDeliveries);
        Assert.False(JsonSerializer.Deserialize<CharacterConfig>("{}")!.EnableCustomDeliveries);
        Assert.Equal(FishingStockItemIds.VersatileLure, new CustomDeliveriesSettings().FishingBaitId);
        Assert.Equal(FishingStockItemIds.VersatileLure, JsonSerializer.Deserialize<CustomDeliveriesSettings>("{}")!.FishingBaitId);
        var zeroBaitSettings = JsonSerializer.Deserialize<CustomDeliveriesSettings>("{\"FishingBaitId\":0}")!;
        Assert.Equal(FishingStockItemIds.VersatileLure, zeroBaitSettings.FishingBaitId);
        using (var normalizedJson = JsonDocument.Parse(JsonSerializer.Serialize(zeroBaitSettings)))
            Assert.Equal(FishingStockItemIds.VersatileLure, normalizedJson.RootElement.GetProperty("FishingBaitId").GetUInt32());
        zeroBaitSettings.FishingBaitId = 123;
        zeroBaitSettings.FishingBaitId = 0;
        Assert.Equal(FishingStockItemIds.VersatileLure, zeroBaitSettings.FishingBaitId);
        var character = new CharacterConfig
        {
            EnableCustomDeliveries = true,
            CustomDeliveriesLastCompleted = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc),
            CustomDeliveriesSettings = new()
            {
                NpcPolicy = DeliveryNpcPolicy.BonusesOnly,
                AllowedTypes = DeliveryTypes.Crafting | DeliveryTypes.Fishing,
                EligibleCraftJobs = [9, 14],
                EligibleGatherJobs = [17],
                FishingBaitId = 123,
                FishingPresetName = "fixture preset",
                AutoFetchAchievements = false,
            },
        };
        var json = JsonSerializer.Serialize(character);
        var restored = JsonSerializer.Deserialize<CharacterConfig>(json)!;
        Assert.Equal(json, JsonSerializer.Serialize(restored));

        var cloned = restored.Clone();
        var copied = new CharacterConfig();
        copied.CopyCustomDeliveriesSettingsFrom(restored);
        Assert.True(cloned.EnableCustomDeliveries);
        Assert.True(copied.EnableCustomDeliveries);
        Assert.Equal(restored.CustomDeliveriesLastCompleted, cloned.CustomDeliveriesLastCompleted);
        Assert.Equal(DateTime.MinValue, copied.CustomDeliveriesLastCompleted);
        Assert.Equal(JsonSerializer.Serialize(restored.CustomDeliveriesSettings), JsonSerializer.Serialize(copied.CustomDeliveriesSettings));
        cloned.CustomDeliveriesSettings.EligibleCraftJobs.Clear();
        copied.CustomDeliveriesSettings.EligibleGatherJobs.Clear();
        Assert.Equal(new uint[] { 9, 14 }, restored.CustomDeliveriesSettings.EligibleCraftJobs);
        Assert.Equal(new uint[] { 17 }, restored.CustomDeliveriesSettings.EligibleGatherJobs);
    }
}
