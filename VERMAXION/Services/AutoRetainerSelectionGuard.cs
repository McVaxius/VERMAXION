using System;
using VERMAXION.Models;

namespace VERMAXION.Services;

internal sealed class AutoRetainerSelectionGuard
{
    internal const int MaxObservationReadFailures = 3;
    internal const int MaxRepairAttempts = 3;
    internal static readonly TimeSpan ObservationInterval = TimeSpan.FromSeconds(1);
    internal static readonly TimeSpan RepairRetryInterval = TimeSpan.FromSeconds(1);

    private readonly IAutoRetainerSelectionAccessor accessor;
    private readonly Action<string> logInformation;
    private readonly Action<string> logWarning;

    private bool sessionActive;
    private ulong sessionContentId;
    private DateTime nextActionAtUtc = DateTime.MinValue;
    private int observationReadFailureCount;

    public AutoRetainerSelectionGuardState State { get; private set; } =
        AutoRetainerSelectionGuardState.Inactive;

    public ulong SessionContentId => sessionContentId;
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

    public void NotifyCurrentTaskWorkStarted(
        bool enabled,
        bool isLoggedIn,
        ulong localContentId,
        DateTime nowUtc)
    {
        if (!ReconcileSession(isLoggedIn, localContentId, "work-start notification"))
            return;

        if (State != AutoRetainerSelectionGuardState.AwaitingWorkStart)
            return;

        if (!enabled)
        {
            Complete("Disabled when VERMAXION work started");
            return;
        }

        var snapshot = SafeRead(localContentId);
        if (!snapshot.Success)
        {
            logWarning(
                $"[AR][SelectionGuard] Not armed for {FormatContentId(localContentId)} because the work-start selection snapshot failed closed: {snapshot.Error}");
            Complete("Work-start selection snapshot failed");
            return;
        }

        if (!snapshot.Enabled)
        {
            logInformation(
                $"[AR][SelectionGuard] Not armed for {FormatContentId(localContentId)} because AutoRetainer checking was already deselected when VERMAXION work started.");
            Complete("Already deselected before VERMAXION work");
            return;
        }

        State = AutoRetainerSelectionGuardState.Observing;
        Status = "Watching for one selected-to-deselected transition";
        observationReadFailureCount = 0;
        nextActionAtUtc = nowUtc + ObservationInterval;
        logInformation(
            $"[AR][SelectionGuard] Armed for {FormatContentId(localContentId)} after the first VERMAXION task began real work.");
    }

    public void Update(
        bool enabled,
        bool isLoggedIn,
        ulong localContentId,
        DateTime nowUtc)
    {
        if (!ReconcileSession(isLoggedIn, localContentId, "framework update"))
            return;

        if (!enabled)
        {
            if (State is AutoRetainerSelectionGuardState.Observing or AutoRetainerSelectionGuardState.Repairing)
            {
                logInformation(
                    $"[AR][SelectionGuard] Observation stopped for {FormatContentId(sessionContentId)} because the global guard was disabled.");
                Complete("Disabled while active");
            }

            return;
        }

        if (nowUtc < nextActionAtUtc)
            return;

        switch (State)
        {
            case AutoRetainerSelectionGuardState.Observing:
                ObserveSelection(nowUtc);
                break;
            case AutoRetainerSelectionGuardState.Repairing:
                AttemptRepair(nowUtc);
                break;
        }
    }

    public void ResetSession(string reason)
    {
        if (sessionActive || State != AutoRetainerSelectionGuardState.Inactive)
        {
            logInformation(
                $"[AR][SelectionGuard] Session reset: reason={reason}, contentId={FormatContentId(sessionContentId)}, state={State}.");
        }

        sessionActive = false;
        sessionContentId = 0;
        State = AutoRetainerSelectionGuardState.Inactive;
        Status = "Inactive";
        nextActionAtUtc = DateTime.MinValue;
        observationReadFailureCount = 0;
        RepairAttemptCount = 0;
        RepairIncidentCount = 0;
        RepairSucceeded = false;
    }

