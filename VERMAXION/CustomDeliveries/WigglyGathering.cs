using System;
using System.Collections;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.Loader;
using ECommons.Reflection;

namespace VERMAXION.CustomDeliveries;

// WigglyQuest 7.5.27 has no StartGatheringComplex/Stop IPC. Use the same native
// registry and controller calls as its CustomDeliveryController, without starting
// that controller's turn-ins or changing its saved delivery queue/settings.
internal sealed class WigglyGathering
{
    internal const string PluginInternalName = "WigglyQuest";
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly object owner;
    private readonly object controller;
    private readonly object movement;
    private readonly object request;
    private readonly MethodInfo start;
    private readonly MethodInfo update;
    private readonly MethodInfo stop;
    private readonly MethodInfo stopMovement;
    private readonly FieldInfo currentRequest;
    private readonly PropertyInfo isRunning;
    private readonly int missingItems;
    private object? ownedRequest;

    internal uint TerritoryId { get; }
    internal Vector3 Position { get; }

    internal WigglyGathering(DeliveryRoute route)
    {
        owner = ResolvePlugin();
        var services = owner.GetType().GetField("_serviceProvider", Members)?.GetValue(owner) as IServiceProvider
            ?? throw new InvalidOperationException("Wiggly Questionable services are unavailable");
        object GetService(string name) => services.GetService(owner.GetType().Assembly.GetType(name, true)!)
            ?? throw new InvalidOperationException($"Wiggly Questionable {name} is unavailable");
        var registry = GetService("Questionable.Controller.GatheringPointRegistry");
        controller = GetService("Questionable.Controller.GatheringController");
        movement = GetService("Questionable.Controller.MovementController");
        var requestType = controller.GetType().GetNestedType("GatheringRequest", BindingFlags.Public)
            ?? throw new InvalidOperationException("Wiggly Questionable gathering request contract is unavailable");
        start = RequireMethod(controller, "Start", [requestType]);
        update = RequireMethod(controller, "Update", Type.EmptyTypes);
        stop = RequireMethod(controller, "Stop", [typeof(string)]);
        stopMovement = RequireMethod(movement, "Stop", Type.EmptyTypes);
        currentRequest = controller.GetType().GetField("_currentRequest", Members)
            ?? throw new InvalidOperationException("Wiggly Questionable gathering ownership cannot be read");
        isRunning = controller.GetType().GetProperty("IsRunning", Members)
            ?? throw new InvalidOperationException("Wiggly Questionable gathering state cannot be read");
        if (start.ReturnType != typeof(bool) || isRunning.PropertyType != typeof(bool) || !update.ReturnType.IsEnum)
            throw new InvalidOperationException("Wiggly Questionable gathering contract has changed");
        EnsureIdle();

        var find = registry.GetType().GetMethod("TryGetGatheringPointId", Members)
            ?? throw new InvalidOperationException("Wiggly Questionable gathering route lookup is unavailable");
        var args = new object?[] { route.ItemId, Enum.ToObject(find.GetParameters()[1].ParameterType, route.Job), null };
        if (find.Invoke(registry, args) is not true || args[2] == null)
            throw new InvalidOperationException($"Wiggly Questionable has no gathering route for item {route.ItemId}, job {route.Job}");
        var point = args[2]!;
        var read = registry.GetType().GetMethod("TryGetGatheringPoint", Members)
            ?? throw new InvalidOperationException("Wiggly Questionable gathering path lookup is unavailable");
        var pathArgs = new object?[] { point, null };
        if (read.Invoke(registry, pathArgs) is not true || pathArgs[1] == null)
            throw new InvalidOperationException($"Wiggly Questionable has no supported gathering path for item {route.ItemId}");
        var root = pathArgs[1]!;
        var step = ReadList(root, "Steps").LastOrDefault()
            ?? throw new InvalidOperationException("Wiggly Questionable gathering path has no territory");
        TerritoryId = Convert.ToUInt32(step.GetType().GetProperty("TerritoryId")?.GetValue(step));
        var location = ReadList(root, "Groups").SelectMany(group => ReadList(group, "Nodes"))
            .SelectMany(node => ReadList(node, "Locations")).FirstOrDefault();
        if (TerritoryId == 0 || location?.GetType().GetProperty("Position")?.GetValue(location) is not Vector3 position)
            throw new InvalidOperationException("Wiggly Questionable gathering destination is unavailable");
        Position = position;
        missingItems = DeliveryPlanning.MissingItems(route.Count,
            Game.NumItemsInInventory(route.ItemId, (short)route.MinCollectibility));
        // Wiggly's Quantity is an inventory total at its requested quality.
        // Existing lower-quality eligible items still count toward our turn-ins.
        var requestedTotal = Game.NumItemsInInventory(route.ItemId, (short)route.TargetCollectibility) + missingItems;
        request = Activator.CreateInstance(requestType, point, route.ItemId, 0u, requestedTotal, route.TargetCollectibility)
            ?? throw new InvalidOperationException("Wiggly Questionable gathering request could not be created");
    }

