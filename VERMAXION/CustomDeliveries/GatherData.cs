// Imported from awgil/ffxiv_satisfy revision 1ab3f9f; adapted for VERMAXION.
using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using System.Numerics;

namespace VERMAXION.CustomDeliveries;

public sealed class GatherData
{
    public uint GatherItemId;
    public uint ClassJobId;
    public uint CollectabilityLow;
    public uint CollectabilityMid;
    public uint CollectabilityHigh;
    public GatherPoint[] GatherPoints = [];

    public GatherData(uint itemId)
    {
        GatherItemId = itemId;
        if (Service.LuminaSheetSubrow<SatisfactionSupply>()?.Flatten().FirstOrDefault(x => x.Item.RowId == itemId) is { } subrow)
            (CollectabilityLow, CollectabilityMid, CollectabilityHigh) = (subrow.CollectabilityLow, subrow.CollectabilityMid, subrow.CollectabilityHigh);
        if (Service.LuminaSheet<GatheringItem>()?.FirstOrDefault(row => row.Item.RowId == GatherItemId) is { RowId: var item } && Service.LuminaSubrows<GatheringItemPoint>(item) is { } points)
            foreach (var point in points)
                GatherPoints = [.. GatherPoints, GatherPoint.FromSubrow(point)];
    }

    public record struct GatherPoint(uint TerritoryId, Vector2 Position, uint Radius, uint ClassJob)
    {
        public static GatherPoint FromSubrow(GatheringItemPoint point)
        {
            var exportedPoint = Service.LuminaRow<ExportedGatheringPoint>(point.GatheringPoint.Value.GatheringPointBase.RowId)!;
            var pos = new Vector2(exportedPoint.Value.X, exportedPoint.Value.Y);
            var classJob = exportedPoint.Value.GatheringType.RowId switch
            {
                0 or 1 => 16u,
                2 or 3 => 17u,
                4 or 5 => 18u,
                _ => throw new Exception($"Unknown gathering type {exportedPoint.Value.GatheringType.RowId}"),
            };
            return new(point.GatheringPoint.Value.TerritoryType.RowId, pos, exportedPoint.Value.Radius, classJob);
        }
    }

}
