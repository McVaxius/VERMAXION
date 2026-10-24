extern alias live;
extern alias host;

using System;
using System.Linq.Expressions;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using AethertekUI;
using Xunit;
using ImGui = host::Dalamud.Bindings.ImGui.ImGui;
using ImGuiP = host::Dalamud.Bindings.ImGui.ImGuiP;
using ImGuiWindowFlags = host::Dalamud.Bindings.ImGui.ImGuiWindowFlags;
using ImTextureID = host::Dalamud.Bindings.ImGui.ImTextureID;

namespace VERMAXION.Tests
{

[Collection("Beastmaster production host")]
public sealed unsafe class IslandSanctuaryNativeTests
{
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Assembly Production = typeof(live::VERMAXION.Plugin).Assembly;

    [Theory]
    [InlineData(false, 1060, false)]
    [InlineData(true, 1060, false)]
    [InlineData(false, 520, false)]
    [InlineData(true, 520, false)]
    [InlineData(false, 1060, true)]
    [InlineData(true, 1060, true)]
    [InlineData(false, 520, true)]
    [InlineData(true, 520, true)]
    public void ActualNativeViewsKeepSettingsEditableAndExecutionDisabled(bool compact, int width, bool lcars)
    {
        var context = ImGui.CreateContext();
        var textType = Production.GetType("VERMAXION.UiText", true)!;
        var constructor = textType.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)[0];
        var callbackType = constructor.GetParameters()[1].ParameterType;
        var callback = Expression.Lambda(callbackType, Expression.Constant(Noop.Instance, typeof(IDisposable)),
            Expression.Parameter(callbackType.GenericTypeArguments[0])).Compile();
        using var text = (IDisposable)constructor.Invoke(["en", callback]);
        using var entered = (IDisposable)textType.GetMethod("Enter", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(text, null)!;
        try
        {
            var io = ImGui.GetIO();
            io.IniFilename = null; io.LogFilename = null;
            io.DisplaySize = new(width, 1900); io.DeltaTime = 1f / 60;
            io.Fonts.AddFontDefault();
            byte* pixels = null; int atlasWidth = 0, atlasHeight = 0;
            io.Fonts.GetTexDataAsRGBA32(0, &pixels, &atlasWidth, &atlasHeight);
            io.Fonts.SetTexID(0, new ImTextureID((nint)1));
            var config = new live::VERMAXION.Models.CharacterConfig();
            var theme = (MaterialTheme)Production.GetType("VERMAXION.VermaxionPresentation", true)!
                .GetMethod("Theme", Static, null, [typeof(uint), typeof(MaterialColorSettings)], null)!
                .Invoke(null, [0x00C6DFu, new MaterialColorSettings { Mode = lcars ? MaterialColorMode.Lcars : MaterialColorMode.Auto }])!;
            theme.Density = compact ? MaterialDensity.Compact : MaterialDensity.Standard;
            var constants = Production.GetType("VERMAXION.UIConstants", true)!;
            var view = Production.GetType("VERMAXION.Windows.IslandSanctuaryView", true)!;
            var settings = view.GetMethod("DrawSettings", Static)!;
            uint enableId = 0;
            bool changed = false;
            void Frame(bool run)
            {
                ImGui.NewFrame();
                using (MaterialTheme.Push(theme))
                {
                    constants.GetMethod("PushStyle", Static)!.Invoke(null, [compact]);
                    ImGui.SetNextWindowPos(Vector2.Zero); ImGui.SetNextWindowSize(new(width, 1850));
                    ImGui.Begin("Island native", ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoTitleBar);
                    ImGui.PushID("IslandSettingsCards"); ImGui.PushID("IslandCharacter");
                    enableId = ImGui.GetID("Enable Island routine");
                    ImGui.PopID(); ImGui.PopID();
                    view.GetMethod("DrawProviderStatus", Static)!.Invoke(null, null);
                    if (run) view.GetMethod("DrawRun", Static)!.Invoke(null, [config]);
                    else changed = (bool)settings.Invoke(null, [config])!;
                    view.GetMethod("DrawActions", Static)!.Invoke(null, null);
                    var bounds = ImGuiP.GetCurrentWindow().InnerRect;
                    Assert.True(ImGuiP.GetCurrentWindow().ContentSize.X <= bounds.Max.X - bounds.Min.X + 1,
                        "Native Island content must fit the visible width.");
                    ImGui.End();
                    constants.GetMethod("PopStyle", Static)!.Invoke(null, null);
                }
                ImGui.Render();
                Assert.True(ImGui.GetDrawData().TotalVtxCount > 0);
                Assert.Equal(0, context.Handle->ColorStack.Size);
                Assert.Equal(0, context.Handle->StyleVarStack.Size);
            }
            Frame(false); Frame(false);
            var preview = new AethertekUI.Tests.NativeTestContext();
            var previewDirectory = Path.Combine(Path.GetTempPath(), "vmx-island-20261022", "native");
            AethertekUI.Tests.SoftwarePreview.Save(Path.Combine(previewDirectory, $"settings-{lcars}-{compact}-{width}.png"), preview);
            Assert.False(config.EnableIslandSanctuary);
            ImGuiP.ActivateItem(enableId);
            Frame(false);
            Assert.True(changed);
            Assert.True(config.EnableIslandSanctuary);
            var before = JsonSerializer.Serialize(config);
            foreach (var action in new[] { "Run##Island", "Stop##Island", "Open Calypso##Island" })
            {
                uint id = 0;
                ImGui.NewFrame();
                ImGui.Begin("Island native", ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoTitleBar);
                id = ImGui.GetID(action); ImGui.End(); ImGui.Render();
                ImGuiP.ActivateItem(id);
                Frame(true);
                Assert.Equal(before, JsonSerializer.Serialize(config));
            }
            Frame(true); Frame(true);
            AethertekUI.Tests.SoftwarePreview.Save(Path.Combine(previewDirectory, $"run-{lcars}-{compact}-{width}.png"), preview);
        }
        finally { ImGui.DestroyContext(context); }
    }

