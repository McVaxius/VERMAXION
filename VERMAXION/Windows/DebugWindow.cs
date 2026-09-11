using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace VERMAXION.Windows;

internal sealed class DebugWindow : Window
{
    private readonly Plugin plugin;

    public DebugWindow(Plugin plugin) : base("Vermaxion Debug##Debug")
    {
        this.plugin = plugin;
        Size = new Vector2(480, 540);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(360, 260),
            MaximumSize = new Vector2(800, 1000),
        };
    }

    public override void Draw()
    {
        ImGui.TextWrapped("Select one task for the next plugin reload. After character registration, FULL STOP runs, then the task's manual action is attempted once.");
        ImGui.TextWrapped("Uncheck to cancel. FULL STOP cancels a pending attempt for this reload while keeping the saved selection.");
        ImGui.Separator();
        ImGui.TextWrapped(plugin.DebugTaskStatus);
        ImGui.Separator();

        var rows = plugin.MainWindow.GetDashboardTaskRows();
        if (!string.IsNullOrWhiteSpace(plugin.Configuration.DebugTaskId) &&
            rows.All(row => row.Id != plugin.Configuration.DebugTaskId))
        {
            ImGui.TextWrapped("The saved task is no longer available.");
            if (ImGui.SmallButton("Clear selection"))
                plugin.SetDebugTaskSelection(null);
        }

        foreach (var row in rows)
        {
            var selected = plugin.Configuration.DebugTaskId == row.Id;
            // A stale selected stub may still be unchecked, but cannot be armed.
            ImGui.BeginDisabled(row.IsConfigurationOnly && !selected);
            if (ImGui.Checkbox($"{row.Task}##Debug_{row.Id}", ref selected))
                plugin.SetDebugTaskSelection(selected ? row.Id : null);
            ImGui.EndDisabled();
            if (row.DebugBlockedReason is { } reason &&
                ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(reason);
        }
    }
}
