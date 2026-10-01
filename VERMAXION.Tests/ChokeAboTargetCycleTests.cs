using System;
using System.Text.Json;
using VERMAXION.Models;
using Xunit;

namespace VERMAXION.Tests;

public sealed class ChokeAboTargetCycleTests
{
    private const ulong ContentId = 1234567890123456789UL;

    [Fact]
    public void OffspringWorkflowsCarryIndependentQuantitiesAndRequireTypedCompletion()
    {
        var config = CharacterConfig.CreateNew();
        config.ChocoboTargetPedigree = 9;
        config.ChocoboBreedingGoal = ChocoboBreedingGoal.AbilityOffspring;
        config.ChocoboDesiredInheritedAbilityId = 28;
        config.ChocoboDesiredAbilityOffspringCount = 3;
        config.ChocoboAcceptableColourIds.AddRange(new uint[] { 74, 36 });
        config.ChocoboDesiredColourOffspringCount = 5;
        config.ChocoboProduceCounterpart = true;
        Assert.True(ChokeAboTargetCycleProtocol.TryCreateEnsureRequestJson(ContentId, config, out var json, out var error), error);
        using (var document = JsonDocument.Parse(json))
        {
            Assert.Equal(1, document.RootElement.GetProperty("offspringGoal").GetInt32());
            Assert.Equal(3, document.RootElement.GetProperty("requestedOffspring").GetInt32());
            Assert.Equal(28U, document.RootElement.GetProperty("desiredInheritedAbilityId").GetUInt32());
            Assert.False(document.RootElement.GetProperty("produceCounterpart").GetBoolean());
            Assert.Equal(100000U, document.RootElement.GetProperty("gilReserve").GetUInt32());
        }
        config.ChocoboBreedingGoal = ChocoboBreedingGoal.ColourOffspring;
        Assert.True(ChokeAboTargetCycleProtocol.TryCreateEnsureRequestJson(ContentId, config, out json, out error), error);
        using (var document = JsonDocument.Parse(json))
        {
            Assert.Equal(2, document.RootElement.GetProperty("offspringGoal").GetInt32());
            Assert.Equal(5, document.RootElement.GetProperty("requestedOffspring").GetInt32());
            Assert.Equal(36U, document.RootElement.GetProperty("acceptableColourIds")[0].GetUInt32());
        }
        string Status(int produced, bool completed, bool busy = false) => JsonSerializer.Serialize(new
        {
            version = 3, contentId = ContentId, phase = "TargetReady", shouldBlockRacing = true,
            targetReady = false, gameActionInProgress = busy, reason = "Retained matching offspring",
            nextCoveringEligibilityUtc = (string?)null, pedigree = 0, racingRank = 0, progressionComplete = false,
            offspringGoal = 1, matchingOffspringProduced = produced, matchingOffspringRequested = 3,
            offspringCollected = 4L, productionComplete = completed,
        });
        Assert.True(ChokeAboTargetCycleProtocol.TryParseStatus(Status(3, true), ContentId, out var status, out error, 3), error);
        Assert.True(status!.ProductionComplete);
        Assert.False(status.ProgressionComplete);
        var decision = ChocoboTargetCyclePolicy.DecideHandoff(ChokeAboTargetCycleCallResult.Success(status), 0, 5);
        Assert.Equal(ChocoboTargetHandoffAction.Complete, decision.Action);
        Assert.False(decision.TargetReady);
        Assert.False(ChokeAboTargetCycleProtocol.TryParseStatus(Status(2, true), ContentId, out _, out _, 3));
        Assert.False(ChokeAboTargetCycleProtocol.TryParseStatus(Status(3, true, true), ContentId, out _, out _, 3));
        Assert.False(ChokeAboTargetCycleProtocol.TryParseStatus(Status(-1, false), ContentId, out _, out _, 3));
        config.ChocoboAcceptableColourIds.Clear();
        Assert.False(ChokeAboTargetCycleProtocol.TryCreateEnsureRequestJson(ContentId, config, out _, out _));
    }

