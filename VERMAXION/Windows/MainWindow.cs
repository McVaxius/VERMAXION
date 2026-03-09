using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using VERMAXION.Services;

namespace VERMAXION.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    public MainWindow(Plugin plugin)
        : base("Vermaxion##Main", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(400, 300),
            MaximumSize = new Vector2(800, 600),
        };
    }

    public void Dispose() { }

    public override void Draw()
    {
        var config = plugin.ConfigManager.GetActiveConfig();
        var engine = plugin.Engine;
        var charKey = plugin.ConfigManager.SelectedCharacterKey;
        var displayName = string.IsNullOrEmpty(charKey) ? "(Default)" : charKey;

        if (plugin.Configuration.KrangleEnabled && !string.IsNullOrEmpty(charKey))
            displayName = KrangleService.KrangleName(charKey);

        ImGui.Text($"Character: {displayName}");
        ImGui.SameLine();
        var enabled = config.Enabled;
        if (ImGui.Checkbox("Enabled", ref enabled))
        {
            config.Enabled = enabled;
            plugin.ConfigManager.SaveCurrentAccount();
        }

        ImGui.SameLine();
        var krangleEnabled = plugin.Configuration.KrangleEnabled;
        if (ImGui.Checkbox("Krangle", ref krangleEnabled))
        {
            plugin.Configuration.KrangleEnabled = krangleEnabled;
            if (!krangleEnabled) KrangleService.ClearCache();
            plugin.Configuration.Save();
        }

        ImGui.Separator();

        // Engine Status
        var stateColor = engine.State switch
        {
            Services.VermaxionEngine.EngineState.Idle => new Vector4(0.5f, 0.5f, 0.5f, 1f),
            Services.VermaxionEngine.EngineState.Complete => new Vector4(0f, 1f, 0f, 1f),
            Services.VermaxionEngine.EngineState.Error => new Vector4(1f, 0f, 0f, 1f),
            _ => new Vector4(1f, 0.8f, 0f, 1f),
        };

        ImGui.TextColored(stateColor, $"Engine: {engine.StatusText}");

        if (engine.IsRunning)
        {
            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
                engine.Cancel();
        }

        ImGui.Spacing();

        // Manual Task Testing
        ImGui.Text("Manual Task Testing:");
        ImGui.Separator();

        if (ImGui.Button("FC Buff Refill"))
            plugin.FCBuffService.RunTask();
        ImGui.SameLine();
        if (ImGui.Button("Verminion (5x)"))
            plugin.VerminionService.RunTask();
        ImGui.SameLine();
        if (ImGui.Button("Mini Cactpot"))
            plugin.CactpotService.RunMiniCactpot();

        if (ImGui.Button("Jumbo Cactpot"))
            plugin.CactpotService.RunJumboCactpot();
        ImGui.SameLine();
        if (ImGui.Button("Chocobo Racing"))
            plugin.ChocoboRaceService.RunTask();
        ImGui.SameLine();
        if (ImGui.Button("Henchman Off"))
            plugin.HenchmanService.StopHenchman();

        ImGui.SameLine();
        if (ImGui.Button("Henchman On"))
            plugin.HenchmanService.StartHenchman();

        ImGui.Spacing();
        ImGui.Separator();

        // Quick Actions
        ImGui.Text("Quick Actions:");
        if (ImGui.Button("Run All Tasks"))
        {
            engine.ManualStart();
        }
        ImGui.SameLine();
        if (ImGui.Button("Config"))
            plugin.ToggleConfigUi();

        ImGui.Spacing();

        // Reset info
        var now = DateTime.UtcNow;
        var nextDaily = Services.ResetDetectionService.GetLastDailyReset(now).AddDays(1);
        var nextWeekly = Services.ResetDetectionService.GetLastWeeklyReset(now).AddDays(7);
        var untilDaily = nextDaily - now;
        var untilWeekly = nextWeekly - now;

        ImGui.TextDisabled($"Next daily reset: {untilDaily.Hours}h {untilDaily.Minutes}m");
        ImGui.TextDisabled($"Next weekly reset: {untilWeekly.Days}d {untilWeekly.Hours}h {untilWeekly.Minutes}m");
        
        // Saturday timer
        var daysUntilSaturday = ((DayOfWeek.Saturday - now.DayOfWeek + 7) % 7);
        if (daysUntilSaturday == 0 && now.Hour >= 15) // After Saturday reset, count to next Saturday
            daysUntilSaturday = 7;
        var nextSaturday = now.AddDays(daysUntilSaturday);
        var saturdayResetTime = new DateTime(nextSaturday.Year, nextSaturday.Month, nextSaturday.Day, 15, 0, 0);
        if (now.Hour >= 15 && now.DayOfWeek == DayOfWeek.Saturday)
            saturdayResetTime = saturdayResetTime.AddDays(7);
        var untilSaturday = saturdayResetTime - now;
        
        ImGui.TextDisabled($"Saturday reset: {untilSaturday.Days}d {untilSaturday.Hours}h {untilSaturday.Minutes}m");
        ImGui.TextDisabled($"Current day: {now.DayOfWeek}");

        ImGui.Spacing();

        // AR connection status
        var arStatus = plugin.ARPostProcessService.IsProcessing ? "Processing" : "Waiting";
        ImGui.TextDisabled($"AR PostProcess: {arStatus}");
    }

    private void DrawStatusRow(string task, bool enabled, string status)
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text(task);
        ImGui.TableSetColumnIndex(1);
        ImGui.TextColored(enabled ? new Vector4(0, 1, 0, 1) : new Vector4(1, 0, 0, 1), enabled ? "On" : "Off");
        ImGui.TableSetColumnIndex(2);
        ImGui.TextDisabled(status);
    }
}
