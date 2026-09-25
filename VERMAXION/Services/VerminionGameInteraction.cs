using System;
using System.Collections.Generic;
using System.Linq;
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
    public static bool IsTutorialBattle()
    {
        var finder = ContentsFinder.Instance();
        return Plugin.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.PlayingLordOfVerminion] &&
            finder != null && finder->QueueInfo.PoppedQueueEntry.ContentType == ContentsType.Regular &&
            finder->QueueInfo.PoppedQueueEntry.Id == 552;
    }

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

    public static bool HasTutorialQueue()
    {
        var finder = ContentsFinder.Instance();
        if (finder == null || finder->QueueInfo.QueueState is ContentsFinderQueueState.None or ContentsFinderQueueState.InContent) return false;
        var found = false;
        foreach (var entry in finder->QueueInfo.QueuedEntries)
        {
            if (entry.Id == 0) continue;
            if (entry.ContentType != ContentsType.Regular || entry.Id != 552) return false;
            found = true;
        }
        var popped = finder->QueueInfo.PoppedQueueEntry;
        return found || popped.ContentType == ContentsType.Regular && popped.Id == 552;
    }

    public static bool TryCommenceTutorial() => HasTutorialQueue() &&
        GameHelpers.TryGetAddonText("ContentsFinderConfirm", 49, out var title) && title == "Stage 1: Tutorial" &&
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
        if (!IsTutorialBattle()) return false;
        var handle = Plugin.GameGui.GetAddonByName("LovmPalette");
        if (handle.IsNull || !handle.IsVisible || !handle.IsReady) return false;
        var addon = (AtkUnitBase*)handle.Address;
        var node = addon->GetNodeById(nodeId);
        if (!IsVisibleThroughParents(node) || (ushort)node->Type < 1000) return false;
        var component = node->GetAsAtkComponentNode()->Component;
        if (component == null) return false;
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

    public static bool TryClickBattlefield(System.Numerics.Vector3 world, bool rightClick)
    {
        if (!IsTutorialBattle() || !Plugin.GameGui.WorldToScreen(world, out var screen)) return false;
        var stage = AtkStage.Instance();
        var handle = Plugin.GameGui.GetAddonByName("LovmMiniMap");
        if (stage == null || handle.IsNull || !handle.IsReady || !handle.IsVisible) return false;
        var count = 0;
        for (var evt = stage->ViewportEventManager.Event; evt != null && count++ < 128; evt = evt->NextEvent)
        {
            if (evt->State.EventType != AtkEventType.RawInputData || (nint)evt->Listener != handle.Address) continue;
            var click = *evt;
            var raw = new FFXIVClientStructs.FFXIV.Component.GUI.AtkInputData();
            var button = rightClick ? FFXIVClientStructs.FFXIV.Client.System.Input.MouseButtonFlags.RBUTTON : FFXIVClientStructs.FFXIV.Client.System.Input.MouseButtonFlags.LBUTTON;
            raw.CursorInputs.PositionX = checked((int)screen.X);
            raw.CursorInputs.PositionY = checked((int)screen.Y);
            raw.CursorInputs.IsGameWindowFocused = true;
            raw.CursorInputs.MouseButtonPressedFlags = button;
            raw.CursorInputs.MouseButtonHeldFlags = button;
            raw.UIFilteredCursorInputs = raw.CursorInputs;
            var data = new AtkEventData { RawInputData = &raw };
            click.Listener->ReceiveEvent(AtkEventType.RawInputData, checked((int)click.Param), &click, &data);
            raw.CursorInputs.MouseButtonPressedFlags = 0;
            raw.CursorInputs.MouseButtonHeldFlags = 0;
            raw.CursorInputs.MouseButtonReleasedFlags = button;
            raw.UIFilteredCursorInputs = raw.CursorInputs;
            click.Listener->ReceiveEvent(AtkEventType.RawInputData, checked((int)click.Param), &click, &data);
            Plugin.Log.Information($"[VerminionControl] battlefield click dispatched; right={rightClick}; world={world}; screen={screen}; awaiting readback");
            return true;
        }
        return false;
    }

    public static bool IsChallengeMenu => Plugin.ClientState.TerritoryType == 388 &&
        GameHelpers.TryGetAddonText("SelectString", 2, out var title) && title == "Verminion Challenge" &&
        GameHelpers.TryReadSelectStringEntries(out var entries) && entries.Contains("Stage 1: Tutorial");
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
            if (name.Contains("LoVM", StringComparison.OrdinalIgnoreCase) || name.Contains("Verminion", StringComparison.OrdinalIgnoreCase))
                Plugin.Log.Information($"[VerminionControl] duty={row.RowId}; name={name}; territory={row.TerritoryType.RowId}");
        }
        var palette = GoldSaucerModule.Instance();
        if (palette != null)
            Plugin.Log.Information($"[VerminionControl] palette={string.Join(',', palette->HotbarMinions.ToArray())}");
        foreach (var row in Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Companion>())
            if (row.RowId != 0 && UIState.Instance() != null && UIState.Instance()->IsCompanionUnlocked(row.RowId))
                Plugin.Log.Information($"[VerminionControl] ownedMinion={row.RowId}; name={row.Singular}");
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

    public static void CaptureBattle(string reason)
    {
        Plugin.Log.Information($"[VerminionControl] snapshot={reason}; territory={Plugin.ClientState.TerritoryType}; playing={Plugin.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.PlayingLordOfVerminion]}");
        var chat = RaptureLogModule.Instance();
        if (chat != null)
        {
            var newest = checked((int)chat->GetCurrentLogIndex());
            for (var index = Math.Max(0, newest - 30); index <= newest; ++index)
            {
                if (!chat->GetLogMessageDetail(index, out _, out var bytes, out var kind, out _, out _, out _) ||
                    kind is not ((ushort)Dalamud.Game.Text.XivChatType.SystemMessage or (ushort)Dalamud.Game.Text.XivChatType.ErrorMessage)) continue;
                var message = Dalamud.Game.Text.SeStringHandling.SeString.Parse(bytes).TextValue;
                if (IsTutorialBattle() || new[] { "minion", "battle", "register", "duty", "party", "unable" }.Any(word => message.Contains(word, StringComparison.OrdinalIgnoreCase)))
                    Plugin.Log.Information($"[VerminionControl] recentSystem index={index}; text={message}");
            }
        }
        Plugin.Log.Information($"[VerminionControl] tutorialPrompt={ReadTutorialPrompt()}");
        foreach (var name in new[] { "LovmReady", "LovmPalette", "LovmPartyList", "LovmQueueList", "LovmMiniMap", "LovmStatus", "LovmConfirm", "LovmResult", "LovmHelp", "Talk", "SelectString", "SelectYesno", "SelectOk", "ContentsFinderConfirm", "ActiveHelp" })
            CaptureAddon(name);
        var finder = ContentsFinder.Instance();
        if (finder != null)
            Plugin.Log.Information($"[VerminionControl] queue state={finder->QueueInfo.QueueState}; popped={finder->QueueInfo.PoppedQueueEntry.ContentType}/{finder->QueueInfo.PoppedQueueEntry.Id}");
        foreach (var obj in Plugin.ObjectTable.OfType<Dalamud.Game.ClientState.Objects.Types.IBattleNpc>().Take(100))
        {
            var native = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)obj.Address;
            if (native == null || native->SubKind != 7) continue;
            Plugin.Log.Information($"[VerminionControl] minion index={obj.ObjectIndex}; data={obj.BaseId}; name={obj.Name}; hp={obj.CurrentHp}/{obj.MaxHp}; position={obj.Position}; targetable={obj.IsTargetable}");
        }
        var stage = AtkStage.Instance();
        if (IsTutorialBattle())
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
            foreach (var type in new[] { NumberArrayType.Lovm, NumberArrayType.LovmQueueList, NumberArrayType.LovmNamePlate, NumberArrayType.LovmMiniMap, NumberArrayType.LovmActionDetail })
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
                _ => "(non-numeric)",
            };
            Plugin.Log.Information($"[VerminionControl] {name}.value[{i}]={text}; type={value.Type}");
        }
        CaptureNodes(name, &addon->UldManager, 0);
    }

    private static void CaptureNodes(string path, AtkUldManager* uld, int depth)
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
            if (text.Length > 0 || depth == 0 && events.Count > 0)
                Plugin.Log.Information($"[VerminionControl] node={path}/{node->NodeId}; type={node->Type}; text={text}; events={string.Join(',', events)}");
            if ((ushort)node->Type >= 1000)
            {
                var component = node->GetAsAtkComponentNode()->Component;
                if (component != null) CaptureNodes($"{path}/{node->NodeId}", &component->UldManager, depth + 1);
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



