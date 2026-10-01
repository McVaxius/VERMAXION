using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace VERMAXION.Models;

public enum FisherBuffPolicy { GearAlone, PlannedBuffs }

public sealed class FishCollectionSettings
{
    public uint PinnedItemId { get; set; }
    public FisherBuffPolicy BuffPolicy { get; set; } = FisherBuffPolicy.PlannedBuffs;
    public HashSet<uint> RecommendationOverrides { get; set; } = new();
    public int BaitUnitPriceLimit { get; set; } = 1_000;
    public int CordialUnitPriceLimit { get; set; } = 5_000;
    public int LureUnitPriceLimit { get; set; } = 10_000;
    public int PreparationGilLimit { get; set; } = 100_000;
    // Exact quality keys, e.g. "44098:HQ". Absence never creates an invented price.
    public Dictionary<string, int> FoodPriceLimits { get; set; } = new();
    public bool AudioEnabled { get; set; }
    public uint SoundEffect { get; set; } = 1;
    public int AlertAdvanceMinutes { get; set; } = 15;
    public int AlertRepeatMinutes { get; set; } = 5;
    public bool IncludeBigFish { get; set; } = true;
    public bool IncludeFabledFish { get; set; } = true;
}

public sealed record FisherObservation(int Level, int Gathering, int Perception, int MaximumGp,
    int GearsetId, DateTimeOffset ObservedAtUtc, bool BaselineCertain);

public sealed class CollectionFish
{
    public uint ItemId { get; set; }
    public string Name { get; set; } = "";
    public bool Target { get; set; }
    public bool Ocean { get; set; }
    public uint FishParameterId { get; set; }
    public uint[] Routes { get; set; } = [];
    public uint SpotId { get; set; }
    public double Patch { get; set; }
    public int MinimumGathering { get; set; }
    public int RecommendedGathering { get; set; }
    public double StartHour { get; set; }
    public double EndHour { get; set; }
    public uint[] Weather { get; set; } = [];
    public uint[] PreviousWeather { get; set; } = [];
    // A stage can contain one item ID or an array of alternative bait IDs in the sourced data.
    public JsonElement[] CatchPath { get; set; } = [];
    public CollectionPredator[] Predators { get; set; } = [];
    public double? IntuitionSeconds { get; set; }
    public uint Folklore { get; set; }
    public int OceanTime { get; set; }
    public uint HookAction { get; set; }
    public bool Snagging { get; set; }
    public int ModestLure { get; set; }
    public int AmbitiousLure { get; set; }
    public int RequiredGathering(FishCollectionSettings settings) => Math.Max(MinimumGathering,
        settings.RecommendationOverrides.Contains(ItemId) ? 0 : RecommendedGathering);
    public bool AlwaysAvailable => !Ocean && StartHour == 0 && EndHour == 24 &&
        Weather.Length == 0 && PreviousWeather.Length == 0 && Predators.Length == 0;
}

public sealed record CollectionPredator(uint ItemId, int Quantity);
public sealed record CollectionSpot(uint SpotId, uint TerritoryId, string Name, float[] Position,
    float? Rotation, uint SourceCatchItemId, long SourceTimestamp)
{
    public Vector3 Point => new(Position[0], Position[1], Position[2]);
}
public sealed record CollectionWeatherRate(uint WeatherId, int Threshold);
public sealed record CollectionTerritoryWeather(uint TerritoryId, CollectionWeatherRate[] Rates);
public sealed record FishOpportunity(CollectionFish Fish, DateTimeOffset StartUtc, DateTimeOffset EndUtc,
    TimeSpan NextWait, uint RouteId = 0)
{
    public string Key => $"{Fish.ItemId}:{StartUtc.ToUnixTimeSeconds()}";
    public DateTimeOffset PreparationUtc => StartUtc.AddMinutes(-60);
    public DateTimeOffset PositionedUtc => StartUtc.AddMinutes(-45);
}
public sealed record CharacterFishKnowledge(HashSet<uint> CaughtItems, HashSet<uint> UnlockedItems,
    DateTimeOffset ObservedAtUtc);
public sealed record FishAssignment(string CharacterKey, FishOpportunity Opportunity);

public static class FishCollectionPolicy
{
    public static int OceanCycleIndex(DateTimeOffset registration)
        => (int)((registration.ToUnixTimeSeconds() / 7200 + 88) % 144);
    public static bool Eligible(CollectionFish fish, FisherObservation? observation,
        CharacterFishKnowledge? knowledge, FishCollectionSettings settings, IEnumerable<FisherMeal> meals)
        => observation is { BaselineCertain: true } && knowledge != null &&
           !knowledge.CaughtItems.Contains(fish.ItemId) && knowledge.UnlockedItems.Contains(fish.ItemId) &&
           (observation.Gathering >= fish.RequiredGathering(settings) ||
            settings.BuffPolicy == FisherBuffPolicy.PlannedBuffs &&
            meals.Any(m => m.RequiredLevel <= observation.Level && m.Apply(observation).Gathering >= fish.RequiredGathering(settings)));

    public static FishAssignment? Choose(IEnumerable<FishAssignment> opportunities, uint pinnedItemId,
        string currentCharacterKey, DateTimeOffset now)
        => opportunities.Where(a => a.Opportunity.EndUtc > now && a.Opportunity.PreparationUtc <= now)
            .OrderByDescending(a => a.Opportunity.Fish.ItemId == pinnedItemId)
            .ThenBy(a => a.Opportunity.Fish.AlwaysAvailable)
            .ThenByDescending(a => a.Opportunity.NextWait)
            .ThenBy(a => a.Opportunity.EndUtc)
            .ThenByDescending(a => a.CharacterKey == currentCharacterKey)
            .ThenBy(a => a.Opportunity.Fish.ItemId)
            .FirstOrDefault();

