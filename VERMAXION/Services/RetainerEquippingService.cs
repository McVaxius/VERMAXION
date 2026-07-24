using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Dalamud.Plugin.Services;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using VERMAXION.IPC;
using VERMAXION.Models;

namespace VERMAXION.Services;

public sealed class RetainerEquippingService
{
    private enum EquippingState
    {
        Idle,
        Inspecting,
        OpeningBell,
        SelectingForScan,
        OpeningGearForScan,
        ReadingGear,
        ReturningToList,
        Allocating,
        SelectingForMoves,
        OpeningGearForMoves,
        Moving,
        Complete,
        Failed,
    }

    private readonly IPluginLog log;
    private readonly ConfigManager configManager;
    private readonly AutoRetainerIPC autoRetainer;
    private readonly WorkshopBellService bell;
    private readonly NativeRetainerEquipmentRuntime runtime;
    private EquippingState state;
    private DateTime stateEnteredAt;
    private CharacterConfig? config;
    private List<AutoRetainerRetainerSnapshot> targeted = [];
    private List<RetainerEquipmentProfile> profiles = [];
    private IReadOnlyList<RetainerGearCandidate> candidates = [];
    private IReadOnlyList<RetainerEquipmentMove> moves = [];
    private int retainerIndex;
    private int moveIndex;
    private bool ownsRetainerList;
    private bool preserveCheckpointOnCleanup;
    private bool returningFromMoves;
    private string retrySignature = string.Empty;

    public RetainerEquippingService(
        IPluginLog log,
        ConfigManager configManager,
        AutoRetainerIPC autoRetainer,
        WorkshopBellService bell,
        IDataManager dataManager,
        IFramework framework)
    {
        this.log = log;
        this.configManager = configManager;
        this.autoRetainer = autoRetainer;
        this.bell = bell;
        runtime = new NativeRetainerEquipmentRuntime(dataManager, framework, log);
    }

    public bool IsActive => state is not (EquippingState.Idle or EquippingState.Complete or EquippingState.Failed);
    public bool IsComplete => state == EquippingState.Complete;
    public bool IsFailed => state == EquippingState.Failed;
    public string StatusText { get; private set; } = "Idle";

    public void Start(CharacterConfig activeConfig)
    {
        if (IsActive)
            return;
        ResetLocal();
        config = activeConfig;
        SetState(EquippingState.Inspecting, "Inspecting AutoRetainer-enabled retainers");
    }

    public void Update()
    {
        if (!IsActive || config == null)
            return;

        switch (state)
        {
            case EquippingState.Inspecting:
                TickInspecting();
                break;
            case EquippingState.OpeningBell:
                TickOpeningBell();
                break;
            case EquippingState.SelectingForScan:
                TickSelectRetainer(forMoves: false);
                break;
            case EquippingState.OpeningGearForScan:
                TickOpenGear(forMoves: false);
                break;
            case EquippingState.ReadingGear:
                TickReadGear();
                break;
            case EquippingState.ReturningToList:
                TickReturnToList();
                break;
            case EquippingState.Allocating:
                TickAllocate();
                break;
            case EquippingState.SelectingForMoves:
                TickSelectRetainer(forMoves: true);
                break;
            case EquippingState.OpeningGearForMoves:
                TickOpenGear(forMoves: true);
                break;
            case EquippingState.Moving:
                TickMove();
                break;
        }
    }

    public void CleanupAfterDispatch()
    {
        runtime.CloseOwnedRetainerUi(ownsRetainerList);
        ownsRetainerList = false;
        bell.Reset();
        if (!preserveCheckpointOnCleanup)
            RestoreCollectOnly();
        ResetLocal();
    }

    public void Cancel()
    {
        runtime.CloseOwnedRetainerUi(ownsRetainerList);
        ownsRetainerList = false;
        bell.Reset();
        RestoreCollectOnly();
        ResetLocal();
    }

