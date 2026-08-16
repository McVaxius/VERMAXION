#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using VERMAXION.Models;
using Xunit;

namespace VERMAXION.Tests;

public sealed class SixItemConfigurationTests
{
    [Fact]
    public void ExistingConfigurationAcknowledgesWizardWhileNewInstallOpensIt()
    {
        var existing = SetupWizardMigrationPolicy.Decide(true, false, false);
        var fresh = SetupWizardMigrationPolicy.Decide(false, false, false);

        Assert.True(existing.Completed);
        Assert.True(existing.Migrated);
        Assert.False(existing.ShouldAutoOpen);
        Assert.False(fresh.Completed);
        Assert.True(fresh.Migrated);
        Assert.True(fresh.ShouldAutoOpen);
    }

    [Fact]
    public void MigratedWizardStateIsNotReopenedOrOverwritten()
    {
        var decision = SetupWizardMigrationPolicy.Decide(false, true, true);

        Assert.True(decision.Completed);
        Assert.True(decision.Migrated);
        Assert.False(decision.ShouldAutoOpen);
    }

    [Theory]
    [InlineData(0, false, FishingStockCatalogPolicy.DefaultVersatileLureTarget)]
    [InlineData(37, true, 37)]
    public void LegacyLureMigrationPreservesPositiveAndDisablesZero(
        int legacyTarget,
        bool expectedEnabled,
        int expectedTarget)
    {
        var values = new Dictionary<uint, FishingStockSetting>();

        Assert.True(FishingStockCatalogPolicy.MigrateLegacy(
            values,
            legacyTarget,
            FishingStockCatalogPolicy.CreateDefaultCatalog()));

        var lure = values[FishingStockItemIds.VersatileLure];
        Assert.Equal(expectedEnabled, lure.Enabled);
        Assert.Equal(expectedTarget, lure.Target);
        // 5 rows: lure + 3 optional baits + Lentils and Chestnuts (4674), the eat-on-boat food row.
        Assert.Equal(5, values.Count);
        // The food row is seeded from its catalog defaults: disabled, Target 10, reorder point Min 4.
        var food = values[FishingStockItemIds.LentilsAndChestnuts];
        Assert.False(food.Enabled);
        Assert.Equal(FishingStockCatalogPolicy.DefaultFoodTarget, food.Target);
        Assert.Equal(FishingStockCatalogPolicy.DefaultFoodMin, food.Min);
    }

    [Fact]
    public void CatalogAddRejectsDuplicatesAndRemovePurgesEveryMap()
    {
        var catalog = FishingStockCatalogPolicy.CreateDefaultCatalog();
        const uint addedItem = 12345;
        var accountDefault = FishingStockCatalogPolicy.CreateDefaultSettings();
        var character = FishingStockCatalogPolicy.CreateDefaultSettings();

        Assert.True(FishingStockCatalogPolicy.TryAdd(catalog, addedItem, 17, true));
        Assert.False(FishingStockCatalogPolicy.TryAdd(catalog, addedItem, 99, false));
        FishingStockCatalogPolicy.SyncRow(accountDefault, catalog[^1]);
        FishingStockCatalogPolicy.SyncRow(character, catalog[^1]);

        Assert.True(FishingStockCatalogPolicy.Remove(
            catalog,
            addedItem,
            new[] { accountDefault, character }));
        Assert.DoesNotContain(catalog, row => row.ItemId == addedItem);
        Assert.False(accountDefault.ContainsKey(addedItem));
        Assert.False(character.ContainsKey(addedItem));

        Assert.True(FishingStockCatalogPolicy.TryAdd(catalog, addedItem, 99, false));
        Assert.Equal(99, catalog[^1].DefaultTarget);
        Assert.False(catalog[^1].DefaultEnabled);
    }

    [Fact]
    public void CatalogNormalizationPreservesFirstOccurrenceAndOrder()
    {
        var catalog = new List<FishingStockCatalogEntry>
        {
            new() { ItemId = 9, DefaultTarget = 10, DefaultEnabled = true },
            new() { ItemId = 7, DefaultTarget = -2, DefaultEnabled = false },
            new() { ItemId = 9, DefaultTarget = 99, DefaultEnabled = false },
        };

        Assert.True(FishingStockCatalogPolicy.NormalizeCatalog(catalog));
        Assert.Equal([9u, 7u], catalog.Select(row => row.ItemId));
        Assert.Equal(10, catalog[0].DefaultTarget);
        Assert.Equal(0, catalog[1].DefaultTarget);
    }

    [Fact]
    public void ExplicitRowSyncCopiesDefaultsWithoutSharedReferences()
    {
        var row = new FishingStockCatalogEntry
        {
            ItemId = FishingStockItemIds.Krill,
            DefaultEnabled = true,
            DefaultTarget = 41,
        };
        var first = new Dictionary<uint, FishingStockSetting>();
        var second = new Dictionary<uint, FishingStockSetting>();

        FishingStockCatalogPolicy.SyncRow(first, row);
        FishingStockCatalogPolicy.SyncRow(second, row);
        first[row.ItemId].Target = 3;

        Assert.Equal(41, second[row.ItemId].Target);
        Assert.True(second[row.ItemId].Enabled);
    }

