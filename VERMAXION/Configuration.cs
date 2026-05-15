using Dalamud.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;

namespace VERMAXION;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    // --- Global UI Settings ---
    public bool IsConfigWindowMovable { get; set; } = true;
    public bool DtrBarEnabled { get; set; } = true;
    public int DtrBarMode { get; set; } = 0; // 0=text-only, 1=icon+text, 2=icon-only
    public string DtrIconEnabled { get; set; } = "\uE03C";
    public string DtrIconDisabled { get; set; } = "\uE03D";
    public bool KrangleEnabled { get; set; } = false;
    public float LeftPanelWidth { get; set; } = 240f;
    public List<string> PostProcessTaskOrder { get; set; } = VERMAXION.PostProcessTaskOrder.DefaultOrder.ToList();
    public Dictionary<string, PostProcessTaskPhase> PostProcessTaskPlacement { get; set; } = VERMAXION.PostProcessTaskOrder.CreateDefaultPlacement();

    // --- Account Tracking ---
    public string LastAccountId { get; set; } = "";

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
