#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using VERMAXION.Models;
using VERMAXION.Services;
using Xunit;

namespace VERMAXION.Tests;

public sealed class FishingStartupCoordinatorTests
{
    private static readonly DateTimeOffset ActiveWindowTime =
        new(2026, 7, 2, 12, 7, 42, TimeSpan.Zero);

    [Fact]
    public void AmbientClockTriggerDoesNotInitiateRelogOrFishing()
    {
        var runtime = RuntimeForRelog();
        var coordinator = new FishingStartupCoordinator(runtime);

        var result = coordinator.Poll(ActiveWindowTime, FishingStartupTrigger.Clock);

        Assert.Equal(FishingStartupAction.None, result.Action);
        Assert.False(result.ClaimsStartup);
        Assert.Equal(0, runtime.SelectionRequests);
        Assert.Equal(0, runtime.RelogRequests);
        Assert.Equal(0, runtime.FishingStarts);
    }

    [Fact]
    public void EmptyCandidateQueueIsBuiltOnlyOncePerWindow()
    {
        var runtime = RuntimeForRelog();
        runtime.Selection = FishingSelectionResult.None("unknown levels");
        var coordinator = new FishingStartupCoordinator(runtime);

        coordinator.Poll(ActiveWindowTime, FishingStartupTrigger.Manual);
        coordinator.Poll(ActiveWindowTime.AddSeconds(10), FishingStartupTrigger.AutoRetainerPostprocess);

        Assert.Equal(1, runtime.SelectionRequests);
    }

    [Fact]
    public void AutoRetainerPostprocessCanInitiateRelog()
    {
        var runtime = RuntimeForRelog();
        var coordinator = new FishingStartupCoordinator(runtime);

        var result = coordinator.Poll(ActiveWindowTime, FishingStartupTrigger.AutoRetainerPostprocess);

        Assert.Equal(FishingStartupAction.RelogStarted, result.Action);
        Assert.True(coordinator.HasPendingRelogContinuation);
        Assert.Equal(1, runtime.RelogRequests);
        Assert.Equal("Low Fisher@World", runtime.LastRelogTarget);
    }

    [Fact]
    public void ManualTriggerCanInitiateRelog()
    {
        var runtime = RuntimeForRelog();
        var coordinator = new FishingStartupCoordinator(runtime);

        var result = coordinator.Poll(ActiveWindowTime, FishingStartupTrigger.Manual);

        Assert.Equal(FishingStartupTrigger.Manual, result.Trigger);
        Assert.Equal(FishingStartupAction.RelogStarted, result.Action);
        Assert.True(coordinator.HasPendingRelogContinuation);
        Assert.Equal("Low Fisher@World", result.Selection.CharacterKey);
        Assert.Equal(1, runtime.RelogRequests);
    }

    [Fact]
    public void ManualGateClearPreservesExistingPendingRelogContinuation()
    {
        var runtime = RuntimeForRelog();
        var coordinator = new FishingStartupCoordinator(runtime);

        coordinator.Poll(ActiveWindowTime, FishingStartupTrigger.Manual);
        coordinator.ResetCurrentWindow(ActiveWindowTime.AddSeconds(5), clearPendingRelogContinuation: false);
        var secondManualClick = coordinator.Poll(ActiveWindowTime.AddSeconds(5), FishingStartupTrigger.Manual);

        Assert.Equal(FishingStartupAction.Waiting, secondManualClick.Action);
        Assert.True(coordinator.HasPendingRelogContinuation);
        Assert.Equal(1, runtime.RelogRequests);

        runtime.IsRelogActive = false;
        var continuation = coordinator.ContinuePendingRelog(ActiveWindowTime.AddSeconds(10), "Low Fisher@World");

        Assert.Equal(FishingStartupAction.FishingStarted, continuation.Action);
        Assert.Equal(1, runtime.FishingStarts);
    }

