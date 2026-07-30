using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Component.Exd;
using Lumina.Excel.Sheets;
using VERMAXION.Models;

namespace VERMAXION.Services;

/// <summary>
/// Registers configured personal items or, when opted in, direct registrables
/// discovered from one ordered snapshot of the four main inventory bags.
/// </summary>
public class RegisterRegistrablesService : IDisposable
{
    private static readonly InventoryType[] MainInventoryTypes =
    [
        InventoryType.Inventory1,
        InventoryType.Inventory2,
        InventoryType.Inventory3,
        InventoryType.Inventory4,
    ];

    private readonly IPluginLog log;
    private readonly ConfigManager configManager;
    private readonly IDataManager dataManager;

    private bool isActive;
    private bool automaticInventoryModeForRun;
    private RegisterState currentState = RegisterState.Idle;
    private DateTime lastProcessTime = DateTime.MinValue;
    private int currentItemIndex;
    private int currentItemAttempts;
    private int exhaustedItemCount;
    private List<QueuedRegistrable> foundItems = [];

    private sealed record QueuedRegistrable(
        uint ItemId,
        string ItemName,
        int SnapshotQuantity,
        string Source);

    public enum RegisterState
    {
        Idle,
        ScanningInventory,
        ProcessingItems,
        WaitingForNextItem,
        Complete,
        Failed,
    }

    public RegisterState State => currentState;
    public bool IsActive => isActive;
    public bool IsComplete => currentState == RegisterState.Complete;
    public bool IsFailed => currentState == RegisterState.Failed;

    public RegisterRegistrablesService(
        IPluginLog log,
        ConfigManager configManager,
        IDataManager dataManager)
    {
        this.log = log;
        this.configManager = configManager;
        this.dataManager = dataManager;
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

        automaticInventoryModeForRun = activeConfig.RegisterUnregisteredItemsFromInventory;
        if (!RegistrableRegistrationPolicy.CanStart(
                activeConfig.EnableRegisterRegistrables,
                automaticInventoryModeForRun,
                activeConfig.PersonalRegistrableItems.Count))
        {
            log.Warning("[RegisterRegistrables] Blocked: no personal registrable items are configured; enablement was preserved");
            SetState(RegisterState.Failed);
            return;
        }

        log.Information(automaticInventoryModeForRun
            ? "[RegisterRegistrables] Starting automatic inventory discovery; the personal list is ignored for this run"
            : $"[RegisterRegistrables] Starting with {activeConfig.PersonalRegistrableItems.Count} items in personal list");
        isActive = true;
        foundItems.Clear();
        currentItemIndex = 0;
        currentItemAttempts = 0;
        exhaustedItemCount = 0;
        SetState(RegisterState.ScanningInventory);
    }

    public void Reset()
    {
        log.Information("[RegisterRegistrables] Resetting service");
        isActive = false;
        automaticInventoryModeForRun = false;
        currentState = RegisterState.Idle;
        lastProcessTime = DateTime.MinValue;
        currentItemIndex = 0;
        currentItemAttempts = 0;
        exhaustedItemCount = 0;
        foundItems.Clear();
    }

    public void Update()
    {
        if (!isActive)
            return;

        switch (currentState)
        {
            case RegisterState.ScanningInventory:
                ScanInventory();
                if (IsComplete || IsFailed)
                    return;
                SetState(RegisterState.ProcessingItems);
                currentItemIndex = 0;
                break;

            case RegisterState.ProcessingItems:
                if (currentItemIndex >= foundItems.Count)
                {
                    log.Information(exhaustedItemCount == 0
                        ? "[RegisterRegistrables] All items processed successfully"
                        : $"[RegisterRegistrables] Processing complete with {exhaustedItemCount} item(s) exhausted after retry limit");
                    SetState(RegisterState.Complete);
                    return;
                }

                if (ProcessCurrentItem())
                {
                    SetState(RegisterState.WaitingForNextItem);
                    lastProcessTime = DateTime.UtcNow;
                }
                break;

            case RegisterState.WaitingForNextItem:
                VerifyCurrentItemAfterUse();
                break;

            case RegisterState.Complete:
            case RegisterState.Failed:
                break;
        }
    }

