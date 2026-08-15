#nullable enable

using System;
using System.Collections.Generic;
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
    public void DefaultOperationSettingsUseInnReturnAndNpcNoInnRepair()
    {
        Assert.Equal(22, FishingDefaults.LureRestockTarget);
        // Inn is the deliberate default return: fishers park at the inn between windows (inn-park
        // pipeline), not the FC estate.
        Assert.Equal(FishingReturnDestination.Inn, FishingDefaults.ReturnDestination);
        Assert.Equal("/li inn", FishingDefaults.ReturnCommand);
        Assert.Equal(FishingRepairMode.NpcNoInn, FishingDefaults.RepairMode);
        Assert.Equal(50, FishingDefaults.RepairThresholdPercent);

        var settings = new FishingOperationSettings(
            FishingDefaults.LureRestockTarget,
            FishingDefaults.ReturnDestination,
            FishingDefaults.ReturnCommand,
            FishingDefaults.RepairMode,
            FishingDefaults.RepairThresholdPercent);

        Assert.Equal("/li inn", FishingOperationPolicy.ResolveReturnCommand(settings));

        var decision = FishingOperationPolicy.EvaluateRepair(
            settings,
            durabilityKnown: true,
            lowestDurabilityPercent: FishingDefaults.RepairThresholdPercent);

        Assert.True(decision.ShouldRepair);
        Assert.Equal("npc-no-inn", decision.AdsMode);
    }

    [Theory]
    [InlineData(0, 22)]
    [InlineData(-1, 22)]
    [InlineData(1, 1)]
    [InlineData(37, 37)]
    public void LureRestockTargetUsesDefaultUnlessConfiguredPositive(int configuredTarget, int expectedTarget)
    {
        Assert.Equal(expectedTarget, FishingOperationPolicy.ResolveLureRestockTarget(configuredTarget));
    }

    // Mirror of the production RailSegments perimeter map — the walked/measured deck geometry with
    // per-segment deck heights: (MinZ, MaxZ, X nominal jittered +/-0.125y, Y measured deck height,
    // outward Rotation). Kept here as the independent spec: a production geometry change must be
    // walk-proven and updated here deliberately, not absorbed silently.
    private static readonly (float MinZ, float MaxZ, float X, float Y, float Rotation)[] ExpectedRailSegments =
    [
        // starboard, stern -> bow — face +x
        (-16.0f, -13.0f, 7.0f, 5.6f, OceanFishingContinuousRailPolicy.StarboardRotation),
        (-12.0f, 4.0f, 7.0f, 6.7f, OceanFishingContinuousRailPolicy.StarboardRotation),
        (6.0f, 14.0f, 7.0f, 5.3f, OceanFishingContinuousRailPolicy.StarboardRotation),
        (15.0f, 22.0f, 7.0f, 7.5f, OceanFishingContinuousRailPolicy.StarboardRotation),
        // port, mid -> bow — face -x
        (-12.0f, 4.0f, -7.0f, 6.7f, OceanFishingContinuousRailPolicy.PortRotation),
        (6.0f, 14.0f, -7.0f, 5.3f, OceanFishingContinuousRailPolicy.PortRotation),
        (15.0f, 20.0f, -7.0f, 7.5f, OceanFishingContinuousRailPolicy.PortRotation),
        // port-aft strip — face -x
        (-19.0f, -14.0f, -6.6f, 5.0f, OceanFishingContinuousRailPolicy.PortRotation),
        // bow centreline — face +z; stern taper — face -z
        (22.0f, 27.0f, 0.0f, 8.25f, OceanFishingContinuousRailPolicy.BowRotation),
        (-24.0f, -21.0f, 4.3f, 5.2f, OceanFishingContinuousRailPolicy.SternRotation),
        (-26.5f, -24.5f, 2.6f, 5.2f, OceanFishingContinuousRailPolicy.SternRotation),
    ];

    [Fact]
    public void ContinuousRailsUseProvenRangesAndOutwardCharacterRotations()
    {
        var random = new Random(20260724);
        var rotationSamples = new Dictionary<float, int>();

        for (var sample = 0; sample < 10_000; sample++)
        {
            var destination = OceanFishingContinuousRailPolicy.SampleCandidate(random);

            // Every sample must land on exactly one mapped segment: measured deck Y (exact), nominal X
            // within the +/-0.125y jitter, Z inside the walked range, and that segment's outward facing.
            var segment = Assert.Single(ExpectedRailSegments, candidate =>
                destination.Rotation == candidate.Rotation &&
                destination.Position.Y == candidate.Y &&
                destination.Position.Z >= candidate.MinZ &&
                destination.Position.Z <= candidate.MaxZ &&
                MathF.Abs(destination.Position.X - candidate.X) <= 0.1251f);
            rotationSamples[segment.Rotation] = rotationSamples.GetValueOrDefault(segment.Rotation) + 1;
        }

        // All four outward facings (starboard, port, bow, stern) must actually be sampled.
        Assert.Equal(4, rotationSamples.Count);
        Assert.Equal(0.5, OceanFishingQueuePolicy.BoatFishingPositionTolerance);
    }

    [Fact]
    public void PreferredAndFallbackClearanceUseTheirOwnExactBoundaries()
    {
        var position = new Vector3(7.125f, OceanFishingContinuousRailPolicy.DeckY, -9f);

        Assert.False(OceanFishingContinuousRailPolicy.HasPlayerClearance(
            position,
            [position + new Vector3(0f, 0f, OceanFishingContinuousRailPolicy.MinimumPlayerClearance - 0.001f)]));
        Assert.True(OceanFishingContinuousRailPolicy.HasPlayerClearance(
            position,
            [position + new Vector3(0f, 0f, OceanFishingContinuousRailPolicy.MinimumPlayerClearance)]));

        Assert.False(OceanFishingContinuousRailPolicy.HasPlayerClearance(
            position,
            [position + new Vector3(0f, 0f, OceanFishingContinuousRailPolicy.FallbackPlayerClearance - 0.001f)],
            OceanFishingContinuousRailPolicy.FallbackPlayerClearance));
        Assert.True(OceanFishingContinuousRailPolicy.HasPlayerClearance(
            position,
            [position + new Vector3(0f, 0f, OceanFishingContinuousRailPolicy.FallbackPlayerClearance)],
            OceanFishingContinuousRailPolicy.FallbackPlayerClearance));
    }

    // Players packed shoulder-to-shoulder along every mapped rail segment (per-segment nominal X and
    // measured deck Y, mirroring the full production perimeter): no candidate anywhere clears even
    // the fallback tier. Spacing 1y leaves at most ~0.55y of clearance for any swept candidate.
    private static Vector3[] PackedRails(float spacing)
    {
        var players = new System.Collections.Generic.List<Vector3>();
        foreach (var (minZ, maxZ, x, y, _) in ExpectedRailSegments)
        {
            for (var z = minZ; z <= maxZ; z += spacing)
                players.Add(new Vector3(x, y, z));
            // Cap the segment END. Without this, spacing that does not divide the segment length leaves
            // an unpopulated tail gap wider than the spacing — "packed every 4y" then still contains
            // fully-clear 3y points near the rail ends, and the tests below pass or fail by seed luck.
            players.Add(new Vector3(x, y, maxZ));
        }

        return [.. players];
    }

    [Fact]
    public void SamplingFailsClosedOnlyWhenTheEntireRailIsPacked()
    {
        // Genuinely packed: fail closed, exactly as before.
        Assert.False(OceanFishingContinuousRailPolicy.TrySample(
            new Random(20260801),
            PackedRails(1.0f),
            previousDestination: null,
            out _));

        // One blocked spot must NOT block the whole rail — this was the exhaustion bug: 32 random darts
        // could all land near the one occupied point and report a full vessel that was nearly empty.
        // The occupied point sits on the measured starboard mid-deck segment (x 7.0, deck Y 6.7).
        Assert.True(OceanFishingContinuousRailPolicy.TrySample(
            new Random(20260801),
            [new Vector3(7.0f, 6.7f, -9f)],
            previousDestination: null,
            out var destination));
        Assert.True(OceanFishingContinuousRailPolicy.HasPlayerClearance(
            destination.Position,
            [new Vector3(7.0f, 6.7f, -9f)]));
        // A fully-clear pick must carry the full-minimum arrival tier, so the arrival gate keeps the
        // 3y first-cast guard at preferred destinations.
        Assert.Equal(OceanFishingContinuousRailPolicy.MinimumPlayerClearance, destination.ArrivalClearance);
    }

    [Fact]
    public void SamplingDegradesToBestAvailableClearanceOnABusyVessel()
    {
        // Players every 4y, segments capped at both ends: no point anywhere clears the full 3y
        // minimum, but mid-gap points clear the 1.5y fallback — a busy boat degrades instead of
        // stalling. Proven across many seeds, not one: an earlier version of this test asserted the
        // same thing with end-gap geometry that made the premise false for ~half of all seeds.
        var players = PackedRails(4.0f);
        for (var seed = 1; seed <= 25; seed++)
        {
            Assert.True(OceanFishingContinuousRailPolicy.TrySample(
                new Random(seed),
                players,
                previousDestination: null,
                out var destination));

            var nearest = players.Min(player => Vector3.Distance(destination.Position, player));
            Assert.InRange(
                nearest,
                OceanFishingContinuousRailPolicy.FallbackPlayerClearance,
                OceanFishingContinuousRailPolicy.MinimumPlayerClearance);
            // A fallback pick must carry the fallback arrival tier — gating it at the full minimum is
            // the livelock this contract exists to prevent.
            Assert.Equal(OceanFishingContinuousRailPolicy.FallbackPlayerClearance, destination.ArrivalClearance);
        }
    }

    [Fact]
    public void RecoverySamplingRejectsThePreviousDestination()
    {
        var previous = OceanFishingContinuousRailPolicy.SampleCandidate(new Random(20260801));

        // An empty vessel with an excluded previous point: sampling must succeed somewhere ELSE —
        // at least the minimum clearance away from the excluded destination.
        Assert.True(OceanFishingContinuousRailPolicy.TrySample(
            new Random(20260801),
            Array.Empty<Vector3>(),
            previous,
            out var destination));
        Assert.True(
            Vector3.Distance(destination.Position, previous.Position) >=
            OceanFishingContinuousRailPolicy.MinimumPlayerClearance);
    }

    [Fact]
    public void OtherPlayerPositionsAreClearanceInputsAndNeverDestinations()
    {
        var players = new[]
        {
            new Vector3(100f, 100f, 100f),
            new Vector3(-100f, -100f, -100f),
        };
        Assert.True(OceanFishingContinuousRailPolicy.TrySample(
            new Random(42),
            players,
            previousDestination: null,
            out var destination));
        Assert.DoesNotContain(destination.Position, players);
    }

    [Theory]
    [InlineData(1.5f, 1.5f, true)]
    [InlineData(1.549f, 1.5f, true)]
    [InlineData(1.551f, 1.5f, false)]
    [InlineData(-3.1315928f, 3.1315928f, true)]
    public void FacingVerificationUsesNormalizedCharacterRotation(
        float current,
        float target,
        bool expected)
    {
        Assert.Equal(
            expected,
            OceanFishingContinuousRailPolicy.IsFacingOutward(current, target));
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
        Assert.True(result.AlwaysFishOverride);
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
        Assert.False(result.AlwaysFishOverride);
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
        Assert.True(FishingReturnPolicy.ShouldSuppressCommand(resultAddonVisible: true));
        Assert.False(FishingReturnPolicy.ShouldSuppressCommand(resultAddonVisible: false));
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
    [InlineData(18, false, 128, 1.0, true, false, false, false, 99.0, false, false, false, OceanFishingQueueAction.TravelToLimsa)]
    [InlineData(18, true, 128, 1.0, true, false, false, false, 99.0, false, false, false, OceanFishingQueueAction.TravelToLimsa)]
    [InlineData(18, true, 129, 20.0, true, false, false, false, 99.0, false, false, false, OceanFishingQueueAction.MoveToRegistrar)]
    [InlineData(18, true, 129, 2.001, true, false, false, false, 99.0, false, false, false, OceanFishingQueueAction.MoveToRegistrar)]
    [InlineData(18, true, 129, 2.0, true, false, false, false, 99.0, false, false, false, OceanFishingQueueAction.InteractWithRegistrar)]
    [InlineData(18, true, 129, 1.0, false, false, false, false, 99.0, false, false, false, OceanFishingQueueAction.WaitForRegistrationOpen)]
    [InlineData(18, true, 129, 1.0, true, false, false, false, 99.0, false, false, false, OceanFishingQueueAction.InteractWithRegistrar)]
    [InlineData(18, true, 129, 1.0, false, true, false, false, 99.0, false, false, false, OceanFishingQueueAction.FailRegistrationClosed)]
    [InlineData(18, true, 129, 1.0, false, false, true, false, 99.0, false, false, false, OceanFishingQueueAction.WaitForDeparture)]
    [InlineData(18, true, 129, 1.0, false, false, false, true, 10.0, false, false, false, OceanFishingQueueAction.MoveToFishingPosition)]
    [InlineData(18, true, 129, 1.0, false, false, false, true, 0.5, false, false, false, OceanFishingQueueAction.CastLine)]
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
    public void DockPreparationUsesKnownLimsaRouteAndNpcIdentifiers()
    {
        Assert.Equal((ushort)129, OceanFishingDockPreparationPolicy.LimsaTerritoryType);
        Assert.Equal(1005422u, OceanFishingDockPreparationPolicy.MerchantAndMenderDataId);
        Assert.Equal(1005421u, OceanFishingDockPreparationPolicy.DryskthotaDataId);
        Assert.Equal(43u, OceanFishingDockPreparationPolicy.ArcanistsGuildAethernetId);
        Assert.Equal(29717u, OceanFishingDockPreparationPolicy.VersatileLureItemId);
        Assert.Equal(new Vector3(-399f, 3f, 80f), OceanFishingDockPreparationPolicy.MerchantAndMenderPosition);
    }

    [Fact]
    public void LimsaSettlementDoesNotDependOnVendorVisibility()
    {
        Assert.True(OceanFishingDockPreparationPolicy.IsLimsaSettlementReady(
            territoryType: 129,
            betweenAreas: false,
            playerAvailable: true));
        Assert.False(OceanFishingDockPreparationPolicy.IsLimsaSettlementReady(128, false, true));
        Assert.False(OceanFishingDockPreparationPolicy.IsLimsaSettlementReady(129, true, true));
        Assert.False(OceanFishingDockPreparationPolicy.IsLimsaSettlementReady(129, false, false));
    }

    [Fact]
    public void DockApproachUsesDataIdObjectThenNameThenFixedFallback()
    {
        var fallback = OceanFishingDockPreparationPolicy.MerchantAndMenderPosition;
        var byName = new Vector3(-398f, 3.1f, 79f);
        var byDataId = new Vector3(-397f, 3.2f, 78f);

        Assert.Equal(
            byDataId,
            OceanFishingDockPreparationPolicy.ResolveMerchantApproachPosition(fallback, byDataId, byName));
        Assert.Equal(
            byName,
            OceanFishingDockPreparationPolicy.ResolveMerchantApproachPosition(fallback, null, byName));
        Assert.Equal(
            fallback,
            OceanFishingDockPreparationPolicy.ResolveMerchantApproachPosition(fallback, null, null));
    }

    [Theory]
    [InlineData(false, 22, 22, false)]
    [InlineData(false, 23, 22, false)]
    [InlineData(false, 9, 22, true)]
    [InlineData(true, 22, 22, true)]
    public void DockNavigationOnlyRunsWhenRepairOrLurePreparationIsNeeded(
        bool repairNeeded,
        int currentLures,
        int targetLures,
        bool expected)
    {
        var decision = OceanFishingDockPreparationPolicy.Evaluate(
            repairNeeded,
            currentLures,
            targetLures);

        Assert.Equal(expected, decision.RequiresDockNavigation);
    }

    [Theory]
    [InlineData(0, 22, 22)]
    [InlineData(9, 22, 13)]
    [InlineData(22, 22, 0)]
    [InlineData(30, 22, 0)]
    public void DockRestockRequestsOnlyTheQuantityNeededForTheTarget(
        int currentCount,
        int targetCount,
        int expectedQuantity)
    {
        Assert.Equal(
            expectedQuantity,
            OceanFishingDockPreparationPolicy.RequiredPurchaseQuantity(currentCount, targetCount));
    }

    [Fact]
    public void RestockFailureContinuationUsesFinalInventoryCount()
    {
        Assert.False(OceanFishingDockPreparationPolicy.CanContinueAfterRestockFailure(0));
        Assert.True(OceanFishingDockPreparationPolicy.CanContinueAfterRestockFailure(1));
        Assert.True(OceanFishingDockPreparationPolicy.CanContinueAfterRestockFailure(9));
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

    [Fact]
    public void FishingCastPolicyRetainsExactPairedStartCommandsAndThreeSecondCadence()
    {
        var eligible = EvaluateCast(sinceLastAttempt: TimeSpan.MaxValue);

        Assert.Equal("/ahstart", FishingCastPolicy.CastCommand);
        Assert.Equal("/ac cast", FishingCastPolicy.DirectCastFallbackCommand);
        Assert.Equal(FishingCastDecision.Attempt, eligible.Decision);
    }

    [Fact]
    public void UnacknowledgedPairedStartRetriesEveryThreeSeconds()
    {
        var early = EvaluateCast(sinceLastAttempt: TimeSpan.FromMilliseconds(2999));
        var due = EvaluateCast(sinceLastAttempt: TimeSpan.FromSeconds(3));

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

    [Theory]
    [InlineData(false, true, false, true, false, "disabled")]
    [InlineData(true, false, false, true, false, "Ocean Fishing duty context inactive")]
    [InlineData(true, true, true, true, false, "route transition active")]
    [InlineData(true, true, false, false, false, "player unavailable")]
    [InlineData(true, true, false, true, true, "result window visible")]
    public void AutoHookStartHonorsOnlyLifecycleSafetyGates(
        bool enabled,
        bool inFishingContext,
        bool zoneTransitionActive,
        bool playerAvailable,
        bool resultWindowVisible,
        string expectedGate)
    {
        var evaluation = EvaluateCast(
            enabled,
            inFishingContext,
            zoneTransitionActive,
            playerAvailable,
            resultWindowVisible: resultWindowVisible);

        Assert.Equal(FishingCastDecision.Suppressed, evaluation.Decision);
        Assert.Equal(expectedGate, evaluation.Gate);
    }

    [Fact]
    public void SessionBaitIsConsumedOncePerSession()
    {
        var voyage = new OceanFishingVoyageState();
        voyage.Reset();
        voyage.BeginSession();

        Assert.True(voyage.TryApplySessionBait());
        Assert.False(voyage.TryApplySessionBait());

        voyage.BeginSession();

        Assert.Equal(2, voyage.SessionNumber);
        Assert.True(voyage.TryApplySessionBait());
        Assert.False(voyage.TryApplySessionBait());
    }

    [Fact]
    public void AcknowledgementRequestsImmediateStopAndPermanentlyLocksMovement()
    {
        var voyage = new OceanFishingVoyageState();
        var now = Utc(2026, 7, 21, 22, 0, 0);
        voyage.Reset();
        voyage.BeginPositioning(now);
        voyage.BeginSession();
        voyage.MarkArrived(now);

        var firstAttempt = EvaluateVoyageStart(voyage, now, atDestination: true);
        var acknowledgement = EvaluateVoyageStart(
            voyage,
            now.AddSeconds(1),
            fishingConditionActive: true,
            atDestination: true);
        var repeatedAcknowledgement = EvaluateVoyageStart(
            voyage,
            now.AddSeconds(2),
            gatheringConditionActive: true);

        Assert.Equal(FishingCastDecision.Attempt, firstAttempt.Decision);
        Assert.True(acknowledgement.StopNavigation);
        Assert.False(repeatedAcknowledgement.StopNavigation);
        Assert.True(voyage.FishingEverStarted);
        Assert.True(voyage.MovementLocked);

        voyage.BeginSession();

        Assert.True(voyage.MovementLocked);
        Assert.True(voyage.FishingEverStarted);
    }

    [Fact]
    public void InterruptedFishingRetriesAutoHookStartInPlace()
    {
        var voyage = new OceanFishingVoyageState();
        var now = Utc(2026, 7, 21, 22, 0, 0);
        voyage.Reset();
        voyage.BeginPositioning(now);
        voyage.BeginSession();
        voyage.MarkArrived(now);

        Assert.Equal(
            FishingCastDecision.Attempt,
            EvaluateVoyageStart(voyage, now, atDestination: true).Decision);
        Assert.Equal(
            FishingCastDecision.Acknowledged,
            EvaluateVoyageStart(
                voyage,
                now.AddSeconds(1),
                fishingConditionActive: true,
                atDestination: true).Decision);

        var interrupted = EvaluateVoyageStart(voyage, now.AddSeconds(3));

        Assert.Equal(FishingCastDecision.Attempt, interrupted.Decision);
        Assert.True(voyage.MovementLocked);
        Assert.Equal(2, voyage.SessionStartAttemptCount);
    }

    [Fact]
    public void InitialStartAndPrematureAcknowledgementAreSuppressedBeforeArrivalWithoutMutation()
    {
        var voyage = new OceanFishingVoyageState();
        var now = Utc(2026, 7, 21, 22, 0, 0);
        voyage.Reset();
        voyage.BeginPositioning(now);
        voyage.BeginSession();

        var attempt = EvaluateVoyageStart(voyage, now, atDestination: false);
        var prematureAcknowledgement = EvaluateVoyageStart(
            voyage,
            now.AddSeconds(1),
            gatheringConditionActive: true,
            atDestination: false);

        Assert.Equal(FishingCastDecision.Suppressed, attempt.Decision);
        Assert.Equal("waiting for verified rail placement", attempt.Gate);
        Assert.False(attempt.StopNavigation);
        Assert.Equal(FishingCastDecision.Suppressed, prematureAcknowledgement.Decision);
        Assert.False(prematureAcknowledgement.StopNavigation);
        Assert.Equal(0, voyage.SessionStartAttemptCount);
        Assert.Equal(0, voyage.PostArrivalStartAttemptCount);
        Assert.False(voyage.DestinationArrived);
        Assert.False(voyage.FishingEverStarted);
        Assert.False(voyage.MovementLocked);
    }

    [Fact]
    public void CurrentDistanceMustRemainInsideThresholdUntilFirstAcknowledgement()
    {
        var voyage = new OceanFishingVoyageState();
        var now = Utc(2026, 7, 21, 22, 0, 0);
        voyage.Reset();
        voyage.BeginPositioning(now);
        voyage.BeginSession();
        voyage.MarkArrived(now);

        var driftedAttempt = EvaluateVoyageStart(
            voyage,
            now.AddSeconds(1),
            fishingConditionActive: true,
            atDestination: false);

        Assert.Equal(FishingCastDecision.Suppressed, driftedAttempt.Decision);
        Assert.False(driftedAttempt.StopNavigation);
        Assert.Equal(0, voyage.SessionStartAttemptCount);
        Assert.Equal(0, voyage.PostArrivalStartAttemptCount);
        Assert.False(voyage.FishingEverStarted);
        Assert.False(voyage.MovementLocked);
    }

    [Fact]
    public void ArrivalBlocksUntilTheCompletePlacementGateIsReady()
    {
        var voyage = new OceanFishingVoyageState();
        var now = Utc(2026, 7, 21, 22, 0, 0);
        voyage.Reset();
        voyage.BeginPositioning(now);
        voyage.BeginSession();
        voyage.MarkArrived(now);

        var blocked = EvaluateVoyageStart(
            voyage,
            now,
            atDestination: true,
            initialPlacementReady: false,
            initialPlacementGate: "waiting for one continuous stopped second");
        var attempt = EvaluateVoyageStart(
            voyage,
            now.AddSeconds(1),
            atDestination: true,
            initialPlacementReady: true);

        Assert.Equal(FishingCastDecision.Suppressed, blocked.Decision);
        Assert.Equal("waiting for one continuous stopped second", blocked.Gate);
        Assert.Equal(FishingCastDecision.Attempt, attempt.Decision);
        Assert.Equal(1, voyage.SessionStartAttemptCount);
        Assert.Equal(1, voyage.PostArrivalStartAttemptCount);
        Assert.False(attempt.StopNavigation);
        Assert.False(voyage.ShouldReapplyFacing(now));
    }

    [Fact]
    public void PrematureFishingDoesNotPreventExistingNavigationStallRecovery()
    {
        var voyage = new OceanFishingVoyageState();
        var now = Utc(2026, 7, 21, 22, 0, 0);
        voyage.Reset();
        voyage.BeginPositioning(now);
        voyage.BeginSession();

        Assert.Equal(
            FishingCastDecision.Suppressed,
            EvaluateVoyageStart(voyage, now, fishingConditionActive: true).Decision);
        Assert.Equal(OceanFishingAdvanceReason.None, EvaluateRecovery(voyage, now, distance: 10f));
        Assert.Equal(
            OceanFishingAdvanceReason.NavigationStalled,
            EvaluateRecovery(voyage, now.AddSeconds(10), distance: 10f));
        Assert.False(voyage.FishingEverStarted);
        Assert.False(voyage.MovementLocked);
        Assert.Equal(0, voyage.SessionStartAttemptCount);
    }

    [Fact]
    public void ContinuousDestinationAttemptsAdvanceWithoutFixedListExhaustion()
    {
        var voyage = new OceanFishingVoyageState();
        var now = Utc(2026, 7, 21, 22, 0, 0);
        voyage.Reset();
        voyage.BeginPositioning(now);

        Assert.Equal(1, voyage.DestinationAttemptNumber);
        for (var expected = 2; expected <= 8; expected++)
        {
            Assert.True(voyage.AdvanceDestination(now.AddSeconds(expected)));
            Assert.Equal(expected, voyage.DestinationAttemptNumber);
        }
    }

    [Fact]
    public void NavigationStallAdvancesAfterTenSecondsWithoutQuarterYalmProgress()
    {
        var voyage = new OceanFishingVoyageState();
        var now = Utc(2026, 7, 21, 22, 0, 0);
        voyage.Reset();
        voyage.BeginPositioning(now);

        Assert.Equal(
            OceanFishingAdvanceReason.None,
            EvaluateRecovery(voyage, now, distance: 10f));
        Assert.Equal(
            OceanFishingAdvanceReason.None,
            EvaluateRecovery(voyage, now.AddMilliseconds(9999), distance: 9.8f));
        Assert.Equal(
            OceanFishingAdvanceReason.NavigationStalled,
            EvaluateRecovery(voyage, now.AddSeconds(10), distance: 9.8f));
    }

    [Fact]
    public void NavigationTimeoutAdvancesAfterThirtyActiveSecondsDespiteProgress()
    {
        var voyage = new OceanFishingVoyageState();
        var now = Utc(2026, 7, 21, 22, 0, 0);
        voyage.Reset();
        voyage.BeginPositioning(now);

        Assert.Equal(OceanFishingAdvanceReason.None, EvaluateRecovery(voyage, now, 10f));
        for (var seconds = 5; seconds < 30; seconds += 5)
        {
            Assert.Equal(
                OceanFishingAdvanceReason.None,
                EvaluateRecovery(voyage, now.AddSeconds(seconds), 10f - seconds * 0.1f));
        }

        Assert.Equal(
            OceanFishingAdvanceReason.NavigationTimeout,
            EvaluateRecovery(voyage, now.AddSeconds(30), 7f));
    }

    [Fact]
    public void FalseCanFishAdvancesAfterTenAvailableNonBusyArrivalSeconds()
    {
        var voyage = new OceanFishingVoyageState();
        var now = Utc(2026, 7, 21, 22, 0, 0);
        voyage.Reset();
        voyage.BeginPositioning(now);
        voyage.MarkArrived(now);

        Assert.Equal(
            OceanFishingAdvanceReason.None,
            EvaluateRecovery(voyage, now, 0.25f, atDestination: true, canFish: false));
        Assert.Equal(
            OceanFishingAdvanceReason.None,
            EvaluateRecovery(voyage, now.AddMilliseconds(9999), 0.25f, atDestination: true, canFish: false));
        Assert.Equal(
            OceanFishingAdvanceReason.CannotFish,
            EvaluateRecovery(voyage, now.AddSeconds(10), 0.25f, atDestination: true, canFish: false));
    }

    [Fact]
    public void FivePostArrivalStartAttemptsAdvanceWithoutAcknowledgement()
    {
        var voyage = new OceanFishingVoyageState();
        var now = Utc(2026, 7, 21, 22, 0, 0);
        voyage.Reset();
        voyage.BeginPositioning(now);
        voyage.BeginSession();
        voyage.MarkArrived(now);

        for (var attempt = 0; attempt < OceanFishingVoyageState.PostArrivalAttemptLimit; attempt++)
        {
            Assert.Equal(
                FishingCastDecision.Attempt,
                EvaluateVoyageStart(voyage, now.AddSeconds(attempt * 3), atDestination: true).Decision);
        }

        Assert.Equal(OceanFishingVoyageState.PostArrivalAttemptLimit, voyage.PostArrivalStartAttemptCount);
        Assert.Equal(
            OceanFishingAdvanceReason.StartUnacknowledged,
            EvaluateRecovery(voyage, now.AddSeconds(12), 0.25f, atDestination: true, canFish: true));
    }

    [Fact]
    public void PausedRecoveryTimeDoesNotCountTowardNavigationStall()
    {
        var voyage = new OceanFishingVoyageState();
        var now = Utc(2026, 7, 21, 22, 0, 0);
        voyage.Reset();
        voyage.BeginPositioning(now);

        Assert.Equal(OceanFishingAdvanceReason.None, EvaluateRecovery(voyage, now, 10f));
        Assert.Equal(OceanFishingAdvanceReason.None, EvaluateRecovery(voyage, now.AddSeconds(5), 9.9f));
        Assert.Equal(
            OceanFishingAdvanceReason.None,
            EvaluateRecovery(voyage, now.AddSeconds(35), 9.9f, timersPaused: true));
        Assert.Equal(OceanFishingAdvanceReason.None, EvaluateRecovery(voyage, now.AddSeconds(39), 9.9f));
        Assert.Equal(
            OceanFishingAdvanceReason.NavigationStalled,
            EvaluateRecovery(voyage, now.AddSeconds(40), 9.9f));
    }

    [Fact]
    public void ArrivalFacingWaitsHalfSecondThenReappliesAtMostOncePerSecond()
    {
        var voyage = new OceanFishingVoyageState();
        var now = Utc(2026, 7, 21, 22, 0, 0);
        voyage.Reset();
        voyage.BeginPositioning(now);
        voyage.MarkArrived(now);

        Assert.False(voyage.ShouldReapplyFacing(now.AddMilliseconds(499)));
        Assert.True(voyage.ShouldReapplyFacing(now.AddMilliseconds(500)));
        Assert.False(voyage.ShouldReapplyFacing(now.AddMilliseconds(1499)));
        Assert.True(voyage.ShouldReapplyFacing(now.AddMilliseconds(1500)));
    }

    [Fact]
    public void PathMustRemainStoppedForOneContinuousSecondBeforeCasting()
    {
        var voyage = ArrivedVoyage(out var now);

        Assert.False(EvaluatePlacement(voyage, now, pathRunning: false).Ready);
        Assert.False(EvaluatePlacement(
            voyage,
            now.AddMilliseconds(999),
            pathRunning: false).Ready);
        Assert.False(EvaluatePlacement(
            voyage,
            now.AddSeconds(1),
            pathRunning: true).Ready);
        Assert.False(EvaluatePlacement(
            voyage,
            now.AddSeconds(2),
            pathRunning: false).Ready);
        Assert.False(EvaluatePlacement(
            voyage,
            now.AddMilliseconds(2999),
            pathRunning: false).Ready);
        Assert.True(EvaluatePlacement(
            voyage,
            now.AddSeconds(3),
            pathRunning: false).Ready);
    }

    [Fact]
    public void InitialPlacementSafetyChangesResetStoppedSettlement()
    {
        var voyage = ArrivedVoyage(out var now);

        Assert.False(EvaluatePlacement(voyage, now, pathRunning: false).Ready);
        var crowded = EvaluatePlacement(
            voyage,
            now.AddSeconds(1),
            playerClear: false,
            pathRunning: false);
        Assert.True(crowded.ShouldResample);
        Assert.False(EvaluatePlacement(
            voyage,
            now.AddSeconds(2),
            pathRunning: false).Ready);
        Assert.True(EvaluatePlacement(
            voyage,
            now.AddSeconds(3),
            pathRunning: false).Ready);
    }

    [Fact]
    public void MissingPathStatusFailsClosedAfterTenActiveSeconds()
    {
        var voyage = ArrivedVoyage(out var now);

        var initial = EvaluatePlacement(
            voyage,
            now,
            pathStatusAvailable: false);
        var beforeBoundary = EvaluatePlacement(
            voyage,
            now.AddMilliseconds(9999),
            pathStatusAvailable: false);
        var boundary = EvaluatePlacement(
            voyage,
            now.AddSeconds(10),
            pathStatusAvailable: false);

        Assert.False(initial.Ready);
        Assert.False(initial.ShouldAbort);
        Assert.Equal("waiting for vnavmesh path status", initial.Gate);
        Assert.False(beforeBoundary.ShouldAbort);
        Assert.True(boundary.ShouldAbort);
    }

    [Fact]
    public void UnverifiedCharacterFacingRequestsResampleAfterTenSettledSeconds()
    {
        var voyage = ArrivedVoyage(out var now);

        Assert.False(EvaluatePlacement(
            voyage,
            now,
            pathRunning: false,
            facingVerified: false).Ready);
        Assert.False(EvaluatePlacement(
            voyage,
            now.AddSeconds(1),
            pathRunning: false,
            facingVerified: false).ShouldResample);
        Assert.False(EvaluatePlacement(
            voyage,
            now.AddMilliseconds(10999),
            pathRunning: false,
            facingVerified: false).ShouldResample);
        Assert.True(EvaluatePlacement(
            voyage,
            now.AddSeconds(11),
            pathRunning: false,
            facingVerified: false).ShouldResample);
    }

    [Theory]
    [InlineData(false, false, true, false, true, "Ocean Fishing duty context inactive")]
    [InlineData(true, true, true, false, true, "route transition active")]
    [InlineData(true, false, false, false, true, "player unavailable")]
    [InlineData(true, false, true, true, true, "placement verification paused by unsafe player state")]
    [InlineData(true, false, true, false, false, "waiting to reach continuous rail destination")]
    public void PlacementPrerequisitesIndependentlyBlockReadiness(
        bool inFishingContext,
        bool zoneTransitionActive,
        bool playerAvailable,
        bool timersPaused,
        bool atDestination,
        string expectedGate)
    {
        var voyage = ArrivedVoyage(out var now);

        var evaluation = EvaluatePlacement(
            voyage,
            now,
            inFishingContext,
            zoneTransitionActive,
            playerAvailable,
            timersPaused,
            atDestination);

        Assert.False(evaluation.Ready);
        Assert.Equal(expectedGate, evaluation.Gate);
    }

    [Fact]
    public void CanFishFallbackDoesNotAccumulateBeforePlacementReadiness()
    {
        var voyage = ArrivedVoyage(out var now);

        Assert.Equal(
            OceanFishingAdvanceReason.None,
            EvaluateRecovery(
                voyage,
                now,
                0.25f,
                atDestination: true,
                canFish: false,
                timersPaused: true));
        Assert.Equal(
            OceanFishingAdvanceReason.None,
            EvaluateRecovery(
                voyage,
                now.AddSeconds(20),
                0.25f,
                atDestination: true,
                canFish: false,
                timersPaused: true));
        Assert.Equal(
            OceanFishingAdvanceReason.None,
            EvaluateRecovery(
                voyage,
                now.AddSeconds(20),
                0.25f,
                atDestination: true,
                canFish: false));
        Assert.Equal(
            OceanFishingAdvanceReason.CannotFish,
            EvaluateRecovery(
                voyage,
                now.AddSeconds(30),
                0.25f,
                atDestination: true,
                canFish: false));
    }

    [Fact]
    public void FishingAcknowledgementPermanentlyForbidsDestinationAdvancesAndFacingRetries()
    {
        var voyage = new OceanFishingVoyageState();
        var now = Utc(2026, 7, 21, 22, 0, 0);
        voyage.Reset();
        voyage.BeginPositioning(now);
        voyage.BeginSession();
        voyage.MarkArrived(now);

        var acknowledgement = EvaluateVoyageStart(
            voyage,
            now,
            gatheringConditionActive: true,
            atDestination: true);

        Assert.True(acknowledgement.StopNavigation);
        Assert.False(voyage.AdvanceDestination(now.AddSeconds(10)));
        Assert.False(voyage.ShouldReapplyFacing(now.AddSeconds(10)));
        Assert.Equal(
            OceanFishingAdvanceReason.None,
            EvaluateRecovery(voyage, now.AddMinutes(1), 0.25f, atDestination: true, canFish: false));
        Assert.Equal(1, voyage.DestinationAttemptNumber);
    }

    [Fact]
    public void RouteTransitionPausesThenAllowsImmediateSessionStartAttempt()
    {
        var duringTransition = EvaluateCast(zoneTransitionActive: true);
        var afterTransition = EvaluateCast(zoneTransitionActive: false);

        Assert.Equal("route transition active", duringTransition.Gate);
        Assert.Equal(FishingCastDecision.Attempt, afterTransition.Decision);
    }

    [Fact]
    public void CurrentVersionDutyGateRejectsStaleFishingAndGatheringConditions()
    {
        var staleFishing = EvaluateCast(
            inFishingContext: false,
            fishingConditionActive: true);
        var staleGathering = EvaluateCast(
            inFishingContext: false,
            gatheringConditionActive: true);

        Assert.Equal(FishingCastDecision.Suppressed, staleFishing.Decision);
        Assert.Equal("Ocean Fishing duty context inactive", staleFishing.Gate);
        Assert.Equal(FishingCastDecision.Suppressed, staleGathering.Decision);
        Assert.Equal("Ocean Fishing duty context inactive", staleGathering.Gate);
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
        bool enabled = true,
        bool inFishingContext = true,
        bool zoneTransitionActive = false,
        bool playerAvailable = true,
        bool gatheringConditionActive = false,
        bool fishingConditionActive = false,
        bool resultWindowVisible = false,
        TimeSpan? sinceLastAttempt = null)
        => FishingCastPolicy.Evaluate(
            enabled,
            inFishingContext,
            zoneTransitionActive,
            playerAvailable,
            gatheringConditionActive,
            fishingConditionActive,
            resultWindowVisible,
            sinceLastAttempt ?? TimeSpan.MaxValue);

    private static OceanFishingStartEvaluation EvaluateVoyageStart(
        OceanFishingVoyageState voyage,
        DateTimeOffset nowUtc,
        bool gatheringConditionActive = false,
        bool fishingConditionActive = false,
        bool atDestination = false,
        bool initialPlacementReady = true,
        string initialPlacementGate = "")
        => voyage.EvaluateFishingStart(
            nowUtc,
            enabled: true,
            inFishingContext: true,
            zoneTransitionActive: false,
            playerAvailable: true,
            gatheringConditionActive,
            fishingConditionActive,
            resultWindowVisible: false,
            atDestination,
            initialPlacementReady,
            initialPlacementGate);

    private static OceanFishingVoyageState ArrivedVoyage(out DateTimeOffset now)
    {
        now = Utc(2026, 7, 24, 22, 0, 0);
        var voyage = new OceanFishingVoyageState();
        voyage.Reset();
        voyage.BeginPositioning(now);
        voyage.MarkArrived(now);
        return voyage;
    }

    private static OceanFishingPlacementEvaluation EvaluatePlacement(
        OceanFishingVoyageState voyage,
        DateTimeOffset nowUtc,
        bool inFishingContext = true,
        bool zoneTransitionActive = false,
        bool playerAvailable = true,
        bool timersPaused = false,
        bool atDestination = true,
        bool playerClear = true,
        bool pathStatusAvailable = true,
        bool pathRunning = false,
        bool facingVerified = true)
        => voyage.EvaluatePlacementReadiness(
            nowUtc,
            inFishingContext,
            zoneTransitionActive,
            playerAvailable,
            timersPaused,
            atDestination,
            playerClear,
            pathStatusAvailable,
            pathRunning,
            facingVerified);

    private static OceanFishingAdvanceReason EvaluateRecovery(
        OceanFishingVoyageState voyage,
        DateTimeOffset nowUtc,
        float distance,
        bool atDestination = false,
        bool canFish = false,
        bool timersPaused = false)
        => voyage.EvaluateRecovery(
            nowUtc,
            distance,
            atDestination,
            canFish,
            timersPaused);

}
