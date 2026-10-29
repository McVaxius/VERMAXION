using System.Numerics;
using System.Runtime.CompilerServices;
using AethertekUI;
using Dalamud.Bindings.ImGui;
using Newtonsoft.Json;
using VERMAXION.Beastmaster;
using VERMAXION.Services;

namespace VERMAXION.Windows;

internal sealed partial class BeastmasterWindow
{
    private string suppliesListMessage = "";
    private bool suppliesListError;
    private CrucibleItemData.CrucibleItemCategory suppliesBulkCategory;

    private partial void DrawSupplies()
    {
        EnsureSuppliesEditorLists();
        var scale = MaterialTheme.Metrics.Scale;
        var width = ImGui.GetContentRegionAvail().X;
        var sideBySide = width >= 990 * scale;
        var availableHeight = ImGui.GetContentRegionAvail().Y;
        var height = sideBySide ? availableHeight
            : Math.Max(scale, (availableHeight - ImGui.GetStyle().ItemSpacing.Y) / 2);
        var listWidth = sideBySide ? (width - ImGui.GetStyle().ItemSpacing.X) * .60f : width;
        using (var list = MaterialSurface.Card("##SuppliesPriorityCard", new Vector2(listWidth / scale, height / scale), true))
            if (list.Visible) DrawSuppliesPriority();
        if (sideBySide) ImGui.SameLine();
        using (var guidance = MaterialSurface.Card("##SuppliesGuidanceCard", new Vector2(sideBySide ? (width - listWidth - ImGui.GetStyle().ItemSpacing.X) / scale : 0, height / scale), true))
            if (guidance.Visible) DrawSuppliesGuidance();
    }

    private void EnsureSuppliesEditorLists()
    {
        var settings = Settings;
        var changed = false;
        if (settings.ShopLists.Count == 0)
        {
            settings.ShopLists.Add(new BeastmasterShopList
            {
                Order = settings.ShopGearOrder.Select(id => new BeastmasterShopList.Entry { ID = id }).ToList(),
            }.CompleteList());
            settings.ShopGearOrder.Clear();
            changed = true;
        }
        foreach (var list in settings.ShopLists)
        {
            var count = list.Order.Count;
            list.CompleteList();
            changed |= count != list.Order.Count;
        }
        var index = Math.Clamp(settings.ShopListIndex, 0, settings.ShopLists.Count - 1);
        changed |= settings.ShopListIndex != index;
        settings.ShopListIndex = index;
        if (changed) SaveSettings();
    }

