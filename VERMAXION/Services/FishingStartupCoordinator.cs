using System;
using System.Collections.Generic;
using VERMAXION.Models;

namespace VERMAXION.Services;

public enum FishingStartupTrigger
{
    Clock,
    AutoRetainerPostprocess,
    Manual,
    Test,
    RelogContinuation,
}

public enum FishingStartupAction
{
    None,
    Waiting,
    AlreadyHandled,
    RelogStarted,
    FishingStarted,
}

public sealed record FishingStartupResult(
    FishingStartupTrigger Trigger,
    FishingRunMode Mode,
    FishingStartupAction Action,
    bool WindowActive,
    DateTimeOffset? RegistrationStartUtc,
    FishingSelectionResult Selection,
    string Reason)
{
    public bool ClaimsStartup => WindowActive && Selection.Selected;
    public bool Started => Action is FishingStartupAction.RelogStarted or FishingStartupAction.FishingStarted;
}

public static class FishingStartupDiagnostics
{
    public static string FormatStarted(FishingStartupResult result)
        => $"[Fishing][Startup] trigger={result.Trigger}, registration={result.RegistrationStartUtc:u}, " +
           $"target={result.Selection.CharacterKey}, action={result.Action}, reason={result.Reason}";
}

public interface IFishingStartupRuntime
{
    int PreWindowOffsetMinutes { get; }
    int MaxFisherLevel { get; }
    bool CanInitiateStartup { get; }
    bool IsFishingActive { get; }
    bool IsRelogActive { get; }

    IReadOnlyList<FishingSelectionResult> BuildCandidateQueue();
    FishingLiveFisherLevelSnapshot ReadCurrentFisherLevel();
    bool IsFishingRunOwnedForWindow(DateTimeOffset registrationStartUtc);
    bool IsQueueRegistrationConfirmedForWindow(DateTimeOffset registrationStartUtc);
    bool IsTerminalFailureBeforeQueueConfirmationForWindow(DateTimeOffset registrationStartUtc);
    void ClearFishingWindowOutcome(DateTimeOffset registrationStartUtc);
    bool BeginRun(FishingRunMode mode, string targetCharacterKey, DateTimeOffset registrationStartUtc, DateTimeOffset registrationDeadlineUtc);
    void AbortRun(string reason);
    bool RequestRelog(string characterKey, DateTimeOffset registrationDeadlineUtc);
    bool StartFishing();
}

/// <summary>
/// Coordinates all Ocean Fishing startup triggers and de-duplicates actions by
/// registration window. The relog attempt and fishing-start attempt are tracked
/// separately so a relogged character can start fishing in the same window.
/// </summary>
public sealed class FishingStartupCoordinator
{
    private readonly IFishingStartupRuntime runtime;

    private DateTimeOffset? trackedRegistrationStartUtc;
    private bool relogAttempted;
    private bool fishingStartAttempted;
    private DateTimeOffset? pendingRelogRegistrationStartUtc;
    private DateTimeOffset? pendingRelogRegistrationDeadlineUtc;
    private FishingRunMode pendingRelogMode;
    private string pendingRelogCharacterKey = string.Empty;
    private IReadOnlyList<FishingSelectionResult> orderedCandidates = Array.Empty<FishingSelectionResult>();
    private bool candidateQueueBuilt;
    private bool attemptSequenceStopped;
    private int activeCandidateIndex;
    private int transientRetriesScheduled;
    private DateTimeOffset? recoveryReadyAtUtc;
    private string recoveryReason = string.Empty;
    private FishingRunMode activeMode;
    private DateTimeOffset activeRegistrationStartUtc;
    private DateTimeOffset activeRegistrationDeadlineUtc;
    private DateTimeOffset decisionNowUtc;

    public FishingStartupCoordinator(IFishingStartupRuntime runtime)
    {
        this.runtime = runtime;
    }

    public bool HasPendingRelogContinuation
        => pendingRelogRegistrationStartUtc.HasValue &&
           !string.IsNullOrWhiteSpace(pendingRelogCharacterKey);

