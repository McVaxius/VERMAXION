using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace VERMAXION.Models;

public enum FishingExecutionMode
{
    CurrentCharacterOnly = 0,
    AutoRetainerRelogCurrentAccount = 1,
}

public enum FishingRunMode
{
    Scheduled = 0,
    Test = 1,
}

public sealed class FishingRunContext
{
    public FishingRunMode Mode { get; init; }
    public string TargetCharacterKey { get; init; } = string.Empty;
    public DateTimeOffset RegistrationStartUtc { get; init; }
    public DateTimeOffset RegistrationDeadlineUtc { get; init; }
    public bool QueueRegistrationConfirmed { get; set; }
    public bool TerminalFailureBeforeQueueConfirmation { get; set; }
    public bool? InitialAutoRetainerMultiModeEnabled { get; set; }
    public bool AutoRetainerMultiModeChanged { get; set; }
    public bool? InitialAutoHookEnabled { get; set; }
    public bool AutoHookChanged { get; set; }
    public bool YesAlreadyLeaseOwned { get; set; }
    public bool CleanupPending { get; set; }
    public string CleanupReason { get; set; } = string.Empty;
    public DateTimeOffset LastCleanupAttemptUtc { get; set; }

    public bool OwnsExternalState
        => AutoRetainerMultiModeChanged || AutoHookChanged || YesAlreadyLeaseOwned;
    public bool OwnsRegistrationLeases
        => AutoRetainerMultiModeChanged || YesAlreadyLeaseOwned;

    public string StatusPrefix => Mode == FishingRunMode.Test ? "Test: " : string.Empty;
}

public enum FishingReturnDestination
{
    None = 0,
    Home = 1,
    Limsa = 2,
    FreeCompany = 3,
    Custom = 4,
}

public enum FishingRepairMode
{
    Disabled = 0,
    Self = 1,
    NpcNoInn = 2,
    NpcNoTeleportNoInn = 3,
}

public static class FishingDefaults
{
    public const FishingExecutionMode ExecutionMode = FishingExecutionMode.AutoRetainerRelogCurrentAccount;
    public const int MaxFisherLevel = 100;
    public const int OceanFishingPreWindowOffsetMinutes = -1;
    public const int MinOceanFishingPreWindowOffsetMinutes = -10;
    public const int MaxOceanFishingPreWindowOffsetMinutes = 0;
    public const int OceanFishingRegistrationIntervalHours = 2;
    public const int OceanFishingRegistrationAvailabilityMinutes = 15;
    public const int LureRestockTarget = 22;
    public const FishingReturnDestination ReturnDestination = FishingReturnDestination.FreeCompany;
    public const string ReturnCommand = "/li fc";
    public const FishingRepairMode RepairMode = FishingRepairMode.NpcNoInn;
    public const int RepairThresholdPercent = 50;
}

public readonly record struct OceanFishingRailDestination(
    Vector3 Position,
    float Rotation);

internal static class OceanFishingFixedRailPolicy
{
    public const float CrowdRadius = 2f;

    public static IReadOnlyList<OceanFishingRailDestination> CanonicalDestinations { get; } =
        Array.AsReadOnly(new[]
        {
            new OceanFishingRailDestination(new Vector3(7.20f, 6.711f, -2.0f), 1.5f),
            new OceanFishingRailDestination(new Vector3(7.20f, 6.711f, -10.0f), 1.5f),
            new OceanFishingRailDestination(new Vector3(7.20f, 6.711f, 3.5f), 1.5f),
            new OceanFishingRailDestination(new Vector3(-7.20f, 6.711f, -1.0f), -1.5f),
            new OceanFishingRailDestination(new Vector3(-7.20f, 6.711f, -8.0f), -1.5f),
            new OceanFishingRailDestination(new Vector3(-7.20f, 6.711f, 4.0f), -1.5f),
        });

    public static IReadOnlyList<OceanFishingRailDestination> Rank(
        IReadOnlyList<Vector3> otherPlayerPositions)
    {
        ArgumentNullException.ThrowIfNull(otherPlayerPositions);

        var players = otherPlayerPositions
            .Where(IsFinite)
            .ToArray();
        return CanonicalDestinations
            .Select((destination, canonicalIndex) => new RankedRail(
                destination,
                canonicalIndex,
                players.Length == 0
                    ? float.PositiveInfinity
                    : players.Min(player => Vector3.Distance(player, destination.Position))))
            .OrderByDescending(candidate => candidate.PlayerClearance >= CrowdRadius)
            .ThenByDescending(candidate => candidate.PlayerClearance)
            .ThenBy(candidate => candidate.CanonicalIndex)
            .Select(candidate => candidate.Destination)
            .ToArray();
    }

    private static bool IsFinite(Vector3 position)
        => float.IsFinite(position.X) &&
           float.IsFinite(position.Y) &&
           float.IsFinite(position.Z);

    private readonly record struct RankedRail(
        OceanFishingRailDestination Destination,
        int CanonicalIndex,
        float PlayerClearance);
}

internal readonly record struct OceanFishingStartEvaluation(
    FishingCastDecision Decision,
    string Gate,
    bool StopNavigation);

internal enum OceanFishingAdvanceReason
{
    None = 0,
    NavigationStalled = 1,
    NavigationTimeout = 2,
    CannotFish = 3,
    StartUnacknowledged = 4,
}

internal sealed class OceanFishingVoyageState
{
    private DateTimeOffset? lastStartAttemptAt;
    private DateTimeOffset? lastRecoveryObservationAt;
    private DateTimeOffset? arrivalAt;
    private DateTimeOffset? lastFacingReapplyAt;
    private TimeSpan destinationNavigationTime;
    private TimeSpan noProgressTime;
    private TimeSpan canFishFalseTime;
    private float bestDistance;
    private bool baitAppliedThisSession;

    public bool FishingEverStarted { get; private set; }
    public bool MovementLocked { get; private set; }
    public bool DestinationArrived { get; private set; }
    public int DestinationCount { get; private set; }
    public int DestinationIndex { get; private set; }
    public int SessionNumber { get; private set; }
    public int SessionStartAttemptCount { get; private set; }
    public int PostArrivalStartAttemptCount { get; private set; }

    public static readonly TimeSpan NavigationStallDelay = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan NavigationTimeout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan FacingSettlementDelay = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan FacingRetryInterval = TimeSpan.FromSeconds(1);
    public const float MinimumNavigationProgress = 0.25f;
    public const int PostArrivalAttemptLimit = 5;

    public void Reset()
    {
        lastStartAttemptAt = null;
        baitAppliedThisSession = false;
        FishingEverStarted = false;
        MovementLocked = false;
        DestinationCount = 0;
        DestinationIndex = 0;
        SessionNumber = 0;
        SessionStartAttemptCount = 0;
        ResetDestinationRecovery(DateTimeOffset.MinValue);
    }

    public void BeginPositioning(int destinationCount, DateTimeOffset nowUtc)
    {
        if (destinationCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(destinationCount));

        DestinationCount = destinationCount;
        DestinationIndex = 0;
        ResetDestinationRecovery(nowUtc);
    }

