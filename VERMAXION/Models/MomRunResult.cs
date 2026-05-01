using System;

namespace VERMAXION.Models;

public sealed class MomRunResult
{
    public string RequestId { get; set; } = string.Empty;
    public MomRunStatus Status { get; set; } = MomRunStatus.Idle;
    public string Route { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = string.Empty;
    public string RequestedJob { get; set; } = string.Empty;
    public int RequestedRunCount { get; set; }
    public int CompletedRunCount { get; set; }
    public bool StopAtSeriesRank25 { get; set; }
    public string FailureReason { get; set; } = string.Empty;
    public string Summary { get; set; } = "Idle";
    public DateTime? CompletedAtUtc { get; set; }

    public static MomRunResult Idle() => new();
}
