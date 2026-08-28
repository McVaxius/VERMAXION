using System;
using VERMAXION.Models;

namespace VERMAXION.Services;

public interface IScheduledOfflineHoldRuntime
{
    bool MasterEnabled { get; }
    bool FeatureEnabled { get; }
    bool IsLoggedIn { get; }
    ScheduledOfflineHoldState? PersistedHold { get; }

    void PersistHold(ScheduledOfflineHoldState? hold);
    AutoRetainerMultiModeReadResult ReadAutoRetainerMultiMode();
    bool TrySetAutoRetainerMultiMode(bool enabled, out string error);
    void SendLogoutCommand();
    bool TryConfirmLogout();
    bool IsIntentionalFishingWakeReady(out string reason);
    bool TryRequestIntentionalFishingWake(out string error);
    bool IsScheduledWakeWorldReady(out string reason);
    FishingStartupResult RunScheduledWakeStartup(int preWindowOffsetMinutes);
    void CompleteIntentionalFishingWake();
}

public sealed class ScheduledOfflineHoldCoordinator
{
    private static readonly TimeSpan StartupPollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RestoreRetryInterval = TimeSpan.FromSeconds(5);

    private readonly IScheduledOfflineHoldRuntime runtime;
    private readonly Action<string> information;
    private readonly Action<string> warning;
    private DateTimeOffset nextStartupPollUtc;
    private DateTimeOffset nextRestoreAttemptUtc;
    private string statusDetail = string.Empty;

    public ScheduledOfflineHoldCoordinator(
        IScheduledOfflineHoldRuntime runtime,
        Action<string>? information = null,
        Action<string>? warning = null)
    {
        this.runtime = runtime;
        this.information = information ?? (_ => { });
        this.warning = warning ?? (_ => { });
    }

    public bool IsActive => runtime.PersistedHold != null;
    public bool SuppressesOrdinaryAutomation
        => ScheduledOfflineHoldPolicy.SuppressesOrdinaryAutomation(runtime.PersistedHold);

    public string StatusText
    {
        get
        {
            var hold = runtime.PersistedHold;
            if (hold == null)
                return runtime.FeatureEnabled ? "Idle" : "Disabled";

            return string.IsNullOrWhiteSpace(statusDetail)
                ? $"{hold.Phase}; wake {hold.WakeAtUtc:u}"
                : $"{hold.Phase}: {statusDetail}";
        }
    }

    public bool BeginAfterSuccessfulRun(
        FishingRunMode mode,
        FishingStartupTrigger startupTrigger,
        FishingReturnDestination returnDestination,
        DateTimeOffset completedRegistrationStartUtc,
        DateTimeOffset nowUtc,
        int preWindowOffsetMinutes)
    {
        if (!ScheduledOfflineHoldPolicy.IsEligible(
                runtime.FeatureEnabled,
                runtime.MasterEnabled,
                mode,
                startupTrigger,
                returnDestination))
        {
            return false;
        }

        if (runtime.PersistedHold != null)
        {
            warning("[Fishing][OfflineHold] A persisted hold already exists; the completed voyage will remain logged in.");
            return false;
        }

        var hold = ScheduledOfflineHoldPolicy.Create(
            nowUtc,
            completedRegistrationStartUtc,
            preWindowOffsetMinutes);
        runtime.PersistHold(hold);
        statusDetail = $"Preparing logout until scheduled wake at {hold.WakeAtUtc:u}";
        information(
            $"[Fishing][OfflineHold] Persisted hold after {returnDestination} return; " +
            $"next registration={hold.NextRegistrationStartUtc:u}, wake={hold.WakeAtUtc:u}, " +
            $"offset={hold.PreWindowOffsetMinutes}m.");
        return true;
    }

    public void Update(DateTimeOffset nowUtc)
    {
        var hold = runtime.PersistedHold;
        if (hold == null)
        {
            statusDetail = string.Empty;
            return;
        }

        var now = nowUtc.ToUniversalTime();
        if (!runtime.MasterEnabled || !runtime.FeatureEnabled)
        {
            BeginCancellation(
                hold,
                !runtime.MasterEnabled
                    ? "Global automation was disabled."
                    : "Scheduled offline hold was disabled.");
            hold = runtime.PersistedHold;
            if (hold == null)
                return;
        }

        switch (hold.Phase)
        {
            case ScheduledOfflineHoldPhase.PreparingLogout:
                PrepareLogout(hold, now);
                break;
            case ScheduledOfflineHoldPhase.LoggingOut:
                ProcessLogout(hold, now);
                break;
            case ScheduledOfflineHoldPhase.Offline:
                ProcessOfflineWait(hold, now);
                break;
            case ScheduledOfflineHoldPhase.Waking:
                ProcessWake(hold, now);
                break;
            case ScheduledOfflineHoldPhase.WaitingForWorldReady:
                ProcessWorldReady(hold, now);
                break;
            case ScheduledOfflineHoldPhase.StartingFishing:
                ProcessFishingStartup(hold, now);
                break;
            case ScheduledOfflineHoldPhase.Cancelling:
                ProcessCancellation(hold, now);
                break;
            default:
                BeginCancellation(hold, $"Unknown persisted phase value {(int)hold.Phase}.");
                ProcessCancellation(hold, now);
                break;
        }
    }

