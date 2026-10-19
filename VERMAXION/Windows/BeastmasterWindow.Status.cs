using System;
using System.Collections.Generic;
using System.Linq;
using AethertekUI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using VERMAXION.Beastmaster;

namespace VERMAXION.Windows;

internal sealed partial class BeastmasterWindow
{
    private string PrimaryRunAction => plugin.BeastmasterService.IsPaused ? "Resume"
        : plugin.BeastmasterService.IsActive ? "Continue" : "Start";

    internal void AddRunTitlebarActions(Window owner)
    {
        owner.TitleBarButtons.Add(new()
        {
            Icon = FontAwesomeIcon.Bolt, Priority = 0, IconOffset = new(2, 1),
            Click = button =>
            {
                if (button == ImGuiMouseButton.Left && Plugin.ClientState.IsLoggedIn)
                    RunForViewedProfile(weekly: false, scheduled: false);
            },
            ShowTooltip = () =>
            {
                var ready = plugin.CanStartBeastmaster(out var reason);
                MaterialText.SetTooltip(UiText.T(PrimaryRunAction) + "\n" + plugin.KrangleTitlebarText(
                    ready ? BuildRunDetail(ready, reason) : UiText.Beastmaster(reason)));
            },
        });
        owner.TitleBarButtons.Add(new()
        {
            Icon = FontAwesomeIcon.Clock, Priority = -5, IconOffset = new(2, 1),
            Click = button => { if (button == ImGuiMouseButton.Left) plugin.BeastmasterService.ToggleStopAfterNextOutcome(); },
            ShowTooltip = () => MaterialText.SetTooltip(UiText.T(plugin.BeastmasterService.StopAfterNextOutcome
                ? "Cancel stop after next outcome" : "Stop after current board") + "\n" +
                UiText.T("One-time action: continue after deaths; stop after a clear or removal from the duty.")),
        });
        owner.TitleBarButtons.Add(new()
        {
            Icon = FontAwesomeIcon.Stop, Priority = -10, IconOffset = new(2, 1),
            Click = button => { if (button == ImGuiMouseButton.Left) plugin.StopBeastmaster(); },
            ShowTooltip = () =>
            {
                var ready = plugin.CanStartBeastmaster(out var reason);
                MaterialText.SetTooltip(UiText.T("Stop") + "\n" + plugin.KrangleTitlebarText(BuildRunDetail(ready, reason)));
            },
        });
    }

    private string RunPhase(bool ready)
    {
        var service = plugin.BeastmasterService;
        return UiText.T(service.HasRunDisplay || service.IsActive || service.State != BeastmasterService.RunState.Idle
            ? service.DisplayPhase : ready ? "Ready" : "Unavailable");
    }

    private string RunHeadline(bool ready, bool includeBoard)
    {
        var service = plugin.BeastmasterService;
        var phase = RunPhase(ready);
        if (!service.HasRunDisplay) return phase;
        var terminal = service.State is BeastmasterService.RunState.Idle or BeastmasterService.RunState.Complete or BeastmasterService.RunState.Failed;
        if (terminal)
            return phase + " · " + UiText.F("{0}/{1} runs completed", service.DisplayCompletedRuns, service.DisplayRunLimit);
        var run = UiText.F("Run {0}/{1}", Math.Clamp(service.CurrentRun, 1, service.DisplayRunLimit), service.DisplayRunLimit);
        return run + (includeBoard ? " · " + RunBoardName(service.DisplayBoard) : string.Empty) + " · " + phase;
    }

    private string RunGoalSummary()
    {
        var service = plugin.BeastmasterService;
        if (!service.HasRunDisplay) return string.Empty;
        if (service.DisplayWeekly)
        {
            var rows = service.SelectedWeeklyObjectives;
            return rows.Count == 0 ? UiText.Beastmaster(service.WeeklyStatusText)
                : rows.All(row => row.State != BeastmasterWeeklyState.Unavailable)
                    ? UiText.F("{0}/{1} complete", rows.Count(row => row.State == BeastmasterWeeklyState.Complete), rows.Count)
                    : UiText.T("Weekly Challenge Log data is unavailable.");
        }
        if (service.DisplayGoal != BeastmasterRunGoal.TrainOwned) return string.Empty;
        var data = service.CharacterData;
        var owned = service.PreparedOwned ?? data.Owned;
        if (owned.Count == 0 || data.OwnershipObservedAtUtc == DateTime.MinValue)
            return UiText.T("Ownership has not been fully observed. Training completion is unknown.");
        var target = TrainingTarget(service.DisplayBoard);
        var reached = owned.Count(number => data.Familiars.TryGetValue(number, out var beast) && beast.Rank is >= 1 and <= 25 && beast.Rank >= target);
        return UiText.F("{0}/{1} at Rank {2}", reached, owned.Count, target) + " · " + UiText.F("{0} remaining", owned.Count - reached);
    }

