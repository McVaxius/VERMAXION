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
        // Night of Devilry set (user requested)
        (43471, "Night of Devilry", "Head"),
        (43472, "Night of Devilry", "Body"),
        (43473, "Night of Devilry", "Hands"),
        (43474, "Night of Devilry", "Legs"),
        (43475, "Night of Devilry", "Feet"),
    };

    private enum GearState { Idle, SelectingGear, EquippingItem, WaitingForEquip, Complete, Failed }
    private GearState state = GearState.Idle;
    private DateTime stateEnteredAt;
    private readonly HashSet<string> attemptedSlots = new();
    private (uint ItemId, string Name, string Slot) selectedItem;

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

    private bool HasItemInInventory(uint itemId)
    {
        try
        {
            // Use actual inventory check like SND examples: GetItemCount(itemId) > 0
            unsafe
            {
                var im = InventoryManager.Instance();
                if (im == null)
                {
                    log.Warning("[SeasonalGear] InventoryManager.Instance() is null");
                    return false;
                }

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

                foreach (var containerType in containers)
                {
                    var container = im->GetInventoryContainer((InventoryType)containerType);
                    if (container == null) continue;

                    for (int i = 0; i < container->Size; i++)
                    {
                        var slot = container->GetInventorySlot(i);
                        if (slot != null && slot->ItemId == itemId && slot->Quantity > 0)
                        {
                            log.Debug($"[SeasonalGear] Found item {itemId} in container {containerType} slot {i}, quantity: {slot->Quantity}");
                            return true; // Found the item
                        }
                    }
                }

                log.Debug($"[SeasonalGear] Item {itemId} not found in any inventory container");
                return false; // Item not found
            }
        }
        catch (Exception ex)
        {
            log.Error($"[SeasonalGear] Error checking item {itemId}: {ex.Message}");
            return false;
        }
    }

    private bool EquipItem(uint itemId, string itemName)
    {
        try
        {
            // Use simple /equipitem command like SND examples
            // if GetItemCount(itemId) > 0 then yield("/equipitem "..itemId)
            if (!HasItemInInventory(itemId))
            {
                log.Warning($"[SeasonalGear] Item {itemId} ({itemName}) not found in inventory");
                return false;
            }

            log.Information($"[SeasonalGear] Equipping {itemName} (ItemID: {itemId})");
            commandManager.ProcessCommand($"/equipitem {itemId}");
            return true;
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
                
                var equipSuccess = EquipItem(selectedItem.ItemId, selectedItem.Name);
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
                if (elapsed > 3.5) // SND waits 3.5 seconds after equipitem
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
