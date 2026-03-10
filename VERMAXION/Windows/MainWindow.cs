using System;
using System.Numerics;
using System.Reflection;
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
            MinimumSize = new Vector2(520, 480),
            MaximumSize = new Vector2(800, 700),
        };
    }

    public void Dispose() { }

    public override void Draw()
    {
        var config = plugin.ConfigManager.GetActiveConfig();
        var engine = plugin.Engine;
        var charKey = plugin.ConfigManager.SelectedCharacterKey;
        var displayName = string.IsNullOrEmpty(charKey) ? "(Default)" : charKey;

        // Version header
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";
        ImGui.Text($"Vermaxion v{version}");
        ImGui.Separator();
        ImGui.Spacing();

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
            VermaxionEngine.EngineState.Idle => new Vector4(0.5f, 0.5f, 0.5f, 1f),
            VermaxionEngine.EngineState.Complete => new Vector4(0f, 1f, 0f, 1f),
            VermaxionEngine.EngineState.Error => new Vector4(1f, 0f, 0f, 1f),
            _ => new Vector4(1f, 0.8f, 0f, 1f),
        };

        ImGui.TextColored(stateColor, $"Engine: {engine.StatusText} (State: {engine.State})");
        
        // Task count
        var pendingTasks = engine.GetPendingTaskCount();
        if (pendingTasks > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(1f, 1f, 0f, 1f), $"({pendingTasks} pending)");
        }
        
        ImGui.Spacing();
        
        // Control buttons row
        // ALWAYS visible STOP button (red, prominent)
        if (engine.IsRunning || plugin.FCBuffService.IsActive || plugin.VerminionService.IsActive || plugin.CactpotService.IsActive || plugin.ChocoboRaceService.IsActive)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(1f, 0.1f, 0.1f, 1f));
            if (ImGui.Button("🛑 STOP ALL TASKS"))
            {
                // Stop engine if running
                if (engine.IsRunning) engine.Stop();
                // Stop all individual services
                plugin.FCBuffService.Reset();
                plugin.VerminionService.Reset();
                plugin.CactpotService.Reset();
                plugin.ChocoboRaceService.Reset();
            }
            ImGui.PopStyleColor();
            ImGui.SameLine();
        }
        
        if (ImGui.Button("Run All"))
            engine.ManualStart();
        if (engine.IsRunning)
        {
            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
                engine.Cancel();
        }
        ImGui.SameLine();
        if (ImGui.Button("Config"))
            plugin.ToggleConfigUi();

        ImGui.Spacing();

        // Task table with test buttons
        if (ImGui.BeginTable("TasksTable", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit))
        {
            ImGui.TableSetupColumn("Task", ImGuiTableColumnFlags.WidthFixed, 200);
            ImGui.TableSetupColumn("Enabled", ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 120);
            ImGui.TableSetupColumn("Test", ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableSetupColumn("Maturity", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableHeadersRow();

            // --- Every AR PostProcess ---
            DrawTaskRow("FC Buff Refill", config.EnableFCBuffRefill, "Every AR run",
                "Test##FCBuff", () => plugin.FCBuffService.RunTask(), "WIP");
            DrawTaskRow("Henchman Mgmt", config.EnableHenchmanManagement, "Stop/Start",
                "Off##Hench", () => plugin.HenchmanService.StopHenchman(), "OK");
            DrawTaskRow("Minion Roulette", config.EnableMinionRoulette, "Every AR run",
                "Test##Minion", () => plugin.MinionRouletteService.RunTask(), "-");
            DrawTaskRow("Seasonal Gear", config.EnableSeasonalGearRoulette, "Every AR run",
                "Test##Seasonal", () => plugin.SeasonalGearService.RunTask(), "-");
            DrawTaskRow("Gear Updater", config.EnableGearUpdater, "Every AR run",
                "Test##Gear", () => plugin.GearUpdaterService.RunTask(), "-");

            // --- Weekly Tasks ---
            DrawTaskRow("Verminion (5x)", config.EnableVerminionQueue,
                config.VerminionCompletedThisWeek ? "Done this week" : "Pending",
                "Test##Verm", () => plugin.VerminionService.RunTask(), "-");
            DrawTaskRow("Jumbo Cactpot", config.EnableJumboCactpot,
                config.JumboCactpotCompletedThisWeek ? "Done this week" : "Pending (Sat)",
                "Test##Jumbo", () => plugin.CactpotService.RunJumboCactpot(), "-");

            // --- Daily Tasks ---
            DrawTaskRow("Mini Cactpot", config.EnableMiniCactpot,
                config.MiniCactpotCompletedToday ? "Done today" : "Pending",
                "Test##Mini", () => plugin.CactpotService.RunMiniCactpot(), "-");
            DrawTaskRow("Chocobo Racing", config.EnableChocoboRacing,
                config.ChocoboRacingCompletedToday ? "Done today" : "Pending",
                "Test##Choco", () => plugin.ChocoboRaceService.RunTask(), "-");

            ImGui.EndTable();

            ImGui.Spacing();

            // Test Functions
            ImGui.Text("Test Functions");
            ImGui.Separator();
            
            if (ImGui.SmallButton("Check FC Buff Inventory"))
            {
                // Force config save before test
                plugin.ConfigManager.SaveCurrentAccount();
                Plugin.Log.Information("[UI] Forced config save before FC Buff Inventory test");
                plugin.FCBuffInventoryService.Start();
            }
            
            ImGui.SameLine();
            if (ImGui.SmallButton("FC GC Test"))
            {
                // Force config save before test
                plugin.ConfigManager.SaveCurrentAccount();
                Plugin.Log.Information("[UI] Forced config save before FC GC test");
                plugin.FCBuffService.TestFreeCompanyGC();
            }
            
            ImGui.SameLine();
            if (ImGui.SmallButton("Force Config Save"))
            {
                var activeConfig = plugin.ConfigManager.GetActiveConfig();
                plugin.ConfigManager.SaveCurrentAccount();
                Plugin.Log.Information($"[UI] Forced config save: FCBuffMinPoints={activeConfig.FCBuffMinPoints}, FCBuffPurchaseAttempts={activeConfig.FCBuffPurchaseAttempts}");
            }
        }

        ImGui.Spacing();

        // Timers
        var now = DateTime.UtcNow;
        var nextDaily = ResetDetectionService.GetLastDailyReset(now).AddDays(1);
        var nextWeekly = ResetDetectionService.GetLastWeeklyReset(now).AddDays(7);
        var untilDaily = nextDaily - now;
        var untilWeekly = nextWeekly - now;

        // Saturday timer
        var daysUntilSaturday = ((int)(DayOfWeek.Saturday - now.DayOfWeek + 7) % 7);
        if (daysUntilSaturday == 0 && now.Hour >= 15)
            daysUntilSaturday = 7;
        var saturdayResetTime = now.Date.AddDays(daysUntilSaturday).AddHours(15);
        var untilSaturday = saturdayResetTime - now;

        ImGui.TextDisabled($"Daily: {untilDaily.Hours}h {untilDaily.Minutes}m  |  Weekly: {untilWeekly.Days}d {untilWeekly.Hours}h {untilWeekly.Minutes}m  |  Saturday: {untilSaturday.Days}d {untilSaturday.Hours}h {untilSaturday.Minutes}m");

        // AR status
        var arStatus = plugin.ARPostProcessService.IsProcessing ? "Processing" : "Waiting";
        ImGui.TextDisabled($"AR PostProcess: {arStatus}  |  {now.DayOfWeek}");
    }

    private void DrawTaskRow(string task, bool enabled, string status, string buttonLabel, Action onClick, string maturity = "-")
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text(task);
        ImGui.TableSetColumnIndex(1);
        ImGui.TextColored(enabled ? new Vector4(0, 1, 0, 1) : new Vector4(1, 0, 0, 1), enabled ? "On" : "Off");
        ImGui.TableSetColumnIndex(2);
        ImGui.TextDisabled(status);
        ImGui.TableSetColumnIndex(3);
        if (ImGui.SmallButton(buttonLabel))
            onClick();
        ImGui.TableSetColumnIndex(4);
        
        // Color code maturity
        Vector4 color;
        switch (maturity)
        {
            case "OK":
                color = new Vector4(0, 1, 0, 1); // Green
                break;
            case "WIP":
                color = new Vector4(1, 1, 0, 1); // Yellow
                break;
            default:
                color = new Vector4(1, 0, 0, 1); // Red
                break;
        }
        ImGui.TextColored(color, maturity);
    }
}