    public bool AdvanceDestination(DateTimeOffset nowUtc)
    {
        if (MovementLocked || FishingEverStarted || DestinationCount <= 0)
            return false;

        DestinationIndex = (DestinationIndex + 1) % DestinationCount;
        ResetDestinationRecovery(nowUtc);
        return true;
    }

    public void BeginSession()
    {
        SessionNumber++;
        SessionStartAttemptCount = 0;
        lastStartAttemptAt = null;
        baitAppliedThisSession = false;
    }

    public bool TryApplySessionBait()
    {
        if (baitAppliedThisSession)
            return false;

        baitAppliedThisSession = true;
        return true;
    }

    public OceanFishingStartEvaluation EvaluateFishingStart(
        DateTimeOffset nowUtc,
        bool enabled,
        bool inFishingContext,
        bool zoneTransitionActive,
        bool playerAvailable,
        bool gatheringConditionActive,
        bool fishingConditionActive,
        bool resultWindowVisible,
        bool atDestination = false)
    {
        var evaluation = FishingCastPolicy.Evaluate(
            enabled,
            inFishingContext,
            zoneTransitionActive,
            playerAvailable,
            gatheringConditionActive,
            fishingConditionActive,
            resultWindowVisible,
            lastStartAttemptAt.HasValue
                ? nowUtc - lastStartAttemptAt.Value
                : TimeSpan.MaxValue);

        if (evaluation.Decision == FishingCastDecision.Attempt)
        {
            lastStartAttemptAt = nowUtc;
            SessionStartAttemptCount++;
            if (atDestination && DestinationArrived)
                PostArrivalStartAttemptCount++;
        }

        var stopNavigation = false;
        if (evaluation.Decision == FishingCastDecision.Acknowledged)
        {
            stopNavigation = !MovementLocked;
            FishingEverStarted = true;
            MovementLocked = true;
        }

        return new OceanFishingStartEvaluation(
            evaluation.Decision,
            evaluation.Gate,
            stopNavigation);
    }

    public void PauseRecovery(DateTimeOffset nowUtc)
        => lastRecoveryObservationAt = nowUtc;

    public void MarkArrived(DateTimeOffset nowUtc)
    {
        if (DestinationArrived)
            return;

        DestinationArrived = true;
        arrivalAt = nowUtc;
        lastFacingReapplyAt = null;
        canFishFalseTime = TimeSpan.Zero;
        PostArrivalStartAttemptCount = 0;
        lastRecoveryObservationAt = nowUtc;
    }

    public bool ShouldReapplyFacing(DateTimeOffset nowUtc)
    {
        if (!DestinationArrived || FishingEverStarted || MovementLocked ||
            !arrivalAt.HasValue || nowUtc - arrivalAt.Value < FacingSettlementDelay)
        {
            return false;
        }

        if (lastFacingReapplyAt.HasValue &&
            nowUtc - lastFacingReapplyAt.Value < FacingRetryInterval)
        {
            return false;
        }

        lastFacingReapplyAt = nowUtc;
        return true;
    }

    public OceanFishingAdvanceReason EvaluateRecovery(
        DateTimeOffset nowUtc,
        float distance,
        bool atDestination,
        bool canFish,
        bool timersPaused)
    {
        if (FishingEverStarted || MovementLocked || DestinationCount <= 0)
            return OceanFishingAdvanceReason.None;

        var delta = TimeSpan.Zero;
        if (lastRecoveryObservationAt.HasValue && nowUtc > lastRecoveryObservationAt.Value)
            delta = nowUtc - lastRecoveryObservationAt.Value;
        lastRecoveryObservationAt = nowUtc;

        if (timersPaused)
            return OceanFishingAdvanceReason.None;

        if (atDestination)
        {
            if (!DestinationArrived)
            {
                MarkArrived(nowUtc);
                delta = TimeSpan.Zero;
            }

            if (canFish)
                canFishFalseTime = TimeSpan.Zero;
            else
                canFishFalseTime += delta;

            if (canFishFalseTime >= FishingCastPolicy.CanFishFallbackDelay)
                return OceanFishingAdvanceReason.CannotFish;
            if (PostArrivalStartAttemptCount >= PostArrivalAttemptLimit)
                return OceanFishingAdvanceReason.StartUnacknowledged;

            return OceanFishingAdvanceReason.None;
        }

        if (DestinationArrived)
        {
            ResetDestinationRecovery(nowUtc);
            bestDistance = distance;
            return OceanFishingAdvanceReason.None;
        }

        destinationNavigationTime += delta;
        if (!float.IsFinite(bestDistance))
        {
            bestDistance = distance;
        }
        else if (float.IsFinite(distance) && bestDistance - distance >= MinimumNavigationProgress)
        {
            bestDistance = distance;
            noProgressTime = TimeSpan.Zero;
        }
        else
        {
            noProgressTime += delta;
        }

        if (noProgressTime >= NavigationStallDelay)
            return OceanFishingAdvanceReason.NavigationStalled;
        if (destinationNavigationTime >= NavigationTimeout)
            return OceanFishingAdvanceReason.NavigationTimeout;

        return OceanFishingAdvanceReason.None;
    }

    private void ResetDestinationRecovery(DateTimeOffset nowUtc)
    {
        lastRecoveryObservationAt = nowUtc == DateTimeOffset.MinValue ? null : nowUtc;
        arrivalAt = null;
        lastFacingReapplyAt = null;
        destinationNavigationTime = TimeSpan.Zero;
        noProgressTime = TimeSpan.Zero;
        canFishFalseTime = TimeSpan.Zero;
        bestDistance = float.PositiveInfinity;
        DestinationArrived = false;
        PostArrivalStartAttemptCount = 0;
    }
}

public readonly record struct OceanFishingStartupWindow(
    DateTimeOffset RegistrationStartUtc,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc);

public readonly record struct OceanFishingRegistrationWindow(
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc);

public static class OceanFishingSchedulePolicy
{
    public static int NormalizePreWindowOffsetMinutes(int offsetMinutes)
        => Math.Clamp(
            offsetMinutes,
            FishingDefaults.MinOceanFishingPreWindowOffsetMinutes,
            FishingDefaults.MaxOceanFishingPreWindowOffsetMinutes);

    public static bool IsStartupWindowActive(DateTimeOffset nowUtc, int preWindowOffsetMinutes)
        => TryGetActiveStartupWindow(nowUtc, preWindowOffsetMinutes, out _);

    public static string DescribeInactiveStartupWindow(DateTimeOffset nowUtc, int preWindowOffsetMinutes)
    {
        var normalizedNow = nowUtc.ToUniversalTime();
        var nextRegistrationStart = GetNextRegistrationStart(normalizedNow);
        var nextWindow = BuildStartupWindow(nextRegistrationStart, preWindowOffsetMinutes);
        return $"No Ocean Fishing startup gate is active at {normalizedNow:u}; " +
               $"next gate is {nextWindow.StartUtc:u} until {nextWindow.EndUtc:u} (end exclusive).";
    }

    public static bool TryGetActiveStartupWindow(
        DateTimeOffset nowUtc,
        int preWindowOffsetMinutes,
        out OceanFishingStartupWindow window)
    {
        var normalizedNow = nowUtc.ToUniversalTime();
        foreach (var registrationStart in GetCandidateRegistrationStarts(normalizedNow))
        {
            var candidate = BuildStartupWindow(registrationStart, preWindowOffsetMinutes);
            if (normalizedNow >= candidate.StartUtc && normalizedNow < candidate.EndUtc)
            {
                window = candidate;
                return true;
            }
        }

        window = default;
        return false;
    }