    private void ScanInventory()
    {
        foundItems.Clear();
        var activeConfig = configManager.GetActiveConfig();
        if (activeConfig == null)
        {
            FailRun("active character configuration became unavailable");
            return;
        }

        IReadOnlyList<uint> sourceItemIds;
        var snapshotQuantities = new Dictionary<uint, int>();
        if (automaticInventoryModeForRun)
        {
            if (!TryReadMainInventorySnapshot(out var bags, out var snapshotError) ||
                !RegistrableRegistrationPolicy.TryBuildInventoryItemSnapshot(bags, out var automaticItemIds))
            {
                FailRun($"automatic inventory snapshot was unreadable: {snapshotError}");
                return;
            }

            foreach (var slot in bags.SelectMany(bag => bag.Slots))
            {
                if (slot.ItemId == 0 || slot.Quantity <= 0)
                    continue;
                snapshotQuantities[slot.ItemId] =
                    snapshotQuantities.GetValueOrDefault(slot.ItemId) + slot.Quantity;
            }

            sourceItemIds = RegistrableRegistrationPolicy.SelectItemSource(
                true,
                activeConfig.PersonalRegistrableItems,
                automaticItemIds);
            log.Information($"[RegisterRegistrables] Captured one ordered main-inventory snapshot with {sourceItemIds.Count} distinct item IDs");
        }
        else
        {
            sourceItemIds = RegistrableRegistrationPolicy.SelectItemSource(
                false,
                activeConfig.PersonalRegistrableItems,
                Array.Empty<uint>());
            log.Information($"[RegisterRegistrables] Scanning inventory for {sourceItemIds.Count} personal items");
        }

        foreach (var itemId in sourceItemIds)
        {
            var quantity = automaticInventoryModeForRun
                ? snapshotQuantities.GetValueOrDefault(itemId)
                : (int)GameHelpers.GetInventoryItemCount(itemId);
            if (quantity <= 0)
                continue;

            var source = "personal list";
            if (automaticInventoryModeForRun)
            {
                if (!TryGetItem(itemId, out var item) ||
                    !RegistrableRegistrationPolicy.TryClassifyDirectAction(
                        GetItemActionId(item),
                        IsFadedOrchestrionCopy(item),
                        out var category))
                {
                    continue;
                }

                source = category.ToString();
            }

            var unlockState = ReadUnlockState(itemId);
            if (unlockState == RegistrableUnlockState.Unreadable)
            {
                FailRun($"registration state was unreadable while building the queue for item {itemId}");
                return;
            }
            if (unlockState == RegistrableUnlockState.Unlocked)
            {
                log.Information($"[RegisterRegistrables] Skipping already registered item {itemId}");
                continue;
            }

            var itemName = GameHelpers.GetItemName(itemId);
            foundItems.Add(new QueuedRegistrable(itemId, itemName, quantity, source));
            log.Information($"[RegisterRegistrables] Queued {itemName} x{quantity} (ID: {itemId}, source: {source})");
        }

        if (foundItems.Count == 0)
        {
            log.Information("[RegisterRegistrables] No locked registrable items found in inventory");
            SetState(RegisterState.Complete);
            return;
        }

        log.Information($"[RegisterRegistrables] Fixed queue contains {foundItems.Count} item(s)");
    }

    private bool ProcessCurrentItem()
    {
        if (currentItemIndex >= foundItems.Count)
            return false;

        var item = foundItems[currentItemIndex];
        var unlockState = ReadUnlockState(item.ItemId);
        if (unlockState == RegistrableUnlockState.Unreadable)
        {
            FailRun($"registration state became unreadable before using {item.ItemName}");
            return false;
        }

        var currentQuantity = item.SnapshotQuantity;
        if (unlockState == RegistrableUnlockState.Locked &&
            !TryReadMainInventoryQuantity(item.ItemId, out currentQuantity, out var quantityError))
        {
            FailRun($"inventory state became unreadable before using {item.ItemName}: {quantityError}");
            return false;
        }

        switch (RegistrableRegistrationPolicy.EvaluateBeforeUse(unlockState, currentQuantity))
        {
            case RegistrablePreUseDecision.AdvanceUnlocked:
                AdvanceCurrentItem($"{item.ItemName} is already registered");
                return false;
            case RegistrablePreUseDecision.AdvanceMissing:
                AdvanceCurrentItem($"{item.ItemName} is no longer present");
                return false;
            case RegistrablePreUseDecision.FailUnreadable:
                FailRun($"registration state became unreadable before using {item.ItemName}");
                return false;
        }

        if (!GameHelpers.IsPlayerAvailable())
        {
            log.Warning("[RegisterRegistrables] Player not available (casting/occupied), waiting...");
            return false;
        }

        currentItemAttempts++;
        var result = GameHelpers.UseItem(item.ItemId);
        if (result)
            log.Information($"[RegisterRegistrables] Use requested for {item.ItemName}");
        else
            log.Warning($"[RegisterRegistrables] Failed to request use of {item.ItemName}");
        return true;
    }

