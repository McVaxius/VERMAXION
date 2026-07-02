using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace VERMAXION.Models;

public enum FishingExecutionMode
{
    CurrentCharacterOnly = 0,
    AutoRetainerRelogCurrentAccount = 1,
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
    int FisherLevel,
    bool FishingEnabled,
    bool AlwaysFishIfWindowOpen,
    bool IsCurrentCharacter);

public sealed record FishingSelectionResult(
    string CharacterKey,
    int FisherLevel,
    bool RequiresRelog,
    IReadOnlyList<string> AlwaysFishKeysToDisable,
    string Reason)
{
    public bool Selected => !string.IsNullOrWhiteSpace(CharacterKey);

    public static FishingSelectionResult None(string reason)
        => new(string.Empty, 0, false, Array.Empty<string>(), reason);
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
    public static FishingSelectionResult Select(
        IEnumerable<FishingCharacterCandidate> candidates,
        int maxFisherLevel,
        FishingExecutionMode mode,
        string currentCharacterKey,
        bool fishingWindowActive)
    {
        var normalizedCurrentKey = NormalizeKey(currentCharacterKey);
        var normalizedCandidates = candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.CharacterKey))
            .Select(candidate => candidate with
            {
                CharacterKey = NormalizeKey(candidate.CharacterKey),
                FisherLevel = Math.Max(0, candidate.FisherLevel),
                IsCurrentCharacter = string.Equals(
                    NormalizeKey(candidate.CharacterKey),
                    normalizedCurrentKey,
                    StringComparison.OrdinalIgnoreCase),
            })
            .GroupBy(candidate => candidate.CharacterKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(candidate => candidate.IsCurrentCharacter)
                .ThenByDescending(candidate => candidate.FishingEnabled)
                .First())
            .ToList();

        var cappedMaxLevel = Math.Clamp(maxFisherLevel, 1, 100);
        if (mode == FishingExecutionMode.CurrentCharacterOnly)
        {
            var current = normalizedCandidates.FirstOrDefault(candidate => candidate.IsCurrentCharacter);
            if (string.IsNullOrWhiteSpace(current.CharacterKey))
                return FishingSelectionResult.None("Current character was not configured for fishing.");

            if (!current.FishingEnabled)
                return FishingSelectionResult.None("Current character fishing is disabled.");

            if (!IsLevelEligible(current, cappedMaxLevel, fishingWindowActive))
                return FishingSelectionResult.None($"Current character Fisher level {current.FisherLevel} is at or above max {cappedMaxLevel}.");

            return BuildResult(current, normalizedCandidates, requiresRelog: false, "Selected current character.");
        }

        if (fishingWindowActive)
        {
            var alwaysCandidate = normalizedCandidates
                .Where(candidate => candidate.FishingEnabled && candidate.AlwaysFishIfWindowOpen)
                .OrderByDescending(candidate => candidate.IsCurrentCharacter)
                .ThenBy(candidate => candidate.FisherLevel)
                .ThenBy(candidate => candidate.CharacterKey, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(alwaysCandidate.CharacterKey))
            {
                return BuildResult(
                    alwaysCandidate,
                    normalizedCandidates,
                    requiresRelog: !alwaysCandidate.IsCurrentCharacter,
                    "Selected always-fish character for active fishing window.");
            }
        }

        var selected = normalizedCandidates
            .Where(candidate => candidate.FishingEnabled && candidate.FisherLevel < cappedMaxLevel)
            .OrderBy(candidate => candidate.FisherLevel)
            .ThenByDescending(candidate => candidate.IsCurrentCharacter)
            .ThenBy(candidate => candidate.CharacterKey, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(selected.CharacterKey))
            return FishingSelectionResult.None($"No enabled configured character has Fisher below max {cappedMaxLevel}.");

        return BuildResult(
            selected,
            normalizedCandidates,
            requiresRelog: !selected.IsCurrentCharacter,
            "Selected lowest Fisher below max.");
    }

    private static bool IsLevelEligible(FishingCharacterCandidate candidate, int maxFisherLevel, bool fishingWindowActive)
        => candidate.FisherLevel < maxFisherLevel || (fishingWindowActive && candidate.AlwaysFishIfWindowOpen);

    private static FishingSelectionResult BuildResult(
        FishingCharacterCandidate selected,
        IReadOnlyCollection<FishingCharacterCandidate> allCandidates,
        bool requiresRelog,
        string reason)
    {
        var alwaysKeys = allCandidates
            .Where(candidate => candidate.FishingEnabled && candidate.AlwaysFishIfWindowOpen)
            .Select(candidate => candidate.CharacterKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var keysToDisable = alwaysKeys.Count <= 1
            ? Array.Empty<string>()
            : alwaysKeys
                .Where(key => !string.Equals(key, selected.CharacterKey, StringComparison.OrdinalIgnoreCase))
                .ToArray();

        return new FishingSelectionResult(
            selected.CharacterKey,
            selected.FisherLevel,
            requiresRelog,
            keysToDisable,
            reason);
    }

    private static string NormalizeKey(string value)
        => value.Trim();
}

public static class XaFishingRosterParser
{
    private const int FisherJobId = 18;

