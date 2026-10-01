using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using VERMAXION.Models;
using VERMAXION.Services;
using Xunit;

namespace VERMAXION.Tests;

public sealed class FishCollectionTests
{
    [Fact]
    public void AllOverworldTargetsForecastUsingTheirBundledWeatherAndPredatorMappings()
    {
        var catalog = FishCollectionCatalog.Load();
        var forecast = new FishCollectionForecast(catalog);
        var now = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var found = catalog.Targets.Where(f => !f.Ocean).Select(f => forecast.Next(f, now)).Where(o => o != null).ToArray();
        Assert.True(found.Length > 250);
        Assert.All(found, o => Assert.True(o!.EndUtc > now && o.EndUtc > o.StartUtc));
    }
    [Fact]
    public void BundledCoverageAndCorrectionsAreComplete()
    {
        var catalog = FishCollectionCatalog.Load();
        Assert.Equal(335, catalog.Targets.Count(f => !f.Ocean));
        Assert.Equal(13, catalog.Targets.Count(f => f.Ocean));
        Assert.Equal(301, catalog.Targets.Where(f => !f.Ocean).Select(f => f.SpotId).Distinct().Count());
        Assert.Equal(catalog.Fish.Length, catalog.Fish.Select(f => f.ItemId).Distinct().Count());
        Assert.All(catalog.Targets.Where(f => !f.Ocean), fish =>
        {
            var spot = Assert.Single(catalog.Spots, s => s.SpotId == fish.SpotId);
            Assert.All(spot.Position, p => Assert.True(float.IsFinite(p)));
            Assert.True(spot.SourceCatchItemId > 0 && spot.SourceTimestamp > 0 && spot.Rotation.HasValue);
        });
        Assert.Equal(15, catalog.Targets.Count(f => f.RecommendedGathering > 0));
        Assert.All(catalog.Targets.Where(f => f.RecommendedGathering > 0), f => Assert.Contains(f.RecommendedGathering, new[] { 403, 803, 1300 }));
        var byId = catalog.Fish.ToDictionary(f => f.ItemId);
        Assert.Equal(349, byId[7913].MinimumGathering); Assert.Equal(370, byId[8764].MinimumGathering);
        Assert.Equal(383, byId[8773].MinimumGathering); Assert.Equal(680, byId[15630].MinimumGathering);
        Assert.Equal(23.5, byId[33242].StartHour); Assert.Equal(24, byId[33242].EndHour);
        Assert.Empty(byId[8775].Weather); Assert.NotEmpty(byId[8774].Weather); Assert.Equal(60d, byId[8775].IntuitionSeconds);
        Assert.Equal(2, byId[51228].OceanTime);
        Assert.All(catalog.Fish.SelectMany(f => f.Predators), p => Assert.True(byId.ContainsKey(p.ItemId)));
        Assert.Contains(2619u, catalog.ReusableBait); Assert.DoesNotContain(29717u, catalog.ReusableBait);
    }
    [Fact]
    public void AllFabledStrategiesIncludeTheNewRoutesAndExactTargetNames()
    {
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("VERMAXION.Resources.ocean-collection-presets.json")!;
        using var json = JsonDocument.Parse(resource);
        var names = json.RootElement.GetProperty("presets").EnumerateArray().Select(p => p.GetProperty("PresetName").GetString()!).ToArray();
        Assert.Equal(55, names.Length);
        foreach (var name in new[] { "SOTHIS", "CORAL MANTA", "STONESCALE", "ELASMOSAURUS", "HAFGUFA", "SEAFARING TOAD", "PLACODUS", "TANIWHA", "GLASS DRAGON", "HELLS CLAW", "JEWEL", "AKUPARA", "MANASVIN" })
            Assert.Single(names, n => n.EndsWith(name, StringComparison.Ordinal));
        Assert.Contains(names, n => n.Contains("SUNSET Spectral AKUPARA", StringComparison.Ordinal));
    }
    [Fact]
    public void NqAndHqQualifySeparatelyAndNormalizeNativePercentageCaps()
    {
        var baseline = new FisherObservation(100, 1000, 900, 700, 3, DateTimeOffset.UtcNow, true);
        var nq = new FisherMeal(1, "test", false, 1, [new(72, true, 5, 40), new(10, true, 3, 20)]);
        var hq = new FisherMeal(1, "test", true, 1, [new(72, true, 8, 70), new(10, true, 4, 30)]);
        Assert.Equal(1040, nq.Apply(baseline).Gathering); Assert.Equal(1070, hq.Apply(baseline).Gathering);
        Assert.Equal(720, nq.Apply(baseline).MaximumGp); Assert.Equal(728, hq.Apply(baseline).MaximumGp);
        Assert.Equal(1000, hq.Bonuses[0].Remove(1070)); Assert.Equal(700, hq.Bonuses[1].Remove(728));
        var fish = new CollectionFish { ItemId = 1, MinimumGathering = 1050 };
        var known = new CharacterFishKnowledge([], [1], DateTimeOffset.UtcNow);
        Assert.False(FishCollectionPolicy.Eligible(fish, baseline, known, new(), [nq]));
        Assert.True(FishCollectionPolicy.Eligible(fish, baseline, known, new(), [hq]));
        Assert.False(FishCollectionPolicy.Eligible(fish, baseline, known, new() { BuffPolicy = FisherBuffPolicy.GearAlone }, [hq]));
        Assert.NotEqual(nq.PriceKey, hq.PriceKey); Assert.Equal(1_000_001u, hq.UseItemId);
        Assert.False(FishCollectionPolicy.Eligible(fish, baseline, known, new(), [hq with { RequiredLevel = 101 }]));
    }
    [Fact]
    public void UnavailableOrUncertainObservationsCannotAssignOrBuyForACharacter()
    {
        var fish = new CollectionFish { ItemId = 1, MinimumGathering = 349, RecommendedGathering = 403 };
        var observation = new FisherObservation(50, 370, 300, 500, 3, DateTimeOffset.UtcNow.AddYears(-1), true);
        var known = new CharacterFishKnowledge([], [1], DateTimeOffset.UtcNow);
        var settings = new FishCollectionSettings();
        Assert.False(FishCollectionPolicy.Eligible(fish, observation, null, settings, []));
        Assert.False(FishCollectionPolicy.Eligible(fish, observation with { BaselineCertain = false }, known, settings, []));
        Assert.False(FishCollectionPolicy.Eligible(fish, observation, known, settings, []));
        settings.RecommendationOverrides.Add(1);
        Assert.True(FishCollectionPolicy.Eligible(fish, observation, known, settings, []));
        Assert.False(FishCollectionPolicy.Eligible(fish, observation with { Gathering = 348 }, known, settings, []));
        Assert.False(FishCollectionPolicy.Eligible(fish, observation, known with { CaughtItems = [1] }, settings, []));
    }
    [Fact]
    public void OpportunityOrderingPinsThenRarityThenDeadlineThenCurrentCharacter()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(7200);
        FishAssignment Candidate(uint id, string key, int wait, int end, bool always = false) => new(key,
            new(new() { ItemId = id, StartHour = always ? 0 : 1, EndHour = 24 }, now.AddMinutes(30), now.AddMinutes(end), TimeSpan.FromDays(wait)));
        FishAssignment[] candidates = [Candidate(1, "other", 1, 45), Candidate(2, "other", 5, 50), Candidate(2, "current", 5, 50), Candidate(3, "current", 100, 40, true)];
        Assert.Equal(1u, FishCollectionPolicy.Choose(candidates, 1, "current", now)!.Opportunity.Fish.ItemId);
        var chosen = FishCollectionPolicy.Choose(candidates, 0, "current", now)!;
        Assert.Equal(2u, chosen.Opportunity.Fish.ItemId); Assert.Equal("current", chosen.CharacterKey);
        Assert.Same(chosen, FishCollectionPolicy.Choose([chosen], 1, "other", now));
        Assert.Null(FishCollectionPolicy.Choose([Candidate(1, "current", 5, 70) with { Opportunity = candidates[0].Opportunity with { StartUtc = now.AddHours(2) } }], 0, "current", now));
    }
    [Fact]
    public void FractionalAndMidnightWindowsRetainTheirActualBoundaries()
    {
        var catalog = MinimalCatalog(); var forecast = new FishCollectionForecast(catalog);
        var midnight = DateTimeOffset.FromUnixTimeSeconds(4200 * 100);
        var fractional = new CollectionFish { SpotId = 1, StartHour = 23.5, EndHour = 24 };
        var window = Assert.Single(forecast.Windows(fractional, midnight.AddSeconds(4100), midnight.AddSeconds(4300)));
        Assert.Equal(midnight.AddSeconds(4112.5), window.Start); Assert.Equal(midnight.AddSeconds(4200), window.End);
        var crossing = new CollectionFish { SpotId = 1, StartHour = 23.5, EndHour = .5 };
        var cross = Assert.Single(forecast.Windows(crossing, midnight.AddSeconds(4100), midnight.AddSeconds(4300)));
        Assert.Equal(TimeSpan.FromSeconds(175), cross.End - cross.Start);
    }
    [Fact]
    public void UnknownIntuitionRequiresSharedOverlapAndKnownDurationsPermitCarryover()
    {
        var predator = new CollectionFish { ItemId = 2, SpotId = 1, StartHour = 9, EndHour = 10 };
        var target = new CollectionFish { ItemId = 1, SpotId = 1, StartHour = 10, EndHour = 11, Predators = [new(2, 1)] };
        var catalog = MinimalCatalog(); catalog.Fish = [target, predator];
        var forecast = new FishCollectionForecast(catalog); var day = DateTimeOffset.FromUnixTimeSeconds(4200 * 100);
        Assert.Empty(forecast.QualifyingWindows(target, day, day.AddSeconds(4200)));
        target.IntuitionSeconds = 60;
        var shared = Assert.Single(forecast.QualifyingWindows(target, day, day.AddSeconds(4200)));
        Assert.Equal(TimeSpan.FromSeconds(60), shared.End - shared.Start);
        var second = new CollectionFish { ItemId = 3, SpotId = 1, StartHour = 10.7, EndHour = 11 };
        catalog.Fish = [target, predator, second]; target.Predators = [new(2, 1), new(3, 1)];
        Assert.Empty(new FishCollectionForecast(catalog).QualifyingWindows(target, day, day.AddSeconds(4200)));
    }
    [Fact]
    public void OceanCycleBoundaryQuantitiesAndTaxAreExact()
    {
        var boundary = DateTimeOffset.FromUnixTimeSeconds(7200 * 56);
        Assert.Equal(143, FishCollectionPolicy.OceanCycleIndex(boundary.AddSeconds(-1)));
        Assert.Equal(0, FishCollectionPolicy.OceanCycleIndex(boundary));
        Assert.Equal(1, FishCollectionPolicy.OceanCycleIndex(boundary.AddHours(2)));
        Assert.Equal(0, FishCollectionPolicy.OceanCycleIndex(boundary.AddHours(288)));
        Assert.Equal(100, FishCollectionPolicy.BundleQuantity(99, 5));
        Assert.Equal(105, FishCollectionPolicy.BundleQuantity(101, 5));
        Assert.Equal(0, FishCollectionPolicy.BundleQuantity(0, 5));
        Assert.Equal(2, FishCollectionPolicy.MealQuantity(TimeSpan.FromMinutes(45)));
        Assert.Equal(3, FishCollectionPolicy.MealQuantity(TimeSpan.FromMinutes(60)));
        Assert.Equal(103950, FishCollectionPolicy.MarketBudget(99, 1000));
        Assert.False(new FishCollectionSettings().AudioEnabled);
    }
    [Fact]
    public void AnOpportunityKeepsOneIdentityAcrossSelectedCharacters()
    {
        var opportunity = new FishOpportunity(new() { ItemId = 1 }, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), TimeSpan.Zero);
        Assert.Equal(new FishAssignment("first", opportunity).Opportunity.Key,
            new FishAssignment("second", opportunity).Opportunity.Key);
        Assert.NotEqual(opportunity.Key, (opportunity with { StartUtc = opportunity.StartUtc.AddHours(1) }).Key);
    }
    [Fact]
    public void AlwaysAvailableFishingYieldsAtTheNextRarePreparationWithoutChangingItsIdentity()
    {
        var now = DateTimeOffset.UtcNow;
        var common = new FishAssignment("current", new(new() { ItemId = 1, EndHour = 24 }, now, now.AddHours(1), TimeSpan.Zero));
        var rare = new FishAssignment("other", new(new() { ItemId = 2, StartHour = 1, EndHour = 2 }, now.AddMinutes(90), now.AddMinutes(95), TimeSpan.FromDays(7)));
        var bounded = FishCollectionPolicy.BoundAlwaysAvailable(common, [common, rare], 0, now);
        Assert.Equal(now.AddMinutes(30), bounded.Opportunity.EndUtc);
        Assert.Equal(common.Opportunity.Key, bounded.Opportunity.Key);
        Assert.Equal(common.CharacterKey, bounded.CharacterKey);
        Assert.Same(common, FishCollectionPolicy.BoundAlwaysAvailable(common, [rare], 1, now));
    }
    [Fact]
    public void PartialOptionalStockCanProceedButTheFullMandatoryMealDurationMustBeCovered()
    {
        var bait = new CollectionSupply(1, "bait", null, 99, true, 1000);
        var food = new CollectionSupply(2, "meal", true, 3, true, 5999, 3);
        Assert.True(bait.ReadyForAttempt(1)); Assert.False(bait.ReadyForAttempt(0));
        Assert.False(food.ReadyForAttempt(2)); Assert.True(food.ReadyForAttempt(3));
        Assert.True((bait with { Mandatory = false }).ReadyForAttempt(0));
    }
    [Fact]
    public void MarketOrdersPinQualityWorldQuantityPricesAndTaxInclusiveBudgetWithoutUnlimitedZero()
    {
        var food = new CollectionSupply(46254, "meal", true, 2, true, 5999, 2);
        using var json = JsonDocument.Parse(FishCollectionPolicy.MarketRequestJson("owned", "current", food, 2, 10000, 20000));
        var root = json.RootElement;
        Assert.Equal("owned", root.GetProperty("clientRequestId").GetString());
        Assert.Equal("current", root.GetProperty("world").GetString());
        Assert.False(root.GetProperty("returnToHomeWorld").GetBoolean());
        Assert.Equal(10000, root.GetProperty("totalGilBudget").GetInt64());
        var item = Assert.Single(root.GetProperty("items").EnumerateArray());
        Assert.Equal(2, item.GetProperty("quantity").GetInt32());
        Assert.Equal(5999, item.GetProperty("maxUnitPrice").GetInt32());
        Assert.Equal("hq", item.GetProperty("quality").GetString());
        Assert.Equal("skip", item.GetProperty("overshoot").GetString());
        Assert.Throws<InvalidOperationException>(() => FishCollectionPolicy.MarketRequestJson("owned", "current", food, 2, 0, 20000));
        Assert.Throws<InvalidOperationException>(() => FishCollectionPolicy.MarketRequestJson("owned", "current", food, 2, 10000, 0));
        Assert.Throws<InvalidOperationException>(() => FishCollectionPolicy.MarketRequestJson("owned", "current", food with { UnitLimit = 0 }, 2, 10000, 20000));
    }
    private static FishCollectionCatalog MinimalCatalog() => new()
    {
        Spots = [new(1, 1, "test", [0, 0, 0], 0, 1, 1)],
        WeatherRates = [new(1, [new(1, 100)])],
    };
}