    [Fact]
    public void RelogContinuationKeepsCachedXadbSelectionUntilNextStartup()
    {
        var runtime = RuntimeForRelog();
        var coordinator = new FishingStartupCoordinator(runtime);

        var initial = coordinator.Poll(ActiveWindowTime, FishingStartupTrigger.Manual);
        runtime.CandidateQueue =
        [
            new(
                "Different Fisher@World",
                FisherLevel: 1,
                RequiresRelog: true,
                AlwaysFishKeysToDisable: Array.Empty<string>(),
                Reason: "Refreshed XADB order."),
        ];
        runtime.IsRelogActive = false;

        var continuation = coordinator.ContinuePendingRelog(
            ActiveWindowTime.AddSeconds(10),
            "Low Fisher@World");

        Assert.Equal("Low Fisher@World", initial.Selection.CharacterKey);
        Assert.Equal(12, continuation.Selection.FisherLevel);
        Assert.Equal("Low Fisher@World", continuation.Selection.CharacterKey);
        Assert.Equal(1, runtime.SelectionRequests);
    }

    [Fact]
    public void RepeatedExplicitPollingSendsOneRelogPerRegistrationWindow()
    {
        var runtime = RuntimeForRelog();
        var coordinator = new FishingStartupCoordinator(runtime);

        var first = coordinator.Poll(ActiveWindowTime, FishingStartupTrigger.AutoRetainerPostprocess);
        runtime.IsRelogActive = false;
        var second = coordinator.Poll(ActiveWindowTime.AddSeconds(10), FishingStartupTrigger.AutoRetainerPostprocess);

        Assert.Equal(FishingStartupAction.RelogStarted, first.Action);
        Assert.Equal(FishingStartupAction.AlreadyHandled, second.Action);
        Assert.Equal(1, runtime.RelogRequests);
    }

    [Fact]
    public void NewRegistrationWindowAllowsNewExplicitRelogAttempt()
    {
        var runtime = RuntimeForRelog();
        var coordinator = new FishingStartupCoordinator(runtime);

        coordinator.Poll(ActiveWindowTime, FishingStartupTrigger.AutoRetainerPostprocess);
        runtime.IsRelogActive = false;
        var nextWindow = coordinator.Poll(ActiveWindowTime.AddHours(2), FishingStartupTrigger.AutoRetainerPostprocess);

        Assert.Equal(FishingStartupAction.RelogStarted, nextWindow.Action);
        Assert.Equal(2, runtime.RelogRequests);
    }

    [Fact]
    public void RelogContinuationRequiresExplicitPendingRelog()
    {
        var runtime = RuntimeForRelog();
        var coordinator = new FishingStartupCoordinator(runtime);

        runtime.Selection = CurrentSelection();
        var withoutPending = coordinator.ContinuePendingRelog(ActiveWindowTime, "Low Fisher@World");

        Assert.Equal(FishingStartupAction.None, withoutPending.Action);
        Assert.Equal(0, runtime.FishingStarts);

        runtime.Selection = RuntimeForRelog().Selection;
        coordinator.Poll(ActiveWindowTime, FishingStartupTrigger.Manual);
        runtime.IsRelogActive = false;

        var afterRelog = coordinator.ContinuePendingRelog(ActiveWindowTime.AddSeconds(10), "Low Fisher@World");
        var repeated = coordinator.ContinuePendingRelog(ActiveWindowTime.AddSeconds(20), "Low Fisher@World");

        Assert.Equal(FishingStartupAction.FishingStarted, afterRelog.Action);
        Assert.Equal(FishingStartupAction.None, repeated.Action);
        Assert.False(coordinator.HasPendingRelogContinuation);
        Assert.Equal(1, runtime.FishingStarts);
        Assert.Equal(1, runtime.RelogRequests);
    }

    [Fact]
    public void RelogContinuationExpiresOutsideOriginalWindow()
    {
        var runtime = RuntimeForRelog();
        var coordinator = new FishingStartupCoordinator(runtime);

        coordinator.Poll(ActiveWindowTime, FishingStartupTrigger.Manual);
        runtime.IsRelogActive = false;

        var nextWindow = coordinator.ContinuePendingRelog(ActiveWindowTime.AddHours(2), "Low Fisher@World");

        Assert.Equal(FishingStartupAction.None, nextWindow.Action);
        Assert.False(coordinator.HasPendingRelogContinuation);
        Assert.Equal(0, runtime.FishingStarts);
    }

