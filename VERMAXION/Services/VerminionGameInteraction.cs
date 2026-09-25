using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.System.Input;
using FFXIVClientStructs.FFXIV.Client.UI;
using NativeFramework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.Enums;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace VERMAXION.Services;

// Interaction research stays on the framework thread and uses the client's declared
// structures. Captures are bounded, at explicit transitions, and use the existing log.
internal static unsafe class VerminionGameInteraction
{
    private static string? restoreCaptureGameScale;
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, ImageSize;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ColorsUsed, ColorsImportant;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint obj);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage, out nint bits, nint section, uint offset);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern bool BitBlt(nint target, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint operation);

    public static void PrepareBattlefieldRendering()
    {
        if (CurrentCpuDutyId() == 0) return;
        var resolutionConfig = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Plugin.PluginInterface.GetPluginConfigDirectory())!, "CustomResolution2782.json");
        if (Plugin.CommandManager.Commands.ContainsKey("/gres") && System.IO.File.Exists(resolutionConfig))
        {
            var game = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(resolutionConfig))["V1"]?["Game"];
            if (game?["IsEnabled"]?.Value<bool>() == true && game["IsScale"]?.Value<bool>() == true &&
                game["Scale"]?.Value<float>() is { } scale && scale < 1)
            {
                restoreCaptureGameScale = scale.ToString(System.Globalization.CultureInfo.InvariantCulture);
                Plugin.CommandManager.ProcessCommand("/gres 1");
            }
        }
    }

    public static void RequestTutorialImage()
    {
        if (CurrentCpuDutyId() == 0) return;
        PrepareBattlefieldRendering();
        HideBattlefieldPanels();
    }

    public static bool CaptureTutorialImage()
    {
        var window = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
        if (CurrentCpuDutyId() == 0 || window == 0 || PInvoke.User32.IsIconic(window) ||
            PInvoke.User32.GetForegroundWindow() != window ||
            !PInvoke.User32.GetClientRect(window, out var bounds)) return false;
        var origin = new PInvoke.POINT();
        if (!ClientToScreen(window, ref origin)) return false;
        var width = bounds.right;
        var height = bounds.bottom;
        if (width <= 0 || height <= 0 || (long)width * height > 16000000) return false;
        var info = new BitmapInfo { Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32 };
        var screenDc = GetDC(0);
        if (screenDc == 0) return false;
        var memoryDc = CreateCompatibleDC(screenDc);
        nint bitmap = 0, previous = 0;
        try
        {
            if (memoryDc == 0) return false;
            bitmap = CreateDIBSection(screenDc, ref info, 0, out var pixels, 0, 0);
            if (bitmap == 0 || pixels == 0) return false;
            previous = SelectObject(memoryDc, bitmap);
            if (!BitBlt(memoryDc, 0, 0, width, height, screenDc, origin.x, origin.y, 0x40CC0020)) return false;
            var data = new byte[width * height * 4];
            System.Runtime.InteropServices.Marshal.Copy(pixels, data, 0, data.Length);
            var path = System.IO.Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(), "verminion-control.bmp");
            using var output = new System.IO.BinaryWriter(System.IO.File.Create(path));
            output.Write((ushort)0x4D42); output.Write(54 + data.Length); output.Write(0); output.Write(54);
            output.Write(40); output.Write(width); output.Write(-height); output.Write((ushort)1); output.Write((ushort)32);
            output.Write(0); output.Write(data.Length); output.Write(0); output.Write(0); output.Write(0); output.Write(0);
            output.Write(data);
            Plugin.Log.Information($"[VerminionControl] bounded game-window image ready; size={width}x{height}; file=verminion-control.bmp");
            return true;
        }
        finally
        {
            if (previous != 0) SelectObject(memoryDc, previous);
            if (bitmap != 0) DeleteObject(bitmap);
            if (memoryDc != 0) DeleteDC(memoryDc);
            ReleaseDC(0, screenDc);
        }
    }

    public static bool IsTutorialBattle()
        => CurrentCpuDutyId() == 552;

    public static uint CurrentCpuDutyId()
    {
        var finder = ContentsFinder.Instance();
        return Plugin.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.PlayingLordOfVerminion] &&
            finder != null && finder->QueueInfo.PoppedQueueEntry.ContentType == ContentsType.Regular &&
            finder->QueueInfo.PoppedQueueEntry.Id is >= 552 and <= 579
            ? finder->QueueInfo.PoppedQueueEntry.Id : 0;
    }

    public static int CompletedStages() => PlayerState.Instance() is var player && player != null ? player->CompletedLoVMStages : -1;

    public static string ChallengeName(int stage) => stage is >= 1 and <= 24
        ? Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.ContentFinderCondition>().GetRow((uint)(551 + stage)).Name.ToString()
        : string.Empty;

    public static bool HasBattleResult()
    {
        var result = Plugin.GameGui.GetAddonByName("LovmResult");
        return !result.IsNull && result.IsReady && result.IsVisible;
    }

    public static VERMAXION.Models.VerminionBattleOutcome ReadBattleOutcome()
    {
        if (!HasBattleResult() || !GameHelpers.TryGetAddonText("LovmResult", 3, out var result))
            return VERMAXION.Models.VerminionBattleOutcome.Unknown;
        return result switch
        {
            "You Lose" => VERMAXION.Models.VerminionBattleOutcome.Defeat,
            "You Win" => VERMAXION.Models.VerminionBattleOutcome.Victory,
            _ => VERMAXION.Models.VerminionBattleOutcome.Unknown,
        };
    }

    public static bool TryLeaveBattleResult() => HasBattleResult() &&
        GameHelpers.TryClickNativeButton("LovmResult", "Quit", 48);

    public static bool OpenChallengeLog()
    {
        var module = AgentModule.Instance();
        var agent = module == null ? null : module->GetAgentByInternalId(AgentId.ContentsNote);
        if (agent == null) return false;
        agent->Show();
        return true;
    }

    public static void CloseChallengeLog()
    {
        var handle = Plugin.GameGui.GetAddonByName("ContentsNote");
        if (!handle.IsNull && handle.IsReady) ((AtkUnitBase*)handle.Address)->Close(true);
    }

    public static void CaptureChallengeLog()
    {
        CaptureAddon("ContentsNote");
        var note = ContentsNote.Instance();
        if (note == null) return;
        Plugin.Log.Information($"[VerminionControl] challengeLog state={note->State}; tab={note->SelectedTab}; count={note->DisplayCount}; participationFlags={note->IsContentNoteComplete(66)},{note->IsContentNoteComplete(47)},{note->IsContentNoteComplete(67)}");
        for (var i = 0; i < Math.Min((int)note->DisplayCount, note->DisplayIds.Length); ++i)
            Plugin.Log.Information($"[VerminionControl] challengeLog row={note->DisplayIds[i]}; status={note->DisplayStatuses[i]}");
    }

    public static int? ReadWeeklyParticipation()
    {
        var note = ContentsNote.Instance();
        if (note == null || note->State != ContentsNote.ContentsNoteState.Loaded || note->SelectedTab != 10) return null;
        int? Read(int row)
        {
            for (var i = 0; i < Math.Min((int)note->DisplayCount, note->DisplayIds.Length); ++i)
                if (note->DisplayIds[i] == row) return note->DisplayStatuses[i];
            return null;
        }
        var matches = Read(67);
        return matches is >= 0 and <= 5 && Read(66) == Math.Min(1, matches.Value) &&
            Read(47) == Math.Min(3, matches.Value) && note->IsContentNoteComplete(67) == (matches == 5)
            ? matches : null;
    }

    public static bool IsFriendlyMinion(Dalamud.Game.ClientState.Objects.Types.IGameObject obj)
        => IsMinionSide(obj, 0);

    public static bool IsEnemyMinion(Dalamud.Game.ClientState.Objects.Types.IGameObject obj)
        => IsMinionSide(obj, 1);

    private static bool IsMinionSide(Dalamud.Game.ClientState.Objects.Types.IGameObject obj, byte battalion)
    {
        if (obj.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.BattleNpc || obj.Address == 0) return false;
        var character = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)obj.Address;
        return character->SubKind == 7 && character->CharacterData.Battalion == battalion;
    }

    public static bool FindEmptyBattlefieldPoint(System.Numerics.Vector3 near, out System.Numerics.Vector2 point)
    {
        point = default;
        // A group can fill the four cardinal points, especially in a small game
        // window. Search a bounded set of diagonal and wider ground points too.
        foreach (var distance in new[] { 2, 4, 6, 8 })
        foreach (var direction in new[] { new System.Numerics.Vector3(1, 0, 0), new(-1, 0, 0), new(0, 0, 1), new(0, 0, -1),
            new(1, 0, 1), new(-1, 0, 1), new(1, 0, -1), new(-1, 0, -1) })
        {
            var candidate = new System.Numerics.Vector3(near.X, 0, near.Z) + direction * distance;
            if (Plugin.ObjectTable.Any(obj => obj.ObjectKind is Dalamud.Game.ClientState.Objects.Enums.ObjectKind.BattleNpc or
                Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventObj && System.Numerics.Vector3.DistanceSquared(obj.Position, candidate) < 4)) continue;
            if (ProjectBattlefield(candidate, out point) && IsBattlefieldPointVisible(point)) return true;
        }
        return false;
    }

    public static bool IsSummoningGateSelected(int gate)
    {
        var handle = Plugin.GameGui.GetAddonByName("LovmPalette");
        if (gate is < 0 or > 2 || handle.IsNull || !handle.IsReady || !handle.IsVisible) return false;
        var node = ((AtkUnitBase*)handle.Address)->GetNodeById((uint)(73 + gate));
        var component = node == null || (ushort)node->Type < 1000 ? null : node->GetAsAtkComponentNode()->Component;
        return component != null && component->GetComponentType() == ComponentType.RadioButton &&
            ((AtkComponentRadioButton*)component)->IsSelected;
    }

    public static bool IsSummoningGateAvailable(int gate)
    {
        var handle = Plugin.GameGui.GetAddonByName("LovmPalette");
        if (gate is < 0 or > 2 || handle.IsNull || !handle.IsReady || !handle.IsVisible) return false;
        var node = ((AtkUnitBase*)handle.Address)->GetNodeById((uint)(73 + gate));
        if (!IsVisibleThroughParents(node) || (ushort)node->Type < 1000) return false;
        var component = node->GetAsAtkComponentNode()->Component;
        return component != null && component->GetComponentType() == ComponentType.RadioButton &&
            ((AtkComponentButton*)component)->IsEnabled;
    }

    public static bool TryReadSummoningCapacity(out int used, out int capacity)
    {
        used = capacity = 0;
        if (!GameHelpers.TryGetAddonText("LovmPalette", 67, out var text)) return false;
        var values = text.Split('/');
        return values.Length == 2 && int.TryParse(values[0], out used) && int.TryParse(values[1], out capacity) &&
            used >= 0 && capacity is >= 60 and <= 240 && used <= capacity;
    }

    public static int? ReadStoneHp(bool enemy, int lane)
    {
        var stage = AtkStage.Instance();
        var array = stage == null ? null : stage->GetNumberArrayData(NumberArrayType.Lovm);
        if (CurrentCpuDutyId() == 0 || lane is < 0 or > 2 || array == null || array->IntArray == null || array->Size != 31) return null;
        var hp = array->IntArray[(enemy ? 9 : 6) + lane];
        return hp is >= 0 and <= 5000 ? hp : null;
    }

    public static int FindPaletteMinion(ushort minion)
    {
        var palette = GoldSaucerModule.Instance();
        return palette == null ? -1 : Array.IndexOf(palette->HotbarMinions.ToArray(), minion);
    }

    public static int? ReadQueuedSummonCount()
    {
        var stage = AtkStage.Instance();
        var array = stage == null ? null : stage->GetNumberArrayData(NumberArrayType.LovmQueueList);
        if (CurrentCpuDutyId() == 0 || array == null || array->IntArray == null || array->Size != 139) return null;
        // Observed alongside the last queued Haurchefant spawning: 1 -> 0,
        // while the visible queue entry disappears and living units go 11 -> 12.
        var count = array->IntArray[102];
        return count is >= 0 and <= 10 ? count : null;
    }

    public static ulong ReadMinionInfoId()
    {
        var stage = AtkStage.Instance();
        var array = stage == null ? null : stage->GetNumberArrayData(NumberArrayType.Lovm);
        // Index 17 is the entity shown in the native minion information panel.
        // Use it immediately after a click to verify the hit, not as durable
        // selection state while moving the pointer or camera.
        return CurrentCpuDutyId() != 0 && array != null && array->IntArray != null && array->Size == 31
            ? unchecked((uint)array->IntArray[17]) : 0;
    }

    public static bool IsPaletteActionReady(uint nodeId)
    {
        if (CurrentCpuDutyId() == 0 || nodeId is not (81 or 82)) return false;
        var handle = Plugin.GameGui.GetAddonByName("LovmPalette");
        if (handle.IsNull || !handle.IsVisible || !handle.IsReady) return false;
        var node = ((AtkUnitBase*)handle.Address)->GetNodeById(nodeId);
        if (!IsVisibleThroughParents(node) || (ushort)node->Type < 1000) return false;
        var component = node->GetAsAtkComponentNode()->Component;
        return component != null && component->GetComponentType() == ComponentType.Button &&
            ((AtkComponentButton*)component)->IsEnabled;
    }

    public static bool PrepareOwnedMinion(ushort minion)
    {
        var ui = UIState.Instance();
        var palette = GoldSaucerModule.Instance();
        if (CurrentCpuDutyId() != 0 || ui == null || palette == null || !ui->IsCompanionUnlocked(minion)) return false;
        if (FindPaletteMinion(minion) >= 0) return true;
        var slot = Array.IndexOf(palette->HotbarMinions.ToArray(), (ushort)0);
        if (slot < 0) return false;
        palette->SetHotbarMinion(slot, minion);
        return palette->GetHotbarMinion(slot) == minion;
    }

    public static bool TryFindUnregisteredMinion(out uint itemId)
    {
        itemId = 0;
        var inventory = FFXIVClientStructs.FFXIV.Client.Game.InventoryManager.Instance();
        if (!Plugin.PlayerState.IsLoaded || UIState.Instance() == null || inventory == null) return false;
        var items = new HashSet<uint>();
        foreach (var type in new[]
        {
            FFXIVClientStructs.FFXIV.Client.Game.InventoryType.Inventory1,
            FFXIVClientStructs.FFXIV.Client.Game.InventoryType.Inventory2,
            FFXIVClientStructs.FFXIV.Client.Game.InventoryType.Inventory3,
            FFXIVClientStructs.FFXIV.Client.Game.InventoryType.Inventory4,
        })
        {
            var bag = inventory->GetInventoryContainer(type);
            if (bag == null || !bag->IsLoaded) return false;
            for (var i = 0; i < bag->Size; ++i)
            {
                var slot = bag->GetInventorySlot(i);
                if (slot == null) return false;
                if (slot->ItemId != 0 && slot->Quantity > 0) items.Add(slot->ItemId);
            }
        }
        var sheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>();
        foreach (var id in items)
        {
            if (!sheet.TryGetRow(id, out var item)) return false;
            if (item.ItemAction.Value.Action.Value.RowId != 853) continue;
            if (!Plugin.UnlockState.IsItemUnlockable(item)) return false;
            if (Plugin.UnlockState.IsItemUnlocked(item)) continue;
            itemId = id;
            return true;
        }
        return true;
    }

    public static bool IsMinionItemRegistered(uint itemId) => Plugin.PlayerState.IsLoaded &&
        Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>().TryGetRow(itemId, out var item) &&
        item.ItemAction.Value.Action.Value.RowId == 853 && Plugin.UnlockState.IsItemUnlocked(item);

    public static int CurrentLogIndex()
    {
        var chat = RaptureLogModule.Instance();
        return chat == null ? -1 : checked((int)chat->GetCurrentLogIndex());
    }

    public static string ReadTutorialPrompt(int afterIndex = -1)
    {
        if (!IsTutorialBattle()) return string.Empty;
        var chat = RaptureLogModule.Instance();
        if (chat == null) return string.Empty;
        var newest = checked((int)chat->GetCurrentLogIndex());
        for (var index = newest; index >= Math.Max(0, newest - 100) && index > afterIndex; --index)
        {
            if (!chat->GetLogMessageDetail(index, out _, out var bytes, out var kind, out _, out _, out _) ||
                kind != (ushort)Dalamud.Game.Text.XivChatType.SystemMessage) continue;
            var message = Dalamud.Game.Text.SeStringHandling.SeString.Parse(bytes).TextValue;
            if (message.StartsWith('\uE070')) return message[1..].Trim();
        }
        return string.Empty;
    }

    public static bool HasChallengeQueue(int stage)
    {
        if (stage is < 1 or > 24) return false;
        var finder = ContentsFinder.Instance();
        if (finder == null || finder->QueueInfo.QueueState is ContentsFinderQueueState.None or ContentsFinderQueueState.InContent) return false;
        var found = false;
        foreach (var entry in finder->QueueInfo.QueuedEntries)
        {
            if (entry.Id == 0) continue;
            if (entry.ContentType != ContentsType.Regular || entry.Id != 551 + stage) return false;
            found = true;
        }
        var popped = finder->QueueInfo.PoppedQueueEntry;
        return found || popped.ContentType == ContentsType.Regular && popped.Id == 551 + stage;
    }

    public static void CancelChallengeQueue(int stage)
    {
        if (!HasChallengeQueue(stage)) return;
        ContentsFinder.Instance()->QueueInfo.CancelQueue();
        Plugin.Log.Information($"[VerminionControl] withdrawal requested for owned Stage {stage} queue; no result recorded");
    }

    public static bool TryCommenceChallenge(int stage) => HasChallengeQueue(stage) &&
        GameHelpers.TryGetAddonText("ContentsFinderConfirm", 49, out var title) && title == ChallengeName(stage) &&
        GameHelpers.TryClickNativeButton("ContentsFinderConfirm", "Commence", 63);

    public static bool TrySummonPaletteSlot(int slot)
    {
        if (slot is < 0 or >= 23 || !Plugin.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.PlayingLordOfVerminion]) return false;
        var handle = Plugin.GameGui.GetAddonByName("LovmPalette");
        if (handle.IsNull || !handle.IsVisible || !handle.IsReady) return false;
        var addon = (AtkUnitBase*)handle.Address;
        var node = addon->GetNodeById((uint)(34 + slot));
        if (!IsVisibleThroughParents(node) || (ushort)node->Type < 1000) return false;
        var componentNode = node->GetAsAtkComponentNode();
        var component = componentNode->Component;
        if (component == null || component->GetComponentType() != ComponentType.DragDrop) return false;
        var dragDrop = (AtkComponentDragDrop*)component;
        var count = 0;
        for (var evt = node->AtkEventManager.Event; evt != null && count++ < 16; evt = evt->NextEvent)
        {
            if (evt->State.EventType != AtkEventType.DragDropClick || evt->Param != slot || evt->Listener != (AtkEventListener*)addon) continue;
            var click = *evt;
            var data = new AtkEventData();
            data.DragDropData.DragDropInterface = &dragDrop->AtkDragDropInterface;
            data.DragDropData.ComponentNode = componentNode;
            data.DragDropData.MouseButtonId = 0;
            click.Listener->ReceiveEvent(AtkEventType.DragDropClick, checked((int)click.Param), &click, &data);
            Plugin.Log.Information($"[VerminionControl] summon dispatched; slot={slot}; awaiting queue/battle readback");
            return true;
        }
        return false;
    }

    public static bool TryClickPaletteIcon(uint nodeId)
    {
        if (CurrentCpuDutyId() == 0) return false;
        var handle = Plugin.GameGui.GetAddonByName("LovmPalette");
        if (handle.IsNull || !handle.IsVisible || !handle.IsReady) return false;
        var addon = (AtkUnitBase*)handle.Address;
        var node = addon->GetNodeById(nodeId);
        if (!IsVisibleThroughParents(node) || (ushort)node->Type < 1000) return false;
        var component = node->GetAsAtkComponentNode()->Component;
        if (component == null) return false;
        if (component->GetComponentType() is ComponentType.Button or ComponentType.RadioButton)
        {
            var enabled = ((AtkComponentButton*)component)->IsEnabled;
            Plugin.Log.Information($"[VerminionControl] palette control node={nodeId}; enabled={enabled}; flags={node->NodeFlags}");
            if (!enabled) return false;
        }
        var count = 0;
        for (var evt = node->AtkEventManager.Event; evt != null && count++ < 16; evt = evt->NextEvent)
        {
            if (evt->State.EventType != AtkEventType.ButtonClick || evt->Listener != (AtkEventListener*)addon) continue;
            var click = *evt;
            var data = new AtkEventData();
            Plugin.Log.Information($"[VerminionControl] palette control click node={nodeId}; param={click.Param}; component={component->GetComponentType()}; position={node->X},{node->Y}; awaiting readback");
            click.Listener->ReceiveEvent(AtkEventType.ButtonClick, checked((int)click.Param), &click, &data);
            return true;
        }
        return false;
    }

    public static bool ProjectBattlefield(System.Numerics.Vector3 world, out System.Numerics.Vector2 screen)
    {
        screen = default;
        var cameras = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.CameraManager.Instance();
        var camera = cameras == null ? null : cameras->CurrentCamera;
        if (camera == null || camera->RenderCamera == null || CurrentCpuDutyId() == 0) return false;
        var visible = camera->WorldToScreen(world, out var projected);
        screen = new(projected.X, projected.Y);
        Plugin.GameGui.WorldToScreen(world, out var controlProjection);
        var ray = camera->ScreenPointToRay(projected);
        var ground = Math.Abs(ray.Direction.Y) > 0.0001f
            ? ray.Origin + ray.Direction * (-ray.Origin.Y / ray.Direction.Y) : default;
        Plugin.Log.Information($"[VerminionControl] projection world={world}; scene={screen}; control={controlProjection}; visible={visible}; ground={ground}");
        return visible;
    }

    public static bool ProjectObjectCenter(Dalamud.Game.ClientState.Objects.Types.IGameObject obj, out System.Numerics.Vector2 screen)
    {
        screen = default;
        return TryGetObjectCenter(obj, out var center) && ProjectBattlefield(center, out screen);
    }

    public static bool TryGetObjectCenter(Dalamud.Game.ClientState.Objects.Types.IGameObject obj, out System.Numerics.Vector3 world)
    {
        world = default;
        if (CurrentCpuDutyId() == 0 || obj.Address == 0) return false;
        var native = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)obj.Address;
        var center = new FFXIVClientStructs.FFXIV.Common.Math.Vector3();
        native->GetCenterPosition(&center);
        Plugin.Log.Information($"[VerminionControl] object center id={obj.GameObjectId}; name={obj.Name}; center={center}");
        world = new(center.X, center.Y, center.Z);
        return true;
    }

    public static bool IsBattlefieldPointVisible(System.Numerics.Vector2 point)
    {
        var window = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
        return window != 0 && PInvoke.User32.GetClientRect(window, out var bounds) &&
            point.X >= 24 && point.Y >= Math.Min(120, bounds.bottom / 3) &&
            point.X < bounds.right - 24 && point.Y < bounds.bottom - 24;
    }

    public static FFXIVClientStructs.FFXIV.Client.System.Input.InputId? FramingDirection(System.Numerics.Vector2 point)
    {
        var window = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
        if (window == 0 || !PInvoke.User32.GetClientRect(window, out var bounds) ||
            !float.IsFinite(point.X) || !float.IsFinite(point.Y)) return null;
        // Near/behind-camera points have extreme horizontal magnification. Bring
        // them into the vertical range before correcting sideways displacement.
        if (point.Y < Math.Min(120, bounds.bottom / 3)) return FFXIVClientStructs.FFXIV.Client.System.Input.InputId.MOVE_FORE;
        if (point.Y >= bounds.bottom - 24) return FFXIVClientStructs.FFXIV.Client.System.Input.InputId.MOVE_BACK;
        if (point.X < 24) return FFXIVClientStructs.FFXIV.Client.System.Input.InputId.MOVE_LEFT;
        if (point.X >= bounds.right - 24) return FFXIVClientStructs.FFXIV.Client.System.Input.InputId.MOVE_RIGHT;
        return null;
    }

    // The installed ClientStructs cursor fields at 0x14/0x18 are mislabeled.
    // Native MouseDevice.ProcessMouseInputMessage writes button-up to 0x18;
    // MouseDevice.Update writes RepeatCounter output to 0x1C (observed 2026-09-25).
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit, Size = 0x30)]
    private struct NativeCursorButtonEdges
    {
        [System.Runtime.InteropServices.FieldOffset(0x14)] public MouseButtonFlags DoubleClicked;
        [System.Runtime.InteropServices.FieldOffset(0x18)] public MouseButtonFlags Released;
        [System.Runtime.InteropServices.FieldOffset(0x1C)] public MouseButtonFlags Repeated;
    }

    private sealed class BattlefieldClick
    {
        public System.Numerics.Vector2 Point;
        public PInvoke.POINT ScreenPoint;
        public bool Right, DoubleClick, Positioned, Pressed, ReleaseRequested;
        public int Wheel;
        public uint Duty;
        public DateTime Expires;
    }

    private static Hook<MouseDeviceInterface.Delegates.GetData>? battlefieldInputHook;
    private static Hook<NativeFramework.Delegates.Tick>? battlefieldFrameHook;
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private delegate bool ReadCursorPosition(PInvoke.POINT* point);
    private static Hook<ReadCursorPosition>? battlefieldCursorHook;
    private static int frameThreadId, frameCursorQueries;
    private static Func<bool>? backgroundInputAllowed;
    private static BattlefieldClick? battlefieldClick, battlefieldPointer;
    private static bool inBattlefieldFrame, frameInputRelease;
    private static CursorInputData* frameDeviceData;
    private static CursorInputData originalDeviceData;
    private static Cursor* frameCursor;
    private static bool originalCursorOutside, originalWindowInactive;
    private static MouseDeviceInterface* battlefieldMouse;
    private static ulong clickedMinion;
    private static DateTime clickedMinionUtc;
    private static readonly Dictionary<string, nint> hiddenBattlefieldPanels = new();

    public static bool BattlefieldClickPending => battlefieldClick != null;

    public static ulong ReadBattlefieldClickHit() =>
        (DateTime.UtcNow - clickedMinionUtc).TotalSeconds <= 2 ? clickedMinion : 0;

    public static void ConfigureBackgroundInput(Func<bool> allowed) => backgroundInputAllowed = allowed;

    public static void DisposeBackgroundInput()
    {
        ReleaseBattlefieldInput();
        backgroundInputAllowed = null;
        battlefieldInputHook?.Dispose();
        battlefieldInputHook = null;
        battlefieldFrameHook?.Dispose();
        battlefieldFrameHook = null;
        battlefieldCursorHook?.Dispose();
        battlefieldCursorHook = null;
        battlefieldMouse = null;
    }

    private static bool ReadBattlefieldCursor(PInvoke.POINT* point)
    {
        var click = battlefieldClick ?? battlefieldPointer;
        if (point == null || Environment.CurrentManagedThreadId != frameThreadId ||
            click == null || DateTime.UtcNow >= click.Expires || CurrentCpuDutyId() != click.Duty || backgroundInputAllowed?.Invoke() != true)
            return battlefieldCursorHook!.Original(point);
        // Only the game's import sees this virtual position. No OS cursor is moved.
        *point = click.ScreenPoint;
        ++frameCursorQueries;
        return true;
    }

    private static bool HandleBattlefieldFrame(NativeFramework* framework)
    {
        inBattlefieldFrame = true;
        frameThreadId = Environment.CurrentManagedThreadId;
        frameCursorQueries = 0;
        try { return battlefieldFrameHook!.Original(framework); }
        finally
        {
            inBattlefieldFrame = false;
            if (frameDeviceData != null)
            {
                // The device owns this buffer. Never replace its pointer or its
                // bindings, and restore it before the next hardware poll.
                *frameDeviceData = originalDeviceData;
                frameDeviceData = null;
                var outsideDuringSample = frameCursor != null && frameCursor->IsCursorOutsideViewPort;
                if (frameCursor != null) frameCursor->IsCursorOutsideViewPort = originalCursorOutside;
                frameCursor = null;
                var inactiveDuringSample = framework->WindowInactive;
                framework->WindowInactive = originalWindowInactive;
                if (frameInputRelease)
                {
                    // Later hover frames may point at a moving enemy. Preserve
                    // the hit that belonged to this completed click.
                    clickedMinion = ReadMinionInfoId();
                    clickedMinionUtc = DateTime.UtcNow;
                    var input = UIInputData.Instance();
                    var stage = AtkStage.Instance();
                    var collision = stage == null ? null : stage->AtkCollisionManager;
                    Plugin.Log.Information($"[VerminionControl] background device frame; minionInfo={clickedMinion}; inactive={inactiveDuringSample}; restoredInactive={framework->WindowInactive}; foreground={PInvoke.User32.GetForegroundWindow() == System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle}; primary={framework->CursorInputs.PositionX},{framework->CursorInputs.PositionY}/{framework->CursorInputs.MouseButtonHeldFlags}; filtered={(input == null ? "none" : $"{input->UIFilteredCursorInputs.PositionX},{input->UIFilteredCursorInputs.PositionY}/{input->UIFilteredCursorInputs.MouseButtonHeldFlags}")}; outside={outsideDuringSample}; cursorQueries={frameCursorQueries}; collision={(collision == null || collision->IntersectingAddon == null ? "none" : collision->IntersectingAddon->NameString)}");
                }
                if (frameInputRelease)
                {
                    battlefieldPointer = battlefieldClick;
                    if (battlefieldPointer != null) battlefieldPointer.Expires = DateTime.UtcNow.AddSeconds(3);
                    battlefieldClick = null;
                }
            }
        }
    }

    private static CursorInputData* HandleBattlefieldInput(MouseDeviceInterface* device)
    {
        var data = battlefieldInputHook!.Original(device);
        var hovering = battlefieldClick == null;
        var click = battlefieldClick ?? battlefieldPointer;
        if (!inBattlefieldFrame || frameDeviceData != null || data == null || device != battlefieldMouse || click == null ||
            CurrentCpuDutyId() != click.Duty || backgroundInputAllowed?.Invoke() != true) return data;

        if (hovering && DateTime.UtcNow >= click.Expires) { battlefieldPointer = null; return data; }
        var release = !hovering && click.Pressed && (click.ReleaseRequested || DateTime.UtcNow >= click.Expires);
        var position = !hovering && !click.Positioned;
        var down = !hovering && !position && !click.Pressed;
        if (!click.Pressed && DateTime.UtcNow >= click.Expires)
        {
            battlefieldClick = null;
            return data;
        }
        originalDeviceData = *data;
        frameDeviceData = data;
        frameInputRelease = release;
        var button = click.Wheel != 0 ? MouseButtonFlags.None : click.Right ? MouseButtonFlags.RBUTTON : MouseButtonFlags.LBUTTON;
        data->PositionX = (int)click.Point.X;
        data->PositionY = (int)click.Point.Y;
        var framework = NativeFramework.Instance();
        // Keep the client's frame-level gate consistent with this virtual
        // device sample. Restore it after this frame; never activate the window.
        originalWindowInactive = framework->WindowInactive;
        framework->WindowInactive = false;
        frameCursor = framework->Cursor;
        if (frameCursor != null)
        {
            originalCursorOutside = frameCursor->IsCursorOutsideViewPort;
            frameCursor->IsCursorOutsideViewPort = false;
        }
        data->DeltaX = position ? data->PositionX - framework->CursorInputs.PositionX : 0;
        data->DeltaY = position ? data->PositionY - framework->CursorInputs.PositionY : 0;
        data->MouseWheel = down ? click.Wheel : 0;
        data->IsGameWindowFocused = true; // Transient client input only; no window activation.
        data->MouseButtonHeldFlags = hovering || release || position ? 0 : button;
        data->MouseButtonPressedFlags = down ? button : 0;
        var edges = (NativeCursorButtonEdges*)data;
        edges->DoubleClicked = down && click.DoubleClick ? button : 0;
        edges->Released = release ? button : 0;
        edges->Repeated = 0;
        click.Positioned = true;
        if (down) click.Pressed = true;
        return data;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool ClientToScreen(nint window, ref PInvoke.POINT point);

    private static void HideBattlefieldPanels(bool keepMiniMap = false)
    {
        foreach (var name in new[] { "ChatLog", "LovmPalette", "LovmPartyList", "LovmMiniMap", "LovmQueueList", "LovmNamePlate", "HowTo" })
        {
            if (keepMiniMap && name == "LovmMiniMap") continue;
            var addon = Plugin.GameGui.GetAddonByName(name);
            if (addon.IsNull || !addon.IsReady || !addon.IsVisible) continue;
            hiddenBattlefieldPanels[name] = addon.Address;
            ((AtkUnitBase*)addon.Address)->Hide(true, false, 0);
            Plugin.Log.Information($"[VerminionControl] temporarily hid {name} for battlefield input; visible={addon.IsVisible}");
        }
    }

    public static bool TryFocusMiniMap(System.Numerics.Vector3 world, out System.Numerics.Vector2 click)
    {
        click = default;
        if (CurrentCpuDutyId() is not (560 or 563 or 570) || battlefieldClick != null) return false;
        ReleaseBattlefieldInput(restoreRendering: false);
        var handle = Plugin.GameGui.GetAddonByName("LovmMiniMap");
        if (handle.IsNull || !handle.IsReady || !handle.IsVisible) return false;
        var addon = (AtkUnitBase*)handle.Address;
        // The captured Stage 9 structure markers identify Gate A, Gate B and
        // friendly Stone B. Derive scale from their live positions and bounds.
        var gateA = Plugin.ObjectTable.FirstOrDefault(obj => obj.BaseId == 2006534 && obj.Name.TextValue == "Gate A");
        var gateB = Plugin.ObjectTable.FirstOrDefault(obj => obj.BaseId == 2006534 && obj.Name.TextValue == "Gate B");
        var stoneB = Plugin.ObjectTable.FirstOrDefault(obj => obj.BaseId == 2006533 && obj.Name.TextValue == "Arcana Stone B");
        var nodeA = addon->GetNodeById(60010);
        var nodeB = addon->GetNodeById(60009);
        var nodeStone = addon->GetNodeById(60015);
        if (gateA == null || gateB == null || stoneB == null || !IsVisibleThroughParents(nodeA) ||
            !IsVisibleThroughParents(nodeB) || !IsVisibleThroughParents(nodeStone)) return false;
        var a = new FFXIVClientStructs.FFXIV.Common.Math.Bounds();
        var b = new FFXIVClientStructs.FFXIV.Common.Math.Bounds();
        var stone = new FFXIVClientStructs.FFXIV.Common.Math.Bounds();
        nodeA->GetBounds(&a);
        nodeB->GetBounds(&b);
        nodeStone->GetBounds(&stone);
        var dx = gateB.Position.X - gateA.Position.X;
        var dz = gateB.Position.Z - stoneB.Position.Z;
        if (dx <= 0 || dz <= 0 || Math.Abs(a.CenterY - b.CenterY) > 2 || Math.Abs(stone.CenterX - b.CenterX) > 2) return false;
        var sx = (b.CenterX - a.CenterX) / dx;
        var sz = (b.CenterY - stone.CenterY) / dz;
        if (sx is < 1 or > 12 || sz is < 1 or > 12 || Math.Abs(sx - sz) > 0.2f ||
            world.X is < -24 or > 24 || world.Z is < -24 or > 24) return false;
        click = new(b.CenterX + (world.X - gateB.Position.X) * sx,
            b.CenterY + (world.Z - gateB.Position.Z) * sz);
        if (!SendBattlefieldClick(click, release: false, keepMiniMap: true)) return false;
        Plugin.Log.Information($"[VerminionControl] minimap camera click world={world}; client={click}; scale={sx},{sz}; awaiting projection readback");
        return true;
    }

    public static bool SendBattlefieldClick(System.Numerics.Vector2 screen, bool release, bool rightClick = false, bool keepMiniMap = false, bool doubleClick = false, int wheel = 0)
    {
        if (release)
        {
            if (battlefieldClick is { } pending && pending.Right == rightClick)
            {
                pending.ReleaseRequested = true;
            }
            return true;
        }
        var duty = CurrentCpuDutyId();
        if (duty == 0 || battlefieldClick != null || backgroundInputAllowed?.Invoke() != true ||
            !float.IsFinite(screen.X) || !float.IsFinite(screen.Y) || !IsBattlefieldPointVisible(screen)) return false;
        var window = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
        var screenPoint = new PInvoke.POINT { x = (int)screen.X, y = (int)screen.Y };
        if (window == 0 || !ClientToScreen(window, ref screenPoint)) return false;
        if (battlefieldInputHook == null)
        {
            var manager = InputDeviceManager.Instance();
            var mouse = manager == null ? null : manager->MouseDevice;
            var framework = NativeFramework.Instance();
            if (mouse == null || mouse->VirtualTable == null || framework == null || framework->VirtualTable == null) return false;
            battlefieldMouse = &mouse->MouseDeviceInterface;
            battlefieldFrameHook = Plugin.GameInterop.HookFromAddress<NativeFramework.Delegates.Tick>(
                (nint)framework->VirtualTable->Tick, HandleBattlefieldFrame);
            battlefieldInputHook = Plugin.GameInterop.HookFromAddress<MouseDeviceInterface.Delegates.GetData>(
                (nint)battlefieldMouse->VirtualTable->GetData, HandleBattlefieldInput);
            battlefieldCursorHook = Plugin.GameInterop.HookFromImport<ReadCursorPosition>(
                null, "user32.dll", "GetCursorPos", 0, ReadBattlefieldCursor);
            battlefieldFrameHook.Enable();
            battlefieldInputHook.Enable();
            battlefieldCursorHook.Enable();
        }
        HideBattlefieldPanels(keepMiniMap);
        clickedMinion = 0;
        battlefieldClick = new() { Point = screen, ScreenPoint = screenPoint, Right = rightClick, DoubleClick = doubleClick, Wheel = wheel, Duty = duty, Expires = DateTime.UtcNow.AddSeconds(1) };
        Plugin.Log.Information($"[VerminionControl] queued background battlefield click; right={rightClick}; double={doubleClick}; wheel={wheel}; client={screen}");
        return true;
    }

    public static float? ReadBattlefieldCameraDistance()
    {
        var manager = FFXIVClientStructs.FFXIV.Client.Game.Control.CameraManager.Instance();
        if (CurrentCpuDutyId() != 570 || manager == null || manager->ActiveCameraIndex is not (0 or 3)) return null;
        var camera = manager->GetActiveCamera();
        return camera != null && float.IsFinite(camera->Distance) && camera->Distance > 0 ? camera->Distance : null;
    }

    public static bool TryZoomBattlefieldOut(out System.Numerics.Vector2 point)
    {
        point = default;
        var window = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
        if (CurrentCpuDutyId() != 570 || window == 0 || !PInvoke.User32.GetClientRect(window, out var bounds)) return false;
        point = new(bounds.right / 2f, bounds.bottom / 2f);
        return SendBattlefieldClick(point, release: false, wheel: -1);
    }

    public static void ReleaseBattlefieldInput(bool restoreRendering = true)
    {
        if (restoreRendering && restoreCaptureGameScale is { } scale)
        {
            restoreCaptureGameScale = null;
            Plugin.CommandManager.ProcessCommand("/gres " + scale);
        }
        battlefieldClick = battlefieldPointer = null;
        clickedMinion = 0;
        foreach (var (name, address) in hiddenBattlefieldPanels)
        {
            var addon = Plugin.GameGui.GetAddonByName(name);
            if (!addon.IsNull && addon.IsReady && addon.Address == address)
                ((AtkUnitBase*)addon.Address)->Show(true, 0);
        }
        hiddenBattlefieldPanels.Clear();
    }

    public static bool IsChallengeMenu => Plugin.ClientState.TerritoryType == 388 &&
        GameHelpers.TryGetAddonText("SelectString", 2, out var title) && title == "Verminion Challenge" &&
        GameHelpers.TryReadSelectStringEntries(out var entries) && entries.Contains("Stage 1: Tutorial");

    public static int ReadHighestAvailableChallenge()
    {
        if (!IsChallengeMenu || !GameHelpers.TryReadSelectStringEntries(out var entries)) return 0;
        var highest = 0;
        for (var stage = 1; stage <= 24; ++stage)
        {
            if (entries.Contains(ChallengeName(stage)))
            {
                if (highest != stage - 1) return 0;
                highest = stage;
            }
        }
        return highest;
    }

    public static void CloseChallengeMenu()
    {
        if (!IsChallengeMenu) return;
        ((AtkUnitBase*)Plugin.GameGui.GetAddonByName("SelectString").Address)->Close(true);
    }
    public static bool IsAdmissionPrompt => Plugin.ClientState.TerritoryType == 388 &&
        GameHelpers.TryGetAddonText("SelectYesno", 2, out var prompt) &&
        prompt.Contains("Lord of Verminion") && prompt.Contains("Are you prepared for battle?");
    public static bool IsSetupMenu => IsChallengeMenu || IsAdmissionPrompt || Plugin.ClientState.TerritoryType == 388 &&
        GameHelpers.TryGetAddonText("SelectString", 2, out var title) && title == "Lord of Verminion" &&
        GameHelpers.TryReadSelectStringEntries(out var entries) && entries.Contains("Verminion Challenge");

    public static bool InteractWithTable(Dalamud.Game.ClientState.Objects.Types.IGameObject table)
    {
        var player = Plugin.ObjectTable.LocalPlayer;
        if (player == null || table.BaseId != 2006529 || !table.IsTargetable ||
            System.Numerics.Vector3.Distance(player.Position, table.Position) > 4) return false;
        var target = FFXIVClientStructs.FFXIV.Client.Game.Control.TargetSystem.Instance();
        if (target == null) return false;
        // Tables have an interaction radius. The generic helper's fixed 2-yalm
        // center-distance limit attempts to walk inside their collision mesh.
        // Let the native interaction perform its own range/line-of-sight checks.
        target->InteractWithObject((FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)table.Address, true);
        return true;
    }

    public static bool PrepareOwnedPalette()
    {
        var palette = GoldSaucerModule.Instance();
        var ui = UIState.Instance();
        if (palette == null || ui == null) return false;
        var available = palette->HotbarMinions.ToArray().Where(id => id != 0 && ui->IsCompanionUnlocked(id)).Distinct().ToList();
        if (available.Count >= 3) return true;
        foreach (var companion in Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Companion>())
        {
            if (companion.RowId == 0 || companion.RowId > ushort.MaxValue || !ui->IsCompanionUnlocked(companion.RowId) || available.Contains((ushort)companion.RowId)) continue;
            var emptySlot = Array.IndexOf(palette->HotbarMinions.ToArray(), (ushort)0);
            if (emptySlot < 0) return false;
            palette->SetHotbarMinion(emptySlot, (ushort)companion.RowId);
            if (palette->GetHotbarMinion(emptySlot) != companion.RowId) return false;
            available.Add((ushort)companion.RowId);
            Plugin.Log.Information($"[VerminionControl] palette slot={emptySlot}; minion={companion.RowId}; verified=True; purchase=None");
            if (available.Count >= 3) return true;
        }
        return false;
    }

    public static void CaptureSetup()
    {
        var player = PlayerState.Instance();
        Plugin.Log.Information($"[VerminionControl] completedStages={(player == null ? -1 : player->CompletedLoVMStages)}");
        foreach (var quest in Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Quest>())
            if (quest.Name.ToString() is "It Could Happen to You" or "World of Wonders" or "Rising to the Challenge")
                Plugin.Log.Information($"[VerminionControl] prerequisite={quest.Name}; quest={quest.RowId & 0xFFFF}; complete={FFXIVClientStructs.FFXIV.Client.Game.QuestManager.IsQuestComplete((ushort)(quest.RowId & 0xFFFF))}");
        foreach (var aetheryte in Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Aetheryte>())
            if (aetheryte.AethernetName.Value.Name.ToString().Contains("Minion"))
                Plugin.Log.Information($"[VerminionControl] aethernet={aetheryte.RowId}; name={aetheryte.AethernetName.Value.Name}");
        foreach (var row in Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.ContentFinderCondition>())
        {
            var name = row.Name.ToString();
            if (name.Contains("LoVM", StringComparison.OrdinalIgnoreCase) || name.Contains("Verminion", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Stage ", StringComparison.OrdinalIgnoreCase))
                Plugin.Log.Information($"[VerminionControl] duty={row.RowId}; name={name}; territory={row.TerritoryType.RowId}");
        }
        var palette = GoldSaucerModule.Instance();
        if (palette != null)
            Plugin.Log.Information($"[VerminionControl] palette={string.Join(',', palette->HotbarMinions.ToArray())}");
        foreach (var row in Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Companion>())
            if (row.RowId != 0 && UIState.Instance() != null && UIState.Instance()->IsCompanionUnlocked(row.RowId))
            {
                var combat = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.CompanionTransient>().GetRow(row.RowId);
                Plugin.Log.Information($"[VerminionControl] ownedMinion={row.RowId}; name={row.Singular}; hp={row.HP}; cost={row.Cost}; type={row.MinionRace.RowId}; attack={combat.Attack}; defense={combat.Defense}; speed={combat.Speed}; area={combat.HasAreaAttack}; arcana={combat.StrengthArcana}; special={combat.SpecialActionName}; effect={combat.SpecialActionDescription}");
            }
    }

    public static void CaptureNearbyObjects()
    {
        foreach (var obj in Plugin.ObjectTable.Where(obj => (byte)obj.ObjectKind is 3 or 5 or 7).Take(100))
            Plugin.Log.Information($"[VerminionControl] nearby kind={obj.ObjectKind}; base={obj.BaseId}; name={obj.Name}; position={obj.Position}; targetable={obj.IsTargetable}");
        CaptureAddon("SelectString");
        CaptureAddon("SelectIconString");
        CaptureAddon("Talk");
    }

    public static void CaptureFinder()
    {
        var agent = AgentContentsFinder.Instance();
        if (agent != null)
        {
            Plugin.Log.Information($"[VerminionControl] selected={agent->SelectedDuty.ContentType}/{agent->SelectedDuty.Id}");
            var count = 0;
            foreach (var entry in agent->ContentList)
            {
                if (++count > 100) break;
                if (entry.Value != null)
                    Plugin.Log.Information($"[VerminionControl] finderRow={count - 1}; type={entry.Value->Id.ContentType}; id={entry.Value->Id.Id}; name={entry.Value->Name}");
            }
        }
        CaptureAddon("ContentsFinder");
    }

    private static DateTime nextRoutineCaptureUtc, movementCaptureUtc;

    public static void CaptureBattle(string reason, bool detailed = true)
    {
        // Keep paired position evidence without filling Dalamud's capped log
        // with every selection retry. Opening, phase, special and result
        // snapshots are independent of this routine sampling interval.
        if (CurrentCpuDutyId() != 552 && reason is "selection readback before movement" or
            "after tutorial movement command" or "boss selection mismatch")
        {
            var now = DateTime.UtcNow;
            if (reason == "after tutorial movement command")
            {
                if (movementCaptureUtc == DateTime.MinValue || (now - movementCaptureUtc).TotalSeconds > 3) return;
                movementCaptureUtc = DateTime.MinValue;
            }
            else
            {
                if (now < nextRoutineCaptureUtc) return;
                nextRoutineCaptureUtc = now.AddSeconds(15);
                movementCaptureUtc = reason == "selection readback before movement" ? now : DateTime.MinValue;
            }
            detailed = false;
        }
        Plugin.Log.Information($"[VerminionControl] snapshot={reason}; territory={Plugin.ClientState.TerritoryType}; playing={Plugin.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.PlayingLordOfVerminion]}");
        var chat = RaptureLogModule.Instance();
        if (detailed && chat != null)
        {
            var newest = checked((int)chat->GetCurrentLogIndex());
            for (var index = Math.Max(0, newest - 30); index <= newest; ++index)
            {
                if (!chat->GetLogMessageDetail(index, out _, out var bytes, out var kind, out _, out _, out _) ||
                    kind is not ((ushort)Dalamud.Game.Text.XivChatType.SystemMessage or (ushort)Dalamud.Game.Text.XivChatType.ErrorMessage)) continue;
                var message = Dalamud.Game.Text.SeStringHandling.SeString.Parse(bytes).TextValue;
                if (IsTutorialBattle() || new[] { "minion", "battle", "register", "duty", "party", "unable", "tutorial", "challenge", "victory", "defeat", "complete" }.Any(word => message.Contains(word, StringComparison.OrdinalIgnoreCase)))
                    Plugin.Log.Information($"[VerminionControl] recentSystem index={index}; text={message}");
            }
        }
        if (detailed)
        {
            Plugin.Log.Information($"[VerminionControl] tutorialPrompt={ReadTutorialPrompt()}");
            foreach (var name in new[] { "LovmReady", "LovmPalette", "LovmPartyList", "LovmQueueList", "LovmMiniMap", "LovmNamePlate", "LovmStatus", "LovmConfirm", "LovmResult", "LovmHelp", "Talk", "SelectString", "SelectYesno", "SelectOk", "ContentsFinderConfirm", "ActiveHelp" })
                CaptureAddon(name);
        }
        var cameraManager = FFXIVClientStructs.FFXIV.Client.Game.Control.CameraManager.Instance();
        if (CurrentCpuDutyId() == 570 && cameraManager != null && cameraManager->ActiveCameraIndex is 0 or 3)
        {
            var camera = cameraManager->GetActiveCamera();
            if (camera != null)
                Plugin.Log.Information($"[VerminionControl] camera distance={camera->Distance}; limits={camera->MinDistance},{camera->MaxDistance}; fov={camera->FoV}");
        }
        var finder = ContentsFinder.Instance();
        if (finder != null)
            Plugin.Log.Information($"[VerminionControl] queue state={finder->QueueInfo.QueueState}; popped={finder->QueueInfo.PoppedQueueEntry.ContentType}/{finder->QueueInfo.PoppedQueueEntry.Id}");
        foreach (var obj in Plugin.ObjectTable.OfType<Dalamud.Game.ClientState.Objects.Types.IBattleNpc>().Take(100))
        {
            var native = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)obj.Address;
            if (native == null || native->SubKind != 7) continue;
            var character = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)native;
            Plugin.Log.Information($"[VerminionControl] minion index={obj.ObjectIndex}; id={obj.GameObjectId}; data={obj.BaseId}; name={obj.Name}; hp={obj.CurrentHp}/{obj.MaxHp}; position={obj.Position}; battalion={character->CharacterData.Battalion}; targetable={obj.IsTargetable}; target={obj.TargetObjectId}");
            if (obj.IsCasting)
            {
                var cast = character->GetCastInfo();
                Plugin.Log.Information($"[VerminionControl] minionCast id={obj.GameObjectId}; action={obj.CastActionId}; elapsed={obj.CurrentCastTime}; total={obj.TotalCastTime}; target={obj.CastTargetObjectId}; location={(cast == null ? "unavailable" : cast->TargetLocation.ToString())}");
            }
            foreach (var status in obj.StatusList.Where(status => status.StatusId != 0))
                Plugin.Log.Information($"[VerminionControl] minionStatus id={obj.GameObjectId}; status={status.StatusId}; name={status.GameData.Value.Name}; remaining={status.RemainingTime}; param={status.Param}");
        }
        var stage = AtkStage.Instance();
        if (detailed && CurrentCpuDutyId() != 0)
        {
            foreach (var obj in Plugin.ObjectTable.Where(obj => obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.BattleNpc || obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventObj).Take(128))
            {
                Plugin.GameGui.WorldToScreen(obj.Position, out var screen);
                Plugin.Log.Information($"[VerminionControl] fieldObject index={obj.ObjectIndex}; kind={obj.ObjectKind}; base={obj.BaseId}; name={obj.Name}; position={obj.Position}; screen={screen}");
            }
            if (stage != null)
            {
                var count = 0;
                for (var evt = stage->ViewportEventManager.Event; evt != null && count++ < 128; evt = evt->NextEvent)
                {
                    var listener = "other";
                    foreach (var name in new[] { "LovmPalette", "LovmPartyList", "LovmQueueList", "LovmMiniMap" })
                        if (Plugin.GameGui.GetAddonByName(name).Address == (nint)evt->Listener) listener = name;
                    if (listener != "other")
                        Plugin.Log.Information($"[VerminionControl] viewport listener={listener}; event={evt->State.EventType}; param={evt->Param}");
                }
            }
        }
        if (stage != null)
            foreach (var type in detailed
                ? new[] { NumberArrayType.Lovm, NumberArrayType.LovmPalette, NumberArrayType.LovmQueueList, NumberArrayType.LovmNamePlate, NumberArrayType.LovmMiniMap, NumberArrayType.LovmActionDetail }
                : new[] { NumberArrayType.Lovm })
            {
                var array = stage->GetNumberArrayData(type);
                if (array == null || array->IntArray == null || array->Size is <= 0 or > 10000) continue;
                var values = new List<string>();
                for (var i = 0; i < Math.Min(array->Size, 512); ++i)
                    if (array->IntArray[i] != 0) values.Add($"{i}:{array->IntArray[i]}");
                Plugin.Log.Information($"[VerminionControl] numberArray={type}; size={array->Size}; nonzero={string.Join(',', values)}");
            }
    }

    public static void CaptureAddon(string name)
    {
        var handle = Plugin.GameGui.GetAddonByName(name);
        if (handle.IsNull || !handle.IsReady || !handle.IsVisible) return;
        var addon = (AtkUnitBase*)handle.Address;
        Plugin.Log.Information($"[VerminionControl] addon={name}; values={addon->AtkValuesCount}; nodes={addon->UldManager.NodeListCount}");
        var stage = AtkStage.Instance();
        var tooltips = new Dictionary<nint, string>();
        if (stage != null && name.StartsWith("Lovm"))
        {
            var count = 0;
            foreach (var entry in stage->TooltipManager.TooltipMap)
            {
                if (++count > 2048) break;
                var node = entry.Item1.Value;
                var tooltip = entry.Item2.Value;
                if (node == null || tooltip == null || tooltip->ParentId != addon->Id ||
                    tooltip->Type != AtkTooltipType.Text) continue;
                tooltips[(nint)node] = tooltip->AtkTooltipArgs.TextArgs.Text.ToString();
            }
        }
        for (var i = 0; i < Math.Min((int)addon->AtkValuesCount, 160); ++i)
        {
            var value = addon->AtkValues[i];
            if (value.Type == AtkValueType.Undefined) continue;
            var text = value.Type switch
            {
                AtkValueType.Int => value.Int.ToString(),
                AtkValueType.UInt => value.UInt.ToString(),
                AtkValueType.Bool => value.Bool.ToString(),
                AtkValueType.Float => value.Float.ToString(),
                AtkValueType.String or AtkValueType.ManagedString => value.String.ToString(),
                _ => "(non-numeric)",
            };
            Plugin.Log.Information($"[VerminionControl] {name}.value[{i}]={text}; type={value.Type}");
        }
        CaptureNodes(name, &addon->UldManager, 0, tooltips);
        if (name == "LovmMiniMap" && CurrentCpuDutyId() is 560 or 570)
        {
            // Bounded control captures supply minimap geometry for checking
            // camera routes against current gate and stone positions.
            for (var i = 0; i < Math.Min((int)addon->UldManager.NodeListCount, 128); ++i)
            {
                var node = addon->UldManager.NodeList[i];
                if (!IsVisibleThroughParents(node)) continue;
                Plugin.Log.Information($"[VerminionControl] minimap node={node->NodeId}; type={node->Type}; screen={node->ScreenX},{node->ScreenY}; local={node->X},{node->Y}; size={node->Width},{node->Height}; scale={node->ScaleX},{node->ScaleY}");
            }
        }
    }

    private static void CaptureNodes(string path, AtkUldManager* uld, int depth, Dictionary<nint, string> tooltips)
    {
        if (depth > 2 || uld->NodeListCount > 512) return;
        for (var i = 0; i < uld->NodeListCount; ++i)
        {
            var node = uld->NodeList[i];
            if (!IsVisibleThroughParents(node)) continue;
            var events = new List<string>();
            var count = 0;
            for (var evt = node->AtkEventManager.Event; evt != null && count++ < 16; evt = evt->NextEvent)
                events.Add($"{evt->State.EventType}:{evt->Param}:{evt->State.StateFlags}");
            var text = node->Type == NodeType.Text ? node->GetAsAtkTextNode()->NodeText.ToString() : string.Empty;
            if (tooltips.TryGetValue((nint)node, out var tooltip))
                Plugin.Log.Information($"[VerminionControl] tooltip node={path}/{node->NodeId}; text={tooltip}");
            if (text.Length > 0 || depth == 0 && events.Count > 0)
                Plugin.Log.Information($"[VerminionControl] node={path}/{node->NodeId}; type={node->Type}; text={text}; events={string.Join(',', events)}");
            if ((ushort)node->Type >= 1000)
            {
                var component = node->GetAsAtkComponentNode()->Component;
                if (component != null) CaptureNodes($"{path}/{node->NodeId}", &component->UldManager, depth + 1, tooltips);
            }
        }
    }

    private static bool IsVisibleThroughParents(AtkResNode* node)
    {
        if (node == null) return false;
        var depth = 0;
        for (; node != null && depth++ < 64; node = node->ParentNode)
            if (!node->IsVisible()) return false;
        return node == null;
    }
}
