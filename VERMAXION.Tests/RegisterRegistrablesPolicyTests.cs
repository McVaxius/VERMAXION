using System;
using System.Collections.Generic;
using System.Text.Json;
using VERMAXION.Models;
using Xunit;

namespace VERMAXION.Tests;

public sealed class RegisterRegistrablesPolicyTests
{
    [Theory]
    [InlineData(1322u, RegistrableCategory.Mount)]
    [InlineData(853u, RegistrableCategory.Minion)]
    [InlineData(20086u, RegistrableCategory.FashionAccessory)]
    [InlineData(37312u, RegistrableCategory.Facewear)]
    [InlineData(25183u, RegistrableCategory.OrchestrionRoll)]
    [InlineData(2633u, RegistrableCategory.EmoteHairstyle)]
    [InlineData(1013u, RegistrableCategory.Barding)]
    [InlineData(3357u, RegistrableCategory.TripleTriadCard)]
    public void AdsDirectActionIdsAreClassified(uint actionId, RegistrableCategory expected)
    {
        Assert.True(RegistrableRegistrationPolicy.TryClassifyDirectAction(
            actionId,
            isFadedOrchestrionCopy: false,
            out var actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(94u)]
    [InlineData(999999u)]
    public void UnrelatedActionsAreRejected(uint actionId)
    {
        Assert.False(RegistrableRegistrationPolicy.TryClassifyDirectAction(
            actionId,
            isFadedOrchestrionCopy: false,
            out _));
    }

    [Fact]
    public void FadedOrchestrionMaterialIsNeverDirectlyRegistrable()
    {
        Assert.False(RegistrableRegistrationPolicy.TryClassifyDirectAction(
            25183,
            isFadedOrchestrionCopy: true,
            out _));
    }

    [Fact]
    public void InventorySnapshotPreservesBagAndSlotOrderWhileDeduplicating()
    {
        var bags = new[]
        {
            Bag((10, 1), (20, 2), (10, 3), (0, 0)),
            Bag((30, 1), (20, 1)),
            Bag((40, 1)),
            Bag((50, 0), (10, 1)),
        };

        Assert.True(RegistrableRegistrationPolicy.TryBuildInventoryItemSnapshot(
            bags,
            out var itemIds));
        Assert.Equal([10u, 20u, 30u, 40u], itemIds);
    }

    [Fact]
    public void InventorySnapshotRequiresExactlyFourReadableBags()
    {
        var unreadable = new[]
        {
            Bag((10, 1)),
            new RegistrableInventoryBagSnapshot(false, []),
            Bag(),
            Bag(),
        };

        Assert.False(RegistrableRegistrationPolicy.TryBuildInventoryItemSnapshot(
            unreadable,
            out var unreadableItems));
        Assert.Empty(unreadableItems);
        Assert.False(RegistrableRegistrationPolicy.TryBuildInventoryItemSnapshot(
            [Bag(), Bag(), Bag()],
            out var shortItems));
        Assert.Empty(shortItems);
    }

    [Fact]
    public void RegisteredItemsAreFilteredWithoutReorderingLockedItems()
    {
        var states = new Dictionary<uint, RegistrableUnlockState>
        {
            [10] = RegistrableUnlockState.Unlocked,
            [20] = RegistrableUnlockState.Locked,
            [30] = RegistrableUnlockState.Unlocked,
            [40] = RegistrableUnlockState.Locked,
        };

        Assert.True(RegistrableRegistrationPolicy.TryFilterLockedItems(
            [10, 20, 30, 40],
            itemId => states[itemId],
            out var locked));
        Assert.Equal([20u, 40u], locked);
    }

    [Fact]
    public void UnreadableRegistrationStateFailsQueueFilteringClosed()
    {
        Assert.False(RegistrableRegistrationPolicy.TryFilterLockedItems(
            [10, 20],
            itemId => itemId == 20
                ? RegistrableUnlockState.Unreadable
                : RegistrableUnlockState.Locked,
            out var locked));
        Assert.Empty(locked);
    }

    [Fact]
    public void AutomaticModeUsesOnlyInventoryAndManualModeUsesOnlyPersonalList()
    {
        Assert.Equal(
            [30u, 40u],
            RegistrableRegistrationPolicy.SelectItemSource(
                automaticInventoryMode: true,
                personalItems: [10, 20],
                automaticItems: [30, 40]));
        Assert.Equal(
            [10u, 20u],
            RegistrableRegistrationPolicy.SelectItemSource(
                automaticInventoryMode: false,
                personalItems: [10, 20],
                automaticItems: [30, 40]));
    }

    [Fact]
    public void AutomaticModeCanStartWithEmptyPersonalListButManualModeCannot()
    {
        Assert.True(RegistrableRegistrationPolicy.CanStart(
            featureEnabled: true,
            automaticInventoryMode: true,
            personalItemCount: 0));
        Assert.False(RegistrableRegistrationPolicy.CanStart(
            featureEnabled: true,
            automaticInventoryMode: false,
            personalItemCount: 0));
        Assert.False(RegistrableRegistrationPolicy.CanStart(
            featureEnabled: false,
            automaticInventoryMode: true,
            personalItemCount: 0));
    }

    [Theory]
    [InlineData(RegistrableUnlockState.Unreadable, 1, RegistrablePreUseDecision.FailUnreadable)]
    [InlineData(RegistrableUnlockState.Unlocked, 2, RegistrablePreUseDecision.AdvanceUnlocked)]
    [InlineData(RegistrableUnlockState.Locked, 0, RegistrablePreUseDecision.AdvanceMissing)]
    [InlineData(RegistrableUnlockState.Locked, 1, RegistrablePreUseDecision.Use)]
    public void RegistrationIsRecheckedImmediatelyBeforeUse(
        RegistrableUnlockState state,
        int remainingQuantity,
        RegistrablePreUseDecision expected)
    {
        Assert.Equal(
            expected,
            RegistrableRegistrationPolicy.EvaluateBeforeUse(state, remainingQuantity));
    }

    [Fact]
    public void VerificationWaitsTheFullSevenSeconds()
    {
        Assert.Equal(
            RegistrablePostUseDecision.Wait,
            RegistrableRegistrationPolicy.EvaluateAfterUse(
                TimeSpan.FromMilliseconds(6999),
                RegistrableUnlockState.Locked,
                remainingQuantity: 1,
                attempts: 1));
        Assert.Equal(
            RegistrablePostUseDecision.Retry,
            RegistrableRegistrationPolicy.EvaluateAfterUse(
                TimeSpan.FromSeconds(7),
                RegistrableUnlockState.Locked,
                remainingQuantity: 1,
                attempts: 1));
    }

    [Fact]
    public void VerifiedRegistrationAdvancesEvenWhenDuplicateCopiesRemain()
    {
        Assert.Equal(
            RegistrablePostUseDecision.AdvanceUnlocked,
            RegistrableRegistrationPolicy.EvaluateAfterUse(
                TimeSpan.FromSeconds(7),
                RegistrableUnlockState.Unlocked,
                remainingQuantity: 2,
                attempts: 1));
    }

    [Fact]
    public void LockedPresentItemExhaustsAfterThreeAttempts()
    {
        Assert.Equal(
            RegistrablePostUseDecision.Retry,
            RegistrableRegistrationPolicy.EvaluateAfterUse(
                TimeSpan.FromSeconds(7),
                RegistrableUnlockState.Locked,
                remainingQuantity: 1,
                attempts: 2));
        Assert.Equal(
            RegistrablePostUseDecision.Exhaust,
            RegistrableRegistrationPolicy.EvaluateAfterUse(
                TimeSpan.FromSeconds(7),
                RegistrableUnlockState.Locked,
                remainingQuantity: 1,
                attempts: 3));
    }

    [Fact]
    public void UnreadablePostUseRegistrationStateFailsClosed()
    {
        Assert.Equal(
            RegistrablePostUseDecision.FailUnreadable,
            RegistrableRegistrationPolicy.EvaluateAfterUse(
                TimeSpan.FromSeconds(7),
                RegistrableUnlockState.Unreadable,
                remainingQuantity: 1,
                attempts: 1));
    }

    [Fact]
    public void ConfigurationDefaultsFalseAndSurvivesCloneAndDefaultCopy()
    {
        Assert.False(new CharacterConfig().RegisterUnregisteredItemsFromInventory);
        Assert.False(
            JsonSerializer.Deserialize<CharacterConfig>("{}")!
                .RegisterUnregisteredItemsFromInventory);

        var account = new AccountConfig();
        account.DefaultConfig.RegisterUnregisteredItemsFromInventory = true;
        var clonedDefault = account.DefaultConfig.Clone();
        var copiedCharacter = account.DefaultConfig.Clone();

        Assert.True(clonedDefault.RegisterUnregisteredItemsFromInventory);
        Assert.True(copiedCharacter.RegisterUnregisteredItemsFromInventory);
    }

    private static RegistrableInventoryBagSnapshot Bag(
        params (uint ItemId, int Quantity)[] slots)
    {
        var snapshots = new List<RegistrableInventorySlotSnapshot>(slots.Length);
        foreach (var slot in slots)
            snapshots.Add(new RegistrableInventorySlotSnapshot(slot.ItemId, slot.Quantity));
        return new RegistrableInventoryBagSnapshot(true, snapshots);
    }
}
