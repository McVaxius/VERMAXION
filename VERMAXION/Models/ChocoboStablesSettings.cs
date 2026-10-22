using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace VERMAXION.Models;

public enum StableDestination { SharedEstate1, SharedEstate2, SharedEstate3, PersonalEstate, Apartment, FreeCompanyEstate }
public enum StableTarget { OwnChocobo, SpecificOther, AllTrainable }

[Serializable]
public sealed class ChocoboStablesSettings
{
    public StableDestination Destination { get; set; } = StableDestination.FreeCompanyEstate;
    public StableTarget Target { get; set; }
    public string OtherOwner { get; set; } = string.Empty;
    public string OtherChocobo { get; set; } = string.Empty;
    public bool CleanStable { get; set; } = true;
    public bool BuyOnionFromMarketboard { get; set; }
    public int OnionMaxUnitPrice { get; set; }
    public int OnionGilBudget { get; set; }
    public ChocoboStablesSettings Clone() => (ChocoboStablesSettings)MemberwiseClone();
}

public sealed record StableBird(int Page, int Row, string Name, string Owner, int Rank, bool Capped, string Training)
{
    public bool Ready => Training.Equals("Ready", StringComparison.OrdinalIgnoreCase);
    public bool Trainable => Ready && !Capped && Rank is >= 0 and < 20;
    // The established HousingChocoboList node 16 contract displays minutes, e.g. "42m".
    public bool HasTrainingCooldown => Training.EndsWith('m') && int.TryParse(Training.AsSpan(0, Training.Length - 1),
        NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) && minutes is >= 0 and <= 60;
    public bool Matches(string owner, string name) => Owner.Equals(owner, StringComparison.Ordinal) && Name.Equals(name, StringComparison.Ordinal);
    public string Progression => Rank >= 20 ? "Maximum rank" : Capped
        ? Rank >= 10 ? "Rank capped; owner must handle Thavnairian Onion progression" : "Experience capped; owner must gain combat experience to rank up"
        : $"Rank {Rank}";
}

internal enum StableTrainingReceipt { Pending, Verified, Uncertain }

/// <summary>One in-memory roster snapshot; a dispatched feed is never retried.</summary>
internal sealed class StableTrainingBatch
{
    private readonly StableBird[] candidates;
    private int index;
    private int? feedBefore;
    public int Trained { get; private set; }
    public int Skipped { get; private set; }
    public int Remaining => candidates.Length - index;
    public bool Stopped { get; private set; }
    public bool Complete => !Stopped && Remaining == 0;
    public bool CanRecordCompletion => Complete && Trained > 0;
    public StableBird? Current => !Stopped && index < candidates.Length ? candidates[index] : null;
    public string Detail { get; private set; } = string.Empty;
    public string Summary => $"All trainable: trained {Trained}; skipped {Skipped}; remaining {Remaining}.";

    public StableTrainingBatch(IReadOnlyList<StableBird> roster)
    {
        candidates = roster.Where(bird => bird.Trainable).ToArray();
        Skipped = roster.Count - candidates.Length;
        if (roster.GroupBy(bird => (bird.Owner, bird.Name)).Any(group => group.Count() != 1)
            || roster.GroupBy(bird => (bird.Page, bird.Row)).Any(group => group.Count() != 1))
            Stop("Selected chocobo is unavailable; no training dispatched.");
    }

    public void Stop(string detail) { Stopped = true; Detail = detail; }

    public bool TryRevalidate(IReadOnlyList<StableBird> rows, out StableBird? current)
    {
        current = null;
        if (Current is not { } expected) return false;
        var matches = rows.Where(bird => bird.Matches(expected.Owner, expected.Name)).ToArray();
        if (matches.Length != 1 || matches[0].Page != expected.Page || matches[0].Row != expected.Row)
        {
            Stop("Selected chocobo is unavailable; no training dispatched.");
            return false;
        }
        current = matches[0];
        return true;
    }

    public void SkipCurrent()
    {
        if (Current == null || feedBefore.HasValue) throw new InvalidOperationException("Cannot skip a dispatched feed.");
        Skipped++; index++;
    }

    public bool Dispatch(int stock)
    {
        if (Stopped || Current == null || feedBefore.HasValue) return false;
        if (stock < 0) { Stop("Krakka Root inventory is unreadable; no training dispatched."); return false; }
        if (stock == 0) { Stop("Insufficient Krakka Roots; remaining birds were not trained."); return false; }
        feedBefore = stock;
        return true;
    }

    public StableTrainingReceipt ObserveReceipt(int stock, IReadOnlyList<StableBird> rows)
    {
        if (Stopped || !feedBefore.HasValue)
            return StableTrainingReceipt.Uncertain;
        if (!TryRevalidate(rows, out var current))
        { Stop("Training result is uncertain; no further training dispatched."); return StableTrainingReceipt.Uncertain; }
        var consumed = feedBefore.Value - stock;
        if (stock < 0 || consumed is < 0 or > 1)
        { Stop("Training result is uncertain; no further training dispatched."); return StableTrainingReceipt.Uncertain; }
        if (current!.Ready) return StableTrainingReceipt.Pending;
        if (!current.HasTrainingCooldown)
        { Stop("Training result is uncertain; no further training dispatched."); return StableTrainingReceipt.Uncertain; }
        if (consumed == 0) return StableTrainingReceipt.Pending;
        feedBefore = null;
        Trained++; index++;
        return StableTrainingReceipt.Verified;
    }
}

internal static class ChocoboOnionQuests
{
    public const uint ItemId = 8166;
    // Current Quest sheet: both reward one onion. MSQ prerequisites are availability gates.
    private static readonly (ushort Reward, ushort[] Steps)[] Routes =
    [
        (1729, [1729]), // Landing a Stable Job
        (1926, [1775, 1776, 1777, 1781, 1782, 1783, 1779, 1780, 1785, 1922, 1923, 1924, 1925, 1926]), // A Hunter's True Nature
    ];

    public static bool ShouldAcquire(StableBird bird, StableTarget target, int stock)
        => target == StableTarget.OwnChocobo && bird.Row == -1 && bird.Capped && bird.Rank is >= 10 and < 20 && stock == 0;

    public static ushort SelectNext(Func<ushort, bool> complete, Func<ushort, bool> available)
    {
        foreach (var route in Routes)
        {
            if (complete(route.Reward)) continue;
            // Prefer an already-unlocked reward before doing another sidequest chain.
            if (available(route.Reward)) return route.Reward;
        }
        foreach (var route in Routes)
        {
            if (complete(route.Reward)) continue;
            for (var i = route.Steps.Length - 1; i >= 0; --i)
                if (!complete(route.Steps[i]) && available(route.Steps[i])) return route.Steps[i];
        }
        return 0;
    }

    public static bool RewardsComplete(Func<ushort, bool> complete) => complete(1729) && complete(1926);
    public static bool IsReward(ushort quest) => quest is 1729 or 1926;
}