    [Theory]
    [InlineData(false, 1f)]
    [InlineData(true, 1.5f)]
    public void ActualLcarsColourAndSoundCallbacksRetainNativeActionsAndPickerValues(bool compact, float scale)
    {
        var context = ImGui.CreateContext();
        var pluginType = typeof(live::VERMAXION.Plugin);
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        var interfaceProperty = pluginType.GetProperty("PluginInterface", Static)!;
        var previousInterface = interfaceProperty.GetValue(null);
        var textType = Production.GetType("VERMAXION.UiText", true)!;
        var constructor = textType.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)[0];
        var callbackType = constructor.GetParameters()[1].ParameterType;
        var callback = Expression.Lambda(callbackType, Expression.Constant(Noop.Instance, typeof(IDisposable)),
            Expression.Parameter(callbackType.GenericTypeArguments[0])).Compile();
        using var text = (IDisposable)constructor.Invoke(["en", callback]);
        using var entered = (IDisposable)textType.GetMethod("Enter", fields)!.Invoke(text, null)!;
        try
        {
            var saves = 0;
            var pluginInterface = DispatchProxy.Create<host::Dalamud.Plugin.IDalamudPluginInterface, QuestionableIntegrationTests.HostProxy>();
            ((QuestionableIntegrationTests.HostProxy)(object)pluginInterface).Handler = (method, arguments) =>
            {
                if (method.Name == "SavePluginConfig") { saves++; return null; }
                throw new InvalidOperationException("Unexpected offline host call: " + method.Name);
            };
            interfaceProperty.SetValue(null, pluginInterface);
            var plugin = (live::VERMAXION.Plugin)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(pluginType);
            var configuration = new live::VERMAXION.Configuration
            {
                CompactUi = compact,
                UiColors = new() { Mode = MaterialColorMode.Picker, BackgroundRgb = 0x123456, ControlsRgb = 0x654321, TextRgb = 0xEDEDED },
            };
            pluginType.GetField("<Configuration>k__BackingField", fields)!.SetValue(plugin, configuration);
            pluginType.GetField("colorDraft", fields)!.SetValue(plugin, configuration.UiColors);
            pluginType.GetField("accentDraft", fields)!.SetValue(plugin, MaterialColorSettings.Rgb(configuration.UiAccentRgb));
            foreach (var name in new[] { "appearanceTitleBars", "soundTitleButtons" })
            {
                var field = pluginType.GetField(name, fields)!;
                field.SetValue(plugin, Activator.CreateInstance(field.FieldType));
            }
            var windows = new host::Dalamud.Interface.Windowing.WindowSystem("VMX LCARS fixture");
            pluginType.GetField("WindowSystem", fields)!.SetValue(plugin, windows);
            using var main = new live::VERMAXION.Windows.MainWindow(plugin);
            var utility = (host::Dalamud.Interface.Windowing.Window)Activator.CreateInstance(
                Production.GetType("VERMAXION.Windows.VerminionWindow", true)!, plugin)!;
            windows.AddWindow(main); windows.AddWindow(utility);
            var original = main.TitleBarButtons.ToArray();
            var prepare = pluginType.GetMethod("PrepareAppearanceTitleBars", fields)!;
            var colour = pluginType.GetMethod("DrawTitleColour", fields)!;
            var sound = pluginType.GetMethod("DrawLcarsSounds", fields)!;
            var quick = pluginType.GetMethod("DrawLcarsQuickPreferences", fields)!;
            var drawQuick = false;
            var languages = ((ValueTuple<string, string>[])textType.GetField("Languages", Static)!.GetValue(null)!).Select(language => language.Item1);
            string[] captions = ["LCARS", "LCARS panels, bars and controls", "LCARS UI sounds", "UI sounds", "Preview sound",
                "Custom UI tones. The titlebar speaker toggles the same setting.", "Select LCARS to use UI sounds.",
                "Turn on UI sounds to preview.", "Audio output is unavailable.", "UI sounds muted", "UI sounds on"];
            foreach (var language in languages)
            {
                var resources = new System.Resources.ResourceManager("VERMAXION.Localization.Strings_" + language.Replace('-', '_'), Production)
                    .GetResourceSet(System.Globalization.CultureInfo.InvariantCulture, true, false)!;
                foreach (var caption in captions) Assert.False(string.IsNullOrWhiteSpace(resources.GetString(caption)), language + ": " + caption);
            }
            var presentation = Production.GetType("VERMAXION.VermaxionPresentation", true)!;
            var themeFactory = presentation.GetMethod("Theme", Static, null, [typeof(uint), typeof(MaterialColorSettings)], null)!;
            var constants = Production.GetType("VERMAXION.UIConstants", true)!;
            var feedback = new System.Collections.Generic.List<MaterialUiSoundCue>();
            var io = ImGui.GetIO(); io.IniFilename = null; io.LogFilename = null;
            io.DisplaySize = new(1400 * scale, 1600 * scale); io.DeltaTime = 1f / 60; io.FontGlobalScale = scale;
            io.Fonts.AddFontDefault();
            byte* pixels = null; int width = 0, height = 0;
            io.Fonts.GetTexDataAsRGBA32(0, &pixels, &width, &height);
            io.Fonts.SetTexID(0, new ImTextureID((nint)1));
            host::Dalamud.Bindings.ImGui.ImDrawListPtr list = default;
            void Frame()
            {
                ImGui.NewFrame();
                var theme = (MaterialTheme)themeFactory.Invoke(null, [configuration.UiAccentRgb, configuration.UiColors])!;
                theme.Density = theme.IsLcars && compact ? MaterialDensity.Compact : MaterialDensity.Standard;
                theme.Feedback = feedback.Add;
                pluginType.GetField("uiTheme", fields)!.SetValue(plugin, theme);
                using (MaterialTheme.Push(theme, scale, MaterialStyleMode.ColorsOnly))
                {
                    constants.GetMethod("PushStyle", Static)!.Invoke(null, [compact]);
                    try
                    {
                        prepare.Invoke(plugin, null);
                        ImGui.SetNextWindowPos(new(20, 20)); ImGui.SetNextWindowSize(new(650 * scale, 1400 * scale));
                        ImGui.Begin("VMX actual appearance callback", ImGuiWindowFlags.NoSavedSettings);
                        try
                        {
                            if (drawQuick) quick.Invoke(plugin, null);
                            else { colour.Invoke(plugin, null); sound.Invoke(plugin, null); }
                            list = ImGui.GetWindowDrawList();
                        }
                        finally { ImGui.End(); }
                    }
                    finally { constants.GetMethod("PopStyle", Static)!.Invoke(null, null); }
                }
                ImGui.Render();
                Assert.Equal(0, context.Handle->ColorStack.Size);
                Assert.Equal(0, context.Handle->StyleVarStack.Size);
            }
            Vector2 Centre(string caption)
            {
                var lastMatch = caption == "UI sounds";
                caption = caption.Replace(" ", string.Empty);
                Vector2? point = null;
                for (var index = 0; index + caption.Length * 4 <= list.VtxBuffer.Size; index++)
                {
                    var matched = true;
                    for (var character = 0; character < caption.Length; character++)
                    {
                        var glyph = ImGui.FindGlyphNoFallback(ImGui.GetFont(), caption[character]);
                        if (list.VtxBuffer[index + character * 4].Uv != new Vector2(glyph.Handle->U0, glyph.Handle->V0)
                            || list.VtxBuffer[index + character * 4 + 2].Uv != new Vector2(glyph.Handle->U1, glyph.Handle->V1))
                        { matched = false; break; }
                    }
                    if (matched)
                    {
                        point = (list.VtxBuffer[index].Pos + list.VtxBuffer[index + caption.Length * 4 - 2].Pos) / 2;
                        if (!lastMatch) return point.Value;
                    }
                }
                return point ?? throw new InvalidOperationException("Actual callback did not show " + caption);
            }
            void Click(string caption)
            {
                var point = Centre(caption);
                io.AddMousePosEvent(point.X, point.Y); Frame();
                io.AddMouseButtonEvent(0, true); Frame();
                io.AddMouseButtonEvent(0, false); Frame();
            }
            Frame(); Frame();
            var picker = configuration.UiColors;
            Click("LCARS"); Frame();
            Assert.Equal(1, saves);
            Assert.Equal(picker with { Mode = MaterialColorMode.Lcars }, configuration.UiColors);
            Assert.True(configuration.UiLcarsSoundsMuted);
            Assert.Equal(original, main.TitleBarButtons.Take(original.Length));
            Assert.Equal(original.Length + 4, main.TitleBarButtons.Count);
            Assert.Equal(6, utility.TitleBarButtons.Count);
            var speaker = main.TitleBarButtons.Single(button => button.Icon == host::Dalamud.Interface.FontAwesomeIcon.VolumeMute);
            feedback.Clear(); Click("Preview sound"); Assert.Empty(feedback);
            speaker.Click(host::Dalamud.Bindings.ImGui.ImGuiMouseButton.Right);
            Assert.Equal(1, saves);
            speaker.Click(host::Dalamud.Bindings.ImGui.ImGuiMouseButton.Left); Frame();
            Assert.Equal(2, saves); Assert.False(configuration.UiLcarsSoundsMuted);
            feedback.Clear(); Click("Preview sound");
            Assert.Equal(new[] { MaterialUiSoundCue.Preview }, feedback);
            Assert.Equal(2, saves);
            Click("UI sounds");
            Assert.True(configuration.UiLcarsSoundsMuted); Assert.Equal(3, saves);
            Click("Auto"); Frame();
            Assert.Equal(4, saves);
            Assert.Equal(picker with { Mode = MaterialColorMode.Auto }, configuration.UiColors);
            Assert.DoesNotContain(speaker, main.TitleBarButtons);
            Assert.Equal(original.Length + 3, main.TitleBarButtons.Count);
            Assert.Equal(5, utility.TitleBarButtons.Count);
            drawQuick = true; Frame(); Frame();
            Click("LCARS"); Frame();
            Assert.Equal(picker with { Mode = MaterialColorMode.Lcars }, configuration.UiColors);
            Assert.Equal(5, saves);
            Click("UI sounds");
            Assert.False(configuration.UiLcarsSoundsMuted); Assert.Equal(6, saves);
            Click("Picker"); Frame();
            Assert.Equal(picker, configuration.UiColors); Assert.Equal(7, saves);
            Click("UI sounds");
            Assert.False(configuration.UiLcarsSoundsMuted); Assert.Equal(7, saves);
        }
        finally { interfaceProperty.SetValue(null, previousInterface); ImGui.DestroyContext(context); }
    }

    private sealed class Noop : IDisposable
    {
        public static readonly Noop Instance = new();
        public void Dispose() { }
    }
}

}

// Adapt the existing shared diagnostic renderer to this test's current native atlas.
// No textures or services outside the fabricated native test frame are accessed.
namespace AethertekUI.Tests
{
    internal sealed unsafe class NativeTestContext
    {
        private nint pixels;
        private int width;
        private int height;
        internal void RefreshAtlas()
        {
            byte* data = null; int atlasWidth = 0, atlasHeight = 0;
            ImGui.GetIO().Fonts.GetTexDataAsRGBA32(0, &data, &atlasWidth, &atlasHeight);
            pixels = (nint)data; width = atlasWidth; height = atlasHeight;
        }
        internal SoftwarePreview.Texture Texture(ImTextureID id) => id.Handle == 1
            ? new(pixels, width, height) : throw new InvalidOperationException("Unexpected native fixture texture.");
    }
}
