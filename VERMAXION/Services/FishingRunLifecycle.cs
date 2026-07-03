using System;
using Dalamud.Plugin.Services;
using VERMAXION.IPC;
using VERMAXION.Models;

namespace VERMAXION.Services;

public sealed class FishingRunLifecycle
{
    private static readonly TimeSpan CleanupRetryInterval = TimeSpan.FromSeconds(2);
    private readonly IPluginLog log;
    private readonly YesAlreadyIPC yesAlready;
    private readonly AutoRetainerIPC autoRetainer;
    private readonly AutoHookIPC autoHook;

    public FishingRunContext? Current { get; private set; }
    public bool IsActive => Current != null;
    public bool IsCleanupPending => Current?.CleanupPending == true;
    public FishingRunMode? Mode => Current?.Mode;
    public string StatusPrefix => Current?.StatusPrefix ?? string.Empty;
    public DateTimeOffset? LastQueueRegistrationConfirmedStartUtc { get; private set; }
    public DateTimeOffset? LastTerminalFailureBeforeQueueConfirmationStartUtc { get; private set; }

    public FishingRunLifecycle(
        IPluginLog log,
        YesAlreadyIPC yesAlready,
        AutoRetainerIPC autoRetainer,
        AutoHookIPC autoHook)
    {
        this.log = log;
        this.yesAlready = yesAlready;
        this.autoRetainer = autoRetainer;
        this.autoHook = autoHook;
    }

    public bool TryBegin(
        FishingRunMode mode,
        string targetCharacterKey,
        DateTimeOffset registrationStartUtc,
        DateTimeOffset registrationDeadlineUtc,
        out string error)
    {
        error = string.Empty;
        if (Current != null)
        {
            if (!Current.CleanupPending &&
                Current.Mode == mode &&
                string.Equals(Current.TargetCharacterKey, targetCharacterKey, StringComparison.OrdinalIgnoreCase) &&
                Current.RegistrationStartUtc == registrationStartUtc)
            {
                return true;
            }

            error = "Another Ocean Fishing run already owns external plugin state.";
            return false;
        }

        var context = new FishingRunContext
        {
            Mode = mode,
            TargetCharacterKey = targetCharacterKey.Trim(),
            RegistrationStartUtc = registrationStartUtc.ToUniversalTime(),
            RegistrationDeadlineUtc = registrationDeadlineUtc.ToUniversalTime(),
        };
        Current = context;

        yesAlready.Pause();
        context.YesAlreadyLeaseOwned = yesAlready.IsPaused;
        if (!context.YesAlreadyLeaseOwned)
        {
            error = "Could not acquire the VERMAXION YesAlready pause lease.";
            Cleanup("startup lease failure");
            return false;
        }

        var multiMode = autoRetainer.ReadMultiModeEnabled();
        if (!multiMode.Success)
        {
            error = $"Could not read AutoRetainer multi-mode: {multiMode.Error}";
            Cleanup("AutoRetainer snapshot failure");
            return false;
        }

        context.InitialAutoRetainerMultiModeEnabled = multiMode.Enabled;
        if (multiMode.Enabled)
        {
            if (!autoRetainer.TrySetMultiModeEnabled(false, out error))
            {
                error = $"Could not disable AutoRetainer multi-mode: {error}";
                Cleanup("AutoRetainer disable failure");
                return false;
            }

            context.AutoRetainerMultiModeChanged = true;
        }

        var autoHookState = autoHook.ReadPluginState();
        if (!autoHookState.Success)
        {
            error = $"Could not read AutoHook state: {autoHookState.Error}";
            Cleanup("AutoHook snapshot failure");
            return false;
        }

        context.InitialAutoHookEnabled = autoHookState.Enabled;
        log.Information($"[Fishing][Lifecycle] Began {mode} run for {context.TargetCharacterKey}; registration={context.RegistrationStartUtc:u}, deadline={context.RegistrationDeadlineUtc:u}, AR={multiMode.Enabled}, AutoHook={autoHookState.Enabled}");
        return true;
    }

    public bool IsActiveForWindow(DateTimeOffset registrationStartUtc)
    {
        var normalized = registrationStartUtc.ToUniversalTime();
        return Current?.RegistrationStartUtc == normalized;
    }

    public bool IsQueueRegistrationConfirmedForWindow(DateTimeOffset registrationStartUtc)
    {
        var normalized = registrationStartUtc.ToUniversalTime();
        return Current?.RegistrationStartUtc == normalized && Current.QueueRegistrationConfirmed ||
               LastQueueRegistrationConfirmedStartUtc == normalized;
    }

    public bool IsTerminalFailureBeforeQueueConfirmationForWindow(DateTimeOffset registrationStartUtc)
    {
        var normalized = registrationStartUtc.ToUniversalTime();
        return Current?.RegistrationStartUtc == normalized && Current.TerminalFailureBeforeQueueConfirmation ||
               LastTerminalFailureBeforeQueueConfirmationStartUtc == normalized;
    }

    public void ClearWindowOutcome(DateTimeOffset registrationStartUtc)
    {
        var normalized = registrationStartUtc.ToUniversalTime();
        if (LastQueueRegistrationConfirmedStartUtc == normalized)
            LastQueueRegistrationConfirmedStartUtc = null;
        if (LastTerminalFailureBeforeQueueConfirmationStartUtc == normalized)
            LastTerminalFailureBeforeQueueConfirmationStartUtc = null;
    }

