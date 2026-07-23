using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace VERMAXION.Models;

public enum RetainerGearSourceMode
{
    IgnoreArmory,
    IgnoreGearset,
    AllGear,
}

public enum RetainerMetricKind
{
    CombatItemLevel,
    GatheringPerception,
}

public enum RetainerGearSource
{
    Inventory,
    Armory,
}

public enum RetainerEquipmentSlot
{
    MainHand,
    OffHand,
    Head,
    Body,
    Hands,
    Legs,
    Feet,
    Ears,
    Neck,
    Wrists,
    RingLeft,
    RingRight,
}

public sealed record AutoRetainerRetainerSnapshot(
    ulong RetainerId,
    string Name,
    uint JobId,
    int Level,
    bool HasVenture,
    long VentureEndsAtUnix,
    int ItemLevel,
    int Perception)
{
    public bool IsGathering => JobId is 16 or 17 or 18;
    public bool StatsKnown => IsGathering ? Perception >= 0 : ItemLevel >= 0;
    public bool IsIdle(long nowUnix) =>
        !HasVenture || VentureEndsAtUnix <= 0;
}

public readonly record struct AutoRetainerEquipmentReadResult(
    bool Success,
    IReadOnlyList<AutoRetainerRetainerSnapshot> Retainers,
    string Error)
{
    public static AutoRetainerEquipmentReadResult Known(
        IReadOnlyList<AutoRetainerRetainerSnapshot> retainers) =>
        new(true, retainers, string.Empty);

    public static AutoRetainerEquipmentReadResult Failed(string error) =>
        new(false, Array.Empty<AutoRetainerRetainerSnapshot>(), error);
}

public readonly record struct AutoRetainerCollectOnlyReadResult(
    bool Success,
    bool Enabled,
    string Error)
{
    public static AutoRetainerCollectOnlyReadResult Known(bool enabled) =>
        new(true, enabled, string.Empty);

    public static AutoRetainerCollectOnlyReadResult Failed(string error) =>
        new(false, false, error);
}

public sealed record RetainerEquipmentProfile(
    ulong RetainerId,
    RetainerMetricKind MetricKind,
    int Level,
    int CurrentMetric,
    IReadOnlyDictionary<RetainerEquipmentSlot, int> CurrentSlotValues)
{
    public IReadOnlyDictionary<RetainerEquipmentSlot, int> CombatSlotWeights { get; init; } =
        new Dictionary<RetainerEquipmentSlot, int>();
    public int CombatMetricDivisor { get; init; }
}

public sealed class RetainerGearCandidate
{
    public uint ItemId { get; init; }
    public RetainerGearSource Source { get; init; }
    public int Container { get; init; }
    public int ContainerSlot { get; init; }
    public RetainerEquipmentSlot Slot { get; init; }
    public bool IsRing { get; init; }
    public bool IsUnique { get; init; }
    public bool IsInSavedGearset { get; init; }
    public int RequiredLevel { get; init; }
    public int ItemLevel { get; init; }
    public int Perception { get; init; }
    public IReadOnlySet<ulong> CompatibleRetainerIds { get; init; } = new HashSet<ulong>();
    public string PhysicalKey => $"{Container}:{ContainerSlot}";
}

public sealed record RetainerEquipmentMove(
    ulong RetainerId,
    RetainerEquipmentSlot Slot,
    RetainerGearCandidate Candidate,
    int Improvement);

public sealed record RetainerAllocationResult(
    IReadOnlyList<RetainerEquipmentMove> Moves,
    IReadOnlyDictionary<ulong, int> ProjectedMetrics);

public static class RetainerEquipmentPolicy
{
    public static bool IsSourceEligible(
        RetainerGearCandidate candidate,
        RetainerGearSourceMode sourceMode,
        bool nonUniqueOnly)
    {
        if (nonUniqueOnly && candidate.IsUnique)
            return false;
        if (sourceMode == RetainerGearSourceMode.IgnoreArmory &&
            candidate.Source != RetainerGearSource.Inventory)
        {
            return false;
        }
        if (sourceMode == RetainerGearSourceMode.IgnoreGearset &&
            candidate.IsInSavedGearset)
        {
            return false;
        }
        return true;
    }

