using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AethertekUI;
using Dalamud.Bindings.ImGui;
using FFXIVClientStructs.FFXIV.Client.Game;
using VERMAXION.Beastmaster;

namespace VERMAXION.Windows;

internal sealed partial class BeastmasterWindow
{
    private static readonly string[] RunBoardNames =
    [
        "First Board of the Unbroken", "Second Board of the Unbroken", "Third Board of the Unbroken",
        "First Master's Board", "Second Master's Board",
    ];
    private static readonly string[] RunDegreeNames = ["Standard", "First Degree", "Second Degree", "Third Degree"];
    private static readonly string[] TeamMethodNames = ["Strongest available", "Train a full team", "Smaller training team", "Train three at a time", "Three strong fighters + trainees", "Custom"];
    private static string RunBoardName(uint board) => board is >= 1 and <= 5 ? UiText.T(RunBoardNames[board - 1]) : UiText.T("Unknown board");
    private static int TrainingTarget(uint board) => BeastmasterSelection.TrainingTarget(board);
    private static int BoardCapacity(BeastmasterSettings settings) => BeastmasterSelection.TeamCapacity(BeastmasterService.TerritoryForBoard(settings.Board));
    private static int TeamMethodIndex(BeastmasterSettings settings) => settings.TeamMode switch
    {
        CrucibleTeamMode.Recommended => 0, CrucibleTeamMode.Custom => 5, _ => Math.Clamp((int)settings.LevelingMode, 0, 3) + 1,
    };
    private static string TeamMethodCaption(BeastmasterSettings settings) => UiText.T(TeamMethodNames[TeamMethodIndex(settings)]);
    private static int TeamMethodCapacity(BeastmasterSettings settings) => settings.TeamMode switch
    {
        CrucibleTeamMode.Recommended => Math.Min(BoardCapacity(settings), settings.RecommendedTeamCap),
        CrucibleTeamMode.Leveling => settings.LevelingMode switch
        {
            CrucibleLevelingMode.Minus_3 => BoardCapacity(settings) - 3,
            CrucibleLevelingMode.Only_3 => 3,
            CrucibleLevelingMode.Carry_Minus_2 => BoardCapacity(settings) - 2,
            _ => BoardCapacity(settings),
        },
        _ => BoardCapacity(settings),
    };

    private void DrawRun()
    {
        var minimum = Math.Max(410 * UIConstants.Scale, MaterialText.Measure(UiText.T("First Board of the Unbroken")).X + 90 * UIConstants.Scale);
        if (ImGui.GetContentRegionAvail().X >= minimum * 2 + ImGui.GetStyle().ItemSpacing.X &&
            ImGui.BeginTable("BeastmasterRunColumns", 2, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoSavedSettings))
        {
            ImGui.TableNextColumn(); DrawRunSetup();
            ImGui.TableNextColumn(); DrawRunMonitoring();
            ImGui.EndTable();
        }
        else { DrawRunSetup(); ImGui.Spacing(); DrawRunMonitoring(); }
    }

    private void DrawRunSetup()
    {
        if (!UIConstants.BeginPanel("BeastmasterRunSetup", "Manual setup")) return;
        UiGui.TextDisabled("Saved separately from VMX task settings.");
        ImGui.BeginDisabled(EditingLocked);
        if (DrawSetupControls(Settings, scheduled: false)) SaveSettings();
        ImGui.EndDisabled();
        ImGui.Separator();
        DrawRunFamiliarSummary();
        if (EditingLocked) UiGui.TextDisabled("Stop Beastmaster before changing its setup, beasts or supplies.");
        UIConstants.EndPanel();
    }

