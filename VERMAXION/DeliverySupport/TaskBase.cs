// Adapted subset of Jaksuhn/clib 1.0.42 TaskSystem/TaskBase.cs required by VSatisfy.
// Movement uses VERMAXION's existing navigator rather than importing movement hooks.
using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using VERMAXION.CustomDeliveries;
using VERMAXION.Services;
using VERMAXION.Models;

namespace VERMAXION.DeliverySupport;

public readonly record struct MovementConfig(float Tolerance, bool MapCenter = false)
{
    public static MovementConfig InteractRange => new(3);
    public static MovementConfig Everything => new(3, true);
    public MovementConfig WithTolerance(float value) => this with { Tolerance = value };
}
[Flags]
public enum UiSkipOptions { None = 0, Talk = 1 }

public abstract class TaskBase : AutoTask
{
    protected async Task MoveTo(uint territoryId, Vector3 position, MovementConfig config,
        bool allowTeleportIfFaster = false, bool allowAethernetWithinTerritory = false)
    {
        await TeleportTo(territoryId, position);
        await MoveTo(position, config);
    }

    protected async Task MoveTo(Vector3 position, MovementConfig config,
        bool allowTeleportIfFaster = false, bool allowAethernet = false)
    {
        CancelToken.ThrowIfCancellationRequested();
        await WaitUntil(GameHelpers.IsPlayerAvailable, "Waiting for player");
        await Dismount();
        var navigation = Service.Navigation;
        await WaitUntil(() => navigation.TryGetNavReady(out var ready) && ready, "Waiting for navigation");
        var found = config.MapCenter
            ? navigation.TryFindReachablePointOnFloor(position, 10, out var target)
            : navigation.TryFindReachablePointNear(position, config.Tolerance, out target);
        ErrorIf(!found, $"No reachable position at the destination's height near {position}");
        var arrival = config.MapCenter ? target : position;
        if (Service.Objects.LocalPlayer is { } player && Vector3.Distance(player.Position, arrival) <= config.Tolerance)
            return;
        var territory = Service.ClientState.TerritoryType;
        using var stop = new OnDispose(() => navigation.Stop());
        Service.Log.Information($"[CustomDeliveries][Movement] Destination: requested={position}; resolved={target}; arrival={arrival}; tolerance={config.Tolerance}");
        navigation.Stop();
        await WaitUntil(() => navigation.TryGetPathfindInProgress(out var pending) && !pending,
            "Waiting for previous navigation query");
        navigation.Stop();
        await Repath();
        var nextMovementCheck = DateTime.MinValue;
        var nextDiagnostic = DateTime.MinValue;
        while (true)
        {
            CancelToken.ThrowIfCancellationRequested();
            ErrorIf(Service.ClientState.TerritoryType != territory, "Territory changed during custom-delivery movement");
            if (Service.Objects.LocalPlayer is { } current && Vector3.Distance(current.Position, arrival) <= config.Tolerance)
            {
                Service.Log.Information($"[CustomDeliveries][Movement] Arrived: current={current.Position}; requested={position}; distance={Vector3.Distance(current.Position, arrival):F2}");
                break;
            }
            var now = DateTime.UtcNow;
            if (now >= nextMovementCheck)
            {
                nextMovementCheck = now + TimeSpan.FromMilliseconds(500);
                if (navigation.TryGetNavReady(out var ready) && ready
                    && GameHelpers.IsPlayerAvailable()
                    && navigation.TryGetPathIsRunning(out var running))
                {
                    var action = navigation.EvaluateDeliveryMovement(target);
                    var local = Service.Objects.LocalPlayer!.Position;
                    var distance = Vector3.Distance(local, arrival);
                    Status = $"Moving to destination ({distance:F1}y remaining)";
                    if (action != GroundNavigationRecoveryAction.Suppress || now >= nextDiagnostic)
                    {
                        nextDiagnostic = now.AddSeconds(12);
                        Service.Log.Information($"[CustomDeliveries][Movement] current={local}; requested={position}; resolved={target}; distance={distance:F2}; pathRunning={running}; recovery={action}");
                    }
                    if (action != GroundNavigationRecoveryAction.Suppress)
                    {
                        // FrenRider/ADS/LootGoblin stop the old route before repathing.
                        // Reuse the existing 12-second tracker; a jump is not XZ progress.
                        navigation.Stop();
                        if (action == GroundNavigationRecoveryAction.Recover)
                            GameHelpers.SendJump();
                        await Repath();
                    }
                }
            }
            await NextFrame();
        }
        navigation.Stop();
        await NextFrame();

        async Task Repath()
        {
            CancelToken.ThrowIfCancellationRequested();
            // Seed the same recovery window after stopping/resetting the navigator.
            navigation.EvaluateDeliveryMovement(target);
            var path = navigation.FindDeliveryPath(target, CancelToken);
            Status = "Pathfinding to destination";
            await WaitUntil(() => path.IsCompleted, "Waiting for delivery pathfinding");
            CancelToken.ThrowIfCancellationRequested();
            ErrorIf(Service.ClientState.TerritoryType != territory, "Territory changed during custom-delivery pathfinding");
            var points = path.GetAwaiter().GetResult();
            ErrorIf(points.Count == 0, $"Navigation returned an empty path to {target}");
            navigation.FollowDeliveryPath(points);
            Service.Log.Information($"[CustomDeliveries][Movement] Pathfinding completed: target={target}; waypoints={points.Count}; following path");
        }
    }

