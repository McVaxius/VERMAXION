#nullable enable

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
}
