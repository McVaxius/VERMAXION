using System;
using System.Numerics;
using AethertekUI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using VERMAXION.Beastmaster;

namespace VERMAXION.Windows;

internal sealed class BeastmasterMiniWindow : Window
{
    private readonly Plugin plugin;
    private readonly AethertekUI.Dalamud.MaterialWindowMotion motion = new();
    private bool materialBackdropStylePushed;
    private readonly AethertekUI.Dalamud.MaterialWindowLock movementLock = new();
    private readonly BeastmasterSplashRotation splashes = new();

    internal BeastmasterMiniWindow(Plugin plugin)
        : base("VERMAXION · Beastmaster Mini##VermaxionBeastmasterMini", ImGuiWindowFlags.None)
    {
        this.plugin = plugin;
        Size = new Vector2(560, 620);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(360, 280),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        plugin.BeastmasterWindow.AddRunTitlebarActions(this);
    }

    public override void OnOpen()
    {
        splashes.Open(Environment.TickCount64);
        plugin.BeastmasterWindow.OnMiniOpen();
    }

    public override void PreDraw()
    {
        UIConstants.PushStyle(plugin.Configuration.CompactUi);
        if (VermaxionPresentation.HasMaterial || plugin.BackgroundBlurEnabled)
        {
            ImGui.PushStyleColor(ImGuiCol.WindowBg, Vector4.Zero);
            materialBackdropStylePushed = true;
        }
        motion.Prepare(this, reducedMotion: !plugin.Configuration.UiStylingEnabled, roundedCorners: plugin.Configuration.UiStylingEnabled);
        movementLock.Apply(this, plugin.Configuration.UiWindowsLocked);
    }

    public override void PostDraw()
    {
        movementLock.Restore(this);
        motion.Restore(this);
        ReleaseMaterialBackdropStyle();
        UIConstants.PopStyle();
    }

    private void BeginMaterialContent()
    {
        ReleaseMaterialBackdropStyle();
        plugin.DrawWindowBackdrop(this);
    }

    private void ReleaseMaterialBackdropStyle()
    {
        if (!materialBackdropStylePushed) return;
        ImGui.PopStyleColor();
        materialBackdropStylePushed = false;
    }

    public override void Draw()
    {
        motion.DrawChrome();
        BeginMaterialContent();
        using var structuralBackgrounds = AethertekUI.MaterialSkin.StructuralBackgroundStyle();
        if (AethertekUI.MaterialTheme.Current.IsLcars) AethertekUI.MaterialLcars.Header(UiText.T("Beastmaster"));
        plugin.BeastmasterWindow.DrawMiniContents(splashes);
    }
}