    public void Cancel(string reason, DateTimeOffset nowUtc)
    {
        var hold = runtime.PersistedHold;
        if (hold == null)
            return;

        BeginCancellation(hold, reason);
        hold = runtime.PersistedHold;
        if (hold != null)
            ProcessCancellation(hold, nowUtc.ToUniversalTime());
    }

    private void PrepareLogout(ScheduledOfflineHoldState hold, DateTimeOffset now)
    {
        if (!hold.InitialAutoRetainerMultiModeEnabled.HasValue)
        {
            var multiMode = runtime.ReadAutoRetainerMultiMode();
            if (!multiMode.Success)
            {
                statusDetail = $"Waiting for readable AutoRetainer multi-mode: {multiMode.Error}";
                if (ScheduledOfflineHoldPolicy.HasLogoutTimedOut(hold, now))
                    BeginCancellation(hold, $"Could not snapshot AutoRetainer multi-mode before logout: {multiMode.Error}");
                return;
            }

            hold.InitialAutoRetainerMultiModeEnabled = multiMode.Enabled;
            runtime.PersistHold(hold);
        }

        if (hold.InitialAutoRetainerMultiModeEnabled == true)
        {
            if (!hold.AutoRetainerMultiModeRestoreRequired)
            {
                // Persist restoration ownership before changing the external state so a reload
                // between the write and the next save still restores the original value.
                hold.AutoRetainerMultiModeRestoreRequired = true;
                runtime.PersistHold(hold);
            }

            if (!runtime.TrySetAutoRetainerMultiMode(false, out var error))
            {
                statusDetail = $"Waiting to disable AutoRetainer multi-mode: {error}";
                if (ScheduledOfflineHoldPolicy.HasLogoutTimedOut(hold, now))
                    BeginCancellation(hold, $"Could not disable AutoRetainer multi-mode before logout: {error}");
                return;
            }
        }

        hold.Phase = ScheduledOfflineHoldPhase.LoggingOut;
        runtime.PersistHold(hold);
        statusDetail = "AutoRetainer is settled; logging out";
        ProcessLogout(hold, now);
    }

    private void ProcessLogout(ScheduledOfflineHoldState hold, DateTimeOffset now)
    {
        if (!runtime.IsLoggedIn)
        {
            hold.Phase = ScheduledOfflineHoldPhase.Offline;
            hold.LoggedOutAtUtc = now;
            runtime.PersistHold(hold);
            statusDetail = $"Offline until {hold.WakeAtUtc:u}";
            information($"[Fishing][OfflineHold] Logout confirmed; holding offline until {hold.WakeAtUtc:u}.");
            return;
        }

        if (ScheduledOfflineHoldPolicy.HasLogoutTimedOut(hold, now))
        {
            warning("[Fishing][OfflineHold] Logout did not complete within 45 seconds; the voyage remains successful and the character will stay logged in.");
            BeginCancellation(hold, "Logout timed out after 45 seconds.");
            var cancelling = runtime.PersistedHold;
            if (cancelling != null)
                ProcessCancellation(cancelling, now);
            return;
        }

        runtime.TryConfirmLogout();
        if (!ScheduledOfflineHoldPolicy.ShouldAttemptLogout(hold, now))
            return;

        hold.LastLogoutAttemptUtc = now;
        runtime.PersistHold(hold);
        runtime.SendLogoutCommand();
        statusDetail = "Waiting for logout confirmation";
    }

    private void ProcessOfflineWait(ScheduledOfflineHoldState hold, DateTimeOffset now)
    {
        if (!ScheduledOfflineHoldPolicy.ShouldWake(hold, now))
        {
            statusDetail = $"Offline until {hold.WakeAtUtc:u}";
            return;
        }

        hold.Phase = ScheduledOfflineHoldPhase.Waking;
        hold.WakeStartedAtUtc ??= now;
        runtime.PersistHold(hold);
        statusDetail = "Restoring AutoRetainer before the scheduled wake";
        ProcessWake(hold, now);
    }

    private void ProcessWake(ScheduledOfflineHoldState hold, DateTimeOffset now)
    {
        if (!TryRestoreAutoRetainer(hold, now))
            return;

        if (ScheduledOfflineHoldPolicy.HasWakeWindowExpired(hold, now))
        {
            BeginCancellation(hold, "The scheduled wake did not reach a usable login before the registration window closed.");
            var cancelling = runtime.PersistedHold;
            if (cancelling != null)
                ProcessCancellation(cancelling, now);
            return;
        }

        if (runtime.IsLoggedIn)
        {
            hold.Phase = ScheduledOfflineHoldPhase.WaitingForWorldReady;
            runtime.PersistHold(hold);
            statusDetail = "Bootstrap character logged in; waiting for world readiness";
            return;
        }

        if (hold.WakeAttemptedAtUtc.HasValue)
        {
            statusDetail = "Intentional fishing wake requested once; waiting for login";
            return;
        }

        if (!runtime.IsIntentionalFishingWakeReady(out var blockedReason))
        {
            statusDetail = $"Waiting for safe character-select wake: {blockedReason}";
            return;
        }

        // Persist the one-shot attempt before firing callbacks. A plugin reload cannot issue a
        // second bootstrap login for the same hold.
        hold.WakeAttemptedAtUtc = now;
        runtime.PersistHold(hold);
        if (!runtime.TryRequestIntentionalFishingWake(out var error))
        {
            BeginCancellation(hold, $"The intentional character-select wake failed: {error}");
            var cancelling = runtime.PersistedHold;
            if (cancelling != null)
                ProcessCancellation(cancelling, now);
            return;
        }

        statusDetail = "Intentional fishing wake requested once; waiting for login";
        information("[Fishing][OfflineHold] Requested one safe first-character bootstrap login for the scheduled wake.");
    }

