using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Newtonsoft.Json.Linq;
using VERMAXION.Beastmaster;
using Xunit;

namespace VERMAXION.Tests;

public sealed class BeastmasterSelectionRegressionTests
{
    [Theory]
    [InlineData(0, false, 10, 0)]
    [InlineData(1, false, 10, 0)] // Registration and scanning are not completed boards.
    [InlineData(1, true, 10, 1)]
    [InlineData(2, false, 10, 1)] // Next entry preserves the previous result.
    [InlineData(3, false, 10, 2)] // Stop during the third board retains two results.
    [InlineData(3, true, 10, 3)]
    [InlineData(10, true, 10, 10)]
    public void DisplayCountsObservedBoardsInsteadOfStartedAttempts(int current, bool result, int limit, int completed)
        => Assert.Equal(completed, BeastmasterSelection.CompletedBoardRuns(current, result, limit));

    [Fact]
    public void CustomPreparationNeedsOnlyItsOwnedSelectionWithoutRankScanning()
    {
        var settings = new BeastmasterSettings { TeamMode = CrucibleTeamMode.Custom, CustomTeam = [13, 20, 27] };
        BeastmasterCandidate[] owned = [new(13, 0, float.NaN, 0), new(20, 0, float.NaN, 1),
            new(27, 0, float.NaN, 2), new(44, 0, float.NaN, 3)];

        Assert.True(BeastmasterSelection.TryPrepareTeam(settings, owned, 10, out var team, out var reason), reason);
        Assert.Equal(settings.CustomTeam, team);
        Assert.Empty(reason);
        Assert.All(owned, beast => Assert.Equal(0, beast.Rank));

        Assert.False(BeastmasterSelection.TryPrepareTeam(settings, owned.Where(beast => beast.Number != 27), 10,
            out _, out reason));
        Assert.Contains("Unowned or excluded familiars: 27.", reason);
        Assert.DoesNotContain("44", reason);
    }

    [Theory]
    [InlineData(CrucibleTeamMode.Recommended)]
    [InlineData(CrucibleTeamMode.Leveling)]
    public void RankedPreparationUsesKnownCandidatesDespiteAnUnrelatedUnreadBeast(CrucibleTeamMode mode)
    {
        var settings = new BeastmasterSettings { TeamMode = mode };
        BeastmasterCandidate[] owned = [new(13, 4, 20, 0), new(20, 3, 40, 1), new(27, 8, 0, 2),
            new(44, 0, float.NaN, 3), new(19, 26, 0, 4)];

        Assert.True(BeastmasterSelection.TryPrepareTeam(settings, owned, 10, out var team, out var reason), reason);
        Assert.Equal(mode == CrucibleTeamMode.Recommended ? new uint[] { 27, 13, 20 } : [20, 13, 27], team);
        Assert.DoesNotContain(44u, team);
        Assert.DoesNotContain(19u, team);
        Assert.Equal(0, owned.Single(beast => beast.Number == 44).Rank);
    }

    [Fact]
    public void RequiredUnreadRanksAreNamedWithoutIncludingExcludedOrWrongKinBeasts()
    {
        var settings = new BeastmasterSettings { TeamMode = CrucibleTeamMode.Recommended, ClassificationLock = 1 };
        BeastmasterCandidate[] owned = [new(13, 4, 20, 0, Classification: 1), new(20, 3, 40, 1, Classification: 1),
            new(27, 0, float.NaN, 2, Classification: 1), new(44, 0, float.NaN, 3, Classification: 1, PickBlocked: true),
            new(19, 0, float.NaN, 4, Classification: 2)];

        Assert.False(BeastmasterSelection.TryPrepareTeam(settings, owned, 10, out var team, out var reason));
        Assert.Equal(new uint[] { 13, 20 }, team);
        Assert.Equal("Not enough eligible familiars have recorded ranks. Missing ranks: 27.", reason);
    }