    public static OceanFishingStartupWindow BuildStartupWindow(
        DateTimeOffset registrationStartUtc,
        int preWindowOffsetMinutes)
    {
        var normalizedRegistrationStart = registrationStartUtc.ToUniversalTime();
        var normalizedOffset = NormalizePreWindowOffsetMinutes(preWindowOffsetMinutes);
        return new OceanFishingStartupWindow(
            normalizedRegistrationStart,
            normalizedRegistrationStart.AddMinutes(normalizedOffset),
            normalizedRegistrationStart.AddMinutes(FishingDefaults.OceanFishingRegistrationAvailabilityMinutes));
    }

    public static OceanFishingRegistrationWindow BuildRegistrationWindow(DateTimeOffset registrationStartUtc)
    {
        var normalizedRegistrationStart = registrationStartUtc.ToUniversalTime();
        return new OceanFishingRegistrationWindow(
            normalizedRegistrationStart,
            normalizedRegistrationStart.AddMinutes(FishingDefaults.OceanFishingRegistrationAvailabilityMinutes));
    }

    public static OceanFishingRegistrationWindow GetCurrentOrNextRegistrationWindow(DateTimeOffset nowUtc)
        => BuildRegistrationWindow(GetNextRegistrationStart(nowUtc.ToUniversalTime()));

    private static DateTimeOffset GetNextRegistrationStart(DateTimeOffset nowUtc)
    {
        var hourStart = new DateTimeOffset(
            nowUtc.Year,
            nowUtc.Month,
            nowUtc.Day,
            nowUtc.Hour,
            0,
            0,
            TimeSpan.Zero);
        var currentEvenHour = hourStart.Hour % FishingDefaults.OceanFishingRegistrationIntervalHours == 0
            ? hourStart
            : hourStart.AddHours(1);

        return nowUtc < BuildStartupWindow(currentEvenHour, 0).EndUtc
            ? currentEvenHour
            : currentEvenHour.AddHours(FishingDefaults.OceanFishingRegistrationIntervalHours);
    }

    private static IEnumerable<DateTimeOffset> GetCandidateRegistrationStarts(DateTimeOffset nowUtc)
    {
        var hourStart = new DateTimeOffset(
            nowUtc.Year,
            nowUtc.Month,
            nowUtc.Day,
            nowUtc.Hour,
            0,
            0,
            TimeSpan.Zero);
        var currentOrPreviousEvenHour = hourStart.Hour % FishingDefaults.OceanFishingRegistrationIntervalHours == 0
            ? hourStart
            : hourStart.AddHours(-1);

        yield return currentOrPreviousEvenHour.AddHours(-FishingDefaults.OceanFishingRegistrationIntervalHours);
        yield return currentOrPreviousEvenHour;
        yield return currentOrPreviousEvenHour.AddHours(FishingDefaults.OceanFishingRegistrationIntervalHours);
    }
}

public readonly record struct FishingOperationSettings(
    int LureRestockTarget,
    FishingReturnDestination ReturnDestination,
    string ReturnCommand,
    FishingRepairMode RepairMode,
    int RepairThresholdPercent);

public readonly record struct FishingCharacterCandidate(
    string CharacterKey,
    int? FisherLevel,
    bool FishingEnabled,
    bool AlwaysFishIfWindowOpen,
    bool IsCurrentCharacter);

public sealed record FishingSelectionResult(
    string CharacterKey,
    int? FisherLevel,
    bool RequiresRelog,
    IReadOnlyList<string> AlwaysFishKeysToDisable,
    string Reason)
{
    public bool Selected => !string.IsNullOrWhiteSpace(CharacterKey);

    public static FishingSelectionResult None(string reason)
        => new(string.Empty, null, false, Array.Empty<string>(), reason);
}

public static class FishingXadbCandidatePolicy
{
    public static IReadOnlyList<FishingCharacterCandidate> ApplyAuthoritativeLevels(
        IEnumerable<FishingCharacterCandidate> configuredCandidates,
        XaFishingRosterSnapshot roster)
    {
        if (!roster.IsUsable)
            return Array.Empty<FishingCharacterCandidate>();

        var levels = roster.Characters.ToDictionary(
            entry => entry.CharacterKey,
            entry => entry.FisherLevel,
            StringComparer.OrdinalIgnoreCase);
        return configuredCandidates
            .Select(candidate => candidate with
            {
                FisherLevel = levels.TryGetValue(candidate.CharacterKey.Trim(), out var fisherLevel)
                    ? fisherLevel
                    : null,
            })
            .ToArray();
    }
}

public static class FishingStartupPolicy
{
    public static FishingSelectionResult SelectStartupTarget(
        IEnumerable<FishingCharacterCandidate> candidates,
        int maxFisherLevel,
        FishingExecutionMode mode,
        string currentCharacterKey,
        bool startupWindowActive)
    {
        if (!startupWindowActive)
            return FishingSelectionResult.None("No VERMAXION Ocean Fishing startup window is active.");

        return FishingSelectionPolicy.Select(
            candidates,
            maxFisherLevel,
            mode,
            currentCharacterKey,
            fishingWindowActive: true);
    }

    public static bool ShouldStartOnCurrentCharacter(FishingSelectionResult selection, string currentCharacterKey)
        => selection.Selected &&
           !selection.RequiresRelog &&
           string.Equals(selection.CharacterKey, currentCharacterKey, StringComparison.OrdinalIgnoreCase);
}

