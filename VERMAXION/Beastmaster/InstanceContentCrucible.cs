// Adapted from erdelf/AutoDuty, commit 7e0ede4be688980d217faf6889ffdac53e18e30f.
using System;
using Dalamud.Hooking;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;

namespace VERMAXION.Beastmaster;

internal static unsafe class InstanceContentCrucible
{
    private delegate void UseItemDelegate(InstanceContentDirector* director, uint slot, int beastId);
    private static Hook<UseItemDelegate>? hook;

    internal static void UseItem(uint slot, int beastId)
    {
        var director = EventFramework.Instance()->GetInstanceContentDirector();
        if (director == null || !BeastmasterService.IsCrucibleTerritory(Svc.ClientState.TerritoryType)) return;
        if (hook == null)
        {
            hook = Svc.Hook.HookFromSignature<UseItemDelegate>("E8 ?? ?? ?? ?? 83 7F 44 00 48 8D 57 44", InvokeOriginal);
            hook.Enable();
        }
        hook.Original(director, slot, beastId);
    }

    private static void InvokeOriginal(InstanceContentDirector* director, uint slot, int beastId) => hook!.Original(director, slot, beastId);
    internal static void Dispose() { hook?.Dispose(); hook = null; }
}
