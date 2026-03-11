using System;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using static FFXIVClientStructs.FFXIV.Client.UI.RaptureAtkUnitManager;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using ECommons.UIHelpers.AddonMasterImplementations;
using ECommons.Automation;

namespace VERMAXION.Services;

public static class GameHelpers
{
    /// <summary>
    /// Interact with a targeted game object via TargetSystem.
    /// Pattern from LootGoblin GameHelpers.
    /// </summary>
    public static unsafe bool InteractWithObject(IGameObject obj)
    {
        try
        {
            if (obj == null) return false;

            var ts = TargetSystem.Instance();
            if (ts == null)
            {
                Plugin.Log.Error("[INTERACT] TargetSystem is null");
                return false;
            }

            var gameObjPtr = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)obj.Address;
            if (gameObjPtr == null)
            {
                Plugin.Log.Error($"[INTERACT] GameObject pointer is null for {obj.Name.TextValue}");
                return false;
            }

            ts->InteractWithObject(gameObjPtr, true);
            Plugin.Log.Information($"[INTERACT] Success: {obj.Name.TextValue}");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[INTERACT] Exception: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Find an NPC/EventObj by name in the object table.
    /// </summary>
    public static IGameObject? FindObjectByName(string name)
    {
        foreach (var obj in Plugin.ObjectTable)
        {
            if (obj.Name.TextValue.Equals(name, StringComparison.OrdinalIgnoreCase))
                return obj;
        }
        return null;
    }

    /// <summary>
    /// Target an object by name, then interact with it.
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
        return InteractWithObject(obj);
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
    public static unsafe void FireAddonCallback(string addonName, bool updateState, params object[] args)
    {
        try
        {
            var addon = RaptureAtkUnitManager.Instance()->GetAddonByName(addonName);
            if (addon == null || !addon->IsVisible)
            {
                Plugin.Log.Warning($"[Callback] Addon '{addonName}' not found or not visible");
                return;
            }

            var atkValues = new AtkValue[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                atkValues[i] = args[i] switch
                {
                    int intVal => new AtkValue { Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int, Int = intVal },
                    uint uintVal => new AtkValue { Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.UInt, UInt = uintVal },
                    bool boolVal => new AtkValue { Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Bool, Byte = (byte)(boolVal ? 1 : 0) },
                    _ => new AtkValue { Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int, Int = Convert.ToInt32(args[i]) },
                };
            }

            fixed (AtkValue* ptr = atkValues)
            {
                addon->FireCallback((uint)atkValues.Length, ptr, updateState);
            }

            Plugin.Log.Information($"[Callback] Fired on '{addonName}' with {args.Length} args, updateState={updateState}");
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[Callback] Failed for '{addonName}': {ex.Message}");
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

    /// <summary>
    /// Close any open addon by pressing Escape.
    /// </summary>
    public static void CloseCurrentAddon()
    {
        PressKey(0x1B); // VK_ESCAPE = 0x1B
    }

    /// <summary>
    /// Send NUMPAD0 key (confirm/accept).
    /// </summary>
    public static void SendConfirm()
    {
        PressKey(0x60); // VK_NUMPAD0 = 0x60
    }

    /// <summary>
    /// Send NUMPAD+ key (often used to close windows).
    /// </summary>
    public static void SendNumpadPlus()
    {
        PressKey(0x6B); // VK_ADD = 0x6B (NUMPAD+)
    }

    // ─── Targeted Key Input (PostMessage to FFXIV window only) ───────────────
    // CRITICAL: Using PostMessage instead of keybd_event to confine keypresses
    // to the FFXIV game window. keybd_event is GLOBAL and sends to whatever
    // window is focused, which leaks keypresses to other applications.

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;

    /// <summary>
    /// Get the FFXIV game window handle.
    /// </summary>
    private static IntPtr GetGameWindowHandle()
    {
        try
        {
            return Process.GetCurrentProcess().MainWindowHandle;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    /// <summary>
    /// Press a key (down + up) using PostMessage targeted at the FFXIV game window.
    /// This ensures keypresses are confined to the game client and never leak
    /// to other focused windows.
    /// </summary>
    /// <param name="vk">Virtual Key code</param>
    public static void PressKey(byte vk)
    {
        try
        {
            var hwnd = GetGameWindowHandle();
            if (hwnd == IntPtr.Zero)
            {
                Plugin.Log.Error($"[GameHelpers] Cannot find FFXIV window handle for key 0x{vk:X2}");
                return;
            }

            PostMessage(hwnd, WM_KEYDOWN, (IntPtr)vk, IntPtr.Zero);
            PostMessage(hwnd, WM_KEYUP, (IntPtr)vk, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[GameHelpers] Failed to press key 0x{vk:X2}: {ex.Message}");
        }
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
}
