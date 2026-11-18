using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;

namespace VERMAXION.IPC;

internal sealed class CombatProviderDependency(IDalamudPluginInterface pluginInterface)
{
    private readonly ICallGateSubscriber<bool> godMod = pluginInterface.GetIpcSubscriber<bool>("BossMod.Presets.GetForceDisabled");
    private readonly ICallGateSubscriber<bool> companion = pluginInterface.GetIpcSubscriber<bool>("BMRVariantLoader.Suite.IsReady.v1");

    public string? GetBlocker()
    {
        var godModLoaded = false;
        var rebornLoaded = false;
        var bossModLoaded = false;
        var companionLoaded = false;
        foreach (var plugin in pluginInterface.InstalledPlugins)
        {
            if (!plugin.IsLoaded) continue;
            switch (plugin.InternalName)
            {
                case "GodMod": godModLoaded = true; break;
                case "BossModReborn": rebornLoaded = true; break;
                case "BossMod": bossModLoaded = true; break;
                case "BMRVariantLoader": companionLoaded = true; break;
            }
        }

        if (godModLoaded && (rebornLoaded || bossModLoaded) || rebornLoaded && bossModLoaded)
            return "Multiple BossMod hosts are loaded. Enable only GodMod or only BossModReborn with God Mod Companion.";
        if (godModLoaded)
            return Read(godMod).HasValue ? null : "GodMod is loaded but its preset IPC is unavailable. Update or reload GodMod.";
        if (rebornLoaded && companionLoaded)
            return Read(companion) == true ? null : "God Mod Companion is not ready on BossModReborn. Check that both plugins are compatible and enabled.";
        if (rebornLoaded)
            return "Enable God Mod Companion alongside BossModReborn, or switch to GodMod.";
        if (companionLoaded)
            return "Enable a supported BossModReborn host for God Mod Companion, or switch to GodMod.";

        return "Enable GodMod or God Mod Companion with a supported BossModReborn host.";
    }

    private static bool? Read(ICallGateSubscriber<bool> subscriber)
    {
        try { return subscriber.HasFunction ? subscriber.InvokeFunc() : null; }
        catch (Exception) { return null; }
    }
}
