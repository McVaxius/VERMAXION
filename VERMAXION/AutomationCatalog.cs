using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using VERMAXION.Models;

namespace VERMAXION;

public enum AutomationOwner
{
    EngineTask,
    RunHook,
    PreemptiveCoordinator,
    ConfigOnlyWip,
    ChildOption,
}

public enum AutomationCadence
{
    EveryRun,
    Daily,
    Weekly,
    Scheduled,
    CoordinatorWindow,
    ConfigOnly,
}

public enum AutomationMaturity
{
    Stable,
    Wip,
}

public sealed record AutomationFeatureDefinition(
    string Id,
    string FlagProperty,
    string Label,
    AutomationCadence Cadence,
    AutomationMaturity Maturity,
    PostProcessTaskPhase DefaultPhase,
    AutomationOwner Owner)
{
    public string CadenceLabel => Cadence switch
    {
        AutomationCadence.EveryRun => "Every applicable run",
        AutomationCadence.Daily => "Daily",
        AutomationCadence.Weekly => "Weekly",
        AutomationCadence.Scheduled => "Scheduled",
        AutomationCadence.CoordinatorWindow => "Coordinator window",
        AutomationCadence.ConfigOnly => "Config only",
        _ => Cadence.ToString(),
    };

    public string OwnershipLabel => Owner switch
    {
        AutomationOwner.EngineTask => "Ordered engine task",
        AutomationOwner.RunHook => "Run-start hook",
        AutomationOwner.PreemptiveCoordinator => "Preemptive coordinator",
        AutomationOwner.ConfigOnlyWip => "Config-only WIP",
        AutomationOwner.ChildOption => "Child route option",
        _ => Owner.ToString(),
    };
}

public sealed record AutomationRegistryValidation(bool IsValid, IReadOnlyList<string> Errors)
{
    public string Message => IsValid
        ? "Automation registry ready."
        : $"Configured but not dispatchable: {string.Join(" ", Errors)}";
}

public static class AutomationCatalog
{
    public const string VerminionQueue = "verminion_queue";
    public const string JumboCactpot = "jumbo_cactpot";
    public const string MiniCactpot = "mini_cactpot";
    public const string ChocoboRacing = "chocobo_racing";
    public const string FCBuffRefill = "fc_buff_refill";
    public const string MinionRoulette = "minion_roulette";
    public const string SeasonalGear = "seasonal_gear";
    public const string GearUpdater = "gear_updater";
    public const string HighestCombatJob = "highest_combat_job";
    public const string CurrentJobEquipment = "current_job_equipment";
    public const string FashionReport = "fashion_report";
    public const string RegisterRegistrables = "register_registrables";
    public const string VendorStock = "vendor_stock";
    public const string RefillListings = "refill_listings";
    public const string NagYourMom = "nag_your_mom";
    public const string NagYourMomCasualCc = "nag_your_mom_casual_cc";
    public const string NagYourMomFrontline = "nag_your_mom_frontline";
    public const string NagYourMomRivalWings = "nag_your_mom_rival_wings";
    public const string NagYourDad = "nag_your_dad";
    public const string EvercoldAdventurerActivity = "evercold_adventurer_activity";
    public const string MiscCommands = "misc_commands";
    public const string LootGoblinMapGather = "lootgoblin_map_gather";
    public const string Fishing = "fishing";

    public static readonly IReadOnlyList<AutomationFeatureDefinition> Features =
    [
        Engine(VerminionQueue, nameof(CharacterConfig.EnableVerminionQueue), "Verminion Queue", AutomationCadence.Weekly),
        Engine(JumboCactpot, nameof(CharacterConfig.EnableJumboCactpot), "Jumbo Cactpot", AutomationCadence.Weekly),
        Engine(MiniCactpot, nameof(CharacterConfig.EnableMiniCactpot), "Mini Cactpot", AutomationCadence.Daily),
        Engine(ChocoboRacing, nameof(CharacterConfig.EnableChocoboRacing), "Chocobo Racing", AutomationCadence.Daily),
        Engine(FCBuffRefill, nameof(CharacterConfig.EnableFCBuffRefill), "FC Buff Refill", AutomationCadence.EveryRun),
        Engine(MinionRoulette, nameof(CharacterConfig.EnableMinionRoulette), "Minion Roulette", AutomationCadence.EveryRun),
        Engine(SeasonalGear, nameof(CharacterConfig.EnableSeasonalGearRoulette), "Seasonal Gear", AutomationCadence.EveryRun),
        Engine(GearUpdater, nameof(CharacterConfig.EnableGearUpdater), "Gear Updater", AutomationCadence.EveryRun),
        Engine(HighestCombatJob, nameof(CharacterConfig.EnableHighestCombatJob), "Highest Combat Job", AutomationCadence.EveryRun),
        Engine(CurrentJobEquipment, nameof(CharacterConfig.EnableCurrentJobEquipment), "Current Job Equipment", AutomationCadence.EveryRun),
        Engine(FashionReport, nameof(CharacterConfig.EnableFashionReport), "Fashion Report", AutomationCadence.Weekly),
        Engine(RegisterRegistrables, nameof(CharacterConfig.EnableRegisterRegistrables), "Register Registrables", AutomationCadence.EveryRun),
        Engine(VendorStock, nameof(CharacterConfig.EnableVendorStock), "Vendor Stock", AutomationCadence.EveryRun),
        Engine(RefillListings, nameof(CharacterConfig.EnableRefillFromListings), "Refill Listings", AutomationCadence.Scheduled, PostProcessTaskPhase.BeforeAR),
        Engine(NagYourMom, nameof(CharacterConfig.EnableNagYourMom), "nag your mom", AutomationCadence.Daily),
        new(NagYourMomCasualCc, nameof(CharacterConfig.EnableNagYourMomCasualCc), "Casual CC route", AutomationCadence.Daily, AutomationMaturity.Stable, PostProcessTaskPhase.AfterAR, AutomationOwner.ChildOption),
        new(NagYourMomFrontline, nameof(CharacterConfig.EnableNagYourMomFrontline), "Frontline route", AutomationCadence.Daily, AutomationMaturity.Stable, PostProcessTaskPhase.AfterAR, AutomationOwner.ChildOption),
        new(NagYourMomRivalWings, nameof(CharacterConfig.EnableNagYourMomRivalWings), "Rival Wings route", AutomationCadence.Daily, AutomationMaturity.Stable, PostProcessTaskPhase.AfterAR, AutomationOwner.ChildOption),
        Engine(NagYourDad, nameof(CharacterConfig.EnableNagYourDad), "nag your dad", AutomationCadence.Scheduled),
        new(EvercoldAdventurerActivity, nameof(CharacterConfig.EnableEvercoldAdventurerActivity), "Adventurer Activity (Evercold)", AutomationCadence.ConfigOnly, AutomationMaturity.Wip, PostProcessTaskPhase.AfterAR, AutomationOwner.ConfigOnlyWip),
        new(MiscCommands, nameof(CharacterConfig.EnableMiscCmd), "Misc Commands", AutomationCadence.EveryRun, AutomationMaturity.Stable, PostProcessTaskPhase.AfterAR, AutomationOwner.RunHook),
        Engine(LootGoblinMapGather, nameof(CharacterConfig.EnableLootGoblinMapGather), "LootGoblin Map Gather", AutomationCadence.Daily),
        new(Fishing, nameof(CharacterConfig.EnableFishing), "Fishing", AutomationCadence.CoordinatorWindow, AutomationMaturity.Stable, PostProcessTaskPhase.BeforeAR, AutomationOwner.PreemptiveCoordinator),
    ];

