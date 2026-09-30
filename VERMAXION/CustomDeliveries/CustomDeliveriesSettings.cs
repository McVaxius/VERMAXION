using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using VERMAXION.Models;

namespace VERMAXION.CustomDeliveries;

[Flags]
public enum DeliveryTypes { None = 0, Crafting = 1, Mining = 2, Botany = 4, Fishing = 8 }
public enum DeliveryNpcPolicy { ClosestTo150, BonusesOnly }
public enum DeliveryJobChoice { Specific, Current, LowestXP, HighestXP }

// VSatisfy 1ab3f9f settings are retained in VERMAXION's per-character configuration.
public sealed class CustomDeliveriesSettings
{
    [JsonInclude] public bool AutoFetchAchievements = true;
    [JsonInclude] public bool AutoShowIfIncomplete = true;
    [JsonInclude] public bool ShowDebugUI;
    [JsonInclude] public DeliveryJobChoice CraftJobType = DeliveryJobChoice.Specific;
    [JsonInclude] public uint SelectedCraftJob = 8;
    [JsonInclude] public uint SelectedGatherJob = 16;
    [JsonInclude] public DeliveryTypes AllowedTypes = DeliveryTypes.Crafting | DeliveryTypes.Mining | DeliveryTypes.Botany | DeliveryTypes.Fishing;
    [JsonInclude] public DeliveryNpcPolicy NpcPolicy = DeliveryNpcPolicy.ClosestTo150;
    [JsonInclude] public List<uint> EligibleCraftJobs = [8, 9, 10, 11, 12, 13, 14, 15];
    [JsonInclude] public List<uint> EligibleGatherJobs = [16, 17];
    private uint fishingBaitId = FishingStockItemIds.VersatileLure;
    [JsonInclude] public uint FishingBaitId
    {
        get => fishingBaitId;
        set => fishingBaitId = value == 0 ? FishingStockItemIds.VersatileLure : value;
    }
    [JsonInclude] public string FishingPresetName = string.Empty;

    public CustomDeliveriesSettings Clone() => new()
    {
        AutoFetchAchievements = AutoFetchAchievements,
        AutoShowIfIncomplete = AutoShowIfIncomplete,
        ShowDebugUI = ShowDebugUI,
        CraftJobType = CraftJobType,
        SelectedCraftJob = SelectedCraftJob,
        SelectedGatherJob = SelectedGatherJob,
        AllowedTypes = AllowedTypes,
        NpcPolicy = NpcPolicy,
        EligibleCraftJobs = [.. EligibleCraftJobs],
        EligibleGatherJobs = [.. EligibleGatherJobs],
        FishingBaitId = FishingBaitId,
        FishingPresetName = FishingPresetName,
    };
}
