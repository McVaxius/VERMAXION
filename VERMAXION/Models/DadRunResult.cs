using System;

namespace VERMAXION.Models;

public sealed class DadRunResult
{
    public string RequestId { get; set; } = string.Empty;
    public DadRunStatus Status { get; set; } = DadRunStatus.Idle;
    public string RequestedBy { get; set; } = string.Empty;
    public int RequestedTaskCount { get; set; }
    public int CompletedTaskCount { get; set; }
    public int ActiveTaskIndex { get; set; }
    public int TotalTaskCount { get; set; }
    public string ActiveTaskName { get; set; } = string.Empty;
    public string ActiveTaskStatus { get; set; } = string.Empty;
    public string BlockedReason { get; set; } = string.Empty;
    public string FailureReason { get; set; } = string.Empty;
    public string Summary { get; set; } = "Idle";
    public DadRunRequest? Request { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    public bool IsTerminal =>
        Status is DadRunStatus.Rejected or DadRunStatus.Completed or DadRunStatus.Failed or DadRunStatus.Cancelled;

    public static DadRunResult Idle() => new()
    {
        Status = DadRunStatus.Idle,
        Summary = "Idle",
    };
}
