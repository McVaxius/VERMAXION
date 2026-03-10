using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace VERMAXION.Services;

public class SeasonalGearService : IDisposable
{
    private readonly ICommandManager commandManager;
    private readonly IPluginLog log;
    private readonly Random rng = new();

    // Seed list of seasonal gear items (user will expand later)
    // ItemID, Name, Slot
    public static readonly List<(uint ItemId, string Name, string Slot)> SeasonalGearList = new()
    {
        (47924, "Maritime Mirrored Sunglasses", "Head"),
        (47623, "Cozy Valentione Beret", "Head"),
        (50851, "Oversized Picot Neotunic", "Body"),
        (50850, "Oversized Narumi Neotunic", "Body"),
    };

    private enum GearState { Idle, SelectingGear, EquippingItem, WaitingForEquip, Complete, Failed }
    private GearState state = GearState.Idle;
    private DateTime stateEnteredAt;
    private readonly HashSet<string> attemptedSlots = new();
    private (uint ItemId, string Name, string Slot) selectedItem;

    // Equipment slot mapping - using numeric values for InventoryType
    private static readonly Dictionary<string, (ushort Container, ushort SlotIndex)> EquipmentSlots = new()
    {
        ["Head"] = (0, 0),     // EquippedItems, slot 0
        ["Body"] = (0, 1),     // EquippedItems, slot 1
        ["Hands"] = (0, 2),    // EquippedItems, slot 2
        ["Legs"] = (0, 3),    // EquippedItems, slot 3
        ["Feet"] = (0, 4),    // EquippedItems, slot 4
        // Note: Waist slot (5) is deprecated in Stormblood
        ["Ears"] = (0, 6),    // EquippedItems, slot 6
        ["Neck"] = (0, 7),    // EquippedItems, slot 7
        ["Wrists"] = (0, 8),  // EquippedItems, slot 8
        ["Ring1"] = (0, 9),   // EquippedItems, slot 9
        ["Ring2"] = (0, 10),  // EquippedItems, slot 10
        ["MainHand"] = (0, 11), // EquippedItems, slot 11
        ["OffHand"] = (0, 12),  // EquippedItems, slot 12
    };

    public bool IsComplete => state == GearState.Complete;
    public bool IsFailed => state == GearState.Failed;
    public bool IsIdle => state == GearState.Idle;
    public string StatusText => state.ToString();

    public SeasonalGearService(ICommandManager commandManager, IPluginLog log)
    {
        this.commandManager = commandManager;
        this.log = log;
    }

    private void SetState(GearState newState)
    {
        log.Information($"[SeasonalGear] {state} -> {newState}");
        state = newState;
        stateEnteredAt = DateTime.UtcNow;
    }

    private (ushort ContainerIndex, ushort SlotIndex)? FindItemInInventory(uint itemId)
    {
        try
        {
            // Search all inventory containers for the item
            var containers = new ushort[]
            {
                2001, // ArmoryHead
                2002, // ArmoryBody
                2003, // ArmoryHands
                2004, // ArmoryLegs
                2005, // ArmoryFeet
                2006, // ArmoryEar
                2007, // ArmoryNeck
                2008, // ArmoryWrist
                2009, // ArmoryRing
                2010, // ArmoryMainHand
                2011, // ArmoryOffHand
                2012, // ArmorySoulCrystal
                // Also check regular inventory bags
                3200, // Inventory1
                3201, // Inventory2
                3202, // Inventory3
                3203  // Inventory4
            };

            unsafe
            {
                var im = InventoryManager.Instance();
                if (im == null)
                {
                    log.Warning("[SeasonalGear] InventoryManager.Instance() is null");
                    return null;
                }

                foreach (var containerType in containers)
                {
                    var container = im->GetInventoryContainer((InventoryType)containerType);
                    if (container == null) continue;

                    for (int i = 0; i < container->Size; i++)
                    {
                        var slot = container->GetInventorySlot(i);
                        if (slot != null && slot->ItemId == itemId)
                        {
                            log.Debug($"[SeasonalGear] Found item {itemId} in container {containerType} slot {i}");
                            return ((ushort)containerType, (ushort)i);
                        }
                    }
                }
            }

            log.Debug($"[SeasonalGear] Item {itemId} not found in any inventory container");
            return null;
        }
        catch (Exception ex)
        {
            log.Error($"[SeasonalGear] Error finding item {itemId}: {ex.Message}");
            return null;
        }
    }

