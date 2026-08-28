using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace VERMAXION.Models;

public enum ChocoboAutomationMode
{
    AlwaysRace = 0,
    TargetPedigree = 1,
}

public enum ChokeAboTargetCyclePhase
{
    Idle,
    Planning,
    PurchasingFeed,
    Feeding,
    Racing,
    RetirementPendingCapture,
    CoveringPendingCapture,
    CoveringWait,
    AdoptionPendingCapture,
    RegistrationPendingCapture,
    Paused,
    TargetReady,
    Blocked,
}

public sealed record ChokeAboTargetCycleStatus(
    int Version,
    ulong ContentId,
    ChokeAboTargetCyclePhase Phase,
    bool ShouldBlockRacing,
    bool TargetReady,
    bool GameActionInProgress,
    string Reason,
    DateTimeOffset? NextCoveringEligibilityUtc);

public readonly record struct ChokeAboTargetCycleCallResult(
    bool Succeeded,
    ChokeAboTargetCycleStatus? Status,
    string Error)
{
    public static ChokeAboTargetCycleCallResult Success(ChokeAboTargetCycleStatus status)
        => new(true, status, string.Empty);

    public static ChokeAboTargetCycleCallResult Failure(string error)
        => new(false, null, error);
}

public static class ChokeAboTargetCycleProtocol
{
    public const int Version = 2;

    public static bool TryCreateEnsureRequestJson(
        ulong contentId,
        int targetPedigree,
        int retirementRank,
        int preferredFeedGrade,
        out string json,
        out string error)
    {
        json = string.Empty;
        if (!TryValidateIdentity(contentId, out error) ||
            !TryValidateSettings(targetPedigree, retirementRank, preferredFeedGrade, out error))
        {
            return false;
        }

        json = JsonSerializer.Serialize(new
        {
            version = Version,
            contentId,
            targetPedigree,
            retirementRank,
            preferredFeedGrade,
        });
        return true;
    }

    public static bool TryCreateIdentityRequestJson(ulong contentId, out string json, out string error)
    {
        json = string.Empty;
        if (!TryValidateIdentity(contentId, out error))
            return false;

        json = JsonSerializer.Serialize(new { version = Version, contentId });
        return true;
    }