    public string PendingRelogCharacterKey => pendingRelogCharacterKey;
    public FishingRunMode? PendingRelogMode => HasPendingRelogContinuation ? pendingRelogMode : null;
    public bool HasRecoveryPending => recoveryReadyAtUtc.HasValue;

    public FishingStartupResult Poll(DateTimeOffset nowUtc, FishingStartupTrigger trigger)
    {
        decisionNowUtc = nowUtc.ToUniversalTime();
        if (!OceanFishingSchedulePolicy.TryGetActiveStartupWindow(
                nowUtc,
                runtime.PreWindowOffsetMinutes,
                out var window))
        {
            return None(
                trigger,
                FishingRunMode.Scheduled,
                OceanFishingSchedulePolicy.DescribeInactiveStartupWindow(
                    nowUtc,
                    runtime.PreWindowOffsetMinutes));
        }

        TrackWindow(window.RegistrationStartUtc);

        if (trigger == FishingStartupTrigger.Clock)
        {
            return Result(
                trigger,
                FishingRunMode.Scheduled,
                FishingStartupAction.None,
                window,
                FishingSelectionResult.None("Ambient clock polling does not start Ocean Fishing."),
                "Ambient clock polling does not start Ocean Fishing.");
        }

        EnsureCandidateQueue(FishingRunMode.Scheduled, window);
        var selection = GetActiveCandidate();
        if (attemptSequenceStopped)
        {
            return Result(
                trigger,
                FishingRunMode.Scheduled,
                FishingStartupAction.AlreadyHandled,
                window,
                selection,
                "Ocean Fishing recovery is terminal for this registration window.");
        }

        if (!selection.Selected)
        {
            return new FishingStartupResult(
                trigger,
                FishingRunMode.Scheduled,
                FishingStartupAction.None,
                WindowActive: true,
                window.RegistrationStartUtc,
                selection,
                selection.Reason);
        }

        if (selection.RequiresRelog)
            return TryStartRelog(trigger, FishingRunMode.Scheduled, window, selection, mutateScheduledGuards: true);

        return TryStartFishing(trigger, FishingRunMode.Scheduled, window, selection, mutateScheduledGuards: true);
    }

    public FishingStartupResult StartTest(DateTimeOffset nowUtc)
    {
        decisionNowUtc = nowUtc.ToUniversalTime();
        var registration = OceanFishingSchedulePolicy.GetCurrentOrNextRegistrationWindow(nowUtc);
        var window = new OceanFishingStartupWindow(registration.StartUtc, nowUtc.ToUniversalTime(), registration.EndUtc);
        orderedCandidates = runtime.BuildCandidateQueue();
        candidateQueueBuilt = true;
        activeCandidateIndex = 0;
        transientRetriesScheduled = 0;
        attemptSequenceStopped = false;
        var selection = GetActiveCandidate();
        if (!selection.Selected)
        {
            return new FishingStartupResult(
                FishingStartupTrigger.Test,
                FishingRunMode.Test,
                FishingStartupAction.None,
                WindowActive: true,
                registration.StartUtc,
                selection,
                selection.Reason);
        }

        return selection.RequiresRelog
            ? TryStartRelog(FishingStartupTrigger.Test, FishingRunMode.Test, window, selection, mutateScheduledGuards: false)
            : TryStartFishing(FishingStartupTrigger.Test, FishingRunMode.Test, window, selection, mutateScheduledGuards: false);
    }

    public void SuppressCurrentWindow(DateTimeOffset nowUtc)
    {
        if (!OceanFishingSchedulePolicy.TryGetActiveStartupWindow(
                nowUtc,
                runtime.PreWindowOffsetMinutes,
                out var window))
        {
            return;
        }

        TrackWindow(window.RegistrationStartUtc);
        relogAttempted = true;
        fishingStartAttempted = true;
        attemptSequenceStopped = true;
        ClearPendingRelogContinuation();
    }

