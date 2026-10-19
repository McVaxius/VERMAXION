using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Newtonsoft.Json.Linq;

namespace VERMAXION.Beastmaster;

public readonly record struct BeastmasterCandidate(uint Number, int Rank, float Experience, int Tier,
    byte Classification = 0, bool PickBlocked = false, bool FightBlocked = false, bool Alive = true);

public enum BeastmasterTeamAddition { Pending, Confirmed, Unexpected }

public static class BeastmasterSelection
{
    // null means an arena: handle its current enemies, then locate the tile
    // after the game returns the player to the board. Arena positions are reused.
    internal static int? ResumeRoute(JArray actions, Vector3 player)
    {
        var stops = actions.Select((action, index) =>
            (Index: index, Name: action!.Value<string>("Name"), Position: RoutePosition(action["Position"])))
            .Where(action => action.Name is "MoveTo" or "Wait" && action.Position != Vector3.Zero).ToArray();
        if (stops.Length == 0) return null;
        var nearest = stops.MinBy(stop => Vector3.DistanceSquared(stop.Position, player));
        var distance = Vector3.DistanceSquared(nearest.Position, player);
        if (distance > 40 * 40) return null;
        // Before reaching a tile, approach it. At/past it with its native menus
        // settled, continue after its encounter rather than waiting to replay it.
        if (distance > 3 * 3 && player.Z > nearest.Position.Z + 0.5f) return nearest.Index;
        var next = stops.FirstOrDefault(stop => stop.Index > nearest.Index);
        var end = next == default ? actions.Count : next.Index;
        var resume = nearest.Index + 1;
        for (var index = resume; index < end; index++)
            if (actions[index]!.Value<string>("Name") == "Boss") resume = index + 1;
        return Math.Min(resume, actions.Count - 1);
    }

    private static Vector3 RoutePosition(JToken? token) => token == null ? Vector3.Zero
        : new(token.Value<float>("X"), token.Value<float>("Y"), token.Value<float>("Z"));

    // Entry needs three different familiars; later battles may use fewer survivors.
    public static bool CanEnterBoard(IEnumerable<uint> team) => team.Where(number => number != 0).Distinct().Take(3).Count() == 3;

    public static bool TeamMatches(IReadOnlyCollection<uint> observed, IReadOnlyCollection<uint> intended) =>
        observed.Count == intended.Count && observed.All(number => number != 0) &&
        observed.Distinct().Count() == observed.Count && observed.ToHashSet().SetEquals(intended);

    public static BeastmasterTeamAddition ObserveAddition(IReadOnlyCollection<uint> before, IReadOnlyCollection<uint> observed, uint number)
    {
        if (TeamMatches(observed, before)) return BeastmasterTeamAddition.Pending;
        return number != 0 && !before.Contains(number) && TeamMatches(observed, before.Append(number).ToArray())
            ? BeastmasterTeamAddition.Confirmed : BeastmasterTeamAddition.Unexpected;
    }

    public static int TeamCapacity(uint territory) => territory switch
    {
        1339 => 10, 1340 => 12, 1341 => 14, 1342 => 12, 1343 => 15, _ => 10,
    };

    public static int TrainingTarget(uint board) => board is >= 1 and <= 5 ? (int)board * 5 : 0;

    public static int TrainingTarget(BeastmasterSettings settings) =>
        settings.TrainingTargetRank > 0 ? settings.TrainingTargetRank : TrainingTarget(settings.Board);

    public static int CompletedBoardRuns(int currentRun, bool resultSeen, int runLimit) =>
        Math.Clamp(Math.Max(0, currentRun - 1) + (currentRun > 0 && resultSeen ? 1 : 0), 0, Math.Max(0, runLimit));

    public static bool ResultRanksComplete(IEnumerable<uint> expected, IEnumerable<(uint Number, int Rank)> observations)
    {
        var rows = observations.ToArray();
        return rows.Length >= 3 && rows.All(row => row.Number != 0 && row.Rank is >= 1 and <= 25) &&
            rows.Select(row => row.Number).Distinct().Count() == rows.Length &&
            expected.All(number => rows.Any(row => row.Number == number));
    }

    public static bool TrainingComplete(IEnumerable<uint> owned, IReadOnlyDictionary<uint, CrucibleFamiliar> observations, int target)
    {
        var numbers = owned.Distinct().ToArray();
        return target > 0 && numbers.Length > 0 && numbers.All(number => observations.TryGetValue(number, out var seen) &&
            seen.Rank is >= 1 and <= 25 && seen.Rank >= target && seen.ObservedAtUtc != DateTime.MinValue && seen.Exp.Length > 0);
    }

    // This only requests confirmation; cached target ranks do not complete a goal.
    // Unknown ownership/ranks need ordinary native registration, not a startup scan.
    public static bool RequiresTrainingCompletionRead(BeastmasterSettings settings, IReadOnlyCollection<uint>? owned,
        IReadOnlyDictionary<uint, CrucibleFamiliar> observations)
    {
        var target = TrainingTarget(settings);
        return settings.Goal == BeastmasterRunGoal.TrainOwned && target > 0 && owned is { Count: > 0 } && owned.All(number =>
            observations.TryGetValue(number, out var beast) && beast.Rank is >= 1 and <= 25 && beast.Rank >= target);
    }

    public static bool TrainingCompletionConfirmed(IEnumerable<uint> owned, IReadOnlyDictionary<uint, CrucibleFamiliar> observations,
        int target, bool readCompleted, IReadOnlyCollection<uint>? freshlyRead) =>
        readCompleted && freshlyRead != null && owned.All(freshlyRead.Contains) && TrainingComplete(owned, observations, target);