    public static bool RequiresWork(
        RetainerEquipmentProfile profile,
        int combatItemLevelTarget,
        int gatheringPerceptionTarget)
    {
        var target = profile.MetricKind == RetainerMetricKind.CombatItemLevel
            ? Math.Max(0, combatItemLevelTarget)
            : Math.Max(0, gatheringPerceptionTarget);
        return target > 0 && (profile.CurrentMetric < 0 || profile.CurrentMetric < target);
    }

    public static RetainerAllocationResult Allocate(
        IReadOnlyList<RetainerEquipmentProfile> retainers,
        IReadOnlyList<RetainerGearCandidate> candidates,
        RetainerGearSourceMode sourceMode,
        bool nonUniqueOnly)
    {
        var targetSlots = retainers
            .SelectMany(profile => profile.CurrentSlotValues.Keys.Select(slot => (profile, slot)))
            .ToList();
        var eligible = candidates
            .Where(candidate => IsSourceEligible(candidate, sourceMode, nonUniqueOnly))
            .GroupBy(candidate => candidate.PhysicalKey, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();

        if (targetSlots.Count == 0 || eligible.Count == 0)
            return new RetainerAllocationResult([], retainers.ToDictionary(x => x.RetainerId, x => x.CurrentMetric));

        // A square Hungarian assignment with one private dummy column per target slot
        // finds the maximum total real improvement while keeping physical items distinct.
        var size = Math.Max(targetSlots.Count, eligible.Count + targetSlots.Count);
        var weights = new int[size, size];
        for (var row = 0; row < targetSlots.Count; row++)
        {
            var (profile, slot) = targetSlots[row];
            var current = profile.CurrentSlotValues.TryGetValue(slot, out var value) ? value : 0;
            for (var column = 0; column < eligible.Count; column++)
            {
                var candidate = eligible[column];
                if (!CanEquip(profile, slot, candidate))
                    continue;
                var rawImprovement = Math.Max(0, MetricValue(profile.MetricKind, candidate) - current);
                var slotWeight = profile.MetricKind == RetainerMetricKind.CombatItemLevel
                    ? profile.CombatSlotWeights.GetValueOrDefault(slot, 1)
                    : 1;
                weights[row, column] = rawImprovement * Math.Max(0, slotWeight);
            }
        }

        var assignment = MaximumWeightAssignment(weights, size);
        var moves = new List<RetainerEquipmentMove>();
        for (var row = 0; row < targetSlots.Count; row++)
        {
            var column = assignment[row];
            if (column < 0 || column >= eligible.Count || weights[row, column] <= 0)
                continue;
            var (profile, slot) = targetSlots[row];
            moves.Add(new RetainerEquipmentMove(profile.RetainerId, slot, eligible[column], weights[row, column]));
        }

        var projected = retainers.ToDictionary(profile => profile.RetainerId, profile => profile.CurrentMetric);
        foreach (var profile in retainers)
        {
            var selected = moves.Where(move => move.RetainerId == profile.RetainerId).ToList();
            if (selected.Count == 0)
                continue;

            if (profile.MetricKind == RetainerMetricKind.GatheringPerception)
            {
                projected[profile.RetainerId] = Math.Max(0, profile.CurrentMetric) +
                    selected.Sum(move => move.Improvement);
            }
            else
            {
                var values = profile.CurrentSlotValues.ToDictionary(pair => pair.Key, pair => pair.Value);
                foreach (var move in selected)
                    values[move.Slot] = move.Candidate.ItemLevel;
                var divisor = profile.CombatMetricDivisor > 0
                    ? profile.CombatMetricDivisor
                    : values.Count;
                projected[profile.RetainerId] = divisor == 0
                    ? 0
                    : values.Sum(pair =>
                        pair.Value * Math.Max(
                            0,
                            profile.CombatSlotWeights.GetValueOrDefault(pair.Key, 1))) / divisor;
            }
        }

        return new RetainerAllocationResult(moves, projected);
    }

    public static string BuildRetrySignature(
        int combatTarget,
        int perceptionTarget,
        RetainerGearSourceMode sourceMode,
        bool nonUniqueOnly,
        IEnumerable<RetainerGearCandidate> candidates)
    {
        var canonical = new StringBuilder()
            .Append(Math.Max(0, combatTarget)).Append('|')
            .Append(Math.Max(0, perceptionTarget)).Append('|')
            .Append((int)sourceMode).Append('|')
            .Append(nonUniqueOnly ? '1' : '0');
        foreach (var candidate in candidates
                     .OrderBy(item => item.PhysicalKey, StringComparer.Ordinal)
                     .ThenBy(item => item.ItemId))
        {
            canonical.Append('|').Append(candidate.PhysicalKey)
                .Append(':').Append(candidate.ItemId)
                .Append(':').Append((int)candidate.Source)
                .Append(':').Append((int)candidate.Slot)
                .Append(':').Append(candidate.IsRing ? 1 : 0)
                .Append(':').Append(candidate.IsUnique ? 1 : 0)
                .Append(':').Append(candidate.IsInSavedGearset ? 1 : 0)
                .Append(':').Append(candidate.RequiredLevel)
                .Append(':').Append(candidate.ItemLevel)
                .Append(':').Append(candidate.Perception);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static bool CanEquip(
        RetainerEquipmentProfile profile,
        RetainerEquipmentSlot targetSlot,
        RetainerGearCandidate candidate)
    {
        if (candidate.RequiredLevel > profile.Level ||
            !candidate.CompatibleRetainerIds.Contains(profile.RetainerId))
        {
            return false;
        }

        return candidate.IsRing
            ? targetSlot is RetainerEquipmentSlot.RingLeft or RetainerEquipmentSlot.RingRight
            : candidate.Slot == targetSlot;
    }

    private static int MetricValue(RetainerMetricKind kind, RetainerGearCandidate candidate)
        => kind == RetainerMetricKind.GatheringPerception
            ? candidate.Perception
            : candidate.ItemLevel;

    private static int[] MaximumWeightAssignment(int[,] weights, int size)
    {
        var maxWeight = 0;
        for (var row = 0; row < size; row++)
        for (var column = 0; column < size; column++)
            maxWeight = Math.Max(maxWeight, weights[row, column]);

        var u = new int[size + 1];
        var v = new int[size + 1];
        var p = new int[size + 1];
        var way = new int[size + 1];
        for (var i = 1; i <= size; i++)
        {
            p[0] = i;
            var j0 = 0;
            var minv = Enumerable.Repeat(int.MaxValue, size + 1).ToArray();
            var used = new bool[size + 1];
            do
            {
                used[j0] = true;
                var i0 = p[j0];
                var delta = int.MaxValue;
                var j1 = 0;
                for (var j = 1; j <= size; j++)
                {
                    if (used[j])
                        continue;
                    var current = maxWeight - weights[i0 - 1, j - 1] - u[i0] - v[j];
                    if (current < minv[j])
                    {
                        minv[j] = current;
                        way[j] = j0;
                    }
                    if (minv[j] < delta)
                    {
                        delta = minv[j];
                        j1 = j;
                    }
                }
                for (var j = 0; j <= size; j++)
                {
                    if (used[j])
                    {
                        u[p[j]] += delta;
                        v[j] -= delta;
                    }
                    else
                    {
                        minv[j] -= delta;
                    }
                }
                j0 = j1;
            } while (p[j0] != 0);

            do
            {
                var j1 = way[j0];
                p[j0] = p[j1];
                j0 = j1;
            } while (j0 != 0);
        }

        var result = Enumerable.Repeat(-1, size).ToArray();
        for (var j = 1; j <= size; j++)
        {
            if (p[j] > 0)
                result[p[j] - 1] = j - 1;
        }
        return result;
    }
}

public sealed class AutoRetainerCollectOnlyLease
{
    public bool IsHeld { get; private set; }
    public bool OriginalCollectOnly { get; private set; }

    public bool Acquire(bool currentCollectOnly)
    {
        if (IsHeld)
            return false;
        OriginalCollectOnly = currentCollectOnly;
        IsHeld = true;
        return true;
    }

    public bool? Release()
    {
        if (!IsHeld)
            return null;
        IsHeld = false;
        return OriginalCollectOnly;
    }
}