public static class FishingSelectionPolicy
{
    public static IReadOnlyList<FishingSelectionResult> BuildOrderedCandidates(
        IEnumerable<FishingCharacterCandidate> candidates,
        int maxFisherLevel,
        FishingExecutionMode mode,
        string currentCharacterKey,
        bool fishingWindowActive,
        IReadOnlySet<string>? excludedCharacterKeys = null)
    {
        var normalizedCurrentKey = NormalizeKey(currentCharacterKey);
        var excluded = excludedCharacterKeys ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalizedCandidates = candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.CharacterKey))
            .Select(candidate => candidate with
            {
                CharacterKey = NormalizeKey(candidate.CharacterKey),
                FisherLevel = candidate.FisherLevel.HasValue
                    ? Math.Max(0, candidate.FisherLevel.Value)
                    : null,
                IsCurrentCharacter = string.Equals(
                    NormalizeKey(candidate.CharacterKey),
                    normalizedCurrentKey,
                    StringComparison.OrdinalIgnoreCase),
            })
            .GroupBy(candidate => candidate.CharacterKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(candidate => candidate.IsCurrentCharacter)
                .ThenByDescending(candidate => candidate.FishingEnabled)
                .ThenByDescending(candidate => candidate.FisherLevel.HasValue)
                .First())
            .Where(candidate => candidate.FishingEnabled && !excluded.Contains(candidate.CharacterKey))
            .ToList();

        var cappedMaxLevel = Math.Clamp(maxFisherLevel, 1, 100);
        if (mode == FishingExecutionMode.CurrentCharacterOnly)
        {
            var current = normalizedCandidates.FirstOrDefault(candidate => candidate.IsCurrentCharacter);
            if (string.IsNullOrWhiteSpace(current.CharacterKey) ||
                !IsLevelEligible(current, cappedMaxLevel, fishingWindowActive))
            {
                return Array.Empty<FishingSelectionResult>();
            }

            return [BuildResult(current, requiresRelog: false, "Selected current character.")];
        }

        var ordered = normalizedCandidates
            .Where(candidate =>
                fishingWindowActive && candidate.AlwaysFishIfWindowOpen ||
                candidate.FisherLevel.HasValue && candidate.FisherLevel.Value < cappedMaxLevel)
            .OrderByDescending(candidate => fishingWindowActive && candidate.AlwaysFishIfWindowOpen)
            .ThenBy(candidate => candidate.FisherLevel ?? int.MaxValue)
            .ThenByDescending(candidate => candidate.IsCurrentCharacter)
            .ThenBy(candidate => candidate.CharacterKey, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => BuildResult(
                candidate,
                requiresRelog: !candidate.IsCurrentCharacter,
                fishingWindowActive && candidate.AlwaysFishIfWindowOpen
                    ? "Selected always-fish character for active fishing window."
                    : "Selected lowest known Fisher below max."))
            .ToArray();

        return ordered;
    }

    public static FishingSelectionResult Select(
        IEnumerable<FishingCharacterCandidate> candidates,
        int maxFisherLevel,
        FishingExecutionMode mode,
        string currentCharacterKey,
        bool fishingWindowActive)
    {
        var ordered = BuildOrderedCandidates(
            candidates,
            maxFisherLevel,
            mode,
            currentCharacterKey,
            fishingWindowActive);
        return ordered.Count > 0
            ? ordered[0]
            : FishingSelectionResult.None(
                mode == FishingExecutionMode.CurrentCharacterOnly
                    ? "Current character is disabled, unknown, or at the configured Fisher cap."
                    : $"No enabled configured character has a known Fisher below max {Math.Clamp(maxFisherLevel, 1, 100)}, and no AlwaysFish override applies.");
    }

    private static bool IsLevelEligible(FishingCharacterCandidate candidate, int maxFisherLevel, bool fishingWindowActive)
        => candidate.FisherLevel.HasValue && candidate.FisherLevel.Value < maxFisherLevel ||
           fishingWindowActive && candidate.AlwaysFishIfWindowOpen;

    private static FishingSelectionResult BuildResult(
        FishingCharacterCandidate selected,
        bool requiresRelog,
        string reason)
    {
        return new FishingSelectionResult(
            selected.CharacterKey,
            selected.FisherLevel,
            requiresRelog,
            Array.Empty<string>(),
            reason);
    }

    private static string NormalizeKey(string value)
        => value.Trim();
}

public enum FishingAttemptFailureKind
{
    CharacterPermanent,
    SharedTransient,
    Stop,
}

public static class FishingRecoveryPolicy
{
    public const int MaximumTransientRetries = 2;
    public static readonly TimeSpan MinimumAttemptTimeRemaining = TimeSpan.FromSeconds(60);

    public static TimeSpan GetTransientBackoff(int retryNumber)
        => retryNumber switch
        {
            1 => TimeSpan.FromSeconds(3),
            2 => TimeSpan.FromSeconds(10),
            _ => TimeSpan.MaxValue,
        };

    public static bool CanStartAttempt(DateTimeOffset nowUtc, DateTimeOffset registrationDeadlineUtc)
        => registrationDeadlineUtc.ToUniversalTime() - nowUtc.ToUniversalTime() >= MinimumAttemptTimeRemaining;

    public static bool MayRecover(
        FishingAttemptFailureKind failureKind,
        bool queueConfirmed,
        bool registrationOpen,
        int transientRetriesAlreadyScheduled)
        => !queueConfirmed &&
           registrationOpen &&
           failureKind switch
           {
               FishingAttemptFailureKind.CharacterPermanent => true,
               FishingAttemptFailureKind.SharedTransient => transientRetriesAlreadyScheduled < MaximumTransientRetries,
               _ => false,
           };
}

public enum FishingCleanupCommand
{
    None,
    Discard,
    Sell,
}

public static class FishingInventoryCleanupPolicy
{
    public static IReadOnlyList<FishingCleanupCommand> Build(bool discardEnabled, bool sellEnabled)
    {
        var result = new List<FishingCleanupCommand>(2);
        if (discardEnabled)
            result.Add(FishingCleanupCommand.Discard);
        if (sellEnabled)
            result.Add(FishingCleanupCommand.Sell);
        return result;
    }

    public static bool TreatAsNothingToProcess(bool busyObserved, TimeSpan elapsed)
        => !busyObserved && elapsed >= TimeSpan.FromSeconds(10);
}

public static class FishingReturnPolicy
{
    public static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan FailAfter = TimeSpan.FromSeconds(120);

    public static bool IsVerified(bool commandRequired, bool activityObserved, bool territoryChanged, bool currentlyBusy)
        => !commandRequired || territoryChanged || activityObserved && !currentlyBusy;

    public static bool ShouldRetry(int commandsSent, TimeSpan elapsed)
        => commandsSent == 1 && elapsed >= RetryAfter;

    public static bool ShouldSuppressCommand(bool resultAddonVisible)
        => resultAddonVisible;
}

public static class XaFishingRosterParser
{
    private const int FisherJobId = 18;
    private const int MinimumIpcContractVersion = 6;

    public static XaFishingRosterSnapshot Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return XaFishingRosterSnapshot.Failure(
                XaFishingRosterReadStatus.EmptyResponse,
                "XA Database returned an empty account roster response.");

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Malformed("The account roster root is not an object.");

            var generatedAtUtc = TryGetDateTimeOffset(root, "generatedAtUtc", out var generatedAt)
                ? generatedAt
                : null;
            if (!TryGetProperty(root, "ipcContractVersion", out var contractVersion) ||
                !TryReadInt(contractVersion, out var ipcContractVersion))
            {
                return UnsupportedContract(
                    "XA Database account roster IPC must include numeric ipcContractVersion; XADB 0.0.0.39+ contract v6 is required.",
                    generatedAtUtc);
            }

            if (ipcContractVersion < MinimumIpcContractVersion)
            {
                return UnsupportedContract(
                    $"XA Database account roster IPC contract v{ipcContractVersion} is unsupported; XADB 0.0.0.39+ contract v6 is required.",
                    generatedAtUtc);
            }

            if (!TryGetProperty(root, "isFullRosterAvailable", out var fullRoster) ||
                fullRoster.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return Malformed("The account roster does not contain a boolean isFullRosterAvailable status.");
            }

            if (!fullRoster.GetBoolean())
            {
                return new XaFishingRosterSnapshot(
                    XaFishingRosterReadStatus.FullRosterUnavailable,
                    generatedAtUtc,
                    Array.Empty<XaFishingRosterEntry>(),
                    ReadWarnings(root, "XA Database contract v6 roster IPC did not advertise a full account roster."));
            }

            if (!TryGetProperty(root, "characters", out var characters) ||
                characters.ValueKind != JsonValueKind.Array)
            {
                return Malformed("The full account roster does not contain a characters array.", generatedAtUtc);
            }