    [Fact]
    public void UnknownExperienceDoesNotBecomeZeroForLevelingOrder()
    {
        BeastmasterCandidate[] owned = [new(13, 4, float.NaN, 0), new(20, 4, 50, 1), new(27, 3, float.NaN, 2)];

        Assert.Equal(new uint[] { 27, 20, 13 }, BeastmasterSelection.Leveling(owned, 3, false));
        Assert.True(float.IsNaN(owned[0].Experience));
    }

    [Fact]
    public void TrainingPreparationRunsKnownTraineesAndMinimalSupportWhileUnknownsRemainIncomplete()
    {
        var settings = new BeastmasterSettings { Goal = BeastmasterRunGoal.TrainOwned,
            TeamMode = CrucibleTeamMode.Leveling, Board = 1 };
        BeastmasterCandidate[] owned = [new(13, 4, 20, 0), new(20, 3, 40, 1), new(27, 8, 0, 2),
            new(44, 0, float.NaN, 3), new(19, 1, 0, 4, PickBlocked: true), new(7, 10, 0, 5)];

        Assert.True(BeastmasterSelection.TryPrepareTeam(settings, owned, 10, out var team, out var reason), reason);
        Assert.Equal(new uint[] { 20, 13, 7 }, team);
        Assert.DoesNotContain(27u, team);
        Assert.DoesNotContain(44u, team);
        Assert.DoesNotContain(19u, team);

        var observed = owned.ToDictionary(beast => beast.Number, beast => new CrucibleFamiliar(beast.Number)
            { Rank = beast.Number is 13 or 20 ? 5 : beast.Rank, Exp = beast.Rank > 0 ? "0" : "",
                ObservedAtUtc = beast.Rank > 0 ? DateTime.UtcNow : DateTime.MinValue });
        Assert.False(BeastmasterSelection.TrainingComplete(owned.Select(beast => beast.Number), observed, 5));
    }

    [Fact]
    public void TrainingPreparationDoesNotSubstituteFarmingWhenOnlyUnreadOrExcludedTraineesRemain()
    {
        var settings = new BeastmasterSettings { Goal = BeastmasterRunGoal.TrainOwned,
            TeamMode = CrucibleTeamMode.Leveling, Board = 1 };
        BeastmasterCandidate[] owned = [new(13, 5, 0, 0), new(20, 8, 0, 1), new(27, 10, 0, 2),
            new(44, 0, float.NaN, 3), new(19, 1, 0, 4, PickBlocked: true)];

        Assert.False(BeastmasterSelection.TryPrepareTeam(settings, owned, 10, out var team, out var reason));
        Assert.Empty(team);
        Assert.Contains("Missing ranks: 44.", reason);

        settings.TeamMode = CrucibleTeamMode.Recommended;
        Assert.False(BeastmasterSelection.TryPrepareTeam(settings, owned, 10, out team, out reason));
        Assert.Empty(team);
        Assert.Equal("Training all owned beasts requires a training team method.", reason);
    }

    [Fact]
    public void DuplicateCandidatesAndSelectionsCannotSatisfyTheEntryMinimum()
    {
        var settings = new BeastmasterSettings { TeamMode = CrucibleTeamMode.Recommended };
        BeastmasterCandidate[] owned = [new(13, 4, 0, 0), new(13, 4, 0, 0), new(20, 3, 0, 1)];
        Assert.False(BeastmasterSelection.TryPrepareTeam(settings, owned, 10, out var team, out _));
        Assert.Equal(new uint[] { 13, 20 }, team);
        settings.TeamMode = CrucibleTeamMode.Custom;
        settings.CustomTeam = [13, 13, 20];
        Assert.False(BeastmasterSelection.TryPrepareTeam(settings, owned, 10, out team, out _));
        Assert.Equal(new uint[] { 13, 20 }, team);
    }

