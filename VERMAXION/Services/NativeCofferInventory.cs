using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace VERMAXION.Services;

internal sealed unsafe class NativeCofferInventory(Plugin plugin)
{
    private static readonly InventoryType[] Bags = [InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4];
    internal sealed record Stack(uint ItemId, string Name, int Quantity);
    internal static bool IsEligible(uint itemId) => Plugin.DataManager.GetExcelSheet<Item>().TryGetRow(itemId, out var item) &&
        item.ItemAction.RowId == 388;

    internal CofferObservation Observe(uint itemId)
    {
        var stacks = ReadBags(out var freeSlots);
        var count = stacks?.Where(stack => stack.ItemId == itemId).Sum(stack => stack.Quantity) ?? 0;
        var first = stacks?.FirstOrDefault(stack => stack.ItemId == itemId)?.Quantity ?? 0;
        var manager = ActionManager.Instance();
        return new(Plugin.ClientState.IsLoggedIn && Plugin.PlayerState.IsLoaded ? Plugin.PlayerState.ContentId : 0,
            stacks != null, IsEligible(itemId), count, first, freeSlots,
            manager != null && manager->AnimationLock == 0 && manager->GetActionStatus(ActionType.Item, itemId) == 0,
            plugin.CofferOpeningBlocker());
    }

    internal IReadOnlyList<Stack> Candidates()
        => (ReadBags(out _) ?? []).Where(stack => IsEligible(stack.ItemId)).DistinctBy(stack => stack.ItemId)
            .OrderBy(stack => stack.Name, StringComparer.CurrentCulture).ToArray();

    private static List<Stack>? ReadBags(out int freeSlots)
    {
        freeSlots = 0;
        if (!Plugin.ClientState.IsLoggedIn || !Plugin.PlayerState.IsLoaded) return null;
        var manager = InventoryManager.Instance();
        if (manager == null) return null;
        var result = new List<Stack>();
        foreach (var type in Bags)
        {
            var container = manager->GetInventoryContainer(type);
            if (container == null || !container->IsLoaded || container->Size is < 1 or > 140) return null;
            for (var index = 0; index < container->Size; index++)
            {
                var slot = container->GetInventorySlot(index);
                if (slot == null) return null;
                if (slot->ItemId == 0 || slot->Quantity == 0) { freeSlots++; continue; }
                if ((slot->Flags & InventoryItem.ItemFlags.HighQuality) != 0 || slot->Quantity > int.MaxValue) continue;
                var name = Plugin.DataManager.GetExcelSheet<Item>().GetRowOrDefault(slot->ItemId)?.Name.ToString() ?? slot->ItemId.ToString();
                result.Add(new(slot->ItemId, name, (int)slot->Quantity));
            }
        }
        return result;
    }
}
