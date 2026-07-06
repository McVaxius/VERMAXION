#nullable enable

using System;
using System.Linq;
using System.Numerics;
using VERMAXION.Models;
using Xunit;

namespace VERMAXION.Tests;

public sealed class FishingPolicyTests
{
    [Fact]
    public void DefaultExecutionModeUsesAutoRetainerCurrentAccountRelog()
    {
        Assert.Equal(FishingExecutionMode.AutoRetainerRelogCurrentAccount, FishingDefaults.ExecutionMode);
        Assert.Equal(-1, FishingDefaults.OceanFishingPreWindowOffsetMinutes);
        Assert.Equal(-10, FishingDefaults.MinOceanFishingPreWindowOffsetMinutes);
        Assert.Equal(0, FishingDefaults.MaxOceanFishingPreWindowOffsetMinutes);
        Assert.Equal(15, FishingDefaults.OceanFishingRegistrationAvailabilityMinutes);
    }

    [Fact]
    public void DefaultOperationSettingsUseFcReturnAndNpcNoInnRepair()
    {
        Assert.Equal(22, FishingDefaults.LureRestockTarget);
        Assert.Equal(FishingReturnDestination.FreeCompany, FishingDefaults.ReturnDestination);
        Assert.Equal("/li fc", FishingDefaults.ReturnCommand);
        Assert.Equal(FishingRepairMode.NpcNoInn, FishingDefaults.RepairMode);
        Assert.Equal(50, FishingDefaults.RepairThresholdPercent);

        var settings = new FishingOperationSettings(
            FishingDefaults.LureRestockTarget,
            FishingDefaults.ReturnDestination,
            FishingDefaults.ReturnCommand,
            FishingDefaults.RepairMode,
            FishingDefaults.RepairThresholdPercent);

        Assert.Equal("/li fc", FishingOperationPolicy.ResolveReturnCommand(settings));

        var decision = FishingOperationPolicy.EvaluateRepair(
            settings,
            durabilityKnown: true,
            lowestDurabilityPercent: FishingDefaults.RepairThresholdPercent);

        Assert.True(decision.ShouldRepair);
        Assert.Equal("npc-no-inn", decision.AdsMode);
    }

    [Fact]
    public void XadbParserReadsFisherFromJobLevelsAndJobs()
    {
        const string json = """
        {
          "version": 2,
          "ipcContractVersion": 6,
          "generatedAtUtc": "2026-07-03T15:55:00Z",
          "isFullRosterAvailable": true,
          "characters": [
            {
              "characterName": "Alpha One",
              "worldName": "Cactuar",
              "source": "xa_characters",
              "lastSnapshotUtc": "2026-07-03T15:50:00Z",
              "jobLevels": { "18": 42 }
            },
            {
              "characterKey": "Beta Two@Gilgamesh",
              "source": "xa_characters",
              "lastSnapshotUtc": "2026-07-02T10:00:00Z",
              "jobs": [
                { "jobId": 19, "jobAbbrev": "PLD", "level": 90 },
                { "jobId": 18, "jobAbbrev": "FSH", "level": 73 }
              ]
            },
            {
              "characterKey": "Gamma Three@Siren",
              "source": "legacy",
              "lastSnapshotUtc": "2026-06-01T00:00:00Z",
              "jobLevels": { "FSH": 12 }
            }
          ]
        }
        """;

        var roster = XaFishingRosterParser.Parse(json);
        var characters = roster.Characters.ToDictionary(entry => entry.CharacterKey);

        Assert.Equal(XaFishingRosterReadStatus.Ready, roster.Status);
        Assert.True(roster.IsUsable);
        Assert.Equal(DateTimeOffset.Parse("2026-07-03T15:55:00Z"), roster.GeneratedAtUtc);
        Assert.Equal(42, characters["Alpha One@Cactuar"].FisherLevel);
        Assert.Equal("xa_characters", characters["Alpha One@Cactuar"].Source);
        Assert.Equal(DateTimeOffset.Parse("2026-07-03T15:50:00Z"), characters["Alpha One@Cactuar"].SnapshotTimestamp);
        Assert.Equal(73, characters["Beta Two@Gilgamesh"].FisherLevel);
        Assert.Equal(12, characters["Gamma Three@Siren"].FisherLevel);
    }

    [Fact]
    public void XadbParserFailsClosedForUnavailableOrMalformedFullRoster()
    {
        var unavailable = XaFishingRosterParser.Parse(
            """{ "ipcContractVersion": 6, "isFullRosterAvailable": false, "characters": [] }""");
        var missingStatus = XaFishingRosterParser.Parse(
            """{ "ipcContractVersion": 6, "characters": [] }""");
        var malformedCharacter = XaFishingRosterParser.Parse(
            """{ "ipcContractVersion": 6, "isFullRosterAvailable": true, "characters": [{ "characterKey": "Bad@World", "jobLevels": [] }] }""");

        Assert.Equal(XaFishingRosterReadStatus.FullRosterUnavailable, unavailable.Status);
        Assert.False(unavailable.IsUsable);
        Assert.Equal(XaFishingRosterReadStatus.MalformedResponse, missingStatus.Status);
        Assert.False(missingStatus.IsUsable);
        Assert.Equal(XaFishingRosterReadStatus.MalformedResponse, malformedCharacter.Status);
        Assert.False(malformedCharacter.IsUsable);
    }

    [Fact]
    public void XadbParserFailsClosedForMissingOrUnsupportedContract()
    {
        var missingContract = XaFishingRosterParser.Parse(
            """{ "isFullRosterAvailable": true, "characters": [] }""");
        var oldContract = XaFishingRosterParser.Parse(
            """{ "ipcContractVersion": 5, "isFullRosterAvailable": true, "characters": [] }""");

        Assert.Equal(XaFishingRosterReadStatus.UnsupportedContract, missingContract.Status);
        Assert.False(missingContract.IsUsable);
        Assert.Contains("XADB 0.0.0.39+ contract v6", missingContract.Detail);
        Assert.Equal(XaFishingRosterReadStatus.UnsupportedContract, oldContract.Status);
        Assert.False(oldContract.IsUsable);
        Assert.Contains("contract v5 is unsupported", oldContract.Detail);
        Assert.Contains("XADB 0.0.0.39+ contract v6", oldContract.Detail);
    }