    public void ResetCurrentWindow(DateTimeOffset nowUtc, bool clearPendingRelogContinuation = true)
    {
        if (OceanFishingSchedulePolicy.TryGetActiveStartupWindow(
                nowUtc,
                runtime.PreWindowOffsetMinutes,
                out var window))
        {
            trackedRegistrationStartUtc = window.RegistrationStartUtc;
            runtime.ClearFishingWindowOutcome(window.RegistrationStartUtc);
        }
        else
        {
            trackedRegistrationStartUtc = null;
        }

        relogAttempted = false;
        fishingStartAttempted = false;
        if (clearPendingRelogContinuation)
        {
            var hadPendingRelogContinuation = HasPendingRelogContinuation;
            ClearPendingRelogContinuation();
            if (hadPendingRelogContinuation)
                runtime.AbortRun("pending relog continuation reset");
            orderedCandidates = Array.Empty<FishingSelectionResult>();
            candidateQueueBuilt = false;
            activeCandidateIndex = 0;
            transientRetriesScheduled = 0;
            recoveryReadyAtUtc = null;
            attemptSequenceStopped = false;
        }
    }

    public void CancelPendingRun()
    {
        if (!HasPendingRelogContinuation)
            return;

        ClearPendingRelogContinuation();
        runtime.AbortRun("pending relog continuation cancelled");
    }

    public FishingStartupResult ContinuePendingRelog(DateTimeOffset nowUtc, string currentCharacterKey)
    {
        decisionNowUtc = nowUtc.ToUniversalTime();
        if (!HasPendingRelogContinuation)
            return None(FishingStartupTrigger.RelogContinuation, FishingRunMode.Scheduled, "No pending VERMAXION fishing relog continuation.");

        var pendingStart = pendingRelogRegistrationStartUtc!.Value;
        var pendingDeadline = pendingRelogRegistrationDeadlineUtc!.Value;
        OceanFishingStartupWindow window = default;
        var continuationValid = pendingRelogMode == FishingRunMode.Test
            ? nowUtc.ToUniversalTime() < pendingDeadline
            : OceanFishingSchedulePolicy.TryGetActiveStartupWindow(
                  nowUtc,
                  runtime.PreWindowOffsetMinutes,
                  out window) && pendingStart == window.RegistrationStartUtc;

        if (pendingRelogMode == FishingRunMode.Test)
            window = new OceanFishingStartupWindow(pendingStart, nowUtc.ToUniversalTime(), pendingDeadline);

        if (!continuationValid)
        {
            var pendingRegistration = pendingRelogRegistrationStartUtc;
            var expiredMode = pendingRelogMode;
            ClearPendingRelogContinuation();
            runtime.AbortRun("pending relog continuation expired");
            return None(
                FishingStartupTrigger.RelogContinuation,
                expiredMode,
                $"Pending fishing relog continuation expired; pending registration was {pendingRegistration:u}.");
        }

        if (pendingRelogMode == FishingRunMode.Scheduled)
            TrackWindow(window.RegistrationStartUtc);
        var selection = GetActiveCandidate(currentCharacterKey) with
        {
            CharacterKey = pendingRelogCharacterKey,
            RequiresRelog = false,
            Reason = "Continuing explicit VERMAXION fishing relog.",
        };

        if (!string.Equals(currentCharacterKey, pendingRelogCharacterKey, StringComparison.OrdinalIgnoreCase))
        {
            return Result(
                FishingStartupTrigger.RelogContinuation,
                pendingRelogMode,
                FishingStartupAction.Waiting,
                window,
                selection,
                $"Waiting for fishing relog target {pendingRelogCharacterKey}; current character is {currentCharacterKey}.");
        }

        return TryStartFishing(
            FishingStartupTrigger.RelogContinuation,
            pendingRelogMode,
            window,
            selection,
            mutateScheduledGuards: pendingRelogMode == FishingRunMode.Scheduled,
            requireExistingRunOwnership: true);
    }

