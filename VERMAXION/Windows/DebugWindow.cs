using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using VERMAXION.Models;

namespace VERMAXION.Windows;

internal sealed class DebugWindow : Window
{
    private readonly AethertekUI.Dalamud.MaterialWindowMotion motion = new();
    private bool materialBackdropStylePushed;
    private readonly AethertekUI.Dalamud.MaterialWindowLock movementLock = new();
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

    public override void PreDraw()
    {
        UIConstants.PushStyle(plugin.Configuration.CompactUi);
        if (VermaxionPresentation.HasMaterial || plugin.BackgroundBlurEnabled)
        {
            ImGui.PushStyleColor(ImGuiCol.WindowBg, Vector4.Zero);
            materialBackdropStylePushed = true;
        }
        motion.Prepare(this, reducedMotion: !plugin.Configuration.UiStylingEnabled, roundedCorners: plugin.Configuration.UiStylingEnabled);
        movementLock.Apply(this, plugin.Configuration.UiWindowsLocked);
    }

    public override void PostDraw()
    {
        movementLock.Restore(this);
        motion.Restore(this);
        ReleaseMaterialBackdropStyle();
        UIConstants.PopStyle();
    }

    private void BeginMaterialContent()
    {
        ReleaseMaterialBackdropStyle();
        plugin.DrawWindowBackdrop(this);
    }

    private void ReleaseMaterialBackdropStyle()
    {
        if (!materialBackdropStylePushed) return;
        ImGui.PopStyleColor();
        materialBackdropStylePushed = false;
    }

    public override void Draw()
    {
        motion.DrawChrome();
        BeginMaterialContent();
        using var structuralBackgrounds = AethertekUI.MaterialSkin.StructuralBackgroundStyle();
        if (AethertekUI.MaterialTheme.Current.IsLcars) AethertekUI.MaterialLcars.Header(UiText.T(WindowName.Split("##", 2)[0]));
        UiGui.TitleWithButtons("Vermaxion Debug", UiText.T("Vermaxion Debug"), this);
        UIConstants.Heading("Reload task selection", plugin.Configuration.CompactUi);
        if (UIConstants.FullStopButton())
            plugin.FullStop();
        UiGui.TextWrapped(UiText.F($"Saved task: {plugin.Configuration.DebugTaskId ?? UiText.T("none")}"));
        UIConstants.Status("DebugStatus", plugin.DebugTaskStatus, UIConstants.Metadata);
        if (plugin.IsChocoboReloadTestSelected)
        {
            var now = DateTime.UtcNow;
            var allowance = ChocoboDailyAllowance.Remaining(plugin.ConfigManager.GetActiveConfig(), now);
            UiGui.TextWrapped(UiText.F("Lazyparasite racing budget: {0} of {1} remaining",
                TimeSpan.FromSeconds(allowance).ToString(@"hh\:mm\:ss", UiText.Current.Culture),
                TimeSpan.FromSeconds(ChocoboDailyAllowance.LimitSeconds).ToString(@"hh\:mm\:ss", UiText.Current.Culture)));
            UiGui.TextDisabled(UiText.F("Resets {0} local (09:00 UTC). Covering waits do not consume racing time.",
                ChocoboDailyAllowance.ResetAt(now).AddDays(1).ToLocalTime().ToString("f", UiText.Current.Culture)));
        }
        UIConstants.SetNextItemWidth(-1f);
        UiGui.InputTextWithHint("##DebugTaskSearch", "Search task name or identifier", ref taskSearch, 100);
        ImGui.Separator();

        ImGui.BeginChild("##DebugBody", Vector2.Zero, true);
        if (UIConstants.CollapsingHeading("How reload selection works"))
        {
            UiGui.TextWrapped("Select one task for the next plugin reload. After character registration, FULL STOP runs, then the task's manual action is attempted once.");
            UiGui.TextWrapped("Uncheck to cancel. FULL STOP cancels a pending attempt for this reload while keeping the saved selection.");
        }
        if (UIConstants.CollapsingHeading("Loaded build"))
        {
            UiGui.TextWrapped(UiText.F($"Loaded build: {plugin.DebugBuildMarker}"));
            UiGui.TextWrapped(UiText.F($"Loaded DLL: {typeof(Plugin).Assembly.Location}"));
        }
        var rows = plugin.MainWindow.GetDashboardTaskRows();
        var selectedTaskId = AutomationCatalog.ResolveDebugTaskId(plugin.Configuration.DebugTaskId);
        if (!string.IsNullOrWhiteSpace(plugin.Configuration.DebugTaskId) &&
            rows.All(row => row.Id != selectedTaskId))
        {
            UiGui.TextWrapped("The saved task is no longer available.");
            if (UIConstants.Button("Clear selection"))
                plugin.SetDebugTaskSelection(null);
        }

        var visibleRows = rows.Where(row => string.IsNullOrWhiteSpace(taskSearch) ||
            row.Task.Contains(taskSearch, StringComparison.OrdinalIgnoreCase) ||
            UiText.T(row.Task).Contains(taskSearch, StringComparison.OrdinalIgnoreCase) ||
            row.Id.Contains(taskSearch, StringComparison.OrdinalIgnoreCase) ||
            row.ButtonLabel.Contains(taskSearch, StringComparison.OrdinalIgnoreCase)).ToList();
        if (visibleRows.Count == 0)
            UiGui.TextDisabled("No tasks match this search.");

        foreach (var row in visibleRows)
        {
            var selected = selectedTaskId == row.Id;
            // A stale selected stub may still be unchecked, but cannot be armed.
            ImGui.BeginDisabled(row.IsConfigurationOnly && !selected);
            if (UIConstants.Checkbox($"##Debug_{row.Id}", ref selected))
                plugin.SetDebugTaskSelection(selected ? row.Id : null);
            ImGui.SameLine();
            UiGui.TextWrapped(row.Task);
            if (plugin.GetDebugTaskBlockedReason(selected ? plugin.Configuration.DebugTaskId : row.Id, row.DebugBlockedReason) is { } reason &&
                ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                UiGui.SetTooltip(reason);
            ImGui.EndDisabled();
            if (selected && row.Id == AutomationCatalog.RegisterRegistrables)
                UiGui.TextWrapped(row.Status);
        }
        ImGui.EndChild();
    }
}