            var result = new List<XaFishingRosterEntry>();
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var character in characters.EnumerateArray())
            {
                if (character.ValueKind != JsonValueKind.Object)
                    return Malformed("The account roster contains a non-object character row.", generatedAtUtc);

                var key = GetCharacterKey(character);
                if (string.IsNullOrWhiteSpace(key))
                    return Malformed("The account roster contains a character row without a character key.", generatedAtUtc);
                if (!seenKeys.Add(key))
                    return Malformed($"The account roster contains duplicate character key '{key}'.", generatedAtUtc);

                if (!TryGetFisherLevel(character, out var fisherLevel, out var levelError))
                    return Malformed($"{key}: {levelError}", generatedAtUtc);

                DateTimeOffset? snapshotTimestamp = null;
                if (TryGetProperty(
                        character,
                        out var snapshotElement,
                        "lastSnapshotUtc",
                        "snapshotUtc",
                        "updatedUtc",
                        "capturedAtUtc",
                        "lastSaveUtc"))
                {
                    if (!TryReadDateTimeOffset(snapshotElement, out var parsedTimestamp))
                        return Malformed($"{key}: snapshot timestamp is malformed.", generatedAtUtc);
                    snapshotTimestamp = parsedTimestamp;
                }

                result.Add(new XaFishingRosterEntry(
                    key,
                    fisherLevel,
                    GetString(character, "source").Trim(),
                    snapshotTimestamp));
            }

            return new XaFishingRosterSnapshot(
                XaFishingRosterReadStatus.Ready,
                generatedAtUtc,
                result,
                ReadWarnings(root, "XA Database full account roster is ready."));
        }
        catch (JsonException ex)
        {
            return Malformed($"Account roster JSON is malformed: {ex.Message}");
        }
    }

    private static string GetCharacterKey(JsonElement character)
    {
        var characterKey = GetString(character, "characterKey");
        if (!string.IsNullOrWhiteSpace(characterKey))
            return characterKey.Trim();

        var characterName = GetString(character, "characterName");
        var worldName = GetString(character, "worldName");
        return string.IsNullOrWhiteSpace(characterName) || string.IsNullOrWhiteSpace(worldName)
            ? string.Empty
            : $"{characterName.Trim()}@{worldName.Trim()}";
    }

    private static bool TryGetFisherLevel(
        JsonElement character,
        out int? level,
        out string error)
    {
        level = null;
        error = string.Empty;
        var found = false;
        var maximumLevel = 0;

        if (TryGetProperty(character, "jobLevels", out var jobLevels))
        {
            if (jobLevels.ValueKind != JsonValueKind.Object)
            {
                error = "jobLevels is not an object.";
                return false;
            }

            if (TryGetProperty(jobLevels, FisherJobId.ToString(CultureInfo.InvariantCulture), out var fisherProperty) ||
                TryGetProperty(jobLevels, "FSH", out fisherProperty))
            {
                if (!TryReadInt(fisherProperty, out var numericLevel))
                {
                    error = "Fisher jobLevels value is not an integer.";
                    return false;
                }

                maximumLevel = Math.Max(maximumLevel, numericLevel);
                found = true;
            }
        }

        if (TryGetProperty(character, "jobs", out var jobs))
        {
            if (jobs.ValueKind != JsonValueKind.Array)
            {
                error = "jobs is not an array.";
                return false;
            }

            foreach (var job in jobs.EnumerateArray())
            {
                if (job.ValueKind != JsonValueKind.Object)
                {
                    error = "jobs contains a non-object entry.";
                    return false;
                }

                var jobIdMatches = TryGetInt(job, "jobId", out var jobId) && jobId == FisherJobId;
                var abbrevMatches = string.Equals(GetString(job, "jobAbbrev"), "FSH", StringComparison.OrdinalIgnoreCase);
                if (!jobIdMatches && !abbrevMatches)
                    continue;

                if (!TryGetProperty(job, "level", out var jobLevelElement) ||
                    !TryReadInt(jobLevelElement, out var jobLevel))
                {
                    error = "Fisher jobs entry does not contain an integer level.";
                    return false;
                }

                maximumLevel = Math.Max(maximumLevel, jobLevel);
                found = true;
            }
        }

        level = found ? Math.Max(0, maximumLevel) : null;
        return true;
    }

    private static string GetString(JsonElement obj, string propertyName)
        => TryGetProperty(obj, propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;

    private static bool TryGetInt(JsonElement obj, string propertyName, out int value)
    {
        value = 0;
        return TryGetProperty(obj, propertyName, out var property) &&
               TryReadInt(property, out value);
    }

    private static bool TryReadInt(JsonElement element, out int value)
    {
        value = 0;
        return element.ValueKind switch
        {
            JsonValueKind.Number => element.TryGetInt32(out value),
            JsonValueKind.String => int.TryParse(
                element.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value),
            _ => false,
        };
    }

    private static bool TryGetDateTimeOffset(
        JsonElement obj,
        string propertyName,
        out DateTimeOffset? value)
    {
        value = null;
        if (!TryGetProperty(obj, propertyName, out var property))
            return false;

        if (!TryReadDateTimeOffset(property, out var parsed))
            return false;

        value = parsed;
        return true;
    }

    private static bool TryReadDateTimeOffset(JsonElement element, out DateTimeOffset value)
    {
        value = default;
        return element.ValueKind == JsonValueKind.String &&
               DateTimeOffset.TryParse(
                   element.GetString(),
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                   out value);
    }

    private static string ReadWarnings(JsonElement root, string fallback)
    {
        if (!TryGetProperty(root, "warnings", out var warnings) ||
            warnings.ValueKind != JsonValueKind.Array)
        {
            return fallback;
        }

        var messages = warnings
            .EnumerateArray()
            .Where(entry => entry.ValueKind == JsonValueKind.String)
            .Select(entry => entry.GetString())
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .ToArray();
        return messages.Length == 0 ? fallback : string.Join(" | ", messages!);
    }

    private static XaFishingRosterSnapshot Malformed(
        string detail,
        DateTimeOffset? generatedAtUtc = null)
        => new(
            XaFishingRosterReadStatus.MalformedResponse,
            generatedAtUtc,
            Array.Empty<XaFishingRosterEntry>(),
            detail);

    private static XaFishingRosterSnapshot UnsupportedContract(
        string detail,
        DateTimeOffset? generatedAtUtc = null)
        => new(
            XaFishingRosterReadStatus.UnsupportedContract,
            generatedAtUtc,
            Array.Empty<XaFishingRosterEntry>(),
            detail);

    private static bool TryGetProperty(JsonElement obj, string propertyName, out JsonElement property)
        => TryGetProperty(obj, out property, propertyName);

    private static bool TryGetProperty(
        JsonElement obj,
        out JsonElement property,
        params string[] propertyNames)
    {
        if (obj.ValueKind == JsonValueKind.Object)
        {
            foreach (var propertyName in propertyNames)
            {
                if (obj.TryGetProperty(propertyName, out property))
                    return true;
            }
        }

        if (obj.ValueKind == JsonValueKind.Object)
        {
            foreach (var candidate in obj.EnumerateObject())
            {
                if (propertyNames.Any(propertyName =>
                        string.Equals(candidate.Name, propertyName, StringComparison.OrdinalIgnoreCase)))
                {
                    property = candidate.Value;
                    return true;
                }
            }
        }

        property = default;
        return false;
    }
}

