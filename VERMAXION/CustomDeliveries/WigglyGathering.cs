using System;
using System.Runtime.Loader;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using ECommons.Reflection;

namespace VERMAXION.CustomDeliveries;

// WigglyQuest API 3 owns gather-only travel/update. Reflection is retained solely
// for provider-instance identity, which the public operation API does not expose.
internal sealed class WigglyGathering
{
    internal const string PluginInternalName = "WigglyQuest";
    private readonly object owner;
    private readonly ulong character;
    private readonly Func<object> currentPlugin;
    private readonly Func<ulong> currentCharacter;
    private readonly WigglyGatheringRequest request;
    private readonly ICallGateSubscriber<bool> running;
    private readonly ICallGateSubscriber<WigglyGatheringRequest, WigglyGatheringStartResult?> start;
    private readonly ICallGateSubscriber<WigglyGatheringStatus?> status;
    private readonly ICallGateSubscriber<string, (bool, string?)> cancel;
    private string? ownedRequestId;
    private bool startAttempted;

    internal WigglyGathering(DeliveryRoute route)
        : this(Service.PluginInterface, route, ResolvePlugin, () => Service.PlayerState.ContentId,
            quality => Game.NumItemsInInventory(route.ItemId, (short)quality)) { }

    internal WigglyGathering(IDalamudPluginInterface ipc, DeliveryRoute route, Func<object> currentPlugin,
        Func<ulong> currentCharacter, Func<ushort, int> inventory)
    {
        this.currentPlugin = currentPlugin;
        this.currentCharacter = currentCharacter;
        owner = currentPlugin();
        character = currentCharacter();
        if (character == 0) throw new InvalidOperationException("The gathering character is unavailable");
        var eligible = inventory(route.MinCollectibility);
        var atTargetQuality = inventory(route.TargetCollectibility);
        if (eligible < 0 || atTargetQuality < 0)
            throw new InvalidOperationException("Custom-delivery inventory is unavailable");
        request = new()
        {
            ItemId = route.ItemId,
            ClassJob = checked((byte)route.Job),
            // Quantity is an inventory total at the requested quality. Existing
            // lower-quality qualifying items still count toward our turn-ins.
            Quantity = checked(atTargetQuality + DeliveryPlanning.MissingItems(route.Count, eligible)),
            Collectability = route.TargetCollectibility,
        };
        running = ipc.GetIpcSubscriber<bool>("WigglyQuest.IsRunning");
        var canGather = ipc.GetIpcSubscriber<uint, byte, bool>("WigglyQuest.Gathering.CanGather");
        start = ipc.GetIpcSubscriber<WigglyGatheringRequest, WigglyGatheringStartResult?>("WigglyQuest.Gathering.Start");
        status = ipc.GetIpcSubscriber<WigglyGatheringStatus?>("WigglyQuest.Gathering.GetStatus");
        cancel = ipc.GetIpcSubscriber<string, (bool, string?)>("WigglyQuest.Gathering.Cancel");
        if (!running.HasFunction || !canGather.HasFunction || !start.HasFunction || !status.HasFunction || !cancel.HasFunction)
            throw new InvalidOperationException("WigglyQuest gathering IPC is unavailable; API 3 is required");
        EnsureIdle();
        if (!canGather.InvokeFunc(request.ItemId, request.ClassJob))
            throw new InvalidOperationException($"Wiggly Questionable has no gathering route for item {route.ItemId}, job {route.Job}");
    }

    internal static string? GetBlockedReason(DeliveryRoute route)
    {
        try { _ = new WigglyGathering(route); return null; }
        catch (Exception ex) { return ex.GetBaseException().Message; }
    }

    internal void Start()
    {
        EnsureCurrent();
        if (startAttempted) throw new InvalidOperationException("This gathering start was already attempted; no redispatch");
        EnsureIdle();
        startAttempted = true; // A missing acknowledgement never authorizes another start.
        var result = start.InvokeFunc(request);
        ownedRequestId = result?.RequestId;
        if (result?.Accepted != true || string.IsNullOrWhiteSpace(ownedRequestId))
            throw new InvalidOperationException($"Wiggly Questionable did not acknowledge the gathering request: {result?.Reason ?? "unavailable response"}");
        Service.Log?.Information($"[CustomDeliveries][Gathering] Accepted Wiggly request: item={request.ItemId}; targetInventory={request.Quantity}; collectability={request.Collectability}");
    }

