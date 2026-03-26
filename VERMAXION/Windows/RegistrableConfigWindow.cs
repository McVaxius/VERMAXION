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
    private readonly IPluginLog log;
    private readonly RegistrableConfigManager configManager;
    private readonly ConfigManager characterConfigManager;
    private readonly IDataManager dataManager;
    private string itemIdSearch = string.Empty;
    private string itemNameSearch = string.Empty;
    private bool isTypingInIdBox = false;
    private bool isTypingInNameBox = false;
    private List<RegistrableItem> allGameItems = new List<RegistrableItem>();

    public RegistrableConfigWindow(IPluginLog log, RegistrableConfigManager configManager, ConfigManager characterConfigManager, IDataManager dataManager)
        : base("Register Registrables Configuration", ImGuiWindowFlags.AlwaysAutoResize)
    {
        this.log = log;
        this.configManager = configManager;
        this.characterConfigManager = characterConfigManager;
        this.dataManager = dataManager;
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
            
            var activeConfig = characterConfigManager.GetSelectedConfig();
            var personalItems = activeConfig?.PersonalRegistrableItems ?? new List<uint>();
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
                        // Add to personal list
                        personalItems.Add(item.ItemId);
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
                        personalItems.Add(item.ItemId);
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
        
        var activeConfig = characterConfigManager.GetSelectedConfig();
        if (activeConfig != null && activeConfig.PersonalRegistrableItems.Count > 0)
        {
            ImGui.Text($"Count: {activeConfig.PersonalRegistrableItems.Count}");
            
            // Scrollable table area with fixed height
            ImGui.BeginChild("##PersonalItemsScroll", new Vector2(0, 300), false);
            
            if (ImGui.BeginTable("PersonalItems", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
            {
                ImGui.TableSetupColumn("Item ID", ImGuiTableColumnFlags.WidthFixed, 80);
                ImGui.TableSetupColumn("Item Name", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Remove", ImGuiTableColumnFlags.WidthFixed, 60);
                ImGui.TableHeadersRow();

                var personalItems = activeConfig.PersonalRegistrableItems.ToList();
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

        // Export personal list
        if (ImGui.Button("Export Personal List"))
        {
            var activeConfig = characterConfigManager.GetSelectedConfig();
            if (activeConfig != null && activeConfig.PersonalRegistrableItems.Count > 0)
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
            if (!string.IsNullOrWhiteSpace(clipboardText))
            {
                try
                {
                    var importedIds = JsonSerializer.Deserialize<List<uint>>(clipboardText);
                    if (importedIds != null)
                    {
                        var activeConfig = characterConfigManager.GetSelectedConfig();
                        if (activeConfig != null)
                        {
                            activeConfig.PersonalRegistrableItems.Clear();
                            activeConfig.PersonalRegistrableItems.AddRange(importedIds);
                            characterConfigManager.SaveCurrentAccount();
                            log.Information($"[RegistrableConfig] Imported {importedIds.Count} personal items from clipboard");
                            itemIdSearch = string.Empty;
                            itemNameSearch = string.Empty;
                        }
                    }
                }
                catch (Exception ex)
                {
                    log.Error($"[RegistrableConfig] Import failed: {ex.Message}");
                }
            }
            else
            {
                log.Warning("[RegistrableConfig] Clipboard is empty - nothing to import");
            }
        }
        
        ImGui.SameLine();
        ImGui.Text("(Imports personal list from clipboard)");
        
        ImGui.Separator();
        
        // Clear All button
        if (ImGui.Button("Clear All Personal Items"))
        {
            var activeConfig = characterConfigManager.GetSelectedConfig();
            if (activeConfig != null)
            {
                var count = activeConfig.PersonalRegistrableItems.Count;
                activeConfig.PersonalRegistrableItems.Clear();
                characterConfigManager.SaveCurrentAccount();
                log.Information($"[RegistrableConfig] Cleared {count} personal items");
                itemIdSearch = string.Empty;
                itemNameSearch = string.Empty;
            }
        }
        
        ImGui.SameLine();
        ImGui.Text("(Removes all personal items)");
        
        // Default list button
        if (ImGui.Button("Load Default List"))
        {
            var defaultItems = new List<uint> { 6001, 6006, 6269, 6994, 7553, 7844, 7845, 7846 };
            var activeConfig = characterConfigManager.GetSelectedConfig();
            if (activeConfig != null)
            {
                activeConfig.PersonalRegistrableItems.Clear();
                activeConfig.PersonalRegistrableItems.AddRange(defaultItems);
                characterConfigManager.SaveCurrentAccount();
                log.Information($"[RegistrableConfig] Loaded default list with {defaultItems.Count} items");
                itemIdSearch = string.Empty;
                itemNameSearch = string.Empty;
            }
        }
        
        ImGui.SameLine();
        ImGui.Text("(Loads recommended default items)");
    }
}
