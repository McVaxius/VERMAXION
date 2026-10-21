using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using AethertekUI;
using Dalamud.Bindings.ImGui;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VERMAXION.Beastmaster;

namespace VERMAXION.Windows;

internal sealed partial class BeastmasterWindow
{
    private string beastsListMessage = "";
    private bool beastsListError;
    private IBeastmasterList? editorRenameTarget;
    private string editorRenameDraft = "";
    private object? editorPositionTarget;
    private int editorPositionDraft;
    private readonly record struct EditorDragPayload(int ListKey, uint EntryId);
    private readonly record struct EditorImportedEntry(uint Id, bool Blocked, bool FightBlocked);

    private partial void DrawBeasts()
    {
        EnsureBeastEditorLists();
        DrawBeastsRefresh();

        var scale = MaterialTheme.Metrics.Scale;
        var width = ImGui.GetContentRegionAvail().X;
        var sideBySide = width >= 1040 * scale;
        var height = Math.Max(340 * scale, ImGui.GetContentRegionAvail().Y);
        var rosterWidth = sideBySide ? (width - ImGui.GetStyle().ItemSpacing.X) * .56f : width;
        using (var roster = MaterialSurface.Card("##BeastsRosterCard", new Vector2(rosterWidth / scale, height / scale), true))
            if (roster.Visible) DrawBeastsRoster();
        if (sideBySide) ImGui.SameLine();
        using (var priority = MaterialSurface.Card("##BeastsPriorityCard", new Vector2(sideBySide ? (width - rosterWidth - ImGui.GetStyle().ItemSpacing.X) / scale : 0, height / scale), true))
            if (priority.Visible) DrawBeastsPriority();
    }

    private void EnsureBeastEditorLists()
    {
        var settings = Settings;
        var changed = false;
        if (settings.BeastTierLists.Count == 0)
        {
            settings.BeastTierLists.Add(BeastTierList.Default());
            changed = true;
        }
        foreach (var list in settings.BeastTierLists)
        {
            var count = list.Count;
            list.Complete(CrucibleTeam.FamiliarSheetDatas.Keys);
            changed |= count != list.Count;
        }
        var index = Math.Clamp(settings.BeastTierListIndex, 0, settings.BeastTierLists.Count - 1);
        changed |= settings.BeastTierListIndex != index;
        settings.BeastTierListIndex = index;
        if (changed) SaveSettings();
    }