    [Fact]
    public void CurrentCharacterSelectionUsesXadbLevelDespiteBogusLiveLevel()
    {
        const int bogusLiveLevel = 0;
        var roster = XaFishingRosterParser.Parse(
            """
            {
              "ipcContractVersion": 6,
              "isFullRosterAvailable": true,
              "characters": [
                {
                  "characterKey": "Current@World",
                  "source": "xa_characters",
                  "lastSnapshotUtc": "2026-07-03T15:50:00Z",
                  "jobLevels": { "18": 53 }
                }
              ]
            }
            """);
        var candidates = FishingXadbCandidatePolicy.ApplyAuthoritativeLevels(
            [new("Current@World", bogusLiveLevel, true, false, true)],
            roster);
        var xadbCurrent = Assert.Single(candidates);
        var selection = FishingSelectionPolicy.Select(
            candidates,
            maxFisherLevel: 100,
            FishingExecutionMode.AutoRetainerRelogCurrentAccount,
            currentCharacterKey: "Current@World",
            fishingWindowActive: true);

        Assert.NotEqual(bogusLiveLevel, selection.FisherLevel);
        Assert.Equal(53, selection.FisherLevel);
    }

    [Fact]
    public void SelectionUsesLowestEnabledFisherBelowMax()
    {
        var result = FishingSelectionPolicy.Select(
            [
                new("High@World", 99, true, false, false),
                new("Low@World", 11, true, false, false),
                new("Disabled@World", 1, false, false, false),
            ],
            maxFisherLevel: 100,
            FishingExecutionMode.AutoRetainerRelogCurrentAccount,
            currentCharacterKey: "High@World",
            fishingWindowActive: false);

        Assert.True(result.Selected);
        Assert.Equal("Low@World", result.CharacterKey);
        Assert.True(result.RequiresRelog);
    }

    [Fact]
    public void DisabledCurrentCharacterDoesNotBlockRelogTargetSelection()
    {
        var result = FishingStartupPolicy.SelectStartupTarget(
            [
                new("Current@World", 99, false, false, true),
                new("Other@World", 12, true, false, false),
            ],
            maxFisherLevel: 100,
            FishingExecutionMode.AutoRetainerRelogCurrentAccount,
            currentCharacterKey: "Current@World",
            startupWindowActive: true);

        Assert.True(result.Selected);
        Assert.Equal("Other@World", result.CharacterKey);
        Assert.True(result.RequiresRelog);
    }

    [Fact]
    public void StartupPolicyDoesNotSelectTargetOutsideVermaxionWindow()
    {
        var result = FishingStartupPolicy.SelectStartupTarget(
            [
                new("Current@World", 12, true, false, true),
                new("Other@World", 1, true, false, false),
            ],
            maxFisherLevel: 100,
            FishingExecutionMode.AutoRetainerRelogCurrentAccount,
            currentCharacterKey: "Current@World",
            startupWindowActive: false);

        Assert.False(result.Selected);
        Assert.False(FishingStartupPolicy.ShouldStartOnCurrentCharacter(result, "Current@World"));
    }

    [Fact]
    public void RelogRequiredTargetIsSelectedOnlyDuringStartupWindow()
    {
        var candidates = new[]
        {
            new FishingCharacterCandidate("Current@World", 90, true, false, true),
            new FishingCharacterCandidate("Other@World", 10, true, false, false),
        };

        var inactive = FishingStartupPolicy.SelectStartupTarget(
            candidates,
            maxFisherLevel: 100,
            FishingExecutionMode.AutoRetainerRelogCurrentAccount,
            currentCharacterKey: "Current@World",
            startupWindowActive: false);
        var active = FishingStartupPolicy.SelectStartupTarget(
            candidates,
            maxFisherLevel: 100,
            FishingExecutionMode.AutoRetainerRelogCurrentAccount,
            currentCharacterKey: "Current@World",
            startupWindowActive: true);

        Assert.False(inactive.Selected);
        Assert.Equal("Other@World", active.CharacterKey);
        Assert.True(active.RequiresRelog);
    }

    [Fact]
    public void AlwaysFishPriorityWinsWithoutMutatingOtherFlags()
    {
        var result = FishingSelectionPolicy.Select(
            [
                new("Alpha@World", 90, true, true, false),
                new("Beta@World", 10, true, false, true),
                new("Gamma@World", 20, true, true, false),
            ],
            maxFisherLevel: 100,
            FishingExecutionMode.AutoRetainerRelogCurrentAccount,
            currentCharacterKey: "Beta@World",
            fishingWindowActive: true);

        Assert.Equal("Gamma@World", result.CharacterKey);
        Assert.True(result.RequiresRelog);
        Assert.Empty(result.AlwaysFishKeysToDisable);
    }

    [Fact]
    public void CurrentCharacterOnlyModeDoesNotRelog()
    {
        var result = FishingSelectionPolicy.Select(
            [
                new("Current@World", 21, true, false, true),
                new("Lower@World", 1, true, false, false),
            ],
            maxFisherLevel: 100,
            FishingExecutionMode.CurrentCharacterOnly,
            currentCharacterKey: "Current@World",
            fishingWindowActive: false);

        Assert.Equal("Current@World", result.CharacterKey);
        Assert.False(result.RequiresRelog);
    }

    [Fact]
    public void MaxLevelExcludesNormalCandidates()
    {
        var result = FishingSelectionPolicy.Select(
            [
                new("Done@World", 100, true, false, true),
                new("AlsoDone@World", 100, true, false, false),
            ],
            maxFisherLevel: 100,
            FishingExecutionMode.AutoRetainerRelogCurrentAccount,
            currentCharacterKey: "Done@World",
            fishingWindowActive: false);

        Assert.False(result.Selected);
    }

