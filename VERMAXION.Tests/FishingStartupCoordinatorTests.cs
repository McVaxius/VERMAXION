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
        new(2026, 7, 1, 0, 0, 30, TimeSpan.Zero);

    [Fact]
    public void ClockPollingInitiatesRelogWithoutArCallback()
    {
        var runtime = RuntimeForRelog();
        var coordinator = new FishingStartupCoordinator(runtime);

        var result = coordinator.Poll(ActiveWindowTime, FishingStartupTrigger.Clock);

        Assert.Equal(FishingStartupAction.RelogStarted, result.Action);
        Assert.Equal(1, runtime.RelogRequests);
        Assert.Equal("Low Fisher@World", runtime.LastRelogTarget);
    }

    [Fact]
    public void RepeatedPollingSendsOneRelogPerRegistrationWindow()
    {
        var runtime = RuntimeForRelog();
        var coordinator = new FishingStartupCoordinator(runtime);

        var first = coordinator.Poll(ActiveWindowTime, FishingStartupTrigger.Clock);
        runtime.IsRelogActive = false;
        var second = coordinator.Poll(ActiveWindowTime.AddSeconds(10), FishingStartupTrigger.Clock);

        Assert.Equal(FishingStartupAction.RelogStarted, first.Action);
        Assert.Equal(FishingStartupAction.AlreadyHandled, second.Action);
        Assert.Equal(1, runtime.RelogRequests);
    }

    [Fact]
    public void NewRegistrationWindowAllowsNewRelogAttempt()
    {
        var runtime = RuntimeForRelog();
        var coordinator = new FishingStartupCoordinator(runtime);

        coordinator.Poll(ActiveWindowTime, FishingStartupTrigger.Clock);
        runtime.IsRelogActive = false;
        var nextWindow = coordinator.Poll(ActiveWindowTime.AddHours(2), FishingStartupTrigger.Clock);

        Assert.Equal(FishingStartupAction.RelogStarted, nextWindow.Action);
        Assert.Equal(2, runtime.RelogRequests);
    }

    [Fact]
    public void ReloggedCurrentCharacterStartsFishingOnceInSameWindow()
    {
        var runtime = RuntimeForRelog();
        var coordinator = new FishingStartupCoordinator(runtime);

        coordinator.Poll(ActiveWindowTime, FishingStartupTrigger.Clock);

        runtime.IsRelogActive = false;
        runtime.Selection = CurrentSelection();
        var afterRelog = coordinator.Poll(ActiveWindowTime.AddSeconds(10), FishingStartupTrigger.Clock);
        var repeated = coordinator.Poll(ActiveWindowTime.AddSeconds(20), FishingStartupTrigger.Clock);

        Assert.Equal(FishingStartupAction.FishingStarted, afterRelog.Action);
        Assert.Equal(FishingStartupAction.AlreadyHandled, repeated.Action);
        Assert.Equal(1, runtime.FishingStarts);
        Assert.Equal(1, runtime.RelogRequests);
    }

    [Fact]
    public void ManualTriggerUsesSameSelectionAndRelogPath()
    {
        var runtime = RuntimeForRelog();
        var coordinator = new FishingStartupCoordinator(runtime);

        var result = coordinator.Poll(ActiveWindowTime, FishingStartupTrigger.Manual);

        Assert.Equal(FishingStartupTrigger.Manual, result.Trigger);
        Assert.Equal(FishingStartupAction.RelogStarted, result.Action);
        Assert.Equal("Low Fisher@World", result.Selection.CharacterKey);
        Assert.Equal(1, runtime.RelogRequests);
    }

    [Fact]
    public void SimulatedValidWindowLogsSelectionAndRequiredRelogCommands()
    {
        var runtime = RuntimeForRelog();
        var coordinator = new FishingStartupCoordinator(runtime);

        var result = coordinator.Poll(ActiveWindowTime, FishingStartupTrigger.Clock);
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
        Assert.Contains(logLines, line => line.Contains("/ays reset"));
        Assert.Contains(logLines, line => line.Contains("/ays relog Low Fisher@World"));
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
    public void FullStopSuppressionPreventsRestartInCurrentWindow()
    {
        var runtime = RuntimeForRelog();
        var coordinator = new FishingStartupCoordinator(runtime);

        coordinator.SuppressCurrentWindow(ActiveWindowTime);
        var result = coordinator.Poll(ActiveWindowTime.AddSeconds(10), FishingStartupTrigger.Clock);

        Assert.Equal(FishingStartupAction.AlreadyHandled, result.Action);
        Assert.Equal(0, runtime.RelogRequests);
        Assert.Equal(0, runtime.FishingStarts);
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

        public int RelogRequests { get; private set; }
        public int FishingStarts { get; private set; }
        public string LastRelogTarget { get; private set; } = string.Empty;

        public FishingSelectionResult SelectTarget() => Selection;

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