    [Fact]
    public void OwnedInteractionReadinessRequiresTypedV3EvidenceForTheActiveCharacter()
    {
        var json = JsonSerializer.Serialize(new
        {
            version = 3, contentId = ContentId, phase = "Paused", shouldBlockRacing = true,
            targetReady = false, gameActionInProgress = false, reason = "Pending owned covering confirmation",
            nextCoveringEligibilityUtc = (string?)null, pedigree = 0, racingRank = 0, progressionComplete = false,
            canResumeOwnedInteraction = true,
        });
        Assert.True(ChokeAboTargetCycleProtocol.TryParseStatus(json, ContentId, out var status, out var error, 3), error);
        Assert.True(status!.CanResumeOwnedInteraction);
        Assert.False(ChokeAboTargetCycleProtocol.TryParseStatus(json, ContentId + 1, out _, out _, 3));
        Assert.False(ChokeAboTargetCycleProtocol.TryParseStatus(json.Replace("\"canResumeOwnedInteraction\":true", "\"canResumeOwnedInteraction\":\"true\""), ContentId, out _, out _, 3));
        var older = json.Replace(",\"canResumeOwnedInteraction\":true", "");
        Assert.True(ChokeAboTargetCycleProtocol.TryParseStatus(older, ContentId, out status, out error, 3), error);
        Assert.False(status!.CanResumeOwnedInteraction);
    }

    [Fact]
    public void AllowanceSamplesExcludePreparationAndRetainOnlyVerifiedActivityAcrossReload()
    {
        var config = CharacterConfig.CreateNew();
        var now = new DateTime(2026, 9, 23, 10, 0, 0, DateTimeKind.Utc);
        ChocoboDailyAllowance.Sample(config, now, false);
        ChocoboDailyAllowance.Sample(config, now.AddMinutes(20), true);
        Assert.Equal(0, config.ChocoboAllowanceSecondsUsed);
        ChocoboDailyAllowance.Sample(config, now.AddMinutes(21), true);
        var reloaded = JsonSerializer.Deserialize<CharacterConfig>(JsonSerializer.Serialize(config))!;
        ChocoboDailyAllowance.Sample(reloaded, now.AddMinutes(22), true, afterReload: true);
        Assert.Equal(120, reloaded.ChocoboAllowanceSecondsUsed);
        ChocoboDailyAllowance.Sample(reloaded, now.AddMinutes(23), false);
        Assert.Equal(180, reloaded.ChocoboAllowanceSecondsUsed);
        Assert.False(reloaded.ChocoboRacePending);
        Assert.Equal(DateTime.MinValue, reloaded.ChocoboAllowanceSampleUtc);
        ChocoboDailyAllowance.Sample(reloaded, now.AddHours(2), true, afterReload: true);
        Assert.Equal(180, reloaded.ChocoboAllowanceSecondsUsed);
        ChocoboDailyAllowance.Sample(reloaded, now.AddHours(3), false, afterReload: true);
        Assert.Equal(180, reloaded.ChocoboAllowanceSecondsUsed); // A finished queue cannot charge an unknown unload interval.
    }

