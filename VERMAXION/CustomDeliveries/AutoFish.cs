// Imported from awgil/ffxiv_satisfy revision 1ab3f9f; completed through VERMAXION's existing AutoHook integration.
using System;
using System.Threading;
using System.Threading.Tasks;
using VERMAXION.DeliverySupport;
using FFXIVClientStructs.FFXIV.Client.Game.Event;

namespace VERMAXION.CustomDeliveries;

public sealed class AutoFish(DeliveryRoute route, Func<DeliveryRoute, CancellationToken, Task> fish,
    Action? fishingCleanup) : AutoCommon(route)
{
    protected override async Task Execute()
    {
        var npc = Route.Npc;
        ErrorIf(npc.FishData == null || npc.CraftData == null, "Fishing or turn-in location data is missing");
        await EquipJob(Route.Job);
        // Cleanup stays owned through turn-in, so restoring an enabled AutoHook cannot recast before travel.
        using var restore = new OnDispose(() => fishingCleanup?.Invoke());
        if (Game.NumItemsInInventory(Route.ItemId, (short)Route.MinCollectibility) < Route.Count)
        {
            ErrorIf(npc.FishData!.IsSpearFish, "This request requires spearfishing; rod AutoHook cannot complete it");
            Status = "Travelling to custom-delivery fishing spot";
            await TeleportTo(npc.FishData.TerritoryTypeId, npc.FishData.Center);
            await Dismount();
            // Keep an already valid casting position; map centres are not always the shoreline.
            if (!CanFishHere())
                await MoveTo(npc.FishData.Center, MovementConfig.Everything.WithTolerance(10));
            Status = $"Fishing {ItemName(Route.ItemId)} with AutoHook";
            await fish(Route, CancelToken);
        }
        ErrorIf(Game.NumItemsInInventory(Route.ItemId, (short)Route.MinCollectibility) < Route.Count,
            "Fishing finished without the required qualifying collectible count");
        Status = "Returning to custom-delivery NPC";
        await TeleportTo(npc.TerritoryId, npc.CraftData!.TurnInLocation);
        await MoveTo(npc.CraftData.TurnInLocation, MovementConfig.InteractRange);
        await TurnIn();
    }

    private static unsafe bool CanFishHere()
    {
        var framework = EventFramework.Instance();
        return framework != null && framework->EventHandlerModule.FishingEventHandler != null
            && framework->EventHandlerModule.FishingEventHandler->CanFish;
    }
}
