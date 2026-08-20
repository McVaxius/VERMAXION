using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using VERMAXION.Models;
using VERMAXION.Services;
using System.Text.Json;
using Lumina.Excel.Sheets;

namespace VERMAXION.Windows;

/// <summary>
/// Configuration window for managing registrable items with FrenRider-style dropdown
/// </summary>
public class RegistrableConfigWindow : Window
{
    private static readonly IReadOnlyList<uint> DefaultItems = [6001, 6006, 6269, 6994, 7553, 7844, 7845, 7846];
    private readonly IPluginLog log;
    private readonly RegistrableConfigManager configManager;
    private readonly ConfigManager characterConfigManager;
    private readonly IDataManager dataManager;
    private string itemIdSearch = string.Empty;
    private string itemNameSearch = string.Empty;
    private bool isTypingInIdBox = false;
    private bool isTypingInNameBox = false;
    private List<RegistrableItem> allGameItems = new List<RegistrableItem>();
    private string personalListSearch = string.Empty;
    private RegistrableImportPreview? pendingImportPreview;
    private string importPreviewStatus = string.Empty;
    private bool replacementConfirmationRequested;
    private string replacementConfirmationTitle = string.Empty;
    private string replacementConfirmationMessage = string.Empty;
    private IReadOnlyList<uint> pendingReplacementIds = [];
    private string pendingReplacementScopeKey = string.Empty;