    public void MarkQueueRegistrationConfirmed(string reason)
    {
        var context = Current;
        if (context == null)
            return;

        if (!context.QueueRegistrationConfirmed)
        {
            log.Information($"[Fishing][Lifecycle] Queue registration confirmed: {reason}");
            context.QueueRegistrationConfirmed = true;
        }

        LastQueueRegistrationConfirmedStartUtc = context.RegistrationStartUtc;
        if (LastTerminalFailureBeforeQueueConfirmationStartUtc == context.RegistrationStartUtc)
            LastTerminalFailureBeforeQueueConfirmationStartUtc = null;
    }

    public void MarkTerminalFailureBeforeQueueConfirmation(string reason)
    {
        var context = Current;
        if (context == null || context.QueueRegistrationConfirmed)
            return;

        context.TerminalFailureBeforeQueueConfirmation = true;
        LastTerminalFailureBeforeQueueConfirmationStartUtc = context.RegistrationStartUtc;
        log.Warning($"[Fishing][Lifecycle] Terminal pre-queue failure for {context.RegistrationStartUtc:u}: {reason}");
    }

    public bool EnsureAutoHookEnabled(out string error)
    {
        error = string.Empty;
        var context = Current;
        if (context == null)
        {
            error = "No active Ocean Fishing run context.";
            return false;
        }

        var state = autoHook.ReadPluginState();
        if (!state.Success)
        {
            error = $"Could not read AutoHook state: {state.Error}";
            return false;
        }

        if (state.Enabled)
            return true;

        if (!autoHook.TrySetPluginState(true, out error))
            return false;

        context.AutoHookChanged = context.InitialAutoHookEnabled != true;
        return true;
    }

    public bool ReleaseRegistrationLeases(string reason)
    {
        var context = Current;
        if (context == null)
            return true;

        log.Information($"[Fishing][Lifecycle] Releasing registration leases: {reason}");

        if (FishingExternalStatePolicy.ShouldRestore(
                context.InitialAutoRetainerMultiModeEnabled,
                context.AutoRetainerMultiModeChanged))
        {
            if (autoRetainer.TrySetMultiModeEnabled(
                    context.InitialAutoRetainerMultiModeEnabled!.Value,
                    out var arError))
            {
                context.AutoRetainerMultiModeChanged = false;
            }
            else
            {
                log.Warning($"[Fishing][Lifecycle] AutoRetainer multi-mode restore failed: {arError}");
            }
        }

        if (context.YesAlreadyLeaseOwned)
        {
            yesAlready.Unpause();
            context.YesAlreadyLeaseOwned = yesAlready.IsPaused;
            if (context.YesAlreadyLeaseOwned)
                log.Warning("[Fishing][Lifecycle] YesAlready pause lease release could not be verified.");
        }

        return !context.AutoRetainerMultiModeChanged && !context.YesAlreadyLeaseOwned;
    }

    public void Cleanup(string reason)
    {
        var context = Current;
        if (context == null)
            return;

        if (!context.CleanupPending)
            log.Information($"[Fishing][Lifecycle] Entering verified cleanup for {context.Mode} run: {reason}");

        context.CleanupPending = true;
        context.CleanupReason = reason;
        context.LastCleanupAttemptUtc = default;
        Update();
    }

    public void Update()
    {
        var context = Current;
        if (context?.CleanupPending != true)
            return;

        var now = DateTimeOffset.UtcNow;
        if (context.LastCleanupAttemptUtc != default &&
            now - context.LastCleanupAttemptUtc < CleanupRetryInterval)
        {
            return;
        }

        context.LastCleanupAttemptUtc = now;
        if (FishingExternalStatePolicy.ShouldRestore(context.InitialAutoHookEnabled, context.AutoHookChanged))
        {
            if (autoHook.TrySetPluginState(context.InitialAutoHookEnabled!.Value, out var hookError))
                context.AutoHookChanged = false;
            else
                log.Warning($"[Fishing][Lifecycle] AutoHook restore failed; ownership retained for retry: {hookError}");
        }

        ReleaseRegistrationLeases(context.CleanupReason);

        if (context.OwnsExternalState)
        {
            log.Warning(
                $"[Fishing][Lifecycle] Cleanup remains pending; AutoHook={context.AutoHookChanged}, " +
                $"AR multi-mode={context.AutoRetainerMultiModeChanged}, YesAlready={context.YesAlreadyLeaseOwned}");
            return;
        }

        log.Information($"[Fishing][Lifecycle] Verified cleanup complete: {context.CleanupReason}");
        Current = null;
    }

    public void ForceCleanup(string reason)
    {
        var context = Current;
        if (context == null)
            return;

        context.CleanupPending = true;
        context.CleanupReason = reason;
        context.LastCleanupAttemptUtc = default;
        Update();
        context = Current;
        if (context == null)
            return;

        log.Warning(
            $"[Fishing][Lifecycle] Forced best-effort cleanup ended with unresolved state: " +
            $"AutoHook={context.AutoHookChanged}, AR multi-mode={context.AutoRetainerMultiModeChanged}, " +
            $"YesAlready={context.YesAlreadyLeaseOwned}; reason={reason}");
        Current = null;
    }
}
