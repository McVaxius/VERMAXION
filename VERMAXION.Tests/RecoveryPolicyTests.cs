using System;
using System.IO;
using System.Numerics;
using System.Text.Json;
using VERMAXION.Models;
using Xunit;

namespace VERMAXION.Tests;

public sealed class RecoveryPolicyTests : IDisposable
{
    private readonly string tempDirectory = Path.Combine(Path.GetTempPath(), $"vermaxion-tests-{Guid.NewGuid():N}");

    public static TheoryData<int, FCBuffFrequency, FCBuffFrequency> LowRankCadences => new()
    {
        { 1, FCBuffFrequency.EveryAR, FCBuffFrequency.Daily },
        { 1, FCBuffFrequency.Daily, FCBuffFrequency.Daily },
        { 1, FCBuffFrequency.Weekly, FCBuffFrequency.Weekly },
        { 1, FCBuffFrequency.Monthly, FCBuffFrequency.Monthly },
        { 7, FCBuffFrequency.EveryAR, FCBuffFrequency.Daily },
        { 7, FCBuffFrequency.Daily, FCBuffFrequency.Daily },
        { 7, FCBuffFrequency.Weekly, FCBuffFrequency.Weekly },
        { 7, FCBuffFrequency.Monthly, FCBuffFrequency.Monthly },
    };

    public static TheoryData<int?, FCBuffFrequency> EligibleAndUnknownRankCadences => new()
    {
        { 8, FCBuffFrequency.EveryAR },
        { 8, FCBuffFrequency.Daily },
        { 8, FCBuffFrequency.Weekly },
        { 8, FCBuffFrequency.Monthly },
        { 30, FCBuffFrequency.EveryAR },
        { 30, FCBuffFrequency.Daily },
        { 30, FCBuffFrequency.Weekly },
        { 30, FCBuffFrequency.Monthly },
        { null, FCBuffFrequency.EveryAR },
        { null, FCBuffFrequency.Daily },
        { null, FCBuffFrequency.Weekly },
        { null, FCBuffFrequency.Monthly },
    };

    [Fact]
    public void GroundNavigationProgressRestartsTheStallWindow()
    {
        var tracker = new GroundNavigationRecoveryTracker();
        var now = new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);
        var destination = new Vector3(10, 0, 10);