    public static IReadOnlyDictionary<string, int> ParseFisherLevels(string? json)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json))
            return result;

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!TryGetProperty(root, "characters", out var characters) ||
            characters.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var character in characters.EnumerateArray())
        {
            var key = GetCharacterKey(character);
            if (string.IsNullOrWhiteSpace(key))
                continue;

            if (TryGetFisherLevel(character, out var fisherLevel))
                result[key] = Math.Max(0, fisherLevel);
        }

        return result;
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

    private static bool TryGetFisherLevel(JsonElement character, out int level)
    {
        level = 0;
        var found = false;

        if (TryGetProperty(character, "jobLevels", out var jobLevels) &&
            jobLevels.ValueKind == JsonValueKind.Object)
        {
            if (TryGetObjectInt(jobLevels, FisherJobId.ToString(CultureInfo.InvariantCulture), out var numericLevel) ||
                TryGetObjectInt(jobLevels, "FSH", out numericLevel))
            {
                level = Math.Max(level, numericLevel);
                found = true;
            }
        }

        if (TryGetProperty(character, "jobs", out var jobs) &&
            jobs.ValueKind == JsonValueKind.Array)
        {
            foreach (var job in jobs.EnumerateArray())
            {
                var jobIdMatches = TryGetInt(job, "jobId", out var jobId) && jobId == FisherJobId;
                var abbrevMatches = string.Equals(GetString(job, "jobAbbrev"), "FSH", StringComparison.OrdinalIgnoreCase);
                if (!jobIdMatches && !abbrevMatches)
                    continue;

                if (TryGetInt(job, "level", out var jobLevel))
                {
                    level = Math.Max(level, jobLevel);
                    found = true;
                }
            }
        }

        return found;
    }

    private static bool TryGetObjectInt(JsonElement obj, string propertyName, out int value)
    {
        value = 0;
        return TryGetProperty(obj, propertyName, out var property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt32(out value);
    }

    private static string GetString(JsonElement obj, string propertyName)
        => TryGetProperty(obj, propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;

    private static bool TryGetInt(JsonElement obj, string propertyName, out int value)
    {
        value = 0;
        return TryGetProperty(obj, propertyName, out var property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt32(out value);
    }

    private static bool TryGetProperty(JsonElement obj, string propertyName, out JsonElement property)
    {
        if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(propertyName, out property))
            return true;

        if (obj.ValueKind == JsonValueKind.Object)
        {
            foreach (var candidate in obj.EnumerateObject())
            {
                if (string.Equals(candidate.Name, propertyName, StringComparison.OrdinalIgnoreCase))
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

public enum FishingRelogPrepAction
{
    FinishVermaxionPostprocess,
    ReleaseVermaxionSuppression,
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
            new(FishingRelogPrepAction.SendCommand, "/ays m d"),
            new(FishingRelogPrepAction.Wait, DelayMilliseconds: 1000),
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

        if (wrongCharacterArrived)
            return new(FishingRelogRuntimeAction.Fail, "Relog arrived on a different character than the selected fishing target.");

        var cappedOverallTimeout = overallTimeout ?? DefaultOverallTimeout;
        if (startedAtUtc != default && nowUtc - startedAtUtc >= cappedOverallTimeout)
            return new(FishingRelogRuntimeAction.Fail, $"Relog did not reach the selected character within {cappedOverallTimeout.TotalSeconds:F0}s.");

        if (!readyForRelog)
            return new(FishingRelogRuntimeAction.Wait, string.IsNullOrWhiteSpace(blockedReason) ? "Waiting for relog readiness." : blockedReason);

        if (!lastRelogCommandAtUtc.HasValue)
            return new(FishingRelogRuntimeAction.SendRelog, "Relog command has not been sent.");

        if (observableProgress)
            return new(FishingRelogRuntimeAction.Wait, "Relog transition was observed; waiting for target character.");

        var cappedRetryInterval = retryInterval ?? DefaultRetryInterval;
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
    public const double RegistrarInteractDistance = 4.5;
    public const double BoatFishingPositionTolerance = 1.5;

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

        if (!snapshot.SuppliesPrepared)
            return OceanFishingQueueAction.PrepareSupplies;

        if (snapshot.TerritoryType != LimsaTerritoryType)
            return OceanFishingQueueAction.TravelToLimsa;

        if (snapshot.RegistrarDistance > RegistrarInteractDistance)
            return OceanFishingQueueAction.MoveToRegistrar;

        return snapshot.RegistrationWindowOpen
            ? OceanFishingQueueAction.InteractWithRegistrar
            : OceanFishingQueueAction.WaitForRegistrationOpen;
    }
}

public readonly record struct AutoRetainerMultiModeReadResult(bool Success, bool Enabled, string Error)
{
    public static AutoRetainerMultiModeReadResult Known(bool enabled)
        => new(true, enabled, string.Empty);

    public static AutoRetainerMultiModeReadResult Failed(string error)
        => new(false, false, error);
}

public static class FishingCastPolicy
{
    public static bool ShouldCast(
        bool enabled,
        bool inFishingContext,
        bool playerAvailable,
        bool busy,
        bool resultWindowVisible)
        => enabled && inFishingContext && playerAvailable && !busy && !resultWindowVisible;
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