    [Fact]
    public void CandidateQueueOrdersAlwaysFishThenKnownLevelsAndExcludesUnknownOffline()
    {
        var ordered = FishingSelectionPolicy.BuildOrderedCandidates(
            [
                new("Unknown@World", null, true, false, false),
                new("High@World", 80, true, false, false),
                new("AlwaysUnknown@World", null, true, true, false),
                new("Low@World", 12, true, false, true),
                new("AlwaysKnown@World", 90, true, true, false),
            ],
            maxFisherLevel: 100,
            FishingExecutionMode.AutoRetainerRelogCurrentAccount,
            currentCharacterKey: "Low@World",
            fishingWindowActive: true);

        Assert.Equal(
            ["AlwaysKnown@World", "AlwaysUnknown@World", "Low@World", "High@World"],
            ordered.Select(candidate => candidate.CharacterKey).ToArray());
        Assert.DoesNotContain(ordered, candidate => candidate.CharacterKey == "Unknown@World");
        Assert.Null(ordered[1].FisherLevel);
    }

    [Fact]
    public void EqualLevelsPreferCurrentCharacterThenCharacterKey()
    {
        var ordered = FishingSelectionPolicy.BuildOrderedCandidates(
            [
                new("Zulu@World", 25, true, false, false),
                new("Current@World", 25, true, false, true),
                new("Alpha@World", 25, true, false, false),
            ],
            maxFisherLevel: 100,
            FishingExecutionMode.AutoRetainerRelogCurrentAccount,
            currentCharacterKey: "Current@World",
            fishingWindowActive: true);

        Assert.Equal(
            ["Current@World", "Alpha@World", "Zulu@World"],
            ordered.Select(candidate => candidate.CharacterKey).ToArray());
    }

    [Fact]
    public void UnknownCurrentLevelIsNotTreatedAsZero()
    {
        var result = FishingSelectionPolicy.Select(
            [
                new("Current@World", null, true, false, true),
                new("Known@World", 25, true, false, false),
            ],
            maxFisherLevel: 100,
            FishingExecutionMode.AutoRetainerRelogCurrentAccount,
            currentCharacterKey: "Current@World",
            fishingWindowActive: true);

        Assert.Equal("Known@World", result.CharacterKey);
        Assert.Equal(25, result.FisherLevel);
    }

    [Fact]
    public void RecoveryPolicyRetriesTransientFailureTwiceWithRequiredBackoff()
    {
        Assert.Equal(TimeSpan.FromSeconds(3), FishingRecoveryPolicy.GetTransientBackoff(1));
        Assert.Equal(TimeSpan.FromSeconds(10), FishingRecoveryPolicy.GetTransientBackoff(2));
        Assert.True(FishingRecoveryPolicy.MayRecover(
            FishingAttemptFailureKind.SharedTransient, false, true, 0));
        Assert.True(FishingRecoveryPolicy.MayRecover(
            FishingAttemptFailureKind.SharedTransient, false, true, 1));
        Assert.False(FishingRecoveryPolicy.MayRecover(
            FishingAttemptFailureKind.SharedTransient, false, true, 2));
        Assert.False(FishingRecoveryPolicy.MayRecover(
            FishingAttemptFailureKind.SharedTransient, true, true, 0));
        Assert.False(FishingRecoveryPolicy.MayRecover(
            FishingAttemptFailureKind.CharacterPermanent, false, false, 0));
    }

    [Fact]
    public void AttemptRequiresSixtySecondsRemaining()
    {
        var deadline = Utc(2026, 7, 2, 12, 15, 0);

        Assert.True(FishingRecoveryPolicy.CanStartAttempt(deadline.AddSeconds(-60), deadline));
        Assert.False(FishingRecoveryPolicy.CanStartAttempt(deadline.AddSeconds(-59), deadline));
    }

    [Fact]
    public void InventoryCleanupAndReturnPoliciesAreOrderedAndObserved()
    {
        Assert.Equal(
            [FishingCleanupCommand.Discard, FishingCleanupCommand.Sell],
            FishingInventoryCleanupPolicy.Build(true, true));
        Assert.True(FishingInventoryCleanupPolicy.TreatAsNothingToProcess(
            busyObserved: false,
            TimeSpan.FromSeconds(10)));
        Assert.False(FishingReturnPolicy.IsVerified(
            commandRequired: true,
            activityObserved: false,
            territoryChanged: false,
            currentlyBusy: false));
        Assert.True(FishingReturnPolicy.IsVerified(
            commandRequired: true,
            activityObserved: true,
            territoryChanged: false,
            currentlyBusy: false));
        Assert.True(FishingReturnPolicy.ShouldRetry(1, TimeSpan.FromSeconds(30)));
        Assert.False(FishingReturnPolicy.ShouldRetry(2, TimeSpan.FromSeconds(120)));
    }

    [Fact]
    public void RegistrationDialogueUsesLocalizedCustomSheetRows()
    {
        Assert.Equal("custom/006/CtsIkdEntrance_00663", OceanFishingDialoguePolicy.SheetName);
        Assert.Equal(4u, OceanFishingDialoguePolicy.BoardingRow);
        Assert.Equal(10u, OceanFishingDialoguePolicy.EmbarkRow);
        Assert.True(OceanFishingDialoguePolicy.Matches("Lokalisierter Text", "Lokalisierter Text"));
        Assert.True(OceanFishingDialoguePolicy.MatchesEmbarkPrompt("Lokalisierter Text", "Lokalisierter Text"));
        Assert.False(OceanFishingDialoguePolicy.Matches("Register to board.", "Lokalisierter Text"));
    }

    [Fact]
    public void RegistrationEmbarkPromptAcceptsRouteSpecificEnglishPrompt()
    {
        Assert.True(OceanFishingDialoguePolicy.MatchesEmbarkPrompt(
            "Embark to the Northern Strait of Merlthor?",
            "Localized embark text"));
    }

    [Theory]
    [InlineData("Log out?")]
    [InlineData("Discard this item?")]
    [InlineData("Teleport to Limsa Lominsa?")]
    [InlineData("Register to board.")]
    [InlineData("Embark?")]
    [InlineData("Embark to ?")]
    public void RegistrationEmbarkPromptRejectsUnrelatedPrompts(string prompt)
    {
        Assert.False(OceanFishingDialoguePolicy.MatchesEmbarkPrompt(prompt, "Localized embark text"));
    }