    [Fact]
    public void RelogContinuationRequiresAtLeastSixtySecondsRemaining()
    {
        var validRuntime = RuntimeForRelog();
        var validCoordinator = new FishingStartupCoordinator(validRuntime);
        validCoordinator.Poll(ActiveWindowTime, FishingStartupTrigger.Manual);
        validRuntime.IsRelogActive = false;

        var beforeClose = validCoordinator.ContinuePendingRelog(
            new DateTimeOffset(2026, 7, 2, 12, 14, 59, TimeSpan.Zero),
            "Low Fisher@World");

        Assert.Equal(FishingStartupAction.AlreadyHandled, beforeClose.Action);
        Assert.Equal(0, validRuntime.FishingStarts);

        var expiredRuntime = RuntimeForRelog();
        var expiredCoordinator = new FishingStartupCoordinator(expiredRuntime);
        expiredCoordinator.Poll(ActiveWindowTime, FishingStartupTrigger.Manual);
        expiredRuntime.IsRelogActive = false;

        var atClose = expiredCoordinator.ContinuePendingRelog(
            new DateTimeOffset(2026, 7, 2, 12, 15, 0, TimeSpan.Zero),
            "Low Fisher@World");

        Assert.Equal(FishingStartupAction.None, atClose.Action);
        Assert.False(expiredCoordinator.HasPendingRelogContinuation);
        Assert.Equal(0, expiredRuntime.FishingStarts);
    }

    [Fact]
    public void SimulatedValidWindowLogsSelectionAndRequiredRelogCommands()
    {
        var runtime = RuntimeForRelog();
        var coordinator = new FishingStartupCoordinator(runtime);

        var result = coordinator.Poll(ActiveWindowTime, FishingStartupTrigger.AutoRetainerPostprocess);
        var logLines = new[]
        {
            FishingStartupDiagnostics.FormatStarted(result),
        }.Concat(
            FishingRelogPrepPolicy.BuildReleaseSequence(result.Selection.CharacterKey)
                .Where(step => step.Action == FishingRelogPrepAction.SendCommand)
                .Select(FishingRelogDiagnostics.FormatCommand))
            .ToArray();

        Assert.Contains(logLines, line => line.Contains("target=Low Fisher@World"));
        Assert.Contains(logLines, line => line.Contains("/ays relog Low Fisher@World"));
        Assert.DoesNotContain(logLines, line => line.Contains("/ays m d"));
        Assert.DoesNotContain(logLines, line => line.Contains("/ays reset"));
    }

    [Fact]
    public void ManualTriggerOutsideGateDoesNotPrepOrRelogAndReportsTiming()
    {
        var runtime = RuntimeForRelog();
        var coordinator = new FishingStartupCoordinator(runtime);

        var result = coordinator.Poll(
            new DateTimeOffset(2026, 7, 1, 1, 0, 0, TimeSpan.Zero),
            FishingStartupTrigger.Manual);

        Assert.False(result.WindowActive);
        Assert.False(result.ClaimsStartup);
        Assert.Equal(FishingStartupAction.None, result.Action);
        Assert.Equal(0, runtime.RelogRequests);
        Assert.Equal(0, runtime.FishingStarts);
        Assert.Contains("No Ocean Fishing startup gate is active", result.Reason);
        Assert.Contains("next gate", result.Reason);
    }

    [Fact]
    public void DiagnosticsAfterRegistrationClosesReportNextFullGate()
    {
        var runtime = RuntimeForRelog();
        var coordinator = new FishingStartupCoordinator(runtime);

        var result = coordinator.Poll(
            new DateTimeOffset(2026, 7, 2, 12, 15, 0, TimeSpan.Zero),
            FishingStartupTrigger.Manual);

        Assert.False(result.WindowActive);
        Assert.Contains("next gate is 2026-07-02 13:59:00Z until 2026-07-02 14:15:00Z (end exclusive)", result.Reason);
    }

    [Fact]
    public void FullStopSuppressionPreventsAutomaticRetryAndResetAllowsManualRetry()
    {
        var runtime = RuntimeForRelog();
        var coordinator = new FishingStartupCoordinator(runtime);

        coordinator.SuppressCurrentWindow(ActiveWindowTime);
        var suppressed = coordinator.Poll(ActiveWindowTime.AddSeconds(10), FishingStartupTrigger.AutoRetainerPostprocess);

        Assert.Equal(FishingStartupAction.AlreadyHandled, suppressed.Action);
        Assert.Equal(0, runtime.RelogRequests);
        Assert.Equal(0, runtime.FishingStarts);

        coordinator.ResetCurrentWindow(ActiveWindowTime.AddSeconds(20));
        var manualRetry = coordinator.Poll(ActiveWindowTime.AddSeconds(20), FishingStartupTrigger.Manual);

        Assert.Equal(FishingStartupAction.RelogStarted, manualRetry.Action);
        Assert.Equal(1, runtime.RelogRequests);
    }

