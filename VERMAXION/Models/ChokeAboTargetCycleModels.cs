using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace VERMAXION.Models;

public enum ChocoboBreedingMode { OwnedParents, NpcPermits }
public enum ChocoboFeedPolicy { FallBack, Skip, Stop }
public enum ChocoboBreedingGoal { ReachPedigree, AbilityOffspring, ColourOffspring }

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
    PurchasingSupplies,
}

public sealed record ChokeAboTargetCycleStatus(
    int Version,
    ulong ContentId,
    ChokeAboTargetCyclePhase Phase,
    bool ShouldBlockRacing,
    bool TargetReady,
    bool GameActionInProgress,
    string Reason,
    DateTimeOffset? NextCoveringEligibilityUtc,
    int Pedigree = 0,
    int RacingRank = 0,
    bool ProgressionComplete = false,
    bool CanResumeOwnedInteraction = false,
    uint InheritedAbilityId = 0,
    uint LearnedAbilityId = 0,
    uint ColourId = 0,
    bool RacerDataAvailable = false,
    ChocoboBreedingGoal OffspringGoal = ChocoboBreedingGoal.ReachPedigree,
    int MatchingOffspringProduced = 0,
    int MatchingOffspringRequested = 0,
    long OffspringCollected = 0,
    bool ProductionComplete = false);

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

    public static bool TryCreateEnsureRequestJson(ulong contentId, CharacterConfig config, out string json, out string error)
    {
        json = string.Empty;
        if (!Enum.IsDefined(config.ChocoboBreedingGoal))
        {
            error = "Choose a valid chocobo breeding goal.";
            return false;
        }
        if (!TryValidateIdentity(contentId, out error) ||
            !TryValidateSettings(config.ChocoboTargetPedigree, config.ChocoboRetirementRank, config.ChocoboPreferredFeedGrade, out error)) return false;
        if (!Enum.IsDefined(config.ChocoboBreedingMode) || !Enum.IsDefined(config.ChocoboFeedPolicy))
        {
            error = "Invalid breeding mode or feeding policy.";
            return false;
        }
        var production = config.ChocoboBreedingGoal != ChocoboBreedingGoal.ReachPedigree;
        var requested = config.ChocoboBreedingGoal == ChocoboBreedingGoal.AbilityOffspring
            ? config.ChocoboDesiredAbilityOffspringCount : config.ChocoboDesiredColourOffspringCount;
        if (production && (config.ChocoboTargetPedigree != 9 || requested < 1 ||
            config.ChocoboBreedingGoal == ChocoboBreedingGoal.AbilityOffspring && config.ChocoboDesiredInheritedAbilityId is 0 or > byte.MaxValue ||
            config.ChocoboBreedingGoal == ChocoboBreedingGoal.ColourOffspring && (config.ChocoboAcceptableColourIds == null ||
                config.ChocoboAcceptableColourIds.Count == 0 || config.ChocoboAcceptableColourIds.Any(id => id is 0 or > byte.MaxValue))))
        { error = "Production requires G9, a positive quantity, and a selected inherited ability or acceptable colours."; return false; }
        json = JsonSerializer.Serialize(new { version = 3, contentId, targetPedigree = config.ChocoboTargetPedigree,
            retirementRank = 40, preferredFeedGrade = config.ChocoboPreferredFeedGrade,
            breedingMode = (int)config.ChocoboBreedingMode, feedPolicy = (int)config.ChocoboFeedPolicy,
            produceCounterpart = !production && config.ChocoboBreedingMode == ChocoboBreedingMode.NpcPermits && config.ChocoboProduceCounterpart,
            gilReserve = config.ChocoboGilReserve, mgpReserve = config.ChocoboMgpReserve,
            offspringGoal = (int)config.ChocoboBreedingGoal,
            requestedOffspring = production ? requested : 0,
            desiredInheritedAbilityId = config.ChocoboDesiredInheritedAbilityId,
            acceptableColourIds = (config.ChocoboAcceptableColourIds ?? new List<uint>()).Distinct().Order().ToArray(),
            raceAdmissionAllowed = ChocoboDailyAllowance.Remaining(config, DateTime.UtcNow) > 0 && !config.ChocoboProgressionPaused });
        return true;
    }

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

    public static bool TryCreateIdentityRequestJson(ulong contentId, out string json, out string error, int version = Version)
    {
        json = string.Empty;
        if (!TryValidateIdentity(contentId, out error))
            return false;

        json = JsonSerializer.Serialize(new { version, contentId });
        return true;
    }

    public static bool TryParseStatus(
        string json,
        ulong expectedContentId,
        out ChokeAboTargetCycleStatus? status,
        out string error, int expectedVersion = Version)
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

            if (version != expectedVersion)
            {
                error = $"Choke-abo returned V{version}; V{expectedVersion} is required.";
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

            var pedigree = 0;
            var racingRank = 0;
            var complete = false;
            var canResumeOwnedInteraction = false;
            if (expectedVersion == 3)
            {
                if (!TryReadInt32(root, "pedigree", out pedigree, out error) ||
                    !TryReadInt32(root, "racingRank", out racingRank, out error) ||
                    !TryReadBoolean(root, "progressionComplete", out complete, out error)) return false;
                if (pedigree is < 0 or > 9 || racingRank is < 0 or > 50 ||
                    complete && (racingRank != 50 || pedigree < 2 || !targetReady || phase != ChokeAboTargetCyclePhase.TargetReady || gameActionInProgress))
                {
                    error = "Choke-abo V3 progress is inconsistent with current game-state completion.";
                    return false;
                }
                if (root.TryGetProperty("canResumeOwnedInteraction", out _) &&
                    !TryReadBoolean(root, "canResumeOwnedInteraction", out canResumeOwnedInteraction, out error)) return false;
            }
            uint inheritedId = 0, learnedId = 0, colourId = 0;
            if (root.TryGetProperty("inheritedAbilityId", out var inheritedAbility) &&
                    (inheritedAbility.ValueKind != JsonValueKind.Number || !inheritedAbility.TryGetUInt32(out inheritedId) || inheritedId > byte.MaxValue) ||
                root.TryGetProperty("learnedAbilityId", out var learnedAbility) &&
                    (learnedAbility.ValueKind != JsonValueKind.Number || !learnedAbility.TryGetUInt32(out learnedId) || learnedId > byte.MaxValue) ||
                root.TryGetProperty("colourId", out var colour) &&
                    (colour.ValueKind != JsonValueKind.Number || !colour.TryGetUInt32(out colourId) || colourId > byte.MaxValue))
            {
                error = "Choke-abo returned invalid racer ability or colour data.";
                return false;
            }
            var racerDataAvailable = false;
            if (root.TryGetProperty("racerDataAvailable", out var availability))
            {
                if (availability.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    error = "Choke-abo returned invalid racer-data availability.";
                    return false;
                }
                racerDataAvailable = availability.GetBoolean();
            }
            var offspringGoal = ChocoboBreedingGoal.ReachPedigree;
            var produced = 0;
            var requested = 0;
            long collected = 0;
            var productionComplete = false;
            if (root.TryGetProperty("offspringGoal", out var productionGoal))
            {
                if (productionGoal.ValueKind != JsonValueKind.Number || !productionGoal.TryGetInt32(out var goal) ||
                    !Enum.IsDefined((ChocoboBreedingGoal)goal) ||
                    !TryReadInt32(root, "matchingOffspringProduced", out produced, out error) || produced < 0 ||
                    !TryReadInt32(root, "matchingOffspringRequested", out requested, out error) || requested < 0 ||
                    !root.TryGetProperty("offspringCollected", out var attempts) || attempts.ValueKind != JsonValueKind.Number ||
                    !attempts.TryGetInt64(out collected) || collected < 0 ||
                    !TryReadBoolean(root, "productionComplete", out productionComplete, out error))
                { error = "Choke-abo returned invalid production progress."; return false; }
                offspringGoal = (ChocoboBreedingGoal)goal;
                if (offspringGoal != ChocoboBreedingGoal.ReachPedigree && (requested < 1 || complete || targetReady) || productionComplete &&
                    (offspringGoal == ChocoboBreedingGoal.ReachPedigree || phase != ChokeAboTargetCyclePhase.TargetReady ||
                     produced < requested || gameActionInProgress || !shouldBlockRacing || complete))
                { error = "Choke-abo returned inconsistent production completion."; return false; }
            }
            status = new ChokeAboTargetCycleStatus(
                version,
                contentId,
                phase,
                shouldBlockRacing,
                targetReady,
                gameActionInProgress,
                reason,
                nextCoveringEligibilityUtc, pedigree, racingRank, complete, canResumeOwnedInteraction,
                inheritedId, learnedId, colourId, racerDataAvailable,
                offspringGoal, produced, requested, collected, productionComplete);
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
        if (status.Version == 3 && (status.ProgressionComplete || status.ProductionComplete))
            return new ChocoboTargetHandoffDecision(ChocoboTargetHandoffAction.Complete, status.TargetReady, status.Reason);
        if (status.GameActionInProgress)
            return new ChocoboTargetHandoffDecision(ChocoboTargetHandoffAction.Wait, false, status.Reason);
        if (status.Version < 3 && completedRaces >= configuredRaces)
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
        if (target.ChocoboBreedingGoal != source.ChocoboBreedingGoal ||
            target.ChocoboProduceCounterpart != source.ChocoboProduceCounterpart)
            target.ChocoboProgressionPaused = true;
        target.ChocoboRacesPerDay = source.ChocoboRacesPerDay;
        target.SkipChocoboRacingAtRank50 = source.SkipChocoboRacingAtRank50;
        target.ChocoboAutomationMode = source.ChocoboAutomationMode;
        target.ChocoboTargetPedigree = source.ChocoboTargetPedigree;
        target.ChocoboRetirementRank = source.ChocoboRetirementRank;
        target.ChocoboPreferredFeedGrade = source.ChocoboPreferredFeedGrade;
        target.ChocoboBreedingMode = source.ChocoboBreedingMode;
        target.ChocoboProduceCounterpart = source.ChocoboProduceCounterpart;
        target.ChocoboBreedingGoal = source.ChocoboBreedingGoal;
        target.ChocoboDesiredInheritedAbilityId = source.ChocoboDesiredInheritedAbilityId;
        target.ChocoboDesiredAbilityOffspringCount = source.ChocoboDesiredAbilityOffspringCount;
        target.ChocoboAcceptableColourIds = new List<uint>(source.ChocoboAcceptableColourIds);
        target.ChocoboDesiredColourOffspringCount = source.ChocoboDesiredColourOffspringCount;
        target.ChocoboFeedPolicy = source.ChocoboFeedPolicy;
        target.ChocoboGilReserve = source.ChocoboGilReserve;
        target.ChocoboMgpReserve = source.ChocoboMgpReserve;
    }
}

