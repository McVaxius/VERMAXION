using System;
using System.Text.Json;
using VERMAXION.Models;
using VERMAXION.Services;
using Xunit;

namespace VERMAXION.Tests;

public sealed class ScheduledOfflineHoldCoordinatorTests
{
    private static readonly DateTimeOffset CompletedAt =
        new(2026, 8, 28, 12, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset CompletedRegistration =
        new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SettingDefaultsOffAndLegacyConfigurationHasNoHold()
    {
        var current = new Configuration();
        var legacy = JsonSerializer.Deserialize<Configuration>("{}")!;

        Assert.False(current.LogoutBetweenScheduledOceanFishingVoyages);
        Assert.Null(current.ScheduledOfflineHold);
        Assert.False(legacy.LogoutBetweenScheduledOceanFishingVoyages);
        Assert.Null(legacy.ScheduledOfflineHold);
    }

    [Fact]
    public void EligibilityRequiresEnabledAutomaticScheduledVoyage()
    {
        Assert.True(IsEligible(FishingStartupTrigger.WindowWatch));
        Assert.True(IsEligible(FishingStartupTrigger.AutoRetainerPostprocess));
        Assert.True(IsEligible(FishingStartupTrigger.ScheduledWake));

        Assert.False(IsEligible(FishingStartupTrigger.Manual));
        Assert.False(ScheduledOfflineHoldPolicy.IsEligible(
            true,
            true,
            FishingRunMode.Test,
            FishingStartupTrigger.Test));
        Assert.False(ScheduledOfflineHoldPolicy.IsEligible(
            false,
            true,
            FishingRunMode.Scheduled,
            FishingStartupTrigger.WindowWatch));
        Assert.False(ScheduledOfflineHoldPolicy.IsEligible(
            true,
            false,
            FishingRunMode.Scheduled,
            FishingStartupTrigger.WindowWatch));
    }

    [Fact]
    public void HoldSnapshotsTheNextFutureWindowAndConfiguredStartupGate()
    {
        var hold = ScheduledOfflineHoldPolicy.Create(
            CompletedAt,
            CompletedRegistration,
            preWindowOffsetMinutes: -1);

        Assert.Equal(new DateTimeOffset(2026, 8, 28, 14, 0, 0, TimeSpan.Zero), hold.NextRegistrationStartUtc);
        Assert.Equal(new DateTimeOffset(2026, 8, 28, 14, 15, 0, TimeSpan.Zero), hold.NextRegistrationEndUtc);
        Assert.Equal(new DateTimeOffset(2026, 8, 28, 13, 59, 0, TimeSpan.Zero), hold.StartupWindowStartUtc);
        Assert.Equal(hold.StartupWindowStartUtc, hold.WakeAtUtc);
        Assert.Equal(-1, hold.PreWindowOffsetMinutes);

        var nextHold = ScheduledOfflineHoldPolicy.Create(
            CompletedAt,
            CompletedRegistration,
            preWindowOffsetMinutes: -7);
        Assert.Equal(-1, hold.PreWindowOffsetMinutes);
        Assert.Equal(new DateTimeOffset(2026, 8, 28, 13, 53, 0, TimeSpan.Zero), nextHold.WakeAtUtc);

        var afterCurrentGate = ScheduledOfflineHoldPolicy.Create(
            new DateTimeOffset(2026, 8, 28, 14, 1, 0, TimeSpan.Zero),
            CompletedRegistration,
            preWindowOffsetMinutes: -1);
        Assert.Equal(new DateTimeOffset(2026, 8, 28, 16, 0, 0, TimeSpan.Zero), afterCurrentGate.NextRegistrationStartUtc);
        Assert.Equal(new DateTimeOffset(2026, 8, 28, 15, 59, 0, TimeSpan.Zero), afterCurrentGate.WakeAtUtc);
    }

    [Fact]
    public void PersistedOfflinePhaseRestoresAutoRetainerAndRequestsOneWake()
    {
        var runtime = RuntimeWithPersistedOfflineHold();
        var coordinator = new ScheduledOfflineHoldCoordinator(runtime);
        var wakeAt = runtime.PersistedHold!.WakeAtUtc;

        coordinator.Update(wakeAt);
        coordinator.Update(wakeAt.AddSeconds(1));
        coordinator.Update(wakeAt.AddSeconds(2));

        Assert.True(runtime.AutoRetainerMultiModeEnabled);
        Assert.False(runtime.PersistedHold!.AutoRetainerMultiModeRestoreRequired);
        Assert.Equal(ScheduledOfflineHoldPhase.Waking, runtime.PersistedHold.Phase);
        Assert.Equal(1, runtime.WakeRequests);
        Assert.True(coordinator.SuppressesOrdinaryAutomation);
    }

    [Fact]
    public void LogoutAttemptsUseFiveSecondCadenceAndFortyFiveSecondTimeout()
    {
        var hold = ScheduledOfflineHoldPolicy.Create(
            CompletedAt,
            CompletedRegistration,
            preWindowOffsetMinutes: -1);
        hold.LastLogoutAttemptUtc = CompletedAt;

        Assert.False(ScheduledOfflineHoldPolicy.ShouldAttemptLogout(
            hold,
            CompletedAt.AddSeconds(5).AddTicks(-1)));
        Assert.True(ScheduledOfflineHoldPolicy.ShouldAttemptLogout(
            hold,
            CompletedAt.AddSeconds(5)));
        Assert.False(ScheduledOfflineHoldPolicy.HasLogoutTimedOut(
            hold,
            CompletedAt.AddSeconds(45).AddTicks(-1)));
        Assert.True(ScheduledOfflineHoldPolicy.HasLogoutTimedOut(
            hold,
            CompletedAt.AddSeconds(45)));
    }

    [Fact]
    public void ScheduledWakeWaitsForWorldReadyThenUsesExistingStartupCoordinatorHandoff()
    {
        var runtime = RuntimeWithPersistedOfflineHold();
        var coordinator = new ScheduledOfflineHoldCoordinator(runtime);
        var wakeAt = runtime.PersistedHold!.WakeAtUtc;
        coordinator.Update(wakeAt);

        runtime.IsLoggedIn = true;
        coordinator.Update(wakeAt.AddSeconds(1));
        Assert.Equal(ScheduledOfflineHoldPhase.WaitingForWorldReady, runtime.PersistedHold!.Phase);

        runtime.WorldReady = true;
        runtime.StartupResult = StartedResult(runtime.PersistedHold.NextRegistrationStartUtc);
        coordinator.Update(wakeAt.AddSeconds(2));

        Assert.Equal(1, runtime.WakeRequests);
        Assert.Equal(1, runtime.StartupRequests);
        Assert.Equal(-1, runtime.LastStartupPreWindowOffsetMinutes);
        Assert.Equal(1, runtime.CompletedWakeHandoffs);
        Assert.Null(runtime.PersistedHold);
        Assert.False(coordinator.SuppressesOrdinaryAutomation);
    }

    [Fact]
    public void FeatureDisableCancelsOfflineWithoutRequestingLoginAndRestoresAutoRetainer()
    {
        var runtime = RuntimeWithPersistedOfflineHold();
        runtime.FeatureEnabled = false;
        var coordinator = new ScheduledOfflineHoldCoordinator(runtime);

        coordinator.Update(CompletedAt);

        Assert.True(runtime.AutoRetainerMultiModeEnabled);
        Assert.Equal(0, runtime.WakeRequests);
        Assert.Null(runtime.PersistedHold);
    }

    [Fact]
    public void FullStopCancellationDoesNotRequestLogin()
    {
        var runtime = RuntimeWithPersistedOfflineHold();
        var coordinator = new ScheduledOfflineHoldCoordinator(runtime);

        coordinator.Cancel("Full Stop", CompletedAt);

        Assert.True(runtime.AutoRetainerMultiModeEnabled);
        Assert.Equal(0, runtime.WakeRequests);
        Assert.Null(runtime.PersistedHold);
    }

    [Fact]
    public void LogoutTimeoutRestoresAutoRetainerClearsHoldAndLeavesLoginAlone()
    {
        var runtime = new FakeRuntime();
        var coordinator = new ScheduledOfflineHoldCoordinator(runtime);
        Assert.True(coordinator.BeginAfterSuccessfulRun(
            FishingRunMode.Scheduled,
            FishingStartupTrigger.WindowWatch,
            CompletedRegistration,
            CompletedAt,
            preWindowOffsetMinutes: -1));

        coordinator.Update(CompletedAt);
        Assert.False(runtime.AutoRetainerMultiModeEnabled);
        Assert.Equal(1, runtime.LogoutRequests);

        coordinator.Update(CompletedAt + ScheduledOfflineHoldPolicy.LogoutTimeout);

        Assert.True(runtime.IsLoggedIn);
        Assert.True(runtime.AutoRetainerMultiModeEnabled);
        Assert.Equal(0, runtime.WakeRequests);
        Assert.Null(runtime.PersistedHold);
    }

    private static bool IsEligible(FishingStartupTrigger trigger)
        => ScheduledOfflineHoldPolicy.IsEligible(
            featureEnabled: true,
            masterEnabled: true,
            FishingRunMode.Scheduled,
            trigger);

    private static FakeRuntime RuntimeWithPersistedOfflineHold()
    {
        var hold = ScheduledOfflineHoldPolicy.Create(
            CompletedAt,
            CompletedRegistration,
            preWindowOffsetMinutes: -1);
        hold.Phase = ScheduledOfflineHoldPhase.Offline;
        hold.InitialAutoRetainerMultiModeEnabled = true;
        hold.AutoRetainerMultiModeRestoreRequired = true;
        hold.LoggedOutAtUtc = CompletedAt;
        return new FakeRuntime
        {
            IsLoggedIn = false,
            AutoRetainerMultiModeEnabled = false,
            PersistedHold = hold,
        };
    }

    private static FishingStartupResult StartedResult(DateTimeOffset registrationStartUtc)
        => new(
            FishingStartupTrigger.ScheduledWake,
            FishingRunMode.Scheduled,
            FishingStartupAction.FishingStarted,
            WindowActive: true,
            registrationStartUtc,
            new FishingSelectionResult(
                "Synthetic Fisher@World",
                12,
                RequiresRelog: false,
                AlwaysFishKeysToDisable: Array.Empty<string>(),
                "Synthetic eligible candidate."),
            "Synthetic scheduled wake claimed the window.");

    private sealed class FakeRuntime : IScheduledOfflineHoldRuntime
    {
        public bool MasterEnabled { get; set; } = true;
        public bool FeatureEnabled { get; set; } = true;
        public bool IsLoggedIn { get; set; } = true;
        public ScheduledOfflineHoldState? PersistedHold { get; set; }
        public bool AutoRetainerMultiModeEnabled { get; set; } = true;
        public bool AutoRetainerReadable { get; set; } = true;
        public bool WakeReady { get; set; } = true;
        public bool WakeAccepted { get; set; } = true;
        public bool WorldReady { get; set; }
        public FishingStartupResult StartupResult { get; set; } = WaitingResult();
        public int LogoutRequests { get; private set; }
        public int WakeRequests { get; private set; }
        public int StartupRequests { get; private set; }
        public int? LastStartupPreWindowOffsetMinutes { get; private set; }
        public int CompletedWakeHandoffs { get; private set; }

        public void PersistHold(ScheduledOfflineHoldState? hold)
            => PersistedHold = hold;

        public AutoRetainerMultiModeReadResult ReadAutoRetainerMultiMode()
            => AutoRetainerReadable
                ? AutoRetainerMultiModeReadResult.Known(AutoRetainerMultiModeEnabled)
                : AutoRetainerMultiModeReadResult.Failed("Synthetic unreadable state.");

        public bool TrySetAutoRetainerMultiMode(bool enabled, out string error)
        {
            AutoRetainerMultiModeEnabled = enabled;
            error = string.Empty;
            return true;
        }

        public void SendLogoutCommand()
            => LogoutRequests++;

        public bool TryConfirmLogout()
            => true;

        public bool IsIntentionalFishingWakeReady(out string reason)
        {
            reason = WakeReady ? "ready" : "Synthetic character select is not ready.";
            return WakeReady;
        }

        public bool TryRequestIntentionalFishingWake(out string error)
        {
            WakeRequests++;
            error = WakeAccepted ? string.Empty : "Synthetic callback failure.";
            return WakeAccepted;
        }

        public bool IsScheduledWakeWorldReady(out string reason)
        {
            reason = WorldReady ? "ready" : "Synthetic world is not ready.";
            return WorldReady;
        }

        public FishingStartupResult RunScheduledWakeStartup(int preWindowOffsetMinutes)
        {
            StartupRequests++;
            LastStartupPreWindowOffsetMinutes = preWindowOffsetMinutes;
            return StartupResult;
        }

        public void CompleteIntentionalFishingWake()
            => CompletedWakeHandoffs++;

        private static FishingStartupResult WaitingResult()
            => new(
                FishingStartupTrigger.ScheduledWake,
                FishingRunMode.Scheduled,
                FishingStartupAction.Waiting,
                WindowActive: true,
                CompletedRegistration,
                FishingSelectionResult.None("Synthetic startup wait."),
                "Synthetic startup wait.");
    }
}
