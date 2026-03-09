using System;
using System.Collections.Generic;

namespace Vermaxion.Models;

public class AttemptData
{
    public int CurrentAttempt { get; set; } = 0;
    public int TotalAttempts { get; set; } = 5;
    public DateTime StartTime { get; set; } = DateTime.MinValue;
    public DateTime? LastFailureTime { get; set; } = null;
    public List<DateTime> QueuePopTimes { get; set; } = new();
    public List<string> FailureReasons { get; set; } = new();

    public void Reset()
    {
        CurrentAttempt = 0;
        StartTime = DateTime.Now;
        LastFailureTime = null;
        QueuePopTimes.Clear();
        FailureReasons.Clear();
    }

    public void RecordQueuePop()
    {
        QueuePopTimes.Add(DateTime.Now);
    }

    public void RecordFailure(string reason)
    {
        CurrentAttempt++;
        LastFailureTime = DateTime.Now;
        FailureReasons.Add(reason);
    }

    public bool IsCompleted => CurrentAttempt >= TotalAttempts;

    public TimeSpan ElapsedTime => StartTime == DateTime.MinValue 
        ? TimeSpan.Zero 
        : DateTime.Now - StartTime;
}