public static class ChocoboDailyAllowance
{
    public const double LimitSeconds = 3 * 60 * 60;

    public static bool Sample(CharacterConfig config, DateTime utc, bool racingActivity, bool afterReload = false)
    {
        var wasActive = config.ChocoboRacePending;
        var changed = Account(config, config.ChocoboAllowanceSampleUtc, utc,
            wasActive && (!afterReload || racingActivity));
        config.ChocoboRacePending = racingActivity;
        config.ChocoboAllowanceSampleUtc = racingActivity ? utc : DateTime.MinValue;
        return changed || wasActive != racingActivity;
    }

    public static DateTime ResetAt(DateTime utc)
    {
        var reset = utc.Date.AddHours(9);
        return utc < reset ? reset.AddDays(-1) : reset;
    }

    public static bool Account(CharacterConfig config, DateTime fromUtc, DateTime toUtc, bool racingActivity)
    {
        var reset = ResetAt(toUtc);
        var changed = false;
        if (reset > config.ChocoboAllowanceResetUtc)
        {
            config.ChocoboAllowanceResetUtc = reset;
            config.ChocoboAllowanceSecondsUsed = 0;
            changed = true;
        }
        // Only the current server day survives a reset. The interval before
        // 09:00 belongs to the preceding allowance, even during the same race.
        var start = fromUtc > config.ChocoboAllowanceResetUtc ? fromUtc : config.ChocoboAllowanceResetUtc;
        if (racingActivity && fromUtc != DateTime.MinValue && toUtc > start)
        {
            config.ChocoboAllowanceSecondsUsed += (toUtc - start).TotalSeconds;
            changed = true;
        }
        return changed;
    }

    public static double Remaining(CharacterConfig config, DateTime utc)
        => ResetAt(utc) > config.ChocoboAllowanceResetUtc ? LimitSeconds
            : !double.IsFinite(config.ChocoboAllowanceSecondsUsed) || config.ChocoboAllowanceSecondsUsed < 0 ? 0
            : Math.Max(0, LimitSeconds - config.ChocoboAllowanceSecondsUsed);
}