    [Fact]
    public void TestModeIgnoresSchedulingGateCapturesNextRegistrationAndDoesNotMutateAlwaysFish()
    {
        var runtime = RuntimeForRelog();
        var coordinator = new FishingStartupCoordinator(runtime);
        var startedAt = new DateTimeOffset(2026, 7, 2, 1, 20, 0, TimeSpan.Zero);

        var result = coordinator.StartTest(startedAt);

        Assert.Equal(FishingRunMode.Test, result.Mode);
        Assert.Equal(FishingStartupAction.RelogStarted, result.Action);
        Assert.Equal(new DateTimeOffset(2026, 7, 2, 2, 0, 0, TimeSpan.Zero), result.RegistrationStartUtc);
        Assert.Equal(FishingRunMode.Test, runtime.LastRunMode);
        Assert.Equal(new DateTimeOffset(2026, 7, 2, 2, 15, 0, TimeSpan.Zero), runtime.LastRegistrationDeadline);
        Assert.Equal(0, runtime.DisableAlwaysFishRequests);
    }

    [Fact]
    public void StartupReportsXadbFailureInsteadOfGenericEmptyCandidateReason()
    {
        const string xadbFailure =
            "XADB 0.0.0.39+ contract v6 roster IPC is required via XA.Database.GetAccountCharacterListJson; status=UnsupportedContract, detail=contract v5 is unsupported.";
        var manualRuntime = RuntimeForRelog();
        manualRuntime.CandidateQueue = [FishingSelectionResult.None(xadbFailure)];
        var manualCoordinator = new FishingStartupCoordinator(manualRuntime);
        var testRuntime = RuntimeForRelog();
        testRuntime.CandidateQueue = [FishingSelectionResult.None(xadbFailure)];
        var testCoordinator = new FishingStartupCoordinator(testRuntime);

        var manual = manualCoordinator.Poll(ActiveWindowTime, FishingStartupTrigger.Manual);
        var test = testCoordinator.StartTest(new DateTimeOffset(2026, 7, 2, 1, 20, 0, TimeSpan.Zero));

        Assert.Equal(FishingRunMode.Scheduled, manual.Mode);
        Assert.Equal(FishingStartupAction.None, manual.Action);
        Assert.False(manual.Selection.Selected);
        Assert.Equal(xadbFailure, manual.Reason);
        Assert.DoesNotContain("No eligible Ocean Fishing candidates remain", manual.Reason);
        Assert.Equal(0, manualRuntime.RelogRequests);
        Assert.Equal(0, manualRuntime.FishingStarts);

        Assert.Equal(FishingRunMode.Test, test.Mode);
        Assert.Equal(FishingStartupAction.None, test.Action);
        Assert.False(test.Selection.Selected);
        Assert.Equal(xadbFailure, test.Reason);
        Assert.DoesNotContain("No eligible Ocean Fishing candidates remain", test.Reason);
        Assert.Equal(0, testRuntime.RelogRequests);
        Assert.Equal(0, testRuntime.FishingStarts);
    }

    [Fact]
    public void TestRelogContinuationCanStartTargetPrepBeforeRegistrationOpens()
    {
        var runtime = RuntimeForRelog();
        var coordinator = new FishingStartupCoordinator(runtime);
        var startedAt = new DateTimeOffset(2026, 7, 2, 1, 20, 0, TimeSpan.Zero);

        coordinator.StartTest(startedAt);
        runtime.IsRelogActive = false;
        var continuation = coordinator.ContinuePendingRelog(startedAt.AddMinutes(5), "Low Fisher@World");

        Assert.Equal(FishingRunMode.Test, continuation.Mode);
        Assert.Equal(FishingStartupAction.FishingStarted, continuation.Action);
        Assert.Equal(1, runtime.FishingStarts);
    }