    internal static string? GetBlockedReason(DeliveryRoute route)
    {
        try { _ = new WigglyGathering(route); return null; }
        catch (Exception ex) { return ex.GetBaseException().Message; }
    }

    internal void Start()
    {
        EnsureLoaded();
        EnsureIdle();
        Service.Log.Information($"[CustomDeliveries][Gathering] Starting Wiggly request: toProduce={missingItems}; targetInventory={request.GetType().GetProperty("Quantity")?.GetValue(request)}");
        // Capture the native request even if Start throws after acquiring it.
        object? accepted;
        try { accepted = start.Invoke(controller, [request]); }
        finally { ownedRequest = currentRequest.GetValue(controller); }
        if (accepted is not true || ownedRequest == null)
            throw new InvalidOperationException("Wiggly Questionable rejected the gathering request");
    }

    internal bool Update()
    {
        EnsureLoaded();
        var current = currentRequest.GetValue(controller);
        if (current == null) return false;
        if (!ReferenceEquals(current, ownedRequest))
            throw new InvalidOperationException("Another Wiggly Questionable gathering request replaced the owned request");
        // The provider's ordinary delivery controller does this once per frame.
        // It is idle here; VERMAXION drives only this owned gathering request.
        update.Invoke(controller, null);
        return isRunning.GetValue(controller) is true;
    }

    internal void Stop()
    {
        if (ownedRequest == null) return;
        if (DalamudReflector.TryGetDalamudPlugin(PluginInternalName, out object plugin, out AssemblyLoadContext? _, true, true)
            && ReferenceEquals(plugin, owner) && ReferenceEquals(currentRequest.GetValue(controller), ownedRequest))
        {
            try { stop.Invoke(controller, ["VERMAXION custom-delivery cleanup"]); }
            finally { stopMovement.Invoke(movement, null); }
        }
        ownedRequest = null;
    }

    private void EnsureIdle()
    {
        var running = Service.PluginInterface.GetIpcSubscriber<bool>("WigglyQuest.IsRunning");
        if (!running.HasFunction) throw new InvalidOperationException("Wiggly Questionable ownership IPC is unavailable");
        if (running.InvokeFunc() || isRunning.GetValue(controller) is true)
            throw new InvalidOperationException("Wiggly Questionable is already running another task");
    }

    private void EnsureLoaded()
    {
        if (!ReferenceEquals(ResolvePlugin(), owner))
            throw new InvalidOperationException("Wiggly Questionable reloaded during the gathering request");
    }

    private static object ResolvePlugin()
    {
        if (!DalamudReflector.TryGetDalamudPlugin(PluginInternalName, out object plugin, out AssemblyLoadContext? _, true, true)
            || plugin == null) throw new InvalidOperationException("Wiggly Questionable (WigglyQuest) is not loaded");
        return plugin;
    }

    private static MethodInfo RequireMethod(object service, string name, Type[] parameters)
        => service.GetType().GetMethod(name, Members, null, parameters, null)
            ?? throw new InvalidOperationException($"Wiggly Questionable {service.GetType().Name}.{name} contract is unavailable");

    private static System.Collections.Generic.IEnumerable<object> ReadList(object source, string name)
        => (source.GetType().GetProperty(name)?.GetValue(source) as IEnumerable)?.Cast<object>()
            ?? throw new InvalidOperationException($"Wiggly Questionable gathering {name} contract is unavailable");
}