    [Fact]
    public void SplashAdvancesAtTenSecondsAndReopeningResetsTheIntervalWithoutARepeat()
    {
        var full = new BeastmasterSplashRotation();
        full.Open(1_000);
        var first = full.Current(1_000);
        Assert.InRange(first, 0, 49);
        Assert.Equal(first, full.Current(10_999));
        var second = full.Current(11_000);
        Assert.NotEqual(first, second);
        Assert.Equal(second, full.Current(14_999));

        full.Open(15_000);
        var reopened = full.Current(15_000);
        Assert.NotEqual(second, reopened);
        Assert.Equal(reopened, full.Current(24_999));
        Assert.NotEqual(reopened, full.Current(25_000));

        var mini = new BeastmasterSplashRotation();
        mini.Open(50_000);
        var miniFirst = mini.Current(50_000);
        full.Open(52_000);
        Assert.Equal(miniFirst, mini.Current(59_999));
        Assert.NotEqual(miniFirst, mini.Current(60_000));
    }

    [Fact]
    public void ResumeUsesCurrentTileAndKeepsTheRoutesPostFightAndBranchInstructions()
    {
        var first = Route(1339);
        Assert.Equal(1, BeastmasterSelection.ResumeRoute(first, new(-700, 0, 0))); // Fresh entrance.
        Assert.Equal(25, BeastmasterSelection.ResumeRoute(first, new(-700, 0, -32.9f))); // Fourth tile cleared.
        Assert.Equal(36, BeastmasterSelection.ResumeRoute(first, new(-700, 0, -50.9f))); // Reused Bone Knight arena.
        Assert.Equal(18, BeastmasterSelection.ResumeRoute(first, new(-705, 0, -24.9f))); // Camp settled.
        Assert.Equal(52, BeastmasterSelection.ResumeRoute(first, new(-700, 0, -75))); // Final fight cleared.
        Assert.Equal(16, BeastmasterSelection.ResumeRoute(Route(1340), new(-695, 0, -34))); // Positioned rest.
        // Rejoin after the left fight, before the native route skips the right branch.
        Assert.Equal(23, BeastmasterSelection.ResumeRoute(Route(1343), new(-709, 0, -26)));
    }

    [Fact]
    public void ResumeDoesNotGuessProgressFromAReusedArena()
    {
        foreach (var territory in new uint[] { 1339, 1340, 1341, 1342, 1343 })
        {
            var actions = Route(territory);
            Assert.Null(BeastmasterSelection.ResumeRoute(actions, new(120, 0, -420)));
            Assert.Null(BeastmasterSelection.ResumeRoute(actions, new(520, 0, 0)));
            Assert.NotNull(BeastmasterSelection.ResumeRoute(actions, new(-700, 0, -8)));
        }
    }