    private bool DrawSetupControls(BeastmasterSettings settings, bool scheduled)
    {
        ImGui.PushID(scheduled ? "ScheduledSetup" : "ManualSetup");
        var changed = false;
        using (UIConstants.PushSettingsRows())
        {
            var goal = (int)settings.Goal;
            ImGui.SetNextItemWidth(-1);
            if (UIConstants.Combo("Goal", ref goal, ["Fixed board runs", "Train owned familiars"], 2))
            {
                settings.Goal = (BeastmasterRunGoal)goal;
                if (settings.Goal == BeastmasterRunGoal.TrainOwned) settings.TeamMode = CrucibleTeamMode.Leveling;
                changed = true;
            }
            var board = settings.Board is >= 1 and <= 5 ? (int)settings.Board - 1 : -1;
            ImGui.SetNextItemWidth(-1);
            if (UIConstants.Combo("Board", ref board, RunBoardNames, RunBoardNames.Length))
            { settings.Board = (uint)(board + 1); changed = true; }
            var degreeUnlocked = !ViewingCurrent || RunDegreeUnlocked();
            var degree = degreeUnlocked ? (int)settings.Degree : 0;
            ImGui.BeginDisabled(!degreeUnlocked);
            ImGui.SetNextItemWidth(-1);
            if (UIConstants.Combo("Difficulty", ref degree, RunDegreeNames, RunDegreeNames.Length))
            { settings.Degree = (CrucibleDegree)degree; changed = true; }
            ImGui.EndDisabled();
            if (!degreeUnlocked) UiGui.TextDisabled("Higher degrees require completion of quest 71045. Standard will be used.");
            if (settings.Goal == BeastmasterRunGoal.TrainOwned)
                UiGui.TextDisabled(UiText.F("Training target: Rank {0}", TrainingTarget(settings.Board)));
            var count = settings.RunCount;
            ImGui.SetNextItemWidth(130 * UIConstants.Scale);
            if (UIConstants.InputInt(scheduled ? "Boards per trigger" : "Boards this run", ref count, 1, 10))
            { settings.RunCount = Math.Max(1, count); changed = true; }
            UiGui.TextDisabled("Completed boards are always exited automatically.");
            var completionCommand = settings.CompletionCommand;
            ImGui.SetNextItemWidth(-1);
            if (UIConstants.InputText("Completion command (optional)", ref completionCommand, 512))
            { settings.CompletionCommand = completionCommand; changed = true; }
            UiGui.TextDisabled("Leave blank to do nothing. Runs once after the plan completes, outside the duty.");
            var training = settings.Goal == BeastmasterRunGoal.TrainOwned;
            var methods = training ? TeamMethodNames.Skip(1).Take(4).ToArray() : TeamMethodNames;
            var method = training ? Math.Clamp((int)settings.LevelingMode, 0, 3) : TeamMethodIndex(settings);
            ImGui.SetNextItemWidth(-1);
            if (UIConstants.Combo("Team method", ref method, methods, methods.Length))
            {
                if (training) method++;
                settings.TeamMode = method == 0 ? CrucibleTeamMode.Recommended : method == 5 ? CrucibleTeamMode.Custom : CrucibleTeamMode.Leveling;
                if (method is >= 1 and <= 4) settings.LevelingMode = (CrucibleLevelingMode)(method - 1);
                changed = true;
            }
            if (settings.TeamMode == CrucibleTeamMode.Recommended)
            {
                var cap = settings.RecommendedTeamCap;
                ImGui.SetNextItemWidth(130 * UIConstants.Scale);
                if (UIConstants.InputInt("Team cap", ref cap, 1, 3)) { settings.RecommendedTeamCap = cap; changed = true; }
                UIConstants.SameLineIfFits("Reset");
                if (UIConstants.Button("Reset##BeastmasterTeamCap")) { settings.RecommendedTeamCap = 15; changed = true; }
            }
        }
        UiGui.TextDisabled(UiText.F("Up to {0} familiars", TeamMethodCapacity(settings)));
        UIConstants.WrappedText(UIConstants.Metadata, TeamMethodExplanation(settings), translate: false);
        if (settings.Goal == BeastmasterRunGoal.TrainOwned)
        {
            if (settings.LevelingMode != CrucibleLevelingMode.Carry_Minus_2)
                UiGui.TextDisabled("When only one or two trainees remain, add only enough trained fighters to reach three.");
            if (settings.TeamMode != CrucibleTeamMode.Leveling)
                UIConstants.WrappedText(UIConstants.Amber, "Choose a training team method for this goal.");
        }
        var classifications = CrucibleTeam.ClassificationSheetNames;
        using (UIConstants.PushSettingsRows())
        {
            var classification = settings.ClassificationLock;
            var preview = classification > 0 && classification < classifications.Length && classifications[classification].Length > 0
                ? classifications[classification] : UiText.T("All kins");
            ImGui.SetNextItemWidth(-1);
            if (UIConstants.BeginCombo("Kin filter", preview))
            {
                if (UiGui.Selectable("All kins", classification == 0)) { settings.ClassificationLock = 0; changed = true; }
                for (var index = 1; index < classifications.Length; index++)
                    if (classifications[index].Length > 0 && UiGui.Selectable("##BeastmasterClassification" + index, classification == index, classifications[index]))
                    { settings.ClassificationLock = index; changed = true; }
                ImGui.EndCombo();
            }
        }
        ImGui.PopID();
        return changed;
    }

