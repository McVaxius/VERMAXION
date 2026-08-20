using System;
using System.Collections.Generic;
using System.Linq;

namespace VERMAXION;

public enum PostProcessTaskPhase
{
    BeforeAR,
    AfterAR,
}

public static class PostProcessTaskOrder
{
    public const string RefillListings = AutomationCatalog.RefillListings;
    public const string RetainerEquipping = AutomationCatalog.RetainerEquipping;
    public const string AlliedSociety = AutomationCatalog.AlliedSociety;
    public const string AfterArPark = AutomationCatalog.AfterArPark;
    public const string FCBuffRefill = AutomationCatalog.FCBuffRefill;
    public const string VendorStock = AutomationCatalog.VendorStock;
    public const string RegisterRegistrables = AutomationCatalog.RegisterRegistrables;
    public const string GearUpdater = AutomationCatalog.GearUpdater;
    public const string HighestCombatJob = AutomationCatalog.HighestCombatJob;
    public const string CurrentJobEquipment = AutomationCatalog.CurrentJobEquipment;
    public const string SeasonalGear = AutomationCatalog.SeasonalGear;
    public const string MinionRoulette = AutomationCatalog.MinionRoulette;
    public const string VerminionQueue = AutomationCatalog.VerminionQueue;
    public const string MiniCactpot = AutomationCatalog.MiniCactpot;
    public const string JumboCactpot = AutomationCatalog.JumboCactpot;
    public const string FashionReport = AutomationCatalog.FashionReport;
    public const string ChocoboRacing = AutomationCatalog.ChocoboRacing;
    public const string LootGoblinMapGather = AutomationCatalog.LootGoblinMapGather;
    public const string NagYourMom = AutomationCatalog.NagYourMom;
    public const string NagYourDad = AutomationCatalog.NagYourDad;

    public const string LegacyFishing = AutomationCatalog.Fishing;

    public static readonly IReadOnlyList<TaskDefinition> Definitions = AutomationCatalog.EngineTasks
        .Select(definition => new TaskDefinition(
            definition.Id,
            definition.Label,
            definition.CadenceLabel,
            definition.Maturity,
            definition.OwnershipLabel))
        .ToList();

    public static readonly IReadOnlyList<string> NewlyDispatchableIds =
    [
        GearUpdater,
        HighestCombatJob,
        CurrentJobEquipment,
        AlliedSociety,
        SeasonalGear,
        MinionRoulette,
        RetainerEquipping,
    ];

    public static readonly IReadOnlyList<string> DefaultOrder =
    [
        RefillListings,
        RetainerEquipping,
        FCBuffRefill,
        VendorStock,
        RegisterRegistrables,
        GearUpdater,
        HighestCombatJob,
        CurrentJobEquipment,
        AlliedSociety,
        SeasonalGear,
        MinionRoulette,
        VerminionQueue,
        MiniCactpot,
        JumboCactpot,
        FashionReport,
        ChocoboRacing,
        LootGoblinMapGather,
        NagYourMom,
        NagYourDad,
        AfterArPark,
    ];

    private static readonly HashSet<string> KnownIds = Definitions
        .Select(definition => definition.Id)
        .ToHashSet(StringComparer.Ordinal);

    public static List<string> Normalize(IEnumerable<string>? order)
    {
        var normalized = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var id in order ?? [])
        {
            if (!KnownIds.Contains(id) || !seen.Add(id))
                continue;

            normalized.Add(id);
        }

        if (normalized.Count == 0)
            return DefaultOrder.ToList();

        var missingNewIds = NewlyDispatchableIds.Where(id => seen.Add(id)).ToList();
        if (missingNewIds.Count > 0)
        {
            var insertionIndex = FindNewTaskInsertionIndex(normalized);
            normalized.InsertRange(insertionIndex, missingNewIds);
        }

        foreach (var id in DefaultOrder)
        {
            if (seen.Add(id))
                normalized.Add(id);
        }

