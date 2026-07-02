#nullable enable

using System;
using System.Linq;
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
          "characters": [
            {
              "characterName": "Alpha One",
              "worldName": "Cactuar",
              "jobLevels": { "18": 42 }
            },
            {
              "characterKey": "Beta Two@Gilgamesh",
              "jobs": [
                { "jobId": 19, "jobAbbrev": "PLD", "level": 90 },
                { "jobId": 18, "jobAbbrev": "FSH", "level": 73 }
              ]
            },
            {
              "characterKey": "Gamma Three@Siren",
              "jobLevels": { "FSH": 12 }
            }
          ]
        }
        """;

        var levels = XaFishingRosterParser.ParseFisherLevels(json);

        Assert.Equal(42, levels["Alpha One@Cactuar"]);
        Assert.Equal(73, levels["Beta Two@Gilgamesh"]);
        Assert.Equal(12, levels["Gamma Three@Siren"]);
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
    public void AlwaysFishPriorityWinsWhenWindowIsActiveAndClearsOtherFlags()
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
        Assert.Equal(["Alpha@World"], result.AlwaysFishKeysToDisable.ToArray());
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
    public void OceanFishingStartupWindowCoversOffsetThroughFirstRegistrationMinute()
    {
        Assert.True(OceanFishingSchedulePolicy.IsStartupWindowActive(Utc(2026, 6, 30, 23, 59, 0), -1));
        Assert.True(OceanFishingSchedulePolicy.IsStartupWindowActive(Utc(2026, 7, 1, 0, 0, 0), -1));
        Assert.True(OceanFishingSchedulePolicy.IsStartupWindowActive(Utc(2026, 7, 1, 0, 0, 59), -1));

        Assert.False(OceanFishingSchedulePolicy.IsStartupWindowActive(Utc(2026, 6, 30, 23, 58, 59), -1));
        Assert.False(OceanFishingSchedulePolicy.IsStartupWindowActive(Utc(2026, 7, 1, 0, 1, 0), -1));
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
        Assert.Equal(Utc(2026, 7, 1, 0, 1, 0), widerOffsetWindow.EndUtc);

        Assert.False(OceanFishingSchedulePolicy.IsStartupWindowActive(Utc(2026, 6, 30, 23, 55, 0), -1));
        Assert.True(OceanFishingSchedulePolicy.IsStartupWindowActive(Utc(2026, 6, 30, 23, 55, 0), -5));
    }

    [Fact]
    public void OceanFishingOffsetIsClampedToConfiguredRange()
    {
        Assert.Equal(-10, OceanFishingSchedulePolicy.NormalizePreWindowOffsetMinutes(-99));
        Assert.Equal(0, OceanFishingSchedulePolicy.NormalizePreWindowOffsetMinutes(5));
    }

    [Fact]
    public void RelogPrepUsesRequiredCommandOrder()
    {
        var steps = FishingRelogPrepPolicy.BuildReleaseSequence("Fishy Person@Cactuar").ToArray();

        Assert.Equal(FishingRelogPrepAction.FinishVermaxionPostprocess, steps[0].Action);
        Assert.Equal(FishingRelogPrepAction.ReleaseVermaxionSuppression, steps[1].Action);
        Assert.Equal("/ays m d", steps[2].Command);
        Assert.Equal(FishingRelogPrepAction.Wait, steps[3].Action);
        Assert.Equal("/ays reset", steps[4].Command);
        Assert.Equal(FishingRelogPrepAction.Wait, steps[5].Action);
        Assert.Equal("/ays relog Fishy Person@Cactuar", steps[6].Command);
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
    [InlineData(true, true, true, false, false, true)]
    [InlineData(false, true, true, false, false, false)]
    [InlineData(true, false, true, false, false, false)]
    [InlineData(true, true, false, false, false, false)]
    [InlineData(true, true, true, true, false, false)]
    [InlineData(true, true, true, false, true, false)]
    public void CastPolicyOnlyCastsInReadyEligibleState(
        bool enabled,
        bool inFishingContext,
        bool playerAvailable,
        bool busy,
        bool resultWindowVisible,
        bool expected)
    {
        Assert.Equal(expected, FishingCastPolicy.ShouldCast(
            enabled,
            inFishingContext,
            playerAvailable,
            busy,
            resultWindowVisible));
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
}