    private static string TeamMethodExplanation(BeastmasterSettings settings) => settings.TeamMode switch
    {
        CrucibleTeamMode.Recommended => UiText.T("Highest-ranked familiars first; beast priorities break ties."),
        CrucibleTeamMode.Custom => UiText.T("Choose custom membership in Beasts. Any setup that selects Custom uses this character's shared selection."),
        _ => settings.LevelingMode switch
        {
            CrucibleLevelingMode.Full => UiText.F("Bring up to {0} trainees, lowest rank and EXP first. Train more familiars together with more reserves.", BoardCapacity(settings)),
            CrucibleLevelingMode.Minus_3 => UiText.F("Bring up to {0} trainees, leaving three team slots open. Smaller rosters can earn more EXP per familiar, with fewer reserves.", BoardCapacity(settings) - 3),
            CrucibleLevelingMode.Only_3 => UiText.T("Bring three trainees, lowest rank and EXP first. Focus training on fewer familiars, with fewer reserves."),
            _ => UiText.F("Three strongest familiars fight first, with up to {0} trainees. Bring up to {1} total, leaving two slots open. Reserves also earn EXP.", BoardCapacity(settings) - 5, BoardCapacity(settings) - 2),
        },
    };

    private static unsafe bool RunDegreeUnlocked() => Plugin.PlayerState.IsLoaded && QuestManager.IsQuestComplete(71045);

    private void DrawRunMonitoring()
    {
        if (UIConstants.BeginPanel("BeastmasterTraining", "Training progress"))
        {
            var monitorSettings = ViewingCurrent && plugin.BeastmasterOwnsWork ? plugin.BeastmasterService.Settings : Settings;
            DrawTrainingProgress(monitorSettings, "Manual");
            if (ViewingCurrent && plugin.BeastmasterService.TryGetEntryRequirements(out var minimumLevel, out var minimumItemLevel))
                UIConstants.WrappedText(plugin.BeastmasterService.MeetsEntryRequirements(out _) ? VermaxionPresentation.Ready : UIConstants.Amber,
                    UiText.F("Entry requirements: level {0}, item level {1}. Equipped item level: {2}.", minimumLevel, minimumItemLevel,
                        BeastmasterService.EquippedItemLevel?.ToString() ?? UiText.T("Unavailable")), translate: false);
            else UiGui.TextDisabled("Entry requirements are checked for the active character before starting.");
            UIConstants.EndPanel();
        }
        DrawBoardViewer();
        DrawManualWeeklyAction();
        if (UIConstants.BeginPanel("BeastmasterRunActions", "Run actions"))
        {
            DrawRunAutomation();
            UiGui.TextDisabled("Shop, rest and healing rules are in Supplies.");
            UIConstants.EndPanel();
        }
    }

