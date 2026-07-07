using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using static FFXIVClientStructs.FFXIV.Client.UI.RaptureAtkUnitManager;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using ECommons.UIHelpers.AddonMasterImplementations;
using ECommons.Automation;
using ECommons.GameHelpers;
using ECommons.Throttlers;
using Lumina.Excel.Sheets;
using VERMAXION.Models;
using AtkValueType = FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType;
using NativeGameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace VERMAXION.Services;

public static class GameHelpers
{
    private const string OceanFishingResultAddonName = "IKDResult";

    /// <summary>
    /// Interact with a targeted game object via TargetSystem.
    /// </summary>
    public static unsafe bool InteractWithObject(IGameObject obj)
    {
        try
        {
            if (obj == null) return false;

            if (Player.IsAnimationLocked)
            {
                Plugin.Log.Debug($"[INTERACT] Player is animation locked, skipping interaction with {obj.Name.TextValue}");
                return false;
            }

            if (Plugin.Condition[ConditionFlag.Occupied] ||
                Plugin.Condition[ConditionFlag.OccupiedInQuestEvent] ||
                Plugin.Condition[ConditionFlag.OccupiedInCutSceneEvent] ||
                Plugin.Condition[ConditionFlag.WatchingCutscene] ||
                Plugin.Condition[ConditionFlag.BetweenAreas] ||
                Plugin.Condition[ConditionFlag.BetweenAreas51])
            {
                Plugin.Log.Debug($"[INTERACT] Player is occupied, skipping interaction with {obj.Name.TextValue}");
                return false;
            }

            if (!obj.IsTargetable)
            {
                Plugin.Log.Debug($"[INTERACT] Target is not targetable: {obj.Name.TextValue}");
                return false;
            }

            var ts = TargetSystem.Instance();
            if (ts == null)
            {
                Plugin.Log.Error("[INTERACT] TargetSystem is null");
                return false;
            }

            // AutoRetainer pattern: Distance validation using GetValidInteractionDistance
            var localPlayer = Plugin.ObjectTable.LocalPlayer;
            if (localPlayer != null)
            {
                var distance = Vector3.Distance(localPlayer.Position, obj.Position);
                var maxDistance = GetValidInteractionDistance(obj);
                if (distance > maxDistance)
                {
                    Plugin.Log.Debug($"[INTERACT] Target too far: {distance:F1}y (max: {maxDistance:F1}y) for {obj.Name.TextValue}");
                    return false;
                }
            }

            var gameObjPtr = obj.Struct();
            if (gameObjPtr == null)
            {
                Plugin.Log.Error($"[INTERACT] GameObject pointer is null for {obj.Name.TextValue}");
                return false;
            }

            var throttleKey = $"InteractWithObject.{obj.GameObjectId:X16}";
            if (!EzThrottler.Throttle(throttleKey, 5000))
            {
                Plugin.Log.Debug($"[INTERACT] Throttled interaction with {obj.Name.TextValue} (5-second cooldown)");
                return false;
            }

            ts->InteractWithObject(gameObjPtr, false);
            Plugin.Log.Information($"[INTERACT] Success: {obj.Name.TextValue}");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[INTERACT] Exception: {ex.Message}");
            return false;
        }
    }

    private static unsafe NativeGameObject* Struct(this IGameObject obj)
        => (NativeGameObject*)obj.Address;

