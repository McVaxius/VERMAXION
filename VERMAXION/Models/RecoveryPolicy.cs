using System;
using System.IO;
using System.Text.Json;

namespace VERMAXION.Models;

public static class TaskWatchdogPolicy
{
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    public static bool ShouldTimeout(DateTime now, DateTime lastProgressAt, bool paused, TimeSpan? timeout = null)
        => !paused
           && lastProgressAt != DateTime.MinValue
           && now - lastProgressAt >= (timeout ?? Timeout);
}

public static class FCBuffRecoveryPolicy
{
    private const int DailyResetHourUtc = 9;
    private const int WeeklyResetHourUtc = 9;
    public const int MaxPurchaseAttempts = 15;
    public const int MaxTeleportRetries = 3;
    public static readonly TimeSpan TeleportRetryInterval = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan TeleportTimeout = TimeSpan.FromSeconds(60);

    public static int ClampPurchaseAttempts(int attempts)
        => Math.Clamp(attempts, 1, MaxPurchaseAttempts);

    public static bool ShouldRetryTeleport(TimeSpan elapsed, TimeSpan sinceLastRetry, int retries)
        => elapsed < TeleportTimeout
           && retries < MaxTeleportRetries
           && sinceLastRetry >= TeleportRetryInterval;

    public static bool UsesRankOneToSevenShortcut(int? freeCompanyRank)
        => freeCompanyRank is >= 1 and <= 7;

    public static bool ShouldRun(
        FCBuffFrequency frequency,
        int? freeCompanyRank,
        DateTime lastCompleted,
        DateTime nextReset,
        DateTime now)
    {
        return frequency switch
        {
            FCBuffFrequency.EveryAR when !UsesRankOneToSevenShortcut(freeCompanyRank) => true,
            FCBuffFrequency.EveryAR or FCBuffFrequency.Daily or FCBuffFrequency.Weekly or FCBuffFrequency.Monthly =>
                lastCompleted == DateTime.MinValue || nextReset == DateTime.MinValue || now >= nextReset,
            _ => true,
        };
    }

    public static FCBuffFrequency ResolveCompletionFrequency(
        FCBuffFrequency configuredFrequency,
        bool usedRankOneToSevenShortcut)
        => usedRankOneToSevenShortcut && configuredFrequency == FCBuffFrequency.EveryAR
            ? FCBuffFrequency.Daily
            : configuredFrequency;

    public static (DateTime LastCompleted, DateTime NextReset)? GetCompletionTimestamps(
        FCBuffFrequency configuredFrequency,
        bool usedRankOneToSevenShortcut,
        DateTime completedAt)
    {
        var completionFrequency = ResolveCompletionFrequency(configuredFrequency, usedRankOneToSevenShortcut);
        return completionFrequency == FCBuffFrequency.EveryAR
            ? null
            : (completedAt, GetNextReset(completionFrequency, completedAt));
    }

    public static DateTime GetNextReset(FCBuffFrequency frequency, DateTime now)
    {
        var utc = now.ToUniversalTime();
        return frequency switch
        {
            FCBuffFrequency.Daily => GetNextDailyReset(utc),
            FCBuffFrequency.Weekly => GetNextWeeklyReset(utc),
            FCBuffFrequency.Monthly => new DateTime(utc.Year, utc.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1),
            _ => DateTime.MinValue,
        };
    }

    private static DateTime GetNextDailyReset(DateTime now)
    {
        var reset = now.Date.AddHours(DailyResetHourUtc);
        return now >= reset ? reset.AddDays(1) : reset;
    }

    private static DateTime GetNextWeeklyReset(DateTime now)
    {
        var daysUntilTuesday = ((int)DayOfWeek.Tuesday - (int)now.DayOfWeek + 7) % 7;
        var reset = now.Date.AddDays(daysUntilTuesday).AddHours(WeeklyResetHourUtc);
        return now >= reset ? reset.AddDays(7) : reset;
    }
}

public static class RegistrableRetryPolicy
{
    public const int MaxAttemptsPerItem = 3;

    public static bool ShouldExhaust(int attempts, int remainingQuantity)
        => remainingQuantity > 0 && attempts >= MaxAttemptsPerItem;
}

public sealed record VermaxionIncident(
    DateTime TimestampUtc,
    string Type,
    string State,
    string TaskId,
    string Summary,
    string Diagnostics);

public sealed class VermaxionIncidentWriter
{
    public const long DefaultMaxBytes = 100L * 1024L * 1024L;
    private readonly string path;
    private readonly long maxBytes;
    private readonly object sync = new();

    public VermaxionIncidentWriter(string configDirectory, long maxBytes = DefaultMaxBytes)
    {
        Directory.CreateDirectory(configDirectory);
        path = System.IO.Path.Combine(configDirectory, "vermaxion-incidents.jsonl");
        this.maxBytes = maxBytes;
    }

    public string Path => path;

    public void Write(VermaxionIncident incident)
    {
        var line = JsonSerializer.Serialize(incident) + Environment.NewLine;
        lock (sync)
        {
            RotateIfNeeded(System.Text.Encoding.UTF8.GetByteCount(line));
            File.AppendAllText(path, line);
        }
    }

    private void RotateIfNeeded(int incomingBytes)
    {
        if (!File.Exists(path) || new FileInfo(path).Length + incomingBytes <= maxBytes)
            return;

        var backupPath = path + ".1";
        if (File.Exists(backupPath))
            File.Delete(backupPath);
        File.Move(path, backupPath);
    }
}