public enum XaFishingRosterReadStatus
{
    Ready,
    EmptyResponse,
    MalformedResponse,
    UnsupportedContract,
    FullRosterUnavailable,
    IpcFailure,
}

public sealed record XaFishingRosterEntry(
    string CharacterKey,
    int? FisherLevel,
    string Source,
    DateTimeOffset? SnapshotTimestamp);

public sealed record XaFishingRosterSnapshot(
    XaFishingRosterReadStatus Status,
    DateTimeOffset? GeneratedAtUtc,
    IReadOnlyList<XaFishingRosterEntry> Characters,
    string Detail)
{
    public bool IsUsable => Status == XaFishingRosterReadStatus.Ready;

    public static XaFishingRosterSnapshot Failure(
        XaFishingRosterReadStatus status,
        string detail)
        => new(status, null, Array.Empty<XaFishingRosterEntry>(), detail);
}

public enum FishingRelogPrepAction
{
    FinishVermaxionPostprocess,
    ReleaseVermaxionSuppression,
    DisableAutoRetainerMultiMode,
    SendCommand,
    Wait,
}

public sealed record FishingRelogPrepStep(FishingRelogPrepAction Action, string Command = "", int DelayMilliseconds = 0);

public static class FishingRelogPrepPolicy
{
    public static IReadOnlyList<FishingRelogPrepStep> BuildReleaseSequence(string characterKey)
    {
        var normalizedKey = characterKey.Trim();
        return
        [
            new(FishingRelogPrepAction.FinishVermaxionPostprocess),
            new(FishingRelogPrepAction.ReleaseVermaxionSuppression),
            new(FishingRelogPrepAction.DisableAutoRetainerMultiMode),
            new(FishingRelogPrepAction.SendCommand, $"/ays relog {normalizedKey}"),
        ];
    }
}

public static class FishingRelogDiagnostics
{
    public static string FormatCommand(FishingRelogPrepStep step)
        => $"[Fishing][Relog] Sending {step.Command}";
}

public static class BeforeArMultiModePolicy
{
    public static bool ShouldRunBeforeAr(bool readSucceeded, bool multiModeEnabled)
        => readSucceeded && multiModeEnabled;
}

public enum FishingRelogRuntimeAction
{
    Complete,
    Fail,
    Wait,
    SendRelog,
}

public readonly record struct FishingRelogRuntimeDecision(
    FishingRelogRuntimeAction Action,
    string Reason);

public static class FishingRelogCommandPolicy
{
    public static readonly TimeSpan DefaultRetryInterval = TimeSpan.FromSeconds(45);
    public static readonly TimeSpan DefaultOverallTimeout = TimeSpan.FromMinutes(4);

    public static FishingRelogRuntimeDecision Evaluate(
        DateTimeOffset nowUtc,
        DateTimeOffset startedAtUtc,
        DateTimeOffset? lastRelogCommandAtUtc,
        bool registrationOpen,
        bool readyForRelog,
        string blockedReason,
        bool targetReached,
        bool observableProgress,
        bool wrongCharacterArrived,
        TimeSpan? retryInterval = null,
        TimeSpan? overallTimeout = null)
    {
        if (targetReached)
            return new(FishingRelogRuntimeAction.Complete, "Arrived on the target character.");

        if (!registrationOpen)
            return new(FishingRelogRuntimeAction.Fail, "Ocean Fishing registration closed before relog completed.");

        var cappedOverallTimeout = overallTimeout ?? DefaultOverallTimeout;
        if (startedAtUtc != default && nowUtc - startedAtUtc >= cappedOverallTimeout)
            return new(FishingRelogRuntimeAction.Fail, $"Relog did not reach the selected character within {cappedOverallTimeout.TotalSeconds:F0}s.");

        if (!readyForRelog)
            return new(FishingRelogRuntimeAction.Wait, string.IsNullOrWhiteSpace(blockedReason) ? "Waiting for relog readiness." : blockedReason);

        if (!lastRelogCommandAtUtc.HasValue)
            return new(FishingRelogRuntimeAction.SendRelog, "Relog command has not been sent.");

        var cappedRetryInterval = retryInterval ?? DefaultRetryInterval;
        if (wrongCharacterArrived)
        {
            return nowUtc - lastRelogCommandAtUtc.Value >= cappedRetryInterval
                ? new(FishingRelogRuntimeAction.SendRelog, $"An intermediate character arrived; retrying relog to the selected target after {cappedRetryInterval.TotalSeconds:F0}s.")
                : new(FishingRelogRuntimeAction.Wait, "An intermediate character arrived; waiting until the relog command can be retried.");
        }

        if (observableProgress)
            return new(FishingRelogRuntimeAction.Wait, "Relog transition was observed; waiting for target character.");

        return nowUtc - lastRelogCommandAtUtc.Value >= cappedRetryInterval
            ? new(FishingRelogRuntimeAction.SendRelog, $"No logout or area transition was observed within {cappedRetryInterval.TotalSeconds:F0}s; retrying relog.")
            : new(FishingRelogRuntimeAction.Wait, "Waiting for observable relog progress.");
    }
}

public enum OceanFishingQueueAction
{
    SwitchToFisher,
    PrepareSupplies,
    TravelToLimsa,
    MoveToRegistrar,
    WaitForRegistrationOpen,
    InteractWithRegistrar,
    WaitForQueueConfirmation,
    WaitForDeparture,
    MoveToFishingPosition,
    CastLine,
    CloseResult,
    ReturnAfterCompletion,
    Complete,
    FailRegistrationClosed,
}

public readonly record struct OceanFishingQueueSnapshot(
    int CurrentJobId,
    bool SuppliesPrepared,
    ushort TerritoryType,
    double RegistrarDistance,
    bool RegistrationWindowOpen,
    bool RegistrationWindowClosed,
    bool QueueConfirmed,
    bool DutyActive,
    double FishingPositionDistance,
    bool ResultAddonVisible,
    bool FishingComplete,
    bool ReturnCommandSent);

public static class OceanFishingQueuePolicy
{
    public const int FisherJobId = 18;
    public const ushort LimsaTerritoryType = 129;
    public const double RegistrarInteractDistance = 2.0;
    public const double BoatFishingPositionTolerance = 0.5;

    public static OceanFishingQueueAction Decide(OceanFishingQueueSnapshot snapshot)
    {
        if (snapshot.FishingComplete)
            return snapshot.ReturnCommandSent
                ? OceanFishingQueueAction.Complete
                : OceanFishingQueueAction.ReturnAfterCompletion;

        if (snapshot.ResultAddonVisible)
            return OceanFishingQueueAction.CloseResult;

        if (snapshot.DutyActive)
        {
            return snapshot.FishingPositionDistance <= BoatFishingPositionTolerance
                ? OceanFishingQueueAction.CastLine
                : OceanFishingQueueAction.MoveToFishingPosition;
        }

        if (snapshot.QueueConfirmed)
            return OceanFishingQueueAction.WaitForDeparture;

        if (snapshot.RegistrationWindowClosed)
            return OceanFishingQueueAction.FailRegistrationClosed;

        if (snapshot.CurrentJobId != FisherJobId)
            return OceanFishingQueueAction.SwitchToFisher;

        if (snapshot.TerritoryType != LimsaTerritoryType)
            return OceanFishingQueueAction.TravelToLimsa;

        if (!snapshot.SuppliesPrepared)
            return OceanFishingQueueAction.PrepareSupplies;

        if (snapshot.RegistrarDistance > RegistrarInteractDistance)
            return OceanFishingQueueAction.MoveToRegistrar;

        return snapshot.RegistrationWindowOpen
            ? OceanFishingQueueAction.InteractWithRegistrar
            : OceanFishingQueueAction.WaitForRegistrationOpen;
    }
}

