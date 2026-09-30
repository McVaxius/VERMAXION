namespace VERMAXION.CustomDeliveries;

public sealed record DeliveryRoute(NPCInfo Npc, DeliveryTypes Type, uint Job, int Slot, uint ItemId,
    int Count, ushort MinCollectibility, ushort TargetCollectibility);
