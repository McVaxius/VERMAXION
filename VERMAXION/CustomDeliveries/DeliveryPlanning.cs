using System;

namespace VERMAXION.CustomDeliveries;

internal static class DeliveryPlanning
{
    internal static int RankTurnins(int npcAllowances, int satisfaction, int required,
        int satisfactionPerItem, bool pendingQuest)
    {
        var count = Math.Max(0, npcAllowances);
        if (pendingQuest) return 0;
        if (required <= 0) return count;
        var remaining = Math.Max(0, required - satisfaction);
        return satisfactionPerItem > 0
            ? Math.Min(count, (int)Math.Ceiling(remaining / (double)satisfactionPerItem)) : 0;
    }

    internal static int AvailableQuestRank(int rank, int satisfaction, int required)
        => rank > 0 && required > 0 && satisfaction >= required ? rank + 1 : rank;

    internal static int Count(DeliveryNpcPolicy policy, int characterAllowances,
        int npcAllowancesAtRank, uint? verifiedProgress)
    {
        var count = Math.Max(0, Math.Min(characterAllowances, npcAllowancesAtRank));
        if (policy == DeliveryNpcPolicy.ClosestTo150)
            count = verifiedProgress is < 150 ? Math.Min(count, (int)(150 - verifiedProgress.Value)) : 0;
        return count;
    }

    internal static int MissingItems(int plannedTurnins, int eligibleInventory)
        => Math.Max(0, plannedTurnins - Math.Max(0, eligibleInventory));

    internal static bool IsScripOverflowPrompt(string prompt)
        => prompt.Contains("Unable to receive the following items:", StringComparison.OrdinalIgnoreCase);
}
