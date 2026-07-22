using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;
using VERMAXION.Models;

namespace VERMAXION.Services;

/// <summary>
/// Internal native boundary for the equipment automations. All unsafe game access
/// is kept here so policy and state-machine behavior remain deterministic in tests.
/// </summary>
internal sealed unsafe class NativeEquipmentAutomationRuntime : IEquipmentAutomationRuntime
{
    private const int GearsetCapacity = 100;
    private const int EquippedItemCount = 14;
    private const ushort EquippedItemsContainer = 1000;
    private static readonly ushort[] InventoryContainers = [3200, 3201, 3202, 3203];
    private static readonly IReadOnlyDictionary<EquipmentSlot, ushort> ArmouryContainers =
        new Dictionary<EquipmentSlot, ushort>
        {
            [EquipmentSlot.Head] = 2001,
            [EquipmentSlot.Body] = 2002,
            [EquipmentSlot.Hands] = 2003,
            [EquipmentSlot.Legs] = 2004,
            [EquipmentSlot.Feet] = 2005,
        };

    private readonly IDataManager dataManager;
    private readonly IFramework framework;
    private readonly IPlayerState playerState;
    private readonly IPluginLog log;
    private readonly RecommendedEquipHelper recommendedEquip = new();

    public NativeEquipmentAutomationRuntime(
        IDataManager dataManager,
        IFramework framework,
        IPlayerState playerState,
        IPluginLog log)
    {
        this.dataManager = dataManager;
        this.framework = framework;
        this.playerState = playerState;
        this.log = log;
    }

    public DateTime UtcNow => DateTime.UtcNow;
    public ulong CharacterContentId => playerState.ContentId;
    public uint CurrentJobId => playerState.ClassJob.RowId;

    public int CurrentGearsetId
    {
        get
        {
            var module = RaptureGearsetModule.Instance();
            if (module == null || module->CurrentGearsetIndex is < 0 or >= GearsetCapacity)
                return -1;
            return module->Entries[module->CurrentGearsetIndex].Id;
        }
    }

    public IReadOnlyList<GearsetSnapshot> GetValidGearsets()
    {
        var result = new List<GearsetSnapshot>(GearsetCapacity);
        var module = RaptureGearsetModule.Instance();
        var sheet = dataManager.GetExcelSheet<ClassJob>();
        var nativePlayerState = PlayerState.Instance();
        if (module == null || sheet == null || nativePlayerState == null)
            return result;

        for (var slot = 0; slot < GearsetCapacity; slot++)
        {
            ref var entry = ref module->Entries[slot];
            if ((entry.Flags & RaptureGearsetModule.GearsetFlag.Exists) == 0 ||
                entry.Id >= GearsetCapacity || entry.ClassJob == 0 ||
                !module->IsValidGearset(entry.Id) ||
                !sheet.TryGetRow(entry.ClassJob, out var classJob))
            {
                continue;
            }

            var level = (int)nativePlayerState->GetClassJobLevel(entry.ClassJob, false);
            if (level <= 0)
                continue;

            var items = entry.Items.ToArray().Select(item => item.ItemId).ToArray();
            var name = string.IsNullOrWhiteSpace(entry.NameString)
                ? classJob.Name.ToString()
                : entry.NameString;
            result.Add(new GearsetSnapshot(
                entry.Id,
                entry.ClassJob,
                classJob.JobType is >= 1 and <= 6,
                classJob.ItemSoulCrystal.RowId != 0,
                level,
                name,
                items));
        }

        return result;
    }

    public IReadOnlyList<uint> GetEquippedItemIds()
    {
        var items = new uint[EquippedItemCount];
        var inventory = InventoryManager.Instance();
        var container = inventory == null
            ? null
            : inventory->GetInventoryContainer((InventoryType)EquippedItemsContainer);
        if (container == null)
            return items;

        for (var slot = 0; slot < Math.Min(EquippedItemCount, container->Size); slot++)
        {
            var item = container->GetInventorySlot(slot);
            items[slot] = item == null ? 0 : item->ItemId;
        }
        return items;
    }