    [Fact]
    public void TestModeExpiryAbortsWithoutConsumingScheduledAttemptGuard()
    {
        var runtime = RuntimeForRelog();
        var coordinator = new FishingStartupCoordinator(runtime);
        var testStart = new DateTimeOffset(2026, 7, 2, 1, 20, 0, TimeSpan.Zero);

        coordinator.StartTest(testStart);
        runtime.IsRelogActive = false;
        var expired = coordinator.ContinuePendingRelog(
            new DateTimeOffset(2026, 7, 2, 2, 15, 0, TimeSpan.Zero),
            "Intermediate@World");
        var scheduled = coordinator.Poll(ActiveWindowTime, FishingStartupTrigger.Manual);

        Assert.Equal(FishingStartupAction.None, expired.Action);
        Assert.Equal(1, runtime.AbortRequests);
        Assert.Equal(FishingStartupAction.RelogStarted, scheduled.Action);
        Assert.Equal(2, runtime.RelogRequests);
        Assert.Equal(0, runtime.DisableAlwaysFishRequests);
    }

    [Fact]
    public void TargetOnlyStartupBeginsOwnedScheduledRunWithoutRelog()
    {
        var runtime = RuntimeForRelog();
        runtime.Selection = CurrentSelection();
        var coordinator = new FishingStartupCoordinator(runtime);

        var result = coordinator.Poll(ActiveWindowTime, FishingStartupTrigger.Manual);

        Assert.Equal(FishingStartupAction.FishingStarted, result.Action);
        Assert.Equal(FishingRunMode.Scheduled, runtime.LastRunMode);
        Assert.Equal(0, runtime.RelogRequests);
        Assert.Equal(1, runtime.FishingStarts);
    }

    [Fact]
    public void PreQueueFishingFailureCanRetrySameRegistrationWindow()
    {
        var runtime = RuntimeForRelog();
        runtime.Selection = CurrentSelection();
        var coordinator = new FishingStartupCoordinator(runtime);

        var first = coordinator.Poll(ActiveWindowTime, FishingStartupTrigger.AutoRetainerPostprocess);
        runtime.IsFishingActive = false;
        runtime.ActiveRunRegistrationStartUtc = null;

        var retry = coordinator.Poll(ActiveWindowTime.AddSeconds(10), FishingStartupTrigger.AutoRetainerPostprocess);

        Assert.Equal(FishingStartupAction.FishingStarted, first.Action);
        Assert.Equal(FishingStartupAction.FishingStarted, retry.Action);
        Assert.Equal(2, runtime.FishingStarts);
    }

    [Fact]
    public void ConfirmedQueueRegistrationHandlesWindowAfterRunStops()
    {
        var runtime = RuntimeForRelog();
        runtime.Selection = CurrentSelection();
        var coordinator = new FishingStartupCoordinator(runtime);

        var first = coordinator.Poll(ActiveWindowTime, FishingStartupTrigger.AutoRetainerPostprocess);
        runtime.IsFishingActive = false;
        runtime.ActiveRunRegistrationStartUtc = null;
        runtime.QueueConfirmedRegistrationStartUtc = first.RegistrationStartUtc;

        var repeated = coordinator.Poll(ActiveWindowTime.AddSeconds(10), FishingStartupTrigger.AutoRetainerPostprocess);

        Assert.Equal(FishingStartupAction.FishingStarted, first.Action);
        Assert.Equal(FishingStartupAction.AlreadyHandled, repeated.Action);
        Assert.Equal(1, runtime.FishingStarts);
    }

    [Fact]
    public void TerminalPreQueueFailureHandlesWindow()
    {
        var runtime = RuntimeForRelog();
        runtime.Selection = CurrentSelection();
        var coordinator = new FishingStartupCoordinator(runtime);

        var first = coordinator.Poll(ActiveWindowTime, FishingStartupTrigger.AutoRetainerPostprocess);
        runtime.IsFishingActive = false;
        runtime.ActiveRunRegistrationStartUtc = null;
        runtime.TerminalFailureRegistrationStartUtc = first.RegistrationStartUtc;

        var repeated = coordinator.Poll(ActiveWindowTime.AddSeconds(10), FishingStartupTrigger.AutoRetainerPostprocess);

        Assert.Equal(FishingStartupAction.FishingStarted, first.Action);
        Assert.Equal(FishingStartupAction.AlreadyHandled, repeated.Action);
        Assert.Equal(1, runtime.FishingStarts);
    }