    private void DrawTrainingProgress(BeastmasterSettings settings, string id)
    {
        var owned = RecordedOwned();
        var target = TrainingTarget(settings.Board);
        var candidates = RecordedCandidates(settings).ToList();
        var reached = candidates.Count(beast => beast.Rank is >= 1 and <= 25 && beast.Rank >= target && target > 0);
        var unknown = candidates.Count(beast => beast.Rank is < 1 or > 25);
        var pending = candidates.Where(beast => beast.Rank is >= 1 and <= 25 && beast.Rank < target).ToList();
        var excluded = pending.Count(beast => beast.PickBlocked || settings.ClassificationLock != 0 && beast.Classification != settings.ClassificationLock ||
            settings.TeamMode == CrucibleTeamMode.Custom && !settings.CustomTeam.Contains(beast.Number));
        var eligible = pending.Count(beast => beast.Rank > 0 && !beast.PickBlocked &&
            (settings.ClassificationLock == 0 || beast.Classification == settings.ClassificationLock) &&
            (settings.TeamMode != CrucibleTeamMode.Custom || settings.CustomTeam.Contains(beast.Number)));
        ImGui.PushID(id);
        ImGui.ProgressBar(owned.Count > 0 ? (float)reached / owned.Count : 0, new Vector2(-1, ImGui.GetFrameHeight()),
            UiText.F("{0}/{1} at Rank {2}", reached, owned.Count, target));
        UiGui.TextDisabled(UiText.F("{0} eligible to train · {1} excluded · {2} unknown rank", eligible, excluded, unknown));
        if (CharacterData.OwnershipObservedAtUtc == DateTime.MinValue)
            UiGui.TextDisabled("Ownership has not been fully observed. Training completion is unknown.");
        if (excluded > 0) UiGui.TextDisabled("Excluded familiars keep this goal incomplete.");
        var service = plugin.BeastmasterService;
        if (ViewingCurrent && service.HasRunDisplay)
            UiGui.TextDisabled(UiText.F("{0}/{1} runs completed", service.DisplayCompletedRuns, service.DisplayRunLimit));
        ImGui.PopID();
    }

    private void DrawManualWeeklyAction()
    {
        var ready = plugin.CanStartBeastmasterWeekly(out var blocker);
        ImGui.BeginDisabled(ViewingCurrent && !ready);
        if (UIConstants.Button("Run weekly challenges##BeastmasterWeekly")) RunForViewedProfile(weekly: true, scheduled: false);
        ImGui.EndDisabled();
        if (ViewingCurrent)
        {
            var rows = plugin.BeastmasterService.SelectedWeeklyObjectives;
            var progress = UiText.F("{0}/{1} complete", rows.Count(row => row.State == BeastmasterWeeklyState.Complete), rows.Count);
            UIConstants.SameLineIfFits(progress);
            UiGui.TextDisabled(progress);
            if (!ready) EditorTooltip(UiText.Beastmaster(blocker));
        }
        else UiGui.TextDisabled("Weekly Challenge Log status is available for the playing character.");
    }

    private void DrawRunAutomation()
    {
        var settings = Settings;
        var changed = false;
        Toggle("Fight picks", settings.FightPicks, value => { settings.FightPicks = value; if (ViewingCurrent && plugin.BeastmasterOwnsWork) plugin.BeastmasterService.Settings.FightPicks = value; });
        UIConstants.SameLineIfFits("Loot"); Toggle("Loot", settings.Loot, value => { settings.Loot = value; if (ViewingCurrent && plugin.BeastmasterOwnsWork) plugin.BeastmasterService.Settings.Loot = value; });
        UIConstants.SameLineIfFits("Treasure"); Toggle("Treasure", settings.Treasure, value => { settings.Treasure = value; if (ViewingCurrent && plugin.BeastmasterOwnsWork) plugin.BeastmasterService.Settings.Treasure = value; });
        if (changed) SaveSettings();
        void Toggle(string label, bool value, Action<bool> set)
        {
            if (!UIConstants.Checkbox(label + "##BeastmasterAutomation", ref value)) return;
            set(value); changed = true;
        }
    }

    private HashSet<uint> RecordedOwned() => CharacterData.Owned.Concat(CharacterData.Team)
        .Concat(CharacterData.Familiars.Where(entry => entry.Value.Rank > 0 || entry.Value.Hp > 0 || entry.Value.Strength > 0 ||
            entry.Value.Constitution > 0 || entry.Value.Intelligence > 0 || entry.Value.PhysicalResistance > 0 || entry.Value.MagicResistance > 0 || entry.Value.Exp.Length > 0)
            .Select(entry => entry.Key)).Where(CrucibleTeam.FamiliarSheetDatas.ContainsKey).ToHashSet();