public static class OceanFishingRegistrarPolicy
{
    public static Vector3 ResolveApproachPosition(Vector3 fallbackPosition, Vector3? liveObjectPosition)
        => liveObjectPosition ?? fallbackPosition;

    public static bool IsWithinInteractionRange(double distance)
        => distance <= OceanFishingQueuePolicy.RegistrarInteractDistance;
}

internal readonly record struct OceanFishingDockPreparationDecision(
    bool RepairNeeded,
    bool LureRestockNeeded)
{
    public bool RequiresDockNavigation => RepairNeeded || LureRestockNeeded;
}

internal static class OceanFishingDockPreparationPolicy
{
    public const ushort LimsaTerritoryType = 129;
    public const uint MerchantAndMenderDataId = 1005422;
    public const uint DryskthotaDataId = 1005421;
    public const uint ArcanistsGuildAethernetId = 43;
    public const uint VersatileLureItemId = 29717;
    public const double InteractDistance = 3.0;
    public static readonly Vector3 MerchantAndMenderPosition = new(-399.0f, 3.0f, 80.0f);
    public static readonly Vector3 DryskthotaPosition = new(-409.42f, 4.0f, 74.48f);

    public static OceanFishingDockPreparationDecision Evaluate(
        bool repairNeeded,
        int currentLureCount,
        int lureTarget)
        => new(
            repairNeeded,
            Math.Max(0, currentLureCount) < Math.Max(0, lureTarget));

    public static bool IsLimsaSettlementReady(
        uint territoryType,
        bool betweenAreas,
        bool playerAvailable)
        => territoryType == LimsaTerritoryType && !betweenAreas && playerAvailable;

    public static Vector3 ResolveMerchantApproachPosition(
        Vector3 fallbackPosition,
        Vector3? dataIdObjectPosition,
        Vector3? nameFallbackObjectPosition)
        => dataIdObjectPosition ?? nameFallbackObjectPosition ?? fallbackPosition;

    public static int RequiredPurchaseQuantity(int currentCount, int targetCount, int maximumQuantity = 99)
    {
        var remaining = Math.Max(0, targetCount) - Math.Max(0, currentCount);
        return remaining <= 0
            ? 0
            : Math.Clamp(remaining, 1, Math.Max(1, maximumQuantity));
    }

    public static bool CanContinueAfterRestockFailure(int finalLureCount)
        => finalLureCount > 0;
}

public enum OceanFishingRegistrationDecision
{
    ContinueDialogs,
    WaitForQueueRecognitionGrace,
    QueueConfirmed,
    RegistrationExpired,
    GenuineFailure,
}

public static class OceanFishingRegistrationPolicy
{
    public static readonly TimeSpan QueueRecognitionGracePeriod = TimeSpan.FromSeconds(60);

    public static OceanFishingRegistrationDecision Decide(
        bool queueConfirmed,
        bool embarkAccepted,
        DateTimeOffset nowUtc,
        DateTimeOffset registrationDeadlineUtc,
        bool genuineFailure)
    {
        if (queueConfirmed)
            return OceanFishingRegistrationDecision.QueueConfirmed;
        if (genuineFailure)
            return OceanFishingRegistrationDecision.GenuineFailure;
        if (nowUtc < registrationDeadlineUtc)
            return OceanFishingRegistrationDecision.ContinueDialogs;
        if (embarkAccepted &&
            nowUtc < registrationDeadlineUtc + QueueRecognitionGracePeriod)
        {
            return OceanFishingRegistrationDecision.WaitForQueueRecognitionGrace;
        }

        return OceanFishingRegistrationDecision.RegistrationExpired;
    }

    public static bool ShouldRetainRegistrationLeases(OceanFishingRegistrationDecision decision)
        => decision is OceanFishingRegistrationDecision.ContinueDialogs or
           OceanFishingRegistrationDecision.WaitForQueueRecognitionGrace or
           OceanFishingRegistrationDecision.QueueConfirmed;
}

public enum OceanFishingQueueEvidence
{
    None,
    InDutyQueue,
    WaitingForDuty,
    WaitingForDutyFinder,
    OceanFishingDutyEntry,
    ContentsFinderConfirm,
}

public static class OceanFishingQueueEvidencePolicy
{
    public static OceanFishingQueueEvidence Detect(
        bool inDutyQueue,
        bool waitingForDuty,
        bool waitingForDutyFinder,
        bool oceanFishingDutyActive,
        bool contentsFinderConfirmVisible)
    {
        if (oceanFishingDutyActive)
            return OceanFishingQueueEvidence.OceanFishingDutyEntry;
        if (contentsFinderConfirmVisible)
            return OceanFishingQueueEvidence.ContentsFinderConfirm;
        if (inDutyQueue)
            return OceanFishingQueueEvidence.InDutyQueue;
        if (waitingForDuty)
            return OceanFishingQueueEvidence.WaitingForDuty;
        if (waitingForDutyFinder)
            return OceanFishingQueueEvidence.WaitingForDutyFinder;
        return OceanFishingQueueEvidence.None;
    }
}

public static class OceanFishingDialoguePolicy
{
    public const string SheetName = "custom/006/CtsIkdEntrance_00663";
    public const uint BoardingRow = 4;
    public const uint EmbarkRow = 10;
    public const string EnglishEmbarkPrefix = "Embark to ";

    public static bool Matches(string actualText, string localizedSheetText)
    {
        var actual = actualText?.Trim() ?? string.Empty;
        var localized = localizedSheetText?.Trim() ?? string.Empty;
        return localized.Length > 0 &&
               (string.Equals(actual, localized, StringComparison.Ordinal) ||
                actual.Contains(localized, StringComparison.Ordinal));
    }

    public static bool MatchesEmbarkPrompt(string actualText, string localizedSheetText)
    {
        if (Matches(actualText, localizedSheetText))
            return true;

        var normalized = NormalizeWhitespace(actualText);
        if (!normalized.StartsWith(EnglishEmbarkPrefix, StringComparison.Ordinal) ||
            !normalized.EndsWith("?", StringComparison.Ordinal))
        {
            return false;
        }

        var destination = normalized[EnglishEmbarkPrefix.Length..^1].Trim();
        return destination.Length > 0;
    }

    public static string DescribeEmbarkExpectation(string localizedSheetText)
    {
        var localized = localizedSheetText?.Trim();
        if (string.IsNullOrWhiteSpace(localized))
            localized = "<unavailable>";

        return $"{SheetName} row {EmbarkRow} text '{localized}' or English route prompt '{EnglishEmbarkPrefix}...?'.";
    }

