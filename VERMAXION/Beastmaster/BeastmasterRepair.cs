using System;
using VERMAXION.Models;

namespace VERMAXION.Beastmaster;

internal static class BeastmasterRepairPolicy
{
    internal static string ToAdsMode(BeastmasterRepairMode mode, RepairCity city = RepairCity.Automatic)
        => FishingRepairPolicy.ApplyCity(mode switch
    {
        BeastmasterRepairMode.Self => "self",
        BeastmasterRepairMode.NpcNoInn => "npc-no-inn",
        BeastmasterRepairMode.NpcNearby => "npc-no-teleport-no-inn",
        BeastmasterRepairMode.NpcInn => "npc-yes-inn",
        _ => string.Empty,
    }, city);

    internal static bool ShouldRepair(BeastmasterRepairMode mode, int threshold, int? lowestDurabilityPercent)
        => ToAdsMode(mode).Length > 0 && Math.Clamp(threshold, 0, 100) > 0 &&
           lowestDurabilityPercent is >= 0 && lowestDurabilityPercent < Math.Clamp(threshold, 0, 100);

    // Non-repairable equipment (including soul crystals) has no repair job/material.
    // Condition zero on a repairable item is broken equipment, not missing data.
    internal static int? ConditionPercent(uint repairClassJobId, uint repairMaterialId, ushort condition)
        => repairClassJobId != 0 && repairMaterialId != 0 ? condition / 300 : null;

    internal static bool CanCancelOwnedRequest(bool requestAccepted, DateTime requestedAtUtc, string requestedMode,
        bool statusReadable, bool running, string task, string mode, DateTime? completedAtUtc)
        => requestAccepted && requestedAtUtc != default && statusReadable && running &&
           (completedAtUtc == null || completedAtUtc < requestedAtUtc) &&
           IsOwnedMode(requestedMode) &&
           string.Equals(requestedMode, mode, StringComparison.Ordinal) &&
           string.Equals(task, requestedMode == "extract-materia" ? "materia extraction"
               : requestedMode == "self" ? "self-repair" : "NPC repair", StringComparison.Ordinal);

    private static bool IsOwnedMode(string mode)
    {
        if (mode is "self" or "npc-no-inn" or "npc-no-teleport-no-inn" or "npc-yes-inn" or "extract-materia") return true;
        foreach (var city in Enum.GetValues<RepairCity>())
            if (mode == FishingRepairPolicy.ApplyCity("npc-no-inn", city) ||
                mode == FishingRepairPolicy.ApplyCity("npc-yes-inn", city)) return true;
        return false;
    }

    internal static bool ShouldRefreshFood(uint selectedUseId, float wellFedSeconds)
        => selectedUseId != 0 && wellFedSeconds <= 90;

    internal static bool FoodRefreshConfirmed(int priorCount, int? currentCount, float priorRemaining, float remaining)
        => priorCount > 0 && currentCount is >= 0 && currentCount < priorCount && remaining > priorRemaining;

    // ADS clears the task/mode on completion. A fresh idle completion receipt is
    // settlement only; callers still verify actual durability before reporting success.
    internal static bool HasFreshCompletion(DateTime requestedAtUtc, bool readable, bool running, DateTime? completedAtUtc)
        => requestedAtUtc != default && readable && !running && completedAtUtc >= requestedAtUtc;
}
