extern alias live;

using System;
using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Text.Json;
using VERMAXION.CustomDeliveries;
using VERMAXION.Models;
using Xunit;

namespace VERMAXION.Tests;

public sealed class CustomDeliveriesConfigurationTests
{
    [Fact]
    public void IdleRefreshFailureRemainsBlockedAndReachesDisplayedStatusAndEngineReason()
    {
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        var serviceType = typeof(live::VERMAXION.CustomDeliveries.CustomDeliveriesService);
        var service = (live::VERMAXION.CustomDeliveries.CustomDeliveriesService)RuntimeHelpers.GetUninitializedObject(serviceType);
        serviceType.GetField("status", instance)!.SetValue(service, "Idle");
        var refresh = serviceType.GetMethod("TryRefreshData", instance, null, [typeof(Action)], null)!;
        var displayed = typeof(live::VERMAXION.Windows.MainWindow).GetMethod("GetCustomDeliveryStatus",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        var engineType = typeof(live::VERMAXION.Services.VermaxionEngine);
        var engine = RuntimeHelpers.GetUninitializedObject(engineType);
        engineType.GetField("customDeliveriesService", instance)!.SetValue(engine, service);
        var evaluate = engineType.GetMethod("EvaluateCustomDeliveries", instance, null,
            [typeof(live::VERMAXION.Models.CharacterConfig), typeof(bool)], null)!;
        var config = new live::VERMAXION.Models.CharacterConfig { EnableCustomDeliveries = true };
        var settings = config.CustomDeliveriesSettings;

        Assert.Null(service.DataRefreshError);
        Assert.Equal("Weekly: unknown allowances left", displayed.Invoke(null, [service]));
        Assert.Equal("Custom delivery data is unavailable for the current character", service.GetStartBlockedReason(settings));

        const string detail = "Fixture requested-item row is unavailable";
        Action failure = () => throw new InvalidOperationException(detail);
        Assert.Equal(false, refresh.Invoke(service, [failure]));
        const string expected = "Custom delivery data unavailable: " + detail;
        Assert.False(service.IsActive);
        Assert.False(service.IsFailed); // An idle refresh failure must not manufacture an active task failure.
        Assert.Null(service.RemainingAllowances);
        Assert.Equal(expected, service.DataRefreshError);
        Assert.Equal(expected, service.GetStartBlockedReason(settings));
        Assert.Equal(expected, displayed.Invoke(null, [service]));
        var eligibility = (live::VERMAXION.Models.TaskEligibility)evaluate.Invoke(engine, [config, false])!;
        Assert.False(eligibility.IsRunnable);
        Assert.Equal(expected, eligibility.Reason);

        config.EnableCustomDeliveries = false;
        eligibility = (live::VERMAXION.Models.TaskEligibility)evaluate.Invoke(engine, [config, false])!;
        Assert.Equal(live::VERMAXION.Models.TaskEligibilityStatus.Disabled, eligibility.Status);
        eligibility = (live::VERMAXION.Models.TaskEligibility)evaluate.Invoke(engine, [config, true])!;
        Assert.Equal(live::VERMAXION.Models.TaskEligibilityStatus.Blocked, eligibility.Status);
        Assert.Equal(expected, eligibility.Reason);

        foreach (var language in new[] { "en", "de", "es", "fr", "hi", "id", "it", "ja", "ko", "pl", "pt_BR", "ru", "tr", "vi", "zh_Hans" })
        {
            var resources = new ResourceManager("VERMAXION.Localization.Strings_" + language, serviceType.Assembly);
            var template = resources.GetString("Custom delivery data unavailable: {0}", CultureInfo.InvariantCulture);
            resources.ReleaseAllResources();
            Assert.False(string.IsNullOrWhiteSpace(template), language);
            Assert.Contains("{0}", template!);
        }

        Assert.Equal(true, refresh.Invoke(service, [(Action)(() => { })]));
        Assert.Null(service.DataRefreshError); // A later successful refresh must not leave a stale exception on unknown data.
        Assert.Equal("Weekly: unknown allowances left", displayed.Invoke(null, [service]));
        Assert.Equal("Custom delivery data is unavailable for the current character", service.GetStartBlockedReason(settings));
    }

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
