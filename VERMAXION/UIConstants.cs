using AethertekUI;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.ManagedFontAtlas;

namespace VERMAXION;

public static class UIConstants
{
    public static float Scale => ImGuiHelpers.GlobalScale;
    public static Vector4 Text => MaterialTheme.Current.Colors.OnSurface; // #F3F5F7
    public static Vector4 Metadata => MaterialTheme.Current.Colors.OnSurfaceVariant; // #B8C4CC
    public static Vector4 Blue => MaterialTheme.Current.Colors.Primary; // #7DE7F0
    public static readonly Vector4 Amber = new(255 / 255f, 211 / 255f, 138 / 255f, 1f); // #FFD38A
    internal static readonly Vector4 Mint = new(135 / 255f, 230 / 255f, 199 / 255f, 1f);
    internal static Vector4 Window => MaterialTheme.Current.Colors.Background;
    internal static Vector4 Panel => MaterialTheme.Current.Colors.Surface;
    internal static Vector4 Raised => MaterialTheme.Current.Colors.SurfaceContainerHigh;
    internal static Vector4 Border => MaterialTheme.Current.Colors.OutlineVariant;
    internal static Vector4 ButtonColor => MaterialTheme.Current.Colors.SurfaceContainerHigh;
    internal static Vector4 Hover => MaterialTheme.Current.Colors.SurfaceContainerHighest;
    internal static Vector4 Pressed => MaterialTheme.Current.Colors.PrimaryContainer;
    internal static Vector4 Selected => MaterialTheme.Current.Colors.PrimaryContainer;
    internal static Vector4 SelectedHover => MaterialTheme.Current.Colors.PrimaryContainer;
    internal static Vector4 SelectedPressed => MaterialTheme.Current.Colors.Primary;
    internal static bool Compact;
    // Applied before Begin, so window padding and child/table density agree across every surface.
    public static void PushStyle(bool compact)
    {
        Compact = compact;
        var scale = ImGuiHelpers.GlobalScale;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(compact ? 8 : 16, compact ? 6 : 12) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2((compact ? 6 : 10) * scale, System.Math.Max(0, ((compact ? 30f : 36f) * scale - ImGui.GetFontSize()) / 2f)));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(compact ? 6 : 10, compact ? 3 : 8) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemInnerSpacing, new Vector2(compact ? 4 : 8, compact ? 3 : 6) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(compact ? 5 : 10, compact ? 3 : 7) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, MaterialTheme.Metrics.OuterRadius);
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
        ImGui.PushStyleColor(ImGuiCol.TableRowBgAlt, MaterialColor.Alpha(Raised, .35f));
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
        using (UiText.Font(UiFontRole.PluginName))
            WrappedText(Text, text);
        ImGui.Separator();
    }

    public static void SameLineIfFits(string nextLabel, float measuredWidth = 0)
    {
        var width = measuredWidth > 0 ? measuredWidth : MaterialText.Measure(UiText.T(nextLabel.Split("##", 2)[0]), false).X + ImGui.GetStyle().FramePadding.X * 2;
        var remaining = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X - ImGui.GetItemRectMax().X;
        if (remaining >= width + ImGui.GetStyle().ItemSpacing.X)
            ImGui.SameLine();
    }

    internal static void ApplicationHeading(string subtitle)
    {
        using (UiText.Font(Compact ? UiFontRole.CompactTitle : UiFontRole.Title))
            MaterialText.Text("VERMAXION");
        WrappedText(Metadata, subtitle);
    }

    internal static void WrappedText(Vector4 color, string text, bool translate = true)
    {
        var display = translate ? UiText.T(text) : text;
        var insets = WrappedTextInsets(display);
        var x = ImGui.GetCursorPosX();
        var width = ImGui.GetContentRegionAvail().X;
        ImGui.SetCursorPosX(x + insets.Left);
        ImGui.PushTextWrapPos(x + System.MathF.Max(1, width - insets.Right));
        try { MaterialText.TextColored(color, display); }
        finally { ImGui.PopTextWrapPos(); ImGui.SetCursorPosX(x); }
    }

    private static unsafe (float Left, float Right) WrappedTextInsets(string text)
    {
        var font = ImGui.GetFont();
        var scale = ImGui.GetFontSize() / font.FontSize;
        var left = 0f; var right = 0f;
        foreach (var character in MaterialText.NativeGlyphText(text))
        {
            var glyph = ImGui.FindGlyphNoFallback(font, character);
            if (glyph.Handle == null) continue;
            left = System.MathF.Max(left, -glyph.Handle->X0 * scale);
            right = System.MathF.Max(right, (glyph.Handle->X1 - glyph.Handle->AdvanceX) * scale);
        }
        return (System.MathF.Ceiling(left), System.MathF.Ceiling(right));
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
        // A group's bounds follow content width; the table fills the available width.
        var panelMin = ImGui.GetItemRectMin();
        var panelMax = ImGui.GetItemRectMax();
        ImGui.EndGroup();
        ImGui.GetWindowDrawList().AddRect(panelMin, panelMax,
            ImGui.GetColorU32(Border), 6f * Scale, ImDrawFlags.None, Scale);
    }

    internal static bool CollapsingHeading(string title, ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.None, string? display = null)
    {
        using var headerStyle = new MaterialStyleScope();
        headerStyle.Color(ImGuiCol.Header, Raised);
        headerStyle.Color(ImGuiCol.HeaderHovered, Hover);
        headerStyle.Color(ImGuiCol.HeaderActive, Pressed);
        bool open;
        using (UiText.Font(UiFontRole.PluginName))
        {
            var width = Math.Max(1, ImGui.GetContentRegionAvail().X - ImGui.GetTreeNodeToLabelSpacing() - ImGui.GetStyle().FramePadding.X);
            var original = title.Split("##", 2)[0];var translated = display ?? UiText.T(original);
            using var lineHeight = MaterialText.PushLineHeight(translated);
            var size = MaterialText.Measure(translated);
            var padding = ImGui.GetStyle().FramePadding;
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, padding);
            ImGui.PushStyleColor(ImGuiCol.Text, Vector4.Zero);
            // Keep the original English-derived helper identity at the original wrapping boundary.
            open = ImGui.CollapsingHeader(ImGui.CalcTextSize(original).X <= width ? title : "##Wrapped_" + title, flags);
            ImGui.PopStyleColor(); ImGui.PopStyleVar();
            var position = ImGui.GetItemRectMin() + new Vector2(ImGui.GetTreeNodeToLabelSpacing(), padding.Y);
            MaterialIcons.Draw(open ? MaterialIcon.ChevronDown : MaterialIcon.ArrowRight, ImGui.GetItemRectMin() + padding, ImGui.GetFontSize(), Text);
            var drawing=ImGui.GetWindowDrawList();
            drawing.PushClipRect(ImGui.GetItemRectMin(),ImGui.GetItemRectMax(),true);
            try { MaterialText.AddText(drawing,position,ImGui.GetColorU32(ImGuiCol.Text),translated); }
            finally { drawing.PopClipRect(); }
            if(size.X>width && ImGui.IsItemHovered()) MaterialText.SetTooltip(translated);
        }
        return open;
    }

    internal static float ButtonWidth(string label)
        => MaterialText.Measure(UiText.T(label.Split("##", 2)[0])).X + ImGui.GetStyle().FramePadding.X * 2;

    internal static bool WrappedSelectable(string label, bool selected, bool current = false, string? display = null)
    {
        var text = display ?? UiText.T(label.Split("##", System.StringSplitOptions.None)[0]);
        var position = ImGui.GetCursorScreenPos();
        var width = System.Math.Max(ImGui.GetContentRegionAvail().X,MaterialText.Measure(text).X+ImGui.GetStyle().FramePadding.X*2);
        var textHeight = Math.Max(ImGui.GetTextLineHeight(), MaterialText.Measure(text).Y);
        var height = System.Math.Max((Compact?30f:36f) * Scale, textHeight+ImGui.GetStyle().ItemSpacing.Y);
        if(current)
            ImGui.GetWindowDrawList().AddRectFilled(position,position+new Vector2(width,height),ImGui.GetColorU32(MaterialColor.Alpha(Selected,.5f)),4*Scale);
        var clicked = ImGui.Selectable("##Wrapped_" + label, selected, ImGuiSelectableFlags.None, new Vector2(width, height));
        MaterialText.AddText(ImGui.GetWindowDrawList(),ImGui.GetFont(), ImGui.GetFontSize(),
            position + new Vector2(ImGui.GetStyle().FramePadding.X, (height - textHeight) / 2),
            ImGui.GetColorU32(ImGuiCol.Text), text);
        if(current)
            ImGui.GetWindowDrawList().AddLine(position+new Vector2(2*Scale,3*Scale),position+new Vector2(2*Scale,height-3*Scale),ImGui.GetColorU32(Blue),3*Scale);
        return clicked;
    }

    // Native Button owns interaction; measured single-line text can move the action to a fresh row.
    internal static bool Button(string label, Vector2 size = default, bool action = false)
        => ButtonWithCaption(label, UiText.T(label.Split("##", System.StringSplitOptions.None)[0]), size, action);

    internal static bool Button(string label, Vector2 size, bool action, float? previousWidth)
        => ButtonWithCaption(label, UiText.T(label.Split("##", System.StringSplitOptions.None)[0]), size, action, previousWidth);

    internal static bool ButtonWithCaption(string label, string caption, Vector2 size = default, bool action = false)
        => ButtonWithCaption(label, caption, size, action, null);

    internal static bool ButtonWithCaption(string label, string caption, Vector2 size, bool action, float? previousWidth)
    {
        var originalVisible = label.Split("##", System.StringSplitOptions.None)[0];
        var visibleLabel = caption;
        var legacyWidth = previousWidth.HasValue ? System.Math.Max(1f, previousWidth.Value) :
            System.Math.Max(1f, System.Math.Min(size.X < 0 ? ImGui.GetContentRegionAvail().X : size.X > 0 ? size.X : ButtonWidth(label), ImGui.GetContentRegionAvail().X));
        var iconWidth = action ? 28f * Scale : 0;
        var oldWrapped = ImGui.CalcTextSize(originalVisible).X > Math.Max(1,legacyWidth-ImGui.GetStyle().FramePadding.X*2-iconWidth);
        using var controls = ImGui.GetStyle().FramePadding.Y == 0 || MaterialControls.Context == MaterialControlContext.Dense
            ? default(MaterialControls.ControlScope) : MaterialControls.Push(MaterialControlContext.Toolbar);
        var paddingY = ImGui.GetStyle().FramePadding.Y;
        using var lineHeight = MaterialText.PushLineHeight(visibleLabel);
        var textSize = MaterialText.Measure(visibleLabel);
        var naturalWidth = textSize.X+ImGui.GetStyle().FramePadding.X*2+iconWidth;
        var width = MaterialLayout.FitNextItemWidth(size.X,naturalWidth);
        var height = System.Math.Max(size.Y > 0 ? size.Y : MaterialControls.Metrics.Height,
            System.Math.Max(textSize.Y, action ? 20 * Scale : 0) + paddingY * 2);
        var emphasis = originalVisible is "Run" or "Run All" or "Resume" or "CPU campaign" or "Scan";
        if (emphasis)
        {
            var colors = MaterialTheme.Current.Colors;
            ImGui.PushStyleColor(ImGuiCol.Button, colors.PrimaryContainer);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, MaterialColor.Layer(colors.PrimaryContainer, colors.Primary, .16f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, MaterialColor.Layer(colors.PrimaryContainer, colors.Primary, .28f));
            ImGui.PushStyleColor(ImGuiCol.Border, colors.Primary);
        }
        ImGui.PushStyleColor(ImGuiCol.Text, Vector4.Zero);
        var clicked = ImGui.Button(oldWrapped ? "##Wrapped_" + label : label, new Vector2(width, height));
        ImGui.PopStyleColor();
        if (emphasis) ImGui.PopStyleColor(4);
        var position = ImGui.GetItemRectMin() + new Vector2(ImGui.GetStyle().FramePadding.X + iconWidth, (height - textSize.Y) / 2);
        if (action) VermaxionPresentation.ActionIcon(originalVisible, ImGui.GetItemRectMin() + new Vector2(ImGui.GetStyle().FramePadding.X, (height - 20 * Scale) / 2), 20 * Scale);
        MaterialText.AddText(ImGui.GetWindowDrawList(),ImGui.GetFont(), ImGui.GetFontSize(), position,
            ImGui.GetColorU32(ImGuiCol.Text), visibleLabel);
        return clicked;
    }

    internal static bool FullStopButton(Vector2 size = default)
        => FullStopButton(size, null);

    internal static bool FullStopButton(Vector2 size, float? previousWidth)
    {
        var red = VermaxionPresentation.Rgb(0xFF646C);
        using var stopStyle = new MaterialStyleScope();
        stopStyle.Color(ImGuiCol.Button, MaterialColor.Layer(Window, red, .12f));
        stopStyle.Color(ImGuiCol.ButtonHovered, MaterialColor.Layer(Window, red, .24f));
        stopStyle.Color(ImGuiCol.ButtonActive, MaterialColor.Layer(Window, red, .32f));
        stopStyle.Color(ImGuiCol.Border, red);
        stopStyle.Color(ImGuiCol.Text, red);
        return Button("FULL STOP", size == default ? new Vector2(180f * Scale, 0) : size, true, previousWidth);
    }

    internal static void Status(string id, string text, Vector4 color, bool translate = true)
    {
        var display = translate ? UiText.T(text) : text;
        var insets = WrappedTextInsets(display);
        var width = System.MathF.Max(1, ImGui.GetContentRegionAvail().X - insets.Left - insets.Right);
        var limit = ImGui.GetTextLineHeightWithSpacing() * 3;
        if (MaterialText.Measure(display, false, width).Y > limit)
        {
            var visible = ImGui.BeginChild(id, new Vector2(0, limit), false);
            try { if (visible) WrappedText(color, display, translate: false); }
            finally { ImGui.EndChild(); }
        }
        else WrappedText(color, display, translate: false);
        if (ImGui.IsItemHovered()) MaterialText.SetTooltip(display);
    }

    internal static void SetNextItemWidth(float width)
        => ImGui.SetNextItemWidth(MaterialLayout.FitNextItemWidth(width,
            Math.Max(80*Scale,MaterialText.Measure("00000").X+ImGui.GetStyle().FramePadding.X*2)));

    internal static bool Checkbox(string label, ref bool value)
    {
        var text = UiText.T(label.Split("##", System.StringSplitOptions.None)[0]);
        if (ImGui.CalcTextSize(label.Split("##",2)[0]).X + 36f * Scale + ImGui.GetStyle().ItemInnerSpacing.X <= ImGui.GetContentRegionAvail().X)
            return UiGui.Checkbox(label, ref value);
        ImGui.BeginGroup();
        try
        {
        var changed = ImGui.Checkbox("##Wrapped_" + label, ref value);
        ImGui.SameLine();
        UiGui.TextUnformatted(text);
        if (ImGui.IsItemClicked()) { value = !value; changed = true; }
        return changed;
        }
        finally { ImGui.EndGroup(); }
    }

    private static string ControlLabel(string label)
    {
        var text = UiText.T(label.Split("##", System.StringSplitOptions.None)[0]);
        if (text.Length == 0) return label;
        MaterialText.Text(text);
        return "##Control_" + label;
    }

    internal static bool InputInt(string label, ref int value, int step = 1, int fastStep = 100)
    {
        var width = ImGui.CalcItemWidth();
        var id = ControlLabel(label);
        SetNextItemWidth(width);
        var roomForSteps = System.Math.Min(width, ImGui.GetContentRegionAvail().X) >=
            MaterialText.Measure("00000").X + ImGui.GetStyle().FramePadding.X * 2 + ImGui.GetFrameHeight() * 2 + ImGui.GetStyle().ItemInnerSpacing.X * 2;
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
        var open = MaterialText.BeginCombo(id, UiText.T(preview));
        if (!open && ImGui.IsItemHovered()) MaterialText.SetTooltip(UiText.T(preview));
        return open;
    }

    internal static bool Combo(string label, ref int index, string items)
    {
        var width = ImGui.CalcItemWidth();
        var id = ControlLabel(label);
        SetNextItemWidth(width);
        return UiGui.Combo(id, ref index, items.Split("\0").Where(item => item.Length > 0).ToArray(), items.Split("\0").Count(item => item.Length > 0));
    }

    internal static bool Combo(string label, ref int index, string[] items, int count)
    {
        var width = ImGui.CalcItemWidth();
        var id = ControlLabel(label);
        SetNextItemWidth(width);
        return UiGui.Combo(id, ref index, items, count);
    }

    internal static bool InputText(string label, ref string value, int maxLength)
    {
        var width = ImGui.CalcItemWidth();
        var id = ControlLabel(label);
        SetNextItemWidth(width);
        using var height = MaterialText.PushLineHeight(value);
        return MaterialShapedInput.SingleLine(id, "", ref value, maxLength);
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
        public const string AutoWidthMainTaskColumns = "Automatically give Task and State & dependencies equal space and reserve 180 scaled pixels for Actions. Turn this off to use the manual Favorite/Task layout and drag its dividers; Dalamud saves the manual widths for each dashboard view.";
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
