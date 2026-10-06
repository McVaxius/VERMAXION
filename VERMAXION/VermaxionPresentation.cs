using System.Numerics;
using AethertekUI;
using Dalamud.Bindings.ImGui;

namespace VERMAXION;

internal enum UiFontRole { Body, BodyStrong, Title, PluginName, Counter, Action, CompactTitle }

internal static class VermaxionPresentation
{
    // VERMAXION-review-v1 content: 976 wide, 48 header, 76 status, 50 actions, 16 gaps.
    internal const float MainHeader = 48, MainStatus = 76, MainAction = 50, MainGap = 16, MainRow = 38;
    internal const uint ReferenceAccent = 0x00C6DF;
    internal static readonly float[] FontSizes = [16, 16, 38, 20, 22, 18, 26];
    internal static readonly string[] FontFiles = ["segoeui.ttf", "seguisb.ttf", "segoeuib.ttf", "seguisb.ttf", "seguisb.ttf", "seguisb.ttf", "segoeuib.ttf"];
    internal static float AtlasHeight(UiFontRole role) => FontSizes[(int)role] * 4 / 3;
    internal static Vector4 Rgb(uint rgb) => new(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1);
    internal static readonly Vector4 Ready = Rgb(0x7DCF6E), Pending = Rgb(0xE8B86C), Error = Rgb(0xEE7788);
    internal static MaterialControlMetrics Controls(float height, float icon = 20)
    {
        var s = MaterialTheme.Metrics.Scale;
        return new() { Height = height * s, Padding = new(12 * s, Math.Max(0, (height * s - ImGui.GetTextLineHeight()) * .5f)),
            Gap = 8 * s, IconSize = icon * s, Rounding = 4 * s, ItemSpacing = new(10 * s, 4 * s), CellPadding = new(16 * s, 8 * s) };
    }
    internal static MaterialTheme Theme(uint accent)
    {
        accent &= 0xFFFFFF;
        var reference = MaterialColor.LabToLch(MaterialColor.SrgbToOklab(new Vector3(Rgb(ReferenceAccent).X, Rgb(ReferenceAccent).Y, Rgb(ReferenceAccent).Z)));
        var selected = Rgb(accent);
        var seed = MaterialColor.LabToLch(MaterialColor.SrgbToOklab(new(selected.X, selected.Y, selected.Z)));
        var hueShift = seed.Y < .001f ? 0 : seed.Z - reference.Z;
        var chromaScale = seed.Y < .001f ? 0 : seed.Y / reference.Y;
        Vector4 Relative(uint rgb)
        {
            var color = Rgb(rgb);
            if (accent == ReferenceAccent) return color;
            var lch = MaterialColor.LabToLch(MaterialColor.SrgbToOklab(new(color.X, color.Y, color.Z)));
            return new(MaterialColor.GamutMap(lch.X, lch.Y * chromaScale, lch.Z + hueShift), 1);
        }
        var palette = new OklchPaletteGenerator().Generate(new(selected.X, selected.Y, selected.Z));
        var background = Relative(0x0E2029);
        var foreground = Relative(0xF3F5F7);
        var primary = Relative(ReferenceAccent);
        var colors = new MaterialColorScheme(palette)
        {
            Background = background, OnBackground = foreground, Surface = Relative(0x152D38), OnSurface = foreground,
            SurfaceContainerLowest = Relative(0x0D1E28), SurfaceContainerLow = Relative(0x152D38),
            SurfaceContainer = Relative(0x19313C), SurfaceContainerHigh = Relative(0x203944), SurfaceContainerHighest = Relative(0x27444F),
            SurfaceVariant = Relative(0x2C4956), OnSurfaceVariant = Relative(0xB8C8D0),
            Outline = Relative(0x355563), OutlineVariant = Relative(0x2C4956),
            Primary = primary, OnPrimary = MaterialColor.Contrast(primary, background) >= MaterialColor.Contrast(primary, foreground) ? background : foreground,
            PrimaryContainer = Relative(0x075563), OnPrimaryContainer = foreground,
            Secondary = Relative(0x78D3DE), OnSecondary = background, SecondaryContainer = Relative(0x203944), OnSecondaryContainer = foreground,
            Tertiary = Relative(0x82BCD9), OnTertiary = background, TertiaryContainer = Relative(0x244350), OnTertiaryContainer = foreground,
            InverseSurface = foreground, InverseOnSurface = background, InversePrimary = Relative(0x075563),
        };
        return new(colors) { SurfaceOpacity = 1 };
    }
    internal static void Surface(Vector2 min, Vector2 max, bool raised = false)
    {
        var c = MaterialTheme.Current.Colors;
        MaterialCanvas.Surface(min, max, raised ? c.SurfaceContainerHigh : c.Surface, c.Background, 4 * MaterialTheme.Metrics.Scale);
        Dalamud.Bindings.ImGui.ImGui.GetWindowDrawList().AddRect(min, max, MaterialCanvas.Color(c.OutlineVariant), 4 * MaterialTheme.Metrics.Scale);
    }

    internal static void Logo(Vector2 origin, Vector2 size)
    {
        var draw = ImGui.GetWindowDrawList();
        var ink = MaterialCanvas.Color(MaterialTheme.Current.Colors.Primary);
        Vector2 P(float x,float y) => origin + new Vector2(x * size.X,y * size.Y);
        draw.AddQuadFilled(P(.02f,.05f),P(.25f,.05f),P(.56f,.68f),P(.43f,.91f),ink);
        draw.AddQuadFilled(P(.43f,.91f),P(.59f,.91f),P(.98f,.05f),P(.76f,.05f),ink);
    }

    internal static void ActionIcon(string label, Vector2 origin, float size)
    {
        var color = ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        color.W *= ImGui.GetStyle().Alpha;
        if (label is "Verminion" or "Fish collection")
        {
            var draw = ImGui.GetWindowDrawList();var ink = MaterialCanvas.Color(color);
            Vector2 P(float x,float y) => origin + new Vector2(x,y)*size;
            if (label == "Verminion")
            {
                draw.AddCircleFilled(P(.5f,.64f),size*.27f,ink,16);
                for (var t=0;t<4;t++) draw.AddCircleFilled(P(.15f+t*.23f,t is 0 or 3 ? .3f : .17f),size*.12f,ink,12);
            }
            else
            {
                draw.AddTriangleFilled(P(.65f,.5f),P(.96f,.15f),P(.96f,.85f),ink);
                draw.AddQuadFilled(P(.02f,.5f),P(.35f,.18f),P(.7f,.5f),P(.35f,.82f),ink);
                draw.AddCircleFilled(P(.22f,.44f),size*.04f,MaterialCanvas.Color(MaterialTheme.Current.Colors.Surface),12);
            }
        }
        else MaterialIcons.Draw(label switch { "Run All"=>MaterialIcon.Play,"Settings"=>MaterialIcon.Settings,"FULL STOP"=>MaterialIcon.Stop,_=>MaterialIcon.None },origin,size,color);
    }
}
