extern alias live;
extern alias host;

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;
using System.Resources;
using AethertekUI;
using Xunit;
using ImGui = host::Dalamud.Bindings.ImGui.ImGui;
using ImGuiP = host::Dalamud.Bindings.ImGui.ImGuiP;
using ImGuiWindowFlags = host::Dalamud.Bindings.ImGui.ImGuiWindowFlags;
using ImTextureID = host::Dalamud.Bindings.ImGui.ImTextureID;
using Status = live::VERMAXION.Services.CofferOpeningStatus;

namespace VERMAXION.Tests;

[Collection("Beastmaster production host")]
public sealed unsafe class CofferOpeningNativeTests
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private static readonly Assembly Production = typeof(live::VERMAXION.Plugin).Assembly;

    [Fact]
    public void CompiledCofferMessagesExistInEveryUiLanguageWithMatchingFormatArguments()
    {
        string[] keys = ["Open coffers", "Open coffer stack", "Open stack",
            "Opens one selected stack; keeps at least one bag slot free.", "No coffers in inventory.",
            "Coffers opened: {0}/{1}", "Coffers are idle.", "Opening coffers.", "Waiting for coffer consumption.",
            "Coffer stack completed.", "Coffer opening stopped.", "Coffer inventory is unavailable.",
            "Selected item is not a supported coffer.", "Selected coffer stack is unavailable.",
            "Keep at least one free inventory slot.", "Coffer use was not confirmed; check inventory before starting again.",
            "Coffer inventory changed.", "Coffer item use is unavailable.", "Coffer item use was rejected.",
            "Coffer character changed.", "Another automation or activity owns the character."];
        foreach (var language in new[] { "en", "de", "fr", "es", "it", "ru", "ja", "ko", "zh_Hans", "vi", "pt_BR", "id", "pl", "tr", "hi" })
        {
            var resources = new ResourceManager("VERMAXION.Localization.Strings_" + language, Production);
            foreach (var key in keys)
            {
                var value = resources.GetString(key, CultureInfo.InvariantCulture);
                Assert.False(string.IsNullOrWhiteSpace(value));
                Assert.DoesNotContain('\ufffd', value!);
                if (language != "en") Assert.NotEqual(key, value);
                if (key.Contains("{0}")) Assert.Contains("2", string.Format(CultureInfo.InvariantCulture, value!, 2, 3));
            }
            resources.ReleaseAllResources();
        }
    }

    [Theory]
    [InlineData(false, 420, 1f)]
    [InlineData(true, 420, 1.5f)]
    [InlineData(false, 700, 1.5f)]
    [InlineData(true, 700, 1f)]
    public void ActualCofferControlsFitAndRespectStartStopAndBlockedStates(bool compact, int width, float scale)
    {
        var context = ImGui.CreateContext();
        var textType = Production.GetType("VERMAXION.UiText", true)!;
        var constructor = textType.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
        var callbackType = constructor.GetParameters()[1].ParameterType;
        var callback = Expression.Lambda(callbackType, Expression.Constant(Noop.Instance, typeof(IDisposable)),
            Expression.Parameter(callbackType.GenericTypeArguments[0])).Compile();
        using var text = (IDisposable)constructor.Invoke(["en", callback]);
        using var entered = (IDisposable)textType.GetMethod("Enter", Flags)!.Invoke(text, null)!;
        try
        {
            var io = ImGui.GetIO();
            io.IniFilename = null; io.LogFilename = null;
            io.DisplaySize = new(width * scale, 600 * scale); io.DeltaTime = 1f / 60; io.FontGlobalScale = scale;
            io.Fonts.AddFontDefault();
            byte* pixels = null; int atlasWidth = 0, atlasHeight = 0;
            io.Fonts.GetTexDataAsRGBA32(0, &pixels, &atlasWidth, &atlasHeight);
            io.Fonts.SetTexID(0, new ImTextureID((nint)1));
            var theme = new MaterialTheme(new Vector3(.2f, .7f, .6f), compact ? MaterialDensity.Compact : MaterialDensity.Standard);
            var constants = Production.GetType("VERMAXION.UIConstants", true)!;
            var view = Production.GetType("VERMAXION.Windows.CofferOpeningWindow", true)!.GetMethod("DrawContent", Flags)!;
            var stackType = Production.GetType("VERMAXION.Services.NativeCofferInventory+Stack", true)!;
            var stacks = Array.CreateInstance(stackType, 2);
            stacks.SetValue(Activator.CreateInstance(stackType, [52450u, "Sample coffer", 3]), 0);
            stacks.SetValue(Activator.CreateInstance(stackType, [32161u, "Other coffer", 1]), 1);
            var starts = 0; var stops = 0; uint chosen = 0;
            object?[] arguments = [new Status(), stacks, 0u, null, "",
                (Action<uint>)(itemId => { starts++; chosen = itemId; }), (Action)(() => stops++)];
            uint startId = 0, stopId = 0;
            void Frame()
            {
                ImGui.NewFrame();
                using (MaterialTheme.Push(theme, scale))
                {
                    constants.GetMethod("PushStyle", Flags)!.Invoke(null, [compact]);
                    ImGui.SetNextWindowPos(Vector2.Zero); ImGui.SetNextWindowSize(new(width * scale, 580 * scale));
                    ImGui.Begin("Coffer native", ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoTitleBar);
                    startId = ImGui.GetID("Open stack###CofferStart"); stopId = ImGui.GetID("Stop###CofferStop");
                    view.Invoke(null, arguments);
                    var window = ImGuiP.GetCurrentWindow();
                    Assert.True(window.ContentSize.X <= window.InnerRect.Max.X - window.InnerRect.Min.X + 1);
                    ImGui.End(); constants.GetMethod("PopStyle", Flags)!.Invoke(null, null);
                }
                ImGui.Render();
                Assert.True(ImGui.GetDrawData().TotalVtxCount > 0);
                Assert.Equal(0, context.Handle->ColorStack.Size);
                Assert.Equal(0, context.Handle->StyleVarStack.Size);
            }
            Frame(); Frame();
            Assert.Equal(52450u, (uint)arguments[2]!);
            ImGuiP.ActivateItem(startId); Frame();
            Assert.Equal(1, starts); Assert.Equal(52450u, chosen);
            ImGuiP.ActivateItem(stopId); Frame(); Assert.Equal(0, stops);
            arguments[0] = new Status { State = "Running", Summary = "Opening coffers.", Requested = 3, Opened = 1 };
            Frame(); ImGuiP.ActivateItem(startId); Frame(); Assert.Equal(1, starts);
            ImGuiP.ActivateItem(stopId); Frame(); Assert.Equal(1, stops);
            arguments[0] = new Status(); arguments[3] = "Another automation or activity owns the character.";
            Frame(); ImGuiP.ActivateItem(startId); Frame(); Assert.Equal(1, starts);
            var previews = Path.Combine(Path.GetTempPath(), "vmx-coffers-native");
            AethertekUI.Tests.SoftwarePreview.Save(Path.Combine(previews, $"coffers-{compact}-{width}-{scale}.png"),
                new AethertekUI.Tests.NativeTestContext());
            arguments[1] = Array.CreateInstance(stackType, 0); arguments[3] = null;
            Frame(); ImGuiP.ActivateItem(startId); Frame(); Assert.Equal(1, starts);
        }
        finally { ImGui.DestroyContext(context); }
    }

    private sealed class Noop : IDisposable
    {
        internal static readonly Noop Instance = new();
        public void Dispose() { }
    }
}
