using System;
using System.Collections.Generic;

namespace VERMAXION.Models;

public sealed class DadRunRequest
{
    public string RequestId { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;
    public string RequestedBy { get; set; } = string.Empty;
    public DadDungeonTask? Dungeon { get; set; }
    public DadDailyMsqTask? DailyMsq { get; set; }
    public DadCommendationTask? Commendation { get; set; }
    public DadAstropeTask? Astrope { get; set; }

    public int GetConfiguredTaskCount()
    {
        var count = 0;
        if (Dungeon != null) count++;
        if (DailyMsq != null) count++;
        if (Commendation != null) count++;
        if (Astrope != null) count++;
        return count;
    }
}

public sealed class DadDungeonTask
{
    public int Count { get; set; } = 1;
    public string Frequency { get; set; } = DadRunRequestOptions.FrequencyPerArRun;
    public uint ContentFinderConditionId { get; set; }
    public string SelectedDungeon { get; set; } = string.Empty;
    public string SelectedJob { get; set; } = string.Empty;
    public string ExecutionPreference { get; set; } = DadRunRequestOptions.TrustThenDutySupport;
    public bool QueueViaLanParty { get; set; }
    public bool Unsynced { get; set; }
}

public sealed class DadDailyMsqTask
{
    public string LanPartyPreset { get; set; } = "Daily MSQ";
}

public sealed class DadCommendationTask
{
    public int Attempts { get; set; } = 1;
}

public sealed class DadAstropeTask
{
    public int Attempts { get; set; } = 1;
    public DadTimeWindow ValidLocalTimeWindow { get; set; } = new();
}

public static class DadRunRequestOptions
{
    public const string FrequencyPerArRun = "Per AR run";
    public const string FrequencyDailyReset = "Daily reset";
    public const string FrequencyWeeklyReset = "Weekly reset";
    public const string TrustThenDutySupport = "TrustThenDutySupport";

    public static readonly string[] DungeonFrequencies =
    [
        FrequencyPerArRun,
        FrequencyDailyReset,
        FrequencyWeeklyReset,
    ];

    public static readonly string[] LanPartyPresetStubs =
    [
        "Daily MSQ",
        "Leveling",
        "Expert",
        "Custom",
    ];

    public static readonly string[] JobHintExamples =
    [
        "PLD",
        "WAR",
        "DRK",
        "GNB",
        "WHM",
        "SCH",
        "AST",
        "SGE",
        "MNK",
        "DRG",
        "NIN",
        "SAM",
        "RPR",
        "BRD",
        "MCH",
        "DNC",
        "BLM",
        "SMN",
        "RDM",
        "PCT",
        "BLU",
    ];

    public static string NormalizeFrequency(string value)
    {
        if (string.Equals(value, "per AR", StringComparison.OrdinalIgnoreCase))
            return FrequencyPerArRun;

        foreach (var option in DungeonFrequencies)
        {
            if (string.Equals(value, option, StringComparison.OrdinalIgnoreCase))
                return option;
        }

        return FrequencyPerArRun;
    }

    public static int GetFrequencyIndex(string value)
    {
        var normalized = NormalizeFrequency(value);
        for (var i = 0; i < DungeonFrequencies.Length; i++)
        {
            if (DungeonFrequencies[i] == normalized)
                return i;
        }

        return 0;
    }

    public static int GetLanPartyPresetIndex(string value)
        => GetLanPartyPresetIndex(value, LanPartyPresetStubs);

    public static int GetLanPartyPresetIndex(string value, IReadOnlyList<string> options)
    {
        for (var i = 0; i < options.Count; i++)
        {
            if (string.Equals(value, options[i], StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return 0;
    }
}