    private IEnumerable<BeastmasterCandidate> RecordedCandidates(BeastmasterSettings settings)
    {
        var priority = settings.BeastTierLists.ElementAtOrDefault(settings.BeastTierListIndex);
        foreach (var number in RecordedOwned())
        {
            CharacterData.Familiars.TryGetValue(number, out var familiar);
            var entry = priority?.FirstOrDefault(row => row.ID == number);
            var tierIndex = priority?.FindIndex(row => row.ID == number) ?? -1;
            yield return new BeastmasterCandidate(number, familiar?.Rank ?? 0, CrucibleUi.ExpShare(familiar?.Exp ?? ""),
                tierIndex >= 0 ? tierIndex : int.MaxValue,
                CrucibleTeam.FamiliarSheetDatas.TryGetValue(number, out var sheet) ? sheet.Classification : (byte)0,
                entry?.PickBlock ?? false, entry?.BattleBlock ?? false);
        }
    }

    private List<uint> PreviewTeam(BeastmasterSettings settings)
    {
        var candidates = RecordedCandidates(settings).ToList();
        var cap = TeamMethodCapacity(settings);
        if (settings.Goal == BeastmasterRunGoal.TrainOwned)
            return BeastmasterSelection.Training(candidates, cap, settings.LevelingMode == CrucibleLevelingMode.Carry_Minus_2, TrainingTarget(settings.Board), settings.ClassificationLock);
        if (settings.TeamMode == CrucibleTeamMode.Custom)
            return settings.CustomTeam.Distinct().Where(number => candidates.Any(beast => beast.Number == number &&
                (settings.ClassificationLock == 0 || beast.Classification == settings.ClassificationLock))).Take(cap).ToList();
        if (settings.TeamMode == CrucibleTeamMode.Recommended)
            return BeastmasterSelection.Recommended(candidates, cap, settings.ClassificationLock);
        return BeastmasterSelection.Leveling(candidates, cap, settings.LevelingMode == CrucibleLevelingMode.Carry_Minus_2, settings.ClassificationLock);
    }

