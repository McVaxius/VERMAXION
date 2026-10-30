using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;

namespace VERMAXION.Beastmaster;

internal static unsafe class InstanceContentCrucible
{
    // The void native call dispatches a request; it does not confirm its effect.
    internal static bool TryUseItem(uint slot, uint itemId, int beastId, out int itemCount)
    {
        itemCount = 0;
        var crucible = Current();
        var actions = ActionManager.Instance();
        if (crucible == null || actions == null || actions->CastSpellId != 0 || actions->AnimationLock > 0 || actions->ActionQueued)
            return false;
        if (slot >= crucible->Inventory.Length || itemId == 0 || crucible->Inventory[(int)slot].ItemId != itemId)
            return false;
        foreach (ref readonly var entry in crucible->Inventory)
            if (entry.ItemId == itemId) ++itemCount;
        crucible->UseItem(slot, beastId);
        return true;
    }

    internal static int? ItemCount(uint itemId)
    {
        var crucible = Current();
        if (crucible == null) return null;
        var count = 0;
        foreach (ref readonly var entry in crucible->Inventory)
            if (entry.ItemId == itemId) ++count;
        return count;
    }

    private static InstanceContentCrucibleOfTheUnbroken* Current()
    {
        var framework = EventFramework.Instance();
        var director = framework == null ? null : framework->GetInstanceContentDirector();
        return director != null && director->InstanceContentType == InstanceContentType.CrucibleOfTheUnbroken &&
            BeastmasterService.IsCrucibleTerritory(Svc.ClientState.TerritoryType)
            ? (InstanceContentCrucibleOfTheUnbroken*)director : null;
    }
}
