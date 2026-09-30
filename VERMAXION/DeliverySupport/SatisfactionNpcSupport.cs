// Adapted from Jaksuhn/clib 1.0.42 SatisfactionNpcExtensions; English achievement matching.
using System;
using System.Linq;
using Lumina.Excel.Sheets;
using VERMAXION.CustomDeliveries;

namespace VERMAXION.DeliverySupport;

internal static class SatisfactionNpcSupport
{
    internal static uint GetAchievementId(SatisfactionNpc row)
    {
        var name = row.Npc.Value.Singular.ToString();
        return Service.LuminaSheet<Achievement>()?
            .FirstOrDefault(r => r.AchievementCategory.RowId == 22 && r.AchievementTarget.RowId == 46
                && r.Data.Count > 0 && r.Data[0].RowId == 150
                && r.Description.ToString().Contains(name, StringComparison.OrdinalIgnoreCase)).RowId ?? 0;
    }
}
