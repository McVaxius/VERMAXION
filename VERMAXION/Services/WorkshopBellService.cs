using System;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using VERMAXION.IPC;
using VERMAXION.Models;

namespace VERMAXION.Services;

public sealed class WorkshopBellService
{
    private enum BellRouteState
    {
        Idle,
        Routing,
        WaitingForRoute,
        MovingToBell,
        InteractingBell,
        Complete,
        Failed,
    }

    private const string RetainerListAddonName = "RetainerList";
    private const float MaxBellSearchDistance = 200f;
    private const float BellInteractionDistance = 2f;
    private static readonly TimeSpan RouteSettleDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan RouteTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan BellMoveTimeout = TimeSpan.FromSeconds(90);

    private readonly IPluginLog log;
    private readonly LifestreamIPC lifestream;
    private readonly VNavmeshIPC vnavmesh;
    private BellRouteState state = BellRouteState.Idle;
    private DateTime stateEnteredAt = DateTime.MinValue;
    private DateTime nextActionAt = DateTime.MinValue;
    private DateTime lastNavigationCommandAt = DateTime.MinValue;
    private bool bellInteracted;
    private RefillFromListingsRoute route = RefillFromListingsRoute.Workshop;

    public bool IsActive => state is not (BellRouteState.Idle or BellRouteState.Complete or BellRouteState.Failed);
    public bool IsComplete => state == BellRouteState.Complete;
    public bool IsFailed => state == BellRouteState.Failed;
    public string StatusText { get; private set; } = "Idle.";
    public string LastError { get; private set; } = string.Empty;

    public WorkshopBellService(IPluginLog log, LifestreamIPC lifestream, VNavmeshIPC vnavmesh)
    {
        this.log = log;
        this.lifestream = lifestream;
        this.vnavmesh = vnavmesh;
    }

    public void Start(RefillFromListingsRoute route)
    {
        if (IsActive)
            return;

        Reset();
        this.route = route;
        log.Information($"[WorkshopBell] Start route={route}, mode=Lifestream-first, territory={Plugin.ClientState.TerritoryType}, map={Plugin.ClientState.MapId}");
        SetState(BellRouteState.Routing, $"Routing to {GetRouteLabel(route)}...");
        TickRouting();
    }

    public void Reset()
    {
        vnavmesh.Stop();
        state = BellRouteState.Idle;
        stateEnteredAt = DateTime.MinValue;
        nextActionAt = DateTime.MinValue;
        lastNavigationCommandAt = DateTime.MinValue;
        bellInteracted = false;
        StatusText = "Idle.";
        LastError = string.Empty;
    }

    public void Update()
    {
        if (state is BellRouteState.Idle or BellRouteState.Complete or BellRouteState.Failed)
            return;

        if (DateTime.UtcNow < nextActionAt)
            return;

        switch (state)
        {
            case BellRouteState.Routing:
                TickRouting();
                break;
            case BellRouteState.WaitingForRoute:
                TickWaitingForRoute();
                break;
            case BellRouteState.MovingToBell:
                TickMovingToBell();
                break;
            case BellRouteState.InteractingBell:
                TickInteractingBell();
                break;
        }
    }

    private void TickRouting()
    {
        if (GameHelpers.IsAddonVisible(RetainerListAddonName))
        {
            SetState(BellRouteState.Complete, "Retainer list open.");
            return;
        }

        var command = GetLifestreamCommand(route);
        log.Information($"[WorkshopBell] Lifestream-first: skipping local bell search before /li route. route={route}, command={command}, territory={Plugin.ClientState.TerritoryType}, map={Plugin.ClientState.MapId}");
        log.Information($"[WorkshopBell] Executing selected Lifestream route: /li {command}");
        if (!lifestream.ExecuteCommand(command))
        {
            Fail($"Failed to execute Lifestream {GetRouteLabel(route)} route.");
            return;
        }

        SetState(BellRouteState.WaitingForRoute, $"Waiting for {GetRouteLabel(route)} route...");
        nextActionAt = DateTime.UtcNow.Add(RouteSettleDelay);
    }

    private void TickWaitingForRoute()
    {
        if (GameHelpers.IsAddonVisible(RetainerListAddonName))
        {
            SetState(BellRouteState.Complete, "Retainer list open.");
            return;
        }

        if (DateTime.UtcNow - stateEnteredAt > RouteTimeout)
        {
            Fail($"Timed out waiting for Lifestream {GetRouteLabel(route)} route.");
            return;
        }

        if (lifestream.IsBusy())
        {
            StatusText = "Waiting for Lifestream...";
            nextActionAt = DateTime.UtcNow.AddSeconds(1);
            return;
        }

        if (!TryFindNearestBell(out var bell, out var distance))
        {
            Fail($"No Summoning Bell found after Lifestream {GetRouteLabel(route)} route.");
            return;
        }

        SetState(distance > BellInteractionDistance
            ? BellRouteState.MovingToBell
            : BellRouteState.InteractingBell,
            distance > BellInteractionDistance
                ? $"Moving to {GetRouteLabel(route)} bell... ({distance:F1}y)"
                : "Opening retainer bell...");
    }