    private void DrawSuppliesPriority()
    {
        MaterialSettings.Section(UiText.T("Item priorities"));
        UiGui.TextUnformatted("Shared by this character's manual and scheduled runs.");
        UiGui.TextDisabled("This order and these blocks apply to buying, treasure choices and item use.");
        var settings = Settings;
        var active = settings.ShopLists[settings.ShopListIndex];
        ImGui.BeginDisabled(EditingLocked);
        try
        {
            ImGui.SetNextItemWidth(EditorListComboWidth(out var inlineActions));
            bool listOpen;
            using (MaterialSkin.NativeFieldStyle())
            using (MaterialSkin.BackdropStyle(popup: true))
                listOpen = MaterialText.BeginCombo("##SuppliesList", active.Name);
            if (listOpen)
            {
                try
                {
                    MaterialSkin.Backdrop();
                    for (var index = 0; index < settings.ShopLists.Count; index++)
                    {
                        var list = settings.ShopLists[index];
                        if (UiGui.Selectable("###SuppliesList" + RuntimeHelpers.GetHashCode(list), index == settings.ShopListIndex, list.Name))
                        {
                            settings.ShopListIndex = index;
                            SaveSettings();
                        }
                    }
                }
                finally { ImGui.EndCombo(); }
            }
            if (inlineActions) ImGui.SameLine();
            if (UiGui.Button("New###SuppliesNew"))
            {
                settings.ShopLists.Add(new BeastmasterShopList { Name = EditorUniqueName(settings.ShopLists, UiText.T("Shop List")) }.CompleteList());
                settings.ShopListIndex = settings.ShopLists.Count - 1;
                SaveSettings();
            }
            ImGui.SameLine();
            if (UiGui.Button("Copy###SuppliesCopy"))
            {
                settings.ShopLists.Add(active.Clone(EditorUniqueName(settings.ShopLists, active.Name)));
                settings.ShopListIndex = settings.ShopLists.Count - 1;
                SaveSettings();
            }
            ImGui.SameLine();
            if (UiGui.Button("Rename###SuppliesRename")) EditorBeginRename(active, "##SuppliesRenamePopup");
            ImGui.SameLine();
            ImGui.BeginDisabled(!ImGui.GetIO().KeyCtrl || settings.ShopLists.Count <= 1);
            try
            {
                if (UiGui.Button("Delete###SuppliesDelete"))
                {
                    settings.ShopLists.Remove(active);
                    settings.ShopListIndex = Math.Clamp(settings.ShopListIndex, 0, settings.ShopLists.Count - 1);
                    SaveSettings();
                }
            }
            finally { ImGui.EndDisabled(); }
            EditorTooltip("Delete this list. Hold Ctrl and click. The last list cannot be deleted.");
            EditorRenamePopup(settings.ShopLists, "##SuppliesRenamePopup");
        }
        finally { ImGui.EndDisabled(); }
        active = settings.ShopLists[settings.ShopListIndex];
        UiGui.TextDisabled("Drag the handle or type a new # to reorder. Ctrl-click Block to change its category.");
        DrawSuppliesBulkControls(active);
        DrawSuppliesPriorityRows(active);
        ImGui.BeginDisabled(EditingLocked);
        try
        {
            ImGui.BeginDisabled(!ImGui.GetIO().KeyCtrl);
            try
            {
                if (UiGui.Button("Clear order###SuppliesReset"))
                {
                    active.Order.Clear();
                    active.CompleteList();
                    SaveSettings();
                }
            }
            finally { ImGui.EndDisabled(); }
            EditorTooltip("Restore this list's default order and clear its blocks. Hold Ctrl and click.");
            ImGui.SameLine();
            if (UiGui.Button("Import###SuppliesImport")) ImportSuppliesEditorList();
        }
        finally { ImGui.EndDisabled(); }
        ImGui.SameLine();
        if (UiGui.Button("Export###SuppliesExport"))
        {
            ImGui.SetClipboardText(JsonConvert.SerializeObject(new { active.Name, Order = active.Order.Select(e => new { e.ID, e.Blocked }) }));
            suppliesListMessage = "Shop list copied to clipboard.";
            suppliesListError = false;
        }
        EditorTooltip("Import adds a new list from exported JSON or known item IDs. Missing catalog entries are appended. Export copies this list to the clipboard.");
        if (suppliesListMessage.Length > 0) UiGui.TextColored(suppliesListError ? new Vector4(1, .4f, .4f, 1) : MaterialTheme.Current.Colors.OnSurfaceVariant, suppliesListMessage);
    }

