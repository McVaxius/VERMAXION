using System;

namespace VERMAXION.Models;

public static class AutomationStatusContract
{
    public const int Version = 1;
    public const string Channel = "VERMAXION.GetAutomationStatusJson";
}

public enum AutomationActivity
{
    Idle,
    Fishing,
    FishingRelog,
    FishingCleanup,
    Engine,
    BeforeAutoRetainer,
    SuppressionRecovery,
    ManualService,
    CharacterPostprocessPending,
}

public sealed class AutomationStatus
{
    public int Version { get; init; } = AutomationStatusContract.Version;
    public bool IsBusy { get; init; }
    public string Activity { get; init; } = AutomationActivity.Idle.ToString();
    public string State { get; init; } = "Idle";
    public string Summary { get; init; } = "VERMAXION is idle.";
    public DateTime GeneratedAtUtc { get; init; } = DateTime.UtcNow;
}

public sealed class AutomationOwnershipSnapshot
{
    public bool FishingRelogActive { get; init; }
    public bool FishingRelogPending { get; init; }
    public bool FishingActive { get; init; }
    public bool FishingCleanupActive { get; init; }
    public bool BeforeAutoRetainerActive { get; init; }
    public bool SuppressionRecoveryActive { get; init; }
    public bool EngineOwnsLiveWork { get; init; }
    public bool ManualServiceActive { get; init; }
    public bool CharacterPostprocessRequested { get; init; }
    public string State { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
}

public static class AutomationStatusPolicy
{
    public static AutomationStatus Evaluate(AutomationOwnershipSnapshot snapshot, DateTime generatedAtUtc)
    {
        var activity = ResolveActivity(snapshot);
        var busy = activity != AutomationActivity.Idle;
        return new AutomationStatus
        {
            IsBusy = busy,
            Activity = activity.ToString(),
            State = string.IsNullOrWhiteSpace(snapshot.State)
                ? busy ? activity.ToString() : "Idle"
                : snapshot.State.Trim(),
            Summary = string.IsNullOrWhiteSpace(snapshot.Summary)
                ? busy ? $"VERMAXION owns {activity} automation." : "VERMAXION is idle."
                : snapshot.Summary.Trim(),
            GeneratedAtUtc = generatedAtUtc.Kind == DateTimeKind.Utc
                ? generatedAtUtc
                : generatedAtUtc.ToUniversalTime(),
        };
    }

    private static AutomationActivity ResolveActivity(AutomationOwnershipSnapshot snapshot)
    {
        if (snapshot.FishingRelogActive || snapshot.FishingRelogPending)
            return AutomationActivity.FishingRelog;
        if (snapshot.CharacterPostprocessRequested)
            return AutomationActivity.CharacterPostprocessPending;
        if (snapshot.FishingCleanupActive)
            return AutomationActivity.FishingCleanup;
        if (snapshot.FishingActive)
            return AutomationActivity.Fishing;
        if (snapshot.BeforeAutoRetainerActive)
            return AutomationActivity.BeforeAutoRetainer;
        if (snapshot.SuppressionRecoveryActive)
            return AutomationActivity.SuppressionRecovery;
        if (snapshot.EngineOwnsLiveWork)
            return AutomationActivity.Engine;
        if (snapshot.ManualServiceActive)
            return AutomationActivity.ManualService;
        return AutomationActivity.Idle;
    }
}