    [Fact]
    public void PreviouslyObservedDutyContextLossInfersCompletionOnlyAfterSettlement()
    {
        Assert.True(OceanFishingCompletionPolicy.ShouldInferFromDutyContextLoss(
            dutyContextPreviouslyObserved: true,
            stillInOceanFishingTerritory: false,
            playerAvailable: true,
            areaTransitioning: false));
        Assert.False(OceanFishingCompletionPolicy.ShouldInferFromDutyContextLoss(
            dutyContextPreviouslyObserved: true,
            stillInOceanFishingTerritory: false,
            playerAvailable: false,
            areaTransitioning: true));
    }

    [Fact]
    public void LockedAethernetGetsOneAttunementAttemptThenWalksDirect()
    {
        Assert.Equal(
            OceanFishingAttunementAction.NavigateToLockedShard,
            OceanFishingAttunementPolicy.Decide(
                unlocked: false,
                shardLoaded: false,
                inInteractionRange: false,
                attunementAttempted: false,
                TimeSpan.Zero));
        Assert.Equal(
            OceanFishingAttunementAction.AttuneLockedShard,
            OceanFishingAttunementPolicy.Decide(
                unlocked: false,
                shardLoaded: true,
                inInteractionRange: true,
                attunementAttempted: false,
                TimeSpan.Zero));
        Assert.Equal(
            OceanFishingAttunementAction.VerifyAttunement,
            OceanFishingAttunementPolicy.Decide(
                unlocked: false,
                shardLoaded: true,
                inInteractionRange: true,
                attunementAttempted: true,
                TimeSpan.FromSeconds(9)));
        Assert.Equal(
            OceanFishingAttunementAction.WalkDirect,
            OceanFishingAttunementPolicy.Decide(
                unlocked: false,
                shardLoaded: true,
                inInteractionRange: true,
                attunementAttempted: true,
                TimeSpan.FromSeconds(10)));
        Assert.Equal(
            OceanFishingAttunementAction.UseUnlockedShard,
            OceanFishingAttunementPolicy.Decide(
                unlocked: true,
                shardLoaded: false,
                inInteractionRange: false,
                attunementAttempted: false,
                TimeSpan.Zero));
    }

    [Fact]
    public void OceanFishingStartupWindowCoversOffsetThroughFullRegistrationPeriod()
    {
        Assert.True(OceanFishingSchedulePolicy.IsStartupWindowActive(Utc(2026, 6, 30, 23, 59, 0), -1));
        Assert.True(OceanFishingSchedulePolicy.IsStartupWindowActive(Utc(2026, 7, 1, 0, 0, 0), -1));
        Assert.True(OceanFishingSchedulePolicy.IsStartupWindowActive(Utc(2026, 7, 1, 0, 14, 59), -1));

        Assert.False(OceanFishingSchedulePolicy.IsStartupWindowActive(Utc(2026, 6, 30, 23, 58, 59), -1));
        Assert.False(OceanFishingSchedulePolicy.IsStartupWindowActive(Utc(2026, 7, 1, 0, 15, 0), -1));
    }

    [Fact]
    public void OceanFishingStartupWindowAtReportedTimestampResolvesFullGate()
    {
        var active = OceanFishingSchedulePolicy.TryGetActiveStartupWindow(
            Utc(2026, 7, 2, 12, 7, 42),
            -1,
            out var window);

        Assert.True(active);
        Assert.Equal(Utc(2026, 7, 2, 12, 0, 0), window.RegistrationStartUtc);
        Assert.Equal(Utc(2026, 7, 2, 11, 59, 0), window.StartUtc);
        Assert.Equal(Utc(2026, 7, 2, 12, 15, 0), window.EndUtc);
    }

    [Fact]
    public void OceanFishingStartupWindowIsInactiveAwayFromEvenHourRegistration()
    {
        Assert.False(OceanFishingSchedulePolicy.IsStartupWindowActive(Utc(2026, 7, 1, 1, 0, 0), -1));
        Assert.False(OceanFishingSchedulePolicy.IsStartupWindowActive(Utc(2026, 7, 1, 3, 15, 0), -10));
    }

    [Fact]
    public void OceanFishingOffsetChangesOnlyPreWindowStart()
    {
        var registrationStart = Utc(2026, 7, 1, 0, 0, 0);
        var registrationWindow = OceanFishingSchedulePolicy.BuildRegistrationWindow(registrationStart);
        var defaultOffsetWindow = OceanFishingSchedulePolicy.BuildStartupWindow(registrationStart, -1);
        var widerOffsetWindow = OceanFishingSchedulePolicy.BuildStartupWindow(registrationStart, -5);

        Assert.Equal(Utc(2026, 7, 1, 0, 0, 0), registrationWindow.StartUtc);
        Assert.Equal(Utc(2026, 7, 1, 0, 15, 0), registrationWindow.EndUtc);
        Assert.Equal(Utc(2026, 6, 30, 23, 59, 0), defaultOffsetWindow.StartUtc);
        Assert.Equal(Utc(2026, 6, 30, 23, 55, 0), widerOffsetWindow.StartUtc);
        Assert.Equal(defaultOffsetWindow.EndUtc, widerOffsetWindow.EndUtc);
        Assert.Equal(registrationWindow.EndUtc, widerOffsetWindow.EndUtc);

        Assert.False(OceanFishingSchedulePolicy.IsStartupWindowActive(Utc(2026, 6, 30, 23, 55, 0), -1));
        Assert.True(OceanFishingSchedulePolicy.IsStartupWindowActive(Utc(2026, 6, 30, 23, 55, 0), -5));
    }

    [Fact]
    public void OceanFishingOffsetIsClampedToConfiguredRange()
    {
        Assert.Equal(-10, OceanFishingSchedulePolicy.NormalizePreWindowOffsetMinutes(-99));
        Assert.Equal(0, OceanFishingSchedulePolicy.NormalizePreWindowOffsetMinutes(5));
    }

