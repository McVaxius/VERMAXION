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
    public const int LureRestockTarget = 0;
    public const FishingReturnDestination ReturnDestination = FishingReturnDestination.Home;
    public const string ReturnCommand = "/li home";
    public const FishingRepairMode RepairMode = FishingRepairMode.Disabled;
    public const int RepairThresholdPercent = 50;
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
            new(FishingRelogPrepAction.SendCommand, "/ays reset"),
            new(FishingRelogPrepAction.Wait, DelayMilliseconds: 1000),
            new(FishingRelogPrepAction.SendCommand, $"/ays relog {normalizedKey}"),
        ];
    }
}

public static class BeforeArMultiModePolicy
{
    public static bool ShouldRunBeforeAr(bool readSucceeded, bool multiModeEnabled)
        => readSucceeded && multiModeEnabled;
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
