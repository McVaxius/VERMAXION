using System;
using System.Collections.Generic;
using System.Linq;

namespace VERMAXION.Models;

public enum RunOutcome
{
    None,
    Succeeded,
    PartialFailure,
    Failed,
    Cancelled,
    Skipped,
    ForceStopped,
}

public enum ARPostProcessFinishMode
{
    Normal,
    ReleaseOnly,
}

public enum BeforeArGateState
{
    Idle,
    Armed,
    WaitingForWorldReady,
    Running,
    ReleasePending,
    Skipped,
}

public readonly record struct SuppressionReadResult(bool Success, bool IsSuppressed, string Error)
{
    public static SuppressionReadResult Known(bool isSuppressed) => new(true, isSuppressed, string.Empty);
    public static SuppressionReadResult Unknown(string error) => new(false, false, error);
}

public readonly record struct SuppressionSnapshot(bool RemoteKnown, bool RemoteSuppressed, bool OwnedByVermaxion)
{
    public override string ToString()
        => $"remote={(RemoteKnown ? RemoteSuppressed.ToString() : "Unknown")}, owned={OwnedByVermaxion}";
}

public enum SuppressionLeaseAction
{
    None,
    Acquire,
    Release,
    ClearStaleOwnership,
    PreserveExternal,
    WaitForRemote,
}

internal static class LifecyclePolicy
{
    public static bool CanStart(bool isRunning) => !isRunning;

    public static bool RequiresSettling(bool ownedWorkStarted) => ownedWorkStarted;

    public static bool ShouldSkipBeforeArForTimeout(TimeSpan elapsed, bool workStarted, TimeSpan timeout)
        => !workStarted && elapsed >= timeout;

    public static List<string> BuildRunnableQueue(
        IEnumerable<string> orderedTaskIds,
        Func<string, bool> belongsToPhase,
        Func<string, bool> isRunnable)
    {
        return orderedTaskIds
            .Where(belongsToPhase)
            .Where(isRunnable)
            .ToList();
    }
}

internal readonly record struct BeforeArArmDecision(bool ShouldArm, string Reason);

internal static class BeforeArArmPolicy
{
    public const string MultiModeOffReason = "MultiMode off";
    public const string MultiModeUnreadableReason = "MultiMode unreadable";
    public const string NoDueWorkReason = "no due Before-AR work";

    public static BeforeArArmDecision Evaluate(
        bool multiModeReadSucceeded,
        bool multiModeEnabled,
        bool characterEnabled,
        int dueBeforeArTaskCount)
    {
        if (!multiModeReadSucceeded)
            return new BeforeArArmDecision(false, MultiModeUnreadableReason);
        if (!multiModeEnabled)
            return new BeforeArArmDecision(false, MultiModeOffReason);
        if (!characterEnabled || dueBeforeArTaskCount <= 0)
            return new BeforeArArmDecision(false, NoDueWorkReason);

        return new BeforeArArmDecision(true, "ready");
    }
}

internal static class BeforeArArmedStallPolicy
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);

    public static bool ShouldRelease(
        DateTime nowUtc,
        DateTime armedAtUtc,
        BeforeArGateState gateState,
        bool suppressionOwnedByVermaxion,
        bool engineOwnsActiveWork,
        bool loginTransitionStarted,
        bool autoRetainerBusy,
        TimeSpan? timeout = null)
    {
        return gateState == BeforeArGateState.Armed &&
               armedAtUtc != DateTime.MinValue &&
               suppressionOwnedByVermaxion &&
               !engineOwnsActiveWork &&
               !loginTransitionStarted &&
               autoRetainerBusy &&
               nowUtc - armedAtUtc >= (timeout ?? DefaultTimeout);
    }
}

internal static class ARPostProcessFinishPolicy
{
    public static bool ShouldRunBeforeFinishCallback(ARPostProcessFinishMode mode)
        => mode == ARPostProcessFinishMode.Normal;
}

internal static class SuppressionLeasePolicy
{
    public static SuppressionLeaseAction DecideAcquire(bool ownedByVermaxion, SuppressionReadResult remote)
    {
        if (!remote.Success)
            return SuppressionLeaseAction.WaitForRemote;
        if (ownedByVermaxion && remote.IsSuppressed)
            return SuppressionLeaseAction.None;
        if (ownedByVermaxion)
            return SuppressionLeaseAction.Acquire;
        if (remote.IsSuppressed)
            return SuppressionLeaseAction.PreserveExternal;

        return SuppressionLeaseAction.Acquire;
    }

    public static SuppressionLeaseAction DecideRelease(bool ownedByVermaxion, SuppressionReadResult remote)
    {
        if (!ownedByVermaxion)
            return remote.Success && remote.IsSuppressed
                ? SuppressionLeaseAction.PreserveExternal
                : SuppressionLeaseAction.None;
        if (!remote.Success)
            return SuppressionLeaseAction.WaitForRemote;
        if (!remote.IsSuppressed)
            return SuppressionLeaseAction.ClearStaleOwnership;

        return SuppressionLeaseAction.Release;
    }
}