    public static readonly IReadOnlyList<AutomationFeatureDefinition> EngineTasks =
        Features.Where(feature => feature.Owner == AutomationOwner.EngineTask).ToList();

    public static readonly IReadOnlyDictionary<string, AutomationFeatureDefinition> ById =
        Features.ToDictionary(feature => feature.Id, StringComparer.Ordinal);

    public static AutomationFeatureDefinition Get(string id)
        => ById.TryGetValue(id, out var definition)
            ? definition
            : throw new KeyNotFoundException($"Unknown automation ID '{id}'.");

    public static AutomationRegistryValidation ValidateRuntimeRegistry(
        IEnumerable<string> runtimeBindingIds,
        IEnumerable<string> orderedIds)
    {
        var errors = new List<string>();
        var enableProperties = typeof(CharacterConfig)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.PropertyType == typeof(bool) &&
                               IsFeatureEnableProperty(property.Name))
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        var catalogFlags = Features.Select(feature => feature.FlagProperty).ToList();

        AddDuplicateErrors(catalogFlags, "catalog flag", errors);
        AddDuplicateErrors(Features.Select(feature => feature.Id), "catalog ID", errors);

        var missingFlags = enableProperties.Except(catalogFlags, StringComparer.Ordinal).ToList();
        var extraFlags = catalogFlags.Except(enableProperties, StringComparer.Ordinal).ToList();
        if (missingFlags.Count > 0)
            errors.Add($"Missing catalog flags: {string.Join(", ", missingFlags)}.");
        if (extraFlags.Count > 0)
            errors.Add($"Catalog flags without CharacterConfig properties: {string.Join(", ", extraFlags)}.");

        var engineIds = EngineTasks.Select(feature => feature.Id).ToList();
        ValidateExactIds("runtime bindings", engineIds, runtimeBindingIds, errors);
        ValidateExactIds("ordered task IDs", engineIds, orderedIds, errors);

        return new AutomationRegistryValidation(errors.Count == 0, errors);
    }

    private static AutomationFeatureDefinition Engine(
        string id,
        string flagProperty,
        string label,
        AutomationCadence cadence,
        PostProcessTaskPhase phase = PostProcessTaskPhase.AfterAR)
        => new(id, flagProperty, label, cadence, AutomationMaturity.Stable, phase, AutomationOwner.EngineTask);

    private static void ValidateExactIds(
        string source,
        IReadOnlyCollection<string> expected,
        IEnumerable<string> actualValues,
        List<string> errors)
    {
        var actual = actualValues.ToList();
        AddDuplicateErrors(actual, source, errors);
        var missing = expected.Except(actual, StringComparer.Ordinal).ToList();
        var extra = actual.Except(expected, StringComparer.Ordinal).ToList();
        if (missing.Count > 0)
            errors.Add($"{source} missing: {string.Join(", ", missing)}.");
        if (extra.Count > 0)
            errors.Add($"{source} extra: {string.Join(", ", extra)}.");
    }

    private static void AddDuplicateErrors(IEnumerable<string> values, string source, List<string> errors)
    {
        var duplicates = values
            .GroupBy(value => value, StringComparer.Ordinal)
            .Where(group => group.Count() != 1)
            .Select(group => group.Key)
            .ToList();
        if (duplicates.Count > 0)
            errors.Add($"Duplicate {source} values: {string.Join(", ", duplicates)}.");
    }

    public static bool IsFeatureEnableProperty(string propertyName)
        => propertyName.Length > "Enable".Length &&
           propertyName.StartsWith("Enable", StringComparison.Ordinal) &&
           char.IsUpper(propertyName["Enable".Length]);
}
