using System;
using System.Linq;
using System.Numerics;
using AethertekUI;
using Dalamud.Bindings.ImGui;
using VERMAXION.Beastmaster;
using VERMAXION.Models;

namespace VERMAXION.Windows;

internal sealed partial class BeastmasterWindow
{
    private static readonly string[] ScheduleFrequencies = ["Every AR", "Daily", "Weekly", "Monthly"];
    private bool focusSchedulePanel;

    private void DrawSchedule()
    {
        using (UiText.Font(UiFontRole.BodyStrong)) UiGui.TextUnformatted("VMX Schedule");
        UiGui.TextDisabled("Scheduled task settings for this character. Saved separately from manual Run.");
        UiGui.TextDisabled("Editing settings does not start a run.");
        UiGui.TextDisabled("Schedule edits apply to the next start; the current run keeps its setup.");
        var width = ImGui.GetContentRegionAvail().X;
        var minimum = Math.Max(420 * UIConstants.Scale, MaterialText.Measure(UiText.T("Weekly Challenge Log task")).X + 80 * UIConstants.Scale);
        if (width >= minimum * 2 + ImGui.GetStyle().ItemSpacing.X &&
            ImGui.BeginTable("BeastmasterScheduleColumns", 2, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoSavedSettings))
        {
            ImGui.TableNextColumn(); DrawCrewcibleTask();
            ImGui.TableNextColumn(); DrawWeeklyTask();
            ImGui.EndTable();
        }
        else { DrawCrewcibleTask(); ImGui.Spacing(); DrawWeeklyTask(); }
        UiGui.TextDisabled("Beasts and Supplies are shared for this character.");
        UiGui.TextDisabled("Execution order: VMX Settings > Task Order.");
        focusSchedulePanel = false;
    }

    private void DrawCrewcibleTask()
    {
        if (!UIConstants.BeginPanel("BeastmasterCrewcibleTask", "Crewcible task")) return;
        if (focusSchedulePanel && !focusWeeklyTask) ImGui.SetScrollHereY(0);
        var profile = ViewedConfig!;
        var shared = Settings;
        var setup = CharacterData.ScheduledSetup ??= BeastmasterRunSetup.From(shared);
        var scheduled = setup.ApplyTo(shared);
        var changed = false;
        var enabled = profile.EnableBeastmaster;
        if (UIConstants.Checkbox("Enable task##Crewcible", ref enabled)) { profile.EnableBeastmaster = enabled; changed = true; }
        var frequency = (int)profile.BeastmasterFrequency;
        ImGui.SetNextItemWidth(-1);
        if (UIConstants.Combo("Frequency##Crewcible", ref frequency, ScheduleFrequencies, ScheduleFrequencies.Length))
        {
            profile.BeastmasterFrequency = (BeastmasterTaskFrequency)frequency;
            profile.BeastmasterLastCompleted = profile.BeastmasterNextReset = DateTime.MinValue;
            changed = true;
        }
        UiGui.TextDisabled("Checked at VMX opportunities.");
        if (DrawSetupControls(scheduled, scheduled: true))
        {
            CharacterData.ScheduledSetup = BeastmasterRunSetup.From(scheduled);
            changed = true;
        }
        if (changed) SaveSettings();
        MaterialSettings.Section(UiText.T("Training progress"));
        DrawTrainingProgress(scheduled, "Scheduled");
        UiGui.TextDisabled(UiText.F("Run at most {0} boards per trigger. Unfinished training continues at the next eligible trigger.", scheduled.RunCount));
        DrawScheduleTiming(profile.BeastmasterLastCompleted, profile.BeastmasterNextReset);
        ImGui.BeginDisabled(ViewingCurrent && (plugin.Engine.IsRunning || !plugin.CanStartScheduledBeastmaster(weekly: false, out _)));
        if (UIConstants.Button("Run task now##Crewcible")) RunForViewedProfile(weekly: false, scheduled: true);
        ImGui.EndDisabled();
        DrawScheduleReadiness(weekly: false);
        UIConstants.EndPanel();
    }

