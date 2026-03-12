using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Plugin.Services;
using VERMAXION.Models;

namespace VERMAXION.Services;

/// <summary>
/// Register Registrables automation service.
/// Processes items from personal list that exist in inventory.
/// Uses items one by one with 7-second intervals until consumed.
/// </summary>
public class RegisterRegistrablesService : IDisposable
{
    private readonly ICommandManager commandManager;
    private readonly IObjectTable objectTable;
    private readonly IPluginLog log;
    private readonly ConfigManager configManager;

    private bool isActive = false;
    private RegisterState currentState = RegisterState.Idle;
    private DateTime lastProcessTime = DateTime.MinValue;
    private int currentItemIndex = 0;
    private List<(uint ItemId, string ItemName, int Quantity)> foundItems = new();

    public enum RegisterState
    {
        Idle,
        ScanningInventory,
        ProcessingItems,
        WaitingForNextItem,
        Complete,
        Failed
    }

    public RegisterState State => currentState;
    public bool IsActive => isActive;
    public bool IsComplete => currentState == RegisterState.Complete;
    public bool IsFailed => currentState == RegisterState.Failed;

    public RegisterRegistrablesService(ICommandManager commandManager, IObjectTable objectTable, IPluginLog log, ConfigManager configManager)
    {
        this.commandManager = commandManager;
        this.objectTable = objectTable;
        this.log = log;
        this.configManager = configManager;
    }

    public void Start()
    {
        if (isActive)
        {
            log.Warning("[RegisterRegistrables] Service already active");
            return;
        }

        var activeConfig = configManager.GetActiveConfig();
        if (activeConfig == null || !activeConfig.EnableRegisterRegistrables)
        {
            log.Information("[RegisterRegistrables] Feature disabled for character");
            return;
        }

        if (activeConfig.PersonalRegistrableItems.Count == 0)
        {
            log.Information("[RegisterRegistrables] No personal registrable items configured, disabling feature");
            activeConfig.EnableRegisterRegistrables = false;
            configManager.SaveCurrentAccount();
            return;
        }

        log.Information($"[RegisterRegistrables] Starting with {activeConfig.PersonalRegistrableItems.Count} items in personal list");
        isActive = true;
        foundItems.Clear();
        currentItemIndex = 0;
        SetState(RegisterState.ScanningInventory);
    }

    public void Reset()
    {
        log.Information("[RegisterRegistrables] Resetting service");
        isActive = false;
        currentState = RegisterState.Idle;
        lastProcessTime = DateTime.MinValue;
        currentItemIndex = 0;
        foundItems.Clear();
    }

    public void Update()
    {
        if (!isActive) return;

        switch (currentState)
        {
            case RegisterState.ScanningInventory:
                ScanInventory();
                SetState(RegisterState.ProcessingItems);
                currentItemIndex = 0;
                break;
                
            case RegisterState.ProcessingItems:
                if (currentItemIndex >= foundItems.Count)
                {
                    log.Information("[RegisterRegistrables] All items processed successfully");
                    SetState(RegisterState.Complete);
                    return;
                }
                
                ProcessCurrentItem();
                SetState(RegisterState.WaitingForNextItem);
                lastProcessTime = DateTime.Now;
                break;
                
            case RegisterState.WaitingForNextItem:
                if (DateTime.Now - lastProcessTime >= TimeSpan.FromSeconds(7))
                {
                    // Check if item was consumed
                    var currentQuantity = (int)GameHelpers.GetInventoryItemCount(foundItems[currentItemIndex].ItemId);
                    if (currentQuantity == 0)
                    {
                        log.Information($"[RegisterRegistrables] Item {foundItems[currentItemIndex].ItemName} consumed, moving to next");
                        currentItemIndex++;
                    }
                    else
                    {
                        log.Warning($"[RegisterRegistrables] Item {foundItems[currentItemIndex].ItemName} not consumed (still have {currentQuantity}), retrying");
                    }
                    SetState(RegisterState.ProcessingItems);
                }
                break;

            case RegisterState.Complete:
            case RegisterState.Failed:
                // Terminal states, nothing to do
                break;
        }
    }

    private void ScanInventory()
    {
        foundItems.Clear();
        var activeConfig = configManager.GetActiveConfig();
        var personalItems = activeConfig?.PersonalRegistrableItems ?? new List<uint>();
        
        log.Information($"[RegisterRegistrables] Scanning inventory for {personalItems.Count} personal items");
        
        foreach (var itemId in personalItems)
        {
            var quantity = (int)GameHelpers.GetInventoryItemCount(itemId);
            if (quantity > 0)
            {
                var itemName = GameHelpers.GetItemName(itemId);
                foundItems.Add((itemId, itemName, quantity));
                log.Information($"[RegisterRegistrables] Found {itemName} x{quantity} (ID: {itemId})");
            }
        }
        
        if (foundItems.Count == 0)
        {
            log.Information("[RegisterRegistrables] No registrable items found in inventory");
            SetState(RegisterState.Complete);
            return;
        }
        
        log.Information($"[RegisterRegistrables] Found {foundItems.Count} registrable items to process");
    }

    private void ProcessCurrentItem()
    {
        if (currentItemIndex >= foundItems.Count) return;
        
        var item = foundItems[currentItemIndex];
        log.Information($"[RegisterRegistrables] Processing {item.ItemName} (ID: {item.ItemId}, Qty: {item.Quantity})");
        
        // Check if player is available to use items
        if (!GameHelpers.IsPlayerAvailable())
        {
            log.Warning("[RegisterRegistrables] Player not available (casting/occupied), waiting...");
            return;
        }
        
        var result = GameHelpers.UseItem(item.ItemId);
        if (result)
        {
            log.Information($"[RegisterRegistrables] Successfully used {item.ItemName}");
        }
        else
        {
            log.Warning($"[RegisterRegistrables] Failed to use {item.ItemName}");
        }
    }

    private void SetState(RegisterState newState)
    {
        if (currentState == newState) return;

        log.Information($"[RegisterRegistrables] {currentState} -> {newState}");
        currentState = newState;

        // Handle state-specific logic
        switch (newState)
        {
            case RegisterState.Complete:
                log.Information("[RegisterRegistrables] Register Registrables completed successfully");
                isActive = false;
                break;

            case RegisterState.Failed:
                log.Error("[RegisterRegistrables] Register Registrables failed");
                isActive = false;
                break;
        }
    }

    public void Dispose()
    {
        Reset();
    }
}
