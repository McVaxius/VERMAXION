#nullable enable

using System.Linq;
using VERMAXION.Models;
using Xunit;

namespace VERMAXION.Tests;

/// <summary>Covers which restock purchases may hold the vendor's shop open for the next one. The rule
/// exists because closing and re-interacting leaves a stale NPC event and the game swallows the THIRD
/// consecutive interact with one NPC — so the hold must cover every dock purchase, and must never span the
/// food vendor, who is a different shop 250y away.</summary>
public sealed class FishingStockShopChainTests
{
    [Theory]
    [InlineData(FishingStockItemIds.VersatileLure)]
    [InlineData(FishingStockItemIds.Ragworm)]
    [InlineData(FishingStockItemIds.Krill)]
    [InlineData(FishingStockItemIds.PlumpWorm)]
    public void EveryDockVendorItemChains(uint itemId) =>
        Assert.True(FishingStockItemIds.IsDockVendorItem(itemId));

    [Fact]
    public void TheFoodItemNeverChains() =>
        Assert.False(FishingStockItemIds.IsDockVendorItem(FishingStockItemIds.LentilsAndChestnuts));

    [Fact]
    public void UnknownCustomItemDoesNotInheritTheDockShop() =>
        Assert.False(FishingStockItemIds.IsDockVendorItem(123456));

    [Fact]
    public void EveryShippedStockRowExceptFoodIsADockItem()
    {
        // Guards the shipped split. Custom catalog entries fail closed and do not inherit a held shop unless
        // their item ID is explicitly classified as belonging to this vendor.
        var catalog = FishingStockCatalogPolicy.CreateDefaultCatalog();
        var dockItems = catalog.Where(row => FishingStockItemIds.IsDockVendorItem(row.ItemId)).ToList();

        Assert.Equal(catalog.Count - 1, dockItems.Count);
        Assert.DoesNotContain(dockItems, row => row.ItemId == FishingStockItemIds.LentilsAndChestnuts);
    }

    [Fact]
    public void FoodIsStillOrderedFirstSoTheHoldStartsAtTheDock()
    {
        // The hold is turned off for the food dispatch and on for the dock ones. Food running first is what
        // keeps that to a single transition; if this ordering ever changes, the walk to Gerulf would happen
        // mid-chain and the shop would have to be released and re-opened.
        var catalog = FishingStockCatalogPolicy.CreateDefaultCatalog();
        var values = FishingStockCatalogPolicy.CreateDefaultSettings();
        foreach (var setting in values.Values)
        {
            setting.Enabled = true;
            setting.Min = 0;
        }

        var requirements = FishingStockCatalogPolicy.BuildRequirements(catalog, values, _ => 0);

        Assert.Equal(FishingStockItemIds.LentilsAndChestnuts, requirements[0].ItemId);
        Assert.All(requirements.Skip(1), r => Assert.True(FishingStockItemIds.IsDockVendorItem(r.ItemId)));
    }
}