    private void DrawRunFamiliarSummary()
    {
        using (UiText.Font(UiFontRole.BodyStrong)) UiGui.TextUnformatted("Selected familiars");
        var live = ViewingCurrent && plugin.BeastmasterOwnsWork;
        var data = live ? plugin.BeastmasterService.CharacterData : CharacterData;
        var team = live ? data.Team.ToList() : PreviewTeam(Settings);
        UiGui.TextDisabled(UiText.F("{0} familiars selected · {1}/{2} shown", team.Count, Math.Min(3, team.Count), team.Count));
        if (team.Count == 0) { UiGui.TextDisabled("No eligible familiars are recorded for this setup. Refresh beasts on the playing character."); return; }
        var chipWidth = Math.Max(125 * UIConstants.Scale, team.Take(3).Max(number => MaterialText.Measure(CrucibleTeam.NameOf(number)).X + 42 * UIConstants.Scale));
        var columns = Math.Clamp((int)((ImGui.GetContentRegionAvail().X + ImGui.GetStyle().ItemSpacing.X) / (chipWidth + ImGui.GetStyle().ItemSpacing.X)), 1, 3);
        var table = ImGui.BeginTable("BeastmasterFamiliarPreview", columns, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoSavedSettings);
        foreach (var number in team.Take(3))
        {
            if (table) { ImGui.TableNextColumn(); ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(UIConstants.Raised)); }
            ImGui.BeginGroup();
            var position = ImGui.GetCursorScreenPos();
            MaterialIcons.Draw(MaterialIcon.Egg, position, ImGui.GetTextLineHeight(), UIConstants.Blue);
            ImGui.Dummy(new Vector2(ImGui.GetTextLineHeight()));
            ImGui.SameLine();
            UiGui.TextWrapped(CrucibleTeam.NameOf(number));
            data.Familiars.TryGetValue(number, out var familiar);
            UiGui.TextDisabled(familiar is { Rank: > 0 } ? UiText.F("Rank {0}", familiar.Rank) : UiText.T("Rank unknown"));
            ImGui.EndGroup();
        }
        if (table) ImGui.EndTable();
        if (team.Count < 3) UIConstants.WrappedText(UIConstants.Amber, "At least three eligible familiars are required before challenging a board.");
    }
    private void DrawBoardViewer()
    {
        if (!UIConstants.BeginPanel("BeastmasterBoardPanel", "Current board")) return;
        var board = Plugin.PlayerState.IsLoaded ? CrucibleBoard.Current() : null;
        if (board == null || board.Stops.Count == 0)
        {
            var settings = ViewingCurrent && plugin.BeastmasterOwnsWork ? plugin.BeastmasterService.Settings : Settings;
            UiGui.TextWrapped(RunBoardName(settings.Board));
            var height = (plugin.Configuration.CompactUi ? 260 : 320) * UIConstants.Scale;
            var min = ImGui.GetCursorScreenPos();
            var max = min + new Vector2(ImGui.GetContentRegionAvail().X, height);
            ImGui.GetWindowDrawList().AddRectFilled(min, max, ImGui.GetColorU32(UIConstants.Window), 4 * UIConstants.Scale);
            ImGui.Dummy(new Vector2(max.X - min.X, height));
            ImGui.SetCursorScreenPos(min + new Vector2(16 * UIConstants.Scale));
            UiGui.TextWrapped("The live board is available after entering the Crucible. Nodes and player position are unknown outside the duty.");
            ImGui.SetCursorScreenPos(new Vector2(min.X, max.Y + ImGui.GetStyle().ItemSpacing.Y));
        }
        else
        {
            UiGui.TextWrapped(board.Name);
            DrawBoardGraph(board);
            UiGui.TextDisabled(UiText.F("{0} stops · {1} connections", board.Stops.Count, board.Paths.Count));
        }
        DrawBoardLegend();
        var service = plugin.BeastmasterService;
        UiGui.TextDisabled(service.RouteStepCount > 0
            ? UiText.F("Route {0:00} · {1}/{2} steps", service.EffectiveBoard, service.RouteStep, service.RouteStepCount)
            : UiText.T("Route has not started."));
        UIConstants.EndPanel();
    }

    private void DrawBoardGraph(CrucibleBoard board)
    {
        var width = ImGui.GetContentRegionAvail().X;
        var height = Math.Max((plugin.Configuration.CompactUi ? 300 : 350) * UIConstants.Scale, width * .82f);
        var min = ImGui.GetCursorScreenPos();
        var size = new Vector2(width, height);
        ImGui.Dummy(size);
        var hovered = ImGui.IsItemHovered();
        var draw = ImGui.GetWindowDrawList();
        var padding = 26 * UIConstants.Scale;
        var minX = board.Stops.Min(stop => stop.Position.X);
        var minY = board.Stops.Min(stop => stop.Position.Y);
        var rangeX = Math.Max(1, board.Stops.Max(stop => stop.Position.X) - minX);
        var rangeY = Math.Max(1, board.Stops.Max(stop => stop.Position.Y) - minY);
        var scale = Math.Max(.01f, Math.Min(Math.Max(1, width - padding * 2) / rangeX, Math.Max(1, height - padding * 2) / rangeY));
        var origin = min + (size - new Vector2(rangeX, rangeY) * scale) / 2;
        Vector2 Point(Vector2 position) => origin + (position - new Vector2(minX, minY)) * scale;
        draw.AddRectFilled(min, min + size, ImGui.GetColorU32(UIConstants.Window), 4 * UIConstants.Scale);
        draw.AddRect(min, min + size, ImGui.GetColorU32(UIConstants.Border), 4 * UIConstants.Scale);
        draw.PushClipRect(min, min + size, true);
        try
        {
            foreach (var path in board.Paths)
            {
                var from = board.Stops.FirstOrDefault(stop => stop.Id == path.From);
                var to = board.Stops.FirstOrDefault(stop => stop.Id == path.To);
                if (from != null && to != null) draw.AddLine(Point(from.Position), Point(to.Position), ImGui.GetColorU32(UIConstants.Metadata), UIConstants.Scale);
            }
            var current = board.StopPlayerIsOn();
            foreach (var stop in board.Stops)
            {
                var point = Point(stop.Position);
                var radius = (stop.Kind == CrucibleStopKind.Boss ? 15 : 11) * UIConstants.Scale;
                var color = DrawBoardStopColor(stop.Kind);
                draw.AddCircleFilled(point, radius, ImGui.GetColorU32(UIConstants.Panel));
                draw.AddCircle(point, radius, ImGui.GetColorU32(color), 0, 1.5f * UIConstants.Scale);
                MaterialIcons.Draw(DrawBoardStopIcon(stop.Kind), point - new Vector2(radius * .66f), radius * 1.32f, color);
                if (current?.Id == stop.Id) draw.AddCircle(point, radius + 4 * UIConstants.Scale, ImGui.GetColorU32(UIConstants.Blue), 0, 2.5f * UIConstants.Scale);
                if (hovered && Vector2.DistanceSquared(ImGui.GetMousePos(), point) <= radius * radius)
                    MaterialText.SetTooltip(UiText.F("{0} · Stop {1}", UiText.T(DrawBoardStopName(stop.Kind)), stop.Id) +
                        (stop.Number > 0 ? UiText.F(" · No. {0}", stop.Number) : string.Empty));
            }
            if (Plugin.ObjectTable.LocalPlayer is { } player)
            {
                var point = Point(new Vector2(player.Position.X, player.Position.Z));
                draw.AddCircleFilled(point, 4 * UIConstants.Scale, ImGui.GetColorU32(UIConstants.Blue));
                draw.AddCircle(point, 7 * UIConstants.Scale, ImGui.GetColorU32(UIConstants.Blue), 0, 1.5f * UIConstants.Scale);
            }
        }
        finally { draw.PopClipRect(); }
    }

    private static string DrawBoardStopName(CrucibleStopKind kind) => kind switch
    {
        CrucibleStopKind.Start => "Start", CrucibleStopKind.Enemy => "Fight", CrucibleStopKind.Elite => "Elite",
        CrucibleStopKind.Boss => "Boss", CrucibleStopKind.Shop => "Shop", CrucibleStopKind.Campsite => "Camp",
        CrucibleStopKind.Treasure => "Treasure", CrucibleStopKind.Random => "Random", _ => "Unknown",
    };
    private static MaterialIcon DrawBoardStopIcon(CrucibleStopKind kind) => kind switch
    {
        CrucibleStopKind.Start => MaterialIcon.MapPin, CrucibleStopKind.Enemy => MaterialIcon.Sword,
        CrucibleStopKind.Elite => MaterialIcon.Star, CrucibleStopKind.Boss => MaterialIcon.Crown,
        CrucibleStopKind.Shop => MaterialIcon.Cart, CrucibleStopKind.Campsite => MaterialIcon.Home,
        CrucibleStopKind.Treasure => MaterialIcon.Chest, _ => MaterialIcon.Info,
    };
    private static Vector4 DrawBoardStopColor(CrucibleStopKind kind) => kind switch
    {
        CrucibleStopKind.Enemy or CrucibleStopKind.Boss => VermaxionPresentation.Error,
        CrucibleStopKind.Elite or CrucibleStopKind.Treasure => UIConstants.Amber,
        CrucibleStopKind.Shop => UIConstants.Blue, CrucibleStopKind.Campsite => VermaxionPresentation.Ready,
        _ => UIConstants.Metadata,
    };

    private static void DrawBoardLegend()
    {
        var first = true;
        foreach (var kind in new[] { CrucibleStopKind.Start, CrucibleStopKind.Enemy, CrucibleStopKind.Elite, CrucibleStopKind.Campsite,
                     CrucibleStopKind.Shop, CrucibleStopKind.Treasure, CrucibleStopKind.Boss, CrucibleStopKind.Random })
        {
            var label = DrawBoardStopName(kind);
            var iconSize = ImGui.GetTextLineHeight();
            if (!first) UIConstants.SameLineIfFits(label, MaterialText.Measure(UiText.T(label)).X + iconSize + ImGui.GetStyle().ItemInnerSpacing.X);
            ImGui.BeginGroup();
            var position = ImGui.GetCursorScreenPos();
            MaterialIcons.Draw(DrawBoardStopIcon(kind), position, iconSize, DrawBoardStopColor(kind));
            ImGui.Dummy(new Vector2(iconSize));
            ImGui.SameLine(0, ImGui.GetStyle().ItemInnerSpacing.X);
            UiGui.TextUnformatted(label);
            ImGui.EndGroup();
            first = false;
        }
        UiGui.TextDisabled("Cyan ring: current stop. Cyan dot: live player position.");
    }
}
