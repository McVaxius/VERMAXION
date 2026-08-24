using System;
using System.Collections.Generic;
using System.Linq;

namespace VERMAXION.Models;

public enum SetupWizardKind
{
    DefaultAndSync,
    FcBuff,
    Fishing,
    RetainerEquipping,
}

public sealed record SetupWizardFieldChange(string Key, string Label, string Before, string After);

public static class SetupWizardPolicy
{
    public static IReadOnlyList<SetupWizardFieldChange> GetImpact(
        SetupWizardKind kind,
        CharacterConfig current,
        CharacterConfig draft)
    {
        var changes = new List<SetupWizardFieldChange>();

        void Add<T>(string key, string label, T before, T after)
        {
            if (EqualityComparer<T>.Default.Equals(before, after))
                return;
            changes.Add(new SetupWizardFieldChange(key, label, Format(before), Format(after)));
        }

        switch (kind)
        {
            case SetupWizardKind.DefaultAndSync:
                Add(nameof(CharacterConfig.Enabled), "Automation enabled", current.Enabled, draft.Enabled);
                break;
            case SetupWizardKind.FcBuff:
                Add(nameof(CharacterConfig.EnableFCBuffRefill), "FC Buff enabled", current.EnableFCBuffRefill, draft.EnableFCBuffRefill);
                Add(nameof(CharacterConfig.FCBuffPurchaseAttempts), "Purchase quantity", current.FCBuffPurchaseAttempts, draft.FCBuffPurchaseAttempts);
                Add(nameof(CharacterConfig.FCBuffMinPoints), "Minimum FC points", current.FCBuffMinPoints, draft.FCBuffMinPoints);
                Add(nameof(CharacterConfig.FCBuffMinGil), "Minimum gil", current.FCBuffMinGil, draft.FCBuffMinGil);
                Add(nameof(CharacterConfig.FCBuffFrequency), "Cadence", current.FCBuffFrequency, draft.FCBuffFrequency);
                break;
            case SetupWizardKind.Fishing:
                Add(nameof(CharacterConfig.EnableFishing), "Fishing enabled", current.EnableFishing, draft.EnableFishing);
                foreach (var itemId in current.FishingStockItems.Keys
                             .Union(draft.FishingStockItems.Keys)
                             .OrderBy(id => id))
                {
                    current.FishingStockItems.TryGetValue(itemId, out var before);
                    draft.FishingStockItems.TryGetValue(itemId, out var after);
                    Add(
                        $"{nameof(CharacterConfig.FishingStockItems)}[{itemId}]",
                        $"Fishing stock {itemId}",
                        FormatStock(before),
                        FormatStock(after));
                }
                break;
            case SetupWizardKind.RetainerEquipping:
                Add(nameof(CharacterConfig.EnableRetainerEquipping), "Retainer Equipping enabled", current.EnableRetainerEquipping, draft.EnableRetainerEquipping);
                Add(nameof(CharacterConfig.RetainerGearSourceMode), "Gear source", current.RetainerGearSourceMode, draft.RetainerGearSourceMode);
                Add(nameof(CharacterConfig.RetainerGearNonUniqueOnly), "Non-unique only", current.RetainerGearNonUniqueOnly, draft.RetainerGearNonUniqueOnly);
                Add(nameof(CharacterConfig.RetainerCombatItemLevelTarget), "Combat item-level target", current.RetainerCombatItemLevelTarget, draft.RetainerCombatItemLevelTarget);
                Add(nameof(CharacterConfig.RetainerGatheringPerceptionTarget), "Gathering Perception target", current.RetainerGatheringPerceptionTarget, draft.RetainerGatheringPerceptionTarget);
                break;
        }

        return changes;
    }

    public static void Apply(SetupWizardKind kind, CharacterConfig source, CharacterConfig target)
    {
        switch (kind)
        {
            case SetupWizardKind.DefaultAndSync:
                target.Enabled = source.Enabled;
                break;
            case SetupWizardKind.FcBuff:
                target.EnableFCBuffRefill = source.EnableFCBuffRefill;
                target.FCBuffPurchaseAttempts = source.FCBuffPurchaseAttempts;
                target.FCBuffMinPoints = source.FCBuffMinPoints;
                target.FCBuffMinGil = source.FCBuffMinGil;
                target.FCBuffFrequency = source.FCBuffFrequency;
                break;
            case SetupWizardKind.Fishing:
                target.EnableFishing = source.EnableFishing;
                target.FishingStockItems = source.FishingStockItems.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.Clone());
                break;
            case SetupWizardKind.RetainerEquipping:
                target.EnableRetainerEquipping = source.EnableRetainerEquipping;
                target.RetainerGearSourceMode = source.RetainerGearSourceMode;
                target.RetainerGearNonUniqueOnly = source.RetainerGearNonUniqueOnly;
                target.RetainerCombatItemLevelTarget = source.RetainerCombatItemLevelTarget;
                target.RetainerGatheringPerceptionTarget = source.RetainerGatheringPerceptionTarget;
                break;
        }
    }

    private static string Format<T>(T value) => value?.ToString() ?? "(none)";

    private static string FormatStock(FishingStockSetting? stock)
        => stock == null
            ? "Not configured"
            : $"Enabled={stock.Enabled}, Target={stock.Target}, Min={stock.Min}";
}

public readonly record struct SetupWizardMigrationDecision(
    bool Completed,
    bool Migrated,
    bool ShouldAutoOpen);

public static class SetupWizardMigrationPolicy
{
    public static SetupWizardMigrationDecision Decide(
        bool hasStoredConfiguration,
        bool stateMigrated,
        bool completed)
    {
        if (stateMigrated)
            return new SetupWizardMigrationDecision(completed, true, false);

        return new SetupWizardMigrationDecision(
            Completed: hasStoredConfiguration,
            Migrated: true,
            ShouldAutoOpen: !hasStoredConfiguration);
    }
}
