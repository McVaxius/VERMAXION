using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace VERMAXION.Windows;

internal sealed class DebugWindow : Window
{
    private readonly Plugin plugin;
    private string taskSearch = string.Empty;

    public DebugWindow(Plugin plugin) : base("Vermaxion Debug##Debug", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.plugin = plugin;
        Size = new Vector2(480, 540);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(400, 360),
            MaximumSize = new Vector2(800, 1000),
        };
    }

    public override void PreDraw() => UIConstants.PushStyle(plugin.Configuration.CompactUi);

    public override void PostDraw() => UIConstants.PopStyle();

    public override void Draw()
    {
        UIConstants.Heading("Reload task selection", plugin.Configuration.CompactUi);
        if (UIConstants.FullStopButton())
            plugin.FullStop();
        ImGui.TextWrapped($"Saved task: {plugin.Configuration.DebugTaskId ?? "none"}");
        UIConstants.Status("DebugStatus", plugin.DebugTaskStatus, UIConstants.Metadata);
        UIConstants.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##DebugTaskSearch", "Search task name or identifier", ref taskSearch, 100);
        ImGui.Separator();

        ImGui.BeginChild("##DebugBody", Vector2.Zero, true);
        if (UIConstants.CollapsingHeading("How reload selection works"))
        {
            ImGui.TextWrapped("Select one task for the next plugin reload. After character registration, FULL STOP runs, then the task's manual action is attempted once.");
            ImGui.TextWrapped("Uncheck to cancel. FULL STOP cancels a pending attempt for this reload while keeping the saved selection.");
        }
        if (UIConstants.CollapsingHeading("Loaded build"))
        {
            ImGui.TextWrapped($"Loaded build: {plugin.DebugBuildMarker}");
            ImGui.TextWrapped($"Loaded DLL: {typeof(Plugin).Assembly.Location}");
        }
        var rows = plugin.MainWindow.GetDashboardTaskRows();
        if (!string.IsNullOrWhiteSpace(plugin.Configuration.DebugTaskId) &&
            rows.All(row => row.Id != plugin.Configuration.DebugTaskId))
        {
            ImGui.TextWrapped("The saved task is no longer available.");
            if (UIConstants.Button("Clear selection"))
                plugin.SetDebugTaskSelection(null);
        }

        var visibleRows = rows.Where(row => string.IsNullOrWhiteSpace(taskSearch) ||
            row.Task.Contains(taskSearch, StringComparison.OrdinalIgnoreCase) ||
            row.Id.Contains(taskSearch, StringComparison.OrdinalIgnoreCase)).ToList();
        if (visibleRows.Count == 0)
            ImGui.TextDisabled("No tasks match this search.");

        foreach (var row in visibleRows)
        {
            var selected = plugin.Configuration.DebugTaskId == row.Id;
            // A stale selected stub may still be unchecked, but cannot be armed.
            ImGui.BeginDisabled(row.IsConfigurationOnly && !selected);
            if (UIConstants.Checkbox($"##Debug_{row.Id}", ref selected))
                plugin.SetDebugTaskSelection(selected ? row.Id : null);
            ImGui.SameLine();
            ImGui.TextWrapped(row.Task);
            if (row.DebugBlockedReason is { } reason &&
                ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(reason);
            ImGui.EndDisabled();
            if (selected && row.Id == AutomationCatalog.RegisterRegistrables)
                ImGui.TextWrapped(row.Status);
        }
        ImGui.EndChild();
    }
}
