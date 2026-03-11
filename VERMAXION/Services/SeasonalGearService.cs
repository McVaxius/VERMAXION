using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.System.String;
using VERMAXION.Services;

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

    private enum GearState { Idle, SelectingGear, EquippingItem, WaitingForEquip, Finalizing, Complete, Failed }
    private GearState state = GearState.Idle;
    private DateTime stateEnteredAt;
    private readonly HashSet<uint> attemptedItems = new(); // Track by ItemId, not slot
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
            // Use built-in GetInventoryItemCount like SND examples: GetItemCount(itemId) > 0
            unsafe
            {
                var im = InventoryManager.Instance();
                if (im == null)
                {
                    log.Warning("[SeasonalGear] InventoryManager.Instance() is null");
                    return false;
                }

                // Built-in method that searches all containers automatically (SND pattern)
                var count = im->GetInventoryItemCount(itemId);
                log.Debug($"[SeasonalGear] GetInventoryItemCount({itemId}) = {count}");
                return count > 0;
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
            // Use InventoryManager.MoveItemSlot() like SND actually implements /equipitem
            // /equipitem is a special SND command that moves items to equipped slots
            if (!HasItemInInventory(itemId))
            {
                log.Warning($"[SeasonalGear] Item {itemId} ({itemName}) not found in inventory");
                return false;
            }

            log.Information($"[SeasonalGear] Equipping {itemName} (ItemID: {itemId})");
            
            unsafe
            {
                var im = InventoryManager.Instance();
                if (im == null)
                {
                    log.Warning("[SeasonalGear] InventoryManager.Instance() is null");
                    return false;
                }

                // Find the item in inventory containers
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
                            log.Debug($"[SeasonalGear] Found item {itemId} in container {containerType} slot {i}");
                            log.Debug($"[SeasonalGear] Item details: Quantity={slot->Quantity}, Flags={slot->Flags}, Condition={slot->Condition}");
                            log.Debug($"[SeasonalGear] Target slot mapping: ItemId={itemId} -> Slot={GetEquipmentSlotForItem(itemId)}");
                            
                            // Determine target equipment slot based on item
                            var targetContainer = (InventoryType)1000; // EquippedItems (not Inventory1!)
                            var targetSlot = GetEquipmentSlotForItem(itemId);
                            
                            if (targetSlot == -1)
                            {
                                log.Warning($"[SeasonalGear] Unknown equipment slot for item {itemId}");
                                return false;
                            }

                            log.Debug($"[SeasonalGear] Target: Container={targetContainer}, Slot={targetSlot}");
                            
                            // Check target slot before moving
                            var equippedContainer = im->GetInventoryContainer(targetContainer);
                            if (equippedContainer != null)
                            {
                                log.Debug($"[SeasonalGear] Target container size: {equippedContainer->Size}");
                                
                                // Log all equipped slots to understand the structure
                                log.Debug("[SeasonalGear] Current equipped items:");
                                for (int eqSlot = 0; eqSlot < equippedContainer->Size && eqSlot < 14; eqSlot++)
                                {
                                    var eqItem = equippedContainer->GetInventorySlot(eqSlot);
                                    if (eqItem != null && eqItem->ItemId > 0)
                                    {
                                        log.Debug($"[SeasonalGear]  Slot {eqSlot}: ItemId={eqItem->ItemId}, Quantity={eqItem->Quantity}");
                                    }
                                }
                                
                                if (targetSlot < equippedContainer->Size)
                                {
                                    var targetSlotItem = equippedContainer->GetInventorySlot(targetSlot);
                                    if (targetSlotItem != null)
                                    {
                                        log.Debug($"[SeasonalGear] Target slot currently has: ItemId={targetSlotItem->ItemId}, Quantity={targetSlotItem->Quantity}");
                                    }
                                    else
                                    {
                                        log.Debug($"[SeasonalGear] Target slot is empty");
                                    }
                                }
                                else
                                {
                                    log.Warning($"[SeasonalGear] Target slot {targetSlot} exceeds container size {equippedContainer->Size}");
                                }
                            }
                            else
                            {
                                log.Warning($"[SeasonalGear] Target container {targetContainer} is null");
                            }
                            
                            log.Debug($"[SeasonalGear] Attempting MoveItemSlot: sourceContainer={containerType}, sourceSlot={i}, targetContainer={targetContainer}, targetSlot={targetSlot}");
                            
                            // Re-check source slot to prevent race condition (Error 11)
                            var sourceSlotCheck = container->GetInventorySlot(i);
                            if (sourceSlotCheck == null || sourceSlotCheck->ItemId != itemId || sourceSlotCheck->Quantity <= 0)
                            {
                                log.Warning($"[SeasonalGear] Source slot changed between check and move (race condition)");
                                return false;
                            }
                            
                            // Use MoveItemSlot like SND implements /equipitem
                            var result = im->MoveItemSlot((InventoryType)containerType, (ushort)i, targetContainer, (ushort)targetSlot);
                            log.Debug($"[SeasonalGear] MoveItemSlot result: {result}");
                            
                            if (result == 0)
                            {
                                // SUCCESS: MoveItemSlot worked, no finalization yet
                                log.Information($"[SeasonalGear] Equip command sent for {itemName}");
                                log.Debug("[SeasonalGear] MoveItemSlot successful - waiting for finalization");
                                
                                // NO individual finalization - will finalize all at once at the end
                                // This avoids crashes and ensures all equipment changes persist together
                            }
                            
                            return result == 0; // 0 = success for MoveItemSlot
                        }
                    }
                }

                log.Warning($"[SeasonalGear] Item {itemId} not found in any container");
                return false;
            }
        }
        catch (Exception ex)
        {
            log.Error($"[SeasonalGear] Error equipping item {itemId}: {ex.Message}");
            return false;
        }
    }

    private int GetEquipmentSlotForItem(uint itemId)
    {
        // Map item to equipment slot (Correct FFXIV equipped slot indices)
        // Based on actual equipped structure from logs
        // Head items
        if (itemId == 47924 || itemId == 47623 || itemId == 43471) return 2;  // Head -> Slot 2
        // Body items  
        if (itemId == 50851 || itemId == 50850 || itemId == 43472) return 3;  // Body -> Slot 3
        // Hands items
        if (itemId == 43473) return 4;  // Hands -> Slot 4
        // Legs items
        if (itemId == 43474) return 6;  // Legs -> Slot 6
        // Feet items
        if (itemId == 43475) return 7;  // Feet -> Slot 7
        
        return -1; // Unknown slot
    }

    public void Start()
    {
        attemptedItems.Clear();
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
        attemptedItems.Clear();
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
                // Group items by slot and randomize selection per slot (only 1 attempt per slot)
                var slotGroups = new Dictionary<string, List<(uint ItemId, string Name, string Slot)>>();
                foreach (var item in SeasonalGearList)
                {
                    if (!attemptedItems.Contains(item.ItemId) && HasItemInInventory(item.ItemId))
                    {
                        if (!slotGroups.ContainsKey(item.Slot))
                            slotGroups[item.Slot] = new List<(uint, string, string)>();
                        slotGroups[item.Slot].Add(item);
                        log.Debug($"[SeasonalGear] Found available item: {item.Name} (ID:{item.ItemId}) for slot {item.Slot}");
                    }
                }

                if (slotGroups.Count == 0)
                {
                    log.Information("[SeasonalGear] No more available items to try, starting finalization");
                    SetState(GearState.Finalizing);
                    return;
                }

                // Pick a random slot, then random item from that slot
                var slots = slotGroups.Keys.ToList();
                var selectedSlot = slots[rng.Next(slots.Count)];
                var slotItems = slotGroups[selectedSlot];
                selectedItem = slotItems[rng.Next(slotItems.Count)];
                attemptedItems.Add(selectedItem.ItemId);
                
                log.Information($"[SeasonalGear] Selected: {selectedItem.Name} (ID:{selectedItem.ItemId}, Slot:{selectedSlot})");
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
                    log.Warning($"[SeasonalGear] Failed to equip {selectedItem.Name}, trying next item");
                    SetState(GearState.SelectingGear);
                }
                break;

            case GearState.WaitingForEquip:
                if (elapsed > 3.5) // SND waits 3.5 seconds after equipitem
                {
                    log.Information($"[SeasonalGear] Equip wait done for {selectedItem.Name}");
                    // Try next available item
                    SetState(GearState.SelectingGear);
                }
                break;

            case GearState.Finalizing:
                if (elapsed > 1.0 && elapsed < 1.1)
                {
                    log.Information("[SeasonalGear] Starting final equipment finalization");
                    try
                    {
                        // Correct sequence: character window -> callback 15 -> SelectYesno -> updategearset
                        CommandHelper.SendCommand("/character");
                        log.Debug("[SeasonalGear] Character window opened");
                    }
                    catch (Exception ex)
                    {
                        log.Error($"[SeasonalGear] Error during finalization: {ex.Message}");
                        SetState(GearState.Failed);
                    }
                }
                else if (elapsed > 2.0 && elapsed < 2.1)
                {
                    // Use game's own recommendation system (button 15)
                    GameHelpers.FireAddonCallback("Character", true, 15);
                    log.Debug("[SeasonalGear] Fired Character callback true 15 (game recommendation)");
                    
                    // Handle SelectYesno dialog if it appears (2 second delay as specified)
                    System.Threading.Tasks.Task.Delay(2000).ContinueWith(_ => {
                        if (GameHelpers.IsAddonVisible("SelectYesno"))
                        {
                            log.Debug("[SeasonalGear] Confirming SelectYesno dialog");
                            GameHelpers.FireAddonCallback("SelectYesno", true, 0);
                        }
                    });
                }
                else if (elapsed > 4.0)  // Adjusted timing to account for 2s delay
                {
                    CommandHelper.SendCommand("/updategearset");
                    log.Debug("[SeasonalGear] Gearset update sent - finalization complete");
                    SetState(GearState.Complete);
                }
                break;
        }
    }

    public void Dispose() { }
}
