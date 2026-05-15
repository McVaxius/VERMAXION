using System.Collections.Generic;
using System.Linq;

namespace VERMAXION;

public enum PostProcessTaskPhase
{
    BeforeAR,
    AfterAR,
}

public static class PostProcessTaskOrder
{
    public const string RefillListings = "refill_listings";
    public const string FCBuffRefill = "fc_buff_refill";
    public const string VendorStock = "vendor_stock";
    public const string RegisterRegistrables = "register_registrables";
    public const string VerminionQueue = "verminion_queue";
    public const string MiniCactpot = "mini_cactpot";
    public const string JumboCactpot = "jumbo_cactpot";
    public const string FashionReport = "fashion_report";
    public const string ChocoboRacing = "chocobo_racing";
    public const string NagYourMom = "nag_your_mom";
    public const string NagYourDad = "nag_your_dad";

    public static readonly IReadOnlyList<TaskDefinition> Definitions =
    [
        new(RefillListings, "Refill Listings"),
        new(FCBuffRefill, "FC Buff Refill"),
        new(VendorStock, "Vendor Stock"),
        new(RegisterRegistrables, "Register Registrables"),
        new(VerminionQueue, "Verminion Queue"),
        new(MiniCactpot, "Mini Cactpot"),
        new(JumboCactpot, "Jumbo Cactpot"),
        new(FashionReport, "Fashion Report"),
        new(ChocoboRacing, "Chocobo Racing"),
        new(NagYourMom, "nag your mom"),
        new(NagYourDad, "nag your dad"),
    ];

    public static readonly IReadOnlyList<string> DefaultOrder =
    [
        RefillListings,
        FCBuffRefill,
        VendorStock,
        RegisterRegistrables,
        VerminionQueue,
        MiniCactpot,
        JumboCactpot,
        FashionReport,
        ChocoboRacing,
        NagYourMom,
        NagYourDad,
    ];

    private static readonly HashSet<string> KnownIds = Definitions.Select(definition => definition.Id).ToHashSet();

    public static List<string> Normalize(IEnumerable<string>? order)
    {
        var normalized = new List<string>();
        var seen = new HashSet<string>();

        foreach (var id in order ?? [])
        {
            if (!KnownIds.Contains(id) || !seen.Add(id))
                continue;

            normalized.Add(id);
        }

        foreach (var id in DefaultOrder)
        {
            if (seen.Add(id))
                normalized.Add(id);
        }

        return normalized;
    }

    public static bool Normalize(Configuration config)
    {
        var changed = false;
        var normalized = Normalize(config.PostProcessTaskOrder);
        if (config.PostProcessTaskOrder == null || !config.PostProcessTaskOrder.SequenceEqual(normalized))
        {
            config.PostProcessTaskOrder = normalized;
            changed = true;
        }

        config.PostProcessTaskPlacement ??= new Dictionary<string, PostProcessTaskPhase>();
        foreach (var id in config.PostProcessTaskPlacement.Keys.Where(id => !KnownIds.Contains(id)).ToList())
        {
            config.PostProcessTaskPlacement.Remove(id);
            changed = true;
        }

        foreach (var id in DefaultOrder)
        {
            if (config.PostProcessTaskPlacement.ContainsKey(id))
                continue;

            config.PostProcessTaskPlacement[id] = GetDefaultPhase(id);
            changed = true;
        }

        return changed;
    }

    public static void ResetToDefault(Configuration config)
    {
        config.PostProcessTaskOrder = DefaultOrder.ToList();
        config.PostProcessTaskPlacement = CreateDefaultPlacement();
    }

    public static string GetLabel(string id)
    {
        return Definitions.FirstOrDefault(definition => definition.Id == id)?.Label ?? id;
    }

    public static PostProcessTaskPhase GetDefaultPhase(string id)
        => id == RefillListings ? PostProcessTaskPhase.BeforeAR : PostProcessTaskPhase.AfterAR;

    public static Dictionary<string, PostProcessTaskPhase> CreateDefaultPlacement()
        => DefaultOrder.ToDictionary(id => id, GetDefaultPhase);

    public sealed record TaskDefinition(string Id, string Label);
}
