namespace VERMAXION.Models;

public enum DadRunStatus
{
    Idle,
    Rejected,
    Queued,
    WaitingForParticipants,
    Running,
    Completed,
    PartialFailure,
    TimedOut,
    Failed,
    Cancelled,
}
