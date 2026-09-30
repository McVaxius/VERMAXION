// Imported from awgil/ffxiv_satisfy revision 1ab3f9f; adapted for VERMAXION.
using System;
using System.Collections.Generic;
using System.Linq;
using Lumina.Excel.Sheets;
using System.Numerics;

namespace VERMAXION.CustomDeliveries;

// data & functions needed to fish an item
public sealed class FishData
{
    public uint FishItemId;
    public bool IsSpearFish;
    public uint FishSpotId;
    public uint TerritoryTypeId;
    public Vector3 Center;
    public int Radius; // TODO: no idea what scale it uses?..

    public FishData(uint itemId, uint fishingSpot = 0, uint spearfishingSpot = 0)
    {
        FishItemId = itemId;
        if ((fishingSpot != 0 ? Service.LuminaRow<FishingSpot>(fishingSpot) ?? default
            : Service.LuminaSheet<FishingSpot>()!.FirstOrDefault(s => s.Item.Any(i => i.RowId == FishItemId))) is var fish && fish.RowId != 0)
        {
            FishSpotId = fish.RowId;
            TerritoryTypeId = fish.TerritoryType.RowId;
            Center = Map.PixelCoordsToWorldCoords(fish.X, fish.Z, fish.TerritoryType.Value.Map.RowId);
            Radius = fish.Radius;
        }
        else if (Service.LuminaSheet<SpearfishingItem>()!.FirstOrDefault(s => s.Item.RowId == FishItemId) is var sfish && sfish.RowId != 0)
        {
            IsSpearFish = true;
            FishSpotId = spearfishingSpot != 0 ? spearfishingSpot : sfish.TerritoryType.RowId;
            var fishSpot = Service.LuminaRow<SpearfishingNotebook>(FishSpotId)!.Value;
            TerritoryTypeId = fishSpot.TerritoryType.RowId;
            Center = Map.PixelCoordsToWorldCoords(fishSpot.X, fishSpot.Y, fishSpot.TerritoryType.Value.Map.RowId);
            Radius = fishSpot.Radius;
        }
        else
        {
            throw new Exception($"Failed to find fishing location for {itemId}");
        }
    }
}
