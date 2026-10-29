using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Newtonsoft.Json.Linq;

namespace VERMAXION.Beastmaster;

public readonly record struct BeastmasterCandidate(uint Number, int Rank, float Experience, int Tier,
    byte Classification = 0, bool PickBlocked = false, bool FightBlocked = false, bool Alive = true);

public enum BeastmasterTeamAddition { Pending, Confirmed, Unexpected }
public readonly record struct BeastmasterFightContext(uint BattleId, bool RandomOutcome);

public static class BeastmasterSelection
{
    public static bool RequiresClearTeam(BeastmasterSettings settings) =>
        settings.Degree is CrucibleDegree.First or CrucibleDegree.Second or CrucibleDegree.Third;

    // The loaded edition owns the board setup. Never substitute a farming or user roster.
    public static List<uint> ClearRoster(uint board)
    {
        return BeastmasterCombat.TryReadSetup(board, 0, out var setup, out _) ? setup.Roster.ToList() : [];
    }

    public static bool TryPrepareClearTeam(BeastmasterSettings settings, IEnumerable<BeastmasterCandidate> owned,
        out List<uint> team, out string reason, IReadOnlyList<uint>? resolvedRoster = null)
    {
        team = [];
        if (settings.Board is < 1 or > 5)
        { reason = "Select one of the five Crucible boards"; return false; }
        if (settings.Goal == BeastmasterRunGoal.TrainOwned)
        { reason = "Degrees 1–3 require a reliable-clear team. Use Standard for training."; return false; }
        if (resolvedRoster == null)
        {
            if (!BeastmasterCombat.TryReadSetup(settings.Board, 0, out var setup, out reason)) return false;
            resolvedRoster = setup.Roster;
        }
        var required = resolvedRoster.ToList();
        if (required.Count != TeamCapacity(1338u + settings.Board) || required.Any(number => number == 0) ||
            required.Distinct().Count() != required.Count)
        { reason = "The native clear roster does not match the board capacity."; return false; }
        var candidates = owned.DistinctBy(beast => beast.Number).ToDictionary(beast => beast.Number);
        var rank = TrainingTarget(settings.Board);
        var missing = required.Where(number => !candidates.TryGetValue(number, out var beast) ||
            beast.Rank < rank || beast.Rank > 25).ToArray();
        if (missing.Length > 0)
        {
            reason = $"Reliable-clear setup requires these familiar numbers at rank {rank} or higher: {string.Join(", ", missing)}.";
            return false;
        }
        team = required;
        reason = string.Empty;
        return true;
    }

    public static List<uint> ClearFightOrder(BeastmasterSettings settings, IEnumerable<BeastmasterCandidate> beasts,
        IReadOnlyList<uint>? resolvedRoster = null)
    {
        var required = resolvedRoster ?? ClearRoster(settings.Board);
        var rank = TrainingTarget(settings.Board);
        var available = beasts.Where(beast => beast.Alive && beast.Rank >= rank && beast.Rank <= 25)
            .DistinctBy(beast => beast.Number).ToDictionary(beast => beast.Number);
        // Preserve registration priority until a verified encounter plan chooses its trio.
        // Neither Gesh absorption nor one healer ID establishes sustain readiness.
        return required.Where(available.ContainsKey).ToList();
    }

    public static bool ClearFightReady(IEnumerable<uint> picked)
    {
        var trio = picked.Take(3).ToArray();
        return trio.Length == 3 && trio.All(number => number != 0) && trio.Distinct().Count() == 3;
    }

    // Board healing runs first when stock is available. A percentage cutoff
    // cannot recover HP by itself and otherwise strands a living clear team.
    public static bool PlayerReadyForFight(uint hp, uint maxHp)
        => maxHp > 0 && hp > 0 && hp <= maxHp;

    public static BeastmasterFightContext? ResolveFightBattle(uint fixedBattleId,
        IReadOnlyList<uint>? randomBattleIds, bool fightPanelReady)
    {
        if (!fightPanelReady) return null;
        if (fixedBattleId > 0 && randomBattleIds == null) return new(fixedBattleId, false);
        if (fixedBattleId != 0 || randomBattleIds == null || randomBattleIds.Count == 0 ||
            randomBattleIds.Any(id => id == 0)) return null;
        var distinct = randomBattleIds.Distinct().Take(2).ToArray();
        return distinct.Length == 1 ? new(distinct[0], true) : null;
    }

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
        int capacity, out List<uint> team, out string reason, IReadOnlyList<uint>? resolvedRoster = null)
    {
        if (RequiresClearTeam(settings)) return TryPrepareClearTeam(settings, owned, out team, out reason, resolvedRoster);
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

    public static bool TryRecoverExcludedTrainees(BeastmasterSettings runSettings, IEnumerable<BeastmasterCandidate> owned,
        int capacity, out List<uint> team)
    {
        team = [];
        if (RequiresClearTeam(runSettings)) return false;
        var candidates = owned.Where(beast => beast.Number != 0).DistinctBy(beast => beast.Number).ToArray();
        if (runSettings.Goal != BeastmasterRunGoal.TrainOwned || runSettings.TeamMode != CrucibleTeamMode.Leveling ||
            TryPrepareTeam(runSettings, candidates, capacity, out _, out _)) return false;

        var list = runSettings.BeastTierLists.ElementAtOrDefault(runSettings.BeastTierListIndex);
        if (list == null) return false;
        var target = TrainingTarget(runSettings);
        var excluded = candidates.Where(beast => beast.PickBlocked && beast.Rank is >= 1 and <= 25 && beast.Rank < target &&
            (runSettings.ClassificationLock == 0 || beast.Classification == runSettings.ClassificationLock) &&
            list.Any(entry => entry.ID == beast.Number && entry.PickBlock)).Select(beast => beast.Number).ToHashSet();
        if (excluded.Count == 0 || !TryPrepareTeam(runSettings,
            candidates.Select(beast => excluded.Contains(beast.Number) ? beast with { PickBlocked = false } : beast),
            capacity, out var rebuilt, out _)) return false;

        // The caller supplies the active run's deep clone. Commit only a usable
        // recovery, retaining saved exclusions, list order and reserve flags.
        foreach (var entry in list.Where(entry => excluded.Contains(entry.ID))) entry.PickBlock = false;
        team = rebuilt;
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
