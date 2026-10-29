// Imported from awgil/ffxiv_satisfy revision 1ab3f9f; adapted for VERMAXION.
using System;
using VERMAXION.DeliverySupport;
using System.Threading.Tasks;

namespace VERMAXION.CustomDeliveries;

public sealed class AutoGather(DeliveryRoute route) : AutoCommon(route)
{
    protected override async Task Execute()
    {
        var npc = Route.Npc;
        var remainingTurnins = Route.Count;
        if (remainingTurnins <= 0)
            return; // nothing to do

        if (npc.GatherData == null || npc.CraftData == null)
            throw new Exception("Gather or turn-in data is not initialized");

        await EquipJob(Route.Job);
        if (remainingTurnins - Game.NumItemsInInventory(Route.ItemId, (short)Route.MinCollectibility) > 0)
            await Gather();

        Status = "Teleporting back to Npc";
        await TeleportTo(npc.TerritoryId, npc.CraftData.TurnInLocation);

        Status = $"Turning in {remainingTurnins}x {ItemName(npc.TurnInItems[1])}";
        ErrorIf(Game.NumItemsInInventory(Route.ItemId, (short)Route.MinCollectibility) < Route.Count,
            "Wiggly Questionable finished without enough qualifying custom-delivery collectibles");
        await TurnIn();
    }

    private async Task Gather()
    {
        if (Service.Travel.GetTeleportBlockedReason() is { } gilReason) throw new InvalidOperationException(gilReason);
        var gathering = new WigglyGathering(Route);
        Status = "Gathering with Wiggly Questionable (including travel)";
        using var scope = BeginScope("Gathering");
        using var stop = new OnDispose(gathering.Stop);
        gathering.Start();
        await WaitWhile(gathering.Update, "Waiting for Wiggly Questionable gathering to finish");
    }
}