    [Fact]
    public void ProgressionAllowanceAndV3BoundariesSurviveReloadWithoutLosingLimits()
    {
        var config = CharacterConfig.CreateNew();
        config.ChocoboAutomationMode = ChocoboAutomationMode.TargetPedigree;
        config.ChocoboProgressionPaused = false;
        Assert.Equal(ChocoboFeedPolicy.Skip, config.ChocoboFeedPolicy);
        Assert.True(ChokeAboTargetCycleProtocol.TryCreateEnsureRequestJson(ContentId, config, out var request, out var error), error);
        using (var doc = JsonDocument.Parse(request))
        {
            Assert.Equal(3, doc.RootElement.GetProperty("version").GetInt32());
            Assert.Equal(100000, doc.RootElement.GetProperty("gilReserve").GetInt32());
            Assert.Equal(50000, doc.RootElement.GetProperty("mgpReserve").GetInt32());
            Assert.Equal((int)ChocoboFeedPolicy.Skip, doc.RootElement.GetProperty("feedPolicy").GetInt32());
            Assert.True(doc.RootElement.GetProperty("raceAdmissionAllowed").GetBoolean());
        }
        var day = new DateTime(2026, 9, 23, 9, 0, 0, DateTimeKind.Utc);
        ChocoboDailyAllowance.Account(config, day, day.AddHours(2), racingActivity: true);
        Assert.Equal(3600, ChocoboDailyAllowance.Remaining(config, day.AddHours(2)));
        ChocoboDailyAllowance.Account(config, day.AddHours(2), day.AddHours(4), racingActivity: false);
        Assert.Equal(3600, ChocoboDailyAllowance.Remaining(config, day.AddHours(4)));
        var resumed = JsonSerializer.Deserialize<CharacterConfig>(JsonSerializer.Serialize(config))!;
        ChocoboDailyAllowance.Account(resumed, day.AddHours(4), day.AddHours(5), racingActivity: true);
        Assert.Equal(0, ChocoboDailyAllowance.Remaining(resumed, day.AddHours(5)));
        ChocoboDailyAllowance.Account(resumed, day.AddDays(1).AddSeconds(-30), day.AddDays(1).AddSeconds(30), racingActivity: true);
        Assert.Equal(30, resumed.ChocoboAllowanceSecondsUsed);
        Assert.Equal(10770, ChocoboDailyAllowance.Remaining(resumed, day.AddDays(1).AddSeconds(30)));
        resumed.ChocoboProgressionPaused = true;
        Assert.True(resumed.Clone().ChocoboProgressionPaused);
        Assert.True(ChokeAboTargetCycleProtocol.TryCreateEnsureRequestJson(ContentId, resumed, out var pausedRequest, out error), error);
        using (var paused = JsonDocument.Parse(pausedRequest))
            Assert.False(paused.RootElement.GetProperty("raceAdmissionAllowed").GetBoolean());
        var exhausted = config.Clone();
        exhausted.ChocoboAllowanceResetUtc = ChocoboDailyAllowance.ResetAt(DateTime.UtcNow);
        exhausted.ChocoboAllowanceSecondsUsed = ChocoboDailyAllowance.LimitSeconds;
        Assert.True(ChokeAboTargetCycleProtocol.TryCreateEnsureRequestJson(ContentId, exhausted, out var exhaustedRequest, out error), error);
        using (var noAllowance = JsonDocument.Parse(exhaustedRequest))
            Assert.False(noAllowance.RootElement.GetProperty("raceAdmissionAllowed").GetBoolean());
        var copied = CharacterConfig.CreateNew();
        copied.ChocoboAllowanceSecondsUsed = 321;
        ChocoboTargetCyclePolicy.CopySettings(resumed, copied);
        Assert.Equal(321, copied.ChocoboAllowanceSecondsUsed); // Account defaults cannot replenish a character's allowance.

        var legacy = StatusJson("Racing", false, false, false);
        Assert.False(ChokeAboTargetCycleProtocol.TryParseStatus(legacy, ContentId, out _, out _, 3));
        var complete = JsonSerializer.Serialize(new { version = 3, contentId = ContentId, phase = "TargetReady",
            shouldBlockRacing = false, targetReady = true, gameActionInProgress = false, reason = "G9 rank 50",
            pedigree = 9, racingRank = 50, progressionComplete = true });
        Assert.True(ChokeAboTargetCycleProtocol.TryParseStatus(complete, ContentId, out var status, out error, 3), error);
        Assert.Equal(ChocoboTargetHandoffAction.Complete,
            ChocoboTargetCyclePolicy.DecideHandoff(ChokeAboTargetCycleCallResult.Success(status!), 0, 5).Action);
        Assert.False(ChokeAboTargetCycleProtocol.TryParseStatus(complete.Replace("\"racingRank\":50", "\"racingRank\":49"), ContentId, out _, out _, 3));
    }

