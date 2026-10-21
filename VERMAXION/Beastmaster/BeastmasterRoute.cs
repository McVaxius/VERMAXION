using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using ECommons.GameFunctions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Newtonsoft.Json.Linq;

namespace VERMAXION.Beastmaster;

// Executes only the five bundled Crucible routes; no AutoDuty engine or type-name deserialization.
internal sealed unsafe class BeastmasterRoute
{
    private readonly BeastmasterService service;
    private readonly JArray actions;
    private bool started;
    private bool positioned;
    private bool movementStarted;
    private bool battleObserved;
    private DateTime stepSince;
    private DateTime movementSince;
    private ulong autoAttackTarget;
    private bool recoveringPosition;
    private bool recoveringArena;
    public int Step { get; private set; }
    internal int StepCount => actions.Count;
    internal bool AllowsCombat => (recoveringPosition ? recoveringArena : Step < actions.Count &&
        actions[Step]!.Value<string>("Name") == "Boss") && !BlockingMenu();

    internal BeastmasterRoute(BeastmasterService service, uint territory, bool resume = false)
    {
        this.service = service;
        if (!BeastmasterService.IsCrucibleTerritory(territory)) throw new InvalidOperationException("Unsupported Crucible board");
        var assembly = typeof(BeastmasterRoute).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.Contains($"({territory})", StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException("The Crucible route is missing");
        using var reader = new StreamReader(stream);
        actions = (JArray?)JObject.Parse(reader.ReadToEnd())["Actions"] ?? throw new InvalidOperationException("The Crucible route has no actions");
        recoveringPosition = resume;
        recoveringArena = resume && Svc.Objects.LocalPlayer is { } player &&
            BeastmasterSelection.ResumeRoute(actions, player.Position) == null;
    }

    internal void Pause() { started = false; movementStarted = false; }

    // The service stops owned movement before calling this once for a death/revival.
    // Re-read the same action without treating the lost battle target as a victory.
    internal void ResetAfterDeath() => ResetStep();

    internal void ObserveArena()
    {
        if (recoveringPosition || BlockingMenu() || Svc.Objects.LocalPlayer is not { IsDead: false } player)
            return;
        // Live enemies and arena entry override unfinished board actions. An
        // idle, targetable hostile must be pulled before InCombat can become true.
        if (!Svc.Condition[ConditionFlag.InCombat] && !NearbyEnemies(player.Position).Any() &&
            BeastmasterSelection.ResumeRoute(actions, player.Position) != null) return;
        recoveringPosition = recoveringArena = true;
        ResetStep();
        service.SetStatus("Engaging the current Crucible arena");
    }

    internal void Update()
    {
        if (Svc.Objects.LocalPlayer is not { IsDead: false } player) return;
        ObserveArena();
        if (Step >= actions.Count && !recoveringPosition) return;
        if (recoveringPosition)
        {
            if (BlockingMenu()) return; // Let the existing handlers settle this tile first.
            var resume = BeastmasterSelection.ResumeRoute(actions, player.Position);
            recoveringArena = resume == null || Svc.Condition[ConditionFlag.InCombat] || NearbyEnemies(player.Position).Any();
            if (recoveringArena) { UpdateBattle(); return; }
            if (!BeastmasterPlayer.IsReady || Svc.Condition[ConditionFlag.SufferingStatusAffliction2] ||
                Svc.Condition[ConditionFlag.SufferingStatusAffliction63]) return;
            Step = resume!.Value;
            recoveringPosition = false;
            ResetStep();
            service.SetStatus($"Resuming board route {Step + 1}/{actions.Count}");
        }
        var action = (JObject)actions[Step]!;
        var name = action.Value<string>("Name") ?? "";
        if (!started && ((JArray?)action["Conditions"] ?? []).Any(c => !Condition((JObject)c))) { Advance(); return; }
        if (BlockingMenu()) return;
        if (!started)
        {
            started = true; stepSince = DateTime.UtcNow;
            service.SetStatus($"Board route {Step + 1}/{actions.Count}: {name}");
        }
        var args = (JArray?)action["Arguments"] ?? [];
        var arg = args.First?.Value<string>() ?? "";
        switch (name)
        {
            case "Wait":
                // AutoDuty approaches an action's position before executing it. These
                // positioned waits are the shop, treasure and campsite tile visits.
                var waitPosition = Vector(action["Position"]);
                if (waitPosition != Vector3.Zero && !positioned)
                {
                    if (!Approach(waitPosition)) return;
                    positioned = true;
                    stepSince = DateTime.UtcNow;
                }
                if (!Svc.Condition[ConditionFlag.InCombat] && DateTime.UtcNow - stepSince >= TimeSpan.FromMilliseconds(int.Parse(arg))) Advance();
                break;
            case "WaitFor":
                if (arg == "IsReady" ? BeastmasterPlayer.IsReady : arg == "Addon;XBMResult" && CrucibleUi.IsOpen(CrucibleUi.ResultWindow)) Advance();
                break;
            case "ModifyIndex":
                if (!int.TryParse(arg, out var offset)) throw new InvalidOperationException("Invalid Crucible route jump");
                Step = arg.StartsWith('+') || arg.StartsWith('-') ? Step + offset + 1 : offset + 1;
                if (Step < 0 || Step > actions.Count) throw new InvalidOperationException("Crucible route jump is outside the board");
                ResetStep();
                break;
            case "MoveTo":
                if (Approach(Vector(action["Position"]))) Advance();
                break;
            case "Boss":
                UpdateBattle();
                break;
            default:
                throw new InvalidOperationException($"Unsupported Crucible-only route action: {name}");
        }
    }

    private void UpdateBattle()
    {
        var inCombat = Svc.Condition[ConditionFlag.InCombat];
        if (inCombat) battleObserved = true;
        var playerPosition = Svc.Objects.LocalPlayer!.Position;
        var enemies = NearbyEnemies(playerPosition).ToList();
        var target = service.ReadCombatTarget();
        var boss = enemies.FirstOrDefault(e => e.EntityId == target.InstanceId);
        // BMR normally leaves idle enemies unprioritized. Keep the opening pull,
        // but never substitute our own target during an active encounter.
        if (boss == null && target.InstanceId == 0 && target.CanPull && !inCombat)
            boss = enemies.OrderBy(e => Vector3.DistanceSquared(e.Position, playerPosition)).FirstOrDefault();
        if (boss == null && (inCombat || enemies.Count > 0))
        {
            // No eligible target is not a victory. Keep BMR dodging while RSR
            // and character autoattacks wait for BMR to allow a target again.
            if (!service.StartCombat(allowAttacks: false)) return;
            var ui = UIState.Instance();
            var actionManager = ActionManager.Instance();
            if (ui != null && actionManager != null && ui->WeaponState.AutoAttackState.IsAutoAttacking)
                actionManager->UseAction(ActionType.GeneralAction, 1);
            Svc.Targets.Target = null;
            autoAttackTarget = 0;
            return;
        }
        if (boss != null)
        {
            if (Svc.Targets.Target?.EntityId != boss.EntityId) Svc.Targets.Target = boss;
            // Enable RSR and BMR's approach immediately, including before the
            // first pull. Waiting for combat or three yards from its centre can
            // leave an idle enemy unengaged indefinitely.
            if (!service.StartCombat()) return;
            if (autoAttackTarget != boss.GameObjectId)
            {
                var ui = UIState.Instance();
                var actionManager = ActionManager.Instance();
                if (ui != null && actionManager != null && (ui->WeaponState.AutoAttackState.IsAutoAttacking ||
                    actionManager->UseAction(ActionType.GeneralAction, 1, boss.GameObjectId)))
                    autoAttackTarget = boss.GameObjectId;
            }
        }
        if (battleObserved && !inCombat && enemies.Count == 0)
        {
            service.StopNavigation();
            if (recoveringPosition) service.SuspendCombat();
            else Advance();
        }
    }

    private static IEnumerable<IBattleNpc> NearbyEnemies(Vector3 playerPosition)
        => Svc.Objects.OfType<IBattleNpc>().Where(enemy => enemy.IsTargetable && enemy.CurrentHp > 0 &&
            enemy.IsHostile() && Vector3.DistanceSquared(enemy.Position, playerPosition) < 50 * 50);

    private bool Approach(Vector3 position)
    {
        if (Svc.Condition[ConditionFlag.InCombat])
        { service.StopNavigation(); movementStarted = false; return false; }
        if (!movementStarted)
        {
            if (!service.MoveTo(position)) return false;
            movementStarted = true;
            movementSince = DateTime.UtcNow;
            return false;
        }
        if (DateTime.UtcNow - movementSince < TimeSpan.FromMilliseconds(500) || !service.NavigationSettled()) return false;
        service.StopNavigation();
        movementStarted = false;
        return true;
    }

    private static bool BlockingMenu() => new[]
    {
        CrucibleUi.BoardLayout, CrucibleUi.TeamWindow, CrucibleUi.ShopWindow, CrucibleUi.LootWindow,
        CrucibleUi.TreasureWindow, CrucibleUi.ResultWindow, CrucibleUi.YesNo,
    }.Any(CrucibleUi.IsOpen);

    private static bool Condition(JObject condition)
    {
        var type = condition.Value<string>("$type") ?? "";
        if (type.Contains("ConditionOr,")) return ((JArray?)condition["conditions"] ?? []).Any(c => Condition((JObject)c));
        if (type.Contains("ConditionConditionFlag,"))
            return Enum.TryParse<ConditionFlag>(condition.Value<string>("flag"), out var flag) && Svc.Condition[flag];
        if (type.Contains("ConditionObjectData,"))
        {
            var obj = Svc.Objects.FirstOrDefault(o => o.BaseId == condition.Value<uint>("baseId"));
            return obj != null && condition.Value<string>("property") == "IsTargetable" && obj.IsTargetable == (condition.Value<int>("value") != 0);
        }
        if (type.Contains("ConditionDistance,"))
        {
            var origin = Vector(condition["originLoc"]);
            Vector3? target = condition.Value<string>("target") == "Object"
                ? Svc.Objects.FirstOrDefault(o => o.BaseId == condition.Value<uint>("targetId"))?.Position
                : Svc.Objects.LocalPlayer?.Position;
            if (!target.HasValue) return false;
            var distance = Vector3.Distance(origin, target.Value);
            return condition.Value<string>("operatorValue") == "<" ? distance < condition.Value<float>("distance") : distance > condition.Value<float>("distance");
        }
        throw new InvalidOperationException("Unsupported Crucible route condition");
    }

    private static Vector3 Vector(JToken? token) => token == null ? Vector3.Zero : new(token.Value<float>("X"), token.Value<float>("Y"), token.Value<float>("Z"));
    private void Advance() { Step++; ResetStep(); }
    private void ResetStep() { started = false; movementStarted = false; positioned = false; battleObserved = false; autoAttackTarget = 0; }
}