    [Fact]
    public void CharacterPermanentFailureImmediatelyAdvancesToNextCachedCandidate()
    {
        var runtime = RuntimeForRelog();
        runtime.CandidateQueue =
        [
            CurrentSelection(),
            new FishingSelectionResult(
                "Fallback@World",
                20,
                true,
                Array.Empty<string>(),
                "fallback"),
        ];
        var coordinator = new FishingStartupCoordinator(runtime);
        var first = coordinator.Poll(ActiveWindowTime, FishingStartupTrigger.Manual);
        runtime.IsFishingActive = false;

        coordinator.ReportAttemptFailure(
            ActiveWindowTime.AddSeconds(1),
            FishingAttemptFailureKind.CharacterPermanent,
            "missing unlock",
            queueConfirmed: false);
        var recovery = coordinator.PollRecovery(
            ActiveWindowTime.AddSeconds(1),
            "Low Fisher@World");

        Assert.Equal(FishingStartupAction.FishingStarted, first.Action);
        Assert.Equal(FishingStartupAction.RelogStarted, recovery.Action);
        Assert.Equal("Fallback@World", runtime.LastRelogTarget);
        Assert.Equal(1, runtime.SelectionRequests);
    }

    [Fact]
    public void SharedTransientFailureRetriesSameCharacterExactlyTwice()
    {
        var runtime = RuntimeForRelog();
        runtime.CandidateQueue = [CurrentSelection()];
        var coordinator = new FishingStartupCoordinator(runtime);
        coordinator.Poll(ActiveWindowTime, FishingStartupTrigger.Manual);
        runtime.IsFishingActive = false;

        coordinator.ReportAttemptFailure(
            ActiveWindowTime,
            FishingAttemptFailureKind.SharedTransient,
            "ADS unavailable",
            queueConfirmed: false);
        Assert.Equal(
            FishingStartupAction.Waiting,
            coordinator.PollRecovery(ActiveWindowTime.AddSeconds(2), "Low Fisher@World").Action);
        Assert.Equal(
            FishingStartupAction.FishingStarted,
            coordinator.PollRecovery(ActiveWindowTime.AddSeconds(3), "Low Fisher@World").Action);

        runtime.IsFishingActive = false;
        coordinator.ReportAttemptFailure(
            ActiveWindowTime.AddSeconds(4),
            FishingAttemptFailureKind.SharedTransient,
            "ADS unavailable",
            queueConfirmed: false);
        Assert.Equal(
            FishingStartupAction.Waiting,
            coordinator.PollRecovery(ActiveWindowTime.AddSeconds(13), "Low Fisher@World").Action);
        Assert.Equal(
            FishingStartupAction.FishingStarted,
            coordinator.PollRecovery(ActiveWindowTime.AddSeconds(14), "Low Fisher@World").Action);

        runtime.IsFishingActive = false;
        coordinator.ReportAttemptFailure(
            ActiveWindowTime.AddSeconds(15),
            FishingAttemptFailureKind.SharedTransient,
            "ADS unavailable",
            queueConfirmed: false);

        Assert.False(coordinator.HasRecoveryPending);
        Assert.Equal(3, runtime.FishingStarts);
        Assert.Equal(
            FishingStartupAction.AlreadyHandled,
            coordinator.Poll(
                ActiveWindowTime.AddSeconds(16),
                FishingStartupTrigger.AutoRetainerPostprocess).Action);
    }

    [Fact]
    public void QueueConfirmationAndRegistrationExpiryPreventRecovery()
    {
        var runtime = RuntimeForRelog();
        runtime.CandidateQueue = [CurrentSelection()];
        var coordinator = new FishingStartupCoordinator(runtime);
        coordinator.Poll(ActiveWindowTime, FishingStartupTrigger.Manual);
        runtime.IsFishingActive = false;

        coordinator.ReportAttemptFailure(
            ActiveWindowTime.AddSeconds(1),
            FishingAttemptFailureKind.CharacterPermanent,
            "post queue",
            queueConfirmed: true);
        Assert.False(coordinator.HasRecoveryPending);

        var expiredRuntime = RuntimeForRelog();
        expiredRuntime.CandidateQueue = [CurrentSelection()];
        var expiredCoordinator = new FishingStartupCoordinator(expiredRuntime);
        expiredCoordinator.Poll(ActiveWindowTime, FishingStartupTrigger.Manual);
        expiredRuntime.IsFishingActive = false;
        expiredCoordinator.ReportAttemptFailure(
            new DateTimeOffset(2026, 7, 2, 12, 15, 0, TimeSpan.Zero),
            FishingAttemptFailureKind.CharacterPermanent,
            "registration closed",
            queueConfirmed: false);
        Assert.False(expiredCoordinator.HasRecoveryPending);
    }

