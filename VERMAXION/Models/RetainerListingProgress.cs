using System;
using System.Collections.Generic;
using System.Linq;

namespace VERMAXION.Models;

internal sealed record RetainerListing(int Slot, uint ItemId, int Quantity, bool IsHq, string ItemName);
internal sealed record RetainerListingIdentity(uint ItemId, int Quantity, bool IsHq);

// Lives only for the current retainer. Row positions are always rebuilt from a fresh scan.
internal sealed class RetainerListingProgress
{
    private readonly Dictionary<RetainerListingIdentity, int> remaining = new();
    private RetainerListingIdentity? pending;
    private int matchingBeforeDispatch;
    private bool dispatched;
    private DateTime recoveryDeadline = DateTime.MinValue;

    public bool Initialized { get; private set; }
    public int Remaining => remaining.Values.Sum();

    public void BeginRecovery(DateTime now)
    {
        if (recoveryDeadline == DateTime.MinValue)
            recoveryDeadline = now + TaskWatchdogPolicy.Timeout;
    }

    public void ExcludeOwnershipWait(TimeSpan elapsed)
    {
        if (recoveryDeadline != DateTime.MinValue)
            recoveryDeadline += elapsed;
    }

    public bool RecoveryTimedOut(DateTime now)
        => recoveryDeadline != DateTime.MinValue && now >= recoveryDeadline;

    private static RetainerListingIdentity Identity(RetainerListing listing)
        => new(listing.ItemId, listing.Quantity, listing.IsHq);

    public void Initialize(IEnumerable<RetainerListing> selected)
    {
        if (Initialized)
            return;
        foreach (var listing in selected)
        {
            var key = Identity(listing);
            remaining.TryGetValue(key, out var count);
            remaining[key] = count + 1;
        }
        Initialized = true;
    }

    public RetainerListing? FindNext(IEnumerable<RetainerListing> live)
        => live.OrderByDescending(listing => listing.Slot)
            .FirstOrDefault(listing => remaining.GetValueOrDefault(Identity(listing)) > 0);

    public void BeginWithdrawal(RetainerListing listing, IEnumerable<RetainerListing> live)
    {
        pending = Identity(listing);
        matchingBeforeDispatch = live.Count(item => Identity(item) == pending);
        dispatched = false;
    }

    public void MarkDispatched() => dispatched = true;

    public bool VerifyWithdrawal(IEnumerable<RetainerListing> live)
    {
        if (!dispatched || pending == null ||
            live.Count(item => Identity(item) == pending) >= matchingBeforeDispatch)
            return false;

        var count = remaining.GetValueOrDefault(pending);
        if (count <= 1)
            remaining.Remove(pending);
        else
            remaining[pending] = count - 1;
        ClearPending();
        recoveryDeadline = DateTime.MinValue;
        return true;
    }

    // Called only after returning to the list, reselecting the same retainer and rescanning.
    public bool ReconcileAfterInterruption(IEnumerable<RetainerListing> live)
    {
        var withdrawn = VerifyWithdrawal(live);
        ClearPending();
        return withdrawn;
    }

    private void ClearPending()
    {
        pending = null;
        matchingBeforeDispatch = 0;
        dispatched = false;
    }

    public void Reset()
    {
        remaining.Clear();
        ClearPending();
        Initialized = false;
        recoveryDeadline = DateTime.MinValue;
    }
}

internal static class RetainerControlPolicy
{
    public static string? GetBlocker(SuppressionSnapshot suppression, PluginBusyReadResult busy,
        bool formalPostprocess, bool waitingForRoute = false)
    {
        if (!suppression.RemoteKnown)
            return "AutoRetainer suppression is unreadable";
        if (!suppression.RemoteSuppressed)
            return "AutoRetainer suppression was lost";
        if (!suppression.OwnedByVermaxion)
            return "AutoRetainer suppression belongs to another owner";
        // AR's postprocess queue waits for our finish signal; its busy flag cannot drain here.
        if (formalPostprocess || waitingForRoute)
            return null;
        if (!busy.Success)
            return "AutoRetainer busy state is unreadable";
        return busy.Busy ? "Waiting for queued AutoRetainer work to drain" : null;
    }

    public static bool IsBuybackPrompt(string prompt, string localizedPrompt)
        => !string.IsNullOrWhiteSpace(prompt) && !string.IsNullOrWhiteSpace(localizedPrompt) &&
           string.Equals(Normalize(prompt), Normalize(localizedPrompt), StringComparison.Ordinal);

    private static string Normalize(string text)
        => string.Join(" ", text.Replace('\u0001', ' ').Replace('\u0002', ' ').Replace('\u0003', ' ')
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
