using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;

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
            // Use simple inventory check like SND examples
            // For now, assume we have the item - could be enhanced with actual inventory check later
            log.Debug($"[SeasonalGear] Checking if item {itemId} exists in inventory");
            return true; // Simplified for now - SND uses GetItemCount(itemId) > 0
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
