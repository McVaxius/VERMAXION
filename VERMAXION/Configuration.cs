using Dalamud.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using VERMAXION.Models;

namespace VERMAXION;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    // --- Global UI Settings ---
    public bool IsConfigWindowMovable { get; set; } = true;
    public bool DtrBarEnabled { get; set; } = true;
    public int DtrBarMode { get; set; } = 0; // 0=text-only, 1=icon+text, 2=icon-only
    public string DtrIconEnabled { get; set; } = "\uE03C";
    public string DtrIconDisabled { get; set; } = "\uE03D";
    public bool KrangleEnabled { get; set; } = false;
    public bool AutoRestoreRetainerCheckingAfterWork { get; set; } = true;
    public bool EnableCharacterSelectStallRecovery { get; set; } = true;
    public float LeftPanelWidth { get; set; } = 240f;
    public CharacterListSortMode CharacterListSortMode { get; set; } = CharacterListSortMode.Name;
    public List<string> PostProcessTaskOrder { get; set; } = VERMAXION.PostProcessTaskOrder.DefaultOrder.ToList();
    public Dictionary<string, PostProcessTaskPhase> PostProcessTaskPlacement { get; set; } = VERMAXION.PostProcessTaskOrder.CreateDefaultPlacement();

    // --- Fishing ---
    public FishingExecutionMode FishingExecutionMode { get; set; } = FishingDefaults.ExecutionMode;
    public int FishingMaxFisherLevel { get; set; } = FishingDefaults.MaxFisherLevel;
    public int OceanFishingPreWindowOffsetMinutes { get; set; } = FishingDefaults.OceanFishingPreWindowOffsetMinutes;
    public bool FishingCharacterSettingsMigrated { get; set; } = false;
    public bool FishingStockCatalogMigrated { get; set; } = false;
    public List<FishingStockCatalogEntry> FishingStockCatalog { get; set; } =
        FishingStockCatalogPolicy.CreateDefaultCatalog();

    // --- Free Company action stock ---
    public Dictionary<ulong, FcActionStockEntry> FcActionStockByFreeCompanyId { get; set; } = new();

    // --- Setup wizard ---
    public bool SetupWizardStateMigrated { get; set; } = false;
    public bool SetupWizardCompleted { get; set; } = false;

    // --- Account Tracking ---
    public string LastAccountId { get; set; } = "";

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? LegacyFields { get; set; }

    public bool TryGetLegacyFishingOperationSettings(out FishingOperationSettings settings)
    {
        settings = default;
        if (LegacyFields == null)
            return false;

        var hasLegacyFishingSettings =
            LegacyFields.ContainsKey("FishingLureRestockTarget") ||
            LegacyFields.ContainsKey("FishingReturnDestination") ||
            LegacyFields.ContainsKey("FishingReturnCommand") ||
            LegacyFields.ContainsKey("FishingRepairMode") ||
            LegacyFields.ContainsKey("FishingRepairThresholdPercent");

        if (!hasLegacyFishingSettings)
            return false;

        settings = new FishingOperationSettings(
            TryGetLegacyInt("FishingLureRestockTarget", out var lureTarget)
                ? Math.Max(0, lureTarget)
                : FishingDefaults.LureRestockTarget,
            TryGetLegacyEnum("FishingReturnDestination", out FishingReturnDestination returnDestination)
                ? returnDestination
                : FishingDefaults.ReturnDestination,
            TryGetLegacyString("FishingReturnCommand", out var returnCommand)
                ? returnCommand
                : FishingDefaults.ReturnCommand,
            TryGetLegacyEnum("FishingRepairMode", out FishingRepairMode repairMode)
                ? repairMode
                : FishingDefaults.RepairMode,
            TryGetLegacyInt("FishingRepairThresholdPercent", out var repairThreshold)
                ? Math.Clamp(repairThreshold, 0, 100)
                : FishingDefaults.RepairThresholdPercent);

        return true;
    }

    public void ClearLegacyFishingOperationSettings()
    {
        if (LegacyFields == null)
            return;

        LegacyFields.Remove("FishingLureRestockTarget");
        LegacyFields.Remove("FishingReturnDestination");
        LegacyFields.Remove("FishingReturnCommand");
        LegacyFields.Remove("FishingRepairMode");
        LegacyFields.Remove("FishingRepairThresholdPercent");
    }

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }

    private bool TryGetLegacyInt(string key, out int value)
    {
        value = 0;
        return LegacyFields != null &&
               LegacyFields.TryGetValue(key, out var element) &&
               element.ValueKind == JsonValueKind.Number &&
               element.TryGetInt32(out value);
    }

    private bool TryGetLegacyString(string key, out string value)
    {
        value = string.Empty;
        if (LegacyFields == null ||
            !LegacyFields.TryGetValue(key, out var element) ||
            element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString() ?? string.Empty;
        return true;
    }

    private bool TryGetLegacyEnum<TEnum>(string key, out TEnum value)
        where TEnum : struct, Enum
    {
        value = default;
        if (LegacyFields == null || !LegacyFields.TryGetValue(key, out var element))
            return false;

        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var numericValue))
        {
            if (Enum.IsDefined(typeof(TEnum), numericValue))
            {
                value = (TEnum)Enum.ToObject(typeof(TEnum), numericValue);
                return true;
            }

            return false;
        }

        return element.ValueKind == JsonValueKind.String &&
               Enum.TryParse(element.GetString(), ignoreCase: true, out value);
    }
}
