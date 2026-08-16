using System;
using System.Collections.Generic;
using System.Linq;

namespace VERMAXION.Models;

public static class FishingStockItemIds
{
    public const uint Ragworm = 29714;
    public const uint Krill = 29715;
    public const uint PlumpWorm = 29716;
    public const uint VersatileLure = 29717;

    /// <summary>Lentils and Chestnuts (34 gil, 30-min Well-Fed, no level requirement) — food for the
    /// eat-on-boat feature. The fisher ROTATES between windows (whichever venture-due toon AR logs in
    /// boards), so stocking food at the same pre-boat vendor stop is the only point that always feeds
    /// the right toon.</summary>
    public const uint LentilsAndChestnuts = 4674;

    /// <summary>Every stock item except the food is bought from the ONE dock vendor (Merchant &amp; Mender,
    /// gil shop 263015 — the baits are rows 0-2 of it); the food is Gerulf, a 250y walk away.</summary>
    /// <remarks>
    /// This split decides which purchases hold the vendor's shop open for the next one, which is the fix
    /// for a reproducible failure on the last bait. Closing the shop and re-interacting
    /// per item leaves the character in an unfinished NPC event each time; the game tolerates ONE stale
    /// event but not two, so the third consecutive interact with the same NPC is silently ignored and the
    /// purchase dies with "did not open a supported shop menu". Reproduction showed that runs needing a
    /// third dock purchase failed there while the same item succeeded when ordered second. Reusing one open
    /// shop means one interact for the whole dock stop.
    ///
    /// A held shop can only be reused by the SAME shop, so the food purchase must never inherit the hold:
    /// Gerulf is a different shop at the other end of a 250y walk.
    /// </remarks>
    public static bool IsDockVendorItem(uint itemId) => itemId is
        Ragworm or Krill or PlumpWorm or VersatileLure;
}

[Serializable]
public sealed class FishingStockCatalogEntry
{
    public uint ItemId { get; set; }
    public int DefaultTarget { get; set; }

    /// <summary>Reorder point default. 0 (or less) = buy whenever inventory is below Target (legacy). &gt;0 =
    /// only reorder once inventory drops to &lt;= this, then buy back up to Target.</summary>
    public int DefaultMin { get; set; }
    public bool DefaultEnabled { get; set; }

    public FishingStockCatalogEntry Clone() => new()
    {
        ItemId = ItemId,
        DefaultTarget = DefaultTarget,
        DefaultMin = DefaultMin,
        DefaultEnabled = DefaultEnabled,
    };
}

[Serializable]
public sealed class FishingStockSetting
{
    public bool Enabled { get; set; }
    public int Target { get; set; }

    /// <summary>Reorder point. 0 (or less) = buy whenever inventory is below Target (legacy behavior, so
    /// existing configs that carry no Min keep restocking). &gt;0 = only start buying once inventory has
    /// dropped to &lt;= Min, then buy back up to Target — avoids topping up a few every voyage.</summary>
    public int Min { get; set; }

    public FishingStockSetting Clone() => new()
    {
        Enabled = Enabled,
        Target = Target,
        Min = Min,
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
    // ~2 lentils per window (lobby + at most one mid-voyage refresh); 10 covers ~5 windows for a
    // character. Min=1: only restock when the bag is down to the last lentil (or none) — the vendor stop
    // stays a no-op on nearly every visit.
    public const int DefaultFoodTarget = 10;
    public const int DefaultFoodMin = 1;

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
        new()
        {
            ItemId = FishingStockItemIds.LentilsAndChestnuts,
            DefaultEnabled = false,
            DefaultTarget = DefaultFoodTarget,
            DefaultMin = DefaultFoodMin,
        },
    ];

    public static Dictionary<uint, FishingStockSetting> CreateDefaultSettings() =>
        CreateDefaultCatalog().ToDictionary(
            row => row.ItemId,
            row => new FishingStockSetting
            {
                Enabled = row.DefaultEnabled,
                Target = row.DefaultTarget,
                Min = row.DefaultMin,
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

            var minValue = Math.Max(0, row.DefaultMin);
            if (minValue != row.DefaultMin)
            {
                row.DefaultMin = minValue;
                changed = true;
            }
        }

        return changed;
    }

    public static bool TryAdd(
        IList<FishingStockCatalogEntry> catalog,
        uint itemId,
        int defaultTarget,
        bool defaultEnabled,
        int defaultMin = 0)
    {
        if (itemId == 0 || catalog.Any(row => row.ItemId == itemId))
            return false;

        catalog.Add(new FishingStockCatalogEntry
        {
            ItemId = itemId,
            DefaultTarget = Math.Max(0, defaultTarget),
            DefaultEnabled = defaultEnabled,
            DefaultMin = Math.Max(0, defaultMin),
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
                    Min = Math.Max(0, row.DefaultMin),
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
                    Min = Math.Max(0, row.DefaultMin),
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
            Min = Math.Max(0, row.DefaultMin),
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
            // Reorder point: Min<=0 keeps legacy behavior (buy whenever below Target); Min>0 only reorders
            // once inventory has dropped to <= Min (clamped to Target so a misconfigured Min>Target can't
            // suppress restocking), then buys back up to Target.
            var reorderPoint = setting.Min > 0 ? Math.Min(setting.Min, target) : target;
            if (current > reorderPoint)
                continue;
            result.Add(new FishingStockRequirement(
                row.ItemId,
                current,
                target,
                Math.Max(0, target - current),
                row.ItemId == FishingStockItemIds.VersatileLure));
        }

        // Purchase order: FOOD FIRST — the shared ADS budget and the registration clock starve whatever
        // sits last, so a wedged bait buy ahead of food can consume the whole window before food is
        // bought. Putting food first would normally risk a cold-resolution teleport (ADS resolving a
        // cross-zone vendor for the first purchase); FishingService prevents that with the navmesh-ready
        // gate plus the walk to the food vendor before the food dispatch (TryEnsureNavmeshReady /
        // TryWalkToFoodVendor), not by ordering. OrderBy is stable; everything else keeps catalog order.
        return result
            .OrderBy(r => r.ItemId == FishingStockItemIds.LentilsAndChestnuts ? 0 : 1)
            .ToList();
    }

}