    protected async Task TeleportTo(uint territoryId, Vector3 destination)
    {
        CancelToken.ThrowIfCancellationRequested();
        if (Service.ClientState.TerritoryType == territoryId) return;
        await WaitUntil(GameHelpers.IsPlayerAvailable, "Waiting before travel");
        if (territoryId == 886)
        {
            // Lifestream already owns travel through Ishgard's special Firmament connection.
            ErrorIf(!Plugin.CommandManager.ProcessCommand("/li firmament"), "Lifestream could not travel to the Firmament");
        }
        else
        {
            var closest = VERMAXION.CustomDeliveries.Map.FindClosestAetheryte(territoryId, destination);
            var primary = VERMAXION.CustomDeliveries.Map.FindPrimaryAetheryte(closest);
            ErrorIf(primary == 0 || !TryTeleport(primary), $"Aetheryte {primary} is unavailable for territory {territoryId}");
        }
        Status = $"Travelling to territory {territoryId}";
        await WaitUntil(() => Service.ClientState.TerritoryType == territoryId && Game.IsTerritoryLoaded()
            && GameHelpers.IsPlayerAvailable(), "Waiting for travel");
        await NextFrame();
    }

    private static unsafe bool TryTeleport(uint id)
        => UIState.Instance() != null && UIState.Instance()->IsAetheryteUnlocked(id)
            && Telepo.Instance() != null && Telepo.Instance()->Teleport(id, 0);

    protected async Task Dismount()
    {
        if (!Service.Conditions[ConditionFlag.Mounted]) return;
        Plugin.CommandManager.ProcessCommand("/mount");
        await WaitUntil(() => !Service.Conditions[ConditionFlag.Mounted], "Dismounting");
    }

    protected async Task EquipJob(uint job)
    {
        if (Service.PlayerState.ClassJob.RowId == job) return;
        Status = $"Equipping job {job}";
        ErrorIf(!TryEquip(job), $"No saved usable gearset for job {job}");
        await WaitUntil(() => Service.PlayerState.ClassJob.RowId == job, "Waiting for gearset");
    }

    private static unsafe bool TryEquip(uint job)
    {
        var module = RaptureGearsetModule.Instance();
        if (module == null) return false;
        for (var index = 0; index < module->Entries.Length; ++index)
        {
            var entry = module->Entries[index];
            if (entry.ClassJob == job && (entry.Flags & RaptureGearsetModule.GearsetFlag.Exists) != 0)
                return module->EquipGearset(entry.Id) == 0;
        }
        return false;
    }

