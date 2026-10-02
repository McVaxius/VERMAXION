using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using VERMAXION.Models;
using VERMAXION.Services;
using System.Text.Json;
using Lumina.Excel.Sheets;

namespace VERMAXION.Windows;

/// <summary>
/// Scoped personal registrable editor with searchable items and confirmed list replacements.
/// </summary>
public class RegistrableConfigWindow : Window
{
    private static readonly IReadOnlyList<uint> DefaultItems = [6001, 6006, 6269, 6994, 7553, 7844, 7845, 7846];
    private readonly IPluginLog log;
    private readonly RegistrableConfigManager configManager;
    private readonly ConfigManager characterConfigManager;
    private readonly IDataManager dataManager;
    private readonly Configuration configuration;
    private string itemIdSearch = string.Empty;
    private string itemNameSearch = string.Empty;
    private List<RegistrableItem> allGameItems = new List<RegistrableItem>();
    private string personalListSearch = string.Empty;
    private RegistrableImportPreview? pendingImportPreview;
    private string importPreviewStatus = string.Empty;
    private bool replacementConfirmationRequested;
    private string replacementConfirmationTitle = string.Empty;
    private string replacementConfirmationMessage = string.Empty;
    private IReadOnlyList<uint> pendingReplacementIds = [];
    private string pendingReplacementScopeKey = string.Empty;
    private string pendingReplacementAccountId = string.Empty;
    private string importPreviewAccountId = string.Empty;
    private string importPreviewScopeKey = string.Empty;

