using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;

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
            // Use ActionManager.UseAction() like SND - NOT a chat command!
            // /equipitem is a special SND command, not a game chat command
            if (!HasItemInInventory(itemId))
            {
                log.Warning($"[SeasonalGear] Item {itemId} ({itemName}) not found in inventory");
                return false;
            }

            log.Information($"[SeasonalGear] Equipping {itemName} (ItemID: {itemId})");
            
            unsafe
            {
                var actionManager = ActionManager.Instance();
                if (actionManager == null)
                {
                    log.Warning("[SeasonalGear] ActionManager.Instance() is null");
                    return false;
                }

                // Use ActionManager.UseAction() for items (ActionType.Item)
                var result = actionManager->UseAction(ActionType.Item, itemId);
                log.Debug($"[SeasonalGear] UseAction(ActionType.Item, {itemId}) result: {result}");
                return result;
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
                // Find items we haven't attempted yet AND that actually exist in inventory
                var candidates = new List<(uint ItemId, string Name, string Slot)>();
                foreach (var item in SeasonalGearList)
                {
                    if (!attemptedItems.Contains(item.ItemId) && HasItemInInventory(item.ItemId))
                    {
                        candidates.Add(item);
                        log.Debug($"[SeasonalGear] Found available item: {item.Name} (ID:{item.ItemId})");
                    }
                }

                if (candidates.Count == 0)
                {
                    log.Information("[SeasonalGear] No more available items to try, complete");
                    SetState(GearState.Complete);
                    return;
                }

                selectedItem = candidates[rng.Next(candidates.Count)];
                attemptedItems.Add(selectedItem.ItemId);
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
        }
    }

    public void Dispose() { }
}
