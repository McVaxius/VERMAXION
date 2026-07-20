using System;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using GameFramework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework;

namespace VERMAXION.Services;

internal static unsafe class RecommendedEquipHelper
{
    private static bool isSubscribed;
    private static bool isDisposed;

    internal static void EquipRecommended()
    {
        if (isDisposed)
            return;

        try
        {
            var module = GetModule();
            if (module == null)
            {
                Plugin.Log.Warning("[RecommendedEquip] RecommendEquipModule is unavailable");
                Unsubscribe();
                return;
            }

            module->SetupForClassJob((byte)(Plugin.ObjectTable.LocalPlayer?.ClassJob.RowId ?? 0));

            if (isSubscribed)
                return;

            Plugin.Framework.Update += DoEquip;
            isSubscribed = true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[RecommendedEquip] Failed to set up recommended equipment");
            Unsubscribe();
        }
    }

    private static void DoEquip(IFramework framework)
    {
        try
        {
            var module = GetModule();
            if (module == null || module->EquippedMainHand == null)
            {
                Unsubscribe();
                return;
            }

            module->EquipRecommendedGear();
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[RecommendedEquip] Failed while equipping recommended gear");
            Unsubscribe();
        }
    }

    private static RecommendEquipModule* GetModule()
    {
        var framework = GameFramework.Instance();
        if (framework == null)
            return null;

        var uiModule = framework->GetUIModule();
        return uiModule == null ? null : uiModule->GetRecommendEquipModule();
    }

    private static void Unsubscribe()
    {
        if (!isSubscribed)
            return;

        try
        {
            Plugin.Framework.Update -= DoEquip;
        }
        finally
        {
            isSubscribed = false;
        }
    }

    internal static void Dispose()
    {
        isDisposed = true;
        Unsubscribe();
    }
}