    internal bool Update()
    {
        EnsureCurrent();
        var observed = ReadStatus();
        if (!MatchesOwned(observed))
            throw new InvalidOperationException("The owned Wiggly Questionable gathering request was replaced or is unavailable");
        switch (observed.State)
        {
            case "Travelling":
            case "Gathering":
                return true;
            case "Complete" when observed.Have >= request.Quantity:
                return false;
            default:
                throw new InvalidOperationException($"Wiggly Questionable gathering ended without verified stock: {observed.State}; {observed.FailureReason}");
        }
    }

    internal void Stop()
    {
        if (ownedRequestId == null || currentCharacter() != character) return;
        object current;
        try { current = currentPlugin(); }
        catch { return; } // Never cancel through a departed/unreadable provider.
        if (!ReferenceEquals(current, owner)) return;
        var observed = ReadStatus();
        if (!MatchesOwned(observed) || observed.State is "Complete" or "Cancelled" or "Failed")
        {
            ownedRequestId = null;
            return;
        }
        if (observed.State is not ("Travelling" or "Gathering"))
            throw new InvalidOperationException("Wiggly gathering cancellation state is unavailable");
        var result = cancel.InvokeFunc(ownedRequestId);
        var after = ReadStatus();
        if (MatchesOwned(after) && after.State is not ("Complete" or "Cancelled" or "Failed"))
            throw new InvalidOperationException($"Wiggly gathering cancellation is unconfirmed: {result.Item2 ?? after.State}");
        ownedRequestId = null;
    }

    private bool MatchesOwned(WigglyGatheringStatus observed)
        => !string.IsNullOrEmpty(ownedRequestId) && observed.RequestId == ownedRequestId
            && observed.ItemId == request.ItemId && observed.Need == request.Quantity;

    private WigglyGatheringStatus ReadStatus()
        => status.InvokeFunc() ?? throw new InvalidOperationException("Wiggly gathering status is unavailable");

    private void EnsureIdle()
    {
        var observed = ReadStatus();
        if (running.InvokeFunc() || observed.State is "Travelling" or "Gathering")
            throw new InvalidOperationException("Wiggly Questionable is already running another task");
        if (observed.State is not ("Idle" or "Complete" or "Cancelled" or "Failed"))
            throw new InvalidOperationException("Wiggly gathering readiness is unavailable");
    }

    private void EnsureCurrent()
    {
        if (currentCharacter() != character || !ReferenceEquals(currentPlugin(), owner))
            throw new InvalidOperationException("Wiggly Questionable or the character changed during the gathering request");
    }

    internal static object ResolvePlugin()
    {
        if (!DalamudReflector.TryGetDalamudPlugin(PluginInternalName, out object plugin, out AssemblyLoadContext? _, true, true)
            || plugin == null) throw new InvalidOperationException("Wiggly Questionable (WigglyQuest) is not loaded");
        return plugin;
    }
}

// Independent IPC DTOs are converted by Dalamud; do not reference the provider assembly.
public sealed class WigglyGatheringRequest
{
    public uint ItemId { get; set; }
    public byte ClassJob { get; set; }
    public int Quantity { get; set; }
    public ushort Collectability { get; set; }
}

public sealed class WigglyGatheringStartResult
{
    public bool Accepted { get; set; }
    public string? RequestId { get; set; }
    public string? Reason { get; set; }
    public ushort? TerritoryId { get; set; }
}

public sealed class WigglyGatheringStatus
{
    public string? RequestId { get; set; }
    public string? State { get; set; }
    public uint ItemId { get; set; }
    public int Have { get; set; }
    public int Need { get; set; }
    public string? FailureReason { get; set; }
}