    [Theory]
    [InlineData(12, 14, 59, 12)]
    [InlineData(12, 15, 0, 14)]
    [InlineData(13, 59, 59, 14)]
    public void CurrentOrNextRegistrationUsesOpenWindowAndEndExclusiveBoundary(
        int hour,
        int minute,
        int second,
        int expectedRegistrationHour)
    {
        var window = OceanFishingSchedulePolicy.GetCurrentOrNextRegistrationWindow(
            Utc(2026, 7, 2, hour, minute, second));

        Assert.Equal(expectedRegistrationHour, window.StartUtc.Hour);
        Assert.Equal(window.StartUtc.AddMinutes(15), window.EndUtc);
    }

    [Fact]
    public void RelogPrepUsesRequiredCommandOrder()
    {
        var steps = FishingRelogPrepPolicy.BuildReleaseSequence("Fishy Person@Cactuar").ToArray();

        Assert.Equal(FishingRelogPrepAction.FinishVermaxionPostprocess, steps[0].Action);
        Assert.Equal(FishingRelogPrepAction.ReleaseVermaxionSuppression, steps[1].Action);
        Assert.Equal(FishingRelogPrepAction.DisableAutoRetainerMultiMode, steps[2].Action);
        Assert.Equal("/ays relog Fishy Person@Cactuar", steps[3].Command);
        Assert.DoesNotContain(steps, step => string.Equals(step.Command, "/ays m d", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(steps, step => string.Equals(step.Command, "/ays reset", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RelogPolicyWaitsWhileOccupiedOrBellBlocked()
    {
        var decision = FishingRelogCommandPolicy.Evaluate(
            Utc(2026, 7, 2, 12, 1, 0),
            Utc(2026, 7, 2, 12, 0, 0),
            lastRelogCommandAtUtc: null,
            registrationOpen: true,
            readyForRelog: false,
            blockedReason: "Waiting for occupied/bell/cutscene state to clear before relog.",
            targetReached: false,
            observableProgress: false,
            wrongCharacterArrived: false);

        Assert.Equal(FishingRelogRuntimeAction.Wait, decision.Action);
        Assert.Contains("occupied/bell", decision.Reason);
    }

    [Fact]
    public void RelogPolicyRetriesUnobservedCommandAfterBoundedInterval()
    {
        var decision = FishingRelogCommandPolicy.Evaluate(
            Utc(2026, 7, 2, 12, 1, 46),
            Utc(2026, 7, 2, 12, 0, 0),
            lastRelogCommandAtUtc: Utc(2026, 7, 2, 12, 1, 0),
            registrationOpen: true,
            readyForRelog: true,
            blockedReason: string.Empty,
            targetReached: false,
            observableProgress: false,
            wrongCharacterArrived: false);

        Assert.Equal(FishingRelogRuntimeAction.SendRelog, decision.Action);
        Assert.Contains("retrying relog", decision.Reason);
    }

    [Fact]
    public void RelogPolicyCompletesOnTargetArrivalRetriesIntermediateAndFailsExpiry()
    {
        var now = Utc(2026, 7, 2, 12, 3, 0);
        var started = Utc(2026, 7, 2, 12, 0, 0);

        var success = FishingRelogCommandPolicy.Evaluate(
            now,
            started,
            lastRelogCommandAtUtc: Utc(2026, 7, 2, 12, 1, 0),
            registrationOpen: true,
            readyForRelog: true,
            blockedReason: string.Empty,
            targetReached: true,
            observableProgress: true,
            wrongCharacterArrived: false);
        var wrong = FishingRelogCommandPolicy.Evaluate(
            now,
            started,
            lastRelogCommandAtUtc: Utc(2026, 7, 2, 12, 1, 0),
            registrationOpen: true,
            readyForRelog: true,
            blockedReason: string.Empty,
            targetReached: false,
            observableProgress: true,
            wrongCharacterArrived: true);
        var expired = FishingRelogCommandPolicy.Evaluate(
            now,
            started,
            lastRelogCommandAtUtc: Utc(2026, 7, 2, 12, 1, 0),
            registrationOpen: false,
            readyForRelog: true,
            blockedReason: string.Empty,
            targetReached: false,
            observableProgress: false,
            wrongCharacterArrived: false);

        Assert.Equal(FishingRelogRuntimeAction.Complete, success.Action);
        Assert.Equal(FishingRelogRuntimeAction.SendRelog, wrong.Action);
        Assert.Equal(FishingRelogRuntimeAction.Fail, expired.Action);
        Assert.Contains("intermediate character", wrong.Reason);
        Assert.Contains("registration closed", expired.Reason);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    public void ExternalStateRestoreOnlyRunsForStateChangedByVermaxion(bool initial, bool changed, bool expected)
    {
        Assert.Equal(expected, FishingExternalStatePolicy.ShouldRestore(initial, changed));
    }

    [Theory]
    [InlineData("/li limsa", "limsa")]
    [InlineData(" /LI fc ", "fc")]
    [InlineData("home", "home")]
    public void LifestreamIpcCommandNormalizationRemovesChatPrefix(string command, string expected)
    {
        Assert.Equal(expected, LifestreamCommandPolicy.NormalizeForIpc(command));
    }

    [Theory]
    [InlineData(1, true, 129, 1.0, true, false, false, false, 99.0, false, false, false, OceanFishingQueueAction.SwitchToFisher)]
    [InlineData(18, false, 129, 1.0, true, false, false, false, 99.0, false, false, false, OceanFishingQueueAction.PrepareSupplies)]
    [InlineData(18, true, 128, 1.0, true, false, false, false, 99.0, false, false, false, OceanFishingQueueAction.TravelToLimsa)]
    [InlineData(18, true, 129, 20.0, true, false, false, false, 99.0, false, false, false, OceanFishingQueueAction.MoveToRegistrar)]
    [InlineData(18, true, 129, 2.001, true, false, false, false, 99.0, false, false, false, OceanFishingQueueAction.MoveToRegistrar)]
    [InlineData(18, true, 129, 2.0, true, false, false, false, 99.0, false, false, false, OceanFishingQueueAction.InteractWithRegistrar)]
    [InlineData(18, true, 129, 1.0, false, false, false, false, 99.0, false, false, false, OceanFishingQueueAction.WaitForRegistrationOpen)]
    [InlineData(18, true, 129, 1.0, true, false, false, false, 99.0, false, false, false, OceanFishingQueueAction.InteractWithRegistrar)]
    [InlineData(18, true, 129, 1.0, false, true, false, false, 99.0, false, false, false, OceanFishingQueueAction.FailRegistrationClosed)]
    [InlineData(18, true, 129, 1.0, false, false, true, false, 99.0, false, false, false, OceanFishingQueueAction.WaitForDeparture)]
    [InlineData(18, true, 129, 1.0, false, false, false, true, 10.0, false, false, false, OceanFishingQueueAction.MoveToFishingPosition)]
    [InlineData(18, true, 129, 1.0, false, false, false, true, 1.0, false, false, false, OceanFishingQueueAction.CastLine)]
    [InlineData(18, true, 129, 1.0, false, false, false, true, 1.0, true, false, false, OceanFishingQueueAction.CloseResult)]
    [InlineData(18, true, 129, 1.0, false, false, false, false, 1.0, false, true, false, OceanFishingQueueAction.ReturnAfterCompletion)]
    public void QueuePolicyCoversOceanFishingStateMachine(
        int currentJobId,
        bool suppliesPrepared,
        ushort territoryType,
        double registrarDistance,
        bool registrationWindowOpen,
        bool registrationWindowClosed,
        bool queueConfirmed,
        bool dutyActive,
        double fishingPositionDistance,
        bool resultAddonVisible,
        bool fishingComplete,
        bool returnCommandSent,
        OceanFishingQueueAction expected)
    {
        var action = OceanFishingQueuePolicy.Decide(new OceanFishingQueueSnapshot(
            currentJobId,
            suppliesPrepared,
            territoryType,
            registrarDistance,
            registrationWindowOpen,
            registrationWindowClosed,
            queueConfirmed,
            dutyActive,
            fishingPositionDistance,
            resultAddonVisible,
            fishingComplete,
            returnCommandSent));

        Assert.Equal(expected, action);
    }

    [Fact]
    public void RegistrarApproachUsesLivePositionAndSafeInteractionRange()
    {
        var fallback = new Vector3(-409.42f, 4f, 74.48f);
        var live = new Vector3(-406.75f, 4.1f, 72.25f);

        Assert.Equal(live, OceanFishingRegistrarPolicy.ResolveApproachPosition(fallback, live));
        Assert.Equal(fallback, OceanFishingRegistrarPolicy.ResolveApproachPosition(fallback, null));
        Assert.True(OceanFishingRegistrarPolicy.IsWithinInteractionRange(2.0));
        Assert.False(OceanFishingRegistrarPolicy.IsWithinInteractionRange(2.001));
    }

    [Fact]
    public void RegistrationLeasesAreRetainedThroughQueueConfirmation()
    {
        var deadline = Utc(2026, 7, 2, 12, 15, 0);
        var delayed = OceanFishingRegistrationPolicy.Decide(
            queueConfirmed: false,
            embarkAccepted: false,
            nowUtc: Utc(2026, 7, 2, 12, 14, 59),
            registrationDeadlineUtc: deadline,
            genuineFailure: false);
        var queued = OceanFishingRegistrationPolicy.Decide(
            queueConfirmed: true,
            embarkAccepted: false,
            nowUtc: Utc(2026, 7, 2, 12, 14, 59),
            registrationDeadlineUtc: deadline,
            genuineFailure: false);
        var expired = OceanFishingRegistrationPolicy.Decide(
            queueConfirmed: false,
            embarkAccepted: false,
            nowUtc: deadline,
            registrationDeadlineUtc: deadline,
            genuineFailure: false);

        Assert.Equal(OceanFishingRegistrationDecision.ContinueDialogs, delayed);
        Assert.True(OceanFishingRegistrationPolicy.ShouldRetainRegistrationLeases(delayed));
        Assert.Equal(OceanFishingRegistrationDecision.QueueConfirmed, queued);
        Assert.True(OceanFishingRegistrationPolicy.ShouldRetainRegistrationLeases(queued));
        Assert.Equal(OceanFishingRegistrationDecision.RegistrationExpired, expired);
        Assert.False(OceanFishingRegistrationPolicy.ShouldRetainRegistrationLeases(expired));
    }

    [Theory]
    [InlineData(true, false, false, OceanFishingQueueEvidence.InDutyQueue)]
    [InlineData(false, true, false, OceanFishingQueueEvidence.WaitingForDuty)]
    [InlineData(false, false, true, OceanFishingQueueEvidence.WaitingForDutyFinder)]
    public void QueueEvidenceRecognizesEverySupportedConditionFlag(
        bool inDutyQueue,
        bool waitingForDuty,
        bool waitingForDutyFinder,
        OceanFishingQueueEvidence expected)
    {
        var evidence = OceanFishingQueueEvidencePolicy.Detect(
            inDutyQueue,
            waitingForDuty,
            waitingForDutyFinder,
            oceanFishingDutyActive: false,
            contentsFinderConfirmVisible: false);

        Assert.Equal(expected, evidence);
    }

    [Theory]
    [InlineData(false, true, OceanFishingQueueEvidence.ContentsFinderConfirm)]
    [InlineData(true, false, OceanFishingQueueEvidence.OceanFishingDutyEntry)]
    public void ReadyPromptAndDutyEntryConfirmQueueDuringGrace(
        bool oceanFishingDutyActive,
        bool contentsFinderConfirmVisible,
        OceanFishingQueueEvidence expected)
    {
        var deadline = Utc(2026, 7, 2, 12, 15, 0);
        var evidence = OceanFishingQueueEvidencePolicy.Detect(
            inDutyQueue: false,
            waitingForDuty: false,
            waitingForDutyFinder: false,
            oceanFishingDutyActive,
            contentsFinderConfirmVisible);
        var decision = OceanFishingRegistrationPolicy.Decide(
            queueConfirmed: evidence != OceanFishingQueueEvidence.None,
            embarkAccepted: true,
            nowUtc: deadline + TimeSpan.FromSeconds(30),
            registrationDeadlineUtc: deadline,
            genuineFailure: false);

        Assert.Equal(expected, evidence);
        Assert.Equal(OceanFishingRegistrationDecision.QueueConfirmed, decision);
        Assert.True(OceanFishingRegistrationPolicy.ShouldRetainRegistrationLeases(decision));
    }

    [Fact]
    public void QueueRecognitionGraceOnlyAppliesAfterEmbarkAcceptance()
    {
        var deadline = Utc(2026, 7, 2, 12, 15, 0);

        var accepted = OceanFishingRegistrationPolicy.Decide(
            queueConfirmed: false,
            embarkAccepted: true,
            nowUtc: deadline,
            registrationDeadlineUtc: deadline,
            genuineFailure: false);
        var notAccepted = OceanFishingRegistrationPolicy.Decide(
            queueConfirmed: false,
            embarkAccepted: false,
            nowUtc: deadline,
            registrationDeadlineUtc: deadline,
            genuineFailure: false);

        Assert.Equal(OceanFishingRegistrationDecision.WaitForQueueRecognitionGrace, accepted);
        Assert.True(OceanFishingRegistrationPolicy.ShouldRetainRegistrationLeases(accepted));
        Assert.Equal(OceanFishingRegistrationDecision.RegistrationExpired, notAccepted);
        Assert.False(OceanFishingRegistrationPolicy.ShouldRetainRegistrationLeases(notAccepted));
    }

    [Fact]
    public void QueueRecognitionGraceExpiresAtExactlySixtySecondsWithoutEvidence()
    {
        var deadline = Utc(2026, 7, 2, 12, 15, 0);
        Assert.Equal(
            TimeSpan.FromSeconds(60),
            OceanFishingRegistrationPolicy.QueueRecognitionGracePeriod);

        var beforeExpiration = OceanFishingRegistrationPolicy.Decide(
            queueConfirmed: false,
            embarkAccepted: true,
            nowUtc: deadline + TimeSpan.FromMilliseconds(59_999),
            registrationDeadlineUtc: deadline,
            genuineFailure: false);
        var atExpiration = OceanFishingRegistrationPolicy.Decide(
            queueConfirmed: false,
            embarkAccepted: true,
            nowUtc: deadline + TimeSpan.FromSeconds(60),
            registrationDeadlineUtc: deadline,
            genuineFailure: false);

        Assert.Equal(OceanFishingRegistrationDecision.WaitForQueueRecognitionGrace, beforeExpiration);
        Assert.Equal(OceanFishingRegistrationDecision.RegistrationExpired, atExpiration);
    }

    [Fact]
    public void FishingRunContextTracksRegistrationLeaseOwnership()
    {
        var context = new FishingRunContext
        {
            AutoRetainerMultiModeChanged = true,
            YesAlreadyLeaseOwned = true,
        };

        Assert.True(context.OwnsRegistrationLeases);
        context.AutoRetainerMultiModeChanged = false;
        Assert.True(context.OwnsRegistrationLeases);
        context.YesAlreadyLeaseOwned = false;
        Assert.False(context.OwnsRegistrationLeases);
    }

    [Fact]
    public void CleanupOwnershipIncludesEveryChangedExternalState()
    {
        var context = new FishingRunContext
        {
            AutoHookChanged = true,
            CleanupPending = true,
        };

        Assert.True(context.OwnsExternalState);
        context.AutoHookChanged = false;
        Assert.False(context.OwnsExternalState);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void BeforeArRequiresReadableEnabledMultiMode(bool readSucceeded, bool enabled, bool expected)
    {
        Assert.Equal(expected, BeforeArMultiModePolicy.ShouldRunBeforeAr(readSucceeded, enabled));
    }

    [Theory]
    [InlineData(true, true, true, true, true, false, false, false, false, true)]
    [InlineData(true, true, true, true, true, true, false, false, false, false)]
    [InlineData(true, true, true, true, true, false, true, false, false, false)]
    [InlineData(true, true, true, true, true, true, true, false, false, false)]
    [InlineData(true, true, false, true, true, false, false, false, false, false)]
    [InlineData(true, true, true, false, true, false, false, false, false, false)]
    [InlineData(false, true, true, true, true, false, false, false, false, false)]
    [InlineData(true, false, true, true, true, false, false, false, false, false)]
    [InlineData(true, true, true, true, false, false, false, false, false, false)]
    [InlineData(true, true, true, true, true, false, false, true, false, false)]
    [InlineData(true, true, true, true, true, false, false, false, true, false)]
    public void CastPolicyOnlyCastsInReadyEligibleState(
        bool enabled,
        bool inFishingContext,
        bool railPositionReady,
        bool canFish,
        bool playerAvailable,
        bool gatheringConditionActive,
        bool fishingConditionActive,
        bool busy,
        bool resultWindowVisible,
        bool expected)
    {
        Assert.Equal(expected, FishingCastPolicy.ShouldCast(
            enabled,
            inFishingContext,
            railPositionReady,
            canFish,
            playerAvailable,
            gatheringConditionActive,
            fishingConditionActive,
            busy,
            resultWindowVisible));
    }

    [Fact]
    public void CastCommandIsExactlyLowercaseActionCast()
    {
        Assert.Equal("/ac cast", FishingCastPolicy.CastCommand);
    }

    [Fact]
    public void FirstCastWaitsForOneSecondMovementAndBaitSettlement()
    {
        var settling = EvaluateCast(
            railSettlementElapsed: TimeSpan.FromMilliseconds(999),
            sinceLastAttempt: TimeSpan.MaxValue);
        var ready = EvaluateCast(
            railSettlementElapsed: TimeSpan.FromSeconds(1),
            sinceLastAttempt: TimeSpan.MaxValue);

        Assert.Equal(FishingCastDecision.Suppressed, settling.Decision);
        Assert.Equal("waiting for movement/bait settlement", settling.Gate);
        Assert.Equal(FishingCastDecision.Attempt, ready.Decision);
    }

    [Fact]
    public void UnacknowledgedCastRetriesEveryThreeSeconds()
    {
        var early = EvaluateCast(
            railSettlementElapsed: TimeSpan.FromSeconds(10),
            sinceLastAttempt: TimeSpan.FromMilliseconds(2999));
        var due = EvaluateCast(
            railSettlementElapsed: TimeSpan.FromSeconds(10),
            sinceLastAttempt: TimeSpan.FromSeconds(3));

        Assert.Equal(FishingCastDecision.Suppressed, early.Decision);
        Assert.Equal("waiting for retry interval", early.Gate);
        Assert.Equal(FishingCastDecision.Attempt, due.Decision);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void FishingOrGatheringAcknowledgesCast(bool gathering, bool fishing)
    {
        var evaluation = EvaluateCast(
            gatheringConditionActive: gathering,
            fishingConditionActive: fishing);

        Assert.Equal(FishingCastDecision.Acknowledged, evaluation.Decision);
        Assert.Equal("Fishing/Gathering active", evaluation.Gate);
    }

    [Theory]
    [InlineData(false, false, false, "Ocean Fishing duty context inactive")]
    [InlineData(true, true, false, "route transition active")]
    [InlineData(true, false, true, "result window visible")]
    public void DutyExitRouteTransitionAndResultCancelCastEligibility(
        bool inFishingContext,
        bool zoneTransitionActive,
        bool resultWindowVisible,
        string expectedGate)
    {
        var evaluation = EvaluateCast(
            inFishingContext: inFishingContext,
            zoneTransitionActive: zoneTransitionActive,
            resultWindowVisible: resultWindowVisible);

        Assert.Equal(FishingCastDecision.Suppressed, evaluation.Decision);
        Assert.Equal(expectedGate, evaluation.Gate);
    }

    [Fact]
    public void BusyStateSuppressesWithoutAcknowledgingOrCancellingPendingCast()
    {
        var whileBusy = EvaluateCast(
            busy: true,
            sinceLastAttempt: TimeSpan.FromSeconds(10));
        var afterBusy = EvaluateCast(
            busy: false,
            sinceLastAttempt: TimeSpan.FromSeconds(10));

        Assert.Equal(FishingCastDecision.Suppressed, whileBusy.Decision);
        Assert.Equal("player occupied/casting", whileBusy.Gate);
        Assert.Equal(FishingCastDecision.Attempt, afterBusy.Decision);
    }

    [Fact]
    public void CanFishFallbackUsesTenSecondThreshold()
    {
        Assert.Equal(TimeSpan.FromSeconds(10), FishingCastPolicy.CanFishFallbackDelay);
        var evaluation = EvaluateCast(canFish: false);
        Assert.Equal(FishingCastDecision.Suppressed, evaluation.Decision);
        Assert.Equal("CanFish false", evaluation.Gate);
        Assert.False(FishingCastPolicy.ShouldAdvanceRail(
            canFish: false,
            gatheringConditionActive: false,
            fishingConditionActive: false,
            playerAvailable: true,
            busy: false,
            TimeSpan.FromMilliseconds(9999)));
        Assert.True(FishingCastPolicy.ShouldAdvanceRail(
            canFish: false,
            gatheringConditionActive: false,
            fishingConditionActive: false,
            playerAvailable: true,
            busy: false,
            TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void RouteCutscenePausesThenAllowsFreshCastAfterSettlement()
    {
        var duringCutscene = EvaluateCast(
            zoneTransitionActive: true,
            railSettlementElapsed: TimeSpan.FromSeconds(10));
        var immediatelyAfterRail = EvaluateCast(
            zoneTransitionActive: false,
            railSettlementElapsed: TimeSpan.Zero);
        var settledAfterRail = EvaluateCast(
            zoneTransitionActive: false,
            railSettlementElapsed: TimeSpan.FromSeconds(1));

        Assert.Equal("route transition active", duringCutscene.Gate);
        Assert.Equal("waiting for movement/bait settlement", immediatelyAfterRail.Gate);
        Assert.Equal(FishingCastDecision.Attempt, settledAfterRail.Decision);
    }

    [Fact]
    public void RepairPolicyRequestsAdsWhenAtOrBelowThreshold()
    {
        var decision = FishingRepairPolicy.Evaluate(
            FishingRepairMode.NpcNoInn,
            thresholdPercent: 50,
            durabilityKnown: true,
            lowestDurabilityPercent: 50);

        Assert.True(decision.ShouldRepair);
        Assert.Equal("npc-no-inn", decision.AdsMode);
    }

    [Fact]
    public void OperationRepairPolicyUsesSelectedModeAndThreshold()
    {
        var settings = new FishingOperationSettings(
            LureRestockTarget: 0,
            ReturnDestination: FishingReturnDestination.Home,
            ReturnCommand: "/li home",
            RepairMode: FishingRepairMode.NpcNoTeleportNoInn,
            RepairThresholdPercent: 35);

        var decision = FishingOperationPolicy.EvaluateRepair(
            settings,
            durabilityKnown: true,
            lowestDurabilityPercent: 35);

        Assert.True(decision.ShouldRepair);
        Assert.Equal("npc-no-teleport-no-inn", decision.AdsMode);
    }

    [Fact]
    public void RepairPolicySkipsWhenDisabledOrAboveThreshold()
    {
        Assert.False(FishingRepairPolicy.Evaluate(FishingRepairMode.Disabled, 50, true, 1).ShouldRepair);
        Assert.False(FishingRepairPolicy.Evaluate(FishingRepairMode.Self, 50, true, 80).ShouldRepair);
    }

    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute, int second)
        => new(year, month, day, hour, minute, second, TimeSpan.Zero);

    private static FishingCastEvaluation EvaluateCast(
        bool inFishingContext = true,
        bool zoneTransitionActive = false,
        bool railPositionReady = true,
        bool canFish = true,
        bool playerAvailable = true,
        bool gatheringConditionActive = false,
        bool fishingConditionActive = false,
        bool busy = false,
        bool resultWindowVisible = false,
        TimeSpan? railSettlementElapsed = null,
        TimeSpan? sinceLastAttempt = null)
        => FishingCastPolicy.Evaluate(
            enabled: true,
            inFishingContext,
            zoneTransitionActive,
            railPositionReady,
            canFish,
            playerAvailable,
            gatheringConditionActive,
            fishingConditionActive,
            busy,
            resultWindowVisible,
            railSettlementElapsed ?? TimeSpan.FromSeconds(10),
            sinceLastAttempt ?? TimeSpan.MaxValue);
}