    private FishingStartupResult TryStartRelog(
        FishingStartupTrigger trigger,
        FishingRunMode mode,
        OceanFishingStartupWindow window,
        FishingSelectionResult selection,
        bool mutateScheduledGuards)
    {
        if (mutateScheduledGuards && relogAttempted)
            return Result(trigger, mode, FishingStartupAction.AlreadyHandled, window, selection,
                $"Relog was already attempted for the {window.RegistrationStartUtc:u} registration window.");

        if (!FishingRecoveryPolicy.CanStartAttempt(decisionNowUtc, window.EndUtc))
            return Result(trigger, mode, FishingStartupAction.AlreadyHandled, window, selection,
                "Less than 60 seconds remain in the registration window; no attempt will start.");

        if (!runtime.CanInitiateStartup)
            return Result(trigger, mode, FishingStartupAction.Waiting, window, selection,
                "Waiting for the current character and VERMAXION engine to become ready.");

        if (runtime.IsRelogActive)
            return Result(trigger, mode, FishingStartupAction.Waiting, window, selection,
                "A fishing relog sequence is already active.");

        if (!runtime.BeginRun(mode, selection.CharacterKey, window.RegistrationStartUtc, window.EndUtc))
            return Result(trigger, mode, FishingStartupAction.Waiting, window, selection,
                "Could not acquire Ocean Fishing run ownership; polling will retry.");
        if (!runtime.RequestRelog(selection.CharacterKey, window.EndUtc))
        {
            runtime.AbortRun("relog request rejected");
            return Result(trigger, mode, FishingStartupAction.Waiting, window, selection,
                $"Could not start relog to {selection.CharacterKey}; polling will retry.");
        }

        if (mutateScheduledGuards)
            relogAttempted = true;
        CaptureActiveRun(mode, window);
        RecordPendingRelogContinuation(selection.CharacterKey, mode, window.RegistrationStartUtc, window.EndUtc);
        return Result(trigger, mode, FishingStartupAction.RelogStarted, window, selection,
            $"Selected {selection.CharacterKey} (Fisher {FormatLevel(selection.FisherLevel)}) and started relog.");
    }

