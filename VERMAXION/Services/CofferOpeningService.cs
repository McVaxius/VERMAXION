using System;

namespace VERMAXION.Services;

public sealed record CofferObservation(ulong CharacterId, bool Readable, bool Eligible,
    int Count, int FirstStackCount, int FreeSlots, bool CanUse, string? Blocker = null, int RequiredFreeSlots = 2);

public sealed record CofferOpeningStatus
{
    public int Version { get; init; } = 1;
    public int RunId { get; init; }
    public uint ItemId { get; init; }
    public string State { get; init; } = "Idle";
    public string Summary { get; init; } = "Coffers are idle.";
    public int Requested { get; init; }
    public int Opened { get; init; }
    public bool IsActive => State is "Running" or "WaitingForReceipt";
}

public sealed record CofferOpeningReply(bool Accepted, string OperationToken, string Reason, CofferOpeningStatus Status);

/// <summary>One explicit stack request; native consumption must settle before another item use.</summary>
public sealed class CofferOpeningService(
    Func<uint, CofferObservation> observe,
    Func<uint, bool> use,
    Func<DateTime>? clock = null) : IDisposable
{
    private readonly Func<DateTime> now = clock ?? (() => DateTime.UtcNow);
    private volatile CofferOpeningStatus status = new();
    private ulong character;
    private string operationToken = "";
    private int expectedCount;
    private DateTime submittedAt, readySince, nextUseAt, nextObservation;
    private bool pending, disposed;
    public CofferOpeningStatus Status => status;
    public bool IsActive => Status.IsActive;
    public bool IsDisposed => disposed;

    public CofferOpeningReply Start(uint itemId, int quantity = 0)
    {
        if (disposed) return Reject("Coffer utility is unloaded.");
        if (IsActive) return Reject("Coffers are already running.");
        if (itemId == 0 || quantity < 0) return Reject("Selected item is not a supported coffer.");
        CofferObservation frame;
        try { frame = observe(itemId); }
        catch (Exception) { return Reject("Coffer inventory is unavailable."); }
        var blocker = Validate(frame);
        if (blocker != null) return Reject(blocker);
        var requested = quantity == 0 ? frame.FirstStackCount : quantity;
        if (requested <= 0 || requested > frame.Count)
            return Reject("Selected coffer stack is unavailable.");
        character = frame.CharacterId;
        operationToken = Guid.NewGuid().ToString("N");
        expectedCount = frame.Count;
        pending = false;
        readySince = nextUseAt = nextObservation = now();
        status = new() { RunId = status.RunId + 1, ItemId = itemId, Requested = requested,
            State = "Running", Summary = "Opening coffers." };
        return new(true, operationToken, "", status);
    }

    public CofferOpeningReply Stop(string token)
    {
        if (!IsActive || string.IsNullOrEmpty(token) || !string.Equals(token, operationToken, StringComparison.Ordinal))
            return Reject("Coffer request is not owned by this caller.");
        Stop();
        return new(true, "", "", status);
    }

    // Operator FULL STOP and disposal may stop any local coffer request.
    public void Stop() => End("Stopped", "Coffer opening stopped.");

    public void Update()
    {
        if (!IsActive || disposed) return;
        var current = now();
        if (current < nextObservation) return;
        nextObservation = current.AddMilliseconds(100);
        CofferObservation frame;
        try { frame = observe(status.ItemId); }
        catch (Exception) { End("Blocked", "Coffer inventory is unavailable."); return; }
        if (frame.CharacterId != character || character == 0)
        { End("Stopped", "Coffer character changed."); return; }
        if (!frame.Readable || frame.Blocker != null || !frame.Eligible)
        { End("Blocked", frame.Blocker ?? (!frame.Readable ? "Coffer inventory is unavailable." : "Selected item is not a supported coffer.")); return; }
        if (pending)
        {
            if (frame.Count == expectedCount - 1)
            {
                pending = false;
                expectedCount = frame.Count;
                status = status with { Opened = status.Opened + 1, State = "Running", Summary = "Opening coffers." };
                readySince = current;
            }
            else if (frame.Count != expectedCount || current - submittedAt >= TimeSpan.FromSeconds(7))
            { End("Blocked", "Coffer use was not confirmed; check inventory before starting again."); return; }
            else return;
        }
        if (status.Opened >= status.Requested)
        { End("Completed", "Coffer stack completed."); return; }
        if (frame.Count != expectedCount || frame.Count == 0)
        { End("Blocked", "Coffer inventory changed."); return; }
        if (frame.FreeSlots < Math.Max(2, frame.RequiredFreeSlots))
        { End("Stopped", "Keep at least one free inventory slot."); return; }
        if (current < nextUseAt) return;
        if (!frame.CanUse)
        {
            if (current - readySince >= TimeSpan.FromSeconds(7)) End("Blocked", "Coffer item use is unavailable.");
            return;
        }
        // Consume this submission before calling native code; an exception is uncertain too.
        pending = true;
        submittedAt = current;
        nextUseAt = current.AddSeconds(1);
        status = status with { State = "WaitingForReceipt", Summary = "Waiting for coffer consumption." };
        try
        {
            if (!use(status.ItemId)) End("Blocked", "Coffer item use was rejected.");
        }
        catch (Exception) { End("Blocked", "Coffer use was not confirmed; check inventory before starting again."); }
    }

    private static string? Validate(CofferObservation frame) => frame.Blocker ??
        (frame.CharacterId == 0 || !frame.Readable ? "Coffer inventory is unavailable." :
         !frame.Eligible ? "Selected item is not a supported coffer." :
         frame.FreeSlots < Math.Max(2, frame.RequiredFreeSlots) ? "Keep at least one free inventory slot." : null);
    private CofferOpeningReply Reject(string reason) => new(false, "", reason, status);
    private void End(string state, string summary)
    {
        if (!IsActive) return;
        pending = false;
        operationToken = "";
        status = status with { State = state, Summary = summary };
    }
    public void Dispose() { Stop(); disposed = true; }
}
