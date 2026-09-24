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
    [InlineData(false, false, false, 0, false)]
    [InlineData(false, false, false, 1, false)]
    [InlineData(false, false, true, 0, false)]
    [InlineData(false, false, true, 1, false)]
    [InlineData(false, true, false, 0, false)]
    [InlineData(false, true, false, 1, true)]
    [InlineData(false, true, true, 0, true)]
    [InlineData(false, true, true, 1, true)]
    [InlineData(true, false, false, 0, false)]
    [InlineData(true, false, false, 1, true)]
    [InlineData(true, false, true, 0, true)]
    [InlineData(true, false, true, 1, true)]
    [InlineData(true, true, false, 0, false)]
    [InlineData(true, true, false, 1, true)]
    [InlineData(true, true, true, 0, true)]
    [InlineData(true, true, true, 1, true)]
    public void ManualStartBypassesOnlyScheduledEnablement(
        bool manualStart,
        bool featureEnabled,
        bool automaticInventoryMode,
        int personalItemCount,
        bool expected)
    {
        Assert.Equal(expected, RegistrableRegistrationPolicy.CanStart(
            featureEnabled, automaticInventoryMode, personalItemCount, manualStart));

        var reason = RegistrableRegistrationPolicy.GetStartBlockedReason(
            featureEnabled, automaticInventoryMode, personalItemCount, manualStart);
        if (expected)
            Assert.Null(reason);
        else if (!manualStart && !featureEnabled)
            Assert.Contains("disabled for scheduled runs", reason);
        else
            Assert.Contains("personal list is empty", reason);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConfigurationDefaultsToInventoryAndPreservesSavedSourceAndList(bool savedSource)
    {
        Assert.True(new CharacterConfig().RegisterUnregisteredItemsFromInventory);
        Assert.True(CharacterConfig.CreateNew().RegisterUnregisteredItemsFromInventory);
        Assert.True(
            JsonSerializer.Deserialize<CharacterConfig>("{}")!
                .RegisterUnregisteredItemsFromInventory);

        var account = new AccountConfig();
        Assert.True(account.DefaultConfig.RegisterUnregisteredItemsFromInventory);
        account.DefaultConfig.RegisterUnregisteredItemsFromInventory = savedSource;
        account.DefaultConfig.PersonalRegistrableItems = [10, 20];
        account.Characters["character-a"] = new CharacterConfig
        {
            RegisterUnregisteredItemsFromInventory = !savedSource,
            PersonalRegistrableItems = [30, 40],
        };

        var restored = JsonSerializer.Deserialize<AccountConfig>(JsonSerializer.Serialize(account))!;
        Assert.Equal(savedSource, restored.DefaultConfig.RegisterUnregisteredItemsFromInventory);
        Assert.Equal([10u, 20u], restored.DefaultConfig.PersonalRegistrableItems);
        Assert.Equal(!savedSource, restored.Characters["character-a"].RegisterUnregisteredItemsFromInventory);
        Assert.Equal([30u, 40u], restored.Characters["character-a"].PersonalRegistrableItems);

        var copiedCharacter = restored.DefaultConfig.Clone();
        Assert.Equal(savedSource, copiedCharacter.RegisterUnregisteredItemsFromInventory);
        Assert.Equal([10u, 20u], copiedCharacter.PersonalRegistrableItems);
        Assert.NotSame(restored.DefaultConfig.PersonalRegistrableItems, copiedCharacter.PersonalRegistrableItems);

        var clonedCharacter = restored.Characters["character-a"].Clone();
        Assert.Equal(!savedSource, clonedCharacter.RegisterUnregisteredItemsFromInventory);
        Assert.Equal([30u, 40u], clonedCharacter.PersonalRegistrableItems);
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