    public static bool TryParseStatus(
        string json,
        ulong expectedContentId,
        out ChokeAboTargetCycleStatus? status,
        out string error)
    {
        status = null;
        if (!TryValidateIdentity(expectedContentId, out error))
            return false;
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "Choke-abo returned an empty V2 status.";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "Choke-abo V2 status root must be an object.";
                return false;
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    error = $"Choke-abo V2 status contains duplicate field '{property.Name}'.";
                    return false;
                }
            }

            if (!TryReadInt32(root, "version", out var version, out error) ||
                !TryReadUInt64(root, "contentId", out var contentId, out error) ||
                !TryReadString(root, "phase", out var phaseText, out error) ||
                !TryReadBoolean(root, "shouldBlockRacing", out var shouldBlockRacing, out error) ||
                !TryReadBoolean(root, "targetReady", out var targetReady, out error) ||
                !TryReadBoolean(root, "gameActionInProgress", out var gameActionInProgress, out error) ||
                !TryReadString(root, "reason", out var reason, out error) ||
                !TryReadOptionalUtc(root, "nextCoveringEligibilityUtc", out var nextCoveringEligibilityUtc, out error))
            {
                return false;
            }

            if (version != Version)
            {
                error = $"Choke-abo returned V{version}; V{Version} is required.";
                return false;
            }
            if (contentId != expectedContentId)
            {
                error = $"Choke-abo Content ID {contentId} does not match the active Content ID {expectedContentId}.";
                return false;
            }
            if (!TryParsePhase(phaseText, out var phase))
            {
                error = $"Choke-abo returned unknown V2 phase '{phaseText}'.";
                return false;
            }

            if (nextCoveringEligibilityUtc.HasValue && phase != ChokeAboTargetCyclePhase.CoveringWait)
            {
                error = "Choke-abo returned covering eligibility outside CoveringWait.";
                return false;
            }

            status = new ChokeAboTargetCycleStatus(
                version,
                contentId,
                phase,
                shouldBlockRacing,
                targetReady,
                gameActionInProgress,
                reason,
                nextCoveringEligibilityUtc);
            error = string.Empty;
            return true;
        }
        catch (JsonException ex)
        {
            error = $"Malformed Choke-abo V2 JSON: {ex.Message}";
            return false;
        }
    }

    public static bool TryValidateSettings(
        int targetPedigree,
        int retirementRank,
        int preferredFeedGrade,
        out string error)
    {
        if (targetPedigree is < 2 or > 9)
        {
            error = $"Target pedigree G{targetPedigree} is outside G2-G9.";
            return false;
        }
        if (retirementRank is < 40 or > 50)
        {
            error = $"Retirement rank {retirementRank} is outside 40-50.";
            return false;
        }
        if (preferredFeedGrade is < 1 or > 3)
        {
            error = $"Preferred feed grade {preferredFeedGrade} is outside Grade 1-3.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool TryValidateIdentity(ulong contentId, out string error)
    {
        if (contentId != 0)
        {
            error = string.Empty;
            return true;
        }

        error = "A non-zero unsigned Content ID is required for Choke-abo V2.";
        return false;
    }

    private static bool TryParsePhase(string value, out ChokeAboTargetCyclePhase phase)
    {
        foreach (var candidate in Enum.GetValues<ChokeAboTargetCyclePhase>())
        {
            if (string.Equals(value, candidate.ToString(), StringComparison.Ordinal))
            {
                phase = candidate;
                return true;
            }
        }

        phase = default;
        return false;
    }

    private static bool TryReadInt32(JsonElement root, string name, out int value, out string error)
    {
        value = 0;
        if (root.TryGetProperty(name, out var element) &&
            element.ValueKind == JsonValueKind.Number &&
            element.TryGetInt32(out value))
        {
            error = string.Empty;
            return true;
        }

        error = $"Required integer field '{name}' is missing or invalid.";
        return false;
    }

    private static bool TryReadUInt64(JsonElement root, string name, out ulong value, out string error)
    {
        value = 0;
        if (root.TryGetProperty(name, out var element) &&
            element.ValueKind == JsonValueKind.Number &&
            element.TryGetUInt64(out value))
        {
            error = string.Empty;
            return true;
        }

        error = $"Required unsigned field '{name}' is missing or invalid.";
        return false;
    }

    private static bool TryReadString(JsonElement root, string name, out string value, out string error)
    {
        value = string.Empty;
        if (root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String)
        {
            value = element.GetString() ?? string.Empty;
            error = string.Empty;
            return true;
        }

        error = $"Required string field '{name}' is missing or invalid.";
        return false;
    }

    private static bool TryReadBoolean(JsonElement root, string name, out bool value, out string error)
    {
        value = false;
        if (root.TryGetProperty(name, out var element) &&
            element.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            value = element.GetBoolean();
            error = string.Empty;
            return true;
        }

        error = $"Required Boolean field '{name}' is missing or invalid.";
        return false;
    }

    private static bool TryReadOptionalUtc(
        JsonElement root,
        string name,
        out DateTimeOffset? value,
        out string error)
    {
        value = null;
        if (!root.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            error = string.Empty;
            return true;
        }
        if (element.ValueKind != JsonValueKind.String ||
            !DateTimeOffset.TryParse(
                element.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed) ||
            parsed.Offset != TimeSpan.Zero)
        {
            error = $"Optional UTC field '{name}' is invalid.";
            return false;
        }

        value = parsed;
        error = string.Empty;
        return true;
    }
}

public enum ChocoboTargetHandoffAction
{
    Wait,
    Race,
    Defer,
    Complete,
}

public readonly record struct ChocoboTargetHandoffDecision(
    ChocoboTargetHandoffAction Action,
    bool TargetReady,
    string Reason);

public enum ChocoboTaskTerminalAction
{
    None,
    PersistCompletion,
    PersistFailure,
    AdvanceDeferred,
}

public static class ChocoboTargetCyclePolicy
{
    public static ChocoboTargetHandoffDecision DecideHandoff(
        ChokeAboTargetCycleCallResult result,
        int completedRaces,
        int configuredRaces)
    {
        if (!result.Succeeded || result.Status == null)
            return new ChocoboTargetHandoffDecision(ChocoboTargetHandoffAction.Defer, false, result.Error);

        var status = result.Status;
        if (status.GameActionInProgress)
            return new ChocoboTargetHandoffDecision(ChocoboTargetHandoffAction.Wait, false, status.Reason);
        if (completedRaces >= configuredRaces)
            return new ChocoboTargetHandoffDecision(ChocoboTargetHandoffAction.Complete, status.TargetReady, status.Reason);
        if (status.TargetReady)
            return new ChocoboTargetHandoffDecision(ChocoboTargetHandoffAction.Race, true, status.Reason);
        if (status.ShouldBlockRacing)
            return new ChocoboTargetHandoffDecision(ChocoboTargetHandoffAction.Defer, false, status.Reason);

        return new ChocoboTargetHandoffDecision(ChocoboTargetHandoffAction.Race, false, status.Reason);
    }

    public static ChocoboTaskTerminalAction ClassifyTerminal(
        bool isComplete,
        bool isFailed,
        bool isDeferred)
    {
        if (isComplete)
            return ChocoboTaskTerminalAction.PersistCompletion;
        if (isFailed)
            return ChocoboTaskTerminalAction.PersistFailure;
        if (isDeferred)
            return ChocoboTaskTerminalAction.AdvanceDeferred;
        return ChocoboTaskTerminalAction.None;
    }

    public static void CopySettings(CharacterConfig source, CharacterConfig target)
    {
        target.ChocoboRacesPerDay = source.ChocoboRacesPerDay;
        target.SkipChocoboRacingAtRank50 = source.SkipChocoboRacingAtRank50;
        target.ChocoboAutomationMode = source.ChocoboAutomationMode;
        target.ChocoboTargetPedigree = source.ChocoboTargetPedigree;
        target.ChocoboRetirementRank = source.ChocoboRetirementRank;
        target.ChocoboPreferredFeedGrade = source.ChocoboPreferredFeedGrade;
    }
}