    private void DrawBeastsRefresh()
    {
        MaterialSettings.Section(UiText.T("Recorded beasts"));
        UiGui.TextDisabled(UiText.F("Saved data for {0}", DisplayIdentity(viewedCharacterKey)));
        UIConstants.SameLineIfFits("Refresh beasts");
        var refresh = plugin.BeastmasterService;
        var canRefresh = plugin.CanRefreshBeastmaster(out var reason);
        ImGui.BeginDisabled(!ViewingCurrent || !canRefresh);
        if (UIConstants.Button("Refresh beasts##Beastmaster")) plugin.RefreshBeastmaster();
        ImGui.EndDisabled();
        EditorTooltip(UiText.T("Beasts and Supplies are shared for this character.") +
            (!ViewingCurrent ? "\n" + UiText.T("Refresh is available only for the playing character.") :
                reason.Length > 0 ? "\n" + UiText.Beastmaster(reason) : string.Empty));
        if (ViewingCurrent)
            UIConstants.WrappedText(refresh.IsRefreshing ? UIConstants.Blue : UIConstants.Metadata,
                UiText.Beastmaster(refresh.RefreshStatus), translate: false);
        UiGui.TextDisabled(CharacterData.OwnershipObservedAtUtc == DateTime.MinValue ? UiText.T("Ownership not fully observed.") :
            UiText.F("Ownership observed: {0}", CharacterData.OwnershipObservedAtUtc.ToLocalTime().ToString("g")));
        UiGui.TextDisabled(CharacterData.RanksObservedAtUtc == DateTime.MinValue ? UiText.T("Full rank observation unavailable. Unread ranks remain unknown.") :
            UiText.F("Ranks observed: {0}", CharacterData.RanksObservedAtUtc.ToLocalTime().ToString("g")));
    }
    private void DrawBeastsRoster()
    {
        var settings = Settings;
        var owned = RecordedOwned().OrderBy(number => number).ToList();
        var team = settings.CustomTeam;
        var cache = CharacterData.Familiars;
        ImGui.BeginDisabled(EditingLocked || !ImGui.GetIO().KeyCtrl);
        try
        {
            if (UiGui.Button("Clear recorded beast data###BeastsClearRanks"))
            {
                CharacterData.Familiars.Clear();
                CharacterData.Team.Clear();
                CharacterData.Owned.Clear();
                CharacterData.OwnershipObservedAtUtc = DateTime.MinValue;
                CharacterData.RanksObservedAtUtc = DateTime.MinValue;
                SaveSettings();
            }
            EditorTooltip("Clear this character's saved ranks, EXP, stats, ownership observations and last team. Hold Ctrl and click.");
            UIConstants.SameLineIfFits("Clear custom familiars");
            if (UiGui.Button("Clear custom familiars###BeastsClearCustom"))
            {
                settings.CustomTeam.Clear();
                SaveSettings();
            }
            EditorTooltip("Clear the custom team. Hold Ctrl and click.");
        }
        finally { ImGui.EndDisabled(); }
        if (owned.Count == 0)
        {
            UiGui.TextWrapped("No recorded beasts. Use Refresh beasts while playing this character outside duty.");
            return;
        }
        var rows = owned;
        var scale = MaterialTheme.Metrics.Scale;
        var height = Math.Max(160 * scale, ImGui.GetContentRegionAvail().Y - 48 * scale);
        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.BordersOuter |
            ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY | ImGuiTableFlags.ScrollX | ImGuiTableFlags.SizingFixedFit;
        if (ImGui.BeginTable("##BeastsRoster", 7, flags, new Vector2(0, height), 660 * scale))
        {
            try
            {
                ImGui.TableSetupScrollFreeze(2, 1);
                ImGui.TableSetupColumn("##Pick", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight());
                ImGui.TableSetupColumn("No.", ImGuiTableColumnFlags.WidthFixed, EditorColumnWidth("No.", 34));
                ImGui.TableSetupColumn("Familiar", ImGuiTableColumnFlags.WidthStretch, 1);
                ImGui.TableSetupColumn("Classification", ImGuiTableColumnFlags.WidthFixed, EditorColumnWidth("Classification", 108));
                ImGui.TableSetupColumn("Rank", ImGuiTableColumnFlags.WidthFixed, EditorColumnWidth("Rank", 44));
                ImGui.TableSetupColumn("EXP", ImGuiTableColumnFlags.WidthFixed, 92 * scale);
                ImGui.TableSetupColumn("HP", ImGuiTableColumnFlags.WidthFixed, 70 * scale);
                UiGui.TableHeadersRow();
                foreach (var number in rows)
                {
                    cache.TryGetValue(number, out var familiar);
                    var picked = team.Contains(number);
                    ImGui.PushID("BeastRoster" + number);
                    try
                    {
                        ImGui.TableNextRow();
                        if (picked) ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1, ImGui.ColorConvertFloat4ToU32(MaterialTheme.Current.Colors.PrimaryContainer));
                        ImGui.TableSetColumnIndex(0);
                        var full = !picked && team.Count >= 15;
                        ImGui.BeginDisabled(EditingLocked || full);
                        try
                        {
                            if (ImGui.Checkbox("##Selected", ref picked))
                            {
                                if (picked && owned.Contains(number)) settings.CustomTeam.Add(number);
                                else settings.CustomTeam.RemoveAll(id => id == number);
                                SaveSettings();
                                team = settings.CustomTeam;
                            }
                        }
                        finally { ImGui.EndDisabled(); }
                        if (full) EditorTooltip("The team is full. Clear a selection to choose another familiar.");
                        ImGui.TableSetColumnIndex(1);
                        MaterialText.Text(number.ToString(CultureInfo.InvariantCulture));
                        ImGui.TableSetColumnIndex(2);
                        MaterialText.Text(CrucibleTeam.NameOf(number));
                        if (ImGui.IsItemHovered()) DrawBeastsStatsTooltip(number, familiar);
                        ImGui.TableSetColumnIndex(3);
                        MaterialText.Text(CrucibleTeam.FamiliarSheetDatas.TryGetValue(number, out var sheet) ? sheet.ClassificationName : "");
                        ImGui.TableSetColumnIndex(4);
                        MaterialText.Text(familiar is { Rank: > 0 } ? familiar.Rank.ToString(CultureInfo.InvariantCulture) : "—");
                        ImGui.TableSetColumnIndex(5);
                        if (familiar is { Exp.Length: > 0 }) ImGui.ProgressBar(CrucibleUi.ExpShare(familiar.Exp), new Vector2(-1, ImGui.GetFrameHeight()), familiar.Exp);
                        else MaterialText.Text("—");
                        ImGui.TableSetColumnIndex(6);
                        MaterialText.Text(familiar is { Hp: > 0 } ? familiar.Hp.ToString(CultureInfo.InvariantCulture) : "—");
                    }
                    finally { ImGui.PopID(); }
                }
            }
            finally { ImGui.EndTable(); }
        }
        UiGui.TextDisabled("Custom membership is used by any setup that selects Custom. Hover a name for recorded stats.");
    }

    private static void DrawBeastsStatsTooltip(uint number, CrucibleFamiliar? familiar)
    {
        ImGui.BeginTooltip();
        try
        {
            MaterialText.Text(CrucibleTeam.NameOf(number));
            if (familiar is not { Rank: > 0 }) UiGui.TextUnformatted("Rank not read yet. Read from the bestiary at the next team setup.");
            else
            {
                UiGui.TextUnformatted(UiText.F("Strength: {0}", familiar.Strength));
                UiGui.TextUnformatted(UiText.F("Intelligence: {0}", familiar.Intelligence));
                UiGui.TextUnformatted(UiText.F("Constitution: {0}", familiar.Constitution));
                UiGui.TextUnformatted(UiText.F("Physical resistance: {0}", familiar.PhysicalResistance));
                UiGui.TextUnformatted(UiText.F("Magic resistance: {0}", familiar.MagicResistance));
            }
        }
        finally { ImGui.EndTooltip(); }
    }

    private void DrawBeastsPriority()
    {
        MaterialSettings.Section(UiText.T("Beast priority list"));
        var settings = Settings;
        var active = settings.BeastTierLists[settings.BeastTierListIndex];
        ImGui.BeginDisabled(EditingLocked);
        try
        {
            ImGui.SetNextItemWidth(EditorListComboWidth(out var inlineActions));
            if (MaterialText.BeginCombo("##BeastsList", active.Name))
            {
                try
                {
                    for (var index = 0; index < settings.BeastTierLists.Count; index++)
                    {
                        var list = settings.BeastTierLists[index];
                        if (UiGui.Selectable("###BeastsList" + RuntimeHelpers.GetHashCode(list), index == settings.BeastTierListIndex, list.Name))
                        {
                            settings.BeastTierListIndex = index;
                            SaveSettings();
                        }
                    }
                    ImGui.Separator();
                    UiGui.TextDisabled(UiText.T("Premade lists"));
                    foreach (var name in BeastTierList.PremadeNames)
                    {
                        if (!UiGui.Selectable("###BeastsPremade" + name, false, UiText.T(name))) continue;
                        var list = BeastTierList.CreatePremade(name);
                        list.Name = EditorUniqueName(settings.BeastTierLists, UiText.T(name));
                        settings.BeastTierLists.Add(list);
                        settings.BeastTierListIndex = settings.BeastTierLists.Count - 1;
                        SaveSettings();
                        break;
                    }
                }
                finally { ImGui.EndCombo(); }
            }
            if (inlineActions) ImGui.SameLine();
            if (UiGui.Button("New###BeastsNew"))
            {
                var list = BeastTierList.Default();
                list.Name = EditorUniqueName(settings.BeastTierLists, UiText.T("Tier List"));
                list.Complete(CrucibleTeam.FamiliarSheetDatas.Keys);
                settings.BeastTierLists.Add(list);
                settings.BeastTierListIndex = settings.BeastTierLists.Count - 1;
                SaveSettings();
            }
            ImGui.SameLine();
            if (UiGui.Button("Copy###BeastsCopy"))
            {
                settings.BeastTierLists.Add(active.Clone(EditorUniqueName(settings.BeastTierLists, active.Name)));
                settings.BeastTierListIndex = settings.BeastTierLists.Count - 1;
                SaveSettings();
            }
            ImGui.SameLine();
            if (UiGui.Button("Rename###BeastsRename")) EditorBeginRename(active, "##BeastsRenamePopup");
            ImGui.SameLine();
            ImGui.BeginDisabled(!ImGui.GetIO().KeyCtrl || settings.BeastTierLists.Count <= 1);
            try
            {
                if (UiGui.Button("Delete###BeastsDelete"))
                {
                    settings.BeastTierLists.Remove(active);
                    settings.BeastTierListIndex = Math.Clamp(settings.BeastTierListIndex, 0, settings.BeastTierLists.Count - 1);
                    SaveSettings();
                }
            }
            finally { ImGui.EndDisabled(); }
            EditorTooltip("Delete this list. Hold Ctrl and click. The last list cannot be deleted.");
            EditorRenamePopup(settings.BeastTierLists, "##BeastsRenamePopup");
        }
        finally { ImGui.EndDisabled(); }
        active = settings.BeastTierLists[settings.BeastTierListIndex];
        UiGui.TextDisabled("Drag the handle or type a new # to reorder. Ctrl-click a block to change its classification.");
        DrawBeastsPriorityRows(active);
        ImGui.BeginDisabled(EditingLocked);
        try
        {
            ImGui.BeginDisabled(!ImGui.GetIO().KeyCtrl);
            try
            {
                if (UiGui.Button("Clear order###BeastsReset"))
                {
                    active.Clear();
                    active.Complete(CrucibleTeam.FamiliarSheetDatas.Keys);
                    SaveSettings();
                }
            }
            finally { ImGui.EndDisabled(); }
            EditorTooltip("Restore this list's default order and clear its blocks. Hold Ctrl and click.");
            ImGui.SameLine();
            if (UiGui.Button("Import###BeastsImport")) ImportBeastsEditorList();
        }
        finally { ImGui.EndDisabled(); }
        ImGui.SameLine();
        if (UiGui.Button("Export###BeastsExport"))
        {
            ImGui.SetClipboardText(JsonConvert.SerializeObject(new { active.Name, Entries = active.Select(e => new { e.ID, e.PickBlock, e.BattleBlock }) }));
            beastsListMessage = "Beast tier list copied to clipboard.";
            beastsListError = false;
        }
        if (beastsListMessage.Length > 0) UiGui.TextColored(beastsListError ? new Vector4(1, .4f, .4f, 1) : MaterialTheme.Current.Colors.OnSurfaceVariant, beastsListMessage);
    }

    private void DrawBeastsPriorityRows(BeastTierList active)
    {
        var scale = MaterialTheme.Metrics.Scale;
        var height = Math.Max(150 * scale, ImGui.GetContentRegionAvail().Y - 100 * scale);
        var moveFrom = -1;
        var moveTo = -1;
        using var controls = MaterialTable.PushControls();
        var blockColumnWidth = Math.Max(ImGui.GetFrameHeight(), Math.Max(MaterialText.Measure("E").X, MaterialText.Measure("R").X));
        const string excludeTooltip = "Pick Blocked prevents automatic team selection. Ctrl-click to change the whole classification.";
        const string reserveTooltip = "Fight Blocked keeps this familiar in reserve unless fewer than three unblocked fighters survive. Ctrl-click to change the whole classification.";
        if (!ImGui.BeginTable("##BeastsPriorityRows", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingFixedFit, new Vector2(0, height), 490 * scale)) return;
        try
        {
            ImGui.TableSetupScrollFreeze(2, 1);
            ImGui.TableSetupColumn("##Drag", ImGuiTableColumnFlags.WidthFixed, 28 * scale);
            ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, 50 * scale);
            ImGui.TableSetupColumn("Familiar", ImGuiTableColumnFlags.WidthStretch, 1);
            ImGui.TableSetupColumn("Pick Blocked", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoHeaderWidth, blockColumnWidth);
            ImGui.TableSetupColumn("Fight Blocked", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoHeaderWidth, blockColumnWidth);
            string[] headers = ["", "#", UiText.T("Familiar"), "E", "R"];
            var headerHeight = Math.Max(ImGui.GetTextLineHeight(), headers.Max(caption => MaterialText.Measure(caption).Y)) + 2 * ImGui.GetStyle().CellPadding.Y;
            ImGui.TableNextRow(ImGuiTableRowFlags.Headers, headerHeight);
            for (var column = 0; column < headers.Length; column++)
            {
                if (!ImGui.TableSetColumnIndex(column)) continue;
                var original = ImGui.TableGetColumnName(column);
                var position = ImGui.GetCursorScreenPos();
                var available = ImGui.GetContentRegionAvail().X;
                var drawList = ImGui.GetWindowDrawList();
                var color = ImGui.GetColorU32(ImGuiCol.Text);
                ImGui.PushStyleColor(ImGuiCol.Text, Vector4.Zero);
                try { ImGui.TableHeader(original); }
                finally { ImGui.PopStyleColor(); }
                MaterialText.AddText(drawList, position, color, headers[column]);
                if (column == 3) EditorTooltip(UiText.T("Pick Blocked") + "\n" + UiText.T(excludeTooltip));
                else if (column == 4) EditorTooltip(UiText.T("Fight Blocked") + "\n" + UiText.T(reserveTooltip));
                else if (headers[column] != original && MaterialText.Measure(headers[column]).X > available - 16 * scale)
                    EditorTooltip(headers[column]);
            }
            for (var index = 0; index < active.Count; index++)
            {
                var row = active[index];
                ImGui.PushID("BeastPriority" + row.ID);
                try
                {
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.BeginDisabled(EditingLocked);
                    try
                    {
                        UiGui.SmallButton("::###Drag");
                        var payload = new EditorDragPayload(RuntimeHelpers.GetHashCode(active), row.ID);
                        if (!EditingLocked)
                        {
                            MaterialDragDrop.Source("VMX_BEAST_ORDER", in payload, CrucibleTeam.NameOf(row.ID));
                            if (MaterialDragDrop.TryAccept<EditorDragPayload>("VMX_BEAST_ORDER", out var source) && source.ListKey == payload.ListKey)
                            {
                                moveFrom = active.FindIndex(e => e.ID == source.EntryId);
                                moveTo = index;
                            }
                        }
                        ImGui.TableSetColumnIndex(1);
                        if (EditorPosition(row, index, active.Count, out var position)) { moveFrom = index; moveTo = position; }
                    }
                    finally { ImGui.EndDisabled(); }
                    ImGui.TableSetColumnIndex(2);
                    MaterialText.Text(CrucibleTeam.NameOf(row.ID));
                    if (ImGui.IsItemHovered())
                        DrawBeastsStatsTooltip(row.ID, CharacterData.Familiars.GetValueOrDefault(row.ID));
                    ImGui.TableSetColumnIndex(3);
                    ImGui.BeginDisabled(EditingLocked);
                    try
                    {
                        var pick = row.PickBlock;
                        if (ImGui.Checkbox("##PickBlock", ref pick)) SetBeastsBlock(active, row, pick, false);
                        EditorTooltip(excludeTooltip);
                        ImGui.TableSetColumnIndex(4);
                        var fight = row.BattleBlock;
                        if (ImGui.Checkbox("##FightBlock", ref fight)) SetBeastsBlock(active, row, fight, true);
                        EditorTooltip(reserveTooltip);
                    }
                    finally { ImGui.EndDisabled(); }
                }
                finally { ImGui.PopID(); }
            }
        }
        finally { ImGui.EndTable(); }
        if (moveFrom >= 0 && moveTo >= 0 && moveFrom != moveTo && !EditingLocked)
        {
            var row = active[moveFrom];
            active.RemoveAt(moveFrom);
            active.Insert(moveTo, row);
            SaveSettings();
        }
    }

    private void SetBeastsBlock(BeastTierList active, BeastTierList.Entry row, bool blocked, bool fight)
    {
        var group = CrucibleTeam.FamiliarSheetDatas.GetValueOrDefault(row.ID)?.Classification;
        foreach (var entry in active)
            if (ReferenceEquals(row, entry) || ImGui.GetIO().KeyCtrl && group.HasValue && CrucibleTeam.FamiliarSheetDatas.GetValueOrDefault(entry.ID)?.Classification == group)
            {
                if (fight) entry.BattleBlock = blocked;
                else entry.PickBlock = blocked;
            }
        SaveSettings();
    }

    private void ImportBeastsEditorList()
    {
        try
        {
            var catalog = CrucibleTeam.FamiliarSheetDatas.Keys.ToHashSet();
            var entries = EditorParseClipboard("Entries", "PickBlock", "BattleBlock", catalog, out var name);
            var list = new BeastTierList { Name = EditorUniqueName(Settings.BeastTierLists, name) };
            list.AddRange(entries.Select(e => new BeastTierList.Entry { ID = e.Id, PickBlock = e.Blocked, BattleBlock = e.FightBlocked }));
            list.Complete(catalog);
            Settings.BeastTierLists.Add(list);
            Settings.BeastTierListIndex = Settings.BeastTierLists.Count - 1;
            SaveSettings();
            beastsListMessage = "Beast tier list imported.";
            beastsListError = false;
        }
        catch (Exception)
        {
            beastsListMessage = "Unable to import the beast tier list. Use an exported list or known familiar IDs separated by spaces or commas.";
            beastsListError = true;
        }
    }

    private static float EditorColumnWidth(string caption, float minimum) =>
        Math.Max(minimum * MaterialTheme.Metrics.Scale, MaterialText.Measure(UiText.T(caption)).X + 2 * ImGui.GetStyle().CellPadding.X + 12 * MaterialTheme.Metrics.Scale);

    private static float EditorListComboWidth(out bool inlineActions)
    {
        var available = ImGui.GetContentRegionAvail().X;
        var actions = new[] { "New", "Copy", "Rename", "Delete" }.Sum(label => MaterialText.Measure(UiText.T(label)).X + 2 * ImGui.GetStyle().FramePadding.X + ImGui.GetStyle().ItemSpacing.X);
        inlineActions = available >= actions + 150 * MaterialTheme.Metrics.Scale;
        return inlineActions ? available - actions : available;
    }

    private static void EditorTooltip(string text)
    {
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) UiGui.SetTooltip(text);
    }

    private static string EditorUniqueName<T>(IEnumerable<T> lists, string proposed, IBeastmasterList? except = null) where T : IBeastmasterList
    {
        proposed = proposed.Trim();
        if (proposed.Length == 0) proposed = UiText.T("Untitled");
        var name = proposed;
        for (var suffix = 2; lists.Any(list => !ReferenceEquals(list, except) && string.Equals(list.Name, name, StringComparison.OrdinalIgnoreCase)); suffix++)
            name = proposed + " (" + suffix.ToString(CultureInfo.InvariantCulture) + ")";
        return name;
    }

    private void EditorBeginRename(IBeastmasterList target, string popup)
    {
        editorRenameTarget = target;
        editorRenameDraft = target.Name;
        ImGui.OpenPopup(popup);
    }

    private void EditorRenamePopup<T>(IEnumerable<T> lists, string popup) where T : IBeastmasterList
    {
        ImGui.SetNextWindowSize(new Vector2(Math.Min(360 * MaterialTheme.Metrics.Scale, ImGui.GetWindowWidth()), 0), ImGuiCond.Always);
        if (!ImGui.BeginPopup(popup)) return;
        try
        {
            UiGui.TextUnformatted("List name");
            ImGui.SetNextItemWidth(-1);
            UiGui.InputTextWithHint("##Name", "List name", ref editorRenameDraft, 128);
            if (UiGui.Button("Save###RenameSave") && editorRenameTarget is not null)
            {
                editorRenameTarget.Name = EditorUniqueName(lists, editorRenameDraft, editorRenameTarget);
                SaveSettings();
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (UiGui.Button("Cancel###RenameCancel")) ImGui.CloseCurrentPopup();
        }
        finally { ImGui.EndPopup(); }
    }

    private bool EditorPosition(object row, int index, int count, out int position)
    {
        var value = ReferenceEquals(editorPositionTarget, row) ? editorPositionDraft : index + 1;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputInt("##Position", ref value, 0, 0))
        {
            editorPositionTarget = row;
            editorPositionDraft = value;
        }
        position = index;
        if (!ImGui.IsItemDeactivatedAfterEdit() || !ReferenceEquals(editorPositionTarget, row)) return false;
        position = Math.Clamp(editorPositionDraft, 1, count) - 1;
        editorPositionTarget = null;
        return position != index;
    }

    private static List<EditorImportedEntry> EditorParseClipboard(string collection, string firstFlag, string? secondFlag, ISet<uint> catalog, out string name)
    {
        var text = ImGui.GetClipboardText().Trim();
        if (text.Length == 0 || text.Length > 500_000) throw new FormatException();
        name = UiText.T("Imported");
        JArray entries;
        if (text[0] is '{' or '[')
        {
            var token = JToken.Parse(text);
            if (token is JObject root)
            {
                if (root["Name"] is { } nameToken)
                {
                    if (nameToken.Type != JTokenType.String) throw new FormatException();
                    name = nameToken.Value<string>()!;
                }
                entries = root[collection] as JArray ?? throw new FormatException();
            }
            else entries = token as JArray ?? throw new FormatException();
        }
        else
        {
            entries = new JArray();
            foreach (var part in Regex.Split(text, @"[\s,;]+"))
            {
                if (!uint.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var id)) throw new FormatException();
                entries.Add(id);
            }
        }
        if (entries.Count == 0 || entries.Count > catalog.Count) throw new FormatException();
        var result = new List<EditorImportedEntry>();
        var seen = new HashSet<uint>();
        foreach (var entry in entries)
        {
            var idToken = entry is JObject item ? item["ID"] : entry;
            if (idToken?.Type != JTokenType.Integer || !uint.TryParse(idToken.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var id) || !catalog.Contains(id) || !seen.Add(id)) throw new FormatException();
            result.Add(new EditorImportedEntry(id, ReadFlag(entry, firstFlag), secondFlag is not null && ReadFlag(entry, secondFlag)));
        }
        return result;

        static bool ReadFlag(JToken token, string flag)
        {
            if (token is not JObject item || item[flag] is not { } value) return false;
            if (value.Type != JTokenType.Boolean) throw new FormatException();
            return value.Value<bool>();
        }
    }
}