    private string BuildRunDetail(bool ready, string blocker, bool includeGoal = true)
    {
        var service = plugin.BeastmasterService;
        var status = service.HasRunDisplay || service.IsActive || service.State is BeastmasterService.RunState.Complete or BeastmasterService.RunState.Failed
            ? UiText.Beastmaster(service.StatusText) : ready ? UiText.T("Ready to start") : UiText.Beastmaster(blocker);
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(status)) lines.Add(status);
        if (service.StopAfterNextOutcome) lines.Add(UiText.T("Stopping when the current board ends."));
        var headline = RunHeadline(ready, includeBoard: true);
        if (!lines.Contains(headline)) lines.Add(headline);
        if (service.HasRunDisplay && service.State is not (BeastmasterService.RunState.Idle or BeastmasterService.RunState.Complete or BeastmasterService.RunState.Failed))
            lines.Add(UiText.F("{0}/{1} runs completed", service.DisplayCompletedRuns, service.DisplayRunLimit));
        if (service.DisplayPhase == "Checking new beasts" && service.ScanTotal > 0)
            lines.Add(UiText.F("{0}/{1} read", service.ScanReadCount, service.ScanTotal));
        if (includeGoal)
        {
            var goal = RunGoalSummary();
            if (goal.Length > 0) lines.Add(goal);
        }
        if (includeGoal && service.RouteStepCount > 0)
            lines.Add(UiText.F("Route {0:00} · {1}/{2} steps", service.DisplayBoard, service.RouteStep, service.RouteStepCount));
        return string.Join("\n", lines);
    }

    internal unsafe MaterialTitlebarStatus? RunTitlebarStatus(Window owner, ref string title)
    {
        if (!plugin.Configuration.UiShowTitlebarStatus) return null;
        var ready = plugin.CanStartBeastmaster(out var blocker);
        var concise = RunHeadline(ready, includeBoard: false);
        var detail = BuildRunDetail(ready, blocker);
        var service = plugin.BeastmasterService;
        var terminal = service.State is BeastmasterService.RunState.Idle or BeastmasterService.RunState.Complete or BeastmasterService.RunState.Failed;
        var shortText = service.HasRunDisplay
            ? $"{(terminal ? service.DisplayCompletedRuns : Math.Clamp(service.CurrentRun, 1, service.DisplayRunLimit))}/{service.DisplayRunLimit}"
            : RunPhase(ready);
        var fullTitle = title;
        var window = ImGuiP.FindWindowByName(owner.WindowName);
        if (window.Handle != null)
        {
            var style = ImGui.GetStyle();
            var fontSize = window.InnerRect.Min.Y - window.Pos.Y - 2 * style.FramePadding.Y;
            if (fontSize > 0)
            {
                using var font = UiText.Font(UiFontRole.Body);
                var collapse = (window.Flags & (ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.Modal)) == 0 &&
                    style.WindowMenuButtonPosition != ImGuiDir.None;
                var actions = owner.TitleBarButtons.Count(button => !owner.IsClickthrough || button.AvailableClickthrough);
                if (owner.AllowPinning || owner.AllowClickthrough || owner.AllowBackgroundBlur) actions++;
                var available = window.Size.X - 2 * (window.WindowBorderSize + style.FramePadding.X)
                    - ((owner.ShowCloseButton ? 1 : 0) + (collapse ? 1 : 0)) * (fontSize + style.ItemInnerSpacing.X)
                    - actions * (ImGuiP.CalcFontSize(window) + style.ItemInnerSpacing.X)
                    - 1.26f * fontSize - 4 * style.ItemInnerSpacing.X - Measure(shortText);
                // Preserve the numeric counter before shortening the visible caption;
                // the full title remains in the tooltip and the native ID is unchanged.
                if (Measure(title) > available) title = UiText.T("Crewcible");
                if (Measure(title) > available) title = "VMX";

                float Measure(string text) => MaterialText.Measure(text).X * (fontSize / ImGui.GetFontSize());
            }
        }
        return new(plugin.KrangleTitlebarText(plugin.Configuration.UiDetailedTitlebarStatus ? detail.Replace("\n", " · ") : concise),
            plugin.KrangleTitlebarText(shortText), fullTitle + "\n" + plugin.KrangleTitlebarText(detail));
    }
}