    private bool ReconcileSession(bool isLoggedIn, ulong localContentId, string source)
    {
        if (!isLoggedIn || localContentId == 0)
        {
            if (sessionActive || State != AutoRetainerSelectionGuardState.Inactive)
                ResetSession(!isLoggedIn ? "logout" : "local content ID unavailable");
            return false;
        }

        if (sessionActive && sessionContentId == localContentId)
            return true;

        var previousContentId = sessionContentId;
        var hadSession = sessionActive;
        ResetSession(hadSession
            ? $"local content ID changed from {FormatContentId(previousContentId)} via {source}"
            : $"new login session via {source}");

        sessionActive = true;
        sessionContentId = localContentId;
        State = AutoRetainerSelectionGuardState.AwaitingWorkStart;
        Status = "Waiting for VERMAXION real work";
        return true;
    }

    private void ObserveSelection(DateTime nowUtc)
    {
        var selection = SafeRead(sessionContentId);
        if (!selection.Success)
        {
            observationReadFailureCount++;
            if (observationReadFailureCount >= MaxObservationReadFailures)
            {
                logWarning(
                    $"[AR][SelectionGuard] Stopped watching {FormatContentId(sessionContentId)} after {observationReadFailureCount} consecutive read failures; no selection write was attempted. Last error: {selection.Error}");
                Complete("Selection observation failed closed");
                return;
            }

            logWarning(
                $"[AR][SelectionGuard] Selection observation read {observationReadFailureCount}/{MaxObservationReadFailures} failed for {FormatContentId(sessionContentId)}: {selection.Error}");
            nextActionAtUtc = nowUtc + ObservationInterval;
            return;
        }

        observationReadFailureCount = 0;
        if (selection.Enabled)
        {
            nextActionAtUtc = nowUtc + ObservationInterval;
            return;
        }

        RepairIncidentCount++;
        State = AutoRetainerSelectionGuardState.Repairing;
        Status = "Restoring AutoRetainer character selection";
        logWarning(
            $"[AR][SelectionGuard] Detected selected-to-deselected transition for {FormatContentId(sessionContentId)} after VERMAXION work; starting one bounded repair incident.");
        AttemptRepair(nowUtc);
    }

    private void AttemptRepair(DateTime nowUtc)
    {
        RepairAttemptCount++;
        var result = SafeWrite(sessionContentId, enabled: true);
        if (result.Success && result.Enabled && result.SaveInvoked)
        {
            RepairSucceeded = true;
            logInformation(
                $"[AR][SelectionGuard] Restored and persisted AutoRetainer checking for {FormatContentId(sessionContentId)} on attempt {RepairAttemptCount}/{MaxRepairAttempts}; observation is complete until the next login.");
            Complete("Selection restored and persisted");
            return;
        }

        if (RepairAttemptCount >= MaxRepairAttempts)
        {
            logWarning(
                $"[AR][SelectionGuard] Repair exhausted for {FormatContentId(sessionContentId)} after {RepairAttemptCount} attempts; observation is complete until the next login. Last error: {FormatWriteFailure(result)}");
            Complete("Selection repair exhausted");
            return;
        }

        logWarning(
            $"[AR][SelectionGuard] Repair attempt {RepairAttemptCount}/{MaxRepairAttempts} failed for {FormatContentId(sessionContentId)}: {FormatWriteFailure(result)}");
        nextActionAtUtc = nowUtc + RepairRetryInterval;
    }

    private AutoRetainerSelectionReadResult SafeRead(ulong localContentId)
    {
        try
        {
            return accessor.ReadCurrentCharacterSelection(localContentId);
        }
        catch (Exception ex)
        {
            return AutoRetainerSelectionReadResult.Failed(
                $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private AutoRetainerSelectionWriteResult SafeWrite(ulong localContentId, bool enabled)
    {
        try
        {
            return accessor.WriteCurrentCharacterSelection(localContentId, enabled);
        }
        catch (Exception ex)
        {
            return AutoRetainerSelectionWriteResult.Failed(
                $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private void Complete(string status)
    {
        State = AutoRetainerSelectionGuardState.Completed;
        Status = status;
        nextActionAtUtc = DateTime.MaxValue;
    }

    private static string FormatWriteFailure(AutoRetainerSelectionWriteResult result)
    {
        var error = string.IsNullOrWhiteSpace(result.Error) ? "write verification failed" : result.Error;
        return $"{error} (enabled={result.Enabled}, saveInvoked={result.SaveInvoked})";
    }

    private static string FormatContentId(ulong contentId)
        => contentId == 0 ? "none" : contentId.ToString("X16");
}