    private void VerifyCurrentItemAfterUse()
    {
        var elapsed = DateTime.UtcNow - lastProcessTime;
        if (elapsed < RegistrableRegistrationPolicy.VerificationDelay ||
            currentItemIndex >= foundItems.Count)
        {
            return;
        }

        var item = foundItems[currentItemIndex];
        var unlockState = ReadUnlockState(item.ItemId);
        var currentQuantity = item.SnapshotQuantity;
        if (unlockState == RegistrableUnlockState.Locked &&
            !TryReadMainInventoryQuantity(item.ItemId, out currentQuantity, out var quantityError))
        {
            FailRun($"inventory state became unreadable while verifying {item.ItemName}: {quantityError}");
            return;
        }

        switch (RegistrableRegistrationPolicy.EvaluateAfterUse(
                    elapsed,
                    unlockState,
                    currentQuantity,
                    currentItemAttempts))
        {
            case RegistrablePostUseDecision.AdvanceUnlocked:
                AdvanceCurrentItem($"{item.ItemName} registration verified");
                SetState(RegisterState.ProcessingItems);
                break;
            case RegistrablePostUseDecision.AdvanceMissing:
                AdvanceCurrentItem($"{item.ItemName} is no longer present");
                SetState(RegisterState.ProcessingItems);
                break;
            case RegistrablePostUseDecision.Retry:
                log.Warning($"[RegisterRegistrables] {item.ItemName} is still present and locked, retrying attempt {currentItemAttempts + 1}/{RegistrableRetryPolicy.MaxAttemptsPerItem}");
                SetState(RegisterState.ProcessingItems);
                break;
            case RegistrablePostUseDecision.Exhaust:
                log.Warning($"[RegisterRegistrables] Item {item.ItemName} is still present and locked after {currentItemAttempts}/{RegistrableRetryPolicy.MaxAttemptsPerItem} attempts; exhausting item and continuing");
                exhaustedItemCount++;
                AdvanceCurrentItem($"{item.ItemName} exhausted");
                SetState(RegisterState.ProcessingItems);
                break;
            case RegistrablePostUseDecision.FailUnreadable:
                FailRun($"registration state became unreadable while verifying {item.ItemName}");
                break;
        }
    }

    private void AdvanceCurrentItem(string reason)
    {
        log.Information($"[RegisterRegistrables] {reason}; moving to next queue item");
        currentItemIndex++;
        currentItemAttempts = 0;
    }

    private void FailRun(string reason)
    {
        log.Error($"[RegisterRegistrables] Failed closed: {reason}");
        SetState(RegisterState.Failed);
    }

    private bool TryGetItem(uint itemId, out Item item)
    {
        var sheet = dataManager.GetExcelSheet<Item>();
        return sheet.TryGetRow(itemId, out item);
    }

    private static bool IsFadedOrchestrionCopy(Item item)
        => item.FilterGroup == 12 && item.ItemUICategory.RowId == 94;

    private static uint GetItemActionId(Item item)
    {
        try
        {
            return item.ItemAction.Value.Action.Value.RowId;
        }
        catch
        {
            return 0;
        }
    }

    private static unsafe RegistrableUnlockState ReadUnlockState(uint itemId)
    {
        try
        {
            var exdItem = ExdModule.GetItemRowById(itemId);
            var uiState = UIState.Instance();
            if (exdItem == null || uiState == null)
                return RegistrableUnlockState.Unreadable;

            return uiState->IsItemActionUnlocked(exdItem) switch
            {
                0 => RegistrableUnlockState.Locked,
                1 => RegistrableUnlockState.Unlocked,
                _ => RegistrableUnlockState.Unreadable,
            };
        }
        catch
        {
            return RegistrableUnlockState.Unreadable;
        }
    }

    private static unsafe bool TryReadMainInventorySnapshot(
        out List<RegistrableInventoryBagSnapshot> bags,
        out string error)
    {
        bags = [];
        error = string.Empty;
        try
        {
            var inventory = InventoryManager.Instance();
            if (inventory == null)
            {
                error = "InventoryManager is unavailable";
                return false;
            }

            foreach (var inventoryType in MainInventoryTypes)
            {
                var container = inventory->GetInventoryContainer(inventoryType);
                if (container == null)
                {
                    error = $"{inventoryType} is unavailable";
                    return false;
                }
                if (!container->IsLoaded)
                {
                    error = $"{inventoryType} is not loaded";
                    return false;
                }

                var slots = new List<RegistrableInventorySlotSnapshot>(container->Size);
                for (var slotIndex = 0; slotIndex < container->Size; slotIndex++)
                {
                    var slot = container->GetInventorySlot(slotIndex);
                    if (slot == null)
                    {
                        error = $"{inventoryType} slot {slotIndex} is unavailable";
                        return false;
                    }

                    slots.Add(new RegistrableInventorySlotSnapshot(
                        slot->ItemId,
                        slot->Quantity));
                }

                bags.Add(new RegistrableInventoryBagSnapshot(true, slots));
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static unsafe bool TryReadMainInventoryQuantity(
        uint itemId,
        out int quantity,
        out string error)
    {
        quantity = 0;
        error = string.Empty;
        try
        {
            var inventory = InventoryManager.Instance();
            if (inventory == null)
            {
                error = "InventoryManager is unavailable";
                return false;
            }

            foreach (var inventoryType in MainInventoryTypes)
            {
                var container = inventory->GetInventoryContainer(inventoryType);
                if (container == null || !container->IsLoaded)
                {
                    error = $"{inventoryType} is unavailable or not loaded";
                    return false;
                }

                for (var slotIndex = 0; slotIndex < container->Size; slotIndex++)
                {
                    var slot = container->GetInventorySlot(slotIndex);
                    if (slot == null)
                    {
                        error = $"{inventoryType} slot {slotIndex} is unavailable";
                        return false;
                    }
                    if (slot->ItemId == itemId)
                        quantity += slot->Quantity;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private void SetState(RegisterState newState)
    {
        if (currentState == newState)
            return;

        log.Information($"[RegisterRegistrables] {currentState} -> {newState}");
        currentState = newState;
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
        => Reset();
}