    private void DrawWeeklyTask()
    {
        if (!UIConstants.BeginPanel("BeastmasterWeeklyTask", "Weekly Challenge Log task")) return;
        if (focusSchedulePanel && focusWeeklyTask) ImGui.SetScrollHereY(0);
        var profile = ViewedConfig!;
        var changed = false;
        var enabled = profile.EnableBeastmasterWeekly;
        if (UIConstants.Checkbox("Enable task##BeastmasterWeekly", ref enabled)) { profile.EnableBeastmasterWeekly = enabled; changed = true; }
        var frequency = (int)profile.BeastmasterWeeklyFrequency;
        ImGui.SetNextItemWidth(-1);
        if (UIConstants.Combo("Frequency##BeastmasterWeekly", ref frequency, ScheduleFrequencies, ScheduleFrequencies.Length))
        {
            profile.BeastmasterWeeklyFrequency = (BeastmasterTaskFrequency)frequency;
            profile.BeastmasterWeeklyLastCompleted = profile.BeastmasterWeeklyNextReset = DateTime.MinValue;
            changed = true;
        }
        UiGui.TextDisabled(UiText.T("Degree") + ": " + UiText.T("Standard"));
        changed |= DrawWeeklyBoardSelection();
        if (changed) SaveSettings();
        UiGui.TextDisabled("Independent of the ordinary task's enablement, training goal and board-run allowance.");
        MaterialSettings.Section(UiText.T("Weekly progress"));
        if (ViewingCurrent)
        {
            var service = plugin.BeastmasterService;
            var selected = service.SelectedWeeklyObjectives;
            var rows = service.WeeklyObjectives;
            var complete = selected.Count(row => row.State == BeastmasterWeeklyState.Complete);
            ImGui.ProgressBar(selected.Count > 0 ? (float)complete / selected.Count : 0, new Vector2(-1, ImGui.GetFrameHeight()),
                UiText.F("{0}/{1} complete", complete, selected.Count));
            UIConstants.WrappedText(UIConstants.Metadata, UiText.Beastmaster(service.WeeklyStatusText), translate: false);
            MaterialSettings.Section(UiText.T("Weekly challenge objectives"));
            foreach (var objective in rows)
            {
                ImGui.Separator();
                using (UiText.Font(UiFontRole.BodyStrong)) UiGui.TextWrapped(objective.Name);
                UiGui.TextDisabled(objective.BoardName);
                UIConstants.WrappedText(objective.State switch
                {
                    BeastmasterWeeklyState.Complete => VermaxionPresentation.Ready,
                    BeastmasterWeeklyState.Unfinished => UIConstants.Blue,
                    BeastmasterWeeklyState.Locked or BeastmasterWeeklyState.GearBlocked => UIConstants.Amber,
                    _ => UIConstants.Metadata,
                }, UiText.Beastmaster(objective.Reason), translate: false);
            }
        }
        else UiGui.TextDisabled("Weekly Challenge Log status is available for the playing character. Saved scheduling settings remain editable.");
        ImGui.Separator();
        UiGui.TextDisabled("Runs missing selected boards. Locked or gear-blocked entries remain unfinished.");
        DrawScheduleTiming(profile.BeastmasterWeeklyLastCompleted, profile.BeastmasterWeeklyNextReset);
        ImGui.BeginDisabled(ViewingCurrent && (plugin.Engine.IsRunning || !plugin.CanStartScheduledBeastmaster(weekly: true, out _)));
        if (UIConstants.Button("Run weeklies now##BeastmasterWeekly")) RunForViewedProfile(weekly: true, scheduled: true);
        ImGui.EndDisabled();
        DrawScheduleReadiness(weekly: true);
        UIConstants.EndPanel();
    }

    private void DrawScheduleReadiness(bool weekly)
    {
        if (!ViewingCurrent) return;
        var eligibility = plugin.Engine.GetTaskEligibility(weekly ? PostProcessTaskOrder.BeastmasterWeekly : PostProcessTaskOrder.Beastmaster);
        var reason = UiText.Beastmaster(eligibility.Reason);
        UIConstants.SameLineIfFits(reason, MaterialText.Measure(reason).X);
        UIConstants.WrappedText(eligibility.Status switch
        {
            TaskEligibilityStatus.Runnable => VermaxionPresentation.Ready,
            TaskEligibilityStatus.Blocked or TaskEligibilityStatus.Unsupported => UIConstants.Amber,
            _ => UIConstants.Metadata,
        }, reason, translate: false);
        var service = plugin.BeastmasterService;
        if (plugin.BeastmasterOwnsWork && service.HasRunDisplay && service.DisplayWeekly == weekly && service.StatusText != eligibility.Reason)
            UIConstants.WrappedText(service.IsPaused || service.State == BeastmasterService.RunState.Recovering ? UIConstants.Amber : UIConstants.Metadata,
                UiText.Beastmaster(service.StatusText), translate: false);
    }

    private bool DrawWeeklyBoardSelection()
    {
        MaterialSettings.Section(UiText.T("Weekly boards"));
        var settings = Settings;
        var selected = settings.WeeklyBoards ?? (ViewingCurrent
            ? BeastmasterWeeklyPolicy.SelectedObjectives(settings, plugin.BeastmasterService.WeeklyObjectives).Select(row => row.Board).ToHashSet()
            : []);
        var changed = false;
        if (settings.WeeklyBoards == null)
            UiGui.TextDisabled(ViewingCurrent ? "Automatically use unlocked boards."
                : "Unlocked boards are selected when this character is played. Choose boards below to save an explicit selection.");
        for (uint board = 1; board <= RunBoardNames.Length; board++)
        {
            var included = selected.Contains(board);
            if (!UIConstants.Checkbox(RunBoardNames[board - 1] + "##WeeklyBoard", ref included)) continue;
            // Never use the playing character's native unlocks for an offline profile.
            settings.WeeklyBoards ??= [.. selected];
            if (included) settings.WeeklyBoards.Add(board);
            else settings.WeeklyBoards.Remove(board);
            changed = true;
        }
        if (settings.WeeklyBoards is { Count: 0 }) UiGui.TextDisabled("No weekly boards selected.");
        if (settings.WeeklyBoards != null && UIConstants.Button("Use unlocked boards##WeeklyBoards"))
        { settings.WeeklyBoards = null; changed = true; }
        return changed;
    }

    private static void DrawScheduleTiming(DateTime lastCompleted, DateTime nextReset)
    {
        if (lastCompleted != DateTime.MinValue) UiGui.TextDisabled(UiText.F("Last completed: {0}", lastCompleted.ToLocalTime().ToString("g")));
        if (nextReset != DateTime.MinValue) UiGui.TextDisabled(UiText.F("Next eligible: {0}", nextReset.ToLocalTime().ToString("g")));
    }
}