        Assert.Equal(GroundNavigationRecoveryAction.Dispatch, tracker.Evaluate(destination, false, Vector3.Zero, now));
        Assert.Equal(GroundNavigationRecoveryAction.Suppress, tracker.Evaluate(destination, false, new Vector3(0.5f, 0, 0), now.AddSeconds(11)));
        Assert.Equal(GroundNavigationRecoveryAction.Suppress, tracker.Evaluate(destination, false, new Vector3(0.5f, 0, 0), now.AddSeconds(22)));
        Assert.Equal(GroundNavigationRecoveryAction.Recover, tracker.Evaluate(destination, false, new Vector3(0.5f, 0, 0), now.AddSeconds(23)));
    }

    [Fact]
    public void GroundNavigationRecoversAtTwelveSecondsOnlyOncePerWindow()
    {
        var tracker = new GroundNavigationRecoveryTracker();
        var now = new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);
        var destination = new Vector3(10, 0, 10);

        Assert.Equal(GroundNavigationRecoveryAction.Dispatch, tracker.Evaluate(destination, false, Vector3.Zero, now));
        Assert.Equal(GroundNavigationRecoveryAction.Suppress, tracker.Evaluate(destination, false, Vector3.Zero, now.AddSeconds(11.999)));
        Assert.Equal(GroundNavigationRecoveryAction.Recover, tracker.Evaluate(destination, false, Vector3.Zero, now.AddSeconds(12)));
        Assert.Equal(GroundNavigationRecoveryAction.Suppress, tracker.Evaluate(destination, false, Vector3.Zero, now.AddSeconds(12)));
        Assert.Equal(GroundNavigationRecoveryAction.Suppress, tracker.Evaluate(destination, false, Vector3.Zero, now.AddSeconds(23.999)));
        Assert.Equal(GroundNavigationRecoveryAction.Recover, tracker.Evaluate(destination, false, Vector3.Zero, now.AddSeconds(24)));
    }

    [Fact]
    public void GroundNavigationDispatchesOnlyMaterialDestinationChanges()
    {
        var tracker = new GroundNavigationRecoveryTracker();
        var now = new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);
        var destination = new Vector3(10, 0, 10);

        Assert.Equal(GroundNavigationRecoveryAction.Dispatch, tracker.Evaluate(destination, false, Vector3.Zero, now));
        Assert.Equal(GroundNavigationRecoveryAction.Suppress, tracker.Evaluate(destination + new Vector3(0.49f, 0, 0), false, Vector3.Zero, now.AddSeconds(1)));
        Assert.Equal(GroundNavigationRecoveryAction.Dispatch, tracker.Evaluate(destination + new Vector3(0.5f, 0, 0), false, Vector3.Zero, now.AddSeconds(2)));
    }

    [Fact]
    public void GroundNavigationResetMakesTheSameDestinationNewAgain()
    {
        var tracker = new GroundNavigationRecoveryTracker();
        var now = new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);
        var destination = new Vector3(10, 0, 10);

        Assert.Equal(GroundNavigationRecoveryAction.Dispatch, tracker.Evaluate(destination, false, Vector3.Zero, now));
        tracker.Reset();
        Assert.Equal(GroundNavigationRecoveryAction.Dispatch, tracker.Evaluate(destination, false, Vector3.Zero, now.AddSeconds(30)));
    }

    [Fact]
    public void GroundNavigationUnavailablePlayerRestartsObservationWithoutRecovery()
    {
        var tracker = new GroundNavigationRecoveryTracker();
        var now = new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);
        var destination = new Vector3(10, 0, 10);

        Assert.Equal(GroundNavigationRecoveryAction.Dispatch, tracker.Evaluate(destination, false, Vector3.Zero, now));
        Assert.Equal(GroundNavigationRecoveryAction.Suppress, tracker.Evaluate(destination, false, null, now.AddSeconds(12)));
        Assert.Equal(GroundNavigationRecoveryAction.Suppress, tracker.Evaluate(destination, false, Vector3.Zero, now.AddMinutes(5)));
        Assert.Equal(GroundNavigationRecoveryAction.Recover, tracker.Evaluate(destination, false, Vector3.Zero, now.AddMinutes(5).AddSeconds(12)));
    }

    [Fact]
    public void GroundNavigationFlyDispatchesDirectlyAndClearsGroundRecovery()
    {
        var tracker = new GroundNavigationRecoveryTracker();
        var now = new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);
        var destination = new Vector3(10, 0, 10);

        Assert.Equal(GroundNavigationRecoveryAction.Dispatch, tracker.Evaluate(destination, false, Vector3.Zero, now));
        Assert.Equal(GroundNavigationRecoveryAction.Dispatch, tracker.Evaluate(destination, true, Vector3.Zero, now.AddSeconds(12)));
        Assert.Equal(GroundNavigationRecoveryAction.Dispatch, tracker.Evaluate(destination, true, null, now.AddSeconds(12.5)));
        Assert.Equal(GroundNavigationRecoveryAction.Dispatch, tracker.Evaluate(destination, false, Vector3.Zero, now.AddSeconds(13)));
    }

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

    [Theory]
    [MemberData(nameof(LowRankCadences))]
    public void FCBuffLowRankCompletionUsesValidConfiguredCadence(
        int rank,
        FCBuffFrequency configuredFrequency,
        FCBuffFrequency expectedCompletionFrequency)
    {
        var completedAt = new DateTime(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc);

        Assert.True(FCBuffRecoveryPolicy.UsesRankOneToSevenShortcut(rank));
        Assert.Equal(
            expectedCompletionFrequency,
            FCBuffRecoveryPolicy.ResolveCompletionFrequency(configuredFrequency, usedRankOneToSevenShortcut: true));

        var completion = FCBuffRecoveryPolicy.GetCompletionTimestamps(
            configuredFrequency,
            usedRankOneToSevenShortcut: true,
            completedAt);

        Assert.NotNull(completion);
        Assert.Equal(completedAt, completion.Value.LastCompleted);
        Assert.Equal(ExpectedReset(expectedCompletionFrequency), completion.Value.NextReset);
        Assert.NotEqual(DateTime.MinValue, completion.Value.NextReset);
        Assert.True(completion.Value.NextReset > completion.Value.LastCompleted);
        Assert.False(FCBuffRecoveryPolicy.ShouldRun(
            configuredFrequency,
            rank,
            completion.Value.LastCompleted,
            completion.Value.NextReset,
            completion.Value.NextReset.AddTicks(-1)));
        Assert.True(FCBuffRecoveryPolicy.ShouldRun(
            configuredFrequency,
            rank,
            completion.Value.LastCompleted,
            completion.Value.NextReset,
            completion.Value.NextReset));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    public void FCBuffLowRankEveryArFallbackSuppressesUntilDailyReset(int rank)
    {
        var completedAt = new DateTime(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc);
        var completion = FCBuffRecoveryPolicy.GetCompletionTimestamps(
            FCBuffFrequency.EveryAR,
            usedRankOneToSevenShortcut: true,
            completedAt)!.Value;

        Assert.False(FCBuffRecoveryPolicy.ShouldRun(
            FCBuffFrequency.EveryAR,
            rank,
            completion.LastCompleted,
            completion.NextReset,
            completion.NextReset.AddTicks(-1)));
        Assert.True(FCBuffRecoveryPolicy.ShouldRun(
            FCBuffFrequency.EveryAR,
            rank,
            completion.LastCompleted,
            completion.NextReset,
            completion.NextReset));
    }

    [Theory]
    [MemberData(nameof(EligibleAndUnknownRankCadences))]
    public void FCBuffEligibleAndUnknownRanksFollowConfiguredCadence(
        int? rank,
        FCBuffFrequency frequency)
    {
        var completedAt = new DateTime(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc);
        var savedResetFrequency = frequency == FCBuffFrequency.EveryAR
            ? FCBuffFrequency.Daily
            : frequency;
        var nextReset = FCBuffRecoveryPolicy.GetNextReset(savedResetFrequency, completedAt);

        Assert.False(FCBuffRecoveryPolicy.UsesRankOneToSevenShortcut(rank));
        Assert.Equal(
            frequency == FCBuffFrequency.EveryAR,
            FCBuffRecoveryPolicy.ShouldRun(
                frequency,
                rank,
                completedAt,
                nextReset,
                nextReset.AddTicks(-1)));
        Assert.True(FCBuffRecoveryPolicy.ShouldRun(
            frequency,
            rank,
            completedAt,
            nextReset,
            nextReset));
    }

    [Theory]
    [InlineData(FCBuffFrequency.EveryAR)]
    [InlineData(FCBuffFrequency.Daily)]
    [InlineData(FCBuffFrequency.Weekly)]
    [InlineData(FCBuffFrequency.Monthly)]
    public void FCBuffOrdinaryCompletionRetainsConfiguredCadence(FCBuffFrequency frequency)
    {
        var completedAt = new DateTime(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal(
            frequency,
            FCBuffRecoveryPolicy.ResolveCompletionFrequency(frequency, usedRankOneToSevenShortcut: false));

        var completion = FCBuffRecoveryPolicy.GetCompletionTimestamps(
            frequency,
            usedRankOneToSevenShortcut: false,
            completedAt);
        if (frequency == FCBuffFrequency.EveryAR)
        {
            Assert.Null(completion);
            return;
        }

        Assert.NotNull(completion);
        Assert.Equal(completedAt, completion.Value.LastCompleted);
        Assert.Equal(ExpectedReset(frequency), completion.Value.NextReset);
    }

    [Fact]
    public void FCBuffEveryArFallbackStopsSuppressingAfterRankEightPromotion()
    {
        var completedAt = new DateTime(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc);
        var completion = FCBuffRecoveryPolicy.GetCompletionTimestamps(
            FCBuffFrequency.EveryAR,
            usedRankOneToSevenShortcut: true,
            completedAt)!.Value;
        var beforeReset = completion.NextReset.AddTicks(-1);

        Assert.False(FCBuffRecoveryPolicy.ShouldRun(
            FCBuffFrequency.EveryAR,
            7,
            completion.LastCompleted,
            completion.NextReset,
            beforeReset));
        Assert.True(FCBuffRecoveryPolicy.ShouldRun(
            FCBuffFrequency.EveryAR,
            8,
            completion.LastCompleted,
            completion.NextReset,
            beforeReset));
        Assert.True(FCBuffRecoveryPolicy.ShouldRun(
            FCBuffFrequency.EveryAR,
            null,
            completion.LastCompleted,
            completion.NextReset,
            beforeReset));
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

    private static DateTime ExpectedReset(FCBuffFrequency frequency)
        => frequency switch
        {
            FCBuffFrequency.Daily => new DateTime(2026, 8, 28, 9, 0, 0, DateTimeKind.Utc),
            FCBuffFrequency.Weekly => new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc),
            FCBuffFrequency.Monthly => new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            _ => DateTime.MinValue,
        };
}