    [Fact]
    public void FreshAndLegacyCharactersKeepAlwaysRaceDefaults()
    {
        var fresh = CharacterConfig.CreateNew();
        var legacy = JsonSerializer.Deserialize<CharacterConfig>("{}")!;

        foreach (var config in new[] { fresh, legacy })
        {
            Assert.Equal(ChocoboAutomationMode.AlwaysRace, config.ChocoboAutomationMode);
            Assert.Equal(9, config.ChocoboTargetPedigree);
            Assert.Equal(40, config.ChocoboRetirementRank);
            Assert.Equal(3, config.ChocoboPreferredFeedGrade);
        }
    }

    [Fact]
    public void CloneAndDefaultCopyPreserveTargetSettings()
    {
        var source = CharacterConfig.CreateNew();
        source.ChocoboRacesPerDay = 17;
        source.SkipChocoboRacingAtRank50 = false;
        source.ChocoboAutomationMode = ChocoboAutomationMode.TargetPedigree;
        source.ChocoboTargetPedigree = 7;
        source.ChocoboRetirementRank = 46;
        source.ChocoboPreferredFeedGrade = 2;

        var clone = source.Clone();
        var copied = CharacterConfig.CreateNew();
        ChocoboTargetCyclePolicy.CopySettings(source, copied);

        foreach (var config in new[] { clone, copied })
        {
            Assert.Equal(17, config.ChocoboRacesPerDay);
            Assert.False(config.SkipChocoboRacingAtRank50);
            Assert.Equal(ChocoboAutomationMode.TargetPedigree, config.ChocoboAutomationMode);
            Assert.Equal(7, config.ChocoboTargetPedigree);
            Assert.Equal(46, config.ChocoboRetirementRank);
            Assert.Equal(2, config.ChocoboPreferredFeedGrade);
        }
    }

    [Fact]
    public void EnsureRequestCarriesStrictV2IdentityAndInputs()
    {
        Assert.True(ChokeAboTargetCycleProtocol.TryCreateEnsureRequestJson(
            ContentId,
            8,
            44,
            2,
            out var json,
            out var error), error);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(2, root.GetProperty("version").GetInt32());
        Assert.Equal(ContentId, root.GetProperty("contentId").GetUInt64());
        Assert.Equal(8, root.GetProperty("targetPedigree").GetInt32());
        Assert.Equal(44, root.GetProperty("retirementRank").GetInt32());
        Assert.Equal(2, root.GetProperty("preferredFeedGrade").GetInt32());
    }