    /// <summary>
    /// Find an NPC/EventObj by name in the object table.
    /// Uses AutoRetainer's proven targeting pattern: ObjectKind filtering first, then name matching.
    /// Excludes all player characters to avoid targeting other players.
    /// </summary>
    public static IGameObject? FindObjectByName(string name)
    {
        foreach (var obj in Plugin.ObjectTable)
        {
            // AutoRetainer pattern: Filter by ObjectKind FIRST to avoid players entirely
            if (obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventNpc ||
                obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.BattleNpc ||
                obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventObj ||
                obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Aetheryte)
            {
                // Then check name matching (case-insensitive like AutoRetainer)
                if (obj.Name.TextValue.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return obj;
            }
        }
        return null;
    }

    public static IGameObject? FindObjectByDataId(uint dataId)
    {
        foreach (var obj in Plugin.ObjectTable)
        {
            if (obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventNpc ||
                obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.BattleNpc ||
                obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventObj ||
                obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Aetheryte)
            {
                if (obj.BaseId == dataId)
                    return obj;
            }
        }

        return null;
    }

    /// <summary>
    /// Target an object by name, then interact with it.
    /// Uses AutoRetainer's proven targeting pattern: direct TargetManager targeting.
    /// Returns true if interaction was initiated.
    /// </summary>
    public static bool TargetAndInteract(string objectName)
    {
        var obj = FindObjectByName(objectName);
        if (obj == null)
        {
            Plugin.Log.Warning($"[INTERACT] Object '{objectName}' not found");
            return false;
        }

        try
        {
            // AutoRetainer pattern: Use TargetManager directly instead of chat targeting.
            Plugin.TargetManager.Target = obj;
            Plugin.Log.Information($"[INTERACT] Set target to {objectName}");

            // AutoRetainer pattern: Use frame-based timing instead of fixed delay
            // Give the game one frame to process the target change
            Plugin.Framework.RunOnFrameworkThread(() => { });
            System.Threading.Tasks.Task.Delay(50).Wait();
            
            return InteractWithObject(obj);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[INTERACT] Failed to interact with '{objectName}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Check if a UI addon is currently visible.
    /// Pattern from LootGoblin GameHelpers.
    /// </summary>
    public static unsafe bool IsAddonVisible(string addonName)
    {
        try
        {
            var addon = RaptureAtkUnitManager.Instance()->GetAddonByName(addonName);
            return addon != null && addon->IsVisible;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Fire a callback on a named addon with variable arguments.
    /// Pattern from LootGoblin GameHelpers.
    /// SND equivalent: /callback AddonName true/false arg1 arg2 ...
    /// </summary>
    public static void FireAddonCallback(string addonName, bool updateState, params object[] args)
    {
        TryFireAddonCallback(addonName, updateState, args);
    }

    public static unsafe bool TryFireAddonCallback(string addonName, bool updateState, params object[] args)
    {
        var formattedArgs = FormatCallbackArgs(updateState, args);
        try
        {
            var addon = RaptureAtkUnitManager.Instance()->GetAddonByName(addonName);
            if (addon == null || !addon->IsVisible)
            {
                Plugin.Log.Warning($"[Callback] Addon '{addonName}' not found or not visible. Args: {formattedArgs}");
                return false;
            }

            var atkValues = new AtkValue[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                atkValues[i] = args[i] switch
                {
                    int intVal => new AtkValue { Type = AtkValueType.Int, Int = intVal },
                    uint uintVal => new AtkValue { Type = AtkValueType.UInt, UInt = uintVal },
                    bool boolVal => new AtkValue { Type = AtkValueType.Bool, Byte = (byte)(boolVal ? 1 : 0) },
                    _ => new AtkValue { Type = AtkValueType.Int, Int = Convert.ToInt32(args[i]) },
                };
            }

            fixed (AtkValue* ptr = atkValues)
            {
                addon->FireCallback((uint)atkValues.Length, ptr, updateState);
            }

            Plugin.Log.Information($"[Callback] Fired on '{addonName}'. Args: {formattedArgs}");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[Callback] Failed for '{addonName}'. Args: {formattedArgs}. Error: {ex.Message}");
            return false;
        }
    }

    public static unsafe bool IsAddonReady(string addonName)
    {
        try
        {
            return ECommons.GenericHelpers.TryGetAddonByName<AtkUnitBase>(addonName, out var addon) &&
                   ECommons.GenericHelpers.IsAddonReady(addon);
        }
        catch
        {
            return false;
        }
    }

    public static unsafe bool TryFireReadyAddonCallback(string addonName, bool updateState, params object[] args)
    {
        var formattedArgs = FormatCallbackArgs(updateState, args);
        try
        {
            if (!ECommons.GenericHelpers.TryGetAddonByName<AtkUnitBase>(addonName, out var addon) ||
                !ECommons.GenericHelpers.IsAddonReady(addon))
            {
                Plugin.Log.Debug($"[Callback] Addon '{addonName}' not ready. Args: {formattedArgs}");
                return false;
            }

            Callback.Fire(addon, updateState, args);
            Plugin.Log.Information($"[Callback] Fired on ready addon '{addonName}'. Args: {formattedArgs}");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[Callback] Failed for ready addon '{addonName}'. Args: {formattedArgs}. Error: {ex.Message}");
            return false;
        }
    }

    internal static unsafe OceanFishingResultAddonSnapshot GetIKDResultAddonSnapshot()
    {
        try
        {
            if (!ECommons.GenericHelpers.TryGetAddonByName<AtkUnitBase>(OceanFishingResultAddonName, out var addon) ||
                addon == null)
            {
                return new OceanFishingResultAddonSnapshot(
                    Found: false,
                    Visible: false,
                    Ready: false,
                    Detail: "ECommons ready lookup: not found");
            }

            return BuildIKDResultSnapshot(addon);
        }
        catch (Exception ex)
        {
            return new OceanFishingResultAddonSnapshot(
                Found: false,
                Visible: false,
                Ready: false,
                Detail: $"ECommons ready lookup failed: {ex.Message}");
        }
    }

    internal static unsafe bool TryCloseReadyIKDResult(
        out OceanFishingResultAddonSnapshot snapshot,
        out string error)
    {
        error = string.Empty;
        snapshot = OceanFishingResultAddonSnapshot.NotPolled;
        try
        {
            if (!ECommons.GenericHelpers.TryGetAddonByName<AtkUnitBase>(OceanFishingResultAddonName, out var addon) ||
                addon == null)
            {
                snapshot = new OceanFishingResultAddonSnapshot(
                    Found: false,
                    Visible: false,
                    Ready: false,
                    Detail: "ECommons ready lookup: not found");
                return false;
            }

            snapshot = BuildIKDResultSnapshot(addon);
            if (!snapshot.Ready)
                return false;

            Callback.Fire(addon, true, 0);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            if (string.Equals(snapshot.Detail, OceanFishingResultAddonSnapshot.NotPolled.Detail, StringComparison.Ordinal))
                snapshot = GetIKDResultAddonSnapshot();
            return false;
        }
    }

    private static unsafe OceanFishingResultAddonSnapshot BuildIKDResultSnapshot(AtkUnitBase* addon)
    {
        var visible = TryReadAddonVisible(addon, out var visibleError);
        var ready = TryReadAddonReady(addon, out var readyError);
        var errors = JoinAddonReadErrors(visibleError, readyError);
        var detail = $"ECommons ready lookup: found, visible={visible}, ready={ready}";
        if (!string.IsNullOrWhiteSpace(errors))
            detail = $"{detail}, error={errors}";

        return new OceanFishingResultAddonSnapshot(
            Found: true,
            Visible: visible,
            Ready: ready,
            Detail: detail);
    }

    private static unsafe bool TryReadAddonVisible(AtkUnitBase* addon, out string error)
    {
        error = string.Empty;
        try
        {
            return addon != null && addon->IsVisible;
        }
        catch (Exception ex)
        {
            error = $"visible={ex.Message}";
            return false;
        }
    }

    private static unsafe bool TryReadAddonReady(AtkUnitBase* addon, out string error)
    {
        error = string.Empty;
        try
        {
            return addon != null && ECommons.GenericHelpers.IsAddonReady(addon);
        }
        catch (Exception ex)
        {
            error = $"ready={ex.Message}";
            return false;
        }
    }

    private static string JoinAddonReadErrors(params string[] errors)
        => string.Join("; ", errors.Where(error => !string.IsNullOrWhiteSpace(error)));

    public static bool TargetAndInteractByDataId(uint dataId, string fallbackName)
    {
        var obj = FindObjectByDataId(dataId) ?? FindObjectByName(fallbackName);
        if (obj == null)
        {
            Plugin.Log.Warning($"[INTERACT] Object dataId={dataId} fallback='{fallbackName}' not found");
            return false;
        }

        try
        {
            Plugin.TargetManager.Target = obj;
            Plugin.Log.Information($"[INTERACT] Set target to {obj.Name.TextValue} (dataId={dataId})");
            Plugin.Framework.RunOnFrameworkThread(() => { });
            System.Threading.Tasks.Task.Delay(50).Wait();

            return InteractWithObject(obj);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[INTERACT] Failed to interact with dataId={dataId}, fallback='{fallbackName}': {ex.Message}");
            return false;
        }
    }

    private static string FormatCallbackArgs(bool updateState, IReadOnlyList<object> args)
        => $"{updateState.ToString().ToLowerInvariant()}{(args.Count == 0 ? string.Empty : " " + string.Join(" ", args.Select(FormatCallbackArg)))}";

    private static string FormatCallbackArg(object arg)
        => arg switch
        {
            bool value => value.ToString().ToLowerInvariant(),
            null => "null",
            _ => Convert.ToString(arg, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
        };

    /// <summary>
    /// Fire the standard close callback (-1) for an addon if it is currently visible.
    /// Mirrors the pattern already used for result windows elsewhere in VERMAXION.
    /// </summary>
    public static bool TryCloseAddonByCallback(string addonName)
    {
        if (!IsAddonVisible(addonName))
            return false;

        FireAddonCallback(addonName, true, -1);
        return true;
    }

    public static unsafe bool TrySelectStringExact(string expectedEntry, out string visibleEntries)
    {
        visibleEntries = string.Empty;
        try
        {
            nint addonPtr = Plugin.GameGui.GetAddonByName("SelectString", 1);
            if (addonPtr == 0 || !((AtkUnitBase*)addonPtr)->IsVisible)
                return false;

            var master = new AddonMaster.SelectString(addonPtr);
            var entries = new List<string>();
            var expected = NormalizeAddonText(expectedEntry).Trim();
            for (var i = 0; i < master.EntryCount; i++)
            {
                var text = NormalizeAddonText(master.Entries[i].Text).Trim();
                entries.Add($"{i}:{text}");
                if (!string.Equals(text, expected, StringComparison.OrdinalIgnoreCase))
                    continue;

                visibleEntries = string.Join(", ", entries);
                FireAddonCallback("SelectString", true, i);
                Plugin.Log.Information($"[SelectString] Selected exact entry {i}: '{text}'");
                return true;
            }

            visibleEntries = string.Join(", ", entries);
            return false;
        }
        catch (Exception ex)
        {
            visibleEntries = $"read failed: {ex.Message}";
            Plugin.Log.Warning($"[SelectString] Exact selection failed: {ex.Message}");
            return false;
        }
    }

    public static bool TrySelectFirstStringEntry()
    {
        if (!IsAddonVisible("SelectString"))
            return false;

        return TryFireAddonCallback("SelectString", true, 0);
    }

    public static bool TryCommenceDuty()
    {
        if (!IsAddonVisible("ContentsFinderConfirm"))
            return false;

        return TryFireAddonCallback("ContentsFinderConfirm", true, 8);
    }

    public static unsafe bool TrySetLocalPlayerRotation(float rotation)
    {
        try
        {
            var player = Plugin.ObjectTable.LocalPlayer;
            if (player == null || player.Address == nint.Zero)
                return false;

            ((NativeGameObject*)player.Address)->SetRotation(rotation);
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning($"[Fishing] Failed to set player rotation: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Click Yes on any visible SelectYesno dialog.
    /// Pattern from LootGoblin GameHelpers.
    /// </summary>
    public static unsafe bool ClickYesIfVisible()
    {
        try
        {
            nint addonPtr = Plugin.GameGui.GetAddonByName("SelectYesno", 1);
            if (addonPtr == 0) return false;

            var addon = (AddonSelectYesno*)addonPtr;
            if (!addon->AtkUnitBase.IsVisible) return false;

            new AddonMaster.SelectYesno(&addon->AtkUnitBase).Yes();
            Plugin.Log.Information("[YES/NO] Clicked Yes");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[YES/NO] Failed: {ex.Message}");
            return false;
        }
    }

    public static unsafe bool TryClickYesIfPromptContains(
        IReadOnlyCollection<string> expectedFragments,
        string reason,
        bool allowUnreadable,
        out string promptText)
    {
        promptText = string.Empty;

        try
        {
            nint addonPtr = Plugin.GameGui.GetAddonByName("SelectYesno", 1);
            if (addonPtr == 0)
                return false;

            var addon = (AddonSelectYesno*)addonPtr;
            if (!addon->AtkUnitBase.IsVisible)
                return false;

            var yesNo = new AddonMaster.SelectYesno(&addon->AtkUnitBase);
            promptText = NormalizeAddonText(yesNo.Text);
            if (!string.IsNullOrWhiteSpace(promptText))
            {
                var readablePromptText = promptText;
                if (!expectedFragments.Any(fragment => readablePromptText.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
                {
                    Plugin.Log.Debug($"[YES/NO] SelectYesno prompt did not match {reason}: '{promptText}'");
                    return false;
                }

                yesNo.Yes();
                Plugin.Log.Information($"[YES/NO] Clicked guarded Yes for {reason}: '{promptText}'");
                return true;
            }

            if (!allowUnreadable)
                return false;

            yesNo.Yes();
            Plugin.Log.Warning($"[YES/NO] Clicked guarded Yes for {reason} after unreadable prompt text");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[YES/NO] Guarded Yes failed for {reason}: {ex.Message}");
            return false;
        }
    }

    public static unsafe bool TryClickYesIfPromptAllowed(
        Func<string, bool> isAllowed,
        string reason,
        bool allowUnreadable,
        out string promptText,
        string expectedDescription = "")
    {
        promptText = string.Empty;

        try
        {
            nint addonPtr = Plugin.GameGui.GetAddonByName("SelectYesno", 1);
            if (addonPtr == 0)
                return false;

            var addon = (AddonSelectYesno*)addonPtr;
            if (!addon->AtkUnitBase.IsVisible)
                return false;

            var yesNo = new AddonMaster.SelectYesno(&addon->AtkUnitBase);
            promptText = NormalizeAddonText(yesNo.Text);
            if (!string.IsNullOrWhiteSpace(promptText))
            {
                if (!isAllowed(promptText))
                {
                    var expectedSuffix = string.IsNullOrWhiteSpace(expectedDescription)
                        ? string.Empty
                        : $" Expected {expectedDescription}";
                    Plugin.Log.Debug($"[YES/NO] SelectYesno prompt rejected for {reason}: '{promptText}'.{expectedSuffix}");
                    return false;
                }

                yesNo.Yes();
                Plugin.Log.Information($"[YES/NO] Clicked policy-guarded Yes for {reason}: '{promptText}'");
                return true;
            }

            if (!allowUnreadable)
                return false;

            yesNo.Yes();
            Plugin.Log.Warning($"[YES/NO] Clicked policy-guarded Yes for {reason} after unreadable prompt text");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[YES/NO] Policy-guarded Yes failed for {reason}: {ex.Message}");
            return false;
        }
    }

    private static string NormalizeAddonText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (char.IsControl(c))
            {
                if (builder.Length > 0 && builder[^1] != ' ')
                    builder.Append(' ');
                continue;
            }

            builder.Append(c);
        }

        return builder.ToString().Trim();
    }

    /// <summary>
    /// Check if the player is available (not casting, not occupied, not in combat).
    /// </summary>
    public static bool IsPlayerAvailable()
    {
        var player = Plugin.ObjectTable.LocalPlayer;
        if (player == null) return false;
        if (player.IsCasting) return false;
        if (Plugin.Condition[ConditionFlag.InCombat]) return false;
        if (Plugin.Condition[ConditionFlag.Casting]) return false;
        if (Plugin.Condition[ConditionFlag.OccupiedInQuestEvent]) return false;
        if (Plugin.Condition[ConditionFlag.OccupiedInCutSceneEvent]) return false;
        if (Plugin.Condition[ConditionFlag.BetweenAreas]) return false;
        if (Plugin.Condition[ConditionFlag.BetweenAreas51]) return false;
        return true;
    }

    /// <summary>
    /// Get the remaining time of a status effect by ID on the local player.
    /// Returns 0 if not found.
    /// SND equivalent: GetStatusTimeRemaining(statusId)
    /// </summary>
    public static unsafe float GetStatusTimeRemaining(uint statusId)
    {
        try
        {
            var player = Plugin.ObjectTable.LocalPlayer;
            if (player == null) return 0f;

            foreach (var status in player.StatusList)
            {
                if (status.StatusId == statusId)
                    return status.RemainingTime;
            }
            return 0f;
        }
        catch
        {
            return 0f;
        }
    }

    /// <summary>
    /// Use an item from inventory by item ID.
    /// Mirrors FrenRider's approach: uses extraParam 65535 and checks for casting/occupied state.
    /// </summary>
    public static unsafe bool UseItem(uint itemId)
    {
        try
        {
            var player = Plugin.ObjectTable.LocalPlayer;
            if (player == null)
            {
                Plugin.Log.Warning($"UseItem({itemId}): LocalPlayer is null");
                return false;
            }

            // Check if player is casting
            if (player.IsCasting)
            {
                Plugin.Log.Debug($"UseItem({itemId}): Player is casting, skipping");
                return false;
            }

            // Check if player is occupied (in cutscene, etc)
            if (Plugin.Condition[ConditionFlag.OccupiedInQuestEvent] ||
                Plugin.Condition[ConditionFlag.OccupiedInCutSceneEvent] ||
                Plugin.Condition[ConditionFlag.Occupied33] ||
                Plugin.Condition[ConditionFlag.Occupied39])
            {
                Plugin.Log.Debug($"UseItem({itemId}): Player is occupied, skipping");
                return false;
            }

            var am = ActionManager.Instance();
            if (am == null)
            {
                Plugin.Log.Warning($"UseItem({itemId}): ActionManager is null");
                return false;
            }

            // Check if the action is ready
            var status = am->GetActionStatus(ActionType.Item, itemId);
            if (status != 0)
            {
                Plugin.Log.Debug($"UseItem({itemId}): ActionStatus={status}, not ready");
                return false;
            }

            // Use item with extraParam 65535 (required for item usage)
            var result = am->UseAction(ActionType.Item, itemId, extraParam: 65535);
            Plugin.Log.Information($"UseItem({itemId}): UseAction result={result}");
            return result;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"UseItem({itemId}) failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Get item name from game data.
    /// </summary>
    public static string GetItemName(uint itemId)
    {
        try
        {
            var itemSheet = Plugin.DataManager.GetExcelSheet<Item>();
            if (itemSheet == null) return $"Unknown Item {itemId}";

            if (!itemSheet.TryGetRow(itemId, out var item)) return $"Unknown Item {itemId}";
            return item.Name.ToString() ?? $"Unknown Item {itemId}";
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"GetItemName({itemId}) failed: {ex.Message}");
            return $"Unknown Item {itemId}";
        }
    }

    /// <summary>
    /// Check if player is alive.
    /// </summary>
    public static bool IsPlayerAlive()
    {
        try
        {
            var player = Plugin.ObjectTable.LocalPlayer;
            return player != null && player.CurrentHp > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Get the count of an item in the player's inventory (NQ + HQ).
    /// </summary>
    public static unsafe uint GetInventoryItemCount(uint itemId)
    {
        try
        {
            var im = InventoryManager.Instance();
            if (im == null) return 0;
            return (uint)(im->GetInventoryItemCount(itemId) + im->GetInventoryItemCount(itemId, true));
        }
        catch
        {
            return 0;
        }
    }

    public static unsafe bool TryGetLowestEquippedGearConditionPercent(out int lowestConditionPercent)
    {
        lowestConditionPercent = 100;
        var foundEquippedItem = false;

        try
        {
            var manager = InventoryManager.Instance();
            if (manager == null)
                return false;

            var equippedContainer = manager->GetInventoryContainer(InventoryType.EquippedItems);
            if (equippedContainer == null || !equippedContainer->IsLoaded)
                return false;

            for (var i = 0; i < equippedContainer->Size; i++)
            {
                var slot = equippedContainer->GetInventorySlot(i);
                if (slot == null || slot->ItemId == 0)
                    continue;

                foundEquippedItem = true;
                lowestConditionPercent = Math.Min(lowestConditionPercent, slot->Condition / 300);
            }

            return foundEquippedItem;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning($"[GameHelpers] Failed to read equipped durability: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Close any open addon by pressing Escape.
    /// </summary>
    public static void CloseCurrentAddon()
    {
        PressKey(VirtualKey.ESCAPE);
    }

    /// <summary>
    /// Safer general-purpose UI cleanup for task transitions.
    /// ESC closes the remaining top-level addon without advancing dialogue or toggling camera state.
    /// </summary>
    public static void ResetInteractionState()
    {
        CloseCurrentAddon();
    }

    /// <summary>
    /// Send NUMPAD0 key (confirm/accept).
    /// </summary>
    public static void SendConfirm()
    {
        PressKey(VirtualKey.NUMPAD0);
    }

    /// <summary>
    /// Send NUMPAD+ key (often used to close windows).
    /// </summary>
    public static void SendNumpadPlus()
    {
        PressKey(VirtualKey.ADD);
    }

    /// <summary>
    /// Send END key.
    /// </summary>
    public static void SendEnd()
    {
        PressKey(VirtualKey.END);
    }

    // ─── Keyboard Input (ECommons WindowsKeypress - same pattern as LootGoblin) ─────
    // Uses ECommons.Automation.WindowsKeypress which sends PostMessage with proper
    // scan codes to the game window handle. This confines keypresses to the FFXIV
    // client and does NOT leak to other windows.
    // Previous keybd_event approach was GLOBAL and leaked to any focused window.

    /// <summary>
    /// Press and release a key using ECommons WindowsKeypress.
    /// Sends PostMessage to the FFXIV game window with proper scan codes.
    /// </summary>
    public static void PressKey(VirtualKey key)
    {
        try
        {
            WindowsKeypress.SendKeypress(key, null);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[GameHelpers] Failed to press key {key}: {ex.Message}");
        }
    }

    /// <summary>
    /// Press and hold a key down using ECommons WindowsKeypress.
    /// </summary>
    public static void KeyDown(VirtualKey key)
    {
        try
        {
            WindowsKeypress.SendKeyHold(key, null);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[GameHelpers] Failed to press key down {key}: {ex.Message}");
        }
    }

    /// <summary>
    /// Release a key using ECommons WindowsKeypress.
    /// </summary>
    public static void KeyUp(VirtualKey key)
    {
        try
        {
            WindowsKeypress.SendKeyRelease(key, null);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[GameHelpers] Failed to release key {key}: {ex.Message}");
        }
    }

    /// <summary>
    /// Press a key by VK byte code (legacy compatibility).
    /// </summary>
    public static void PressKey(byte vk)
    {
        PressKey((VirtualKey)vk);
    }

    /// <summary>
    /// Get FC points from the FC window.
    /// Reads from node #17 as per XA docs.
    /// </summary>
    public static unsafe int? GetFCPointsNode()
    {
        try
        {
            var addon = Instance()->GetAddonByName("FreeCompany");
            if (addon == null) return null;
            
            // Navigate to node #17 where FC points are stored (per XA docs)
            var node = addon->GetNodeById(17u);
            if (node == null || node->Type != NodeType.Text) return null;
            
            var textNode = (AtkTextNode*)node;
            var text = textNode->NodeText.ToString();
            
            // Remove commas and parse (same as FUTA_GC.lua)
            var cleanText = text.Replace(",", "");
            if (int.TryParse(cleanText, out var points))
            {
                Plugin.Log.Information($"[GameHelpers] FC points from UI node #17: {points:N0}");
                return points;
            }
            
            Plugin.Log.Warning($"[GameHelpers] Failed to parse FC points from node #17: '{text}'");
            return null;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[GameHelpers] Error reading FC points: {ex.Message}");
            return null;
        }
    }

    public static unsafe bool TryGetAddonText(string addonName, uint nodeId, out string text)
    {
        text = string.Empty;

        try
        {
            var addon = Instance()->GetAddonByName(addonName);
            if (addon == null || !addon->IsVisible)
                return false;

            var node = addon->GetNodeById(nodeId);
            if (node == null || node->Type != NodeType.Text)
                return false;

            var textNode = (AtkTextNode*)node;
            text = textNode->NodeText.ToString();
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning($"[GameHelpers] Failed to read {addonName} node {nodeId}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// AutoRetainer pattern: Get valid interaction distance for different object types.
    /// Based on FFXIV standard interaction distances in yalms.
    /// </summary>
    public static float GetValidInteractionDistance(IGameObject obj)
    {
        if (obj == null) return 2.0f; // Default safe distance
        
        // AutoRetainer distance logic based on ObjectKind
        return obj.ObjectKind switch
        {
            Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventNpc => 4.0f,  // NPCs like summoning bells, vendors
            Dalamud.Game.ClientState.Objects.Enums.ObjectKind.BattleNpc => 3.0f,  // Battle NPCs (enemies, retainers)
            Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventObj => 2.0f,   // Event objects (chests, aetherytes)
            _ => 2.0f // Default distance for unknown types
        };
    }

    /// <summary>
    /// Send jump command to help with pathing when stuck.
    /// Uses /gaction jump for vertical movement assistance.
    /// </summary>
    public static void SendJump()
    {
        try
        {
            CommandHelper.SendCommand("/gaction jump");
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[GameHelpers] Failed to send jump: {ex.Message}");
        }
    }
}
