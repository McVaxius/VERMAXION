using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using VERMAXION.Beastmaster;

namespace VERMAXION.Windows;

internal sealed class BeastmasterMiniWindow : Window
{
    private readonly Plugin plugin;
    private readonly AethertekUI.Dalamud.MaterialWindowMotion motion = new();
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
        motion.Prepare(this, reducedMotion: false, roundedCorners: true);
    }

    public override void PostDraw()
    {
        motion.Restore(this);
        UIConstants.PopStyle();
    }

    public override void Draw()
    {
        motion.DrawChrome();
        if (AethertekUI.MaterialTheme.Current.IsLcars) AethertekUI.MaterialLcars.Header(UiText.T("Beastmaster"));
        plugin.BeastmasterWindow.DrawMiniContents(splashes);
    }
}
