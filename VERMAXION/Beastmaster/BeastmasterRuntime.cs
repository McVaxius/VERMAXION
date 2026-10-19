using System;
using System.Linq;
using System.Numerics;
using ECommons.Automation;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Component.GUI;
using VERMAXION.Services;

namespace VERMAXION.Beastmaster;

internal static class BeastmasterRuntime
{
    internal static BeastmasterService Service { get; set; } = null!;
    internal static BeastmasterSettings Settings => Service.Settings;
    internal static BeastmasterCharacterData CharacterData => Service.CharacterData;
    internal static uint SelectedTerritory => 1338 + Math.Clamp(Settings.Board, 1u, 5u);
    internal static void Save() => Service.Save();
    internal static BeastTierList ActiveTierList
    {
        get
        {
            var settings = Settings;
            if (settings.BeastTierLists.Count == 0) settings.BeastTierLists.Add(BeastTierList.Default());
            settings.BeastTierListIndex = Math.Clamp(settings.BeastTierListIndex, 0, settings.BeastTierLists.Count - 1);
            var list = settings.BeastTierLists[settings.BeastTierListIndex];
            list.Complete(CrucibleTeam.FamiliarSheetDatas.Keys);
            return list;
        }
    }
}

internal static unsafe class BeastmasterAddon
{
    internal static void FireCallBack(AtkUnitBase* addon, bool updateState, params object[] values) => Callback.Fire(addon, updateState, values);
    internal static void ClickTalk()
    {
        var talk = CrucibleUi.Ready("Talk");
        if (talk != null) Callback.Fire(talk, true, 0);
    }
}

internal static class BeastmasterPlayer
{
    internal static bool IsValid => Player.Available && !Svc.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.BetweenAreas]
        && !Svc.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.BetweenAreas51];
    internal static bool IsReady => IsValid && GameHelpers.IsPlayerAvailable();
    internal static bool IsCombatReady => IsValid && Svc.Objects.LocalPlayer is { IsDead: false, CurrentHp: > 0 }
        && !Svc.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.OccupiedInQuestEvent]
        && !Svc.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.OccupiedInCutSceneEvent];
    internal static bool HasStatus(uint status) => Svc.Objects.LocalPlayer?.StatusList.Any(s => s.StatusId == status) == true;
    internal static bool BelowDistanceToPlayer(Vector3 position, float horizontal, float vertical)
    {
        if (Svc.Objects.LocalPlayer is not { } player) return false;
        return Vector2.Distance(new(player.Position.X, player.Position.Z), new(position.X, position.Z)) <= horizontal
            && Math.Abs(player.Position.Y - position.Y) <= vertical;
    }
}
