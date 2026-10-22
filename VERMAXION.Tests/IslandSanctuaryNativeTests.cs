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
    [InlineData(false, 1060)]
    [InlineData(true, 1060)]
    [InlineData(false, 520)]
    [InlineData(true, 520)]
    public void ActualNativeViewsKeepSettingsEditableAndExecutionDisabled(bool compact, int width)
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
            var theme = new MaterialTheme(new Vector3(.1f, .6f, .7f), compact ? MaterialDensity.Compact : MaterialDensity.Standard);
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
            AethertekUI.Tests.SoftwarePreview.Save(Path.Combine(previewDirectory, $"settings-{compact}-{width}.png"), preview);
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
            AethertekUI.Tests.SoftwarePreview.Save(Path.Combine(previewDirectory, $"run-{compact}-{width}.png"), preview);
        }
        finally { ImGui.DestroyContext(context); }
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