    public RegistrableConfigWindow(IPluginLog log, RegistrableConfigManager configManager, ConfigManager characterConfigManager, IDataManager dataManager, Configuration configuration)
        : base("Register Registrables Configuration", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.log = log;
        this.configManager = configManager;
        this.characterConfigManager = characterConfigManager;
        this.dataManager = dataManager;
        this.configuration = configuration;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(520, 480),
            MaximumSize = new Vector2(1200, 900),
        };
        LoadGameItems();
    }

    private void LoadGameItems()
    {
        try
        {
            allGameItems.Clear();
            var itemSheet = dataManager.GetExcelSheet<Item>();
            if (itemSheet != null)
            {
                foreach (var item in itemSheet)
                {
                    if (item.RowId > 0 && !string.IsNullOrEmpty(item.Name.ToString()))
                    {
                        var itemName = item.Name.ToString();
                        
                        // Filter for consumable items that unlock collection items
                        // These are typically items that are consumed on use and unlock something permanent
                        
                        // Check if it's a consumable (most registrables are consumables)
                        if (item.ItemUICategory.RowId == 0)
                            continue;
                            
                        var categoryId = item.ItemUICategory.RowId;
                        
                        // Common categories for registrable items
                        var registrableCategories = new HashSet<uint>
                        {
                            // Mounts (usually in the 80s range)
                            85, // Mount (Whistle)
                            // Minions (usually in the 60s range) 
                            64, // Minion
                            65, // Minion
                            // Orchestrion Rolls (usually in the 90s range)
                            97, // Orchestrion Roll
                            98, // Orchestrion Roll
                            // Emotes
                            85, // Sometimes emotes share category with mounts
                            86, // Emote
                            87, // Emote
                            // Hairstyles
                            68, // Appearance Change
                            69, // Hairstyle
                            // Fashion Accessories
                            103, // Fashion Accessory
                            // Other collection items
                            104, // Other
                            105, // Other
                        };
                        
                        // Additional filtering: look for keywords in item names
                        bool isRegistrable = false;
                        
                        // Check category first
                        if (registrableCategories.Contains(categoryId))
                        {
                            isRegistrable = true;
                        }
                        // Then check name patterns for common registrable items
                        else if (itemName.Contains("Whistle") ||
                                itemName.Contains("Minion") ||
                                itemName.Contains("Orchestrion") ||
                                itemName.Contains("Roll") ||
                                itemName.Contains("Emote") ||
                                itemName.Contains("Hairstyle") ||
                                itemName.Contains("Fashion") ||
                                itemName.Contains("Regalia") ||
                                itemName.Contains("Certificate") ||
                                itemName.Contains("License") ||
                                itemName.Contains("Pass"))
                        {
                            isRegistrable = true;
                        }
                        
                        // Also check if it's a unique/untradeable consumable (common for registrables)
                        if (!isRegistrable && 
                            (item.IsUnique || item.IsUntradable) && 
                            (item.ItemUICategory.RowId >= 60 && item.ItemUICategory.RowId <= 110))
                        {
                            isRegistrable = true;
                        }
                        
                        if (isRegistrable)
                        {
                            allGameItems.Add(new RegistrableItem
                            {
                                ItemId = item.RowId,
                                ItemName = itemName
                            });
                        }
                    }
                }
                log.Information($"[RegistrableConfig] Loaded {allGameItems.Count} registrable items from game data");
            }
            else
            {
                log.Error("[RegistrableConfig] Failed to load item sheet");
            }
        }
        catch (Exception ex)
        {
            log.Error($"[RegistrableConfig] Error loading game items: {ex.Message}");
        }
    }

    public override void PreDraw() => UIConstants.PushStyle(configuration.CompactUi);

    public override void PostDraw() => UIConstants.PopStyle();

    public override void Draw()
    {
        DrawHeader();
        if (pendingImportPreview != null &&
            (!string.Equals(importPreviewAccountId, characterConfigManager.CurrentAccountId, StringComparison.Ordinal) ||
             !string.Equals(importPreviewScopeKey, characterConfigManager.SelectedCharacterKey, StringComparison.Ordinal)))
        {
            pendingImportPreview = null;
            importPreviewStatus = "The editing scope changed. Import again to preview the intended personal list.";
        }
        ImGui.Separator();
        if (ImGui.BeginTabBar("##RegistrableTabs"))
        {
            if (ImGui.BeginTabItem("Personal list"))
            {
                ImGui.BeginChild("##PersonalListTab", Vector2.Zero, false);
                DrawPersonalItems();
                ImGui.EndChild();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Add items"))
            {
                ImGui.BeginChild("##AddItemsTab", Vector2.Zero, false);
                DrawAddItems();
                ImGui.EndChild();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Import/export"))
            {
                ImGui.BeginChild("##ImportExportTab", Vector2.Zero, false);
                DrawImportExport();
                ImGui.EndChild();
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
        // Open and draw the modal from the stable window ID scope.
        DrawReplacementConfirmation();
    }

    private void DrawHeader()
    {
        UIConstants.Heading("Personal registrables", configuration.CompactUi);
        var account = characterConfigManager.GetCurrentAccount();
        var accountLabel = account == null ? "No account selected" :
            string.IsNullOrWhiteSpace(account.AccountAlias) ? account.AccountId : account.AccountAlias;
        ImGui.TextWrapped($"Editing account: {accountLabel} | Scope: {GetSelectedScopeLabel()}");
        ImGui.TextWrapped($"{allGameItems.Count:N0} available items | {GetSelectedEditingConfig()?.PersonalRegistrableItems.Count ?? 0} personal items");

        if (UIConstants.Button("Reload Items"))
        {
            LoadGameItems();
        }
    }

    private CharacterConfig? GetSelectedEditingConfig()
    {
        var account = characterConfigManager.GetCurrentAccount();
        if (account == null)
            return null;
        var key = characterConfigManager.SelectedCharacterKey;
        return string.IsNullOrEmpty(key) ? account.DefaultConfig :
            account.Characters.TryGetValue(key, out var selected) ? selected : null;
    }

    private void DrawAddItems()
    {
        UIConstants.Heading("Find an item", configuration.CompactUi);
        var activeConfig = GetSelectedEditingConfig();
        if (activeConfig == null)
        {
            ImGui.TextWrapped("Select an account and configuration scope before editing personal items.");
            return;
        }

        UIConstants.SetNextItemWidth(-1f);
        var idInput = itemIdSearch;
        if (ImGui.InputTextWithHint("##ItemID", "Search item ID", ref idInput, 20))
        {
            itemIdSearch = Regex.Replace(idInput, @"[^0-9]", "");
            itemNameSearch = string.Empty;
        }
        UIConstants.SetNextItemWidth(-1f);
        var nameInput = itemNameSearch;
        if (ImGui.InputTextWithHint("##ItemName", "Search item name", ref nameInput, 100))
        {
            itemNameSearch = nameInput;
            itemIdSearch = string.Empty;
        }
        ImGui.Separator();

        var personalItems = activeConfig.PersonalRegistrableItems;
        var resultsShown = 0;
        foreach (var item in allGameItems)
        {
            var matches = !string.IsNullOrWhiteSpace(itemIdSearch)
                ? item.ItemId.ToString().Contains(itemIdSearch, StringComparison.Ordinal)
                : !string.IsNullOrWhiteSpace(itemNameSearch) &&
                  item.ItemName.Contains(itemNameSearch, StringComparison.OrdinalIgnoreCase);
            if (!matches)
                continue;

            resultsShown++;
            var isAdded = personalItems.Contains(item.ItemId);
            ImGui.PushID($"Item_{item.ItemId}");
            if (UIConstants.Button(isAdded ? "Remove" : "Add"))
            {
                if (isAdded)
                {
                    personalItems.Remove(item.ItemId);
                    log.Information($"[RegistrableConfig] Removed {item.ItemName} from personal list");
                }
                else
                {
                    activeConfig.PersonalRegistrableItems = RegistrableEditorPolicy
                        .AddIfMissing(personalItems, item.ItemId)
                        .ToList();
                    personalItems = activeConfig.PersonalRegistrableItems;
                    log.Information($"[RegistrableConfig] Added {item.ItemName} to personal list");
                }
                characterConfigManager.SaveCurrentAccount();
            }
            ImGui.SameLine();
            ImGui.TextWrapped($"{item.ItemId} - {item.ItemName}{(isAdded ? " (added)" : string.Empty)}");
            ImGui.PopID();
        }

        if (resultsShown == 0)
            ImGui.TextWrapped(string.IsNullOrWhiteSpace(itemIdSearch) && string.IsNullOrWhiteSpace(itemNameSearch)
                ? "Enter an item ID or name to search." : "No items match this search.");
    }

    private void DrawPersonalItems()
    {
        UIConstants.Heading("Personal list", configuration.CompactUi);
        UIConstants.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##PersonalListSearch", "Search configured item ID or name", ref personalListSearch, 100);
        
        var activeConfig = GetSelectedEditingConfig();
        if (activeConfig != null && activeConfig.PersonalRegistrableItems.Count > 0)
        {
            var names = allGameItems.ToDictionary(item => item.ItemId, item => item.ItemName);
            var personalItems = RegistrableEditorPolicy.SearchConfigured(
                activeConfig.PersonalRegistrableItems,
                personalListSearch,
                names);
            ImGui.Text($"Showing {personalItems.Count} of {activeConfig.PersonalRegistrableItems.Count}");
            
            var listHeight = Math.Max(120f * UIConstants.Scale, ImGui.GetContentRegionAvail().Y - 180f * UIConstants.Scale);
            if (ImGui.BeginTable("PersonalItems", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
                    new Vector2(0, listHeight)))
            {
                ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch, 1f);
                ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed, 80 * UIConstants.Scale);
                ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthFixed, Math.Max(100 * UIConstants.Scale, UIConstants.ButtonWidth("Remove")));
                ImGui.TableSetupScrollFreeze(0, 1);
                ImGui.TableHeadersRow();

                foreach (var itemId in personalItems)
                {
                    ImGui.TableNextRow();
                    
                    // Item ID
                    ImGui.TableSetColumnIndex(1);
                    ImGui.Text(itemId.ToString());
                    
                    // Item Name - look up from game data
                    ImGui.TableSetColumnIndex(0);
                    var gameItem = allGameItems.FirstOrDefault(x => x.ItemId == itemId);
                    var itemName = gameItem?.ItemName ?? $"Unknown ({itemId})";
                    ImGui.TextWrapped(itemName);
                    
                    // Remove button
                    ImGui.TableSetColumnIndex(2);
                    if (UIConstants.Button($"Remove##Personal{itemId}"))
                    {
                        activeConfig.PersonalRegistrableItems.Remove(itemId);
                        characterConfigManager.SaveCurrentAccount();
                        log.Information($"[RegistrableConfig] Removed {itemName} from character's personal list");
                    }
                }

                ImGui.EndTable();
            }

            if (personalItems.Count == 0)
                ImGui.TextDisabled("No configured personal items match this search.");
            
        }
        else
        {
            ImGui.TextWrapped(activeConfig == null ? "Select an available account and configuration scope before editing." :
                "No personal items configured for this scope. Use Add items or Import/export.");
        }
        if (activeConfig != null) DrawBulkActions(activeConfig);
    }

    private void DrawImportExport()
    {
        UIConstants.Heading("Import/export personal list", configuration.CompactUi);
        var activeConfig = GetSelectedEditingConfig();
        if (activeConfig == null)
        {
            ImGui.TextWrapped("Select an account and configuration scope before importing, exporting, or replacing personal items.");
            return;
        }

        // Export personal list
        if (UIConstants.Button("Export Personal List"))
        {
            if (activeConfig.PersonalRegistrableItems.Count > 0)
            {
                var personalItemsJson = JsonSerializer.Serialize(activeConfig.PersonalRegistrableItems, new JsonSerializerOptions { WriteIndented = true });
                ImGui.SetClipboardText(personalItemsJson);
                log.Information($"[RegistrableConfig] Exported {activeConfig.PersonalRegistrableItems.Count} personal items to clipboard");
            }
            else
            {
                log.Warning("[RegistrableConfig] No personal items to export");
            }
        }

        ImGui.TextWrapped("Copies your personal list to the clipboard.");
        
        // Import personal list
        if (UIConstants.Button("Import Personal List"))
        {
            var clipboardText = ImGui.GetClipboardText();
            if (allGameItems.Count == 0)
            {
                pendingImportPreview = null;
                importPreviewStatus = "The game-item catalog is unavailable. Reload items before importing.";
            }
            else
            {
                var knownIds = allGameItems.Select(item => item.ItemId).ToHashSet();
                pendingImportPreview = RegistrableEditorPolicy.ParseImport(
                    clipboardText,
                    knownIds,
                    activeConfig.PersonalRegistrableItems);
                importPreviewAccountId = characterConfigManager.CurrentAccountId;
                importPreviewScopeKey = characterConfigManager.SelectedCharacterKey;
                importPreviewStatus = pendingImportPreview.IsValid
                    ? string.Empty
                    : pendingImportPreview.Error;
            }
        }
        ImGui.TextWrapped("Parses clipboard JSON and previews the replacement before any change.");

        if (!string.IsNullOrWhiteSpace(importPreviewStatus))
            ImGui.TextWrapped($"Import error: {importPreviewStatus}");

        if (pendingImportPreview is { IsValid: true } preview)
        {
            UIConstants.Heading("Import preview", configuration.CompactUi);
            if (ImGui.BeginTable(
                    "ImportPreviewCounts",
                    2,
                    ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchSame))
            {
                ImGui.TableSetupColumn("Result");
                ImGui.TableSetupColumn("Count");
                ImGui.TableHeadersRow();
                var labels = new[] { "Accepted", "Duplicate", "Unknown", "Invalid", "Added", "Removed" };
                var values = new[]
                {
                    preview.AcceptedCount,
                    preview.DuplicateCount,
                    preview.UnknownCount,
                    preview.InvalidCount,
                    preview.AddedCount,
                    preview.RemovedCount,
                };
                for (var index = 0; index < values.Length; index++)
                {
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.Text(labels[index]);
                    ImGui.TableSetColumnIndex(1);
                    ImGui.Text(values[index].ToString());
                }
                ImGui.EndTable();
            }

            if (UIConstants.Button("Apply imported replacement..."))
            {
                RequestReplacementConfirmation(
                    "Replace personal list with import?",
                    $"Replace {GetSelectedScopeLabel()}'s personal list with {preview.AcceptedCount} accepted IDs? {preview.AddedCount} will be added and {preview.RemovedCount} removed; duplicate, unknown, and invalid entries remain excluded.",
                    preview.AcceptedIds);
            }
            if (UIConstants.Button("Cancel import preview"))
                pendingImportPreview = null;
        }
        
    }

    private void DrawBulkActions(CharacterConfig activeConfig)
    {
        if (!UIConstants.BeginPanel("BulkActions", "Bulk actions")) return;
        if (UIConstants.Button("Clear personal list..."))
        {
            RequestReplacementConfirmation(
                "Clear all personal items?",
                $"Remove all {activeConfig.PersonalRegistrableItems.Count} IDs from {GetSelectedScopeLabel()}'s personal registrable list?",
                []);
        }

        ImGui.TextWrapped("Removes all personal items after confirmation.");
        
        // Default list button
        if (UIConstants.Button("Load default list..."))
        {
            RequestReplacementConfirmation(
                "Replace with the default list?",
                $"Replace {GetSelectedScopeLabel()}'s personal list with the {DefaultItems.Count} recommended default IDs?",
                DefaultItems);
        }

        ImGui.TextWrapped("Loads recommended default items after confirmation.");
        UIConstants.EndPanel();
    }

    private void RequestReplacementConfirmation(
        string title,
        string message,
        IReadOnlyList<uint> replacementIds)
    {
        replacementConfirmationTitle = title;
        replacementConfirmationMessage = message;
        pendingReplacementIds = replacementIds.ToList();
        pendingReplacementScopeKey = characterConfigManager.SelectedCharacterKey ?? string.Empty;
        pendingReplacementAccountId = characterConfigManager.CurrentAccountId;
        replacementConfirmationRequested = true;
    }

    private string GetSelectedScopeLabel()
        => string.IsNullOrWhiteSpace(characterConfigManager.SelectedCharacterKey)
            ? "Account default"
            : characterConfigManager.SelectedCharacterKey;

    private void DrawReplacementConfirmation()
    {
        if (replacementConfirmationRequested)
        {
            ImGui.OpenPopup("Confirm personal-list replacement");
            replacementConfirmationRequested = false;
        }

        var open = true;
        ImGui.SetNextWindowSize(new Vector2(480 * UIConstants.Scale, 0), ImGuiCond.Appearing);
        if (!ImGui.BeginPopupModal(
                "Confirm personal-list replacement",
                ref open,
                ImGuiWindowFlags.AlwaysAutoResize))
        {
            return;
        }

        ImGui.TextWrapped(replacementConfirmationTitle);
        ImGui.Separator();
        ImGui.TextWrapped(replacementConfirmationMessage);
        if (UIConstants.Button("Confirm replacement"))
        {
            var activeConfig = GetSelectedEditingConfig();
            var currentScopeKey = characterConfigManager.SelectedCharacterKey ?? string.Empty;
            if (activeConfig == null ||
                !string.Equals(characterConfigManager.CurrentAccountId, pendingReplacementAccountId, StringComparison.Ordinal) ||
                !string.Equals(currentScopeKey, pendingReplacementScopeKey, StringComparison.Ordinal))
            {
                replacementConfirmationMessage = "The selected configuration scope changed or is no longer available. Cancel and review the intended scope before trying again.";
            }
            else
            {
                activeConfig.PersonalRegistrableItems = RegistrableEditorPolicy
                    .Normalize(pendingReplacementIds)
                    .ToList();
                characterConfigManager.SaveCurrentAccount();
                log.Information($"[RegistrableConfig] Replaced personal list with {activeConfig.PersonalRegistrableItems.Count} items");
                itemIdSearch = string.Empty;
                itemNameSearch = string.Empty;
                personalListSearch = string.Empty;
                pendingImportPreview = null;
                pendingReplacementIds = [];
                pendingReplacementScopeKey = string.Empty;
                ImGui.CloseCurrentPopup();
            }
        }
        UIConstants.SameLineIfFits("Cancel");
        if (UIConstants.Button("Cancel"))
        {
            pendingReplacementIds = [];
            pendingReplacementScopeKey = string.Empty;
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }
}