    private FishingStartupResult TryStartFishing(
        FishingStartupTrigger trigger,
        FishingRunMode mode,
        OceanFishingStartupWindow window,
        FishingSelectionResult selection,
        bool mutateScheduledGuards,
        bool requireExistingRunOwnership = false)
    {
        if (!FishingRecoveryPolicy.CanStartAttempt(decisionNowUtc, window.EndUtc))
        {
            if (requireExistingRunOwnership)
            {
                ClearPendingRelogContinuation();
                runtime.AbortRun("pending relog continuation has insufficient registration time");
                return Result(trigger, mode, FishingStartupAction.None, window, selection,
                    "Less than 60 seconds remain in the registration window; the pending relog continuation was cleaned up.");
            }

            return Result(trigger, mode, FishingStartupAction.AlreadyHandled, window, selection,
                "Less than 60 seconds remain in the registration window; no attempt will start.");
        }

        if (mutateScheduledGuards &&
            runtime.IsTerminalFailureBeforeQueueConfirmationForWindow(window.RegistrationStartUtc))
        {
            ClearPendingRelogContinuation();
            runtime.AbortRun("terminal fishing prep failure before queue confirmation");
            return Result(trigger, mode,
                requireExistingRunOwnership ? FishingStartupAction.None : FishingStartupAction.AlreadyHandled,
                window,
                selection,
                $"Fishing prep reached a terminal failure for the {window.RegistrationStartUtc:u} registration window.");
        }

        if (mutateScheduledGuards && fishingStartAttempted)
        {
            if (runtime.IsQueueRegistrationConfirmedForWindow(window.RegistrationStartUtc))
            {
                ClearPendingRelogContinuation();
                return Result(trigger, mode, FishingStartupAction.AlreadyHandled, window, selection,
                    $"Ocean Fishing queue registration was already confirmed for the {window.RegistrationStartUtc:u} registration window.");
            }

            fishingStartAttempted = false;
        }

        if (runtime.IsFishingActive)
        {
            if (mutateScheduledGuards)
                fishingStartAttempted = true;
            ClearPendingRelogContinuation();
            return Result(trigger, mode, FishingStartupAction.AlreadyHandled, window, selection,
                "Fishing prep is already active.");
        }

        if (runtime.IsQueueRegistrationConfirmedForWindow(window.RegistrationStartUtc))
        {
            if (mutateScheduledGuards)
                fishingStartAttempted = true;
            ClearPendingRelogContinuation();
            return Result(trigger, mode, FishingStartupAction.AlreadyHandled, window, selection,
                $"Ocean Fishing queue registration was already confirmed for the {window.RegistrationStartUtc:u} registration window.");
        }

        if (runtime.IsRelogActive)
            return Result(trigger, mode, FishingStartupAction.Waiting, window, selection,
                "Waiting for the fishing relog sequence to finish.");

        if (!runtime.CanInitiateStartup)
            return Result(trigger, mode, FishingStartupAction.Waiting, window, selection,
                "Waiting for the current character and VERMAXION engine to become ready.");

        var runOwnedForWindow = runtime.IsFishingRunOwnedForWindow(window.RegistrationStartUtc);
        if (requireExistingRunOwnership && !runOwnedForWindow)
        {
            ClearPendingRelogContinuation();
            runtime.AbortRun("pending relog continuation lost lifecycle ownership");
            return Result(trigger, mode, FishingStartupAction.None, window, selection,
                "Pending fishing relog continuation lost its Ocean Fishing run ownership and was cleaned up.");
        }

        var liveFisherLevel = runtime.ReadCurrentFisherLevel();
        if (!liveFisherLevel.IsAvailable)
        {
            recoveryReason =
                $"Live Fisher level is unavailable; fishing preparation remains blocked. {liveFisherLevel.Detail}".Trim();
            recoveryReadyAtUtc = decisionNowUtc;
            return Result(
                trigger,
                mode,
                FishingStartupAction.Waiting,
                window,
                selection,
                recoveryReason);
        }

        selection = selection with { FisherLevel = liveFisherLevel.Level };
        var cappedMaxLevel = Math.Clamp(runtime.MaxFisherLevel, 1, 100);
        if (liveFisherLevel.Level >= cappedMaxLevel && !selection.AlwaysFishOverride)
        {
            return RejectCurrentCandidateAtLiveCap(
                trigger,
                mode,
                window,
                selection,
                cappedMaxLevel,
                runOwnedForWindow);
        }

        if (!runOwnedForWindow &&
            !runtime.BeginRun(mode, selection.CharacterKey, window.RegistrationStartUtc, window.EndUtc))
        {
            return Result(trigger, mode, FishingStartupAction.Waiting, window, selection,
                "Could not acquire Ocean Fishing run ownership; polling will retry.");
        }

        if (!runtime.StartFishing())
        {
            if (requireExistingRunOwnership)
                ClearPendingRelogContinuation();
            runtime.AbortRun("fishing service start rejected");
            return Result(trigger, mode,
                requireExistingRunOwnership ? FishingStartupAction.None : FishingStartupAction.Waiting,
                window,
                selection,
                requireExistingRunOwnership
                    ? "Could not restart fishing prep after relog; the owned run was cleaned up."
                    : "Could not start fishing prep; polling will retry.");
        }

        if (mutateScheduledGuards)
            fishingStartAttempted = true;
        CaptureActiveRun(mode, window);
        ClearPendingRelogContinuation();
        return Result(trigger, mode, FishingStartupAction.FishingStarted, window, selection,
            $"Selected current character {selection.CharacterKey} (Fisher {FormatLevel(selection.FisherLevel)}) and started fishing prep.");
    }

    private FishingStartupResult RejectCurrentCandidateAtLiveCap(
        FishingStartupTrigger trigger,
        FishingRunMode mode,
        OceanFishingStartupWindow window,
        FishingSelectionResult selection,
        int cappedMaxLevel,
        bool runOwnedForWindow)
    {
        var reason =
            $"Live Fisher level {selection.FisherLevel} is at or above configured cap {cappedMaxLevel}; " +
            "the cached XADB candidate was rejected before fishing preparation.";

        ClearPendingRelogContinuation();
        if (runOwnedForWindow)
            runtime.AbortRun(reason);

        activeCandidateIndex++;
        transientRetriesScheduled = 0;
        recoveryReason = reason;
        fishingStartAttempted = false;

        if (activeCandidateIndex >= orderedCandidates.Count ||
            !FishingRecoveryPolicy.CanStartAttempt(decisionNowUtc, window.EndUtc))
        {
            recoveryReadyAtUtc = null;
            attemptSequenceStopped = true;
            return Result(
                trigger,
                mode,
                FishingStartupAction.AlreadyHandled,
                window,
                selection,
                $"{reason} No eligible cached candidate remains for this registration window.");
        }

        recoveryReadyAtUtc = decisionNowUtc;
        return Result(
            trigger,
            mode,
            FishingStartupAction.Waiting,
            window,
            selection,
            $"{reason} Advancing to the next cached candidate.");
    }

