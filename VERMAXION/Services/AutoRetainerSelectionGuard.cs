using System;
using VERMAXION.Models;

namespace VERMAXION.Services;

internal sealed class AutoRetainerSelectionGuard
{
    internal const int MaxRepairAttempts = 3;
    internal static readonly TimeSpan ObservationInterval = TimeSpan.FromSeconds(1);
    internal static readonly TimeSpan RepairRetryInterval = TimeSpan.FromSeconds(1);
    internal static readonly TimeSpan ReadFailureLogInterval = TimeSpan.FromSeconds(60);

    private readonly IAutoRetainerSelectionAccessor accessor;
    private readonly Action<string> logInformation;
    private readonly Action<string> logWarning;

    private SelectionTarget? currentTarget;
    private SelectionTarget? previousTarget;
    private ulong currentContentId;
    private ulong previousContentId;
    private bool wasLoggedIn;
    private bool guardEnabled;

    public AutoRetainerSelectionGuardState State { get; private set; } =
        AutoRetainerSelectionGuardState.Inactive;

    public ulong SessionContentId => currentContentId;
    public ulong PreviousContentId => previousContentId;
    public int RepairAttemptCount { get; private set; }
    public int RepairIncidentCount { get; private set; }
    public bool RepairSucceeded { get; private set; }
    public string Status { get; private set; } = "Inactive";

    public AutoRetainerSelectionGuard(
        IAutoRetainerSelectionAccessor accessor,
        Action<string> logInformation,
        Action<string> logWarning)
    {
        this.accessor = accessor;
        this.logInformation = logInformation;
        this.logWarning = logWarning;
    }

    public void Update(
        bool enabled,
        bool isLoggedIn,
        ulong localContentId,
        DateTime nowUtc)
    {
        if (!enabled)
        {
            if (guardEnabled)
                logInformation("[AR][SelectionGuard] Disabled; current and previous character selections will not be changed.");

            guardEnabled = false;
            TrackContentIdWithoutObservation(isLoggedIn, localContentId);
            StopObservation("Disabled");
            return;
        }

        guardEnabled = true;

        if (!isLoggedIn)
        {
            wasLoggedIn = false;
            State = AutoRetainerSelectionGuardState.Inactive;
            Status = "Waiting for login";
            return;
        }

        if (localContentId == 0)
        {
            State = AutoRetainerSelectionGuardState.Inactive;
            Status = "Waiting for local content ID";
            return;
        }

        if (currentContentId == 0 || currentContentId != localContentId)
            BeginCharacterTransition(localContentId, nowUtc);
        else if (!wasLoggedIn || currentTarget == null)
            ArmTrackedTargets(nowUtc);

        wasLoggedIn = true;

        // AutoRetainer can deselect the character being left during a relog. Repair that
        // entry first, then reconcile the character that is currently loaded.
        ProcessTarget(previousTarget, nowUtc);
        ProcessTarget(currentTarget, nowUtc);
        RefreshAggregateState();
    }

    private void BeginCharacterTransition(ulong localContentId, DateTime nowUtc)
    {
        var oldCurrentContentId = currentContentId;
        previousContentId = oldCurrentContentId != 0 && oldCurrentContentId != localContentId
            ? oldCurrentContentId
            : 0;
        currentContentId = localContentId;

        ArmTrackedTargets(nowUtc);
    }

    private void ArmTrackedTargets(DateTime nowUtc)
    {

        previousTarget = previousContentId == 0
            ? null
            : new SelectionTarget(previousContentId, SelectionTargetRole.Previous, nowUtc);
        currentTarget = new SelectionTarget(currentContentId, SelectionTargetRole.Current, nowUtc);

        RepairAttemptCount = 0;
        RepairIncidentCount = 0;
        RepairSucceeded = false;
        State = AutoRetainerSelectionGuardState.Observing;
        Status = previousTarget == null
            ? "Watching current AutoRetainer selection"
            : "Watching current and previous AutoRetainer selections";

        logInformation(previousTarget == null
            ? $"[AR][SelectionGuard] Tracking current character {FormatContentId(currentContentId)}; an already-disabled selection will be restored."
            : $"[AR][SelectionGuard] Tracking current {FormatContentId(currentContentId)} and previous {FormatContentId(previousContentId)}; already-disabled selections will be restored.");
    }

    private void TrackContentIdWithoutObservation(bool isLoggedIn, ulong localContentId)
    {
        if (!isLoggedIn)
        {
            wasLoggedIn = false;
            return;
        }

        if (localContentId == 0)
            return;

        if (currentContentId == 0)
        {
            currentContentId = localContentId;
        }
        else if (currentContentId != localContentId)
        {
            previousContentId = currentContentId;
            currentContentId = localContentId;
        }

        wasLoggedIn = true;
    }

