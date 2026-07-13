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
    private const ulong ThirdContentId = 0x4000174C2E9540;
    private static readonly DateTime StartUtc = new(2026, 7, 12, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void DisabledGuardPerformsNoReflectionReadsOrWrites()
    {
        var (guard, accessor, _, _) = CreateGuard();

        guard.Update(enabled: false, isLoggedIn: true, FirstContentId, StartUtc);
        guard.Update(enabled: false, isLoggedIn: true, FirstContentId, NextObservation(10));

        Assert.Equal(AutoRetainerSelectionGuardState.Inactive, guard.State);
        Assert.Empty(accessor.ReadIds);
        Assert.Empty(accessor.WriteIds);
    }

    [Fact]
    public void DisabledGuardTracksCharacterPairForLaterOptInWithoutReflection()
    {
        var (guard, accessor, _, _) = CreateGuard();

        guard.Update(enabled: false, isLoggedIn: true, FirstContentId, StartUtc);
        guard.Update(enabled: false, isLoggedIn: false, 0, StartUtc + TimeSpan.FromMilliseconds(500));
        guard.Update(enabled: false, isLoggedIn: true, SecondContentId, NextObservation(1));

        Assert.Empty(accessor.ReadIds);
        Assert.Empty(accessor.WriteIds);

        accessor.Selections[FirstContentId] = false;
        accessor.Selections[SecondContentId] = false;
        guard.Update(enabled: true, isLoggedIn: true, SecondContentId, NextObservation(2));

        Assert.Equal([FirstContentId, SecondContentId], accessor.ReadIds);
        Assert.Equal([FirstContentId, SecondContentId], accessor.WriteIds);
    }

    [Fact]
    public void AlreadyDeselectedCurrentCharacterIsRestoredWithoutVermaxionWork()
    {
        var (guard, accessor, _, _) = CreateGuard();
        accessor.Selections[FirstContentId] = false;

        guard.Update(enabled: true, isLoggedIn: true, FirstContentId, StartUtc);

        Assert.Equal(AutoRetainerSelectionGuardState.Completed, guard.State);
        Assert.True(accessor.Selections[FirstContentId]);
        Assert.Equal([FirstContentId], accessor.ReadIds);
        Assert.Equal([FirstContentId], accessor.WriteIds);
        Assert.True(guard.RepairSucceeded);
        Assert.Equal(1, guard.RepairIncidentCount);
    }

    [Fact]
    public void EnabledCurrentCharacterRemainsObservedWithoutWrites()
    {
        var (guard, accessor, _, _) = CreateGuard();

        guard.Update(enabled: true, isLoggedIn: true, FirstContentId, StartUtc);
        guard.Update(enabled: true, isLoggedIn: true, FirstContentId, StartUtc + TimeSpan.FromMilliseconds(500));
        guard.Update(enabled: true, isLoggedIn: true, FirstContentId, NextObservation(1));

        Assert.Equal(AutoRetainerSelectionGuardState.Observing, guard.State);
        Assert.Equal([FirstContentId, FirstContentId], accessor.ReadIds);
        Assert.Empty(accessor.WriteIds);
    }

    [Fact]
    public void RelogRestoresDisabledPreviousThenCurrentCharacter()
    {
        var (guard, accessor, _, _) = CreateGuard();
        guard.Update(enabled: true, isLoggedIn: true, FirstContentId, StartUtc);
        guard.Update(enabled: true, isLoggedIn: false, 0, StartUtc + TimeSpan.FromMilliseconds(500));

        accessor.Selections[FirstContentId] = false;
        accessor.Selections[SecondContentId] = false;
        accessor.ClearHistory();
        guard.Update(enabled: true, isLoggedIn: true, SecondContentId, NextObservation(1));

        Assert.Equal(FirstContentId, guard.PreviousContentId);
        Assert.Equal(SecondContentId, guard.SessionContentId);
        Assert.Equal([FirstContentId, SecondContentId], accessor.ReadIds);
        Assert.Equal([FirstContentId, SecondContentId], accessor.WriteIds);
        Assert.True(accessor.Selections[FirstContentId]);
        Assert.True(accessor.Selections[SecondContentId]);
        Assert.Equal(AutoRetainerSelectionGuardState.Completed, guard.State);
    }

    [Fact]
    public void DelayedPreviousCharacterDeselectionIsRepairedAfterRelog()
    {
        var (guard, accessor, _, _) = CreateGuard();
        guard.Update(enabled: true, isLoggedIn: true, FirstContentId, StartUtc);
        guard.Update(enabled: true, isLoggedIn: false, 0, StartUtc + TimeSpan.FromMilliseconds(500));
        guard.Update(enabled: true, isLoggedIn: true, SecondContentId, NextObservation(1));

        accessor.Selections[FirstContentId] = false;
        accessor.ClearHistory();
        guard.Update(enabled: true, isLoggedIn: true, SecondContentId, NextObservation(2));

        Assert.Equal([FirstContentId, SecondContentId], accessor.ReadIds);
        Assert.Equal([FirstContentId], accessor.WriteIds);
        Assert.True(accessor.Selections[FirstContentId]);
        Assert.Equal(AutoRetainerSelectionGuardState.Observing, guard.State);
    }

    [Fact]
    public void SameCharacterRelogIsDeduplicated()
    {
        var (guard, accessor, _, _) = CreateGuard();
        guard.Update(enabled: true, isLoggedIn: true, FirstContentId, StartUtc);
        guard.Update(enabled: true, isLoggedIn: false, 0, StartUtc + TimeSpan.FromMilliseconds(500));

        accessor.Selections[FirstContentId] = false;
        accessor.ClearHistory();
        guard.Update(enabled: true, isLoggedIn: true, FirstContentId, NextObservation(1));

        Assert.Equal(0UL, guard.PreviousContentId);
        Assert.Equal([FirstContentId], accessor.ReadIds);
        Assert.Equal([FirstContentId], accessor.WriteIds);
    }

    [Fact]
    public void SameCharacterRelogRearmsFreshRepairBudget()
    {
        var (guard, accessor, _, _) = CreateGuard();
        accessor.Selections[FirstContentId] = false;
        guard.Update(enabled: true, isLoggedIn: true, FirstContentId, StartUtc);
        Assert.Single(accessor.WriteIds);

        guard.Update(enabled: true, isLoggedIn: false, 0, StartUtc + TimeSpan.FromMilliseconds(500));
        accessor.Selections[FirstContentId] = false;
        guard.Update(enabled: true, isLoggedIn: true, FirstContentId, NextObservation(1));

        Assert.Equal(0UL, guard.PreviousContentId);
        Assert.Equal([FirstContentId, FirstContentId], accessor.WriteIds);
        Assert.True(accessor.Selections[FirstContentId]);
    }

    [Fact]
    public void SameCharacterRelogPreservesLastDistinctPreviousCharacter()
    {
        var (guard, accessor, _, _) = CreateGuard();
        guard.Update(enabled: true, isLoggedIn: true, FirstContentId, StartUtc);
        guard.Update(enabled: true, isLoggedIn: true, SecondContentId, NextObservation(1));
        guard.Update(enabled: true, isLoggedIn: false, 0, StartUtc + TimeSpan.FromMilliseconds(1500));

        accessor.ClearHistory();
        guard.Update(enabled: true, isLoggedIn: true, SecondContentId, NextObservation(2));

        Assert.Equal(FirstContentId, guard.PreviousContentId);
        Assert.Equal(SecondContentId, guard.SessionContentId);
        Assert.Equal([FirstContentId, SecondContentId], accessor.ReadIds);
        Assert.Empty(accessor.WriteIds);
    }

    [Fact]
    public void RapidTransitionsRetainOnlyNewestCurrentAndPreviousPair()
    {
        var (guard, accessor, _, _) = CreateGuard();
        guard.Update(enabled: true, isLoggedIn: true, FirstContentId, StartUtc);
        guard.Update(enabled: true, isLoggedIn: true, SecondContentId, NextObservation(1));

        accessor.Selections[FirstContentId] = false;
        accessor.Selections[SecondContentId] = false;
        accessor.Selections[ThirdContentId] = false;
        accessor.ClearHistory();
        guard.Update(enabled: true, isLoggedIn: true, ThirdContentId, NextObservation(2));

        Assert.Equal(SecondContentId, guard.PreviousContentId);
        Assert.Equal(ThirdContentId, guard.SessionContentId);
        Assert.DoesNotContain(FirstContentId, accessor.ReadIds);
        Assert.Equal([SecondContentId, ThirdContentId], accessor.WriteIds);
        Assert.False(accessor.Selections[FirstContentId]);
    }

    [Fact]
    public void SuccessfulRepairRunsOnlyOncePerTargetPerTransition()
    {
        var (guard, accessor, _, _) = CreateGuard();
        accessor.Selections[FirstContentId] = false;

        guard.Update(enabled: true, isLoggedIn: true, FirstContentId, StartUtc);
        accessor.Selections[FirstContentId] = false;
        guard.Update(enabled: true, isLoggedIn: true, FirstContentId, NextObservation(10));

        Assert.Single(accessor.WriteIds);
        Assert.False(accessor.Selections[FirstContentId]);
        Assert.Equal(AutoRetainerSelectionGuardState.Completed, guard.State);
    }

    [Fact]
    public void WriteFailuresAreBoundedToThreeAttemptsPerTarget()
    {
        var (guard, accessor, _, warnings) = CreateGuard();
        accessor.Selections[FirstContentId] = false;
        accessor.WriteResults.Enqueue(AutoRetainerSelectionWriteResult.Failed("write member missing"));
        accessor.WriteResults.Enqueue(AutoRetainerSelectionWriteResult.Failed(
            "save threw",
            enabled: true,
            saveInvoked: true));
        accessor.WriteResults.Enqueue(AutoRetainerSelectionWriteResult.Failed("verification failed"));

        guard.Update(enabled: true, isLoggedIn: true, FirstContentId, StartUtc);
        guard.Update(enabled: true, isLoggedIn: true, FirstContentId, NextObservation(1));
        guard.Update(enabled: true, isLoggedIn: true, FirstContentId, NextObservation(2));
        guard.Update(enabled: true, isLoggedIn: true, FirstContentId, NextObservation(20));

        Assert.Equal(AutoRetainerSelectionGuard.MaxRepairAttempts, accessor.WriteIds.Count);
        Assert.Equal(AutoRetainerSelectionGuard.MaxRepairAttempts, guard.RepairAttemptCount);
        Assert.False(guard.RepairSucceeded);
        Assert.Equal(AutoRetainerSelectionGuardState.Completed, guard.State);
        Assert.Contains(warnings, message => message.Contains("Repair exhausted", StringComparison.Ordinal));
    }

    [Fact]
    public void PreviousAndCurrentRepairBudgetsAreIndependent()
    {
        var (guard, accessor, _, _) = CreateGuard();
        guard.Update(enabled: true, isLoggedIn: true, FirstContentId, StartUtc);

        accessor.Selections[FirstContentId] = false;
        accessor.Selections[SecondContentId] = false;
        accessor.WriteResultsByContentId[FirstContentId] = new Queue<AutoRetainerSelectionWriteResult>(
        [
            AutoRetainerSelectionWriteResult.Failed("previous attempt 1"),
            AutoRetainerSelectionWriteResult.Failed("previous attempt 2"),
            AutoRetainerSelectionWriteResult.Failed("previous attempt 3"),
        ]);

        guard.Update(enabled: true, isLoggedIn: true, SecondContentId, NextObservation(1));
        guard.Update(enabled: true, isLoggedIn: true, SecondContentId, NextObservation(2));
        guard.Update(enabled: true, isLoggedIn: true, SecondContentId, NextObservation(3));

        Assert.Equal(3, accessor.WriteIds.FindAll(id => id == FirstContentId).Count);
        Assert.Single(accessor.WriteIds.FindAll(id => id == SecondContentId));
        Assert.False(accessor.Selections[FirstContentId]);
        Assert.True(accessor.Selections[SecondContentId]);
        Assert.Equal(4, guard.RepairAttemptCount);
        Assert.Equal(AutoRetainerSelectionGuardState.Completed, guard.State);
    }

    [Fact]
    public void ReadFailuresStayFailClosedAndRecoverWithoutLogSpam()
    {
        var (guard, accessor, _, warnings) = CreateGuard();
        for (var index = 0; index < 4; index++)
            accessor.ReadResults.Enqueue(AutoRetainerSelectionReadResult.Failed($"read failure {index + 1}"));
        accessor.ReadResults.Enqueue(AutoRetainerSelectionReadResult.Known(false));

        for (var index = 0; index < 4; index++)
            guard.Update(enabled: true, isLoggedIn: true, FirstContentId, NextObservation(index));

        Assert.Empty(accessor.WriteIds);
        Assert.Single(warnings.FindAll(message => message.Contains("selection read failed closed", StringComparison.Ordinal)));

        guard.Update(enabled: true, isLoggedIn: true, FirstContentId, NextObservation(4));

        Assert.Equal([FirstContentId], accessor.WriteIds);
        Assert.True(accessor.Selections[FirstContentId]);
    }

    [Fact]
    public void AlternatingReadFailuresRemainWallClockThrottled()
    {
        var (guard, accessor, _, warnings) = CreateGuard();
        accessor.ReadResults.Enqueue(AutoRetainerSelectionReadResult.Failed("failure 1"));
        accessor.ReadResults.Enqueue(AutoRetainerSelectionReadResult.Known(true));
        accessor.ReadResults.Enqueue(AutoRetainerSelectionReadResult.Failed("failure 2"));
        accessor.ReadResults.Enqueue(AutoRetainerSelectionReadResult.Known(true));
        accessor.ReadResults.Enqueue(AutoRetainerSelectionReadResult.Failed("failure 3"));

        for (var index = 0; index < 5; index++)
            guard.Update(enabled: true, isLoggedIn: true, FirstContentId, NextObservation(index));

        Assert.Single(warnings.FindAll(message => message.Contains("selection read failed closed", StringComparison.Ordinal)));
        Assert.Empty(accessor.WriteIds);
    }

    [Fact]
    public void DisablingGuardCancelsPendingRepair()
    {
        var (guard, accessor, _, _) = CreateGuard();
        accessor.Selections[FirstContentId] = false;
        accessor.WriteResults.Enqueue(AutoRetainerSelectionWriteResult.Failed("retry later"));
        accessor.WriteResults.Enqueue(AutoRetainerSelectionWriteResult.Verified(true));

        guard.Update(enabled: true, isLoggedIn: true, FirstContentId, StartUtc);
        Assert.Equal(AutoRetainerSelectionGuardState.Repairing, guard.State);

        guard.Update(enabled: false, isLoggedIn: true, FirstContentId, NextObservation(1));
        guard.Update(enabled: false, isLoggedIn: true, FirstContentId, NextObservation(2));

        Assert.Single(accessor.WriteIds);
        Assert.Equal(AutoRetainerSelectionGuardState.Inactive, guard.State);
        Assert.False(accessor.Selections[FirstContentId]);
    }

    [Fact]
    public void PendingRepairPausesAcrossLogoutAndMissingContentId()
    {
        var (guard, accessor, _, _) = CreateGuard();
        accessor.Selections[FirstContentId] = false;
        accessor.WriteResults.Enqueue(AutoRetainerSelectionWriteResult.Failed("retry after relog"));
        accessor.WriteResults.Enqueue(AutoRetainerSelectionWriteResult.Verified(true));

        guard.Update(enabled: true, isLoggedIn: true, FirstContentId, StartUtc);
        Assert.Single(accessor.WriteIds);

        guard.Update(enabled: true, isLoggedIn: false, 0, NextObservation(1));
        guard.Update(enabled: true, isLoggedIn: true, 0, NextObservation(2));
        Assert.Single(accessor.WriteIds);

        guard.Update(enabled: true, isLoggedIn: true, SecondContentId, NextObservation(3));

        Assert.Equal([FirstContentId, FirstContentId], accessor.WriteIds);
        Assert.True(accessor.Selections[FirstContentId]);
    }

    [Fact]
    public void RapidTransitionDiscardsStalePendingRetryTarget()
    {
        var (guard, accessor, _, _) = CreateGuard();
        accessor.Selections[FirstContentId] = false;
        accessor.WriteResultsByContentId[FirstContentId] = new Queue<AutoRetainerSelectionWriteResult>(
        [
            AutoRetainerSelectionWriteResult.Failed("retry A"),
            AutoRetainerSelectionWriteResult.Failed("retry A again"),
        ]);

        guard.Update(enabled: true, isLoggedIn: true, FirstContentId, StartUtc);
        guard.Update(enabled: true, isLoggedIn: true, SecondContentId, StartUtc + TimeSpan.FromMilliseconds(250));
        guard.Update(enabled: true, isLoggedIn: true, ThirdContentId, StartUtc + TimeSpan.FromMilliseconds(500));

        accessor.ClearHistory();
        guard.Update(enabled: true, isLoggedIn: true, ThirdContentId, NextObservation(5));

        Assert.DoesNotContain(FirstContentId, accessor.ReadIds);
        Assert.DoesNotContain(FirstContentId, accessor.WriteIds);
        Assert.Equal(SecondContentId, guard.PreviousContentId);
        Assert.Equal(ThirdContentId, guard.SessionContentId);
    }

    [Fact]
    public void AccessorExceptionsDoNotEscapeOrAuthorizeWrites()
    {
        var (guard, accessor, _, warnings) = CreateGuard();
        accessor.ThrowOnRead = true;

        var exception = Record.Exception(() =>
            guard.Update(enabled: true, isLoggedIn: true, FirstContentId, StartUtc));

        Assert.Null(exception);
        Assert.Empty(accessor.WriteIds);
        Assert.Equal(AutoRetainerSelectionGuardState.Observing, guard.State);
        Assert.Contains(warnings, message => message.Contains("InvalidOperationException", StringComparison.Ordinal));
    }

    [Fact]
    public void GuardLifecycleNeverContributesToDadAutomationStatus()
    {
        var (guard, accessor, _, _) = CreateGuard();
        AssertIdleStatus();

        accessor.Selections[FirstContentId] = false;
        accessor.WriteResults.Enqueue(AutoRetainerSelectionWriteResult.Failed("retry"));
        accessor.WriteResults.Enqueue(AutoRetainerSelectionWriteResult.Verified(true));

        guard.Update(enabled: true, isLoggedIn: true, FirstContentId, StartUtc);
        Assert.Equal(AutoRetainerSelectionGuardState.Repairing, guard.State);
        AssertIdleStatus();

        guard.Update(enabled: true, isLoggedIn: true, FirstContentId, NextObservation(1));
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
        accessor.Selections[FirstContentId] = true;
        accessor.Selections[SecondContentId] = true;
        accessor.Selections[ThirdContentId] = true;
        var information = new List<string>();
        var warnings = new List<string>();
        var guard = new AutoRetainerSelectionGuard(accessor, information.Add, warnings.Add);
        return (guard, accessor, information, warnings);
    }

    private sealed class FakeSelectionAccessor : IAutoRetainerSelectionAccessor
    {
        public Dictionary<ulong, bool> Selections { get; } = [];
        public Queue<AutoRetainerSelectionReadResult> ReadResults { get; } = new();
        public Queue<AutoRetainerSelectionWriteResult> WriteResults { get; } = new();
        public Dictionary<ulong, Queue<AutoRetainerSelectionWriteResult>> WriteResultsByContentId { get; } = [];
        public List<ulong> ReadIds { get; } = [];
        public List<ulong> WriteIds { get; } = [];
        public bool ThrowOnRead { get; set; }
        public bool ThrowOnWrite { get; set; }

        public AutoRetainerSelectionReadResult ReadCharacterSelection(ulong contentId)
        {
            ReadIds.Add(contentId);
            if (ThrowOnRead)
                throw new InvalidOperationException("read exploded");
            return ReadResults.Count > 0
                ? ReadResults.Dequeue()
                : AutoRetainerSelectionReadResult.Known(Selections[contentId]);
        }

        public AutoRetainerSelectionWriteResult WriteCharacterSelection(
            ulong contentId,
            bool enabled)
        {
            WriteIds.Add(contentId);
            if (ThrowOnWrite)
                throw new InvalidOperationException("write exploded");

            if (WriteResultsByContentId.TryGetValue(contentId, out var contentResults) &&
                contentResults.Count > 0)
            {
                var queued = contentResults.Dequeue();
                Selections[contentId] = queued.Enabled;
                return queued;
            }

            if (WriteResults.Count > 0)
            {
                var queued = WriteResults.Dequeue();
                Selections[contentId] = queued.Enabled;
                return queued;
            }

            Selections[contentId] = enabled;
            return AutoRetainerSelectionWriteResult.Verified(enabled);
        }

        public void ClearHistory()
        {
            ReadIds.Clear();
            WriteIds.Clear();
        }
    }
}