    private void TrackWindow(DateTimeOffset registrationStartUtc)
    {
        if (trackedRegistrationStartUtc == registrationStartUtc)
            return;

        trackedRegistrationStartUtc = registrationStartUtc;
        relogAttempted = false;
        fishingStartAttempted = false;
        orderedCandidates = Array.Empty<FishingSelectionResult>();
        candidateQueueBuilt = false;
        activeCandidateIndex = 0;
        transientRetriesScheduled = 0;
        recoveryReadyAtUtc = null;
        attemptSequenceStopped = false;

        if (pendingRelogRegistrationStartUtc.HasValue &&
            pendingRelogRegistrationStartUtc != registrationStartUtc)
        {
            ClearPendingRelogContinuation();
            runtime.AbortRun("pending relog continuation moved outside its registration window");
        }
    }

    private void RecordPendingRelogContinuation(
        string characterKey,
        FishingRunMode mode,
        DateTimeOffset registrationStartUtc,
        DateTimeOffset registrationDeadlineUtc)
    {
        pendingRelogCharacterKey = characterKey.Trim();
        pendingRelogMode = mode;
        pendingRelogRegistrationStartUtc = registrationStartUtc;
        pendingRelogRegistrationDeadlineUtc = registrationDeadlineUtc;
    }

    private void ClearPendingRelogContinuation()
    {
        pendingRelogCharacterKey = string.Empty;
        pendingRelogRegistrationStartUtc = null;
        pendingRelogRegistrationDeadlineUtc = null;
        pendingRelogMode = FishingRunMode.Scheduled;
    }

    public void ReportAttemptFailure(
        DateTimeOffset nowUtc,
        FishingAttemptFailureKind failureKind,
        string reason,
        bool queueConfirmed)
    {
        ClearPendingRelogContinuation();
        runtime.AbortRun(reason);

        var now = nowUtc.ToUniversalTime();
        var registrationOpen = activeRegistrationDeadlineUtc != default && now < activeRegistrationDeadlineUtc;
        if (!FishingRecoveryPolicy.MayRecover(
                failureKind,
                queueConfirmed,
                registrationOpen,
                transientRetriesScheduled))
        {
            recoveryReadyAtUtc = null;
            recoveryReason = reason;
            attemptSequenceStopped = true;
            return;
        }

        recoveryReason = reason;
        if (failureKind == FishingAttemptFailureKind.CharacterPermanent)
        {
            activeCandidateIndex++;
            transientRetriesScheduled = 0;
            recoveryReadyAtUtc = now;
        }
        else
        {
            transientRetriesScheduled++;
            recoveryReadyAtUtc = now + FishingRecoveryPolicy.GetTransientBackoff(transientRetriesScheduled);
        }

        if (activeCandidateIndex >= orderedCandidates.Count)
        {
            recoveryReadyAtUtc = null;
            attemptSequenceStopped = true;
        }
    }

