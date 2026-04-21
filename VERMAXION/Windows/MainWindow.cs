using System;
using System.Diagnostics;
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
        var charKey = plugin.ConfigManager.CurrentCharacterKey;
        var displayName = string.IsNullOrEmpty(charKey) ? "(Default)" : charKey;

        // Version header
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";
        ImGui.Text($"Vermaxion v{version}");
        
        // Ko-fi donation button in upper right
        ImGui.SameLine(ImGui.GetWindowWidth() - 120);
        if (ImGui.SmallButton("\u2661 Ko-fi \u2661"))
        {
            System.Diagnostics.Process.Start(new ProcessStartInfo
            {
                FileName = "https://ko-fi.com/mcvaxius",
                UseShellExecute = true
            });
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Support development on Ko-fi");
        }
        
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
        // FULL STOP button - red only when plugin is in operation
        var highlightFullStop = engine.IsRunning;
        if (highlightFullStop)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.8f, 0f, 0f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(1f, 0.2f, 0.2f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.6f, 0f, 0f, 1f));
        }
        if (ImGui.Button("FULL STOP"))
        {
            plugin.FullStop();
        }
        if (highlightFullStop)
        {
            ImGui.PopStyleColor(3);
        }
        ImGui.SameLine();
        
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
            DrawTaskRow("Run Shutdown Bundle", true, "Every AR + manual run",
                "Send##ShutdownBundle", () => plugin.Engine.SendRunShutdownCommandBundle(), "OK");
            DrawTaskRow("FC Buff Refill", config.EnableFCBuffRefill, "Every AR run",
                "Test##FCBuff", () => plugin.FCBuffService.RunTask(), "OK");
            DrawTaskRow("Vendor Stock", config.EnableVendorStock, GetVendorStockStatus(config),
                "Test##Vendor", () => plugin.VendorStockService.RunTask(), "OK");
            DrawTaskRow("Register Registrables", config.EnableRegisterRegistrables, "Every AR run",
                "Test##Register", () => plugin.RegisterRegistrablesService.Start(), "OK");
            DrawTaskRow("Henchman Mgmt", config.EnableHenchmanManagement, "Stop/Start",
                "Off##Hench", () => plugin.HenchmanService.StopHenchman(), "OK");
            DrawTaskRow("Seasonal Gear", config.EnableSeasonalGearRoulette, "Every AR run",
                "Test##Seasonal", () => plugin.SeasonalGearService.RunTask(), "OK");
            DrawTaskRow("Minion Roulette", config.EnableMinionRoulette, "Every AR run",
                "Test##Minion", () => plugin.MinionRouletteService.RunTask(), "OK");
            DrawTaskRow("Gear Updater", config.EnableGearUpdater, "Every AR run",
                "Test##Gear", () => plugin.GearUpdaterService.RunTask(), "OK");

            // --- Weekly Tasks ---
            DrawTaskRow("Verminion (5x)", config.EnableVerminionQueue,
                GetWeeklyTaskStatus(config.VerminionLastCompleted, config.VerminionNextReset, "Done this week", "Weekly"),
                "Test##Verm", () => plugin.VerminionService.RunTask(), "OK");
            DrawTaskRow("Jumbo Cactpot", config.EnableJumboCactpot,
                GetJumboCactpotStatus(config),
                "Test##Jumbo", () => plugin.CactpotService.RunJumboCactpot(), "WIP");
            DrawTaskRow("Fashion Report", config.EnableFashionReport,
                GetFashionReportStatus(config),
                "Test##Fashion", () => plugin.FashionReportService.Start(), "OK");

            // --- Daily Tasks ---
            DrawTaskRow("Mini Cactpot", config.EnableMiniCactpot,
                GetDailyTaskStatus(config.MiniCactpotLastCompleted, config.MiniCactpotNextReset, "Done today", "Daily"),
                "Test##Mini", () => plugin.CactpotService.RunMiniCactpot(), "OK");
            DrawTaskRow("Chocobo Racing", config.EnableChocoboRacing,
                GetDailyTaskStatus(config.ChocoboRacingLastCompleted, config.ChocoboRacingNextReset, "Done today", "Daily"),
                "Test##Choco", () => plugin.ChocoboRaceService.RunTask(), "OK");
            DrawTaskRow("nag your mom", config.EnableNagYourMom,
                GetNagYourMomStatus(config, engine.NagYourMomStatusText),
                "Test##Mom", () =>
                {
                    var result = plugin.MomIPCClient.StartCcRuns(1, config.NagYourMomJob);
                    Plugin.ChatGui.Print($"[Vermaxion] {result.Summary}");
                }, "OK");
            DrawTaskRow("nag your dad", config.EnableNagYourDad,
                GetNagYourDadStatus(config, engine.NagYourDadStatusText),
                "Test##Dad", () =>
                {
                    var result = plugin.DadIPCClient.StartTasks(BuildDadRunRequest(config));
                    Plugin.ChatGui.Print($"[Vermaxion] {result.Summary}");
                }, "WIP");

            // --- Utility Tasks ---
            DrawTaskRow("Highest Combat Job", config.EnableHighestCombatJob, "Every AR run",
                "Test##Highest", () => plugin.HighestCombatJobService.RunTask(), "OK");
            DrawTaskRow("Current Job Equipment", config.EnableCurrentJobEquipment, "Every AR run",
                "Test##Current", () => plugin.CurrentJobEquipmentService.RunTask(), "OK");

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
            if (ImGui.SmallButton("Test FC Points"))
            {
                Plugin.Log.Information("[FC POINTS] Testing FC points reading from UI...");
                var fcPoints = GameHelpers.GetFCPointsNode();
                if (fcPoints.HasValue)
                {
                    Plugin.Log.Information($"[FC POINTS] SUCCESS: FC points = {fcPoints.Value:N0}");
                }
                else
                {
                    Plugin.Log.Information("[FC POINTS] FAILED: Could not read FC points from UI node #17");
                }
            }
            
            ImGui.SameLine();
            if (ImGui.SmallButton("Force Config Load"))
            {
                plugin.ConfigManager.LoadAllAccounts();
                // Get config AFTER loading to ensure we have the latest values
                var activeConfig = plugin.ConfigManager.GetActiveConfig();
                Plugin.Log.Information($"[UI] Forced config load: FCBuffMinPoints={activeConfig.FCBuffMinPoints}, FCBuffPurchaseAttempts={activeConfig.FCBuffPurchaseAttempts}");
            }
            
            // BUTTON PRESSES
            ImGui.Spacing();
            ImGui.Text("Button Presses");
            ImGui.Separator();
            
            if (ImGui.SmallButton("[ESC]"))
            {
                Plugin.Log.Information("[UI] Testing ESC key press");
                GameHelpers.CloseCurrentAddon();
            }
            
            ImGui.SameLine();
            if (ImGui.SmallButton("[NUMPAD+]"))
            {
                Plugin.Log.Information("[UI] Testing NUMPAD+ key press");
                GameHelpers.SendNumpadPlus();
            }
            
            ImGui.SameLine();
            if (ImGui.SmallButton("[END]"))
            {
                Plugin.Log.Information("[UI] Testing END key press");
                GameHelpers.SendEnd();
            }
        }

        ImGui.Spacing();

        // Timers
        var now = DateTime.UtcNow;
        var nextDaily = ResetDetectionService.GetLastDailyReset(now).AddDays(1);
        var nextWeekly = ResetDetectionService.GetLastWeeklyReset(now).AddDays(7);
        var untilDaily = nextDaily - now;
        var untilWeekly = nextWeekly - now;
        var nextFriday = ResetDetectionService.GetNextFashionReportAvailability(now);
        var untilFriday = nextFriday - now;

        var nextJumboPayout = ResetDetectionService.GetNextJumboCactpotPayoutAvailability(now);
        var untilJumboPayout = nextJumboPayout - now;

        ImGui.TextDisabled($"Daily: {untilDaily.Hours}h {untilDaily.Minutes}m  |  Weekly: {untilWeekly.Days}d {untilWeekly.Hours}h {untilWeekly.Minutes}m  |  Fashion: {untilFriday.Days}d {untilFriday.Hours}h {untilFriday.Minutes}m  |  Jumbo payout: {untilJumboPayout.Days}d {untilJumboPayout.Hours}h {untilJumboPayout.Minutes}m");

        // AR status
        var arStatus = plugin.ARPostProcessService.IsProcessing ? "Processing" : "Waiting";
        ImGui.TextDisabled($"AR PostProcess: {arStatus}  |  {now.DayOfWeek}");
        ImGui.TextDisabled($"mom IPC: {(plugin.MomIPCClient.IsReady() ? "Ready" : "Unavailable")}  |  nag your mom: {engine.NagYourMomStatusText}");
        ImGui.TextDisabled($"dad IPC: {(plugin.DadIPCClient.IsReady() ? "Ready" : "Unavailable")}  |  nag your dad: {engine.NagYourDadStatusText}");
        
        ImGui.Spacing();
        ImGui.Separator();
        
        // Support section
        ImGui.Text("Support VERMAXION");
        ImGui.SameLine();
        if (ImGui.SmallButton("Buy me a coffee"))
        {
            System.Diagnostics.Process.Start(new ProcessStartInfo
            {
                FileName = "https://ko-fi.com/mcvaxius",
                UseShellExecute = true
            });
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Support continued development on Ko-fi");
        }
        ImGui.TextDisabled("Every donation helps keep these plugins free and updated!");
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

    private static string GetWeeklyTaskStatus(DateTime lastCompleted, DateTime nextReset, string completedText, string pendingText)
    {
        return ResetDetectionService.TaskIsCompleted(lastCompleted, nextReset) ? completedText : pendingText;
    }

    private static string GetDailyTaskStatus(DateTime lastCompleted, DateTime nextReset, string completedText, string pendingText)
    {
        return ResetDetectionService.TaskIsCompleted(lastCompleted, nextReset) ? completedText : pendingText;
    }

    private static string GetFashionReportStatus(Models.CharacterConfig config)
    {
        if (ResetDetectionService.TaskIsCompleted(config.FashionReportLastCompleted, config.FashionReportNextReset))
            return "Done this week";

        return ResetDetectionService.IsFashionReportAvailable(DateTime.UtcNow) ? "Ready now" : "Pending (Fri 01 UTC)";
    }

    private static string GetJumboCactpotStatus(Models.CharacterConfig config)
    {
        if (ResetDetectionService.IsJumboPurchasePendingPayout(config.JumboCactpotLastCompleted, config.JumboCactpotNextReset))
            return "Ticket purchased";

        if (ResetDetectionService.TaskIsCompleted(config.JumboCactpotLastCompleted, config.JumboCactpotNextReset))
            return "Done this week";

        return ResetDetectionService.IsJumboCactpotPayoutAvailable(DateTime.UtcNow) ? "Ready payout" : "Ready purchase";
    }

    private static string GetVendorStockStatus(Models.CharacterConfig config)
    {
        if (!config.EnableVendorStock)
            return "Off";

        if (config.VendorStockGysahlGreensTarget <= 0 && config.VendorStockGrade8DarkMatterTarget <= 0)
            return "Set targets";

        return "Every AR run";
    }

    private static string GetNagYourMomStatus(Models.CharacterConfig config, string engineStatus)
    {
        if (!config.EnableNagYourMom)
            return "Off";

        if (config.NagYourMomRunsPerDay <= 0)
            return "Set runs/day";

        if (string.IsNullOrWhiteSpace(config.NagYourMomJob))
            return "Set job";

        if (config.NagYourMomAttemptsToday >= config.NagYourMomRunsPerDay)
            return "Daily cap hit";

        if (!TimeSpan.TryParse(config.NagYourMomWindowStartLocal, out var start) || !TimeSpan.TryParse(config.NagYourMomWindowEndLocal, out var end))
            return "Bad local window";

        var now = DateTime.Now.TimeOfDay;
        var inWindow = start <= end
            ? now >= start && now <= end
            : now >= start || now <= end;

        if (!inWindow)
            return "Outside local window";

        return string.IsNullOrWhiteSpace(engineStatus) || engineStatus == "Idle"
            ? "Ready on AR"
            : engineStatus;
    }

    private static string GetNagYourDadStatus(Models.CharacterConfig config, string engineStatus)
    {
        if (!config.EnableNagYourDad)
            return "Off";

        if (config.NagYourDadDungeonCount > 0 &&
            (string.IsNullOrWhiteSpace(config.NagYourDadDungeonName) || config.NagYourDadDungeonContentFinderConditionId == 0))
            return "Set dungeon";

        if (config.NagYourDadDailyMsq && string.IsNullOrWhiteSpace(config.NagYourDadLanPartyPreset))
            return "Set Lan Party preset";

        if (!HasNagYourDadConfiguredWork(config))
            return "Set dad tasks";

        if (config.NagYourDadAstropeAttempts > 0)
        {
            if (!TimeSpan.TryParse(config.NagYourDadWindowStartLocal, out var start) ||
                !TimeSpan.TryParse(config.NagYourDadWindowEndLocal, out var end))
            {
                return "Bad Astrope window";
            }

            var now = DateTime.Now.TimeOfDay;
            var inWindow = start <= end
                ? now >= start && now <= end
                : now >= start || now <= end;

            if (!inWindow)
                return "Outside Astrope window";
        }

        return string.IsNullOrWhiteSpace(engineStatus) || engineStatus == "Idle"
            ? "Ready on AR"
            : engineStatus;
    }

    private static bool HasNagYourDadConfiguredWork(Models.CharacterConfig config)
    {
        if (config.NagYourDadDungeonCount > 0 &&
            !string.IsNullOrWhiteSpace(config.NagYourDadDungeonName) &&
            config.NagYourDadDungeonContentFinderConditionId != 0)
            return true;

        if (config.NagYourDadDailyMsq && !string.IsNullOrWhiteSpace(config.NagYourDadLanPartyPreset))
            return true;

        if (config.NagYourDadCommendationAttempts > 0)
            return true;

        if (config.NagYourDadAstropeAttempts > 0)
            return true;

        return false;
    }

    private static Models.DadRunRequest BuildDadRunRequest(Models.CharacterConfig config)
    {
        var request = new Models.DadRunRequest
        {
            RequestedBy = "VERMAXION UI",
        };

        if (config.NagYourDadDungeonCount > 0 &&
            !string.IsNullOrWhiteSpace(config.NagYourDadDungeonName) &&
            config.NagYourDadDungeonContentFinderConditionId != 0)
        {
            request.Dungeon = new Models.DadDungeonTask
            {
                Count = Math.Max(1, config.NagYourDadDungeonCount),
                Frequency = Models.DadRunRequestOptions.NormalizeFrequency(config.NagYourDadDungeonFrequency),
                ContentFinderConditionId = config.NagYourDadDungeonContentFinderConditionId,
                SelectedDungeon = config.NagYourDadDungeonName.Trim(),
                SelectedJob = config.NagYourDadDungeonJob.Trim().ToUpperInvariant(),
                ExecutionPreference = Models.DadRunRequestOptions.TrustThenDutySupport,
                QueueViaLanParty = config.NagYourDadQueueViaLanParty,
                Unsynced = config.NagYourDadDungeonUnsynced,
            };
        }

        if (config.NagYourDadDailyMsq && !string.IsNullOrWhiteSpace(config.NagYourDadLanPartyPreset))
        {
            request.DailyMsq = new Models.DadDailyMsqTask
            {
                LanPartyPreset = config.NagYourDadLanPartyPreset.Trim(),
            };
        }

        if (config.NagYourDadCommendationAttempts > 0)
        {
            request.Commendation = new Models.DadCommendationTask
            {
                Attempts = config.NagYourDadCommendationAttempts,
            };
        }

        if (config.NagYourDadAstropeAttempts > 0)
        {
            request.Astrope = new Models.DadAstropeTask
            {
                Attempts = config.NagYourDadAstropeAttempts,
                ValidLocalTimeWindow = new Models.DadTimeWindow
                {
                    StartLocal = config.NagYourDadWindowStartLocal,
                    EndLocal = config.NagYourDadWindowEndLocal,
                },
            };
        }

        return request;
    }
}
