using System;
using System.Collections.Generic;
using System.Linq;

namespace VERMAXION.Models;

public enum RegistrableCategory
{
    Mount,
    Minion,
    FashionAccessory,
    Facewear,
    OrchestrionRoll,
    EmoteHairstyle,
    Barding,
    TripleTriadCard,
}

public enum RegistrableUnlockState
{
    Unreadable,
    Locked,
    Unlocked,
}

public enum RegistrablePreUseDecision
{
    Use,
    AdvanceUnlocked,
    AdvanceMissing,
    FailUnreadable,
}

public enum RegistrablePostUseDecision
{
    Wait,
    AdvanceUnlocked,
    AdvanceMissing,
    Retry,
    Exhaust,
    FailUnreadable,
}

public readonly record struct RegistrableInventorySlotSnapshot(uint ItemId, int Quantity);

public sealed record RegistrableInventoryBagSnapshot(
    bool IsReadable,
    IReadOnlyList<RegistrableInventorySlotSnapshot> Slots);

public static class RegistrableRegistrationPolicy
{
    public const int RequiredInventoryBagCount = 4;
    public static readonly TimeSpan VerificationDelay = TimeSpan.FromSeconds(7);

    public static bool TryClassifyDirectAction(
        uint actionId,
        bool isFadedOrchestrionCopy,
        out RegistrableCategory category)
    {
        category = actionId switch
        {
            1322 => RegistrableCategory.Mount,
            853 => RegistrableCategory.Minion,
            20086 => RegistrableCategory.FashionAccessory,
            37312 => RegistrableCategory.Facewear,
            25183 => RegistrableCategory.OrchestrionRoll,
            2633 => RegistrableCategory.EmoteHairstyle,
            1013 => RegistrableCategory.Barding,
            3357 => RegistrableCategory.TripleTriadCard,
            _ => default,
        };

        return !isFadedOrchestrionCopy &&
               actionId is 1322 or 853 or 20086 or 37312 or 25183 or 2633 or 1013 or 3357;
    }

    public static bool TryBuildInventoryItemSnapshot(
        IReadOnlyList<RegistrableInventoryBagSnapshot> bags,
        out List<uint> itemIds)
    {
        itemIds = [];
        if (bags.Count != RequiredInventoryBagCount || bags.Any(bag => !bag.IsReadable))
            return false;

        var seen = new HashSet<uint>();
        foreach (var bag in bags)
        {
            foreach (var slot in bag.Slots)
            {
                if (slot.ItemId != 0 && slot.Quantity > 0 && seen.Add(slot.ItemId))
                    itemIds.Add(slot.ItemId);
            }
        }

        return true;
    }

    public static IReadOnlyList<uint> SelectItemSource(
        bool automaticInventoryMode,
        IReadOnlyList<uint> personalItems,
        IReadOnlyList<uint> automaticItems)
        => automaticInventoryMode
            ? automaticItems.ToList()
            : personalItems.ToList();

    public static bool CanStart(
        bool featureEnabled,
        bool automaticInventoryMode,
        int personalItemCount)
        => featureEnabled && (automaticInventoryMode || personalItemCount > 0);

    public static bool TryFilterLockedItems(
        IReadOnlyList<uint> itemIds,
        Func<uint, RegistrableUnlockState> readUnlockState,
        out List<uint> lockedItemIds)
    {
        lockedItemIds = [];
        foreach (var itemId in itemIds)
        {
            switch (readUnlockState(itemId))
            {
                case RegistrableUnlockState.Unreadable:
                    lockedItemIds.Clear();
                    return false;
                case RegistrableUnlockState.Locked:
                    lockedItemIds.Add(itemId);
                    break;
            }
        }

        return true;
    }

    public static RegistrablePreUseDecision EvaluateBeforeUse(
        RegistrableUnlockState unlockState,
        int remainingQuantity)
        => unlockState switch
        {
            RegistrableUnlockState.Unreadable => RegistrablePreUseDecision.FailUnreadable,
            RegistrableUnlockState.Unlocked => RegistrablePreUseDecision.AdvanceUnlocked,
            _ when remainingQuantity <= 0 => RegistrablePreUseDecision.AdvanceMissing,
            _ => RegistrablePreUseDecision.Use,
        };

    public static RegistrablePostUseDecision EvaluateAfterUse(
        TimeSpan elapsed,
        RegistrableUnlockState unlockState,
        int remainingQuantity,
        int attempts)
    {
        if (elapsed < VerificationDelay)
            return RegistrablePostUseDecision.Wait;
        if (unlockState == RegistrableUnlockState.Unreadable)
            return RegistrablePostUseDecision.FailUnreadable;
        if (unlockState == RegistrableUnlockState.Unlocked)
            return RegistrablePostUseDecision.AdvanceUnlocked;
        if (remainingQuantity <= 0)
            return RegistrablePostUseDecision.AdvanceMissing;
        return RegistrableRetryPolicy.ShouldExhaust(attempts, remainingQuantity)
            ? RegistrablePostUseDecision.Exhaust
            : RegistrablePostUseDecision.Retry;
    }
}