    public RegistrableConfigWindow(IPluginLog log, RegistrableConfigManager configManager, ConfigManager characterConfigManager, IDataManager dataManager)
        : base("Register Registrables Configuration", ImGuiWindowFlags.None)
    {
        this.log = log;
        this.configManager = configManager;
        this.characterConfigManager = characterConfigManager;
        this.dataManager = dataManager;
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

    public override void Draw()
    {
        DrawHeader();
        ImGui.Separator();
        DrawAddItemDropdown();
        ImGui.Separator();
        DrawPersonalItems();
        ImGui.Separator();
        DrawImportExport();
        DrawReplacementConfirmation();
    }

    private void DrawHeader()
    {
        ImGui.Text($"Total items available: {allGameItems.Count:N0}");
        ImGui.Text($"Items in personal list: {characterConfigManager.GetSelectedConfig()?.PersonalRegistrableItems.Count ?? 0}");

        if (ImGui.Button("Reload Items"))
        {
            LoadGameItems();
        }
    }

    private void DrawAddItemDropdown()
    {
        ImGui.Text("Add Item:");
        var activeConfig = characterConfigManager.GetSelectedConfig();
        if (activeConfig == null)
        {
            ImGui.TextDisabled("Select an account and configuration scope before editing personal items.");
            return;
        }
        
        var displayText = $"Search from {allGameItems.Count:N0} items";
        
        ImGui.SetNextItemWidth(400);
        if (ImGui.BeginCombo("##ItemSelect", displayText))
        {
            // Search fields at top of dropdown
            ImGui.Text("Search:");
            
            // Item ID search (numbers only)
            ImGui.SetNextItemWidth(150);
            var idInput = itemIdSearch;
            if (ImGui.InputText("Item ID##ID", ref idInput, 20))
            {
                var numericOnly = Regex.Replace(idInput, @"[^0-9]", "");
                if (numericOnly != idInput)
                {
                    itemIdSearch = numericOnly;
                    itemNameSearch = string.Empty;
                    isTypingInIdBox = true;
                    isTypingInNameBox = false;
                }
            }
            
            if (ImGui.IsItemActive() && !isTypingInIdBox)
            {
                itemNameSearch = string.Empty;
                isTypingInIdBox = true;
                isTypingInNameBox = false;
            }
            
            ImGui.SameLine();
            
            // Item Name search (text)
            ImGui.SetNextItemWidth(200);
            var nameInput = itemNameSearch;
            if (ImGui.InputText("Item Name##Name", ref nameInput, 100))
            {
                itemNameSearch = nameInput;
                itemIdSearch = string.Empty;
                isTypingInNameBox = true;
                isTypingInIdBox = false;
            }
            
            if (ImGui.IsItemActive() && !isTypingInNameBox)
            {
                itemIdSearch = string.Empty;
                isTypingInNameBox = true;
                isTypingInIdBox = false;
            }
            
            ImGui.Separator();
            
            // Show more search results (no scrolling, just bigger visual area)
            var maxResultsToShow = 20; // Show up to 20 results instead of 10
            
            var personalItems = activeConfig.PersonalRegistrableItems;
            var resultsShown = 0;
            
            for (var i = 0; i < allGameItems.Count && resultsShown < maxResultsToShow; i++)
            {
                var item = allGameItems[i];
                var displayItemName = $"{item.ItemId} - {item.ItemName}";
                
                // Filter based on search
                bool showItem = true;
                if (!string.IsNullOrWhiteSpace(itemIdSearch))
                {
                    if (uint.TryParse(itemIdSearch, out uint searchId))
                    {
                        showItem = item.ItemId.ToString().Contains(searchId.ToString());
                    }
                    else
                    {
                        showItem = false;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(itemNameSearch))
                {
                    showItem = item.ItemName.ToLowerInvariant().Contains(itemNameSearch.ToLowerInvariant());
                }
                else if (!string.IsNullOrWhiteSpace(itemIdSearch) || !string.IsNullOrWhiteSpace(itemNameSearch))
                {
                    showItem = false;
                }
                else
                {
                    // If both boxes are empty, don't show anything (too many items)
                    showItem = false;
                }
                
                if (!showItem) continue;
                
                resultsShown++;
                var isAdded = personalItems.Contains(item.ItemId);
                
                ImGui.PushID($"Item_{i}");
                
                // Add/Remove button
                var buttonText = isAdded ? "[-]" : "[+]";
                if (ImGui.Button(buttonText, new Vector2(30, 0)))
                {
                    if (isAdded)
                    {
                        // Remove from personal list
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
                
                // Item name
                if (ImGui.Selectable(displayItemName, false))
                {
                    if (!isAdded)
                    {
                        activeConfig.PersonalRegistrableItems = RegistrableEditorPolicy
                            .AddIfMissing(personalItems, item.ItemId)
                            .ToList();
                        personalItems = activeConfig.PersonalRegistrableItems;
                        characterConfigManager.SaveCurrentAccount();
                        log.Information($"[RegistrableConfig] Added {item.ItemName} to personal list");
                    }
                }
                
                if (isAdded)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(new Vector4(0, 1, 0, 1), "(added)");
                }
                
                ImGui.PopID();
            }
            
            ImGui.EndCombo();
        }
    }

    private void DrawPersonalItems()
    {
        ImGui.Text("Character's Personal Items:");
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##PersonalListSearch", "Search configured item ID or name", ref personalListSearch, 100);
        
        var activeConfig = characterConfigManager.GetSelectedConfig();
        if (activeConfig != null && activeConfig.PersonalRegistrableItems.Count > 0)
        {
            var names = allGameItems.ToDictionary(item => item.ItemId, item => item.ItemName);
            var personalItems = RegistrableEditorPolicy.SearchConfigured(
                activeConfig.PersonalRegistrableItems,
                personalListSearch,
                names);
            ImGui.Text($"Showing {personalItems.Count} of {activeConfig.PersonalRegistrableItems.Count}");
            
            // Scrollable table area with fixed height
            ImGui.BeginChild("##PersonalItemsScroll", new Vector2(0, 300), false);
            
            if (ImGui.BeginTable("PersonalItems", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
            {
                ImGui.TableSetupColumn("Item ID", ImGuiTableColumnFlags.WidthFixed, 80);
                ImGui.TableSetupColumn("Item Name", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Remove", ImGuiTableColumnFlags.WidthFixed, 60);
                ImGui.TableHeadersRow();

                foreach (var itemId in personalItems)
                {
                    ImGui.TableNextRow();
                    
                    // Item ID
                    ImGui.TableSetColumnIndex(0);
                    ImGui.Text(itemId.ToString());
                    
                    // Item Name - look up from game data
                    ImGui.TableSetColumnIndex(1);
                    var gameItem = allGameItems.FirstOrDefault(x => x.ItemId == itemId);
                    var itemName = gameItem?.ItemName ?? $"Unknown ({itemId})";
                    ImGui.Text(itemName);
                    
                    // Remove button
                    ImGui.TableSetColumnIndex(2);
                    if (ImGui.Button($"Remove##Personal{itemId}"))
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
            
            ImGui.EndChild();
        }
        else
        {
            ImGui.Text("No personal items configured for this character.");
        }
    }

    private void DrawImportExport()
    {
        ImGui.Text("Import/Export Personal List");
        var activeConfig = characterConfigManager.GetSelectedConfig();
        if (activeConfig == null)
        {
            ImGui.TextDisabled("Select an account and configuration scope before importing, exporting, or replacing personal items.");
            return;
        }

        // Export personal list
        if (ImGui.Button("Export Personal List"))
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

        ImGui.SameLine();
        ImGui.Text("(Exports your personal list)");
        
        // Import personal list
        if (ImGui.Button("Import Personal List"))
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
                importPreviewStatus = pendingImportPreview.IsValid
                    ? string.Empty
                    : pendingImportPreview.Error;
            }
        }
        ImGui.SameLine();
        ImGui.TextWrapped("Parses clipboard JSON and previews the replacement before any change.");

        if (!string.IsNullOrWhiteSpace(importPreviewStatus))
            ImGui.TextWrapped($"Import error: {importPreviewStatus}");

        if (pendingImportPreview is { IsValid: true } preview)
        {
            ImGui.Text("Import preview");
            if (ImGui.BeginTable(
                    "ImportPreviewCounts",
                    6,
                    ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchSame))
            {
                foreach (var heading in new[] { "Accepted", "Duplicate", "Unknown", "Invalid", "Added", "Removed" })
                    ImGui.TableSetupColumn(heading);
                ImGui.TableHeadersRow();
                ImGui.TableNextRow();
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
                    ImGui.TableSetColumnIndex(index);
                    ImGui.Text(values[index].ToString());
                }
                ImGui.EndTable();
            }

            if (ImGui.Button("Apply imported replacement..."))
            {
                RequestReplacementConfirmation(
                    "Replace personal list with import?",
                    $"Replace {GetSelectedScopeLabel()}'s personal list with {preview.AcceptedCount} accepted IDs? {preview.AddedCount} will be added and {preview.RemovedCount} removed; duplicate, unknown, and invalid entries remain excluded.",
                    preview.AcceptedIds);
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel import preview"))
                pendingImportPreview = null;
        }
        
        ImGui.Separator();
        
        // Clear All button
        if (ImGui.Button("Clear All Personal Items..."))
        {
            RequestReplacementConfirmation(
                "Clear all personal items?",
                $"Remove all {activeConfig.PersonalRegistrableItems.Count} IDs from {GetSelectedScopeLabel()}'s personal registrable list?",
                []);
        }

        ImGui.SameLine();
        ImGui.Text("(Removes all personal items)");
        
        // Default list button
        if (ImGui.Button("Load Default List..."))
        {
            RequestReplacementConfirmation(
                "Replace with the default list?",
                $"Replace {GetSelectedScopeLabel()}'s personal list with the {DefaultItems.Count} recommended default IDs?",
                DefaultItems);
        }

        ImGui.SameLine();
        ImGui.Text("(Loads recommended default items)");
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
        replacementConfirmationRequested = true;
    }

    private string GetSelectedScopeLabel()
        => string.IsNullOrWhiteSpace(characterConfigManager.SelectedCharacterKey)
            ? "the current Account default"
            : characterConfigManager.SelectedCharacterKey;

    private void DrawReplacementConfirmation()
    {
        if (replacementConfirmationRequested)
        {
            ImGui.OpenPopup("Confirm personal-list replacement");
            replacementConfirmationRequested = false;
        }

        var open = true;
        if (!ImGui.BeginPopupModal(
                "Confirm personal-list replacement",
                ref open,
                ImGuiWindowFlags.AlwaysAutoResize))
        {
            return;
        }

        ImGui.Text(replacementConfirmationTitle);
        ImGui.Separator();
        ImGui.TextWrapped(replacementConfirmationMessage);
        if (ImGui.Button("Confirm replacement"))
        {
            var activeConfig = characterConfigManager.GetSelectedConfig();
            var currentScopeKey = characterConfigManager.SelectedCharacterKey ?? string.Empty;
            if (activeConfig == null ||
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
        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
        {
            pendingReplacementIds = [];
            pendingReplacementScopeKey = string.Empty;
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }
}