    public FishingStartupResult PollRecovery(DateTimeOffset nowUtc, string currentCharacterKey)
    {
        decisionNowUtc = nowUtc.ToUniversalTime();
        if (!recoveryReadyAtUtc.HasValue)
            return None(FishingStartupTrigger.RelogContinuation, activeMode, "No automatic fishing recovery is pending.");

        var now = nowUtc.ToUniversalTime();
        var selection = GetActiveCandidate(currentCharacterKey);
        var window = new OceanFishingStartupWindow(
            activeRegistrationStartUtc,
            activeRegistrationStartUtc,
            activeRegistrationDeadlineUtc);

        if (!selection.Selected)
        {
            recoveryReadyAtUtc = null;
            return Result(
                FishingStartupTrigger.RelogContinuation,
                activeMode,
                FishingStartupAction.AlreadyHandled,
                window,
                selection,
                "No remaining Ocean Fishing candidate is eligible.");
        }

        if (!FishingRecoveryPolicy.CanStartAttempt(now, activeRegistrationDeadlineUtc))
        {
            recoveryReadyAtUtc = null;
            return Result(
                FishingStartupTrigger.RelogContinuation,
                activeMode,
                FishingStartupAction.AlreadyHandled,
                window,
                selection,
                "Less than 60 seconds remain in the registration window; recovery stopped.");
        }

        if (now < recoveryReadyAtUtc.Value)
        {
            return Result(
                FishingStartupTrigger.RelogContinuation,
                activeMode,
                FishingStartupAction.Waiting,
                window,
                selection,
                $"Waiting for recovery backoff until {recoveryReadyAtUtc:u}: {recoveryReason}");
        }

        if (!runtime.CanInitiateStartup || runtime.IsRelogActive)
        {
            return Result(
                FishingStartupTrigger.RelogContinuation,
                activeMode,
                FishingStartupAction.Waiting,
                window,
                selection,
                "Waiting for cleanup and client readiness before automatic fishing recovery.");
        }

        var result = selection.RequiresRelog
            ? TryStartRelog(
                FishingStartupTrigger.RelogContinuation,
                activeMode,
                window,
                selection,
                mutateScheduledGuards: false)
            : TryStartFishing(
                FishingStartupTrigger.RelogContinuation,
                activeMode,
                window,
                selection,
                mutateScheduledGuards: false);
        if (result.Started)
            recoveryReadyAtUtc = null;
        else if (!attemptSequenceStopped && !recoveryReadyAtUtc.HasValue)
            recoveryReadyAtUtc = now;
        return result;
    }

    private void EnsureCandidateQueue(FishingRunMode mode, OceanFishingStartupWindow window)
    {
        if (candidateQueueBuilt &&
            activeRegistrationStartUtc == window.RegistrationStartUtc &&
            activeMode == mode)
        {
            return;
        }

        orderedCandidates = runtime.BuildCandidateQueue();
        candidateQueueBuilt = true;
        activeCandidateIndex = 0;
        transientRetriesScheduled = 0;
        attemptSequenceStopped = false;
        CaptureActiveRun(mode, window);
    }

    private FishingSelectionResult GetActiveCandidate(string? currentCharacterKey = null)
    {
        if (activeCandidateIndex < 0 || activeCandidateIndex >= orderedCandidates.Count)
            return FishingSelectionResult.None("No eligible Ocean Fishing candidates remain.");

        var selection = orderedCandidates[activeCandidateIndex];
        if (currentCharacterKey == null)
            return selection;

        return selection with
        {
            RequiresRelog = !string.Equals(
                selection.CharacterKey,
                currentCharacterKey,
                StringComparison.OrdinalIgnoreCase),
        };
    }

    private void CaptureActiveRun(FishingRunMode mode, OceanFishingStartupWindow window)
    {
        activeMode = mode;
        activeRegistrationStartUtc = window.RegistrationStartUtc;
        activeRegistrationDeadlineUtc = window.EndUtc;
    }

    private static string FormatLevel(int? fisherLevel)
        => fisherLevel?.ToString() ?? "unknown";

    private static FishingStartupResult None(FishingStartupTrigger trigger, FishingRunMode mode, string reason)
        => new(
            trigger,
            mode,
            FishingStartupAction.None,
            WindowActive: false,
            RegistrationStartUtc: null,
            FishingSelectionResult.None(reason),
            reason);

    private static FishingStartupResult Result(
        FishingStartupTrigger trigger,
        FishingRunMode mode,
        FishingStartupAction action,
        OceanFishingStartupWindow window,
        FishingSelectionResult selection,
        string reason)
        => new(
            trigger,
            mode,
            action,
            WindowActive: true,
            window.RegistrationStartUtc,
            selection,
            reason);
}