    private void TickMovingToBell()
    {
        if (GameHelpers.IsAddonVisible(RetainerListAddonName))
        {
            SetState(BellRouteState.Complete, "Retainer list open.");
            return;
        }

        if (DateTime.UtcNow - stateEnteredAt > BellMoveTimeout)
        {
            Fail($"Timed out moving to {GetRouteLabel(route)} bell.");
            return;
        }

        if (!TryFindNearestBell(out var bell, out var distance))
        {
            Fail($"No Summoning Bell found after {GetRouteLabel(route)} route.");
            return;
        }

        if (distance > BellInteractionDistance)
        {
            MoveTo(bell.Position, $"Moving to {GetRouteLabel(route)} bell... ({distance:F1}y)");
            return;
        }

        vnavmesh.Stop();
        SetState(BellRouteState.InteractingBell, "Opening retainer bell...");
    }

    private void TickInteractingBell()
    {
        if (GameHelpers.IsAddonVisible(RetainerListAddonName))
        {
            SetState(BellRouteState.Complete, "Retainer list open.");
            return;
        }

        if (!bellInteracted)
        {
            if (!TryFindNearestBell(out var bell, out var distance))
            {
                Fail("No Summoning Bell found before interaction.");
                return;
            }

            if (distance > BellInteractionDistance)
            {
                SetState(BellRouteState.MovingToBell, $"Moving to {GetRouteLabel(route)} bell... ({distance:F1}y)");
                return;
            }

            vnavmesh.Stop();
            Svc.Targets.Target = bell;
            if (GameHelpers.InteractWithObject(bell))
            {
                bellInteracted = true;
                nextActionAt = DateTime.UtcNow.AddSeconds(2);
                return;
            }
        }

        StatusText = "Waiting for retainer list...";
        nextActionAt = DateTime.UtcNow.AddSeconds(1);
    }

    private bool TryFindNearestBell(out IGameObject bell, out float distance)
    {
        var player = Svc.Objects.LocalPlayer;
        bell = null!;
        distance = float.MaxValue;
        if (player == null)
            return false;

        var nearest = Svc.Objects
            .Where(IsBellCandidate)
            .Select(obj => new
            {
                Bell = obj,
                Distance = Vector3.Distance(player.Position, obj.Position),
            })
            .Where(candidate => candidate.Distance <= MaxBellSearchDistance)
            .OrderBy(candidate => candidate.Distance)
            .FirstOrDefault();

        if (nearest == null)
            return false;

        bell = nearest.Bell;
        distance = nearest.Distance;
        return true;
    }

    private static bool IsBellCandidate(IGameObject? obj)
        => obj is { IsTargetable: true } and not IPlayerCharacter &&
           obj.Name.TextValue.Contains("Summoning Bell", StringComparison.OrdinalIgnoreCase);

    private void MoveTo(Vector3 position, string status)
    {
        if ((DateTime.UtcNow - lastNavigationCommandAt).TotalSeconds >= 2)
        {
            lastNavigationCommandAt = DateTime.UtcNow;
            vnavmesh.PathfindAndMoveTo(position);
        }

        StatusText = status;
        nextActionAt = DateTime.UtcNow.AddSeconds(1);
    }

    private void SetState(BellRouteState newState, string status)
    {
        log.Debug($"[WorkshopBell] State: {state} -> {newState}");
        state = newState;
        stateEnteredAt = DateTime.UtcNow;
        StatusText = status;
        if (newState == BellRouteState.InteractingBell)
            bellInteracted = false;
    }

    private void Fail(string error)
    {
        LastError = error;
        StatusText = error;
        vnavmesh.Stop();
        SetState(BellRouteState.Failed, error);
        log.Warning($"[WorkshopBell] {error}");
    }

    private static string GetLifestreamCommand(RefillFromListingsRoute route)
        => route switch
        {
            RefillFromListingsRoute.Inn => "inn",
            RefillFromListingsRoute.Limsa => "limsa",
            _ => "ws",
        };

    private static string GetRouteLabel(RefillFromListingsRoute route)
        => route switch
        {
            RefillFromListingsRoute.Inn => "inn",
            RefillFromListingsRoute.Limsa => "limsa",
            _ => "workshop",
        };
}
