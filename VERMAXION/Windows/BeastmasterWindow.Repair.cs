using Dalamud.Bindings.ImGui;
using VERMAXION.Beastmaster;

namespace VERMAXION.Windows;

internal sealed partial class BeastmasterWindow
{
    private static readonly string[] RepairModeNames =
    [
        "Disabled", "Self repair", "NPC repair without inn", "Nearby NPC only", "NPC repair with inn",
    ];

    private void DrawRepair()
    {
        if (!UIConstants.BeginPanel("BeastmasterRepair", "Repair")) return;
        UiGui.TextWrapped("Shared by this character's manual, scheduled and weekly runs.");
        UiGui.TextDisabled("Repair edits apply to the next Start; the current run keeps its setup.");
        var settings = Settings;
        using (UIConstants.PushSettingsRows())
        {
            var mode = (int)settings.RepairMode;
            ImGui.SetNextItemWidth(-1);
            if (UIConstants.Combo("Repair mode", ref mode, RepairModeNames, RepairModeNames.Length))
            {
                settings.RepairMode = (BeastmasterRepairMode)mode;
                SaveSettings();
            }
            var threshold = settings.RepairThreshold;
            ImGui.SetNextItemWidth(130 * UIConstants.Scale);
            if (UIConstants.InputInt("Repair below durability (%)", ref threshold, 1, 10))
            {
                settings.RepairThreshold = threshold;
                SaveSettings();
            }
        }
        UiGui.TextWrapped("Checks the lowest durability of equipped repairable gear before entry and between boards, outside the duty. Set 0 to disable automatic requests.");
        ImGui.Separator();
        UiGui.TextWrapped(settings.RepairMode switch
        {
            BeastmasterRepairMode.Disabled => "Repairs remain manual.",
            BeastmasterRepairMode.Self => "Self repair requires the appropriate crafting levels and repair materials. It never switches to an NPC automatically.",
            BeastmasterRepairMode.NpcNoInn => "ADS may travel or teleport to an NPC mender without entering an inn.",
            BeastmasterRepairMode.NpcNearby => "Uses a nearby NPC mender only. No teleporting or inn travel.",
            BeastmasterRepairMode.NpcInn => "ADS may travel or teleport to an NPC mender and use an inn.",
            _ => "Choose a repair mode.",
        });
        UiGui.TextDisabled("Automatic repair uses ADS. Crewcible resumes entry after the repair attempt.");
        UIConstants.EndPanel();
    }
}