    public void RestoreCollectOnly(bool preserveCheckpoint = false)
    {
        var active = config ?? configManager.GetActiveConfig();
        if (active == null)
            return;

        var restoration = RetainerCollectOnlyRestorationPolicy.Decide(
            active.RetainerEquipmentCheckpointPending,
            active.RetainerEquipmentOriginalCollectOnly,
            preserveCheckpoint);
        if (!restoration.ShouldRestore)
            return;

        if (!autoRetainer.TrySetCollectOnly(restoration.RestoreValue, out var error))
        {
            log.Warning($"[RetainerEquip] Could not restore AutoRetainer collect-only={restoration.RestoreValue}: {error}");
            return;
        }

        if (restoration.ClearCheckpoint)
        {
            active.RetainerEquipmentCheckpointPending = false;
            active.RetainerEquipmentOriginalCollectOnly = null;
        }
        configManager.SaveCurrentAccount();
        log.Information(preserveCheckpoint
            ? $"[RetainerEquip] Restored AutoRetainer collect-only={restoration.RestoreValue}; checkpoint preserved across rotation."
            : $"[RetainerEquip] Restored AutoRetainer collect-only={restoration.RestoreValue} and cleared checkpoint.");
    }

    private void TickInspecting()
    {
        if (config!.RetainerCombatItemLevelTarget <= 0 &&
            config.RetainerGatheringPerceptionTarget <= 0)
        {
            Complete("Both retainer equipment targets are zero; no work is required.");
            return;
        }

        var contentId = Plugin.PlayerState.ContentId;
        var read = autoRetainer.ReadEnabledRetainers(contentId);
        if (!read.Success)
        {
            Fail($"AutoRetainer retainer data was unavailable: {read.Error}");
            return;
        }

        targeted = read.Retainers
            .Where(retainer =>
            {
                var target = retainer.IsGathering
                    ? config.RetainerGatheringPerceptionTarget
                    : config.RetainerCombatItemLevelTarget;
                var current = retainer.IsGathering ? retainer.Perception : retainer.ItemLevel;
                return target > 0 && (current < 0 || current < target);
            })
            .ToList();
        if (targeted.Count == 0)
        {
            config.RetainerEquipmentLastUnmetSignature = string.Empty;
            configManager.SaveCurrentAccount();
            Complete("All AutoRetainer-enabled retainers already meet their configured target.");
            return;
        }

        if (!EnsureCollectOnly())
            return;

        if (targeted.Any(retainer => retainer.HasVenture))
        {
            preserveCheckpointOnCleanup = true;
            Complete("Venture reassignment suppressed; AutoRetainer must collect targeted retainers before equipping.");
            return;
        }

        candidates = runtime.ScanCandidates(targeted, config.RetainerGearSourceMode);
        retrySignature = RetainerEquipmentPolicy.BuildRetrySignature(
            config.RetainerCombatItemLevelTarget,
            config.RetainerGatheringPerceptionTarget,
            config.RetainerGearSourceMode,
            config.RetainerGearNonUniqueOnly,
            candidates);
        if (string.Equals(
                retrySignature,
                config.RetainerEquipmentLastUnmetSignature,
                StringComparison.Ordinal))
        {
            RestoreCollectOnly();
            Complete("Candidate inventory and retainer targets match the previous bounded unmet attempt; retry suppressed.");
            return;
        }

        if (GameHelpers.IsAddonVisible("RetainerList"))
        {
            Fail("A retainer list was already open; VerMAXion will not adopt an unowned bell session.");
            return;
        }

        bell.Start(config.RefillFromListingsRoute);
        SetState(EquippingState.OpeningBell, "Opening an owned retainer bell session");
    }

    private bool EnsureCollectOnly()
    {
        if (!config!.RetainerEquipmentCheckpointPending)
        {
            var current = autoRetainer.ReadCollectOnly();
            if (!current.Success)
            {
                Fail($"AutoRetainer collect-only state was unreadable: {current.Error}");
                return false;
            }
            config.RetainerEquipmentOriginalCollectOnly = current.Enabled;
            config.RetainerEquipmentCheckpointPending = true;
            configManager.SaveCurrentAccount();
        }

        if (autoRetainer.TrySetCollectOnly(true, out var error))
            return true;
        Fail($"AutoRetainer collect-only could not be enabled: {error}");
        return false;
    }

