using System;

namespace VERMAXION.Models;

public enum StableDestination { SharedEstate1, SharedEstate2, SharedEstate3, PersonalEstate, Apartment, FreeCompanyEstate }
public enum StableTarget { OwnChocobo, SpecificOther }

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
    public bool Matches(string owner, string name) => Owner.Equals(owner, StringComparison.Ordinal) && Name.Equals(name, StringComparison.Ordinal);
    public string Progression => Rank >= 20 ? "Maximum rank" : Capped
        ? Rank >= 10 ? "Rank capped; owner must handle Thavnairian Onion progression" : "Experience capped; owner must gain combat experience to rank up"
        : $"Rank {Rank}";
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
