using System;
using System.Collections.Generic;
using System.Linq;

namespace VERMAXION.Models;

public static class FishingStockItemIds
{
    public const uint PlumpWorm = 29714;
    public const uint Ragworm = 29715;
    public const uint Krill = 29716;
    public const uint VersatileLure = 29717;
}

[Serializable]
public sealed class FishingStockCatalogEntry
{
    public uint ItemId { get; set; }
    public int DefaultTarget { get; set; }
    public bool DefaultEnabled { get; set; }

    public FishingStockCatalogEntry Clone() => new()
    {
        ItemId = ItemId,
        DefaultTarget = DefaultTarget,
        DefaultEnabled = DefaultEnabled,
    };
}

[Serializable]
public sealed class FishingStockSetting
{
    public bool Enabled { get; set; }
    public int Target { get; set; }

    public FishingStockSetting Clone() => new()
    {
        Enabled = Enabled,
        Target = Target,
    };
}

public readonly record struct FishingStockRequirement(
    uint ItemId,
    int InventoryCount,
    int Target,
    int MissingQuantity,
    bool IsVersatileLure);

public readonly record struct FishingStockPurchaseOutcome(
    uint ItemId,
    int RequestedQuantity,
    int AcquiredQuantity,
    int InventoryAfter,
    int Target,
    bool AdsSucceeded,
    string Failure)
{
    public bool TargetReached => InventoryAfter >= Target;
    public bool CanContinueFishing =>
        ItemId != FishingStockItemIds.VersatileLure || InventoryAfter > 0;
    public bool IsPartialFailure => !TargetReached;
}

public static class FishingStockCatalogPolicy
{
    public const int DefaultVersatileLureTarget = 22;
    public const int DefaultOptionalBaitTarget = 99;

    public static List<FishingStockCatalogEntry> CreateDefaultCatalog() =>
    [
        new()
        {
            ItemId = FishingStockItemIds.VersatileLure,
            DefaultEnabled = true,
            DefaultTarget = DefaultVersatileLureTarget,
        },
        new()
        {
            ItemId = FishingStockItemIds.PlumpWorm,
            DefaultEnabled = false,
            DefaultTarget = DefaultOptionalBaitTarget,
        },
        new()
        {
            ItemId = FishingStockItemIds.Ragworm,
            DefaultEnabled = false,
            DefaultTarget = DefaultOptionalBaitTarget,
        },
        new()
        {
            ItemId = FishingStockItemIds.Krill,
            DefaultEnabled = false,
            DefaultTarget = DefaultOptionalBaitTarget,
        },
    ];

    public static Dictionary<uint, FishingStockSetting> CreateDefaultSettings() =>
        CreateDefaultCatalog().ToDictionary(
            row => row.ItemId,
            row => new FishingStockSetting
            {
                Enabled = row.DefaultEnabled,
                Target = row.DefaultTarget,
            });

    public static bool NormalizeCatalog(List<FishingStockCatalogEntry>? catalog)
    {
        if (catalog == null)
            return false;

        var changed = false;
        var seen = new HashSet<uint>();
        for (var index = 0; index < catalog.Count; index++)
        {
            var row = catalog[index];
            if (row == null || row.ItemId == 0 || !seen.Add(row.ItemId))
            {
                catalog.RemoveAt(index);
                index--;
                changed = true;
                continue;
            }

            var target = Math.Max(0, row.DefaultTarget);
            if (target != row.DefaultTarget)
            {
                row.DefaultTarget = target;
                changed = true;
            }
        }

        return changed;
    }

    public static bool TryAdd(
        IList<FishingStockCatalogEntry> catalog,
        uint itemId,
        int defaultTarget,
        bool defaultEnabled)
    {
        if (itemId == 0 || catalog.Any(row => row.ItemId == itemId))
            return false;

        catalog.Add(new FishingStockCatalogEntry
        {
            ItemId = itemId,
            DefaultTarget = Math.Max(0, defaultTarget),
            DefaultEnabled = defaultEnabled,
        });
        return true;
    }

    public static bool Remove(
        IList<FishingStockCatalogEntry> catalog,
        uint itemId,
        IEnumerable<IDictionary<uint, FishingStockSetting>> settings)
    {
        var row = catalog.FirstOrDefault(entry => entry.ItemId == itemId);
        if (row == null)
            return false;

        catalog.Remove(row);
        foreach (var values in settings)
            values.Remove(itemId);
        return true;
    }

    public static bool MigrateLegacy(
        IDictionary<uint, FishingStockSetting> values,
        int legacyLureTarget,
        IEnumerable<FishingStockCatalogEntry> catalog)
    {
        var changed = false;
        foreach (var row in catalog)
        {
            if (row.ItemId == FishingStockItemIds.VersatileLure)
            {
                var migrated = new FishingStockSetting
                {
                    Enabled = legacyLureTarget > 0,
                    Target = legacyLureTarget > 0
                        ? legacyLureTarget
                        : DefaultVersatileLureTarget,
                };
                if (!values.TryGetValue(row.ItemId, out var existing) ||
                    existing.Enabled != migrated.Enabled ||
                    existing.Target != migrated.Target)
                {
                    values[row.ItemId] = migrated;
                    changed = true;
                }
                continue;
            }

            if (values.ContainsKey(row.ItemId))
                continue;

            values[row.ItemId] = new FishingStockSetting
                {
                    Enabled = row.DefaultEnabled,
                    Target = Math.Max(0, row.DefaultTarget),
                };
            changed = true;
        }

        return changed;
    }

    public static void SyncRow(
        IDictionary<uint, FishingStockSetting> values,
        FishingStockCatalogEntry row)
    {
        values[row.ItemId] = new FishingStockSetting
        {
            Enabled = row.DefaultEnabled,
            Target = Math.Max(0, row.DefaultTarget),
        };
    }

    public static IReadOnlyList<FishingStockRequirement> BuildRequirements(
        IEnumerable<FishingStockCatalogEntry> catalog,
        IReadOnlyDictionary<uint, FishingStockSetting> values,
        Func<uint, int> inventoryCount)
    {
        var result = new List<FishingStockRequirement>();
        foreach (var row in catalog)
        {
            if (!values.TryGetValue(row.ItemId, out var setting) || !setting.Enabled)
                continue;

            var target = Math.Max(0, setting.Target);
            var current = Math.Max(0, inventoryCount(row.ItemId));
            result.Add(new FishingStockRequirement(
                row.ItemId,
                current,
                target,
                Math.Max(0, target - current),
                row.ItemId == FishingStockItemIds.VersatileLure));
        }

        return result;
    }
}
