using System;
using System.Collections.Generic;

namespace VERMAXION.Models;

public enum DadOrchestrationRole
{
    None,
    Leader,
    Participant,
}

public enum DadParticipantState
{
    Unknown,
    Idle,
    Discovered,
    WakeRequested,
    WaitingForPostArReady,
    Ready,
    Claimed,
    AssemblyPending,
    AssemblyConfirmed,
    QueuePending,
    Running,
    Completed,
    Failed,
    Cancelled,
    Stale,
}

public enum DadClaimState
{
    None,
    Pending,
    Granted,
    Denied,
    Released,
    Collided,
}

public enum DadModuleId
{
    None,
    Duty,
    DailyMsq,
    Commendation,
    Astrope,
    Mixed,
}

public enum DadTransportMode
{
    LocalOnly,
    LocalhostHybrid,
}

public enum DadQueueAuthority
{
    LocalOnly,
    Leader,
    DadDirect,
    LanParty,
    AuraFarmer,
}

public enum DadRunPhase
{
    Idle,
    Planning,
    DiscoveringParticipants,
    WaitingForReadiness,
    ClaimingSlots,
    AssemblingParty,
    RoutingModules,
    Finalizing,
}

public sealed class DadOrchestrationIntent
{
    public DadModuleId ModuleTarget { get; set; } = DadModuleId.None;
    public DadQueueAuthority QueueAuthority { get; set; } = DadQueueAuthority.LocalOnly;
    public DadTransportMode TransportMode { get; set; } = DadTransportMode.LocalOnly;
    public DadRosterIntent RosterIntent { get; set; } = new();
    public bool RequirePostArReady { get; set; } = true;
    public bool PreferTypedRosterPool { get; set; } = true;
    public string LeaderCharacterKey { get; set; } = string.Empty;
    public string ExecutionConstraintSummary { get; set; } = string.Empty;
}

public sealed class DadRosterIntent
{
    public int ExpectedPartySize { get; set; } = 1;
    public bool RequireRemoteParticipants { get; set; }
    public bool AllowStoredXadbFallback { get; set; } = true;
    public bool RequireExactCharacters { get; set; }
    public List<string> PreferredCharacterKeys { get; set; } = [];
    public List<string> RequiredCharacterKeys { get; set; } = [];
}

public sealed class DadParticipantSnapshot
{
    public string ClientInstanceId { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public DadOrchestrationRole Role { get; set; } = DadOrchestrationRole.None;
    public DadParticipantState State { get; set; } = DadParticipantState.Unknown;
    public DadClaimState ClaimState { get; set; } = DadClaimState.None;
    public bool IsLocalClient { get; set; }
    public bool PostArReady { get; set; }
    public string CharacterKey { get; set; } = string.Empty;
    public string StatusText { get; set; } = string.Empty;
}

public sealed class DadRunStepResultDto
{
    public string RunId { get; set; } = string.Empty;
    public DadModuleId ModuleId { get; set; } = DadModuleId.None;
    public string StepName { get; set; } = string.Empty;
    public DadParticipantState ParticipantState { get; set; } = DadParticipantState.Unknown;
    public bool Success { get; set; }
    public bool Deferred { get; set; }
    public bool TimedOut { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string FailureReason { get; set; } = string.Empty;
    public string BlockedReason { get; set; } = string.Empty;
    public DateTime ReportedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class DadRunRequest
{
    public string RequestId { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;
    public string RequestedBy { get; set; } = string.Empty;
    public DadOrchestrationIntent Orchestration { get; set; } = new();
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

    public DadOrchestrationIntent ApplyOrchestrationDefaults()
    {
        Orchestration ??= new DadOrchestrationIntent();
        Orchestration.RosterIntent ??= new DadRosterIntent();

        if (Orchestration.ModuleTarget == DadModuleId.None)
        {
            Orchestration.ModuleTarget = GetConfiguredTaskCount() switch
            {
                0 => DadModuleId.None,
                > 1 => DadModuleId.Mixed,
                _ when DailyMsq != null => DadModuleId.DailyMsq,
                _ when Commendation != null => DadModuleId.Commendation,
                _ when Astrope != null => DadModuleId.Astrope,
                _ => DadModuleId.Duty,
            };
        }

        if (Orchestration.RosterIntent.ExpectedPartySize <= 0)
            Orchestration.RosterIntent.ExpectedPartySize = DetermineExpectedPartySize();

        Orchestration.RosterIntent.RequireRemoteParticipants = Orchestration.RosterIntent.ExpectedPartySize > 1;

        if (Orchestration.TransportMode == DadTransportMode.LocalOnly &&
            Orchestration.RosterIntent.RequireRemoteParticipants)
        {
            Orchestration.TransportMode = DadTransportMode.LocalhostHybrid;
        }

        if (Orchestration.QueueAuthority == DadQueueAuthority.LocalOnly &&
            Orchestration.RosterIntent.RequireRemoteParticipants)
        {
            Orchestration.QueueAuthority = DadQueueAuthority.Leader;
        }

        if (string.IsNullOrWhiteSpace(Orchestration.ExecutionConstraintSummary))
        {
            Orchestration.ExecutionConstraintSummary = Dungeon != null
                ? Dungeon.QueueViaLanParty
                    ? "PremadeQueue"
                    : Dungeon.Unsynced
                        ? "Unsynced"
                        : Dungeon.ExecutionPreference
                : Orchestration.ModuleTarget switch
                {
                    DadModuleId.DailyMsq => "PremadeDailyMsq",
                    DadModuleId.Commendation => "CommendationFarm",
                    DadModuleId.Astrope => "AstropeFarm",
                    _ => "LocalOnly",
                };
        }

        return Orchestration;
    }

    private int DetermineExpectedPartySize()
    {
        if (DailyMsq != null || Commendation != null || Astrope != null)
            return 4;

        if (Dungeon?.QueueViaLanParty == true)
            return 4;

        return 1;
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
