#nullable enable

using System;
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
    public void RelogContinuationRemainsValidUntilRegistrationCloses()
    {
        var validRuntime = RuntimeForRelog();
        var validCoordinator = new FishingStartupCoordinator(validRuntime);
        validCoordinator.Poll(ActiveWindowTime, FishingStartupTrigger.Manual);
        validRuntime.IsRelogActive = false;

        var beforeClose = validCoordinator.ContinuePendingRelog(
            new DateTimeOffset(2026, 7, 2, 12, 14, 59, TimeSpan.Zero),
            "Low Fisher@World");

        Assert.Equal(FishingStartupAction.FishingStarted, beforeClose.Action);
        Assert.Equal(1, validRuntime.FishingStarts);

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
        Assert.Contains(logLines, line => line.Contains("/ays m d"));
        Assert.Contains(logLines, line => line.Contains("/ays relog Low Fisher@World"));
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

        public int SelectionRequests { get; private set; }
        public int RelogRequests { get; private set; }
        public int FishingStarts { get; private set; }
        public string LastRelogTarget { get; private set; } = string.Empty;

        public FishingSelectionResult SelectTarget()
        {
            SelectionRequests++;
            return Selection;
        }

        public int DisableAlwaysFishOnOtherCharacters(string selectedCharacterKey) => 0;

        public bool RequestRelog(string characterKey)
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