    public static bool TryPrepareTeam(BeastmasterSettings settings, IEnumerable<BeastmasterCandidate> owned,
        int capacity, out List<uint> team, out string reason)
    {
        var candidates = owned.Where(beast => beast.Number != 0).DistinctBy(beast => beast.Number).ToList();
        var eligible = candidates.Where(beast => settings.ClassificationLock == 0 ||
            beast.Classification == settings.ClassificationLock).ToList();
        if (settings.Goal == BeastmasterRunGoal.TrainOwned && settings.TeamMode != CrucibleTeamMode.Leveling)
        {
            team = [];
            reason = "Training all owned beasts requires a training team method.";
            return false;
        }

        if (settings.TeamMode == CrucibleTeamMode.Custom)
        {
            var available = eligible.Select(beast => beast.Number).ToHashSet();
            team = settings.CustomTeam.Where(available.Contains).Distinct().Take(capacity).ToList();
            var unavailable = settings.CustomTeam.Where(number => !available.Contains(number)).Distinct().ToArray();
            reason = unavailable.Length > 0
                ? $"Custom selection cannot form an entry team. Unowned or excluded familiars: {string.Join(", ", unavailable)}."
                : "Crucible entry requires at least three different familiars. Adjust the team selection and restrictions.";
        }
        else
        {
            var carry = settings.LevelingMode == CrucibleLevelingMode.Carry_Minus_2;
            team = settings.TeamMode == CrucibleTeamMode.Recommended
                ? Recommended(eligible, capacity)
                : settings.Goal == BeastmasterRunGoal.TrainOwned
                    ? Training(eligible, capacity, carry, TrainingTarget(settings))
                    : Leveling(eligible, capacity, carry);
            var missingRanks = eligible.Where(beast => !beast.PickBlocked && beast.Rank is < 1 or > 25)
                .Select(beast => beast.Number).ToArray();
            reason = missingRanks.Length > 0
                ? $"Not enough eligible familiars have recorded ranks. Missing ranks: {string.Join(", ", missingRanks)}."
                : settings.Goal == BeastmasterRunGoal.TrainOwned
                    ? "Training is incomplete, but fewer than three eligible beasts can form a training team. Check pick exclusions and kin filters."
                    : "Crucible entry requires at least three different familiars. Adjust the team selection and restrictions.";
        }
        if (!CanEnterBoard(team)) return false;
        reason = string.Empty;
        return true;
    }

    private static float ExperienceOrder(BeastmasterCandidate beast) =>
        float.IsNaN(beast.Experience) ? float.PositiveInfinity : beast.Experience;

    public static List<uint> Training(IEnumerable<BeastmasterCandidate> beasts, int cap, bool carry, int target, int classification = 0)
    {
        var candidates = beasts.Where(b => b.Number != 0 && b.Rank is >= 1 and <= 25 && !b.PickBlocked &&
            (classification == 0 || b.Classification == classification)).DistinctBy(b => b.Number).ToList();
        var trainees = candidates.Where(b => b.Rank < target).OrderBy(b => b.Rank)
            .ThenBy(ExperienceOrder).ThenBy(b => b.Number).ToList();
        if (trainees.Count == 0) return [];
        var result = carry ? Recommended(candidates, Math.Min(3, cap), classification) : [];
        result.AddRange(trainees.Where(b => !result.Contains(b.Number)).Take(Math.Max(0, cap - result.Count)).Select(b => b.Number));
        // Trainee-first teams with only one/two remaining trainees need just enough
        // trained, eligible support to satisfy the three-familiar entry minimum.
        if (!carry && result.Count < 3)
            result.AddRange(Recommended(candidates.Where(b => b.Rank >= target && !result.Contains(b.Number)), 3 - result.Count, classification));
        return result;
    }

    public static List<uint> Recommended(IEnumerable<BeastmasterCandidate> beasts, int cap, int classification = 0) =>
        beasts.Where(b => b.Number != 0 && b.Rank is >= 1 and <= 25 && !b.PickBlocked &&
            (classification == 0 || b.Classification == classification)).DistinctBy(b => b.Number)
            .OrderByDescending(b => b.Rank).ThenBy(b => b.Tier).ThenBy(b => b.Number)
            .Take(cap).Select(b => b.Number).ToList();

    public static List<uint> Leveling(IEnumerable<BeastmasterCandidate> beasts, int cap, bool carry, int classification = 0)
    {
        var candidates = beasts.Where(b => b.Number != 0 && b.Rank is >= 1 and <= 25 && !b.PickBlocked &&
            (classification == 0 || b.Classification == classification)).DistinctBy(b => b.Number).ToList();
        var result = carry ? Recommended(candidates, Math.Min(3, cap), classification) : [];
        result.AddRange(candidates.Where(b => !result.Contains(b.Number)).OrderBy(b => b.Rank)
            .ThenBy(ExperienceOrder).ThenBy(b => b.Number).Take(Math.Max(0, cap - result.Count)).Select(b => b.Number));
        return result;
    }

    public static List<uint> FightOrder(IEnumerable<BeastmasterCandidate> beasts, bool leveling)
    {
        var candidates = beasts.Where(b => b.Alive).ToList();
        // Battle-blocked beasts are reserve fighters only when fewer than three others survive.
        if (candidates.Count(b => !b.FightBlocked) >= 3)
            candidates = candidates.Where(b => !b.FightBlocked).ToList();
        return leveling
            ? candidates.OrderBy(b => b.Rank).ThenBy(ExperienceOrder).ThenBy(b => b.Tier).ThenBy(b => b.Number).Select(b => b.Number).ToList()
            : candidates.OrderByDescending(b => b.Rank).ThenBy(b => b.Tier).ThenBy(b => b.Number).Select(b => b.Number).ToList();
    }
}
