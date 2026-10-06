using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace VERMAXION.IPC;

internal sealed record AutoRetainerFishProtectionResult(bool Success, int Plans = 0,
    int Discard = 0, int UnconditionalSell = 0, int QuickVentureSell = 0,
    int StackFlags = 0, string Error = "");

internal static class AutoRetainerFishProtectionReflection
{
    private static readonly string[] RequiredLists = ["IMDiscardList", "IMAutoVendorHard", "IMAutoVendorSoft"];
    private static readonly string[] StackFlagLists = ["IMAutoVendorHardIgnoreStack", "IMDiscardIgnoreStack"];

    internal static AutoRetainerFishProtectionResult Remove(object config, IReadOnlySet<uint> fishIds, Action save)
    {
        var result = new AutoRetainerFishProtectionResult(false);
        try
        {
            if (Read(config, "DefaultIMSettings") is not { } defaultPlan ||
                Read(config, "AdditionalIMSettings") is not IList additionalPlans)
                throw new InvalidOperationException("AutoRetainer cleanup plans were not readable.");

            var plans = new[] { defaultPlan }.Concat(additionalPlans.Cast<object>()).ToArray();
            var lists = new List<(int Kind, ICollection<uint> Items)>();
            // Validate every plan before changing any of them. Never edit obsolete top-level lists.
            foreach (var plan in plans)
            {
                if (plan == null) throw new InvalidOperationException("AutoRetainer cleanup plan was null.");
                for (var kind = 0; kind < RequiredLists.Length; kind++)
                    lists.Add((kind, ReadList(plan, RequiredLists[kind])));
                foreach (var name in StackFlagLists)
                    if (Member(plan, name) is { } member)
                        lists.Add((3, ReadList(plan, name, member)));
            }

            var counts = new int[4];
            foreach (var (kind, items) in lists)
                foreach (var fishId in items.Where(fishIds.Contains).ToArray())
                {
                    if (!items.Remove(fishId))
                        throw new InvalidOperationException("AutoRetainer fish-list removal did not retain its result.");
                    counts[kind]++;
                }
            result = new(false, plans.Length, counts[0], counts[1], counts[2], counts[3]);
            // Persist even an already-clear plan: a prior save may have failed after its in-memory removal.
            save();
            if (lists.Any(list => list.Items.Any(fishIds.Contains)))
                throw new InvalidOperationException("AutoRetainer fish lists did not remain clear after saving.");
            return result with { Success = true };
        }
        catch (Exception ex)
        {
            // A failed save remains visible; do not reinsert sale/discard entries into memory.
            return result with { Error = ex.GetBaseException().Message };
        }
    }

    private static ICollection<uint> ReadList(object plan, string name, MemberInfo? member = null)
    {
        if (Read(plan, name, member) is not ICollection<uint> list || list.IsReadOnly)
            throw new InvalidOperationException($"AutoRetainer {name} was not a mutable item-ID list.");
        return list;
    }

    private static MemberInfo? Member(object owner, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        return (MemberInfo?)owner.GetType().GetField(name, flags) ?? owner.GetType().GetProperty(name, flags);
    }

    private static object? Read(object owner, string name, MemberInfo? member = null)
        => (member ?? Member(owner, name)) switch
        {
            FieldInfo field => field.GetValue(owner),
            PropertyInfo property when property.GetIndexParameters().Length == 0 => property.GetValue(owner),
            _ => null,
        };
}
