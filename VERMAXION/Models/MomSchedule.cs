using System;

namespace VERMAXION.Models;

public static class MomSchedule
{
    public const string DeadlineSupportBlocker = "Scheduled mom work blocked: update mom to a version that supports queue deadlines.";

    public static bool TryGetQueueDeadlineUtc(string startLocal, string endLocal, DateTime nowLocal, out DateTime deadlineUtc)
    {
        deadlineUtc = default;
        if (!TimeSpan.TryParse(startLocal, out var start) || !TimeSpan.TryParse(endLocal, out var end)
            || start < TimeSpan.Zero || start >= TimeSpan.FromDays(1)
            || end < TimeSpan.Zero || end >= TimeSpan.FromDays(1))
            return false;

        var now = nowLocal.TimeOfDay;
        if (!(start <= end ? now >= start && now < end : now >= start || now < end))
            return false;

        var closingLocal = nowLocal.Date.Add(end);
        if (start > end && now >= start)
            closingLocal = closingLocal.AddDays(1);
        deadlineUtc = closingLocal.ToUniversalTime();
        return true;
    }

    public static bool CompletesCycle(MomRunResult result)
        => result.Status == MomRunStatus.Completed && result.WindowExpired;

    public static int ObserveCompletedRuns(MomRunResult result, ref int alreadyCredited)
    {
        var observed = Math.Max(alreadyCredited, Math.Max(0, result.CompletedRunCount));
        var delta = observed - alreadyCredited;
        alreadyCredited = observed;
        return delta;
    }
}
