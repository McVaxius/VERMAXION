using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dalamud.Plugin.Services;

namespace VERMAXION.Models;

/// <summary>
/// Configuration for Register Registrables feature
/// </summary>
public class RegistrableConfig
{
    public List<RegistrableItem> Items { get; set; } = new();
}

/// <summary>
/// Represents a registrable item with ID and name
/// </summary>
public class RegistrableItem
{
    public uint ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;

    public RegistrableItem() { }

    public RegistrableItem(uint itemId, string itemName)
    {
        ItemId = itemId;
        ItemName = itemName;
    }
}

/// <summary>
/// Manages registrable items configuration with import/export functionality
/// </summary>
public class RegistrableConfigManager
{
    private readonly IPluginLog log;
    private readonly IDataManager dataManager;
    private readonly string configPath;

    public RegistrableConfig RegistrableConfig { get; private set; } = new();

    public RegistrableConfigManager(IPluginLog log, IDataManager dataManager, string configPath)
    {
        this.log = log;
        this.dataManager = dataManager;
        this.configPath = System.IO.Path.Combine(configPath, "registrable_items.json");
        LoadConfig();
    }

    private void LoadConfig()
    {
        try
        {
            if (System.IO.File.Exists(configPath))
            {
                var json = System.IO.File.ReadAllText(configPath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    WriteIndented = true
                };
                RegistrableConfig = JsonSerializer.Deserialize<RegistrableConfig>(json, options) ?? new RegistrableConfig();
                log.Information($"[RegistrableConfig] Loaded {RegistrableConfig.Items.Count} items from config");
            }
            else
            {
                // Create default config with some example items
                CreateDefaultConfig();
                SaveConfig();
            }
        }
        catch (Exception ex)
        {
            log.Error($"[RegistrableConfig] Failed to load config: {ex.Message}");
            RegistrableConfig = new RegistrableConfig();
        }
    }

    public void SaveConfig()
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = true
            };
            var json = JsonSerializer.Serialize(RegistrableConfig, options);
            System.IO.File.WriteAllText(configPath, json);
            log.Information($"[RegistrableConfig] Saved {RegistrableConfig.Items.Count} items to config");
        }
        catch (Exception ex)
        {
            log.Error($"[RegistrableConfig] Failed to save config: {ex.Message}");
        }
    }

    public void ClearItems()
    {
        RegistrableConfig.Items.Clear();
        SaveConfig();
        log.Information("[RegistrableConfig] Cleared all items");
    }

    public void AddItem(uint itemId, string itemName)
    {
        // Remove if already exists
        RegistrableConfig.Items.RemoveAll(x => x.ItemId == itemId);
        // Add new item
        RegistrableConfig.Items.Add(new RegistrableItem(itemId, itemName));
        SaveConfig();
        log.Information($"[RegistrableConfig] Added item {itemId} ({itemName})");
    }

    public void RemoveItem(uint itemId)
    {
        var removed = RegistrableConfig.Items.RemoveAll(x => x.ItemId == itemId);
        if (removed > 0)
        {
            SaveConfig();
            log.Information($"[RegistrableConfig] Removed item {itemId}");
        }
    }

    public string ExportToJson()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
        return JsonSerializer.Serialize(RegistrableConfig, options);
    }

    public bool ImportFromJson(string json)
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = true
            };
            var imported = JsonSerializer.Deserialize<RegistrableConfig>(json, options);
            if (imported != null)
            {
                RegistrableConfig.Items = imported.Items;
                SaveConfig();
                log.Information($"[RegistrableConfig] Imported {RegistrableConfig.Items.Count} items");
                return true;
            }
        }
        catch (Exception ex)
        {
            log.Error($"[RegistrableConfig] Failed to import: {ex.Message}");
        }
        return false;
    }

    public List<RegistrableItem> SearchItems(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<RegistrableItem>();

        query = query.ToLowerInvariant();
        return RegistrableConfig.Items
            .Where(x => x.ItemName.ToLowerInvariant().Contains(query) || x.ItemId.ToString().Contains(query))
            .ToList();
    }

    public RegistrableItem? FindItemById(uint itemId)
    {
        return RegistrableConfig.Items.FirstOrDefault(x => x.ItemId == itemId);
    }

    public RegistrableItem? FindItemByName(string itemName)
    {
        return RegistrableConfig.Items.FirstOrDefault(x => 
            x.ItemName.Equals(itemName, StringComparison.OrdinalIgnoreCase));
    }

    private void CreateDefaultConfig()
    {
        // Add some example registrable items (these would need to be researched)
        RegistrableConfig.Items.Add(new RegistrableItem(12345, "Example Registrable 1"));
        RegistrableConfig.Items.Add(new RegistrableItem(12346, "Example Registrable 2"));
        log.Information("[RegistrableConfig] Created default config with example items");
    }
}
