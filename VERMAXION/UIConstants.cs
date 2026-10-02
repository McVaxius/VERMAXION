using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.ManagedFontAtlas;

namespace VERMAXION;

public static class UIConstants
{
    public static float Scale => ImGuiHelpers.GlobalScale;
    public static readonly Vector4 Text = new(243 / 255f, 245 / 255f, 247 / 255f, 1f); // #F3F5F7
    public static readonly Vector4 Metadata = new(184 / 255f, 196 / 255f, 204 / 255f, 1f); // #B8C4CC
    public static readonly Vector4 Blue = new(125 / 255f, 231 / 255f, 240 / 255f, 1f); // #7DE7F0
    public static readonly Vector4 Amber = new(255 / 255f, 211 / 255f, 138 / 255f, 1f); // #FFD38A
    internal static readonly Vector4 Mint = new(135 / 255f, 230 / 255f, 199 / 255f, 1f);
    internal static readonly Vector4 Window = new(21 / 255f, 28 / 255f, 34 / 255f, 1f);
    internal static readonly Vector4 Panel = new(27 / 255f, 37 / 255f, 45 / 255f, 1f);
    internal static readonly Vector4 Raised = new(37 / 255f, 51 / 255f, 61 / 255f, 1f);
    internal static readonly Vector4 Border = new(77 / 255f, 99 / 255f, 114 / 255f, 1f);
    internal static readonly Vector4 ButtonColor = new(43 / 255f, 57 / 255f, 69 / 255f, 1f);
    internal static readonly Vector4 Hover = new(53 / 255f, 70 / 255f, 83 / 255f, 1f);
    internal static readonly Vector4 Pressed = new(62 / 255f, 83 / 255f, 97 / 255f, 1f);
    internal static readonly Vector4 Selected = new(13 / 255f, 78 / 255f, 90 / 255f, 1f);
    internal static readonly Vector4 SelectedHover = new(18 / 255f, 98 / 255f, 113 / 255f, 1f);
    internal static readonly Vector4 SelectedPressed = new(22 / 255f, 113 / 255f, 130 / 255f, 1f);
    internal static IFontHandle? ApplicationFont;
    internal static IFontHandle? SectionFont;
    // Applied before Begin, so window padding and child/table density agree across every surface.
    public static void PushStyle(bool compact)
    {
        var scale = ImGuiHelpers.GlobalScale;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(compact ? 8 : 16, compact ? 6 : 12) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2((compact ? 6 : 10) * scale, System.Math.Max(0, (36f * scale - ImGui.GetFontSize()) / 2f)));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(compact ? 6 : 10, compact ? 3 : 8) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemInnerSpacing, new Vector2(compact ? 4 : 8, compact ? 3 : 6) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(compact ? 5 : 10, compact ? 3 : 7) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.TabRounding, 4f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.DisabledAlpha, 0.95f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, scale);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, Window);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Panel);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, Panel);
        ImGui.PushStyleColor(ImGuiCol.Text, Text);
        ImGui.PushStyleColor(ImGuiCol.TextDisabled, Metadata);
        ImGui.PushStyleColor(ImGuiCol.Border, Border);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Raised);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Hover);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Pressed);
        ImGui.PushStyleColor(ImGuiCol.Button, ButtonColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Hover);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Pressed);
        ImGui.PushStyleColor(ImGuiCol.Header, Selected);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, SelectedHover);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, SelectedPressed);
        ImGui.PushStyleColor(ImGuiCol.Tab, Raised);
        ImGui.PushStyleColor(ImGuiCol.TabHovered, SelectedHover);
        ImGui.PushStyleColor(ImGuiCol.TabActive, Selected);
        ImGui.PushStyleColor(ImGuiCol.CheckMark, Blue);
        ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, Raised);
        ImGui.PushStyleColor(ImGuiCol.TableRowBgAlt, new Vector4(37 / 255f, 51 / 255f, 61 / 255f, 0.35f));
        ImGui.PushStyleColor(ImGuiCol.TableRowBg, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.TitleBg, Window);
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, Panel);
        ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, Window);
        ImGui.PushStyleColor(ImGuiCol.TabUnfocused, Raised);
        ImGui.PushStyleColor(ImGuiCol.TabUnfocusedActive, Selected);
        ImGui.PushStyleColor(ImGuiCol.TextSelectedBg, Selected);
        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, SelectedPressed);
        ImGui.PushStyleColor(ImGuiCol.PlotHistogramHovered, SelectedPressed);
        ImGui.PushStyleColor(ImGuiCol.SliderGrab, SelectedPressed);
        ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, Blue);
        ImGui.PushStyleColor(ImGuiCol.BorderShadow, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.Separator, Border);
        ImGui.PushStyleColor(ImGuiCol.SeparatorHovered, Hover);
        ImGui.PushStyleColor(ImGuiCol.SeparatorActive, Pressed);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, Window);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, Border);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, Hover);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, Pressed);
        ImGui.PushStyleColor(ImGuiCol.ResizeGrip, Border);
        ImGui.PushStyleColor(ImGuiCol.ResizeGripHovered, Hover);
        ImGui.PushStyleColor(ImGuiCol.ResizeGripActive, Blue);
        ImGui.PushStyleColor(ImGuiCol.TableBorderStrong, Border);
        ImGui.PushStyleColor(ImGuiCol.TableBorderLight, Border);
        ImGui.PushStyleColor(ImGuiCol.NavHighlight, Blue);
        ImGui.PushStyleColor(ImGuiCol.NavWindowingHighlight, new Vector4(0.8f, 0.8f, 0.8f, 0.7f));
        ImGui.PushStyleColor(ImGuiCol.NavWindowingDimBg, new Vector4(Window.X, Window.Y, Window.Z, 0.6f));
        ImGui.PushStyleColor(ImGuiCol.ModalWindowDimBg, new Vector4(Window.X, Window.Y, Window.Z, 0.6f));
    }

    public static void PopStyle()
    {
        ImGui.PopStyleColor(49);
        ImGui.PopStyleVar(13);
    }

    public static void Heading(string text, bool compact = false)
    {
        if (!compact) ImGui.Spacing();
        ImGui.PushTextWrapPos(0f);
        using (SectionFont?.Push())
            ImGui.TextColored(Text, text);
        ImGui.PopTextWrapPos();
        ImGui.Separator();
    }

    public static void SameLineIfFits(string nextLabel)
    {
        var width = ImGui.CalcTextSize(nextLabel, true).X + ImGui.GetStyle().FramePadding.X * 2;
        var remaining = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X - ImGui.GetItemRectMax().X;
        if (remaining >= width + ImGui.GetStyle().ItemSpacing.X)
            ImGui.SameLine();
    }

    internal static void ApplicationHeading(string subtitle)
    {
        using (ApplicationFont?.Push())
            ImGui.TextUnformatted("VERMAXION");
        WrappedText(Metadata, subtitle);
    }

    internal static void WrappedText(Vector4 color, string text)
    {
        ImGui.PushTextWrapPos(0);
        ImGui.TextColored(color, text);
        ImGui.PopTextWrapPos();
    }

    internal static bool BeginPanel(string id, string? title = null)
    {
        ImGui.BeginGroup();
        if (!ImGui.BeginTable(id, 1, ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.EndGroup();
            return false;
        }
        ImGui.TableSetupColumn("##Panel", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(Panel));
        if (title != null) Heading(title, true);
        return true;
    }

    internal static void EndPanel()
    {
        ImGui.EndTable();
        ImGui.EndGroup();
        ImGui.GetWindowDrawList().AddRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(),
            ImGui.GetColorU32(Border), 6f * Scale, ImDrawFlags.None, Scale);
    }

    internal static bool CollapsingHeading(string title)
    {
        using (SectionFont?.Push())
            return ImGui.CollapsingHeader(title, ImGuiTreeNodeFlags.DefaultOpen);
    }

    internal static float ButtonWidth(string label)
        => ImGui.CalcTextSize(label, true).X + ImGui.GetStyle().FramePadding.X * 2;

    internal static bool WrappedSelectable(string label, bool selected)
    {
        var text = label.Split("##", System.StringSplitOptions.None)[0];
        var position = ImGui.GetCursorScreenPos();
        var width = System.Math.Max(1f, ImGui.GetContentRegionAvail().X);
        var height = System.Math.Max(36f * Scale, ImGui.CalcTextSize(text, false, width).Y + ImGui.GetStyle().ItemSpacing.Y);
        var clicked = ImGui.Selectable("##Wrapped_" + label, selected, ImGuiSelectableFlags.None, new Vector2(width, height));
        ImGui.GetWindowDrawList().AddText(ImGui.GetFont(), ImGui.GetFontSize(),
            position + new Vector2(0, (height - ImGui.CalcTextSize(text, false, width).Y) / 2),
            ImGui.GetColorU32(ImGuiCol.Text), text, width);
        return clicked;
    }

    // Native Button owns interaction; draw a wrapped label when the available width is narrow.
    internal static bool Button(string label, Vector2 size = default)
    {
        var visibleLabel = label.Split("##", System.StringSplitOptions.None)[0];
        var width = System.Math.Max(1f, System.Math.Min(size.X < 0 ? ImGui.GetContentRegionAvail().X : size.X > 0 ? size.X : ButtonWidth(label), ImGui.GetContentRegionAvail().X));
        var textWidth = System.Math.Max(1f, width - ImGui.GetStyle().FramePadding.X * 2);
        var textSize = ImGui.CalcTextSize(visibleLabel, false, textWidth);
        var height = System.Math.Max(size.Y > 0 ? size.Y : 36f * Scale, textSize.Y + ImGui.GetStyle().FramePadding.Y * 2);
        if (ImGui.CalcTextSize(visibleLabel).X <= textWidth)
            return ImGui.Button(label, new Vector2(width, height));
        var clicked = ImGui.Button("##Wrapped_" + label, new Vector2(width, height));
        var position = ImGui.GetItemRectMin() + new Vector2(ImGui.GetStyle().FramePadding.X, (height - textSize.Y) / 2);
        ImGui.GetWindowDrawList().AddText(ImGui.GetFont(), ImGui.GetFontSize(), position,
            ImGui.GetColorU32(ImGuiCol.Text), visibleLabel, textWidth);
        return clicked;
    }

    internal static bool FullStopButton()
    {
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(185 / 255f, 35 / 255f, 45 / 255f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(195 / 255f, 43 / 255f, 54 / 255f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(167 / 255f, 30 / 255f, 40 / 255f, 1));
        var clicked = Button("FULL STOP", new Vector2(180f * Scale, 44f * Scale));
        ImGui.PopStyleColor(3);
        return clicked;
    }

    internal static void Status(string id, string text, Vector4 color)
    {
        var limit = ImGui.GetTextLineHeightWithSpacing() * 3;
        if (ImGui.CalcTextSize(text, false, ImGui.GetContentRegionAvail().X).Y > limit)
        {
            if (ImGui.BeginChild(id, new Vector2(0, limit), false))
                WrappedText(color, text);
            ImGui.EndChild();
        }
        else WrappedText(color, text);
    }

    internal static void SetNextItemWidth(float width)
        => ImGui.SetNextItemWidth(width <= 0 ? width : System.Math.Max(1f, System.Math.Min(width, ImGui.GetContentRegionAvail().X)));

    internal static bool Checkbox(string label, ref bool value)
    {
        var text = label.Split("##", System.StringSplitOptions.None)[0];
        if (ImGui.CalcTextSize(text).X + 36f * Scale + ImGui.GetStyle().ItemInnerSpacing.X <= ImGui.GetContentRegionAvail().X)
            return ImGui.Checkbox(label, ref value);
        ImGui.BeginGroup();
        var changed = ImGui.Checkbox("##Wrapped_" + label, ref value);
        ImGui.SameLine();
        WrappedText(Text, text);
        if (ImGui.IsItemClicked()) { value = !value; changed = true; }
        ImGui.EndGroup();
        return changed;
    }

    private static string ControlLabel(string label)
    {
        var text = label.Split("##", System.StringSplitOptions.None)[0];
        if (text.Length == 0) return label;
        ImGui.TextWrapped(text);
        return "##Control_" + label;
    }

    internal static bool InputInt(string label, ref int value, int step = 1, int fastStep = 100)
    {
        var width = ImGui.CalcItemWidth();
        var id = ControlLabel(label);
        SetNextItemWidth(width);
        var roomForSteps = System.Math.Min(width, ImGui.GetContentRegionAvail().X) >=
            ImGui.CalcTextSize("00000").X + ImGui.GetStyle().FramePadding.X * 2 + ImGui.GetFrameHeight() * 2 + ImGui.GetStyle().ItemInnerSpacing.X * 2;
        return ImGui.InputInt(id, ref value, roomForSteps ? step : 0, roomForSteps ? fastStep : 0);
    }

    internal static bool InputFloat(string label, ref float value, float step = 0, float fastStep = 0, string format = "%.3f")
    {
        var width = ImGui.CalcItemWidth();
        var id = ControlLabel(label);
        SetNextItemWidth(width);
        return ImGui.InputFloat(id, ref value, step, fastStep, format);
    }

    internal static bool BeginCombo(string label, string preview)
    {
        var width = ImGui.CalcItemWidth();
        var id = ControlLabel(label);
        SetNextItemWidth(width);
        var open = ImGui.BeginCombo(id, preview);
        if (!open && ImGui.IsItemHovered()) ImGui.SetTooltip(preview);
        return open;
    }

    internal static bool Combo(string label, ref int index, string items)
    {
        var width = ImGui.CalcItemWidth();
        var id = ControlLabel(label);
        SetNextItemWidth(width);
        return ImGui.Combo(id, ref index, items);
    }

    internal static bool Combo(string label, ref int index, string[] items, int count)
    {
        var width = ImGui.CalcItemWidth();
        var id = ControlLabel(label);
        SetNextItemWidth(width);
        return ImGui.Combo(id, ref index, items, count);
    }

    internal static bool InputText(string label, ref string value, int maxLength)
    {
        var width = ImGui.CalcItemWidth();
        var id = ControlLabel(label);
        SetNextItemWidth(width);
        return ImGui.InputText(id, ref value, maxLength);
    }

    public static class ConfigLabels
    {
        // Global Settings
        public const string AutoWidthMainTaskColumns = "Auto width the columns";
        public const string KrangleNames = "Krangle Names";
        public const string AutoRestoreRetainerCheckingAfterWork = "Keep current/previous characters enabled in AutoRetainer";
        public const string EnableCharacterSelectStallRecovery = "Recover stalled character select";
        public const string DtrBarEntry = "DTR Bar Entry";
        
        // Character Settings
        public const string Enabled = "Character automation enabled";
        public const string FCBuffRefill = "FC Buff Refill (Seal Sweetener)";
        public const string AllowFCBuffActivation = "Allow VERMAXION to activate Seal Sweetener II";
        public const string MaintainFCBuffStockTarget = "Maintain configured Seal Sweetener II stock target";
        public const string MaxPurchaseAttempts = "Purchase quantity / stock target";
        public const string MinFCPoints = "Min FC Points";
        public const string MinGil = "Min Gil";
        public const string MinionRoulette = "Minion Roulette";
        public const string SeasonalGearRoulette = "Seasonal Gear Roulette";
        public const string GearUpdater = "Gear Updater";
        public const string VerminionQueue = "Lord of Verminion";
        public const string JumboCactpot = "Jumbo Cactpot (auto DC timing)";
        public const string MiniCactpot = "Mini Cactpot";
        public const string ChocoboRacing = "Chocobo Racing";
        public const string LootGoblinMapGather = "LootGoblin Map Gather";
        public const string NagYourMom = "nag your mom";
        public const string NagYourMomCasualCc = "Casual CC";
        public const string NagYourMomFrontline = "Frontline";
        public const string NagYourMomRivalWings = "Rival Wings";
        public const string NagYourMomRunsPerDay = "CC runs per day";
        public const string NagYourMomFrontlineRunsPerDay = "Frontline runs per day";
        public const string NagYourMomRivalWingsRunsPerDay = "Rival Wings runs per day";
        public const string NagYourMomJob = "mom job";
        public const string NagYourMomWindowStartLocal = "Local start (HH:mm)";
        public const string NagYourMomWindowEndLocal = "Local end (HH:mm)";
        public const string NagYourMomStopAtSeriesRank25 = "Stop at series rank 25";
        public const string NagYourDad = "nag your dad";
        public const string MiscCmd = "Misc Cmd";
        public const string NagYourDadDungeonCount = "dad dungeon count";
        public const string NagYourDadDungeonFrequency = "dad dungeon frequency";
        public const string NagYourDadDungeonName = "dad dungeon";
        public const string NagYourDadDungeonJob = "dad dungeon job";
        public const string NagYourDadQueueViaLanParty = "QUEUE via LAN PARTY module";
        public const string NagYourDadDungeonUnsynced = "Run dungeon unsynced";
        public const string NagYourDadDailyMsq = "Run daily MSQ via LAN Party";
        public const string NagYourDadLanPartyPreset = "LAN Party preset";
        public const string NagYourDadCommendationAttempts = "Commendation attempts";
        public const string NagYourDadAstropeAttempts = "Astrope attempts";
        public const string NagYourDadWindowStartLocal = "Astrope local start (HH:mm)";
        public const string NagYourDadWindowEndLocal = "Astrope local end (HH:mm)";
        public const string RacesPerDay = "Races Per Day";
        public const string SkipChocoboRacingIfLevel50 = "Don't race if racing chocobo is rank 50";
        
        // Section Headers
        public const string GlobalSettings = "Global Settings";
        public const string EveryARPostProcess = "Every AR PostProcess";
        public const string WeeklyTasks = "Weekly Tasks";
        public const string DailyTasks = "Daily Tasks";
        public const string VariableTimeTasks = "Variable time tasks";
        public const string WipTasks = "WIP tasks";
        
        // Other Labels
        public const string Account = "Account:";
        public const string Characters = "Characters";
        public const string Settings = "Settings";
        public const string NewCharactersInheritThese = "New characters inherit these settings";
        public const string AccountAlias = "Account Alias:";
        public const string Save = "Save";
    }
    
    public static class Tooltips
    {
        public const string AutoWidthMainTaskColumns = "Measure the Favorite column and reserve 180 scaled pixels for Actions; Task uses the remaining space, with timing, ownership, dependencies and progress beneath its name. Turn this off to drag the dividers; Dalamud saves the manual widths for each dashboard view.";
        public const string KrangleNames = "Replace character names with exercise words for screenshots";
        public const string AutoRestoreRetainerCheckingAfterWork = "Restore and persist AutoRetainer checking for the current and immediately previous character whenever either is disabled. Turn this off before intentionally deselecting either character.";
        public const string EnableCharacterSelectStallRecovery = "After five minutes waiting at character select during a VERMAXION fishing relog, attempt to load the first live character once. The same guard also controls the status test button.";
        public const string MinionRoulette = "Fire off /minion roulette once per AR postprocess";
        public const string SeasonalGearRoulette = "Randomly equip seasonal event gear for a fun ensemble each AR run";
        public const string GearUpdater = "Updates valid saved gearsets through optional Stylist IPC or the native recommended-equipment path. If none exist, first runs the bounded missing-gearset bootstrap.";
        public const string NagYourMom = "AR-only mom task with a local time window and per-route daily completed-match counts. At the end time, withdraw a waiting queue or finish the current match, then complete mom for this cycle. Requires mom queue-deadline support.";
        public const string NagYourDad = "AR-only DAD launch. Select one live saved DAD preset or schedule; VERMAXION tracks and cancels that exact scheduler job, planner request, or schedule run.";
        public const string MiscCmd = "Sends startup cleanup commands at the start of AutoRetainer and manual VERMAXION runs.";
    }
}
