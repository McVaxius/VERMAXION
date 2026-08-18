using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;
using VERMAXION.IPC;
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
    private readonly IClientState clientState;
    private readonly ICondition condition;
    private readonly IObjectTable objectTable;
    private readonly IPluginLog log;
    private readonly StylistIPC stylist;
    private readonly RecommendedEquipHelper recommendedEquip = new();
    private bool stylistUpdateActive;
    private DateTime stylistFirstPollAt;
    private DateTime stylistNextPollAt;

    public NativeEquipmentAutomationRuntime(
        IDataManager dataManager,
        IFramework framework,
        IPlayerState playerState,
        IClientState clientState,
        ICondition condition,
        IObjectTable objectTable,
        StylistIPC stylist,
        IPluginLog log)
    {
        this.dataManager = dataManager;
        this.framework = framework;
        this.playerState = playerState;
        this.clientState = clientState;
        this.condition = condition;
        this.objectTable = objectTable;
        this.stylist = stylist;
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

    public IReadOnlyList<UnlockedJobSnapshot> GetUnlockedJobs()
    {
        var result = new List<UnlockedJobSnapshot>();
        var sheet = dataManager.GetExcelSheet<ClassJob>();
        var nativePlayerState = PlayerState.Instance();
        if (sheet == null || nativePlayerState == null || !nativePlayerState->IsLoaded)
            return result;

        foreach (var classJob in sheet)
        {
            if (classJob.RowId == 0)
                continue;

            var level = (int)nativePlayerState->GetClassJobLevel((int)classJob.RowId, false);
            var abbreviation = classJob.Abbreviation.ToString().Trim();
            if (level <= 0 || string.IsNullOrWhiteSpace(abbreviation))
                continue;

            result.Add(new UnlockedJobSnapshot(
                classJob.RowId,
                classJob.JobType is >= 1 and <= 6,
                classJob.ItemSoulCrystal.RowId != 0,
                level,
                classJob.Name.ToString(),
                abbreviation));
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

    public bool TryConfirmGearsetChangePrompt()
    {
        try
        {
            nint addonAddress = Plugin.GameGui.GetAddonByName("SelectYesno", 1);
            if (addonAddress == nint.Zero)
                return false;

            var addon = (AddonSelectYesno*)addonAddress;
            if (!addon->AtkUnitBase.IsVisible || !addon->AtkUnitBase.IsReady)
                return false;

            new AddonMaster.SelectYesno(&addon->AtkUnitBase).Yes();
            log.Information("[Equipment] Confirmed ready SelectYesno during owned native gearset-change window.");
            return true;
        }
        catch (Exception ex)
        {
            log.Warning($"[Equipment] Native gearset confirmation check failed: {ex.Message}");
            return false;
        }
    }

    public bool TryBeginRecommendedEquipment(uint classJobId, out string error)
        => recommendedEquip.TryBegin(classJobId, out error);

    public RecommendedEquipmentProgress PollRecommendedEquipment(out string error)
        => recommendedEquip.Poll(out error);

    public void CancelRecommendedEquipment()
        => recommendedEquip.Cancel();

    public bool TryBeginStylistGearsetUpdate(int gearsetId, out string error)
    {
        stylistUpdateActive = false;
        stylistFirstPollAt = DateTime.MinValue;
        stylistNextPollAt = DateTime.MinValue;
        if (!stylist.TryStartUpdate(gearsetId, out error))
            return false;

        stylistUpdateActive = true;
        stylistFirstPollAt = DateTime.UtcNow + TimeSpan.FromMilliseconds(100);
        stylistNextPollAt = stylistFirstPollAt;
        return true;
    }

    public StylistGearsetUpdateProgress PollStylistGearsetUpdate(out string error)
    {
        if (!stylistUpdateActive)
        {
            error = "No Stylist gearset update is active.";
            return StylistGearsetUpdateProgress.Failed;
        }

        var now = DateTime.UtcNow;
        if (now < stylistFirstPollAt || now < stylistNextPollAt)
        {
            error = string.Empty;
            return StylistGearsetUpdateProgress.Pending;
        }
        stylistNextPollAt = now + TimeSpan.FromMilliseconds(100);

        if (!stylist.TryReadBusy(out var busy, out error))
        {
            stylistUpdateActive = false;
            return StylistGearsetUpdateProgress.Failed;
        }
        if (busy)
            return StylistGearsetUpdateProgress.Pending;

        stylistUpdateActive = false;
        error = string.Empty;
        return StylistGearsetUpdateProgress.Complete;
    }

    public bool TryMoveBestMainHandToEquipped(UnlockedJobSnapshot job, out string error)
    {
        if (!framework.IsInFrameworkUpdateThread)
        {
            error = "Inventory moves must run on the framework update thread.";
            return false;
        }

        try
        {
            if (!TryGetSafeMutationContext(out _, out _, out _, out error))
                return false;

            var inventory = InventoryManager.Instance();
            var itemSheet = dataManager.GetExcelSheet<Item>();
            if (inventory == null || itemSheet == null)
            {
                error = "Inventory or item data is unavailable.";
                return false;
            }

            var containers = new[]
            {
                InventoryType.ArmoryMainHand,
                InventoryType.Inventory1,
                InventoryType.Inventory2,
                InventoryType.Inventory3,
                InventoryType.Inventory4,
            };
            var candidates = new List<MainHandCandidate>();
            foreach (var containerType in containers)
            {
                var container = inventory->GetInventoryContainer(containerType);
                if (container == null || !container->IsLoaded)
                {
                    error = $"Inventory container {containerType} is not loaded; the main-hand scan was not complete.";
                    return false;
                }

                for (ushort slot = 0; slot < container->Size; slot++)
                {
                    var inventoryItem = container->GetInventorySlot(slot);
                    if (inventoryItem == null || inventoryItem->ItemId == 0 || inventoryItem->Quantity == 0)
                        continue;

                    var itemId = inventoryItem->ItemId % 1_000_000;
                    if (!itemSheet.TryGetRow(itemId, out var item) ||
                        item.EquipSlotCategory.Value.MainHand != 1 ||
                        item.LevelEquip > job.Level)
                    {
                        continue;
                    }

                    var compatibleJobs = item.ClassJobCategory.Value.Name.ToString()
                        .Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (!compatibleJobs.Contains(job.Abbreviation, StringComparer.OrdinalIgnoreCase))
                        continue;

                    candidates.Add(new MainHandCandidate(
                        containerType,
                        slot,
                        itemId,
                        item.LevelItem.RowId));
                }
            }

            var selected = candidates
                .OrderByDescending(candidate => candidate.ItemLevel)
                .ThenBy(candidate => candidate.ItemId)
                .FirstOrDefault();
            if (selected.ItemId == 0)
            {
                error = $"No compatible level-{job.Level} or lower {job.Abbreviation} main hand was found in ArmouryMainHand or Inventory 1-4.";
                return false;
            }

            inventory->MoveItemSlot(
                selected.Container,
                selected.Slot,
                InventoryType.EquippedItems,
                0,
                true);
            error = string.Empty;
            log.Information($"[Equipment][Bootstrap] Requested forced {job.Abbreviation} main-hand move for item {selected.ItemId}.");
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public bool TryPersistCurrentGearset(out CurrentGearsetPersistenceResult result)
    {
        result = new CurrentGearsetPersistenceResult(false, -1, 0, false, [], "Persistence did not start.");
        try
        {
            if (!TryGetSafeMutationContext(out var module, out var nativePlayerState, out var equipped, out var error))
            {
                result = result with { Error = error };
                return false;
            }

            var currentJob = (uint)nativePlayerState->CurrentClassJobId;
            var expectedItems = ReadContainerItemIds(equipped);
            var activeIndex = module->CurrentGearsetIndex;
            var active = activeIndex is >= 0 and < GearsetCapacity ? module->GetGearset(activeIndex) : null;
            var created = active == null ||
                          (active->Flags & RaptureGearsetModule.GearsetFlag.Exists) == 0 ||
                          active->ClassJob != currentJob;
            int targetId;
            if (created)
            {
                targetId = module->FirstEmptyGearsetSlot();
                if (targetId is < 0 or >= GearsetCapacity)
                {
                    result = result with { ClassJobId = currentJob, Error = "All 100 gearset slots are occupied." };
                    return false;
                }

                var nativeResult = module->CreateGearset();
                if (nativeResult != targetId)
                {
                    result = result with
                    {
                        ClassJobId = currentJob,
                        Error = $"CreateGearset returned {nativeResult}, expected {targetId}."
                    };
                    return false;
                }
            }
            else
            {
                targetId = activeIndex;
                module->UpdateGearset(targetId);
            }

            module->SaveFile(false);
            if (!TryVerifyGearsetExact(module, targetId, currentJob, expectedItems, out error))
            {
                result = new CurrentGearsetPersistenceResult(false, targetId, currentJob, created, expectedItems, error);
                return false;
            }

            result = new CurrentGearsetPersistenceResult(true, targetId, currentJob, created, expectedItems, string.Empty);
            return true;
        }
        catch (Exception ex)
        {
            result = result with { Error = ex.Message };
            return false;
        }
    }

    private readonly record struct MainHandCandidate(
        InventoryType Container,
        ushort Slot,
        uint ItemId,
        uint ItemLevel);

    public bool TryUpdateGearset(int gearsetId, IReadOnlyList<uint> expectedItemIds, out string error)
    {
        try
        {
            if (!TryGetSafeMutationContext(out var module, out var nativePlayerState, out var equipped, out error))
                return false;
            if (gearsetId is < 0 or >= GearsetCapacity || !module->IsValidGearset(gearsetId))
            {
                error = $"Gearset {gearsetId} is unavailable or invalid.";
                return false;
            }
            if (module->CurrentGearsetIndex != gearsetId)
            {
                error = $"Gearset {gearsetId} is not the active gearset.";
                return false;
            }

            var entry = module->GetGearset(gearsetId);
            var currentJob = (uint)nativePlayerState->CurrentClassJobId;
            if (entry == null || entry->ClassJob != currentJob)
            {
                error = $"Active gearset {gearsetId} does not represent current job {currentJob}.";
                return false;
            }

            var equippedItems = ReadContainerItemIds(equipped);
            if (!NormalizedItemSignaturesMatch(expectedItemIds, equippedItems))
            {
                error = "Equipped items changed before native gearset persistence.";
                return false;
            }

            var result = module->UpdateGearset(gearsetId);
            if (result is < 0 or >= GearsetCapacity)
            {
                error = $"Native UpdateGearset returned {result} for gearset {gearsetId}.";
                return false;
            }

            module->SaveFile(false);
            return TryVerifyGearsetExact(module, gearsetId, currentJob, expectedItemIds, out error);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public bool IsGearsetSaveVerified(
        int gearsetId,
        uint expectedClassJobId,
        IReadOnlyList<uint> expectedItemIds,
        out string error)
    {
        try
        {
            var module = RaptureGearsetModule.Instance();
            if (module == null || module->IsVirtual ||
                module->CharacterContentId == 0 || module->CharacterContentId != CharacterContentId ||
                gearsetId is < 0 or >= GearsetCapacity || !module->IsValidGearset(gearsetId))
            {
                error = $"Gearset {gearsetId} is unavailable or invalid.";
                return false;
            }

            if (!TryVerifyGearsetExact(module, gearsetId, expectedClassJobId, expectedItemIds, out error))
                return false;

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

    private bool TryGetSafeMutationContext(
        out RaptureGearsetModule* module,
        out PlayerState* nativePlayerState,
        out InventoryContainer* equipped,
        out string error)
    {
        module = RaptureGearsetModule.Instance();
        nativePlayerState = PlayerState.Instance();
        equipped = null;

        if (!framework.IsInFrameworkUpdateThread)
        {
            error = "Gearset mutation must run on the framework update thread.";
            return false;
        }

        var player = objectTable.LocalPlayer;
        if (!clientState.IsLoggedIn || player == null || !playerState.IsLoaded || playerState.ContentId == 0 ||
            nativePlayerState == null || !nativePlayerState->IsLoaded || module == null)
        {
            error = "Dalamud, native player, or gearset state is not loaded.";
            return false;
        }

        if (playerState.ContentId != nativePlayerState->ContentId ||
            nativePlayerState->ContentId != module->CharacterContentId)
        {
            error = "Dalamud, native, and gearset Content IDs do not agree.";
            return false;
        }

        if (playerState.ClassJob.RowId == 0 ||
            playerState.ClassJob.RowId != nativePlayerState->CurrentClassJobId)
        {
            error = "Current class/job data is not stable.";
            return false;
        }

        if (module->IsVirtual)
        {
            error = "The gearset module is virtual.";
            return false;
        }

        for (var index = 0; index < GearsetCapacity; index++)
        {
            if (module->GetGearset(index) != null)
                continue;
            error = "Gearset container data is unavailable.";
            return false;
        }

        var inventory = InventoryManager.Instance();
        equipped = inventory == null ? null : inventory->GetInventoryContainer(InventoryType.EquippedItems);
        if (equipped == null || !equipped->IsLoaded || equipped->Size < EquippedItemCount)
        {
            error = "The equipped-items container is not loaded.";
            return false;
        }

        if (condition[ConditionFlag.Unconscious] || player.CurrentHp == 0 ||
            condition[ConditionFlag.InCombat] ||
            condition[ConditionFlag.BoundByDuty] ||
            condition[ConditionFlag.BoundByDuty56] ||
            condition[ConditionFlag.BoundByDuty95] ||
            condition[ConditionFlag.Casting] ||
            condition[ConditionFlag.Mounted] ||
            condition[ConditionFlag.Mounting71] ||
            condition[ConditionFlag.InFlight] ||
            condition[ConditionFlag.BetweenAreas] ||
            condition[ConditionFlag.BetweenAreas51] ||
            condition[ConditionFlag.LoggingOut] ||
            condition[ConditionFlag.Occupied] ||
            condition[ConditionFlag.Occupied30] ||
            condition[ConditionFlag.OccupiedInEvent] ||
            condition[ConditionFlag.OccupiedInQuestEvent] ||
            condition[ConditionFlag.Occupied33] ||
            condition[ConditionFlag.OccupiedInCutSceneEvent] ||
            condition[ConditionFlag.Occupied38] ||
            condition[ConditionFlag.Occupied39] ||
            condition[ConditionFlag.WatchingCutscene] ||
            condition[ConditionFlag.WatchingCutscene78])
        {
            error = "Character conditions are unsafe for gearset mutation.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static IReadOnlyList<uint> ReadContainerItemIds(InventoryContainer* container)
    {
        var items = new uint[EquippedItemCount];
        for (var slot = 0; slot < EquippedItemCount; slot++)
        {
            var item = container->GetInventorySlot(slot);
            items[slot] = item == null ? 0 : item->ItemId;
        }
        return items;
    }

    private static bool TryVerifyGearsetExact(
        RaptureGearsetModule* module,
        int gearsetId,
        uint expectedClassJobId,
        IReadOnlyList<uint> expectedItemIds,
        out string error)
    {
        var entry = module->GetGearset(gearsetId);
        if (entry == null || (entry->Flags & RaptureGearsetModule.GearsetFlag.Exists) == 0)
        {
            error = $"Gearset {gearsetId} could not be read after persistence.";
            return false;
        }
        if (entry->ClassJob != expectedClassJobId)
        {
            error = $"Saved class/job {entry->ClassJob} did not match expected {expectedClassJobId}.";
            return false;
        }

        var soulStoneSlot = (int)RaptureGearsetModule.GearsetItemIndex.SoulStone;
        if (expectedItemIds.Count <= soulStoneSlot)
        {
            error = $"Expected equipment contained {expectedItemIds.Count} slots and omitted the soul crystal slot.";
            return false;
        }
        var savedSoulStone = entry->GetItem(RaptureGearsetModule.GearsetItemIndex.SoulStone).ItemId;
        if (NormalizeItemId(savedSoulStone) != NormalizeItemId(expectedItemIds[soulStoneSlot]))
        {
            error = $"Saved soul crystal {savedSoulStone} did not match equipped {expectedItemIds[soulStoneSlot]}.";
            return false;
        }

        var saved = entry->Items.ToArray().Select(item => item.ItemId).ToArray();
        if (saved.Length != expectedItemIds.Count)
        {
            error = $"Saved gearset contained {saved.Length} slots; expected {expectedItemIds.Count}.";
            return false;
        }

        for (var slot = 0; slot < saved.Length; slot++)
        {
            if (NormalizeItemId(saved[slot]) == NormalizeItemId(expectedItemIds[slot]))
                continue;
            error = $"Saved equipment mismatch at slot {slot}: saved {saved[slot]}, equipped {expectedItemIds[slot]}.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool NormalizedItemSignaturesMatch(IReadOnlyList<uint> left, IReadOnlyList<uint> right)
    {
        if (left.Count != right.Count)
            return false;
        for (var index = 0; index < left.Count; index++)
        {
            if (NormalizeItemId(left[index]) != NormalizeItemId(right[index]))
                return false;
        }
        return true;
    }

    private static uint NormalizeItemId(uint itemId) => itemId % 1_000_000;

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