    [Theory]
    [InlineData(0, 9, 40, 3)]
    [InlineData(1, 1, 40, 3)]
    [InlineData(1, 10, 40, 3)]
    [InlineData(1, 9, 39, 3)]
    [InlineData(1, 9, 51, 3)]
    [InlineData(1, 9, 40, 0)]
    [InlineData(1, 9, 40, 4)]
    public void InvalidIdentityOrRangesNeverCreateEnsureJson(
        ulong contentId,
        int targetPedigree,
        int retirementRank,
        int preferredFeedGrade)
    {
        Assert.False(ChokeAboTargetCycleProtocol.TryCreateEnsureRequestJson(
            contentId,
            targetPedigree,
            retirementRank,
            preferredFeedGrade,
            out _,
            out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void ValidStatusRequiresMatchingIdentityAndKnownPhase()
    {
        var json = StatusJson("Racing", shouldBlock: false, targetReady: false, gameAction: false);

        Assert.True(ChokeAboTargetCycleProtocol.TryParseStatus(
            json,
            ContentId,
            out var status,
            out var error), error);
        Assert.Equal(ChokeAboTargetCyclePhase.Racing, status!.Phase);
        Assert.Equal(ContentId, status.ContentId);

        Assert.False(ChokeAboTargetCycleProtocol.TryParseStatus(json, ContentId + 1, out _, out error));
        Assert.Contains("does not match", error);

        var unknownPhase = StatusJson("FuturePhase", shouldBlock: true, targetReady: false, gameAction: false);
        Assert.False(ChokeAboTargetCycleProtocol.TryParseStatus(unknownPhase, ContentId, out _, out error));
        Assert.Contains("unknown", error);
    }

    [Fact]
    public void ManualOwnedBlockedPreflightRemainsValidWithoutInferringAnOwner()
    {
        var json = StatusJson("Blocked", shouldBlock: false, targetReady: false, gameAction: false);

        Assert.True(ChokeAboTargetCycleProtocol.TryParseStatus(json, ContentId, out var status, out var error), error);
        Assert.False(status!.ShouldBlockRacing);
    }

    [Fact]
    public void RequiredBooleanFieldsRejectStringValues()
    {
        var json =
            $"{{\"version\":2,\"contentId\":{ContentId},\"phase\":\"Racing\",\"shouldBlockRacing\":\"false\",\"targetReady\":false,\"gameActionInProgress\":false,\"reason\":\"race\"}}";

        Assert.False(ChokeAboTargetCycleProtocol.TryParseStatus(json, ContentId, out _, out var error));
        Assert.Contains("shouldBlockRacing", error);
    }

    [Fact]
    public void CoveringEligibilityMustBeUtcAndBelongToCoveringWait()
    {
        var valid = StatusJson(
            "CoveringWait",
            shouldBlock: true,
            targetReady: false,
            gameAction: false,
            nextEligibility: "2026-08-29T12:34:56+00:00");
        Assert.True(ChokeAboTargetCycleProtocol.TryParseStatus(valid, ContentId, out var status, out var error), error);
        Assert.Equal(TimeSpan.Zero, status!.NextCoveringEligibilityUtc!.Value.Offset);

        var localOffset = StatusJson(
            "CoveringWait",
            shouldBlock: true,
            targetReady: false,
            gameAction: false,
            nextEligibility: "2026-08-29T08:34:56-04:00");
        Assert.False(ChokeAboTargetCycleProtocol.TryParseStatus(localOffset, ContentId, out _, out error));

        var wrongPhase = StatusJson(
            "Blocked",
            shouldBlock: true,
            targetReady: false,
            gameAction: false,
            nextEligibility: "2026-08-29T12:34:56Z");
        Assert.False(ChokeAboTargetCycleProtocol.TryParseStatus(wrongPhase, ContentId, out _, out error));
    }

    [Fact]
    public void MissingMalformedAndDuplicateFieldsFailClosed()
    {
        Assert.False(ChokeAboTargetCycleProtocol.TryParseStatus("", ContentId, out _, out _));
        Assert.False(ChokeAboTargetCycleProtocol.TryParseStatus("{", ContentId, out _, out _));
        Assert.False(ChokeAboTargetCycleProtocol.TryParseStatus(
            "{\"version\":2}",
            ContentId,
            out _,
            out _));

        var duplicate =
            $"{{\"version\":2,\"version\":2,\"contentId\":{ContentId},\"phase\":\"Racing\",\"shouldBlockRacing\":false,\"targetReady\":false,\"gameActionInProgress\":false,\"reason\":\"race\"}}";
        Assert.False(ChokeAboTargetCycleProtocol.TryParseStatus(duplicate, ContentId, out _, out var error));
        Assert.Contains("duplicate", error);

        var wrongVersion = StatusJson("Racing", false, false, false).Replace("\"version\":2", "\"version\":3");
        Assert.False(ChokeAboTargetCycleProtocol.TryParseStatus(wrongVersion, ContentId, out _, out error));
        Assert.Contains("V3", error);
    }

    [Fact]
    public void TargetModeFailsClosedWhileAlwaysRaceRemainsTheDefault()
    {
        var unavailable = ChokeAboTargetCycleCallResult.Failure("V2 unavailable");
        var decision = ChocoboTargetCyclePolicy.DecideHandoff(unavailable, completedRaces: 0, configuredRaces: 5);

        Assert.Equal(ChocoboAutomationMode.AlwaysRace, CharacterConfig.CreateNew().ChocoboAutomationMode);
        Assert.Equal(ChocoboTargetHandoffAction.Defer, decision.Action);
        Assert.Equal("V2 unavailable", decision.Reason);
    }

    [Theory]
    [InlineData("Planning", true, false, true, ChocoboTargetHandoffAction.Wait)]
    [InlineData("Blocked", true, false, false, ChocoboTargetHandoffAction.Defer)]
    [InlineData("CoveringWait", true, false, false, ChocoboTargetHandoffAction.Defer)]
    [InlineData("Racing", false, false, false, ChocoboTargetHandoffAction.Race)]
    [InlineData("TargetReady", false, true, false, ChocoboTargetHandoffAction.Race)]
    public void RaceAndDeferDecisionsFollowV2Ownership(
        string phase,
        bool shouldBlock,
        bool targetReady,
        bool gameAction,
        ChocoboTargetHandoffAction expected)
    {
        var result = ParseResult(StatusJson(phase, shouldBlock, targetReady, gameAction));
        var decision = ChocoboTargetCyclePolicy.DecideHandoff(result, completedRaces: 1, configuredRaces: 5);

        Assert.Equal(expected, decision.Action);
        Assert.Equal(targetReady, decision.TargetReady);
    }

    [Fact]
    public void MidBatchHandoffWaitsForActionThenDefersOrResumes()
    {
        var action = ParseResult(StatusJson("Feeding", true, false, true));
        var blocked = ParseResult(StatusJson("RegistrationPendingCapture", true, false, false));
        var racing = ParseResult(StatusJson("Racing", false, false, false));

        Assert.Equal(
            ChocoboTargetHandoffAction.Wait,
            ChocoboTargetCyclePolicy.DecideHandoff(action, 2, 5).Action);
        Assert.Equal(
            ChocoboTargetHandoffAction.Defer,
            ChocoboTargetCyclePolicy.DecideHandoff(blocked, 2, 5).Action);
        Assert.Equal(
            ChocoboTargetHandoffAction.Race,
            ChocoboTargetCyclePolicy.DecideHandoff(racing, 2, 5).Action);
    }

    [Fact]
    public void CompletedBatchWaitsForImmediateActionThenCompletesAtStableBoundary()
    {
        var action = ParseResult(StatusJson("Feeding", true, false, true));
        var blocked = ParseResult(StatusJson("RetirementPendingCapture", true, false, false));

        Assert.Equal(
            ChocoboTargetHandoffAction.Wait,
            ChocoboTargetCyclePolicy.DecideHandoff(action, 5, 5).Action);
        Assert.Equal(
            ChocoboTargetHandoffAction.Complete,
            ChocoboTargetCyclePolicy.DecideHandoff(blocked, 5, 5).Action);
    }

    [Fact]
    public void DeferredTerminalNeverRequestsCompletionOrFailurePersistence()
    {
        Assert.Equal(
            ChocoboTaskTerminalAction.AdvanceDeferred,
            ChocoboTargetCyclePolicy.ClassifyTerminal(false, false, true));
        Assert.Equal(
            ChocoboTaskTerminalAction.PersistCompletion,
            ChocoboTargetCyclePolicy.ClassifyTerminal(true, false, false));
        Assert.Equal(
            ChocoboTaskTerminalAction.PersistFailure,
            ChocoboTargetCyclePolicy.ClassifyTerminal(false, true, false));
    }

    private static ChokeAboTargetCycleCallResult ParseResult(string json)
    {
        Assert.True(ChokeAboTargetCycleProtocol.TryParseStatus(json, ContentId, out var status, out var error), error);
        return ChokeAboTargetCycleCallResult.Success(status!);
    }

    private static string StatusJson(
        string phase,
        bool shouldBlock,
        bool targetReady,
        bool gameAction,
        string? nextEligibility = null)
        => JsonSerializer.Serialize(new
        {
            version = 2,
            contentId = ContentId,
            phase,
            shouldBlockRacing = shouldBlock,
            targetReady,
            gameActionInProgress = gameAction,
            reason = $"{phase} reason",
            nextCoveringEligibilityUtc = nextEligibility,
        });
}