    private void ProcessTarget(SelectionTarget? target, DateTime nowUtc)
    {
        if (target == null || target.Completed || nowUtc < target.NextActionAtUtc)
            return;

        if (target.RepairIncidentStarted)
        {
            AttemptRepair(target, nowUtc);
            return;
        }

        var selection = SafeRead(target.ContentId);
        if (!selection.Success)
        {
            target.ConsecutiveReadFailures++;
            if (target.LastReadFailureLogAtUtc == DateTime.MinValue ||
                nowUtc - target.LastReadFailureLogAtUtc >= ReadFailureLogInterval)
            {
                logWarning(
                    $"[AR][SelectionGuard] {FormatRole(target.Role)} selection read failed closed for {FormatContentId(target.ContentId)}; no write was attempted and observation will retry. ConsecutiveFailures={target.ConsecutiveReadFailures}. Error: {selection.Error}");
                target.LastReadFailureLogAtUtc = nowUtc;
            }

            target.NextActionAtUtc = nowUtc + ObservationInterval;
            return;
        }

        target.ConsecutiveReadFailures = 0;
        if (selection.Enabled)
        {
            target.NextActionAtUtc = nowUtc + ObservationInterval;
            return;
        }

        target.RepairIncidentStarted = true;
        RepairIncidentCount++;
        logWarning(
            $"[AR][SelectionGuard] Detected disabled AutoRetainer checking for {FormatRole(target.Role)} character {FormatContentId(target.ContentId)}; starting one bounded repair incident.");
        AttemptRepair(target, nowUtc);
    }

    private void AttemptRepair(SelectionTarget target, DateTime nowUtc)
    {
        target.RepairAttemptCount++;
        RepairAttemptCount++;
        var result = SafeWrite(target.ContentId, enabled: true);
        if (result.Success && result.Enabled && result.SaveInvoked)
        {
            target.RepairSucceeded = true;
            target.Completed = true;
            RepairSucceeded = true;
            logInformation(
                $"[AR][SelectionGuard] Restored and persisted AutoRetainer checking for {FormatRole(target.Role)} character {FormatContentId(target.ContentId)} on attempt {target.RepairAttemptCount}/{MaxRepairAttempts}; this character is complete until the next transition.");
            return;
        }

        if (target.RepairAttemptCount >= MaxRepairAttempts)
        {
            target.Completed = true;
            logWarning(
                $"[AR][SelectionGuard] Repair exhausted for {FormatRole(target.Role)} character {FormatContentId(target.ContentId)} after {target.RepairAttemptCount} attempts; this character is complete until the next transition. Last error: {FormatWriteFailure(result)}");
            return;
        }

        logWarning(
            $"[AR][SelectionGuard] Repair attempt {target.RepairAttemptCount}/{MaxRepairAttempts} failed for {FormatRole(target.Role)} character {FormatContentId(target.ContentId)}: {FormatWriteFailure(result)}");
        target.NextActionAtUtc = nowUtc + RepairRetryInterval;
    }

    private void RefreshAggregateState()
    {
        if (!guardEnabled || !wasLoggedIn)
        {
            State = AutoRetainerSelectionGuardState.Inactive;
            return;
        }

        var repairing = IsRepairing(previousTarget) || IsRepairing(currentTarget);
        if (repairing)
        {
            State = AutoRetainerSelectionGuardState.Repairing;
            Status = "Restoring AutoRetainer character selection";
            return;
        }

        var observing = IsObserving(previousTarget) || IsObserving(currentTarget);
        if (observing)
        {
            State = AutoRetainerSelectionGuardState.Observing;
            Status = previousTarget is { Completed: false }
                ? "Watching current and previous AutoRetainer selections"
                : "Watching current AutoRetainer selection";
            return;
        }

        State = AutoRetainerSelectionGuardState.Completed;
        Status = "Selection repair complete until next character transition";
    }

    private void StopObservation(string status)
    {
        currentTarget = null;
        previousTarget = null;
        RepairAttemptCount = 0;
        RepairIncidentCount = 0;
        RepairSucceeded = false;
        State = AutoRetainerSelectionGuardState.Inactive;
        Status = status;
    }

    private AutoRetainerSelectionReadResult SafeRead(ulong contentId)
    {
        try
        {
            return accessor.ReadCharacterSelection(contentId);
        }
        catch (Exception ex)
        {
            return AutoRetainerSelectionReadResult.Failed(
                $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private AutoRetainerSelectionWriteResult SafeWrite(ulong contentId, bool enabled)
    {
        try
        {
            return accessor.WriteCharacterSelection(contentId, enabled);
        }
        catch (Exception ex)
        {
            return AutoRetainerSelectionWriteResult.Failed(
                $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static bool IsRepairing(SelectionTarget? target)
        => target is { Completed: false, RepairIncidentStarted: true };

    private static bool IsObserving(SelectionTarget? target)
        => target is { Completed: false, RepairIncidentStarted: false };

    private static string FormatWriteFailure(AutoRetainerSelectionWriteResult result)
    {
        var error = string.IsNullOrWhiteSpace(result.Error) ? "write verification failed" : result.Error;
        return $"{error} (enabled={result.Enabled}, saveInvoked={result.SaveInvoked})";
    }

    private static string FormatRole(SelectionTargetRole role)
        => role == SelectionTargetRole.Current ? "current" : "previous";

    private static string FormatContentId(ulong contentId)
        => contentId == 0 ? "none" : contentId.ToString("X16");

    private enum SelectionTargetRole
    {
        Current,
        Previous,
    }

    private sealed class SelectionTarget(
        ulong contentId,
        SelectionTargetRole role,
        DateTime nextActionAtUtc)
    {
        public ulong ContentId { get; } = contentId;
        public SelectionTargetRole Role { get; } = role;
        public DateTime NextActionAtUtc { get; set; } = nextActionAtUtc;
        public DateTime LastReadFailureLogAtUtc { get; set; } = DateTime.MinValue;
        public int ConsecutiveReadFailures { get; set; }
        public int RepairAttemptCount { get; set; }
        public bool RepairIncidentStarted { get; set; }
        public bool RepairSucceeded { get; set; }
        public bool Completed { get; set; }
    }
}