    private static JArray Route(uint territory)
    {
        var assembly = typeof(BeastmasterSelectionRegressionTests).Assembly;
        var name = assembly.GetManifestResourceNames().Single(name => name.Contains($"({territory})", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return (JArray)JObject.Parse(reader.ReadToEnd())["Actions"]!;
    }

    [Fact]
    public void TeamSetupRequiresObservedAdditionAndExactMembership()
    {
        uint[] intended = [13, 20, 27, 19, 7, 44, 2, 17, 15, 39];
        var before = intended[..^1];

        Assert.True(BeastmasterSelection.TeamMatches(intended.Reverse().ToArray(), intended));
        Assert.False(BeastmasterSelection.TeamMatches(before, intended));
        Assert.False(BeastmasterSelection.TeamMatches(intended.Select(id => id == 39 ? 13u : id).ToArray(), intended));

        // Dispatching an input leaves the same native rows pending; only the added
        // familiar alongside every prior member confirms that operation.
        Assert.Equal(BeastmasterTeamAddition.Pending, BeastmasterSelection.ObserveAddition(before, before, 39));
        Assert.Equal(BeastmasterTeamAddition.Confirmed, BeastmasterSelection.ObserveAddition(before, intended, 39));
        Assert.Equal(BeastmasterTeamAddition.Unexpected, BeastmasterSelection.ObserveAddition(before, [..before, 50], 39));
        Assert.Equal(BeastmasterTeamAddition.Unexpected, BeastmasterSelection.ObserveAddition(before, [..before.Skip(1), 39], 39));
        Assert.Equal(BeastmasterTeamAddition.Unexpected, BeastmasterSelection.ObserveAddition(before, [..before, 39, 50], 39));
        Assert.Equal(BeastmasterTeamAddition.Pending, BeastmasterSelection.ObserveAddition(intended, intended, 39));
    }

    [Fact]
    public void EntryRequiresThreeDifferentFamiliarsButBattlesKeepTheRemainingSurvivors()
    {
        Assert.False(BeastmasterSelection.CanEnterBoard([]));
        Assert.False(BeastmasterSelection.CanEnterBoard([0, 13, 13, 20]));
        Assert.True(BeastmasterSelection.CanEnterBoard([13, 20, 27]));

        BeastmasterCandidate[] team = [new(13, 9, 0, 0), new(20, 8, 0, 1), new(27, 7, 0, 2)];
        var survivors = team.Select(beast => beast.Number == 13 ? beast with { Alive = false } : beast).ToArray();
        Assert.False(BeastmasterSelection.CanEnterBoard(survivors.Where(beast => beast.Alive).Select(beast => beast.Number)));
        Assert.Equal(new uint[] { 20, 27 }, BeastmasterSelection.FightOrder(survivors, leveling: false));
        Assert.Equal(new uint[] { 27 }, BeastmasterSelection.FightOrder(survivors.Select(beast => beast.Number == 20 ? beast with { Alive = false } : beast), leveling: false));
    }

    [Fact]
    public void FightBlockedFamiliarIsReserveUntilThreeAlternativesAreUnavailable()
    {
        BeastmasterCandidate[] team =
        [
            new(13, 9, 0, 0, FightBlocked: true),
            new(20, 8, 0, 1),
            new(27, 8, 0, 2),
            new(19, 7, 0, 3),
            new(7, 1, 50, 4),
        ];

        Assert.Equal(new uint[] { 20, 27, 19 }, BeastmasterSelection.FightOrder(team, leveling: false).Take(3));
        Assert.Equal(new uint[] { 7, 19, 20 }, BeastmasterSelection.FightOrder(team, leveling: true).Take(3));

        var injuredTeam = team.Select(beast => beast.Number is 20 or 27 ? beast with { Alive = false } : beast).ToArray();
        Assert.Equal(new uint[] { 13, 19, 7 }, BeastmasterSelection.FightOrder(injuredTeam, leveling: false).Take(3));
        Assert.Empty(BeastmasterSelection.FightOrder(team.Select(beast => beast with { Alive = false }), leveling: false));
    }

    [Fact]
    public void TrainingUsesBoardTargetsAndPadsOnlyTheLastOneOrTwoEligibleTrainees()
    {
        Assert.Equal(new[] { 5, 10, 15, 20, 25 }, Enumerable.Range(1, 5).Select(board => BeastmasterSelection.TrainingTarget((uint)board)));
        BeastmasterCandidate[] owned = [new(1, 4, 20, 0), new(2, 3, 40, 1), new(3, 20, 0, 2),
            new(4, 15, 0, 3), new(5, 10, 0, 4), new(6, 1, 0, 5, PickBlocked: true)];
        Assert.Equal(new uint[] { 2, 1, 3 }, BeastmasterSelection.Training(owned, 10, false, 5));
        Assert.Equal(new uint[] { 1, 3, 4 }, BeastmasterSelection.Training(owned.Where(b => b.Number != 2), 10, false, 5));
        Assert.Equal(new uint[] { 3, 4, 5, 2, 1 }, BeastmasterSelection.Training(owned, 8, true, 5));
        Assert.Empty(BeastmasterSelection.Training(owned.Where(b => b.Rank >= 5), 10, false, 5));
        Assert.Equal(new uint[] { 2, 1 }, BeastmasterSelection.Training(owned.Where(b => b.Rank < 5), 10, false, 5));
    }

    [Fact]
    public void EditedManualTargetControlsTraineesAndCompletionInsteadOfTheBoardDefault()
    {
        var settings = new BeastmasterSettings
        {
            Goal = BeastmasterRunGoal.TrainOwned, TeamMode = CrucibleTeamMode.Leveling,
            Board = 1, TrainingTargetRank = 8,
        };
        BeastmasterCandidate[] owned = [new(1, 2, 0, 0), new(2, 4, 0, 1), new(3, 6, 0, 2), new(4, 9, 0, 3)];
        Assert.True(BeastmasterSelection.TryPrepareTeam(settings, owned, 10, out var editedTeam, out _));
        Assert.Equal(new uint[] { 1, 2, 3 }, editedTeam);

        var records = new uint[] { 1, 2, 3, 4 }.ToDictionary(number => number, number => new CrucibleFamiliar(number)
            { Rank = 5, Exp = "0", ObservedAtUtc = DateTime.UtcNow });
        var numbers = records.Keys.ToArray();
        Assert.False(BeastmasterSelection.RequiresTrainingCompletionRead(settings, numbers, records));
        Assert.False(BeastmasterSelection.TrainingCompletionConfirmed(numbers, records,
            BeastmasterSelection.TrainingTarget(settings), true, numbers));

        foreach (var beast in records.Values) beast.Rank = 8;
        Assert.True(BeastmasterSelection.RequiresTrainingCompletionRead(settings, numbers, records));
        Assert.True(BeastmasterSelection.TrainingCompletionConfirmed(numbers, records,
            BeastmasterSelection.TrainingTarget(settings), true, numbers));

        settings.TrainingTargetRank = 0;
        Assert.True(BeastmasterSelection.TryPrepareTeam(settings, owned, 10, out var boardDefaultTeam, out _));
        Assert.Equal(new uint[] { 1, 2, 4 }, boardDefaultTeam);
    }

    [Fact]
    public void AllOwnedGoalIncludesExcludedBeastsAndNeverCompletesFromUnknownOrEmptyRecords()
    {
        var data = new BeastmasterCharacterData();
        data.EnsureFamiliars([1, 2, 3]);
        Assert.False(BeastmasterSelection.TrainingComplete([], data.Familiars, 5));
        Assert.False(BeastmasterSelection.TrainingComplete([1, 2, 3], data.Familiars, 5));
        foreach (var number in new uint[] { 1, 2, 3 }) data.Familiars[number] = new CrucibleFamiliar(number)
            { Rank = 5, Exp = "0", ObservedAtUtc = DateTime.UtcNow };
        Assert.True(BeastmasterSelection.TrainingComplete([1, 2, 3], data.Familiars, 5));
        // A newly owned beast reopens the goal even when it cannot be picked.
        data.Familiars[4] = new CrucibleFamiliar(4) { Rank = 1, Exp = "0", ObservedAtUtc = DateTime.UtcNow };
        Assert.False(BeastmasterSelection.TrainingComplete([1, 2, 3, 4], data.Familiars, 5));
        data.Familiars[4].Rank = 5;
        data.Familiars[4].ObservedAtUtc = DateTime.MinValue;
        Assert.False(BeastmasterSelection.TrainingComplete([1, 2, 3, 4], data.Familiars, 5));
    }

    [Fact]
    public void StartupOnlyRequestsFullReadForKnownTrainingTargetsNotUnavailableOwnershipOrRanks()
    {
        var settings = new BeastmasterSettings { Goal = BeastmasterRunGoal.TrainOwned, TeamMode = CrucibleTeamMode.Leveling, Board = 1 };
        var records = new uint[] { 1, 2, 3 }.ToDictionary(number => number,
            number => new CrucibleFamiliar(number) { Rank = 5 });
        Assert.False(BeastmasterSelection.RequiresTrainingCompletionRead(settings, null, records));
        Assert.False(BeastmasterSelection.RequiresTrainingCompletionRead(settings, [], records));
        Assert.False(BeastmasterSelection.RequiresTrainingCompletionRead(settings, [1, 2, 3, 4], records));
        records[4] = new CrucibleFamiliar(4) { Rank = 4 };
        Assert.False(BeastmasterSelection.RequiresTrainingCompletionRead(settings, [1, 2, 3, 4], records));
        records[4].Rank = 5;
        // Older records can suggest completion and justify a read, but cannot
        // establish completion until that actual current read succeeds.
        Assert.True(BeastmasterSelection.RequiresTrainingCompletionRead(settings, [1, 2, 3, 4], records));
        Assert.False(BeastmasterSelection.TrainingCompletionConfirmed([1, 2, 3, 4], records, 5, false, null));
        settings.Goal = BeastmasterRunGoal.FixedRuns;
        Assert.False(BeastmasterSelection.RequiresTrainingCompletionRead(settings, [1, 2, 3, 4], records));
    }

    [Fact]
    public void ConditionalCompletionRequiresSuccessfulCurrentReadOfEveryFrozenOwnedBeast()
    {
        uint[] owned = [1, 2, 3];
        var observations = owned.ToDictionary(number => number, number => new CrucibleFamiliar(number)
            { Rank = 5, Exp = "0", ObservedAtUtc = DateTime.UtcNow.AddDays(-1) });
        // Saved target ranks alone and a partial operation are not fresh proof.
        Assert.False(BeastmasterSelection.TrainingCompletionConfirmed(owned, observations, 5, false, owned));
        Assert.False(BeastmasterSelection.TrainingCompletionConfirmed(owned, observations, 5, true, null));
        Assert.False(BeastmasterSelection.TrainingCompletionConfirmed(owned, observations, 5, true, [1, 2]));
        Assert.True(BeastmasterSelection.TrainingCompletionConfirmed(owned, observations, 5, true, owned));
        observations[3] = new CrucibleFamiliar(3) { Rank = 4, Exp = "0", ObservedAtUtc = DateTime.UtcNow };
        Assert.False(BeastmasterSelection.TrainingCompletionConfirmed(owned, observations, 5, true, owned));
        observations[3].Rank = 5;
        // A newly caught beast applies to the next trigger's frozen roster.
        Assert.True(BeastmasterSelection.TrainingCompletionConfirmed(owned, observations, 5, true, [1, 2, 3, 4]));
        Assert.False(BeastmasterSelection.TrainingCompletionConfirmed([1, 2, 3, 4], observations, 5, true, [1, 2, 3, 4]));
    }

    [Fact]
    public void ParticipatingResultRanksMustCoverTheWholeTeamBeforeAnotherTeamDecision()
    {
        uint[] team = [1, 2, 3, 4];
        Assert.True(BeastmasterSelection.ResultRanksComplete(team, [(1, 5), (2, 6), (3, 25), (4, 1)]));
        Assert.False(BeastmasterSelection.ResultRanksComplete(team, [(1, 5), (2, 6), (3, 25)]));
        Assert.False(BeastmasterSelection.ResultRanksComplete(team, [(1, 5), (2, 0), (3, 25), (4, 1)]));
        Assert.False(BeastmasterSelection.ResultRanksComplete(team, [(1, 5), (2, 6), (3, 25), (3, 25)]));
    }

    [Fact]
    public void CarryTeamKeepsTraineeSlotsWhenCarriersAlsoHaveTheLeastExperience()
    {
        BeastmasterCandidate[] owned =
        [
            new(13, 1, 0, 0),
            new(20, 1, 1, 1),
            new(27, 1, 2, 2),
            new(19, 1, 3, 3),
            new(7, 1, 4, 4),
            new(44, 1, 5, 5),
        ];

        Assert.Equal(new uint[] { 13, 20, 27, 19, 7 },
            BeastmasterSelection.Leveling(owned, cap: 5, carry: true));
        Assert.Equal(new uint[] { 20, 27, 19, 7, 44 },
            BeastmasterSelection.Leveling(owned.Select(beast => beast.Number == 13 ? beast with { PickBlocked = true } : beast), cap: 5, carry: true));
    }
}
