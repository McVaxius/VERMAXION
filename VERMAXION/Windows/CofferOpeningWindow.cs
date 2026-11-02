using System;
using System.Linq;
using System.Collections.Generic;
using AethertekUI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using VERMAXION.Services;

namespace VERMAXION.Windows;

internal sealed class CofferOpeningWindow(Plugin plugin) : Window("Open coffers###VermaxionCoffers")
{
    private readonly AethertekUI.Dalamud.MaterialWindowMotion motion = new();
    private readonly AethertekUI.Dalamud.MaterialWindowLock movementLock = new();
    private bool backdropStyle;
    private uint selected;
    private string lastError = "";

    public override void PreDraw()
    {
        SizeConstraints = new() { MinimumSize = new(420, 300), MaximumSize = new(900, 700) };
        UIConstants.PushStyle(plugin.Configuration.CompactUi);
        if (VermaxionPresentation.HasMaterial || plugin.BackgroundBlurEnabled)
        { ImGui.PushStyleColor(ImGuiCol.WindowBg, System.Numerics.Vector4.Zero); backdropStyle = true; }
        motion.Prepare(this, reducedMotion: !plugin.Configuration.UiStylingEnabled, roundedCorners: plugin.Configuration.UiStylingEnabled);
        movementLock.Apply(this, plugin.Configuration.UiWindowsLocked);
    }
    public override void PostDraw()
    {
        movementLock.Restore(this); motion.Restore(this);
        RestoreBackdrop(); UIConstants.PopStyle();
    }
    private void RestoreBackdrop() { if (backdropStyle) { ImGui.PopStyleColor(); backdropStyle = false; } }

    public override void Draw()
    {
        motion.DrawChrome(); RestoreBackdrop(); plugin.DrawWindowBackdrop(this);
        using var backgrounds = MaterialSkin.StructuralBackgroundStyle();
        DrawContent(plugin.CofferOpening.Status, plugin.CofferInventory.Candidates(), ref selected,
            plugin.CofferOpeningBlocker(), lastError, Start, Stop);
    }

    private void Start(uint itemId) => _ = Plugin.Framework.RunOnFrameworkThread(() =>
    {
        var reply = plugin.StartCoffers(itemId);
        lastError = reply.Accepted ? "" : reply.Reason;
    });
    private void Stop() => _ = Plugin.Framework.RunOnFrameworkThread(plugin.CofferOpening.Stop);

    internal static void DrawContent(CofferOpeningStatus status, IReadOnlyList<NativeCofferInventory.Stack> candidates,
        ref uint selected, string? blocker, string error, Action<uint> start, Action stop)
    {
        var selection = selected;
        if (MaterialTheme.Current.IsLcars) MaterialLcars.Header(UiText.T("Open coffers"));
        UIConstants.Heading("Open coffers", MaterialTheme.Current.Density == MaterialDensity.Compact);
        UiGui.TextWrapped("Opens one selected stack; keeps at least one bag slot free.");
        if (!candidates.Any(stack => stack.ItemId == selection)) selection = candidates.FirstOrDefault()?.ItemId ?? 0;
        var current = candidates.FirstOrDefault(stack => stack.ItemId == selection);
        ImGui.BeginDisabled(status.IsActive);
        try
        {
            ImGui.SetNextItemWidth(-1);
            if (ImGui.BeginCombo("###CofferItem", current == null ? UiText.T("No coffers in inventory.") : $"{current.Name} × {current.Quantity}"))
            {
                try
                {
                    foreach (var stack in candidates)
                        if (UiGui.Selectable("###Coffer/" + stack.ItemId, selection == stack.ItemId, $"{stack.Name} × {stack.Quantity}") && !status.IsActive)
                            selection = stack.ItemId;
                }
                finally { ImGui.EndCombo(); }
            }
        }
        finally { ImGui.EndDisabled(); }
        UiGui.TextWrapped(status.Summary);
        UiGui.TextWrapped(UiText.F("Coffers opened: {0}/{1}", status.Opened, status.Requested));
        if (blocker != null) UiGui.TextWrapped(blocker);
        if (!string.IsNullOrEmpty(error)) UiGui.TextWrapped(error);
        ImGui.BeginDisabled(status.IsActive || current == null || blocker != null);
        try
        {
            if (UiGui.Button("Open stack###CofferStart") && !status.IsActive && current != null && blocker == null)
                start(selection);
        }
        finally { ImGui.EndDisabled(); }
        ImGui.BeginDisabled(!status.IsActive);
        try
        {
            if (UiGui.Button("Stop###CofferStop") && status.IsActive)
                stop();
        }
        finally { ImGui.EndDisabled(); }
        selected = selection;
    }
}