    public static FishAssignment BoundAlwaysAvailable(FishAssignment assignment, IEnumerable<FishAssignment> candidates,
        uint pinnedItemId, DateTimeOffset now)
    {
        if (!assignment.Opportunity.Fish.AlwaysAvailable || assignment.Opportunity.Fish.ItemId == pinnedItemId) return assignment;
        var deadline = candidates.Where(a => !a.Opportunity.Fish.AlwaysAvailable && a.Opportunity.PreparationUtc > now)
            .Select(a => a.Opportunity.PreparationUtc).Append(assignment.Opportunity.EndUtc).Min();
        return assignment with { Opportunity = assignment.Opportunity with { EndUtc = deadline } };
    }

    public static string Readiness(CollectionFish fish, FisherObservation? observation,
        CharacterFishKnowledge? knowledge, FishCollectionSettings settings, IEnumerable<FisherMeal> meals)
    {
        if (observation == null || !observation.BaselineCertain) return "Fisher inspection required";
        if (knowledge == null) return "Fishing log and unlock inspection required";
        if (knowledge.CaughtItems.Contains(fish.ItemId)) return "Caught";
        if (!knowledge.UnlockedItems.Contains(fish.ItemId)) return "Required level, action or folklore is locked";
        var minimum = fish.RequiredGathering(settings);
        if (observation.Gathering >= minimum) return "Gear qualifies";
        if (settings.BuffPolicy == FisherBuffPolicy.PlannedBuffs &&
            meals.Any(m => m.RequiredLevel <= observation.Level && m.Apply(observation).Gathering >= minimum)) return "Qualifying meal required";
        return $"Gathering {observation.Gathering}/{minimum}" +
            (fish.MinimumGathering < minimum ? " (expansion recommendation)" : " (mandatory)");
    }

    public static int BundleQuantity(int deficit, uint receiveCount)
        => deficit <= 0 || receiveCount == 0 ? 0 : checked((int)(((long)deficit + receiveCount - 1) / receiveCount * receiveCount));
    // Refresh two minutes before the native 30-minute effect expires so an in-flight cast can settle.
    public static int MealQuantity(TimeSpan duration) => Math.Max(1, checked((int)Math.Ceiling(duration.TotalMinutes / 28)));
    public static long MarketBudget(int quantity, int unitLimit)
        => checked((long)quantity * unitLimit + (long)Math.Ceiling((decimal)quantity * unitLimit * .05m));
    public static string MarketRequestJson(string operationId, string world, CollectionSupply supply,
        int quantity, long remainingGilBudget, long availableGil)
    {
        var budget = Math.Min(Math.Min(remainingGilBudget, availableGil), MarketBudget(quantity, supply.UnitLimit));
        // Emptor interprets zero as unlimited, so it must never leave this bounded adapter.
        if (string.IsNullOrWhiteSpace(operationId) || string.IsNullOrWhiteSpace(world) || quantity <= 0 ||
            supply.UnitLimit <= 0 || budget <= 0) throw new InvalidOperationException("No bounded market purchase can be authorized.");
        return JsonSerializer.Serialize(new
        {
            clientRequestId = operationId, totalGilBudget = budget, world, returnToHomeWorld = false,
            items = new[] { new { itemId = supply.ItemId, quantity, maxUnitPrice = supply.UnitLimit,
                quality = supply.Quality, overshoot = "skip" } },
        });
    }
}

public sealed record CollectionSupply(uint ItemId, string Name, bool? Hq, int Target, bool Mandatory, int UnitLimit,
    int MinimumQuantity = 1)
{
    public string Quality => Hq == true ? "hq" : "nq";
    public bool ReadyForAttempt(int quantity) => !Mandatory || quantity >= MinimumQuantity;
}

public sealed record FisherFoodBonus(int Stat, bool Relative, int Value, int Maximum)
{
    public int Bonus(int baseline) => Relative ? Math.Min(Maximum, baseline * Value / 100) : Value;
    public int? Remove(int effective)
    {
        var low = Math.Max(0, effective - (Relative ? Maximum : Value));
        for (var baseline = low; baseline <= effective; baseline++)
            if (baseline + Bonus(baseline) == effective) return baseline;
        return null;
    }
}
public sealed record FisherMeal(uint ItemId, string Name, bool Hq, uint FoodId, FisherFoodBonus[] Bonuses)
{
    public int RequiredLevel { get; init; }
    public uint UseItemId => ItemId + (Hq ? 1_000_000u : 0);
    public string PriceKey => $"{ItemId}:{(Hq ? "HQ" : "NQ")}";
    public FisherObservation Apply(FisherObservation baseline) => baseline with
    {
        Gathering = baseline.Gathering + Bonuses.Where(b => b.Stat == 72).Sum(b => b.Bonus(baseline.Gathering)),
        Perception = baseline.Perception + Bonuses.Where(b => b.Stat == 73).Sum(b => b.Bonus(baseline.Perception)),
        MaximumGp = baseline.MaximumGp + Bonuses.Where(b => b.Stat == 10).Sum(b => b.Bonus(baseline.MaximumGp)),
    };
}