    protected async Task InteractWithNpc(uint baseId, ulong instanceId, Vector3 waypoint,
        Func<bool> waitUntil, Func<IGameObject, bool>? interact = null, int? selectStringIndex = 0)
    {
        CancelToken.ThrowIfCancellationRequested();
        var territory = Service.ClientState.TerritoryType;
        var label = Service.LuminaRow<Lumina.Excel.Sheets.ENpcResident>(baseId)?.Singular.ToString() ?? $"NPC {baseId}";
        var started = DateTime.UtcNow;
        var nextAction = DateTime.MinValue;
        var nextDiagnostic = DateTime.MinValue;
        var interacted = false;
        while (!waitUntil())
        {
            CancelToken.ThrowIfCancellationRequested();
            ErrorIf(Service.ClientState.TerritoryType != territory, $"Territory changed while opening {label}");
            var now = DateTime.UtcNow;
            ErrorIf(now - started > TimeSpan.FromSeconds(90), $"Timed out opening {label}: {Status}");
            if (now < nextAction)
            {
                await NextFrame();
                continue;
            }
            nextAction = now.AddMilliseconds(500);
            // Like Cactpot, reach the known waypoint before requiring a loaded NPC,
            // then re-resolve the live object and its interaction range after stopping.
            var obj = Service.Objects.Where(o => o.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventNpc
                && ((baseId != 0 && o.BaseId == baseId) || (instanceId != 0 && o.GameObjectId == instanceId)))
                .OrderByDescending(o => o.GameObjectId == instanceId)
                .ThenBy(o => Vector3.DistanceSquared(o.Position, waypoint)).FirstOrDefault();
            var player = Service.Objects.LocalPlayer;
            var distance = obj != null && player != null ? Vector3.Distance(player.Position, obj.Position) : float.NaN;
            var maxDistance = obj != null ? GameHelpers.GetValidInteractionDistance(obj) : 0;
            if (now >= nextDiagnostic)
            {
                nextDiagnostic = now.AddSeconds(5);
                Service.Log.Information($"[CustomDeliveries][Interaction] {label}: loaded={obj != null}; targetable={obj?.IsTargetable}; distance={distance:F2}; range={maxDistance:F2}; shop/dialog ready={waitUntil()}; Talk={Game.IsTalkInProgress()}; SelectString={GameHelpers.IsAddonVisible("SelectString")}; SelectIconString={GameHelpers.IsAddonVisible("SelectIconString")}");
            }
            // Event interaction can make the NPC untargetable. Finish the owned
            // dialogue/menu before deciding that the object needs to spawn again.
            if (interacted && Game.IsTalkInProgress())
                Game.ProgressTalk();
            else if (interacted && selectStringIndex.HasValue && GameHelpers.IsAddonVisible("SelectString"))
                GameHelpers.FireAddonCallback("SelectString", true, selectStringIndex.Value);
            else if (interacted && interact != null && obj != null
                && (GameHelpers.IsAddonVisible("SelectString") || GameHelpers.IsAddonVisible("SelectIconString")))
            {
                if (interact(obj))
                {
                    nextAction = now.AddSeconds(1);
                    Service.Log.Information($"[CustomDeliveries][Interaction] Menu dispatched for {label}; waiting for the owned UI");
                }
            }
            else if (obj == null || !obj.IsTargetable)
            {
                Status = $"Waiting for {label} to load at the destination";
                if (player != null && GameHelpers.IsPlayerAvailable() && Vector3.Distance(player.Position, waypoint) > 1.5f)
                    await MoveTo(waypoint, MovementConfig.InteractRange.WithTolerance(1.5f));
            }
            else if (GameHelpers.IsPlayerAvailable() && distance > maxDistance)
            {
                Status = $"Closing to {label} ({distance:F1}y; interaction range {maxDistance:F1}y)";
                await MoveTo(obj.Position, MovementConfig.InteractRange.WithTolerance(MathF.Max(0.5f, maxDistance - 0.35f)));
            }
            else
            {
                Status = $"Opening {label}";
                // A dispatched interaction is not proof that its UI opened. Recheck
                // and retry, including a shop selector that appears on a later frame.
                if ((interact ?? GameHelpers.InteractWithObject)(obj))
                {
                    interacted = true;
                    nextAction = now.AddSeconds(1);
                    Service.Log.Information($"[CustomDeliveries][Interaction] Interaction/menu dispatched for {label}; waiting for the owned UI");
                }
            }
            await NextFrame();
        }
        Service.Log.Information($"[CustomDeliveries][Interaction] Owned UI opened for {label}");
    }

    protected async Task WaitUntilSkipping(Func<bool> condition, string scopeName, UiSkipOptions skip,
        int? selectStringIndex = null)
    {
        var started = DateTime.UtcNow;
        var lastClick = DateTime.MinValue;
        while (!condition())
        {
            CancelToken.ThrowIfCancellationRequested();
            ErrorIf(DateTime.UtcNow - started > TimeSpan.FromSeconds(90), $"Timed out: {scopeName}");
            if (DateTime.UtcNow - lastClick >= TimeSpan.FromMilliseconds(500))
            {
                if (skip.HasFlag(UiSkipOptions.Talk) && Game.IsTalkInProgress())
                {
                    Game.ProgressTalk();
                    lastClick = DateTime.UtcNow;
                }
                else if (selectStringIndex.HasValue && GameHelpers.IsAddonVisible("SelectString"))
                {
                    GameHelpers.FireAddonCallback("SelectString", true, selectStringIndex.Value);
                    lastClick = DateTime.UtcNow;
                }
            }
            await NextFrame();
        }
    }
}