    private static string NormalizeWhitespace(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts);
    }
}

public static class OceanFishingCompletionPolicy
{
    public static bool ShouldInferFromDutyContextLoss(
        bool dutyContextPreviouslyObserved,
        bool stillInOceanFishingTerritory,
        bool playerAvailable,
        bool areaTransitioning)
        => dutyContextPreviouslyObserved &&
           !stillInOceanFishingTerritory &&
           playerAvailable &&
           !areaTransitioning;
}

public enum OceanFishingAttunementAction
{
    UseUnlockedShard,
    NavigateToLockedShard,
    AttuneLockedShard,
    VerifyAttunement,
    WalkDirect,
}

public static class OceanFishingAttunementPolicy
{
    public static readonly TimeSpan VerificationWait = TimeSpan.FromSeconds(10);

    public static OceanFishingAttunementAction Decide(
        bool unlocked,
        bool shardLoaded,
        bool inInteractionRange,
        bool attunementAttempted,
        TimeSpan sinceAttempt)
    {
        if (unlocked)
            return OceanFishingAttunementAction.UseUnlockedShard;
        if (!attunementAttempted)
            return shardLoaded && inInteractionRange
                ? OceanFishingAttunementAction.AttuneLockedShard
                : OceanFishingAttunementAction.NavigateToLockedShard;
        return sinceAttempt < VerificationWait
            ? OceanFishingAttunementAction.VerifyAttunement
            : OceanFishingAttunementAction.WalkDirect;
    }
}

public readonly record struct AutoRetainerMultiModeReadResult(bool Success, bool Enabled, string Error)
{
    public static AutoRetainerMultiModeReadResult Known(bool enabled)
        => new(true, enabled, string.Empty);

    public static AutoRetainerMultiModeReadResult Failed(string error)
        => new(false, false, error);
}

public readonly record struct PluginStateReadResult(bool Success, bool Enabled, string Error)
{
    public static PluginStateReadResult Known(bool enabled) => new(true, enabled, string.Empty);
    public static PluginStateReadResult Failed(string error) => new(false, false, error);
}

public readonly record struct PluginBusyReadResult(bool Success, bool Busy, string Error)
{
    public static PluginBusyReadResult Known(bool busy) => new(true, busy, string.Empty);
    public static PluginBusyReadResult Failed(string error) => new(false, false, error);
}

public static class FishingExternalStatePolicy
{
    public static bool ShouldRestore(bool? initialState, bool changedByVermaxion)
        => initialState.HasValue && changedByVermaxion;
}

public static class LifestreamCommandPolicy
{
    public static string NormalizeForIpc(string command)
    {
        var normalized = command.Trim();
        return normalized.StartsWith("/li ", StringComparison.OrdinalIgnoreCase)
            ? normalized[4..].Trim()
            : normalized;
    }
}

public enum FishingCastDecision
{
    Suppressed = 0,
    Attempt = 1,
    Acknowledged = 2,
}

public readonly record struct FishingCastEvaluation(
    FishingCastDecision Decision,
    string Gate);

public static class FishingCastPolicy
{
    public const string CastCommand = "/ahstart";
    public static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan CanFishFallbackDelay = TimeSpan.FromSeconds(10);

    public static FishingCastEvaluation Evaluate(
        bool enabled,
        bool inFishingContext,
        bool zoneTransitionActive,
        bool playerAvailable,
        bool gatheringConditionActive,
        bool fishingConditionActive,
        bool resultWindowVisible,
        TimeSpan sinceLastAttempt)
    {
        if (!enabled)
            return Suppressed("disabled");
        if (resultWindowVisible)
            return Suppressed("result window visible");
        if (!inFishingContext)
            return Suppressed("Ocean Fishing duty context inactive");
        if (zoneTransitionActive)
            return Suppressed("route transition active");
        if (gatheringConditionActive || fishingConditionActive)
            return new FishingCastEvaluation(FishingCastDecision.Acknowledged, "Fishing/Gathering active");
        if (!playerAvailable)
            return Suppressed("player unavailable");
        if (sinceLastAttempt < RetryInterval)
            return Suppressed("waiting for retry interval");

        return new FishingCastEvaluation(FishingCastDecision.Attempt, string.Empty);
    }

    private static FishingCastEvaluation Suppressed(string gate)
        => new(FishingCastDecision.Suppressed, gate);
}

public sealed record FishingRepairDecision(bool ShouldRepair, string AdsMode, string Reason);

public static class FishingRepairPolicy
{
    public static FishingRepairDecision Evaluate(
        FishingRepairMode mode,
        int thresholdPercent,
        bool durabilityKnown,
        int lowestDurabilityPercent)
    {
        if (mode == FishingRepairMode.Disabled || thresholdPercent <= 0)
            return new FishingRepairDecision(false, string.Empty, "Repair disabled.");

        var adsMode = ToAdsMode(mode);
        if (string.IsNullOrWhiteSpace(adsMode))
            return new FishingRepairDecision(false, string.Empty, "Repair mode is invalid.");

        if (!durabilityKnown)
            return new FishingRepairDecision(false, adsMode, "Durability unavailable.");

        var threshold = Math.Clamp(thresholdPercent, 0, 100);
        var durability = Math.Clamp(lowestDurabilityPercent, 0, 100);
        if (durability <= threshold)
            return new FishingRepairDecision(true, adsMode, $"Durability {durability}% is at or below threshold {threshold}%.");

        return new FishingRepairDecision(false, adsMode, $"Durability {durability}% is above threshold {threshold}%.");
    }

    public static string ToAdsMode(FishingRepairMode mode)
        => mode switch
        {
            FishingRepairMode.Self => "self",
            FishingRepairMode.NpcNoInn => "npc-no-inn",
            FishingRepairMode.NpcNoTeleportNoInn => "npc-no-teleport-no-inn",
            _ => string.Empty,
        };
}

public static class FishingOperationPolicy
{
    public static int ResolveLureRestockTarget(int configuredTarget)
        => configuredTarget > 0
            ? configuredTarget
            : FishingDefaults.LureRestockTarget;

    public static FishingRepairDecision EvaluateRepair(
        FishingOperationSettings settings,
        bool durabilityKnown,
        int lowestDurabilityPercent)
        => FishingRepairPolicy.Evaluate(
            settings.RepairMode,
            settings.RepairThresholdPercent,
            durabilityKnown,
            lowestDurabilityPercent);

    public static string ResolveReturnCommand(FishingOperationSettings settings)
    {
        if (settings.ReturnDestination == FishingReturnDestination.None)
            return string.Empty;

        var configuredCommand = settings.ReturnCommand?.Trim() ?? string.Empty;
        return settings.ReturnDestination switch
        {
            FishingReturnDestination.Home => string.IsNullOrWhiteSpace(configuredCommand)
                ? "/li home"
                : configuredCommand,
            FishingReturnDestination.Limsa => string.IsNullOrWhiteSpace(configuredCommand)
                ? "/li limsa"
                : configuredCommand,
            FishingReturnDestination.FreeCompany => string.IsNullOrWhiteSpace(configuredCommand)
                ? "/li fc"
                : configuredCommand,
            FishingReturnDestination.Custom => configuredCommand,
            _ => string.Empty,
        };
    }
}