    private void TickOpeningBell()
    {
        bell.Update();
        StatusText = bell.StatusText;
        if (bell.IsFailed)
        {
            Fail(bell.LastError);
            return;
        }
        if (!bell.IsComplete)
            return;
        ownsRetainerList = true;
        retainerIndex = 0;
        SetState(EquippingState.SelectingForScan, "Selecting first targeted retainer");
    }

    private void TickSelectRetainer(bool forMoves)
    {
        if (DateTime.UtcNow - stateEnteredAt > TimeSpan.FromSeconds(15))
        {
            Fail("Timed out selecting the exact AutoRetainer-enabled retainer.");
            return;
        }

        var target = forMoves
            ? targeted.First(retainer => retainer.RetainerId == moves[moveIndex].RetainerId)
            : targeted[retainerIndex];
        if (!runtime.TrySelectRetainer(target.Name))
            return;
        SetState(
            forMoves ? EquippingState.OpeningGearForMoves : EquippingState.OpeningGearForScan,
            $"Opening equipment for {target.Name}");
    }

    private void TickOpenGear(bool forMoves)
    {
        if (runtime.IsRetainerGearWindowReady)
        {
            SetState(
                forMoves ? EquippingState.Moving : EquippingState.ReadingGear,
                forMoves ? "Applying verified retainer upgrades" : "Reading current retainer equipment");
            return;
        }
        if (DateTime.UtcNow - stateEnteredAt > TimeSpan.FromSeconds(15))
        {
            Fail("Timed out opening the localized retainer equipment menu.");
            return;
        }
        runtime.TrySelectGearOption();
    }

    private void TickReadGear()
    {
        var snapshot = targeted[retainerIndex];
        if (!runtime.TryReadProfile(snapshot, out var profile))
        {
            Fail($"Could not read current equipment for {snapshot.Name}.");
            return;
        }
        profiles.Add(profile);
        returningFromMoves = false;
        SetState(EquippingState.ReturningToList, "Returning to the owned retainer list");
    }

    private void TickReturnToList()
    {
        if (!runtime.TryReturnToRetainerList())
        {
            if (DateTime.UtcNow - stateEnteredAt > TimeSpan.FromSeconds(20))
                Fail("Timed out returning to the owned retainer list.");
            return;
        }

        if (returningFromMoves)
        {
            if (moveIndex < moves.Count)
                SetState(EquippingState.SelectingForMoves, "Selecting next retainer for upgrades");
            else
                FinishMoves();
            return;
        }

        retainerIndex++;
        if (retainerIndex < targeted.Count)
            SetState(EquippingState.SelectingForScan, "Selecting next targeted retainer");
        else
            SetState(EquippingState.Allocating, "Building a distinct physical-item allocation");
    }

    private void TickAllocate()
    {
        var result = RetainerEquipmentPolicy.Allocate(
            profiles,
            candidates,
            config!.RetainerGearSourceMode,
            config.RetainerGearNonUniqueOnly);
        moves = result.Moves
            .OrderBy(move => targeted.FindIndex(retainer => retainer.RetainerId == move.RetainerId))
            .ThenBy(move => move.Slot)
            .ToList();
        if (moves.Count == 0)
        {
            config.RetainerEquipmentLastUnmetSignature = retrySignature;
            configManager.SaveCurrentAccount();
            runtime.CloseOwnedRetainerUi(ownsRetainerList);
            ownsRetainerList = false;
            RestoreCollectOnly();
            Complete("No eligible physical item would improve the targeted metric; unmet signature stored.");
            return;
        }

        moveIndex = 0;
        SetState(EquippingState.SelectingForMoves, "Selecting first retainer for upgrades");
    }

    private void TickMove()
    {
        if (moveIndex >= moves.Count)
        {
            returningFromMoves = true;
            SetState(EquippingState.ReturningToList, "Closing final retainer equipment window");
            return;
        }

        var move = moves[moveIndex];
        if (moveIndex > 0 && moves[moveIndex - 1].RetainerId != move.RetainerId)
        {
            returningFromMoves = true;
            SetState(EquippingState.ReturningToList, "Returning for next retainer");
            return;
        }

        if (!runtime.TryMoveAndVerify(move, out var error))
            log.Warning($"[RetainerEquip] Upgrade skipped after move verification failure: {error}");
        moveIndex++;
    }

