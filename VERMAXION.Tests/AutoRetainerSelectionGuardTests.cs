using System;
using System.Collections.Generic;
using VERMAXION.Models;
using VERMAXION.Services;
using Xunit;

namespace VERMAXION.Tests;

public sealed class AutoRetainerSelectionGuardTests
{
    private const ulong FirstContentId = 0x4000174C2E9539;
    private const ulong SecondContentId = 0x4000174C2E9532;
    private static readonly DateTime StartUtc = new(2026, 7, 12, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void DisabledGuardPerformsNoReflectionReadsOrWrites()
    {
        var (guard, accessor, _, _) = CreateGuard();

        guard.Update(enabled: false, isLoggedIn: true, FirstContentId, StartUtc);
        guard.NotifyCurrentTaskWorkStarted(enabled: false, isLoggedIn: true, FirstContentId, StartUtc);
        guard.Update(enabled: false, isLoggedIn: true, FirstContentId, StartUtc + TimeSpan.FromMinutes(1));

        Assert.Equal(AutoRetainerSelectionGuardState.Completed, guard.State);
        Assert.Equal(0, accessor.ReadCount);
        Assert.Equal(0, accessor.WriteCount);
    }

    [Fact]
    public void DeselectionIsIgnoredUntilVermaxionRealWorkStarts()
    {
        var (guard, accessor, _, _) = CreateGuard();
        accessor.Enabled = false;

        guard.Update(enabled: true, isLoggedIn: true, FirstContentId, StartUtc);
        guard.Update(enabled: true, isLoggedIn: true, FirstContentId, StartUtc + TimeSpan.FromHours(1));

        Assert.Equal(AutoRetainerSelectionGuardState.AwaitingWorkStart, guard.State);
        Assert.Equal(0, accessor.ReadCount);
        Assert.Equal(0, accessor.WriteCount);
    }

    [Fact]
    public void CharacterAlreadyDeselectedAtWorkStartIsNeverEnabled()
    {
        var (guard, accessor, _, _) = CreateGuard();
        accessor.Enabled = false;

        guard.NotifyCurrentTaskWorkStarted(enabled: true, isLoggedIn: true, FirstContentId, StartUtc);
        guard.Update(enabled: true, isLoggedIn: true, FirstContentId, StartUtc + TimeSpan.FromHours(1));

        Assert.Equal(AutoRetainerSelectionGuardState.Completed, guard.State);
        Assert.Equal(1, accessor.ReadCount);
        Assert.Equal(0, accessor.WriteCount);
        Assert.False(accessor.Enabled);
    }

    [Fact]
    public void SelectedCharacterRemainingSelectedDoesNotTriggerWrite()
    {
        var (guard, accessor, _, _) = CreateGuard();

        guard.NotifyCurrentTaskWorkStarted(enabled: true, isLoggedIn: true, FirstContentId, StartUtc);
        guard.Update(enabled: true, isLoggedIn: true, FirstContentId, NextObservation(1));

        Assert.Equal(AutoRetainerSelectionGuardState.Observing, guard.State);
        Assert.Equal(2, accessor.ReadCount);
        Assert.Equal(0, accessor.WriteCount);
    }

    [Fact]
    public void SelectedToDeselectedTransitionProducesOneVerifiedRestore()
    {
        var (guard, accessor, _, _) = CreateGuard();

        guard.NotifyCurrentTaskWorkStarted(enabled: true, isLoggedIn: true, FirstContentId, StartUtc);
        accessor.Enabled = false;
        guard.Update(enabled: true, isLoggedIn: true, FirstContentId, NextObservation(1));

        Assert.Equal(AutoRetainerSelectionGuardState.Completed, guard.State);
        Assert.True(guard.RepairSucceeded);
        Assert.Equal(1, guard.RepairIncidentCount);
        Assert.Equal(1, guard.RepairAttemptCount);
        Assert.Equal(1, accessor.WriteCount);
        Assert.True(accessor.Enabled);

        accessor.Enabled = false;
        guard.Update(enabled: true, isLoggedIn: true, FirstContentId, NextObservation(10));

        Assert.Equal(1, accessor.WriteCount);
        Assert.Equal(2, accessor.ReadCount);
    }

    [Fact]
    public void LogoutAndContentIdChangeResetSessionState()
    {
        var (guard, accessor, _, _) = CreateGuard();

        guard.NotifyCurrentTaskWorkStarted(enabled: true, isLoggedIn: true, FirstContentId, StartUtc);
        Assert.Equal(AutoRetainerSelectionGuardState.Observing, guard.State);

        guard.Update(enabled: true, isLoggedIn: true, SecondContentId, StartUtc);
        Assert.Equal(AutoRetainerSelectionGuardState.AwaitingWorkStart, guard.State);
        Assert.Equal(SecondContentId, guard.SessionContentId);
        Assert.Equal(0, guard.RepairAttemptCount);

        guard.Update(enabled: true, isLoggedIn: false, 0, StartUtc);
        Assert.Equal(AutoRetainerSelectionGuardState.Inactive, guard.State);
        Assert.Equal(0UL, guard.SessionContentId);

        guard.Update(enabled: true, isLoggedIn: true, FirstContentId, StartUtc);
        Assert.Equal(AutoRetainerSelectionGuardState.AwaitingWorkStart, guard.State);
        Assert.Equal(1, accessor.ReadCount);
        Assert.Equal(0, accessor.WriteCount);
    }

    [Fact]
    public void WorkStartReadFailureFailsClosedWithoutRetryingOrWriting()
    {
        var (guard, accessor, _, warnings) = CreateGuard();
        accessor.ReadResults.Enqueue(AutoRetainerSelectionReadResult.Failed("AutoRetainer type missing"));

        var exception = Record.Exception(() =>
            guard.NotifyCurrentTaskWorkStarted(enabled: true, isLoggedIn: true, FirstContentId, StartUtc));

        Assert.Null(exception);
        Assert.Equal(AutoRetainerSelectionGuardState.Completed, guard.State);
        Assert.Equal(1, accessor.ReadCount);
        Assert.Equal(0, accessor.WriteCount);
        Assert.Contains(warnings, message => message.Contains("failed closed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ObservationReadFailuresAreBoundedAndNeverWrite()
    {
        var (guard, accessor, _, warnings) = CreateGuard();
        accessor.ReadResults.Enqueue(AutoRetainerSelectionReadResult.Known(true));
        for (var index = 0; index < AutoRetainerSelectionGuard.MaxObservationReadFailures; index++)
            accessor.ReadResults.Enqueue(AutoRetainerSelectionReadResult.Failed($"read failure {index + 1}"));

        guard.NotifyCurrentTaskWorkStarted(enabled: true, isLoggedIn: true, FirstContentId, StartUtc);
        for (var index = 1; index <= AutoRetainerSelectionGuard.MaxObservationReadFailures; index++)
            guard.Update(enabled: true, isLoggedIn: true, FirstContentId, NextObservation(index));

        Assert.Equal(AutoRetainerSelectionGuardState.Completed, guard.State);
        Assert.Equal(1 + AutoRetainerSelectionGuard.MaxObservationReadFailures, accessor.ReadCount);
        Assert.Equal(0, accessor.WriteCount);
        Assert.Contains(warnings, message => message.Contains("Stopped watching", StringComparison.Ordinal));
    }

    [Fact]
    public void WriteAndSaveFailuresAreOneIncidentWithAtMostThreeAttempts()
    {
        var (guard, accessor, _, warnings) = CreateGuard();
        accessor.WriteResults.Enqueue(AutoRetainerSelectionWriteResult.Failed("write member missing"));
        accessor.WriteResults.Enqueue(AutoRetainerSelectionWriteResult.Failed(
            "save threw",
            enabled: true,
            saveInvoked: true));
        accessor.WriteResults.Enqueue(AutoRetainerSelectionWriteResult.Failed("verification failed"));

        guard.NotifyCurrentTaskWorkStarted(enabled: true, isLoggedIn: true, FirstContentId, StartUtc);
        accessor.Enabled = false;
        guard.Update(enabled: true, isLoggedIn: true, FirstContentId, NextObservation(1));
        guard.Update(enabled: true, isLoggedIn: true, FirstContentId, NextObservation(2));
        guard.Update(enabled: true, isLoggedIn: true, FirstContentId, NextObservation(3));
        guard.Update(enabled: true, isLoggedIn: true, FirstContentId, NextObservation(20));

        Assert.Equal(AutoRetainerSelectionGuardState.Completed, guard.State);
        Assert.False(guard.RepairSucceeded);
        Assert.Equal(1, guard.RepairIncidentCount);
        Assert.Equal(AutoRetainerSelectionGuard.MaxRepairAttempts, guard.RepairAttemptCount);
        Assert.Equal(AutoRetainerSelectionGuard.MaxRepairAttempts, accessor.WriteCount);
        Assert.Contains(warnings, message => message.Contains("Repair exhausted", StringComparison.Ordinal));
    }

    [Fact]
    public void AccessorExceptionsDoNotEscapeTheGuard()
    {
        var (guard, accessor, _, _) = CreateGuard();
        accessor.ThrowOnRead = true;

        var readException = Record.Exception(() =>
            guard.NotifyCurrentTaskWorkStarted(enabled: true, isLoggedIn: true, FirstContentId, StartUtc));

        Assert.Null(readException);
        Assert.Equal(AutoRetainerSelectionGuardState.Completed, guard.State);
        Assert.Equal(0, accessor.WriteCount);
    }

    [Fact]
    public void GuardLifecycleNeverContributesToDadAutomationStatus()
    {
        var (guard, accessor, _, _) = CreateGuard();
        AssertIdleStatus();

        guard.Update(enabled: true, isLoggedIn: true, FirstContentId, StartUtc);
        Assert.Equal(AutoRetainerSelectionGuardState.AwaitingWorkStart, guard.State);
        AssertIdleStatus();

        guard.NotifyCurrentTaskWorkStarted(enabled: true, isLoggedIn: true, FirstContentId, StartUtc);
        Assert.Equal(AutoRetainerSelectionGuardState.Observing, guard.State);
        AssertIdleStatus();

        accessor.WriteResults.Enqueue(AutoRetainerSelectionWriteResult.Failed("retry"));
        accessor.WriteResults.Enqueue(AutoRetainerSelectionWriteResult.Verified(true));
        accessor.Enabled = false;
        guard.Update(enabled: true, isLoggedIn: true, FirstContentId, NextObservation(1));
        Assert.Equal(AutoRetainerSelectionGuardState.Repairing, guard.State);
        AssertIdleStatus();

        guard.Update(enabled: true, isLoggedIn: true, FirstContentId, NextObservation(2));
        Assert.Equal(AutoRetainerSelectionGuardState.Completed, guard.State);
        AssertIdleStatus();
    }

    private static void AssertIdleStatus()
    {
        var status = AutomationStatusPolicy.Evaluate(new AutomationOwnershipSnapshot(), StartUtc);
        Assert.False(status.IsBusy);
        Assert.Equal("Idle", status.Activity);
    }

    private static DateTime NextObservation(int number)
        => StartUtc + TimeSpan.FromTicks(AutoRetainerSelectionGuard.ObservationInterval.Ticks * number);

    private static (
        AutoRetainerSelectionGuard Guard,
        FakeSelectionAccessor Accessor,
        List<string> Information,
        List<string> Warnings) CreateGuard()
    {
        var accessor = new FakeSelectionAccessor();
        var information = new List<string>();
        var warnings = new List<string>();
        var guard = new AutoRetainerSelectionGuard(accessor, information.Add, warnings.Add);
        return (guard, accessor, information, warnings);
    }

    private sealed class FakeSelectionAccessor : IAutoRetainerSelectionAccessor
    {
        public Queue<AutoRetainerSelectionReadResult> ReadResults { get; } = new();
        public Queue<AutoRetainerSelectionWriteResult> WriteResults { get; } = new();
        public bool Enabled { get; set; } = true;
        public bool ThrowOnRead { get; set; }
        public bool ThrowOnWrite { get; set; }
        public int ReadCount { get; private set; }
        public int WriteCount { get; private set; }

        public AutoRetainerSelectionReadResult ReadCurrentCharacterSelection(ulong localContentId)
        {
            ReadCount++;
            if (ThrowOnRead)
                throw new InvalidOperationException("read exploded");
            return ReadResults.Count > 0
                ? ReadResults.Dequeue()
                : AutoRetainerSelectionReadResult.Known(Enabled);
        }

        public AutoRetainerSelectionWriteResult WriteCurrentCharacterSelection(
            ulong localContentId,
            bool enabled)
        {
            WriteCount++;
            if (ThrowOnWrite)
                throw new InvalidOperationException("write exploded");

            if (WriteResults.Count > 0)
            {
                var queued = WriteResults.Dequeue();
                Enabled = queued.Enabled;
                return queued;
            }

            Enabled = enabled;
            return AutoRetainerSelectionWriteResult.Verified(enabled);
        }
    }
}
