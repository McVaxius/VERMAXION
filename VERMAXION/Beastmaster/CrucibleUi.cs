// Adapted from erdelf/AutoDuty, commit 7e0ede4be688980d217faf6889ffdac53e18e30f. Private local port.
using ECommons.GameHelpers;
using Dalamud.Memory;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace VERMAXION.Beastmaster
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text.RegularExpressions;
    using ECommons;
    using ECommons.StringHelpers;
    using ECommons.UIHelpers;
    using VERMAXION.Beastmaster.Readers;
    

    internal static unsafe class CrucibleUi
    {
        public const string TeamWindow           = "XBMPetParty";
        public const string BestiaryWindow       = "XBMMonsterNotebook";
        public const string BestiaryWindowFilter = "XBMMonsterNotebookFilterSetting";
        public const string BoardList            = "XBMStageList";
        public const string BoardLayout          = "XBMStageDetailList";
        public const string LootWindow           = "XBMContentsBooty";
        public const string TreasureWindow       = "XBMContentsTreasure";
        public const string ResultWindow         = "XBMResult";
        public const string ShopWindow           = "XBMContentsItemShop";
        public const string YesNo                = "SelectYesno";
        public const string ContextMenu          = "ContextMenu";
        public const string MainHud              = "XBMContentsMainHUD";

        public const int BestiaryPageSize = 25;

        private const uint TeamList = 11;
        
        public static AtkUnitBase* Ready(string name)
        {
            AtkUnitBase* addon = (AtkUnitBase*)Svc.GameGui.GetAddonByName(name).Address;
            return addon != null && addon->IsVisible && addon->IsReady ? addon : null;
        }

        public static bool IsOpen(string name) => Ready(name) != null;

        public static bool IsVisible(string name)
        {
            AtkUnitBase* addon = (AtkUnitBase*)Svc.GameGui.GetAddonByName(name).Address;
            return addon != null && addon->IsVisible;
        }

        public static bool TryReady(string name, out AtkUnitBase* addon)
        {
            addon = Ready(name);
            return addon != null;
        }

        internal static string LootRejectionText =>
            Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Addon>().TryGetRow(17677, out var row)
                ? row.Text.ExtractText() : string.Empty;

        internal static AtkUnitBase* LootRejectionNotice(string expected, out bool anyVisibleOk)
        {
            anyVisibleOk = false;
            var manager = RaptureAtkUnitManager.Instance();
            if (manager == null) return null;
            for (var index = Math.Min((int)manager->AllLoadedUnitsList.Count, 256) - 1; index >= 0; index--)
            {
                var addon = manager->AllLoadedUnitsList.Entries[index].Value;
                if (addon == null || !addon->IsVisible || addon->NameString != "SelectOk") continue;
                anyVisibleOk = true;
                if (!addon->IsReady) continue;
                var notice = (AddonSelectOk*)addon;
                if (notice->PromptText == null || notice->PromptText->NodeText.StringPtr.Value == null ||
                    notice->OkButton == null || !notice->OkButton->IsEnabled) continue;
                var actual = MemoryHelper.ReadSeStringNullTerminated((nint)notice->PromptText->NodeText.StringPtr.Value).TextValue;
                if (MatchesLootRejection(actual, expected)) return addon;
            }
            return null;
        }

        internal static bool MatchesLootRejection(string actual, string expected)
        {
            static string Normalize(string text) => Regex.Replace(text, @"\s+", " ").Trim();
            var observed = Normalize(actual);
            // ExtractText omits the conditional reasons in row 17677. Its
            // remaining localized header/footer identifies this owned loot
            // notice even when the rendered popup contains additional text.
            return expected.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Normalize)
                .Any(phrase => phrase.Length > 0 && observed.Contains(phrase, StringComparison.OrdinalIgnoreCase));
        }

        public static bool TryPrompt(uint textRow, out AtkUnitBase* prompt, bool finalLineOnly = false)
        {
            if (!TryReady(YesNo, out prompt)) return false;
            var node = ((AddonSelectYesno*)prompt)->PromptText;
            if (node == null || node->NodeText.StringPtr.Value == null) return false;
            var actual = MemoryHelper.ReadSeStringNullTerminated((nint)node->NodeText.StringPtr.Value).TextValue.Trim();
            var expected = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Addon>().TryGetRow(textRow, out var row)
                ? row.Text.ExtractText().Trim() : "";
            if (textRow == 17787 && finalLineOnly) return BeastmasterEntryPrompt.Matches(actual, expected);
            if (finalLineOnly)
            {
                actual = actual[(actual.LastIndexOf('\n') + 1)..].Trim();
                expected = expected[(expected.LastIndexOf('\n') + 1)..].Trim();
            }
            return expected.Length > 0 && string.Equals(actual, expected, StringComparison.Ordinal);
        }

        public static string PromptText(AtkUnitBase* prompt)
        {
            var node = ((AddonSelectYesno*)prompt)->PromptText;
            return node == null || node->NodeText.StringPtr.Value == null ? string.Empty : MemoryHelper.ReadSeStringNullTerminated((nint)node->NodeText.StringPtr.Value).TextValue.Trim();
        }

        public static bool ClickButton(AtkUnitBase* addon, uint nodeId)
        {
            AtkResNode*       node   = addon->UldManager.SearchNodeById(nodeId);
            AtkComponentNode* button = node == null ? null : node->GetAsAtkComponentNode();
            if (button == null)
            {
                Svc.Log.Warning($"[Crucible] {addon->NameString} has no button node {nodeId}");
                return false;
            }

            AtkEvent* evt = button->AtkResNode.AtkEventManager.Event;
            while (evt != null && evt->State.EventType != AtkEventType.ButtonClick)
                evt = evt->NextEvent;

            if (evt == null)
            {
                Svc.Log.Warning($"[Crucible] {addon->NameString} button {nodeId} has no click event");
                return false;
            }

            Svc.Log.Debug($"[Crucible] {addon->NameString} click button {nodeId} (param {evt->Param})");
            addon->ReceiveEvent(evt->State.EventType, (int)evt->Param, evt);
            return true;
        }

        public static bool ClickEvent(AtkUnitBase* addon, AtkEventType type, uint param)
        {
            AtkEvent* evt = FindEvent(&addon->UldManager, type, param, 0);
            if (evt == null)
            {
                Svc.Log.Warning($"[Crucible] {addon->NameString} has no {type} event with param {param}");
                return false;
            }

            Svc.Log.Debug($"[Crucible] {addon->NameString} {type} (param {param})");
            AtkEventData data = new();
            addon->ReceiveEvent(type, (int)param, evt, &data);
            return true;
        }

        private static AtkEvent* FindEvent(AtkUldManager* uld, AtkEventType type, uint param, int depth)
        {
            if (depth > 6)
                return null;

            for (int i = 0; i < uld->NodeListCount; i++)
            {
                AtkResNode* node = uld->NodeList[i];
                if (node == null || !node->IsVisible())
                    continue;

                for (AtkEvent* evt = node->AtkEventManager.Event; evt != null; evt = evt->NextEvent)
                    if (evt->State.EventType == type && evt->Param == param)
                        return evt;

                AtkComponentNode* component = node->GetAsAtkComponentNode();
                if (component == null || component->Component == null)
                    continue;

                AtkEvent* found = FindEvent(&component->Component->UldManager, type, param, depth + 1);
                if (found != null)
                    return found;
            }

            return null;
        }

        public static int ContextMenuOptionCount(AtkUnitBase* menu)
        {
            int count = 0;
            for (int i = 0; i < menu->AtkValuesCount; i++)
                if (menu->AtkValues[i].Type.ToString().Contains("String") && menu->AtkValues[i].GetValueAsString().Length > 0)
                    count++;
            return count;
        }

        public static int SelectStringIndex(AtkUnitBase* addon, params string[] contains)
        {
            AddonSelectString* select = (AddonSelectString*)addon;
            ref PopupMenu      menu   = ref select->PopupMenu.PopupMenu;
            if (menu.EntryNames == null)
                return -1;

            for (int i = 0; i < menu.EntryCount; i++)
            {
                if (menu.EntryNames[i].Value == null)
                    continue;

                string entry = MemoryHelper.ReadSeStringNullTerminated((nint)menu.EntryNames[i].Value).TextValue;
                if (contains.Any(s => entry.Contains(s, StringComparison.OrdinalIgnoreCase)))
                    return i;
            }

            return -1;
        }

        public readonly record struct ItemSlot(int Slot, uint Row, string Name);

        public static int ShopCoins(AtkUnitBase* shop) =>
            (int)new ReaderXBMContentsItemShop(shop).Coins;

        public static List<ReaderXBMContentsItemShop.StockEntry> ShopStock(AtkUnitBase* shop) => 
            new ReaderXBMContentsItemShop(shop).StockEntries;

        public static uint[] ShopHeldItems(AtkUnitBase* shop) => 
            new ReaderXBMContentsItemShop(shop).ItemEntriesValid.Select(ie => ie.Id).ToArray();

        public static HashSet<uint> ShopOwnedGear(AtkUnitBase* shop) => 
            new ReaderXBMContentsItemShop(shop).OwnedEntriesOwned.Select(ge => ge.Id).ToHashSet();

        public static IEnumerable<uint> BestiaryShowing(AtkUnitBase* notebook)
        {
            ReaderXBMMonsterNotebook x = new(notebook);
            return x.CurrentPageEntries.Select(m => m.Number);
        }

        public static bool BestiaryShows(AtkUnitBase* notebook, uint number)
        {
            ReaderXBMMonsterNotebook x = new(notebook);

            foreach (ReaderXBMMonsterNotebook.MonsterEntry entry in x.CurrentPageEntries)
                if (entry.Number == number)
                    return true;

            return false;
        }

        public static CrucibleFamiliar? BestiarySelected(AtkUnitBase* notebook)
        {
            ReaderXBMMonsterNotebook reader = new(notebook);

            if (notebook->AtkValuesCount <= 273 || !reader.Selected)
                return null;

            uint number = reader.CurrentNumber;
            int  rank   = reader.CurrentRank;
            if (number == 0 || rank == 0)
                return null;

            return new CrucibleFamiliar(number)
                   {
                       Rank               = rank,
                       Hp                 = reader.CurrentMaxHP,
                       Exp                = reader.TryReadCurrentExperience(out var experience) ? experience.ToString() : string.Empty,
                       Strength           = reader.CurrentStrength,
                       PhysicalResistance = reader.CurrentPhysResistance,
                       Constitution       = reader.CurrentConstitution,
                       Intelligence       = reader.CurrentIntelligence,
                       MagicResistance    = reader.CurrentMagicResistance,
                   };
        }

        public static List<ReaderXBMPetParty.MonsterEntry>? Team(ReaderXBMPetParty? party = null)
        {
            if (party == null)
            {
                AtkUnitBase* addon = Ready(TeamWindow);
                if (addon == null)
                    return null;

                party = new ReaderXBMPetParty(addon);
            }

            return party.TeamEntries;
        }

        public static bool TryRegistrationTeam(out AtkUnitBase* party, out List<ReaderXBMPetParty.MonsterEntry> rows)
        {
            party = Ready(TeamWindow);
            rows = [];
            if (party == null) return false;
            if (party->AtkValues == null || party->AtkValuesCount <= 0)
                throw new BeastmasterReadUnavailableException(TeamWindow, "the registration fields are not available");
            if (!party->AtkValues[0].Bool) return false;
            rows = new ReaderXBMPetParty(party).TeamEntries;
            return true;
        }

        public static CrucibleFamiliar? FamiliarDetail(string window)
        {
            AtkUnitBase* addon = Ready(window);
            if (addon == null)
                return null;

            AtkUldManager* uld  = &addon->UldManager;
            string         name = Text(uld, 13);
            string         hp   = Text(uld, 45);
            if (name.Length == 0 || hp.Length == 0)
                return null;

            return new CrucibleFamiliar((uint)DigitParsers.Digits(Text(uld, 12)))
                   {
                       Rank               = DigitParsers.Digits(Text(uld, 40)),
                       Hp                 = MaxOf(hp),
                       Strength           = DigitParsers.Digits(ComponentText(uld, 47, 3)),
                       PhysicalResistance = DigitParsers.Digits(ComponentText(uld, 48, 3)),
                       Constitution       = DigitParsers.Digits(ComponentText(uld, 49, 3)),
                       Intelligence       = DigitParsers.Digits(ComponentText(uld, 50, 3)),
                       MagicResistance    = DigitParsers.Digits(ComponentText(uld, 51, 3)),
                       Exp                = Text(uld, 42),
                       //Element            = DigitParsers.LeadingGlyphs().Replace(Text(uld, 23), "")
                   };
        }

        public static float ExpShare(string exp)
        {
            TryNumber(exp, out float total);
            return total / 100f;
            
            static bool TryNumber(string text, out float value)
            {
                Match match = DigitParsers.GroupedNumber().Match(text);
                value = 0;
                return match.Success && float.TryParse(match.Value.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            }
        }

        private static string AllText(AtkUldManager* uld)
        {
            List<string> parts = [];
            for (int i = 0; i < uld->NodeListCount; i++)
            {
                AtkResNode* node = uld->NodeList[i];
                if (node == null || !node->IsVisible())
                    continue;

                AtkTextNode* text = node->GetAsAtkTextNode();
                if (text == null)
                    continue;

                string value = text->NodeText.ToString().Trim();
                if (value.Length > 0)
                    parts.Add(value);
            }

            return string.Join(" | ", parts);
        }

        private static string Text(AtkUldManager* uld, uint id, bool visibleOnly = false)
        {
            AtkResNode* node = uld->SearchNodeById(id);
            if (node == null || (visibleOnly && !node->IsVisible()))
                return "";

            AtkTextNode* text = node->GetAsAtkTextNode();
            return text == null ? "" : text->NodeText.ToString().Trim();
        }

        private static string ComponentText(AtkUldManager* uld, uint componentId, uint textId)
        {
            AtkResNode* node = uld->SearchNodeById(componentId);
            if (node == null)
                return "";

            AtkComponentNode* componentNode = node->GetAsAtkComponentNode();
            return componentNode == null || componentNode->Component == null ? "" : Text(&componentNode->Component->UldManager, textId);
        }

        private static uint AsUInt(AtkValue value) =>
            value.Type.ToString() == "UInt" ? value.UInt : (uint)Math.Max(0, value.Int);

        private static int CurrentOf(string fraction)
        {
            int slash = fraction.IndexOf('/');
            return DigitParsers.Digits(slash >= 0 ? fraction[..slash] : fraction);
        }

        private static int MaxOf(string fraction)
        {
            int slash = fraction.LastIndexOf('/');
            return DigitParsers.Digits(slash >= 0 ? fraction[(slash + 1)..] : fraction);
        }

        internal static class Screens
        {
            internal static class PetParty
            {
                private const uint RestOrReturnButton = 21;

                public static int  GetTeamSize(AtkUnitBase*  party)          => (int) new ReaderXBMPetParty(party).TeamSize;
                public static void Pick(AtkUnitBase*         party, int row) => BeastmasterAddon.FireCallBack(party, true, 1, row);
                public static void OpenRowMenu(AtkUnitBase*  party, int row) => BeastmasterAddon.FireCallBack(party, true, 2, row);
                public static void OpenBestiary(AtkUnitBase* party) => BeastmasterAddon.FireCallBack(party, true, 5);
                public static void RestReturn(AtkUnitBase*   party) => BeastmasterAddon.FireCallBack(party, true, 4);
                public static bool Rest(AtkUnitBase*         party) => ClickButton(party, RestOrReturnButton);
                public static bool Return(AtkUnitBase*       party) => ClickButton(party, RestOrReturnButton);

            }

            internal static class StageList
            {
                public static void Highlight(AtkUnitBase* list, uint board) => BeastmasterAddon.FireCallBack(list, true, 2, (int)board);
                public static void Open(AtkUnitBase*      list, uint board) => BeastmasterAddon.FireCallBack(list, true, 1, (int)board);
            }

            internal static class StageDetail
            {
                public static void SetDegree(AtkUnitBase* layout, CrucibleDegree degree) => BeastmasterAddon.FireCallBack(layout, true, 2, (int)degree);
                public static void Confirm(AtkUnitBase*   layout) => BeastmasterAddon.FireCallBack(layout, true, 8);

                public static bool EnsureRankedEntry(AtkUnitBase* layout, ref bool clickSubmitted)
                    => EnsureRankedEntry(layout, ref clickSubmitted, out _);

                public static bool EnsureRankedEntry(AtkUnitBase* layout, ref bool clickSubmitted, out string diagnostic)
                {
                    diagnostic = "Localized ranking label is unavailable.";
                    // Current native Addon row: "Participate in rankings." Resolve its game-language text.
                    if (!Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Addon>().TryGetRow(17568, out var row)) return false;
                    var label = row.Text.ExtractText().Trim();
                    return EnsureRankedEntry(layout, label, ref clickSubmitted, out diagnostic);
                }

                internal static bool EnsureRankedEntry(AtkUnitBase* layout, string label, ref bool clickSubmitted, out string diagnostic)
                {
                    diagnostic = "Ranking window or label is unavailable.";
                    if (label.Length == 0 || layout == null || layout->UldManager.NodeList == null ||
                        layout->UldManager.NodeListCount > 512) return false;
                    AtkComponentCheckBox* checkbox = null;
                    AtkResNode* checkboxNode = null;
                    for (var i = 0; i < layout->UldManager.NodeListCount; ++i)
                    {
                        var node = layout->UldManager.NodeList[i];
                        if (node == null || !node->IsVisible() || (ushort)node->Type < 1000) continue;
                        var component = node->GetAsAtkComponentNode()->Component;
                        if (component == null || component->GetComponentType() != ComponentType.CheckBox) continue;
                        var candidate = (AtkComponentCheckBox*)component;
                        if (candidate->ButtonTextNode == null || candidate->ButtonTextNode->NodeText.ToString().Trim() != label) continue;
                        if (checkbox != null) { diagnostic = "Multiple native checkboxes have the ranking label."; return false; }
                        checkbox = candidate; checkboxNode = node;
                    }
                    if (checkbox == null || !checkbox->IsEnabled)
                    { diagnostic = "Ranking checkbox is missing or disabled."; return false; }
                    if (checkbox->IsChecked)
                    { diagnostic = $"Checkbox {checkboxNode->NodeId} reports checked."; return true; }
                    if (clickSubmitted)
                    { diagnostic = $"Checkbox {checkboxNode->NodeId} remains unchecked after its input request."; return false; }

                    // The checkbox owns the toggle and forwards ButtonClick to its addon. Calling
                    // the addon directly bypasses that input path and does not select rankings.
                    if (!TryFindRankingInput(checkbox, AtkEventType.MouseDown, out var down) ||
                        !TryFindRankingInput(checkbox, AtkEventType.MouseClick, out var click))
                    { diagnostic = $"Checkbox {checkboxNode->NodeId} has no unique component MouseDown/MouseClick bindings."; return false; }
                    var stage = AtkStage.Instance();
                    if (stage == null || stage->NextEventDispatcherIndex >= 32)
                    { diagnostic = "Native UI event dispatch is unavailable."; return false; }
                    clickSubmitted = true;
                    var data = new AtkEventData();
                    data.MouseData.PosX = (short)Math.Clamp(checkboxNode->ScreenX, short.MinValue, short.MaxValue);
                    data.MouseData.PosY = (short)Math.Clamp(checkboxNode->ScreenY, short.MinValue, short.MaxValue);
                    data.MouseData.ButtonId = 0; // Native left mouse button.
                    // Component input forwards ButtonClick only inside an enabled native
                    // dispatch scope. Restore the previous state even if a listener fails.
                    var couldDispatch = stage->CanDispatchEvents;
                    Svc.Log.Information($"[Crewcible] Requesting ranked entry through checkbox {checkboxNode->NodeId} input events {down.Param}/{click.Param}; nativeDispatchWasEnabled={couldDispatch}; awaiting native checked state.");
                    try
                    {
                        stage->CanDispatchEvents = true;
                        down.Listener->ReceiveEvent(AtkEventType.MouseDown, checked((int)down.Param), &down, &data);
                        click.Listener->ReceiveEvent(AtkEventType.MouseClick, checked((int)click.Param), &click, &data);
                    }
                    finally { stage->CanDispatchEvents = couldDispatch; }
                    diagnostic = $"Checkbox {checkboxNode->NodeId} input {down.Param}/{click.Param} submitted; awaiting independent readback.";
                    return false;
                }

                private static bool TryFindRankingInput(AtkComponentCheckBox* checkbox, AtkEventType type, out AtkEvent binding)
                {
                    binding = default;
                    var uld = &checkbox->UldManager;
                    if (uld->NodeList == null || uld->NodeListCount > 64 || checkbox->OwnerNode == null) return false;
                    AtkEvent* selected = null;
                    for (var i = -1; i < uld->NodeListCount; ++i)
                    {
                        var node = i < 0 ? (AtkResNode*)checkbox->OwnerNode : uld->NodeList[i];
                        if (node == null || (i >= 0 && node == (AtkResNode*)checkbox->OwnerNode)) continue;
                        var count = 0;
                        for (var evt = node->AtkEventManager.Event; evt != null; evt = evt->NextEvent)
                        {
                            if (++count > 32) return false;
                            if (evt->State.EventType != type || evt->Listener != (AtkEventListener*)checkbox ||
                                evt->State.StateFlags.HasFlag(AtkEventStateFlags.IsGlobalEvent)) continue;
                            if (selected != null || evt->Param > int.MaxValue) return false;
                            selected = evt;
                        }
                    }
                    if (selected == null) return false;
                    binding = *selected;
                    return true;
                }
            }

            internal static class Notebook
            {
                public static void Close(AtkUnitBase* notebook) => BeastmasterAddon.FireCallBack(notebook, true, 0);

                public static void OpenFilter(AtkUnitBase* notebook) => BeastmasterAddon.FireCallBack(notebook, true, 2);

                private const uint FirstEntryParam = 4;
                public static void ShowPage(AtkUnitBase* notebook, uint page) => BeastmasterAddon.FireCallBack(notebook, true, 3, page);

                public static bool PickEntry(AtkUnitBase* notebook, uint slotOnPage)
                {
                    if (slotOnPage >= ReaderXBMMonsterNotebook.EntryCountPerPage)
                        return false;
                    BeastmasterAddon.FireCallBack(notebook, true, 5, (int)slotOnPage);
                    return ClickEvent(notebook, AtkEventType.MouseDown, FirstEntryParam + slotOnPage);
                }
            }

            internal static class NotebookFilter
            {
                public static void ResetFilters(AtkUnitBase* filter) => BeastmasterAddon.FireCallBack(filter, true, 0u, string.Empty, 0u, 0u, 0u);
            }

            internal static class Menu
            {
                private const int RemoveAllOption = 2;

                public static void ChooseFirst(AtkUnitBase*     menu) => BeastmasterAddon.FireCallBack(menu, true, 0, 0,               0);
                public static void ChooseRemoveAll(AtkUnitBase* menu) => BeastmasterAddon.FireCallBack(menu, true, 0, RemoveAllOption, 0);
                public static void Close(AtkUnitBase*           menu) => BeastmasterAddon.FireCallBack(menu, true, 0, -1,              0);
            }

            internal static class Booty
            {
                public static void Close(AtkUnitBase* loot)            => BeastmasterAddon.FireCallBack(loot, true, 0);
                public static void TakeCoins(AtkUnitBase* loot)            => BeastmasterAddon.FireCallBack(loot, true, 3);
                public static void Take(AtkUnitBase*      loot, int index) => BeastmasterAddon.FireCallBack(loot, true, 4, index);
                public static bool TryReadTakeAll(AtkUnitBase* loot, out bool enabled)
                {
                    enabled = false;
                    if (loot == null || !loot->IsReady || !loot->IsVisible) return false;
                    var node = loot->UldManager.SearchNodeById(46);
                    if (node == null || !node->IsVisible() || (ushort)node->Type < 1000) return false;
                    var component = node->GetAsAtkComponentNode()->Component;
                    if (component == null || component->GetComponentType() != ComponentType.Button) return false;
                    enabled = ((AtkComponentButton*)component)->IsEnabled;
                    return true;
                }
                public static bool TakeAll(AtkUnitBase* loot)
                    => TryReadTakeAll(loot, out var enabled) && enabled &&
                        Services.GameHelpers.TryClickNativeButton(LootWindow, label: null, observedNodeId: 46);
            }

            internal static class Treasure
            {
                public static void Close(AtkUnitBase* treasure) => BeastmasterAddon.FireCallBack(treasure, true, 0);
                public static void Take(AtkUnitBase* treasure, uint nodeId) => BeastmasterAddon.FireCallBack(treasure, true, 2, nodeId);
            }

            internal static class Result
            {
                public static bool Continue(AtkUnitBase* result) => ClickButton(result, 61);
            }

            internal static class ItemShop
            {
                public static void Buy(AtkUnitBase* shop, int index) => BeastmasterAddon.FireCallBack(shop, true, 2, index);

                public static bool Close(AtkUnitBase* shop) => ClickButton(shop, 40);
            }

            internal static class MainHud
            {
                public static void OpenItemMenu(AtkUnitBase* hud, int slot) => BeastmasterAddon.FireCallBack(hud, true, 6, slot);
            }

            internal static class Prompt
            {
                public static void Yes(AtkUnitBase* prompt) => new ECommons.UIHelpers.AddonMasterImplementations.AddonMaster.SelectYesno(prompt).Yes();
                public static void No(AtkUnitBase* prompt) => new ECommons.UIHelpers.AddonMasterImplementations.AddonMaster.SelectYesno(prompt).No();
            }
        }
    }
}
