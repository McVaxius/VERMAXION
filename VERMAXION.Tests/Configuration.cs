using System.Collections.Generic;
using System.Linq;

namespace VERMAXION;

public sealed class Configuration
{
    public bool AutoWidthMainTaskColumns { get; set; } = true;
    public List<string> FavoriteAutomationIds { get; set; } = new();
    public int RefillListingsActionDelayMs { get; set; } = 250;
    public int RefillListingsInterItemDelayMs { get; set; } = 250;
    public List<string> PostProcessTaskOrder { get; set; } = VERMAXION.PostProcessTaskOrder.DefaultOrder.ToList();
    public Dictionary<string, PostProcessTaskPhase> PostProcessTaskPlacement { get; set; } = VERMAXION.PostProcessTaskOrder.CreateDefaultPlacement();

    // Rail-positioning members read by FishingModels' ApplyConfiguration paths. Kept in sync with the
    // real Configuration; the test default mirrors production (discrete spots, empty list is fine for
    // the policies under test).
    public int OceanRailSpreadMode { get; set; } = 2;
    public float OceanRailMinimumPlayerClearance { get; set; } = 1.9f;
    public float OceanRailFallbackPlayerClearance { get; set; } = 1.25f;
    public float OceanRailStepYalms { get; set; } = 0.5f;
    public float OceanRailFacingSweepDegreesPerSecond { get; set; } = 180f;
    public float OceanRailEdgePlayerAoeYalms { get; set; } = 2.0f;
    public int OceanRailSliceIndex { get; set; } = 0;
    public int OceanRailSliceCount { get; set; } = 1;
}
