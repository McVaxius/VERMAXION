using Dalamud.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using VERMAXION.Models;

namespace VERMAXION;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    // --- Global UI Settings ---
    public bool Enabled { get; set; } = true;
    public bool IsConfigWindowMovable { get; set; } = true;
    public bool AutoWidthMainTaskColumns { get; set; } = true;
    public bool DtrBarEnabled { get; set; } = true;
    public int DtrBarMode { get; set; } = 0; // 0=text-only, 1=icon+text, 2=icon-only
    public string DtrIconEnabled { get; set; } = "\uE03C";
    public string DtrIconDisabled { get; set; } = "\uE03D";
    public bool KrangleEnabled { get; set; } = false;
    public bool AutoRestoreRetainerCheckingAfterWork { get; set; } = true;
    public bool EnableCharacterSelectStallRecovery { get; set; } = true;
    public List<string> FavoriteAutomationIds { get; set; } = new();
    public int RefillListingsActionDelayMs { get; set; } = 250;
    public int RefillListingsInterItemDelayMs { get; set; } = 250;
    public float LeftPanelWidth { get; set; } = 240f;
    public CharacterListSortMode CharacterListSortMode { get; set; } = CharacterListSortMode.Name;
    public List<string> PostProcessTaskOrder { get; set; } = VERMAXION.PostProcessTaskOrder.DefaultOrder.ToList();
    public Dictionary<string, PostProcessTaskPhase> PostProcessTaskPlacement { get; set; } = VERMAXION.PostProcessTaskOrder.CreateDefaultPlacement();

    // --- Fishing ---
    public FishingExecutionMode FishingExecutionMode { get; set; } = FishingDefaults.ExecutionMode;
    public int FishingMaxFisherLevel { get; set; } = FishingDefaults.MaxFisherLevel;
    public int OceanFishingPreWindowOffsetMinutes { get; set; } = FishingDefaults.OceanFishingPreWindowOffsetMinutes;
    public OceanFishingProvider OceanFishingProvider { get; set; } =
        Models.OceanFishingProvider.VermaxionAutoHook;
    public OceanFishingRoutePreference OceanFishingRoutePreference { get; set; } =
        Models.OceanFishingRoutePreference.Indigo;
    public bool OceanFishingWindowWatchEnabled { get; set; } = false;

    /// <summary>
    /// Persisted overrides for OceanFishingContinuousRailPolicy's spot-selection knobs, applied at plugin
    /// load (ApplyConfiguration). They exist so spacing can be tuned across cooperating clients WITHOUT
    /// runtime reflection tooling and have the values survive restarts. Rationale for the shipped
    /// defaults: a full 24-player vessel packs the ~32.5y legal rail to ~1.41y max pitch, so any fallback
    /// floor >=1.5y is unsatisfiable exactly when the boat is fullest — the sampler fails closed and the
    /// character stands idle for the voyage. Raising preferred clearance makes idling WORSE (fewer valid
    /// spots), not better.
    /// </summary>
    public float OceanRailMinimumPlayerClearance { get; set; } = 1.9f;
    public float OceanRailFallbackPlayerClearance { get; set; } = 1.25f;
    public float OceanRailStepYalms { get; set; } = 0.5f;

    /// <summary>Angular speed of the CanFish-sampling facing-sweep fallback, in degrees per second. Default
    /// 180 matches a player holding the turn key (full circle in ~2s); clamped to 30-720. The sweep still
    /// snaps in 15-degree increments — this only sets how fast it steps through them.</summary>
    public float OceanRailFacingSweepDegreesPerSecond { get; set; } = 180f;

    /// <summary>Ocean rail positioning mode: 2 (default) = the compiled-in fixed-spot list — characters
    /// position only at the built-in walked deck spots, each client deterministically owning one spot by
    /// index and otherwise taking the nearest listed spot that clears other players by the AoE
    /// (<see cref="OceanRailEdgePlayerAoeYalms"/>); when no listed spot is usable the continuous rail
    /// sampler takes over automatically. 0 = continuous sweep-sampling only (legacy). Mode 1 (edge spread)
    /// was retired; saved configs with 1 behave as 0.</summary>
    public int OceanRailSpreadMode { get; set; } = 2;

    // The mode-2 fishing spots are COMPILED IN (OceanFishingDiscreteSpotPolicy.BuiltInSpots): the vessel's
    // deck geometry is identical for every player, so the walked coordinates are code, not configuration —
    // no user authors them and no serialization can drift them. A legacy OceanRailDiscreteSpots property
    // that once lived here is ignored harmlessly if present in old saved configs.

    /// <summary>Player-centred AoE radius for discrete-spot resolution, in yalms (clamped 0.5-5): the
    /// pairwise spacing every settled fisher keeps from every other player. (Property name retained from
    /// the retired edge-spread mode so saved configs keep their value.)</summary>
    public float OceanRailEdgePlayerAoeYalms { get; set; } = 2.0f;

    /// <summary>Idle inn-parking: during downtime (outside a fishing window with margin, AR idle, nothing
    /// due soon) send the logged-in character to an inn; once confirmed inside, enable it in AutoRetainer,
    /// and pre-emptively exit before its next venture comes due so AR wakes it beside a bell.</summary>
    public bool OceanIdleInnParkEnabled { get; set; } = false;
    public int OceanIdleInnParkMinMinutesToWindow { get; set; } = 5;
    public int OceanIdleInnParkExitLeadMinutes { get; set; } = 5;
    public bool OceanIdleInnParkEnableAutoRetainer { get; set; } = true;

    /// <summary>
    /// Cooperative rail slicing: with SliceCount N > 1 and a unique SliceIndex per client, each
    /// client samples only the central 50% of its private window of the linear rail, so cooperating
    /// clients stay >=~1.48y apart BY CONSTRUCTION even when they sample simultaneously (concurrent
    /// clients otherwise race the clearance check from inside the spawn stack and collide
    /// birthday-style). SliceCount 0/1 = whole rail (stock behavior).
    /// </summary>
    public int OceanRailSliceIndex { get; set; } = 0;
    public int OceanRailSliceCount { get; set; } = 1;

    /// <summary>
    /// Periodically disable AutoRetainer's movement-stuck detector (BailoutManager.RunStuckDetection, AR
    /// 4.6.1.17) by holding its NoMoveCheck throttle. That detector both benches stationary fishing chars
    /// (Enabled=false, pulled from retainers/subs) and NPE-storms at char-select (soft-deadlock). See
    /// AutoRetainerIPC.TrySuppressStuckDetection. Defaults ON; off once AR ships guards for both paths.
    /// </summary>
    public bool SuppressAutoRetainerStuckDetection { get; set; } = true;

    /// <summary>
    /// Once this many minutes into a registration window with no fishing run started, "fake-ready" a
    /// character so AutoRetainer logs one in (see AutoRetainerIPC.TryFakeReadyEnabledCharacter). Guarantees
    /// a boarding attempt even when AR's natural rotation wouldn't surface a ready character during the
    /// window. Defaults on at +5min (registration stays open ~14min, leaving time to log in, travel, and
    /// register).
    /// </summary>
    public bool OceanFishingFakeReadyEnabled { get; set; } = true;
    public int OceanFishingFakeReadyOffsetMinutes { get; set; } = 5;

    public bool FishingCharacterSettingsMigrated { get; set; } = false;
    public bool FishingStockCatalogMigrated { get; set; } = false;

    // Replace, not the Newtonsoft default Populate: Dalamud loads this config with Newtonsoft, whose
    // Populate APPENDS saved rows after the initializer's compile-time rows, and NormalizeCatalog's
    // keep-first dedupe then deleted the SAVED rows — every saved catalog edit silently reverted to
    // compile-time defaults on plugin load. The initializer still seeds fresh configs;
    // rows added to CreateDefaultCatalog later reach existing configs via the load-time append migration.
    [Newtonsoft.Json.JsonProperty(ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Replace)]
    public List<FishingStockCatalogEntry> FishingStockCatalog { get; set; } =
        FishingStockCatalogPolicy.CreateDefaultCatalog();

    // --- Free Company action stock ---
    public Dictionary<ulong, FcActionStockEntry> FcActionStockByFreeCompanyId { get; set; } = new();

    // --- Setup wizard ---
    public bool SetupWizardStateMigrated { get; set; } = false;
    public bool SetupWizardCompleted { get; set; } = false;

    // --- Account Tracking ---
    public string LastAccountId { get; set; } = "";

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? LegacyFields { get; set; }

    public bool TryGetLegacyFishingOperationSettings(out FishingOperationSettings settings)
    {
        settings = default;
        if (LegacyFields == null)
            return false;

        var hasLegacyFishingSettings =
            LegacyFields.ContainsKey("FishingLureRestockTarget") ||
            LegacyFields.ContainsKey("FishingReturnDestination") ||
            LegacyFields.ContainsKey("FishingReturnCommand") ||
            LegacyFields.ContainsKey("FishingRepairMode") ||
            LegacyFields.ContainsKey("FishingRepairThresholdPercent");

        if (!hasLegacyFishingSettings)
            return false;

        settings = new FishingOperationSettings(
            TryGetLegacyInt("FishingLureRestockTarget", out var lureTarget)
                ? Math.Max(0, lureTarget)
                : FishingDefaults.LureRestockTarget,
            TryGetLegacyEnum("FishingReturnDestination", out FishingReturnDestination returnDestination)
                ? returnDestination
                : FishingDefaults.ReturnDestination,
            TryGetLegacyString("FishingReturnCommand", out var returnCommand)
                ? returnCommand
                : FishingDefaults.ReturnCommand,
            TryGetLegacyEnum("FishingRepairMode", out FishingRepairMode repairMode)
                ? repairMode
                : FishingDefaults.RepairMode,
            TryGetLegacyInt("FishingRepairThresholdPercent", out var repairThreshold)
                ? Math.Clamp(repairThreshold, 0, 100)
                : FishingDefaults.RepairThresholdPercent);

        return true;
    }

    public void ClearLegacyFishingOperationSettings()
    {
        if (LegacyFields == null)
            return;

        LegacyFields.Remove("FishingLureRestockTarget");
        LegacyFields.Remove("FishingReturnDestination");
        LegacyFields.Remove("FishingReturnCommand");
        LegacyFields.Remove("FishingRepairMode");
        LegacyFields.Remove("FishingRepairThresholdPercent");
    }

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }

    private bool TryGetLegacyInt(string key, out int value)
    {
        value = 0;
        return LegacyFields != null &&
               LegacyFields.TryGetValue(key, out var element) &&
               element.ValueKind == JsonValueKind.Number &&
               element.TryGetInt32(out value);
    }

    private bool TryGetLegacyString(string key, out string value)
    {
        value = string.Empty;
        if (LegacyFields == null ||
            !LegacyFields.TryGetValue(key, out var element) ||
            element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString() ?? string.Empty;
        return true;
    }

    private bool TryGetLegacyEnum<TEnum>(string key, out TEnum value)
        where TEnum : struct, Enum
    {
        value = default;
        if (LegacyFields == null || !LegacyFields.TryGetValue(key, out var element))
            return false;

        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var numericValue))
        {
            if (Enum.IsDefined(typeof(TEnum), numericValue))
            {
                value = (TEnum)Enum.ToObject(typeof(TEnum), numericValue);
                return true;
            }

            return false;
        }

        return element.ValueKind == JsonValueKind.String &&
               Enum.TryParse(element.GetString(), ignoreCase: true, out value);
    }
}
