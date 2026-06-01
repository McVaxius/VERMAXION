using System;

namespace VERMAXION.Models;

public sealed class AchievementProgressStatus
{
    public uint AchievementId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool Known { get; set; }
    public bool Complete { get; set; }
    public uint Current { get; set; }
    public uint Max { get; set; }
    public string Source { get; set; } = string.Empty;
    public string FailureReason { get; set; } = string.Empty;
    public DateTime? UpdatedAtUtc { get; set; }
}

public sealed class RivalWingsAchievementGateResult
{
    public AchievementProgressStatus DieAnotherDayIII { get; set; } = new();
    public AchievementProgressStatus OutOfHiding { get; set; } = new();
    public bool Verified { get; set; }
    public bool BothComplete { get; set; }
    public bool DisableRouteRecommended { get; set; }
    public string DisableRouteReason { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string FailureReason { get; set; } = string.Empty;
    public DateTime? RequestedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}