    private void FinishMoves()
    {
        config!.RetainerEquipmentLastUnmetSignature = retrySignature;
        configManager.SaveCurrentAccount();
        runtime.CloseOwnedRetainerUi(ownsRetainerList);
        ownsRetainerList = false;
        RestoreCollectOnly();
        Complete("Retainer equipment allocation completed; successful upgrades were retained.");
    }

    private void Complete(string status)
    {
        StatusText = status;
        state = EquippingState.Complete;
        log.Information($"[RetainerEquip] {status}");
    }

    private void Fail(string status)
    {
        StatusText = status;
        state = EquippingState.Failed;
        runtime.CloseOwnedRetainerUi(ownsRetainerList);
        ownsRetainerList = false;
        bell.Reset();
        RestoreCollectOnly();
        log.Warning($"[RetainerEquip] {status}");
    }

    private void SetState(EquippingState next, string status)
    {
        state = next;
        stateEnteredAt = DateTime.UtcNow;
        StatusText = status;
    }

    private void ResetLocal()
    {
        state = EquippingState.Idle;
        stateEnteredAt = DateTime.MinValue;
        StatusText = "Idle";
        config = null;
        targeted = [];
        profiles = [];
        candidates = [];
        moves = [];
        retainerIndex = 0;
        moveIndex = 0;
        ownsRetainerList = false;
        preserveCheckpointOnCleanup = false;
        returningFromMoves = false;
        retrySignature = string.Empty;
    }
}

internal sealed unsafe class NativeRetainerEquipmentRuntime
{
    private static readonly InventoryType[] InventorySources =
    [
        InventoryType.Inventory1,
        InventoryType.Inventory2,
        InventoryType.Inventory3,
        InventoryType.Inventory4,
    ];

    private static readonly InventoryType[] ArmorySources =
    [
        InventoryType.ArmoryMainHand,
        InventoryType.ArmoryOffHand,
        InventoryType.ArmoryHead,
        InventoryType.ArmoryBody,
        InventoryType.ArmoryHands,
        InventoryType.ArmoryLegs,
        InventoryType.ArmoryFeets,
        InventoryType.ArmoryEar,
        InventoryType.ArmoryNeck,
        InventoryType.ArmoryWrist,
        InventoryType.ArmoryRings,
    ];
    private static readonly HashSet<uint> AutoRetainerOffhandItemUiCategories =
    [
        2, 6, 8, 12, 14, 16, 18, 20, 22, 24, 26, 28, 30, 32,
    ];

    private readonly IDataManager dataManager;
    private readonly IFramework framework;
    private readonly IPluginLog log;

    public NativeRetainerEquipmentRuntime(
        IDataManager dataManager,
        IFramework framework,
        IPluginLog log)
    {
        this.dataManager = dataManager;
        this.framework = framework;
        this.log = log;
    }

    public bool IsRetainerGearWindowReady => GameHelpers.IsAddonVisible("RetainerCharacter");

