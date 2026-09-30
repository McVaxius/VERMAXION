using System;

namespace VERMAXION.CustomDeliveries;

internal static class DeliveryPlanning
{
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