    [Fact]
    public void EnabledCatalogOrderProducesExactMissingQuantities()
    {
        var catalog = FishingStockCatalogPolicy.CreateDefaultCatalog();
        var values = FishingStockCatalogPolicy.CreateDefaultSettings();
        values[FishingStockItemIds.PlumpWorm].Enabled = true;
        values[FishingStockItemIds.PlumpWorm].Target = 10;
        var counts = new Dictionary<uint, int>
        {
            [FishingStockItemIds.VersatileLure] = 4,
            [FishingStockItemIds.PlumpWorm] = 7,
        };

        var requirements = FishingStockCatalogPolicy.BuildRequirements(
            catalog,
            values,
            itemId => counts.GetValueOrDefault(itemId));

        Assert.Equal(
            [FishingStockItemIds.VersatileLure, FishingStockItemIds.PlumpWorm],
            requirements.Select(requirement => requirement.ItemId));
        Assert.Equal([18, 3], requirements.Select(requirement => requirement.MissingQuantity));
    }

    [Fact]
    public void PartialAdsPurchasesOnlyBlockWhenVersatileLureIsZero()
    {
        var optional = new FishingStockPurchaseOutcome(
            FishingStockItemIds.Krill, 10, 0, 0, 10, false, "ADS failed");
        var lureRemaining = new FishingStockPurchaseOutcome(
            FishingStockItemIds.VersatileLure, 10, 1, 1, 10, false, "partial");
        var lureEmpty = lureRemaining with { AcquiredQuantity = 0, InventoryAfter = 0 };

        Assert.True(optional.IsPartialFailure);
        Assert.True(optional.CanContinueFishing);
        Assert.True(lureRemaining.CanContinueFishing);
        Assert.False(lureEmpty.CanContinueFishing);
    }

    [Fact]
    public void FcActiveActionNeverDecrementsButConfirmedActivationDoes()
    {
        const ulong fcId = 88;
        var ledger = new Dictionary<ulong, FcActionStockEntry>
        {
            [fcId] = new() { KnownSealSweetenerTwoCount = 3 },
        };

        Assert.Equal(
            FcBuffStockAction.Satisfied,
            FcBuffStockPolicy.Decide(true, ledger[fcId], activationFailed: false));
        Assert.Equal(3, ledger[fcId].KnownSealSweetenerTwoCount);
        Assert.True(FcBuffStockPolicy.ApplyConfirmedActivation(ledger, fcId, DateTime.UtcNow));
        Assert.Equal(2, ledger[fcId].KnownSealSweetenerTwoCount);
    }

    [Fact]
    public void FcReadFailurePreservesCacheAndForcesReconciliation()
    {
        const ulong fcId = 99;
        var originalTimestamp = new DateTime(2026, 7, 23, 12, 0, 0, DateTimeKind.Utc);
        var ledger = new Dictionary<ulong, FcActionStockEntry>
        {
            [fcId] = new()
            {
                KnownSealSweetenerTwoCount = 4,
                LastVerifiedAtUtc = originalTimestamp,
            },
        };

        Assert.Equal(
            FcBuffStockAction.Reconcile,
            FcBuffStockPolicy.Decide(false, ledger[fcId], activationFailed: true));
        Assert.False(FcBuffStockPolicy.ApplyReconciliation(
            ledger,
            fcId,
            FcActionInventoryReadResult.Failed("unreadable"),
            originalTimestamp.AddHours(1)));
        Assert.Equal(4, ledger[fcId].KnownSealSweetenerTwoCount);
        Assert.Equal(originalTimestamp, ledger[fcId].LastVerifiedAtUtc);
    }

    [Fact]
    public void FcSuccessfulZeroReconciliationPersistsByFreeCompanyIdAcrossReload()
    {
        const ulong fcId = 123456;
        var verified = new DateTime(2026, 7, 23, 15, 30, 0, DateTimeKind.Utc);
        var ledger = new Dictionary<ulong, FcActionStockEntry>();

        Assert.True(FcBuffStockPolicy.ApplyReconciliation(
            ledger,
            fcId,
            FcActionInventoryReadResult.Succeeded(0),
            verified));
        var json = JsonSerializer.Serialize(ledger);
        var reloaded = JsonSerializer.Deserialize<Dictionary<ulong, FcActionStockEntry>>(json)!;

        Assert.Equal(0, reloaded[fcId].KnownSealSweetenerTwoCount);
        Assert.Equal(verified, reloaded[fcId].LastVerifiedAtUtc);
        Assert.Equal(FcBuffStockAction.Reconcile, FcBuffStockPolicy.Decide(false, reloaded[fcId], false));
    }
}