    public IReadOnlyList<RetainerGearCandidate> ScanCandidates(
        IReadOnlyList<AutoRetainerRetainerSnapshot> retainers,
        RetainerGearSourceMode sourceMode)
    {
        var result = new List<RetainerGearCandidate>();
        var inventory = InventoryManager.Instance();
        var sheet = dataManager.GetExcelSheet<Item>();
        if (inventory == null || sheet == null)
            return result;

        var gearsetCounts = sourceMode == RetainerGearSourceMode.IgnoreGearset
            ? ReadSavedGearsetItemCounts()
            : new Dictionary<uint, int>();
        var sources = sourceMode == RetainerGearSourceMode.IgnoreArmory
            ? InventorySources
            : InventorySources.Concat(ArmorySources).ToArray();
        foreach (var source in sources)
        {
            var container = inventory->GetInventoryContainer(source);
            if (container == null)
                continue;
            for (var slotIndex = 0; slotIndex < container->Size; slotIndex++)
            {
                var native = container->GetInventorySlot(slotIndex);
                if (native == null || native->ItemId == 0 || native->Quantity == 0 ||
                    !sheet.TryGetRow(native->ItemId, out var item) ||
                    !TryGetSlot(item, out var slot, out var isRing))
                {
                    continue;
                }

                var inGearset = gearsetCounts.TryGetValue(native->ItemId, out var remaining) && remaining > 0;
                if (inGearset)
                    gearsetCounts[native->ItemId] = remaining - 1;
                var compatible = retainers
                    .Where(retainer => IsJobCompatible(item, retainer.JobId))
                    .Select(retainer => retainer.RetainerId)
                    .ToHashSet();
                if (compatible.Count == 0)
                    continue;

                result.Add(new RetainerGearCandidate
                {
                    ItemId = native->ItemId,
                    Source = InventorySources.Contains(source)
                        ? RetainerGearSource.Inventory
                        : RetainerGearSource.Armory,
                    Container = (int)source,
                    ContainerSlot = slotIndex,
                    Slot = slot,
                    IsRing = isRing,
                    IsUnique = item.IsUnique,
                    IsInSavedGearset = inGearset,
                    RequiredLevel = item.LevelEquip,
                    ItemLevel = (int)item.LevelItem.RowId,
                    Perception = ReadPerception(item, native),
                    CompatibleRetainerIds = compatible,
                });
            }
        }
        return result;
    }

    public bool TrySelectRetainer(string expectedName)
    {
        var addon = RaptureAtkUnitManager.Instance()->GetAddonByName("RetainerList");
        if (addon == null || !addon->IsVisible || !addon->IsReady)
            return false;
        for (var index = 0; index < 10; index++)
        {
            var offset = 3 + index * 10;
            if (offset + 8 >= addon->AtkValuesCount)
                break;
            var name = ReadAtkValueString(addon->AtkValues[offset]);
            var activeValue = addon->AtkValues[offset + 8];
            var active = activeValue.Type switch
            {
                AtkValueType.Bool => activeValue.Byte != 0,
                AtkValueType.Int => activeValue.Int != 0,
                AtkValueType.UInt => activeValue.UInt != 0,
                _ => false,
            };
            if (active && string.Equals(name, expectedName, StringComparison.OrdinalIgnoreCase))
            {
                GameHelpers.FireAddonCallback("RetainerList", true, 2, (uint)index, 0, 0);
                return true;
            }
        }
        return false;
    }

    public bool TrySelectGearOption()
    {
        var expected = new[]
        {
            dataManager.GetExcelSheet<Addon>().GetRow(2388).Text.ToString(),
            dataManager.GetExcelSheet<Addon>().GetRow(2389).Text.ToString(),
        };
        return TrySelectString(expected);
    }

    public bool TryReturnToRetainerList()
    {
        if (GameHelpers.IsAddonVisible("RetainerCharacter"))
        {
            GameHelpers.FireAddonCallback("RetainerCharacter", true, -1);
            return false;
        }
        if (GameHelpers.IsAddonVisible("SelectString"))
        {
            var exit = dataManager.GetExcelSheet<Addon>().GetRow(917).Text.ToString();
            TrySelectString([exit]);
            return false;
        }
        return GameHelpers.IsAddonVisible("RetainerList");
    }