    public bool TryEquipGearset(int gearsetId, out string error)
    {
        try
        {
            var module = RaptureGearsetModule.Instance();
            if (module == null || gearsetId is < 0 or >= GearsetCapacity || !module->IsValidGearset(gearsetId))
            {
                error = $"Gearset {gearsetId} is unavailable or invalid.";
                return false;
            }

            var result = module->EquipGearset(gearsetId);
            error = result == 0 ? string.Empty : $"Native EquipGearset returned {result}.";
            return result == 0;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public bool IsGearsetEquipped(int gearsetId, uint classJobId)
        => CurrentGearsetId == gearsetId && CurrentJobId == classJobId;

    public bool TryBeginRecommendedEquipment(uint classJobId, out string error)
        => recommendedEquip.TryBegin(classJobId, out error);

    public RecommendedEquipmentProgress PollRecommendedEquipment(out string error)
        => recommendedEquip.Poll(out error);

    public void CancelRecommendedEquipment()
        => recommendedEquip.Cancel();

    public bool TryUpdateGearset(int gearsetId, IReadOnlyList<uint> expectedItemIds, out string error)
    {
        try
        {
            var module = RaptureGearsetModule.Instance();
            if (module == null || gearsetId is < 0 or >= GearsetCapacity || !module->IsValidGearset(gearsetId))
            {
                error = $"Gearset {gearsetId} is unavailable or invalid.";
                return false;
            }

            var result = module->UpdateGearset(gearsetId);
            if (result is < 0 or >= GearsetCapacity)
            {
                error = $"Native UpdateGearset returned {result} for gearset {gearsetId}.";
                return false;
            }

            module->SaveFile(false);
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public bool IsGearsetSaveVerified(int gearsetId, IReadOnlyList<uint> expectedItemIds, out string error)
    {
        try
        {
            var module = RaptureGearsetModule.Instance();
            if (module == null || gearsetId is < 0 or >= GearsetCapacity || !module->IsValidGearset(gearsetId))
            {
                error = $"Gearset {gearsetId} is unavailable or invalid.";
                return false;
            }

            var entry = module->GetGearset(gearsetId);
            if (entry == null)
            {
                error = $"Gearset {gearsetId} could not be read after save.";
                return false;
            }

            var saved = entry->Items.ToArray().Select(item => item.ItemId).ToArray();
            if (!EquipmentAutomationPolicy.ItemSignaturesMatch(expectedItemIds, saved))
            {
                error = "Saved gearset items do not match the equipped item signature.";
                return false;
            }

            if (module->GetIsSavePending())
            {
                error = "Native gearset file save is still pending.";
                return false;
            }

            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public IReadOnlyList<SeasonalInventoryItem> FindSeasonalInventoryItems(IReadOnlyCollection<uint> curatedItemIds)
    {
        var found = new List<SeasonalInventoryItem>();
        var sheet = dataManager.GetExcelSheet<Item>();
        var inventory = InventoryManager.Instance();
        if (sheet == null || inventory == null)
            return found;

        foreach (var itemId in curatedItemIds.Distinct())
        {
            if (!sheet.TryGetRow(itemId, out var item) || !TryDeriveEquipmentSlot(item, out var equipmentSlot))
                continue;

            if (TryFindInventorySlot(inventory, itemId, equipmentSlot, out _, out _))
                found.Add(new SeasonalInventoryItem(itemId, item.Name.ToString(), equipmentSlot));
        }

        return found;
    }

    public bool TryMoveSeasonalItemToEquipped(SeasonalInventoryItem item, out string error)
    {
        if (!framework.IsInFrameworkUpdateThread)
        {
            error = "Inventory moves must run on the framework update thread.";
            return false;
        }

        try
        {
            var inventory = InventoryManager.Instance();
            if (inventory == null || !TryFindInventorySlot(inventory, item.ItemId, item.Slot, out var sourceType, out var sourceSlot))
            {
                error = $"{item.Name} is no longer present in inventory or the Armoury Chest.";
                return false;
            }

            var result = inventory->MoveItemSlot(
                (InventoryType)sourceType,
                sourceSlot,
                (InventoryType)EquippedItemsContainer,
                (ushort)item.Slot);
            error = result == 0 ? string.Empty : $"Native MoveItemSlot returned {result}.";
            return result == 0;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public bool IsSeasonalItemEquipped(SeasonalInventoryItem item)
    {
        var inventory = InventoryManager.Instance();
        var equipped = inventory == null
            ? null
            : inventory->GetInventoryContainer((InventoryType)EquippedItemsContainer);
        var slot = (int)item.Slot;
        if (equipped == null || slot >= equipped->Size)
            return false;
        var equippedItem = equipped->GetInventorySlot(slot);
        return equippedItem != null && equippedItem->ItemId == item.ItemId;
    }

    private static bool TryDeriveEquipmentSlot(Item item, out EquipmentSlot slot)
    {
        var category = item.EquipSlotCategory.Value;
        if (category.Head != 0) slot = EquipmentSlot.Head;
        else if (category.Body != 0) slot = EquipmentSlot.Body;
        else if (category.Gloves != 0) slot = EquipmentSlot.Hands;
        else if (category.Legs != 0) slot = EquipmentSlot.Legs;
        else if (category.Feet != 0) slot = EquipmentSlot.Feet;
        else
        {
            slot = default;
            return false;
        }

        return true;
    }

    private static bool TryFindInventorySlot(
        InventoryManager* inventory,
        uint itemId,
        EquipmentSlot slot,
        out ushort containerType,
        out ushort inventorySlot)
    {
        var containers = new[] { ArmouryContainers[slot] }.Concat(InventoryContainers);
        foreach (var candidate in containers)
        {
            var container = inventory->GetInventoryContainer((InventoryType)candidate);
            if (container == null)
                continue;

            for (ushort index = 0; index < container->Size; index++)
            {
                var nativeItem = container->GetInventorySlot(index);
                if (nativeItem != null && nativeItem->ItemId == itemId && nativeItem->Quantity > 0)
                {
                    containerType = candidate;
                    inventorySlot = index;
                    return true;
                }
            }
        }

        containerType = 0;
        inventorySlot = 0;
        return false;
    }
}
