using System.Numerics;
using AethertekUI;
using Dalamud.Bindings.ImGui;

namespace VERMAXION;

// Native widgets receive their original English labels/IDs. Only their visible label is painted in the selected locale.
// This also preserves English-derived helper IDs and existing saved window identities.
internal static class UiGui
{
    internal static void TextUnformatted(string text) => MaterialText.Text(UiText.T(text));
    internal static void TextWrapped(string text) => UIConstants.WrappedText(ImGui.GetStyle().Colors[(int)ImGuiCol.Text], text);
    internal static void TextDisabled(string text) => UIConstants.WrappedText(ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled], text);
    internal static void Text(string text) => MaterialText.Text(UiText.T(text));
    internal static void BulletText(string text) => MaterialText.BulletText(UiText.T(text));
    internal static void TextColored(Vector4 color, string text) => MaterialText.TextColored(color, UiText.T(text));

    private static void Label(string original,Vector2 position,Vector4 background,Vector4 foreground,Vector2? clip=null,string? display=null)
    {
        var visible=original.Split("##",2)[0];
        var translated=display ?? UiText.T(visible);
        if(translated==visible && !MaterialText.RequiresShaping(translated)) return;
        var dl=ImGui.GetWindowDrawList();
        var width=Math.Max(MaterialText.Measure(visible).X,MaterialText.Measure(translated).X);
        if(clip is { } max) dl.PushClipRect(position,max,true);
        try
        {
        dl.AddRectFilled(position,position+new Vector2(width,Math.Max(ImGui.GetTextLineHeight(), MaterialText.Measure(translated).Y)),ImGui.ColorConvertFloat4ToU32(background));
        foreground.W*=ImGui.GetStyle().Alpha;
        MaterialText.AddText(dl,position,ImGui.ColorConvertFloat4ToU32(foreground),translated);
        }
        finally { if(clip.HasValue) dl.PopClipRect(); }
    }
    internal static bool Button(string label,string? display=null)
    {
        var translated=display ?? UiText.T(label.Split("##",2)[0]);
        using var controls = ImGui.GetStyle().FramePadding.Y == 0 || MaterialControls.Context == MaterialControlContext.Dense
            ? default(MaterialControls.ControlScope) : MaterialControls.Push(MaterialControlContext.Toolbar);
        using var height = MaterialText.PushLineHeight(translated);
        var width=MaterialText.Measure(translated).X+2*ImGui.GetStyle().FramePadding.X;
        if(width>ImGui.GetContentRegionAvail().X && ImGui.GetCursorPosX()>ImGui.GetStyle().WindowPadding.X+1) ImGui.NewLine();
        var foreground=ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        ImGui.PushStyleColor(ImGuiCol.Text,Vector4.Zero);
        var clicked=ImGui.Button(label,new Vector2(width,0));
        ImGui.PopStyleColor();
        var min=ImGui.GetItemRectMin(); var max=ImGui.GetItemRectMax();
        foreground.W*=ImGui.GetStyle().Alpha;
        ImGui.GetWindowDrawList().PushClipRect(min,max,true);
        try
        {
        MaterialText.AddText(ImGui.GetWindowDrawList(),min+(max-min-MaterialText.Measure(translated))*.5f,ImGui.ColorConvertFloat4ToU32(foreground),translated);
        }
        finally { ImGui.GetWindowDrawList().PopClipRect(); }
        return clicked;
    }
    internal static bool SmallButton(string label,string? display=null)
    {
        // Native small buttons use the same ID and behavior with zero vertical padding.
        using var padding = new MaterialStyleScope();
        padding.Style(ImGuiStyleVar.FramePadding,new Vector2(ImGui.GetStyle().FramePadding.X,0));
        return Button(label,display);
    }
    internal static bool Checkbox(string label,ref bool value,string? display = null)
    {
        var visible=label.Split("##",2)[0];
        var translated=display ?? UiText.T(visible);
        using var height = MaterialText.PushLineHeight(translated);
        var foreground=ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        var gap=ImGui.GetStyle().ItemInnerSpacing;
        // Native Checkbox sizes its hit area from the original label. Adjust that size for the
        // translated ink while keeping the native widget and its original ID.
        ImGui.PushStyleVar(ImGuiStyleVar.ItemInnerSpacing,new Vector2(Math.Max(0,gap.X+MaterialText.Measure(translated).X-MaterialText.Measure(visible).X),gap.Y));
        ImGui.PushStyleColor(ImGuiCol.Text,Vector4.Zero);
        var changed=ImGui.Checkbox(label,ref value);
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
        var p=ImGui.GetItemRectMin()+new Vector2(ImGui.GetFrameHeight()+gap.X,ImGui.GetStyle().FramePadding.Y);
        foreground.W*=ImGui.GetStyle().Alpha;
        MaterialText.AddText(ImGui.GetWindowDrawList(),p,ImGui.ColorConvertFloat4ToU32(foreground),translated);
        return changed;
    }
    internal static bool Selectable(string original,bool selected=false,string? display=null)
    {
        var translated=display ?? UiText.T(original.Split("##",2)[0]);
        var origin=ImGui.GetCursorScreenPos();
        var width=ImGui.GetContentRegionAvail().X;
        var foreground=ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        ImGui.PushStyleColor(ImGuiCol.Text,Vector4.Zero);
        var height=Math.Max(ImGui.GetTextLineHeight(),MaterialText.Measure(translated).Y);
        var clicked=ImGui.Selectable(original,selected,ImGuiSelectableFlags.None,new Vector2(0,height));
        ImGui.PopStyleColor();
        foreground.W*=ImGui.GetStyle().Alpha;
        var dl=ImGui.GetWindowDrawList();
        dl.PushClipRect(origin,origin+new Vector2(width,height),true);
        try
        {
        MaterialText.AddText(dl,origin,ImGui.ColorConvertFloat4ToU32(foreground),translated);
        }
        finally { dl.PopClipRect(); }
        if(MaterialText.Measure(translated).X>width && ImGui.IsItemHovered()) MaterialText.SetTooltip(translated);
        return clicked;
    }
    private static float FitField(string label)
    {
        var remaining=ImGui.GetContentRegionAvail().X-MaterialText.Measure(UiText.T(label.Split("##",2)[0])).X-ImGui.GetStyle().ItemInnerSpacing.X;
        var width=Math.Max(80,Math.Min(ImGui.CalcItemWidth(),remaining));
        ImGui.SetNextItemWidth(width);
        return width;
    }
    private static void FieldLabel(string label,float width, Vector2? itemMin = null)
    {
        var min=itemMin ?? ImGui.GetItemRectMin();
        var p=min+new Vector2(width+ImGui.GetStyle().ItemInnerSpacing.X,ImGui.GetStyle().FramePadding.Y);
        var background=(ImGuiP.GetCurrentWindow().Flags & ImGuiWindowFlags.ChildWindow)!=0 ? ImGuiCol.ChildBg : ImGuiCol.WindowBg;
        Label(label,p,ImGui.GetStyle().Colors[(int)background],ImGui.GetStyle().Colors[(int)ImGuiCol.Text]);
        var translated=UiText.T(label.Split("##",2)[0]);
        if(MaterialText.RequiresShaping(translated))
        {
            var parent=ImGuiP.GetCurrentWindow();
            var right=p.X+MaterialText.Measure(translated).X;
            parent.DC.CursorMaxPos.X=Math.Max(parent.DC.CursorMaxPos.X,right);
            parent.DC.CursorPosPrevLine.X=right;
        }
    }
    internal static bool Combo(string label,ref int value,string[] options,int count)
    {
        using var height = MaterialText.PushLineHeight(UiText.T(label.Split("##",2)[0]),value>=0 && value<count?UiText.T(options[value]):"");
        var width=FitField(label);
        var origin=ImGui.GetCursorScreenPos();
        var changed=false;
        if(MaterialText.BeginCombo(label,value>=0 && value<count?UiText.T(options[value]):""))
        {
            try
            {
            for(var index=0;index<count;index++)
            {
                ImGui.PushID(index);
                try
                {
                    if(Selectable(options[index],value==index)) { changed=value!=index;value=index; }
                    if(value==index) ImGui.SetItemDefaultFocus();
                }
                finally { ImGui.PopID(); }
            }
            }
            finally { ImGui.EndCombo(); }
        }
        FieldLabel(label,width,origin); return changed;
    }
    internal static Vector2 ScaledTextSize(string text, float scale)
    {
        if (!MaterialText.RequiresShaping(text)) return MaterialText.Measure(text) * scale;
        var callerScale = ImGuiP.GetCurrentWindow().FontWindowScale;
        ImGui.SetWindowFontScale(callerScale * scale);
        try { return MaterialText.Measure(text); }
        finally { ImGui.SetWindowFontScale(callerScale); }
    }

    internal static void Title(string original,string translated)
        => TitleWithButtons(original, translated, null);

    internal static void TitleWithButtons(string original,string translated,Dalamud.Interface.Windowing.Window? owner)
    {
        var s=ImGui.GetStyle(); var size=ImGui.GetFontSize();var height=ImGui.GetFrameHeight();
        var flags=ImGuiP.GetCurrentWindow().Flags;
        var collapseOnLeft=(flags & (ImGuiWindowFlags.NoCollapse|ImGuiWindowFlags.Modal))==0 && s.WindowMenuButtonPosition==ImGuiDir.Left;
        var position=ImGui.GetWindowPos()+new Vector2(s.FramePadding.X+(collapseOnLeft?size+s.ItemInnerSpacing.X:0),s.FramePadding.Y);
        var originalWidth=MaterialText.Measure(original).X;
        using var font=UiText.Font(UiFontRole.Body);
        var translatedWidth=ScaledTextSize(translated,size/ImGui.GetFontSize()).X;
        var dl=ImGui.GetWindowDrawList();
        var reserved=size+s.FramePadding.X*2;
        if((flags & ImGuiWindowFlags.NoCollapse)==0 && s.WindowMenuButtonPosition==ImGuiDir.Right) reserved+=size+s.ItemInnerSpacing.X;
        if(owner is not null)
        {
            var buttons=owner.TitleBarButtons.Count(button=>!owner.IsClickthrough||button.AvailableClickthrough);
            if(owner.AllowPinning||owner.AllowClickthrough||owner.AllowBackgroundBlur) buttons++;
            reserved+=buttons*(size+s.ItemInnerSpacing.X);
        }
        dl.PushClipRect(ImGui.GetWindowPos(),ImGui.GetWindowPos()+new Vector2(Math.Max(0,ImGui.GetWindowSize().X-reserved),height),false);
        try
        {
        var bg=s.Colors[(int)(ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows)?ImGuiCol.TitleBgActive:ImGuiCol.TitleBg)];
        dl.AddRectFilled(position,position+new Vector2(Math.Max(originalWidth,translatedWidth),height-s.FramePadding.Y),ImGui.ColorConvertFloat4ToU32(bg));
        MaterialText.AddText(dl,ImGui.GetFont(),size,position,ImGui.ColorConvertFloat4ToU32(s.Colors[(int)ImGuiCol.Text]),translated);
        }
        finally { dl.PopClipRect(); }
    }
    internal static void TableHeadersRow(float height=0)
    {
        for(var column=0;column<ImGui.TableGetColumnCount();column++)
        {
            var translated=UiText.T(ImGui.TableGetColumnName(column));
            if(MaterialText.RequiresShaping(translated)) height=Math.Max(height,MaterialText.Measure(translated).Y+2*ImGui.GetStyle().CellPadding.Y);
        }
        ImGui.TableNextRow(ImGuiTableRowFlags.Headers,height);
        for(var index=0;index<ImGui.TableGetColumnCount();index++)
        {
            if(!ImGui.TableSetColumnIndex(index)) continue;
            var original=ImGui.TableGetColumnName(index);
            var position=ImGui.GetCursorScreenPos();
            var available=ImGui.GetContentRegionAvail().X;
            ImGui.TableHeader(original);
            Label(original,position,ImGui.GetStyle().Colors[(int)ImGuiCol.TableHeaderBg],ImGui.GetStyle().Colors[(int)ImGuiCol.Text], position + new Vector2(Math.Max(1, available), Math.Max(ImGui.GetTextLineHeight(), MaterialText.Measure(UiText.T(original)).Y)));
            var translated=UiText.T(original);
            if(translated!=original && MaterialText.Measure(translated).X>available-16*AethertekUI.MaterialTheme.Metrics.Scale && ImGui.IsItemHovered())
                MaterialText.SetTooltip(translated);
        }
    }

    internal static void SetTooltip(string text) => MaterialText.SetTooltip(UiText.T(text));
    internal static bool InputTextWithHint(string id, string hint, ref string value, int length)
    {
        using var height = MaterialText.PushLineHeight(value, UiText.T(hint));
        return MaterialShapedInput.SingleLine(id, UiText.T(hint), ref value, length);
    }
    internal static bool SearchInputTextWithHint(string id, string hint, ref string value, int length)
    {
        var padding = ImGui.GetStyle().FramePadding;
        var scale = MaterialTheme.Metrics.Scale;
        var iconSize = 18 * scale;
        var colors = MaterialTheme.Current.Colors;
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(padding.X + iconSize + 8 * scale, padding.Y));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, colors.SurfaceContainerLowest);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, colors.SurfaceContainerLow);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, colors.SurfaceContainerLow);
        ImGui.PushStyleColor(ImGuiCol.Border, colors.Primary);
        bool changed;
        try { changed = InputTextWithHint(id, hint, ref value, length); }
        finally { ImGui.PopStyleColor(4); ImGui.PopStyleVar(); }
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        MaterialIcons.Draw(MaterialIcon.Search, min + new Vector2(padding.X, (max.Y - min.Y - iconSize) * .5f), iconSize, colors.OnSurface);
        return changed;
    }
    internal static bool BeginTabItem(string label, ImGuiTabItemFlags flags = ImGuiTabItemFlags.None)
    {
        var visible=label.Split("##",2)[0]; var translated=UiText.T(visible);
        using var height = MaterialText.PushLineHeight(translated);
        var padding=ImGui.GetStyle().FramePadding;
        ImGui.SetNextItemWidth(MathF.Ceiling(Math.Max(MaterialText.Measure(translated).X, MaterialText.Measure(visible).X) + padding.X * 2));
        ImGui.PushStyleColor(ImGuiCol.Text, Vector4.Zero);
        var open=ImGui.BeginTabItem(label, flags);
        ImGui.PopStyleColor();
        var min=ImGui.GetItemRectMin(); var max=ImGui.GetItemRectMax();
        ImGui.GetWindowDrawList().PushClipRect(min,max,true);
        try
        {
        MaterialText.AddText(ImGui.GetWindowDrawList(),min+(max-min-MaterialText.Measure(translated))*.5f,ImGui.GetColorU32(ImGuiCol.Text),translated);
        }
        catch { if(open) ImGui.EndTabItem(); throw; }
        finally { ImGui.GetWindowDrawList().PopClipRect(); }
        return open;
    }
    internal static bool MenuItem(string label, string? shortcut = null, bool selected = false, bool enabled = true)
    {
        var visible=label.Split("##",2)[0]; var translated=UiText.T(visible);
        using var height = MaterialText.PushLineHeight(translated);
        ImGui.PushStyleColor(ImGuiCol.Text, Vector4.Zero);
        var clicked=ImGui.MenuItem(label, shortcut, selected, enabled);
        ImGui.PopStyleColor();
        var min=ImGui.GetItemRectMin();var max=ImGui.GetItemRectMax();
        ImGui.GetWindowDrawList().PushClipRect(min,max,true);
        try
        {
        MaterialText.AddText(ImGui.GetWindowDrawList(),min+ImGui.GetStyle().FramePadding,ImGui.GetColorU32(ImGuiCol.Text),translated);
        }
        finally { ImGui.GetWindowDrawList().PopClipRect(); }
        return clicked;
    }
    internal static bool Selectable(string original,bool selected,ImGuiSelectableFlags flags,Vector2 size)
    {
        var translated=UiText.T(original.Split("##",2)[0]);
        if(MaterialText.RequiresShaping(translated)) size.Y=Math.Max(size.Y,MaterialText.Measure(translated).Y);
        var foreground=ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        ImGui.PushStyleColor(ImGuiCol.Text,Vector4.Zero);
        var clicked=ImGui.Selectable(original,selected,flags,size);
        ImGui.PopStyleColor();
        var min=ImGui.GetItemRectMin();var max=ImGui.GetItemRectMax();
        foreground.W*=ImGui.GetStyle().Alpha;
        ImGui.GetWindowDrawList().PushClipRect(min,max,true);
        try
        {
        MaterialText.AddText(ImGui.GetWindowDrawList(),min,ImGui.ColorConvertFloat4ToU32(foreground),translated);
        }
        finally { ImGui.GetWindowDrawList().PopClipRect(); }
        return clicked;
    }

    internal static bool NativeSwitch(string original, ref bool value)
    {
        var scale=MaterialTheme.Metrics.Scale;var c=MaterialTheme.Current.Colors;
        var caption=UiText.T(value?"On":"Off");var textSize=MaterialText.Measure(caption);
        var height=Math.Max(26*scale,textSize.Y);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(0,Math.Max(0,(height-ImGui.GetTextLineHeight())/2)));
        ImGui.PushStyleColor(ImGuiCol.Text,Vector4.Zero); ImGui.PushStyleColor(ImGuiCol.CheckMark,Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.FrameBg,Vector4.Zero); ImGui.PushStyleColor(ImGuiCol.FrameBgHovered,Vector4.Zero); ImGui.PushStyleColor(ImGuiCol.FrameBgActive,Vector4.Zero);
        var changed=ImGui.Checkbox(original,ref value);
        ImGui.PopStyleColor(5);ImGui.PopStyleVar();
        var min=ImGui.GetItemRectMin();var draw=ImGui.GetWindowDrawList();
        var fill=value?c.Primary:c.SurfaceContainerHigh;
        if(ImGui.IsItemHovered()) fill=MaterialColor.Layer(fill,c.OnSurface,.08f);
        if(ImGui.IsItemActive()) fill=MaterialColor.Layer(fill,c.OnSurface,.12f);
        draw.AddRectFilled(min,min+new Vector2(44,26)*scale,MaterialCanvas.Color(fill),13*scale);
        draw.AddRect(min,min+new Vector2(44,26)*scale,MaterialCanvas.Color(ImGui.IsItemFocused()?c.Primary:c.Outline),13*scale);
        draw.AddCircleFilled(min+new Vector2(value?31:13,13)*scale,10*scale,MaterialCanvas.Color(c.OnSurface),24);
        MaterialText.AddText(draw,min+new Vector2(56*scale,(height-textSize.Y)/2),MaterialCanvas.Color(c.OnSurface),caption);
        return changed;
    }

    internal static bool RadioButton(string original, bool active)
    {
        var visible=original.Split("##",2)[0];var translated=UiText.T(visible);
        using var height = MaterialText.PushLineHeight(translated);
        var gap=ImGui.GetStyle().ItemInnerSpacing;
        ImGui.PushStyleVar(ImGuiStyleVar.ItemInnerSpacing,new Vector2(gap.X+Math.Max(0,MaterialText.Measure(translated).X-MaterialText.Measure(visible).X),gap.Y));
        ImGui.PushStyleColor(ImGuiCol.Text,Vector4.Zero);
        var changed=ImGui.RadioButton(original,active);
        ImGui.PopStyleColor();ImGui.PopStyleVar();
        var min=ImGui.GetItemRectMin()+new Vector2(ImGui.GetFrameHeight()+gap.X,ImGui.GetStyle().FramePadding.Y);
        MaterialText.AddText(ImGui.GetWindowDrawList(),min,ImGui.GetColorU32(ImGuiCol.Text),translated);
        return changed;
    }
    internal static bool SliderInt(string original, ref int value, int min, int max)
    {
        using var height = MaterialText.PushLineHeight(UiText.T(original.Split("##",2)[0]));
        var width=FitField(original);
        var changed=ImGui.SliderInt(original,ref value,min,max);
        FieldLabel(original,width);return changed;
    }
    internal static bool AppearanceInputInt(string label, ref int value)
    {
        var visible = label.Split("##", 2)[0]; var nativeLabel = UiText.T(visible) + label[visible.Length..];
        if (!MaterialText.RequiresShaping(nativeLabel)) return ImGui.InputInt(nativeLabel, ref value);
        using var height = MaterialText.PushLineHeight(UiText.T(visible));
        var origin = ImGui.GetCursorScreenPos(); var width = ImGui.CalcItemWidth();
        var drawing = ImGui.GetWindowDrawList(); var parent = ImGuiP.GetCurrentWindow(); var previousMax = parent.DC.CursorMaxPos;
        drawing.PushClipRect(new Vector2(origin.X, parent.Pos.Y), new Vector2(origin.X + width, parent.Pos.Y + parent.Size.Y), true);
        bool changed;
        try { changed = ImGui.InputInt(label, ref value); }
        finally { drawing.PopClipRect(); }
        AppearanceFieldLabel(label, origin, width, drawing, parent, previousMax);
        return changed;
    }
    private static void AppearanceFieldLabel(string label, Vector2 origin, float width, ImDrawListPtr drawing, ImGuiWindowPtr parent, Vector2 previousMax)
    {
        var translated = UiText.T(label.Split("##", 2)[0]);
        var position = origin + new Vector2(width + ImGui.GetStyle().ItemInnerSpacing.X, ImGui.GetStyle().FramePadding.Y);
        MaterialText.AddText(drawing, position, ImGui.GetColorU32(ImGuiCol.Text), translated);
        var right = position.X + MaterialText.Measure(translated).X;
        parent.DC.CursorMaxPos = new Vector2(Math.Max(previousMax.X, right), parent.DC.CursorMaxPos.Y);
        parent.DC.CursorPosPrevLine = new Vector2(right, parent.DC.CursorPosPrevLine.Y);
    }
    private static unsafe void ModalWidth(string original)
    {
        var scale = MaterialTheme.Metrics.Scale;
        var display = ImGui.GetIO().DisplaySize;
        var width = Math.Max(1, Math.Min(560 * scale, display.X - 40 * scale));
        var popup = ImGuiP.FindWindowByName(original);
        var top = popup.Handle == null ? 40 * scale : Math.Max(40 * scale, popup.Pos.Y);
        var height = Math.Max(1, display.Y - top - 40 * scale);
        ImGui.SetNextWindowSize(new Vector2(width, 0), ImGuiCond.Always);
        ImGui.SetNextWindowSizeConstraints(new Vector2(width, 0), new Vector2(width, height));
    }
    internal static bool BeginPopupModal(string original, ImGuiWindowFlags flags)
    {
        ModalWidth(original);var open=ImGui.BeginPopupModal(original,flags);
        try { if(open) Title(original.Split("##",2)[0],UiText.T(original.Split("##",2)[0])); }
        catch { if(open) ImGui.EndPopup(); throw; }
        return open;
    }
    internal static bool BeginPopupModal(string original, ref bool visible, ImGuiWindowFlags flags)
    {
        ModalWidth(original);var open=ImGui.BeginPopupModal(original,ref visible,flags);
        try { if(open) Title(original.Split("##",2)[0],UiText.T(original.Split("##",2)[0])); }
        catch { if(open) ImGui.EndPopup(); throw; }
        return open;
    }
}