    public bool TryReadProfile(
        AutoRetainerRetainerSnapshot snapshot,
        out RetainerEquipmentProfile profile)
    {
        var current = new Dictionary<RetainerEquipmentSlot, int>();
        var inventory = InventoryManager.Instance();
        var sheet = dataManager.GetExcelSheet<Item>();
        var equipped = inventory == null
            ? null
            : inventory->GetInventoryContainer(InventoryType.RetainerEquippedItems);
        if (equipped == null || sheet == null)
        {
            profile = null!;
            return false;
        }

        foreach (var pair in DestinationSlots)
        {
            var native = equipped->GetInventorySlot(pair.Value);
            var metric = 0;
            if (native != null && native->ItemId != 0 && sheet.TryGetRow(native->ItemId, out var item))
            {
                metric = snapshot.IsGathering
                    ? ReadPerception(item, native)
                    : (int)item.LevelItem.RowId;
            }
            current[pair.Key] = metric;
        }

        var combatWeights = DestinationSlots.Keys.ToDictionary(slot => slot, _ => 1);
        if (!snapshot.IsGathering)
        {
            var mainHand = equipped->GetInventorySlot(DestinationSlots[RetainerEquipmentSlot.MainHand]);
            if (mainHand != null &&
                mainHand->ItemId != 0 &&
                sheet.TryGetRow(mainHand->ItemId, out var mainHandItem) &&
                !AutoRetainerOffhandItemUiCategories.Contains(mainHandItem.ItemUICategory.RowId))
            {
                combatWeights[RetainerEquipmentSlot.MainHand] = 2;
                combatWeights[RetainerEquipmentSlot.OffHand] = 0;
            }
        }

        profile = new RetainerEquipmentProfile(
            snapshot.RetainerId,
            snapshot.IsGathering
                ? RetainerMetricKind.GatheringPerception
                : RetainerMetricKind.CombatItemLevel,
            snapshot.Level,
            snapshot.IsGathering ? snapshot.Perception : snapshot.ItemLevel,
            current)
        {
            CombatSlotWeights = combatWeights,
            CombatMetricDivisor = 12,
        };
        return true;
    }

    public bool TryMoveAndVerify(RetainerEquipmentMove move, out string error)
    {
        error = string.Empty;
        if (!framework.IsInFrameworkUpdateThread)
        {
            error = "Inventory move was attempted outside the framework update thread.";
            return false;
        }
        var inventory = InventoryManager.Instance();
        if (inventory == null)
        {
            error = "InventoryManager was unavailable.";
            return false;
        }
        var source = (InventoryType)move.Candidate.Container;
        var sourceContainer = inventory->GetInventoryContainer(source);
        var sourceItem = sourceContainer == null
            ? null
            : sourceContainer->GetInventorySlot(move.Candidate.ContainerSlot);
        if (sourceItem == null || sourceItem->ItemId != move.Candidate.ItemId)
        {
            error = $"Physical candidate {move.Candidate.PhysicalKey} was no longer present.";
            return false;
        }

        var destination = DestinationSlots[move.Slot];
        var result = inventory->MoveItemSlot(
            source,
            (ushort)move.Candidate.ContainerSlot,
            InventoryType.RetainerEquippedItems,
            destination,
            true);
        if (result != 0)
        {
            error = $"Native retainer equipment move returned {result}.";
            return false;
        }

        var equipped = inventory->GetInventoryContainer(InventoryType.RetainerEquippedItems);
        var verified = equipped == null ? null : equipped->GetInventorySlot(destination);
        if (verified == null || verified->ItemId != move.Candidate.ItemId)
        {
            error = "The destination slot did not verify the requested physical item.";
            return false;
        }
        return true;
    }

    public void CloseOwnedRetainerUi(bool owned)
    {
        if (!owned)
            return;
        if (GameHelpers.IsAddonVisible("RetainerCharacter"))
            GameHelpers.FireAddonCallback("RetainerCharacter", true, -1);
        if (GameHelpers.IsAddonVisible("SelectString"))
            TryReturnToRetainerList();
        if (GameHelpers.IsAddonVisible("RetainerList"))
            GameHelpers.FireAddonCallback("RetainerList", true, -1);
    }

    private bool TrySelectString(IReadOnlyCollection<string> expected)
    {
        var pointer = RaptureAtkUnitManager.Instance()->GetAddonByName("SelectString");
        if (pointer == null || !pointer->IsVisible || !pointer->IsReady)
            return false;
        var master = new AddonMaster.SelectString((AddonSelectString*)pointer);
        foreach (var entry in master.Entries)
        {
            if (expected.Any(value => string.Equals(
                    NormalizeText(value),
                    NormalizeText(entry.Text),
                    StringComparison.Ordinal)))
            {
                entry.Select();
                return true;
            }
        }
        return false;
    }