    private void DrawSuppliesBulkControls(BeastmasterShopList active)
    {
        ImGui.BeginDisabled(EditingLocked);
        try
        {
            UiGui.TextUnformatted("Bulk category");
            ImGui.SetNextItemWidth(Math.Min(ImGui.GetContentRegionAvail().X, 260 * MaterialTheme.Metrics.Scale));
            bool categoryOpen;
            using (MaterialSkin.NativeFieldStyle())
            using (MaterialSkin.BackdropStyle(popup: true))
                categoryOpen = MaterialText.BeginCombo("##SuppliesBulkCategory", UiText.T(suppliesBulkCategory == CrucibleItemData.CrucibleItemCategory.None ? "All categories" : SuppliesCategoryCaption(suppliesBulkCategory)));
            if (categoryOpen)
            {
                try
                {
                    MaterialSkin.Backdrop();
                    CrucibleItemData.CrucibleItemCategory[] categories = [CrucibleItemData.CrucibleItemCategory.None,
                        CrucibleItemData.CrucibleItemCategory.Gear, CrucibleItemData.CrucibleItemCategory.HealItem,
                        CrucibleItemData.CrucibleItemCategory.DamageItem, CrucibleItemData.CrucibleItemCategory.OtherItem,
                        CrucibleItemData.CrucibleItemCategory.Feed];
                    foreach (var category in categories)
                        if (UiGui.Selectable("###SuppliesCategory" + (int)category, suppliesBulkCategory == category, UiText.T(category == CrucibleItemData.CrucibleItemCategory.None ? "All categories" : SuppliesCategoryCaption(category)))) suppliesBulkCategory = category;
                }
                finally { ImGui.EndCombo(); }
            }
            if (UiGui.Button("Block category###SuppliesBlockCategory")) SetSuppliesCategoryBlock(active, true);
            ImGui.SameLine();
            if (UiGui.Button("Allow category###SuppliesAllowCategory")) SetSuppliesCategoryBlock(active, false);
        }
        finally { ImGui.EndDisabled(); }
    }

    private void SetSuppliesCategoryBlock(BeastmasterShopList active, bool blocked)
    {
        foreach (var entry in active.Order)
            if (suppliesBulkCategory == CrucibleItemData.CrucibleItemCategory.None || entry.GetCategory == suppliesBulkCategory) entry.Blocked = blocked;
        SaveSettings();
    }

