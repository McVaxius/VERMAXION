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