    private void ProcessWorldReady(ScheduledOfflineHoldState hold, DateTimeOffset now)
    {
        if (ScheduledOfflineHoldPolicy.HasWakeWindowExpired(hold, now))
        {
            BeginCancellation(hold, "World readiness did not settle before the registration window closed.");
            var cancelling = runtime.PersistedHold;
            if (cancelling != null)
                ProcessCancellation(cancelling, now);
            return;
        }

        if (!runtime.IsScheduledWakeWorldReady(out var reason))
        {
            statusDetail = reason;
            return;
        }

        hold.WorldReadyAtUtc = now;
        hold.Phase = ScheduledOfflineHoldPhase.StartingFishing;
        runtime.PersistHold(hold);
        nextStartupPollUtc = default;
        statusDetail = "World ready; handing the window to the fishing startup coordinator";
        ProcessFishingStartup(hold, now);
    }

    private void ProcessFishingStartup(ScheduledOfflineHoldState hold, DateTimeOffset now)
    {
        if (ScheduledOfflineHoldPolicy.HasWakeWindowExpired(hold, now))
        {
            BeginCancellation(hold, "Fishing startup did not claim the scheduled window before it closed.");
            var cancelling = runtime.PersistedHold;
            if (cancelling != null)
                ProcessCancellation(cancelling, now);
            return;
        }

        if (nextStartupPollUtc != default && now < nextStartupPollUtc)
            return;

        nextStartupPollUtc = now + StartupPollInterval;
        var result = runtime.RunScheduledWakeStartup(hold.PreWindowOffsetMinutes);
        statusDetail = result.Reason;
        if (!result.Started && !(result.Action == FishingStartupAction.AlreadyHandled && result.ClaimsStartup))
            return;

        information($"[Fishing][OfflineHold] Scheduled wake handed off: {FishingStartupDiagnostics.FormatStarted(result)}");
        runtime.CompleteIntentionalFishingWake();
        runtime.PersistHold(null);
        statusDetail = string.Empty;
    }

    private void BeginCancellation(ScheduledOfflineHoldState hold, string reason)
    {
        if (hold.Phase != ScheduledOfflineHoldPhase.Cancelling ||
            !string.Equals(hold.CancellationReason, reason, StringComparison.Ordinal))
        {
            warning($"[Fishing][OfflineHold] Cancelling without forcing login: {reason}");
        }

        hold.Phase = ScheduledOfflineHoldPhase.Cancelling;
        hold.CancellationReason = reason;
        runtime.PersistHold(hold);
        statusDetail = $"Restoring AutoRetainer; {reason}";
    }

    private void ProcessCancellation(ScheduledOfflineHoldState hold, DateTimeOffset now)
    {
        if (!TryRestoreAutoRetainer(hold, now))
            return;

        var reason = hold.CancellationReason;
        runtime.PersistHold(null);
        statusDetail = string.Empty;
        information($"[Fishing][OfflineHold] Cleared hold after verified AutoRetainer restoration; reason={reason}");
    }

    private bool TryRestoreAutoRetainer(ScheduledOfflineHoldState hold, DateTimeOffset now)
    {
        if (!hold.AutoRetainerMultiModeRestoreRequired)
            return true;

        if (!hold.InitialAutoRetainerMultiModeEnabled.HasValue)
        {
            warning("[Fishing][OfflineHold] Persisted AutoRetainer restoration ownership has no original value; retaining the hold.");
            statusDetail = "AutoRetainer restoration is missing its original value";
            return false;
        }

        if (nextRestoreAttemptUtc != default && now < nextRestoreAttemptUtc)
            return false;

        nextRestoreAttemptUtc = now + RestoreRetryInterval;
        if (!runtime.TrySetAutoRetainerMultiMode(
                hold.InitialAutoRetainerMultiModeEnabled.Value,
                out var error))
        {
            statusDetail = $"Waiting to restore AutoRetainer multi-mode: {error}";
            warning($"[Fishing][OfflineHold] AutoRetainer multi-mode restoration remains pending: {error}");
            return false;
        }

        hold.AutoRetainerMultiModeRestoreRequired = false;
        runtime.PersistHold(hold);
        nextRestoreAttemptUtc = default;
        return true;
    }
}
