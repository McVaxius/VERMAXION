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

        // Manual trigger button
        if (!engine.IsRunning)
        {
            if (ImGui.Button("Run Now (Manual)"))
                engine.ManualStart();

            ImGui.SameLine();
            if (ImGui.Button("Config"))
                plugin.ToggleConfigUi();
        }

        ImGui.Separator();

        // Status overview
        if (ImGui.BeginTable("StatusTable", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("Task", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Enabled", ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 120);
            ImGui.TableHeadersRow();

            DrawStatusRow("FC Buff Refill", config.EnableFCBuffRefill, "Every AR run");
            DrawStatusRow("Verminion (5x)", config.EnableVerminionQueue,
                config.VerminionCompletedThisWeek ? "Done this week" : "Pending");
            DrawStatusRow("Mini Cactpot", config.EnableMiniCactpot,
                config.MiniCactpotCompletedToday ? "Done today" : "Pending");
            DrawStatusRow("Jumbo Cactpot", config.EnableJumboCactpot,
                config.JumboCactpotCompletedThisWeek ? "Done this week" : "Pending (Sat)");
            DrawStatusRow("Chocobo Racing", config.EnableChocoboRacing,
                config.ChocoboRacingCompletedToday ? "Done today" : "Pending");
            DrawStatusRow("Henchman Mgmt", config.EnableHenchmanManagement, "Stop/Start");

            ImGui.EndTable();
        }

        ImGui.Spacing();

        // Reset info
        var now = DateTime.UtcNow;
        var nextDaily = Services.ResetDetectionService.GetLastDailyReset(now).AddDays(1);
        var nextWeekly = Services.ResetDetectionService.GetLastWeeklyReset(now).AddDays(7);
        var untilDaily = nextDaily - now;
        var untilWeekly = nextWeekly - now;

        ImGui.TextDisabled($"Next daily reset: {untilDaily.Hours}h {untilDaily.Minutes}m");
        ImGui.TextDisabled($"Next weekly reset: {untilWeekly.Days}d {untilWeekly.Hours}h {untilWeekly.Minutes}m");
        ImGui.TextDisabled($"Saturday: {(now.DayOfWeek == DayOfWeek.Saturday ? "Yes" : "No")} ({now.DayOfWeek})");

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