    private static FakeFishingStartupRuntime RuntimeForRelog()
        => new()
        {
            Selection = new FishingSelectionResult(
                "Low Fisher@World",
                FisherLevel: 12,
                RequiresRelog: true,
                AlwaysFishKeysToDisable: Array.Empty<string>(),
                Reason: "Selected lowest Fisher below max."),
        };

    private static FishingSelectionResult CurrentSelection()
        => new(
            "Low Fisher@World",
            FisherLevel: 12,
            RequiresRelog: false,
            AlwaysFishKeysToDisable: Array.Empty<string>(),
            Reason: "Selected lowest Fisher below max.");

    private sealed class FakeFishingStartupRuntime : IFishingStartupRuntime
    {
        public int PreWindowOffsetMinutes { get; set; } = -1;
        public bool CanInitiateStartup { get; set; } = true;
        public bool IsFishingActive { get; set; }
        public bool IsRelogActive { get; set; }
        public FishingSelectionResult Selection { get; set; } =
            FishingSelectionResult.None("No target.");
        public IReadOnlyList<FishingSelectionResult>? CandidateQueue { get; set; }

        public int SelectionRequests { get; private set; }
        public int RelogRequests { get; private set; }
        public int FishingStarts { get; private set; }
        public int DisableAlwaysFishRequests { get; private set; }
        public int AbortRequests { get; private set; }
        public string LastRelogTarget { get; private set; } = string.Empty;
        public FishingRunMode LastRunMode { get; private set; }
        public DateTimeOffset LastRegistrationDeadline { get; private set; }
        public DateTimeOffset? ActiveRunRegistrationStartUtc { get; set; }
        public DateTimeOffset? QueueConfirmedRegistrationStartUtc { get; set; }
        public DateTimeOffset? TerminalFailureRegistrationStartUtc { get; set; }

        public IReadOnlyList<FishingSelectionResult> BuildCandidateQueue()
        {
            SelectionRequests++;
            return CandidateQueue ??
                   (Selection.Selected ? [Selection] : Array.Empty<FishingSelectionResult>());
        }

        public int DisableAlwaysFishOnOtherCharacters(string selectedCharacterKey)
        {
            DisableAlwaysFishRequests++;
            return 0;
        }

        public bool IsFishingRunActiveForWindow(DateTimeOffset registrationStartUtc)
            => ActiveRunRegistrationStartUtc == registrationStartUtc.ToUniversalTime();

        public bool IsQueueRegistrationConfirmedForWindow(DateTimeOffset registrationStartUtc)
            => QueueConfirmedRegistrationStartUtc == registrationStartUtc.ToUniversalTime();

        public bool IsTerminalFailureBeforeQueueConfirmationForWindow(DateTimeOffset registrationStartUtc)
            => TerminalFailureRegistrationStartUtc == registrationStartUtc.ToUniversalTime();

        public void ClearFishingWindowOutcome(DateTimeOffset registrationStartUtc)
        {
            var normalized = registrationStartUtc.ToUniversalTime();
            if (QueueConfirmedRegistrationStartUtc == normalized)
                QueueConfirmedRegistrationStartUtc = null;
            if (TerminalFailureRegistrationStartUtc == normalized)
                TerminalFailureRegistrationStartUtc = null;
        }

        public bool BeginRun(FishingRunMode mode, string targetCharacterKey, DateTimeOffset registrationStartUtc, DateTimeOffset registrationDeadlineUtc)
        {
            LastRunMode = mode;
            LastRegistrationDeadline = registrationDeadlineUtc;
            ActiveRunRegistrationStartUtc = registrationStartUtc.ToUniversalTime();
            return true;
        }

        public void AbortRun(string reason)
        {
            AbortRequests++;
            ActiveRunRegistrationStartUtc = null;
        }

        public bool RequestRelog(string characterKey, DateTimeOffset registrationDeadlineUtc)
        {
            RelogRequests++;
            LastRelogTarget = characterKey;
            IsRelogActive = true;
            return true;
        }

        public bool StartFishing()
        {
            FishingStarts++;
            IsFishingActive = true;
            return true;
        }
    }
}
