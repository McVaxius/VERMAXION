using System;
using System.IO;
using System.Text.Json;
using VERMAXION.Models;
using Xunit;

namespace VERMAXION.Tests;

public sealed class RecoveryPolicyTests : IDisposable
{
    private readonly string tempDirectory = Path.Combine(Path.GetTempPath(), $"vermaxion-tests-{Guid.NewGuid():N}");

    [Fact]
    public void WatchdogTimesOutOnlyWhenUnpausedWithoutProgress()
    {
        var now = DateTime.UtcNow;
        var lastProgress = now - TaskWatchdogPolicy.Timeout;

        Assert.True(TaskWatchdogPolicy.ShouldTimeout(now, lastProgress, paused: false));
        Assert.False(TaskWatchdogPolicy.ShouldTimeout(now, lastProgress, paused: true));
        Assert.False(TaskWatchdogPolicy.ShouldTimeout(now, now - TimeSpan.FromMinutes(1), paused: false));
    }

    [Fact]
    public void FCBuffAttemptsAndTeleportRetriesAreBounded()
    {
        Assert.Equal(1, FCBuffRecoveryPolicy.ClampPurchaseAttempts(0));
        Assert.Equal(15, FCBuffRecoveryPolicy.ClampPurchaseAttempts(999));
        Assert.True(FCBuffRecoveryPolicy.ShouldRetryTeleport(
            TimeSpan.FromSeconds(15),
            TimeSpan.FromSeconds(15),
            retries: 0));
        Assert.False(FCBuffRecoveryPolicy.ShouldRetryTeleport(
            TimeSpan.FromSeconds(15),
            TimeSpan.FromSeconds(15),
            retries: FCBuffRecoveryPolicy.MaxTeleportRetries));
        Assert.False(FCBuffRecoveryPolicy.ShouldRetryTeleport(
            FCBuffRecoveryPolicy.TeleportTimeout,
            TimeSpan.FromSeconds(15),
            retries: 0));
    }

    [Fact]
    public void RegistrableExhaustsAfterThreeAttempts()
    {
        Assert.False(RegistrableRetryPolicy.ShouldExhaust(2, remainingQuantity: 1));
        Assert.True(RegistrableRetryPolicy.ShouldExhaust(3, remainingQuantity: 1));
        Assert.False(RegistrableRetryPolicy.ShouldExhaust(3, remainingQuantity: 0));
    }

    [Fact]
    public void IncidentWriterRotatesAtBoundWithOneBackup()
    {
        Directory.CreateDirectory(tempDirectory);
        var writer = new VermaxionIncidentWriter(tempDirectory, maxBytes: 300);
        var incident = new VermaxionIncident(DateTime.UtcNow, "timeout", "Running", "task", new string('x', 100), "diag");

        writer.Write(incident);
        writer.Write(incident);
        writer.Write(incident);

        Assert.True(File.Exists(writer.Path));
        Assert.True(File.Exists(writer.Path + ".1"));
        Assert.False(File.Exists(writer.Path + ".2"));
        Assert.All(
            File.ReadAllLines(writer.Path),
            line => Assert.NotNull(JsonSerializer.Deserialize<VermaxionIncident>(line)));
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDirectory))
            Directory.Delete(tempDirectory, recursive: true);
    }
}