        return normalized;
    }

    public static bool Normalize(Configuration config)
    {
        var changed = false;
        var normalized = Normalize(config.PostProcessTaskOrder);
        if (config.PostProcessTaskOrder == null || !config.PostProcessTaskOrder.SequenceEqual(normalized))
        {
            config.PostProcessTaskOrder = normalized;
            changed = true;
        }

        config.PostProcessTaskPlacement ??= new Dictionary<string, PostProcessTaskPhase>();
        foreach (var id in config.PostProcessTaskPlacement.Keys.Where(id => !KnownIds.Contains(id)).ToList())
        {
            config.PostProcessTaskPlacement.Remove(id);
            changed = true;
        }

        foreach (var id in DefaultOrder)
        {
            if (config.PostProcessTaskPlacement.ContainsKey(id))
                continue;

            config.PostProcessTaskPlacement[id] = GetDefaultPhase(id);
            changed = true;
        }

        return changed;
    }

    public static void ResetToDefault(Configuration config)
    {
        config.PostProcessTaskOrder = DefaultOrder.ToList();
        config.PostProcessTaskPlacement = CreateDefaultPlacement();
    }

    public static string GetLabel(string id)
        => AutomationCatalog.ById.TryGetValue(id, out var definition) ? definition.Label : id;

    public static PostProcessTaskPhase GetDefaultPhase(string id)
        => AutomationCatalog.ById.TryGetValue(id, out var definition)
            ? definition.DefaultPhase
            : PostProcessTaskPhase.AfterAR;

    public static Dictionary<string, PostProcessTaskPhase> CreateDefaultPlacement()
        => DefaultOrder.ToDictionary(id => id, GetDefaultPhase, StringComparer.Ordinal);

    public static IReadOnlyList<string> GetLane(
        IEnumerable<string>? order,
        IReadOnlyDictionary<string, PostProcessTaskPhase>? placement,
        PostProcessTaskPhase phase)
        => Normalize(order)
            .Where(id => GetPhase(placement, id) == phase)
            .ToList();

    public static List<string> MoveWithinLane(
        IEnumerable<string>? order,
        IReadOnlyDictionary<string, PostProcessTaskPhase>? placement,
        string taskId,
        int direction)
    {
        var normalized = Normalize(order);
        if (direction == 0 || !KnownIds.Contains(taskId))
            return normalized;

        var phase = GetPhase(placement, taskId);
        var lane = GetLane(normalized, placement, phase);
        var laneIndex = lane.IndexOf(taskId);
        var targetLaneIndex = laneIndex + Math.Sign(direction);
        if (laneIndex < 0 || targetLaneIndex < 0 || targetLaneIndex >= lane.Count)
            return normalized;

        var firstIndex = normalized.IndexOf(taskId);
        var secondIndex = normalized.IndexOf(lane[targetLaneIndex]);
        (normalized[firstIndex], normalized[secondIndex]) = (normalized[secondIndex], normalized[firstIndex]);
        return normalized;
    }

    public static Dictionary<string, PostProcessTaskPhase> ChangePhase(
        IReadOnlyDictionary<string, PostProcessTaskPhase>? placement,
        string taskId,
        PostProcessTaskPhase phase)
    {
        var result = CreateDefaultPlacement();
        foreach (var pair in placement ?? new Dictionary<string, PostProcessTaskPhase>())
        {
            if (KnownIds.Contains(pair.Key))
                result[pair.Key] = pair.Value;
        }
        if (KnownIds.Contains(taskId))
            result[taskId] = phase;
        return result;
    }

    private static PostProcessTaskPhase GetPhase(
        IReadOnlyDictionary<string, PostProcessTaskPhase>? placement,
        string taskId)
        => placement != null && placement.TryGetValue(taskId, out var phase)
            ? phase
            : GetDefaultPhase(taskId);

    private static int FindNewTaskInsertionIndex(IReadOnlyList<string> normalized)
    {
        var registerIndex = normalized.IndexOf(RegisterRegistrables);
        if (registerIndex >= 0)
            return registerIndex + 1;

        var firstDefaultSuccessor = DefaultOrder
            .SkipWhile(id => id != VerminionQueue)
            .Select(normalized.IndexOf)
            .Where(index => index >= 0)
            .DefaultIfEmpty(normalized.Count)
            .Min();
        return firstDefaultSuccessor;
    }

    public sealed record TaskDefinition(
        string Id,
        string Label,
        string Cadence,
        AutomationMaturity Maturity,
        string Owner);
}

internal static class ReadOnlyListExtensions
{
    public static int IndexOf<T>(this IReadOnlyList<T> values, T value)
    {
        var comparer = EqualityComparer<T>.Default;
        for (var index = 0; index < values.Count; index++)
        {
            if (comparer.Equals(values[index], value))
                return index;
        }

        return -1;
    }
}