    private bool IsJobCompatible(Item item, uint jobId)
    {
        if (!dataManager.GetExcelSheet<ClassJob>().TryGetRow(jobId, out var job))
            return false;
        var abbreviation = job.Abbreviation.ToString();
        var category = item.ClassJobCategory.Value;
        var property = category.GetType().GetProperty(
            abbreviation,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
        return property?.GetValue(category) is bool value && value;
    }

    private static bool TryGetSlot(
        Item item,
        out RetainerEquipmentSlot slot,
        out bool isRing)
    {
        var category = item.EquipSlotCategory.Value;
        isRing = false;
        if (category.MainHand != 0) slot = RetainerEquipmentSlot.MainHand;
        else if (category.OffHand != 0) slot = RetainerEquipmentSlot.OffHand;
        else if (category.Head != 0) slot = RetainerEquipmentSlot.Head;
        else if (category.Body != 0) slot = RetainerEquipmentSlot.Body;
        else if (category.Gloves != 0) slot = RetainerEquipmentSlot.Hands;
        else if (category.Legs != 0) slot = RetainerEquipmentSlot.Legs;
        else if (category.Feet != 0) slot = RetainerEquipmentSlot.Feet;
        else if (category.Ears != 0) slot = RetainerEquipmentSlot.Ears;
        else if (category.Neck != 0) slot = RetainerEquipmentSlot.Neck;
        else if (category.Wrists != 0) slot = RetainerEquipmentSlot.Wrists;
        else if (category.FingerL != 0 || category.FingerR != 0)
        {
            slot = RetainerEquipmentSlot.RingLeft;
            isRing = true;
        }
        else
        {
            slot = default;
            return false;
        }
        return true;
    }

    private static int ReadPerception(Item item, InventoryItem* native) =>
        (int)InventoryItem.GetParameterValue(
            73,
            native,
            includeMateria: true,
            checkHQ: true,
            checkPvPCharacterFlag: false,
            checkPvPItemFlag: false);

    private static Dictionary<uint, int> ReadSavedGearsetItemCounts()
    {
        var counts = new Dictionary<uint, int>();
        var module = RaptureGearsetModule.Instance();
        if (module == null)
            return counts;
        for (var index = 0; index < 100; index++)
        {
            ref var entry = ref module->Entries[index];
            if ((entry.Flags & RaptureGearsetModule.GearsetFlag.Exists) == 0)
                continue;
            foreach (var item in entry.Items.ToArray())
            {
                if (item.ItemId == 0)
                    continue;
                counts[item.ItemId] = counts.GetValueOrDefault(item.ItemId) + 1;
            }
        }
        return counts;
    }

    private static string ReadAtkValueString(AtkValue value)
    {
        if (value.Type != AtkValueType.String || !value.String.HasValue)
            return string.Empty;
        try
        {
            return Dalamud.Memory.MemoryHelper
                .ReadSeStringNullTerminated(new nint(value.String))
                .TextValue
                .Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string NormalizeText(string value) =>
        string.Concat(value.Where(character => !char.IsWhiteSpace(character))).Trim();

    private static readonly IReadOnlyDictionary<RetainerEquipmentSlot, ushort> DestinationSlots =
        new Dictionary<RetainerEquipmentSlot, ushort>
        {
            [RetainerEquipmentSlot.MainHand] = 0,
            [RetainerEquipmentSlot.OffHand] = 1,
            [RetainerEquipmentSlot.Head] = 2,
            [RetainerEquipmentSlot.Body] = 3,
            [RetainerEquipmentSlot.Hands] = 4,
            [RetainerEquipmentSlot.Legs] = 6,
            [RetainerEquipmentSlot.Feet] = 7,
            [RetainerEquipmentSlot.Ears] = 8,
            [RetainerEquipmentSlot.Neck] = 9,
            [RetainerEquipmentSlot.Wrists] = 10,
            [RetainerEquipmentSlot.RingLeft] = 11,
            [RetainerEquipmentSlot.RingRight] = 12,
        };
}