    private bool EquipItem(uint itemId, string slotName)
    {
        try
        {
            // Find the item in inventory
            var itemLocation = FindItemInInventory(itemId);
            if (!itemLocation.HasValue)
            {
                log.Warning($"[SeasonalGear] Item {itemId} not found in inventory");
                return false;
            }

            // Get target equipment slot
            if (!EquipmentSlots.TryGetValue(slotName, out var targetSlot))
            {
                log.Error($"[SeasonalGear] Unknown equipment slot: {slotName}");
                return false;
            }

            var (targetContainer, targetSlotIndex) = targetSlot;
            var (sourceContainer, sourceSlotIndex) = itemLocation.Value;

            log.Information($"[SeasonalGear] Moving item {itemId} from container {sourceContainer} slot {sourceSlotIndex} to equipped slot {targetSlotIndex}");

            // Use InventoryManager.MoveItemSlot to equip the item
            unsafe
            {
                var im = InventoryManager.Instance();
                if (im == null)
                {
                    log.Warning("[SeasonalGear] InventoryManager.Instance() is null during equip");
                    return false;
                }

                var result = im->MoveItemSlot((InventoryType)sourceContainer, sourceSlotIndex, (InventoryType)targetContainer, targetSlotIndex);
                if (result == 0) // MoveItemSlot returns 0 on success
                {
                    log.Information($"[SeasonalGear] Successfully equipped item {itemId} to {slotName}");
                    return true;
                }
                else
                {
                    log.Warning($"[SeasonalGear] Failed to equip item {itemId} to {slotName} (error: {result})");
                    return false;
                }
            }
        }
        catch (Exception ex)
        {
            log.Error($"[SeasonalGear] Error equipping item {itemId}: {ex.Message}");
            return false;
        }
    }

    public void Start()
    {
        attemptedSlots.Clear();
        SetState(GearState.SelectingGear);
        log.Information("[SeasonalGear] Starting seasonal gear roulette");
    }

    public void RunTask()
    {
        log.Information("[VERMAXION] Manual Seasonal Gear Roulette triggered");
        Start();
    }

    public void Reset()
    {
        attemptedSlots.Clear();
        SetState(GearState.Idle);
    }

    public void Update()
    {
        if (state == GearState.Idle || state == GearState.Complete || state == GearState.Failed)
            return;

        var elapsed = (DateTime.UtcNow - stateEnteredAt).TotalSeconds;

        switch (state)
        {
            case GearState.SelectingGear:
                // Find items in slots we haven't attempted yet
                var candidates = new List<(uint ItemId, string Name, string Slot)>();
                foreach (var item in SeasonalGearList)
                {
                    if (!attemptedSlots.Contains(item.Slot))
                        candidates.Add(item);
                }

                if (candidates.Count == 0)
                {
                    log.Information("[SeasonalGear] No more slots to try, complete");
                    SetState(GearState.Complete);
                    return;
                }

                selectedItem = candidates[rng.Next(candidates.Count)];
                attemptedSlots.Add(selectedItem.Slot);
                log.Information($"[SeasonalGear] Selected: {selectedItem.Name} (ID:{selectedItem.ItemId}, Slot:{selectedItem.Slot})");
                SetState(GearState.EquippingItem);
                break;

            case GearState.EquippingItem:
                log.Information($"[SeasonalGear] Attempting to equip {selectedItem.Name} (ItemID: {selectedItem.ItemId})");
                
                var equipSuccess = EquipItem(selectedItem.ItemId, selectedItem.Slot);
                if (equipSuccess)
                {
                    log.Information($"[SeasonalGear] Equip command sent for {selectedItem.Name}");
                    SetState(GearState.WaitingForEquip);
                }
                else
                {
                    log.Warning($"[SeasonalGear] Failed to equip {selectedItem.Name}, trying next slot");
                    SetState(GearState.SelectingGear);
                }
                break;

            case GearState.WaitingForEquip:
                if (elapsed > 2.0)
                {
                    log.Information($"[SeasonalGear] Equip wait done for {selectedItem.Name}");
                    // Only one change per slot per ARpostprocess, try next slot
                    SetState(GearState.SelectingGear);
                }
                break;
        }
    }

    public void Dispose() { }
}