    private void DrawSuppliesPriorityRows(BeastmasterShopList active)
    {
        var scale = MaterialTheme.Metrics.Scale;
        var height = Math.Max(145 * scale, ImGui.GetContentRegionAvail().Y - 105 * scale);
        var moveFrom = -1;
        var moveTo = -1;
        using var controls = MaterialTable.PushControls();
        if (!ImGui.BeginTable("##SuppliesPriorityRows", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingFixedFit, new Vector2(0, height), 535 * scale)) return;
        try
        {
            ImGui.TableSetupScrollFreeze(2, 1);
            ImGui.TableSetupColumn("##Drag", ImGuiTableColumnFlags.WidthFixed, 28 * scale);
            ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, 50 * scale);
            ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch, 1);
            ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthFixed, EditorColumnWidth("Type", 95));
            ImGui.TableSetupColumn("Block", ImGuiTableColumnFlags.WidthFixed, EditorColumnWidth("Block", 62));
            UiGui.TableHeadersRow();
            for (var index = 0; index < active.Order.Count; index++)
            {
                var row = active.Order[index];
                ImGui.PushID("SupplyPriority" + row.ID);
                try
                {
                    ImGui.TableNextRow();
                    if (row.Blocked) ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1, ImGui.ColorConvertFloat4ToU32(new Vector4(.8f, .15f, .15f, .18f)));
                    ImGui.TableSetColumnIndex(0);
                    ImGui.BeginDisabled(EditingLocked);
                    try
                    {
                        UiGui.SmallButton("::###Drag");
                        var payload = new EditorDragPayload(RuntimeHelpers.GetHashCode(active), row.ID);
                        if (!EditingLocked)
                        {
                            MaterialDragDrop.Source("VMX_SUPPLY_ORDER", in payload, CrucibleItemData.NameOf(row.ID));
                            if (MaterialDragDrop.TryAccept<EditorDragPayload>("VMX_SUPPLY_ORDER", out var source) && source.ListKey == payload.ListKey)
                            {
                                moveFrom = active.Order.FindIndex(e => e.ID == source.EntryId);
                                moveTo = index;
                            }
                        }
                        ImGui.TableSetColumnIndex(1);
                        if (EditorPosition(row, index, active.Order.Count, out var position)) { moveFrom = index; moveTo = position; }
                    }
                    finally { ImGui.EndDisabled(); }
                    ImGui.TableSetColumnIndex(2);
                    if (CrucibleItemData.InfoOf(row.ID) is { Icon: > 0 } iconInfo)
                    {
                        var icon = Plugin.TextureProvider.GetFromGameIcon(iconInfo.Icon).GetWrapOrEmpty();
                        if (icon.Handle.Handle != 0)
                        {
                            ImGui.Image(icon.Handle, new Vector2(ImGui.GetFrameHeight()));
                            ImGui.SameLine();
                        }
                    }
                    MaterialText.Text(CrucibleItemData.NameOf(row.ID));
                    if (ImGui.IsItemHovered()) DrawSuppliesItemTooltip(row.ID);
                    ImGui.TableSetColumnIndex(3);
                    UiGui.TextUnformatted(SuppliesCategoryCaption(row.GetCategory));
                    ImGui.TableSetColumnIndex(4);
                    ImGui.BeginDisabled(EditingLocked);
                    try
                    {
                        var blocked = row.Blocked;
                        if (UiGui.Checkbox("##Blocked", ref blocked))
                        {
                            foreach (var entry in active.Order)
                                if (ReferenceEquals(entry, row) || ImGui.GetIO().KeyCtrl && entry.GetCategory == row.GetCategory) entry.Blocked = blocked;
                            SaveSettings();
                        }
                        EditorTooltip("Blocked items are not bought, chosen as treasure, or used. Ctrl-click to change the whole category.");
                    }
                    finally { ImGui.EndDisabled(); }
                }
                finally { ImGui.PopID(); }
            }
        }
        finally { ImGui.EndTable(); }
        if (moveFrom >= 0 && moveTo >= 0 && moveFrom != moveTo && !EditingLocked)
        {
            var row = active.Order[moveFrom];
            active.Order.RemoveAt(moveFrom);
            active.Order.Insert(moveTo, row);
            SaveSettings();
        }
    }

    private static string SuppliesCategoryCaption(CrucibleItemData.CrucibleItemCategory category) => category switch
    {
        CrucibleItemData.CrucibleItemCategory.Gear => "Gear",
        CrucibleItemData.CrucibleItemCategory.HealItem => "Healing",
        CrucibleItemData.CrucibleItemCategory.DamageItem => "Damage",
        CrucibleItemData.CrucibleItemCategory.OtherItem => "Item",
        CrucibleItemData.CrucibleItemCategory.Feed => "Feed",
        _ => "Item",
    };

    private static void DrawSuppliesItemTooltip(uint id)
    {
        var info = CrucibleItemData.InfoOf(id);
        if (info is null) return;
        using (MaterialSkin.BackdropStyle(popup: true)) ImGui.BeginTooltip();
        try
        {
            MaterialSkin.Backdrop();
            ImGui.PushTextWrapPos(420 * MaterialTheme.Metrics.Scale);
            try
            {
                MaterialText.Text(info.Value.Name);
                if (info.Value.Type.Length > 0) MaterialText.Text(info.Value.Type);
                if (info.Value.Summary.Length > 0) MaterialText.Text(info.Value.Summary);
                if (info.Value.Description.Length > 0) MaterialText.Text(info.Value.Description);
            }
            finally { ImGui.PopTextWrapPos(); }
        }
        finally { ImGui.EndTooltip(); }
    }

    private void DrawSuppliesGuidance()
    {
        MaterialSettings.Section(UiText.T("Item and camp rules"));
        var settings = Settings;
        ImGui.BeginDisabled(EditingLocked);
        try
        {
            DrawFoodAndExtraction();
            ImGui.Separator();
            var shop = settings.Shop;
            if (UiGui.Checkbox("Shop###SuppliesShop", ref shop)) { settings.Shop = shop; SaveSettings(); }
            var rest = settings.Rest;
            if (UiGui.Checkbox("Rest at camps###SuppliesRest", ref rest)) { settings.Rest = rest; SaveSettings(); }
            var items = settings.Items;
            if (UiGui.Checkbox("Heal items###SuppliesItems", ref items)) { settings.Items = items; SaveSettings(); }
            ImGui.Separator();
            var requirements = settings.RespectGearRequirements;
            if (UiGui.Checkbox("###SuppliesGearRequirements", ref requirements)) { settings.RespectGearRequirements = requirements; SaveSettings(); }
            ImGui.SameLine();
            UiGui.TextWrapped("Avoid harmful gear without immunity");
        }
        finally { ImGui.EndDisabled(); }
        UiGui.TextColored(new Vector4(1, .75f, .25f, 1), "Required immunity gear must be owned first.");
        UiGui.TextWrapped("Demonic Helm requires Soulreaper's Armor. Thunder Axe requires Umbral Wristlet.");
        UiGui.TextWrapped("Only one elemental axe is allowed; incompatible elemental axes are always skipped.");
        ImGui.Separator();
        MaterialSettings.Section(UiText.T("Healing and camps"));
        UiGui.TextWrapped("Combat healing: 40% HP or below.");
        ImGui.Separator();
        UiGui.TextWrapped("Board healing: 60% HP or below when no camp comes before the next fight.");
        ImGui.Separator();
        UiGui.TextWrapped("Camps: rest up to two injured familiars below 60% HP. With none selected, the player receives the camp's 90% self heal.");
        UiGui.TextDisabled("Healing thresholds are fixed by the automation.");
    }

    private void DrawFoodAndExtraction()
    {
        var settings = Settings;
        using (UIConstants.PushSettingsRows())
        {
            var foods = new List<uint> { 0 };
            if (ViewingCurrent)
                foods.AddRange(GameHelpers.GetFishingFoodCandidates().SelectMany(GameHelpers.GetHeldFoodVariants)
                    .OrderBy(GameHelpers.GetItemName, StringComparer.CurrentCulture));
            if (!foods.Contains(settings.FoodItemId)) foods.Add(settings.FoodItemId);
            var names = foods.Select(id => id == 0 ? UiText.T("No food")
                : $"{GameHelpers.GetItemName(id)} ({(id >= 1_000_000 ? "HQ" : "NQ")})").ToArray();
            var selected = foods.IndexOf(settings.FoodItemId);
            ImGui.SetNextItemWidth(-1);
            if (UIConstants.Combo("Food", ref selected, names, names.Length))
            { settings.FoodItemId = foods[selected]; SaveSettings(); }
            var extract = settings.ExtractMateria;
            if (UIConstants.Checkbox("Extract materia before entry", ref extract))
            { settings.ExtractMateria = extract; SaveSettings(); }
        }
        UiGui.TextWrapped("Food refreshes Well Fed at 90 seconds or less whenever usable, including during runs. Missing food warns and continues; quality is never substituted.");
        UiGui.TextDisabled("Materia extraction uses ADS outside the duty before each board. Checkbox tests never use supplies.");
    }

    private void ImportSuppliesEditorList()
    {
        try
        {
            var entries = EditorParseClipboard("Order", "Blocked", null, CrucibleItemData.ShopItems.ToHashSet(), out var name);
            var list = new BeastmasterShopList
            {
                Name = EditorUniqueName(Settings.ShopLists, name),
                Order = entries.Select(e => new BeastmasterShopList.Entry { ID = e.Id, Blocked = e.Blocked }).ToList(),
            }.CompleteList();
            Settings.ShopLists.Add(list);
            Settings.ShopListIndex = Settings.ShopLists.Count - 1;
            SaveSettings();
            suppliesListMessage = "Shop list imported.";
            suppliesListError = false;
        }
        catch (Exception)
        {
            suppliesListMessage = "Unable to import the shop list. Use an exported list or known item IDs separated by spaces or commas.";
            suppliesListError = true;
        }
    }
}
