using System;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using VERMAXION.Models;
using VERMAXION.Services;

namespace VERMAXION.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    public MainWindow(Plugin plugin)
        : base("Vermaxion##Main")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(520, 480),
            MaximumSize = new Vector2(1600, 1200),
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
        if (!engine.RegistryReady)
        {
            ImGui.TextColored(new Vector4(1f, 0.15f, 0.15f, 1f), "CONFIGURED BUT NOT DISPATCHABLE");
            ImGui.TextWrapped(engine.RegistryDiagnostic);
        }
        var lastRunTime = engine.LastRunCompletedAtUtc?.ToString("u") ?? "never";
        ImGui.TextDisabled($"Last run: {engine.LastRunOutcome} at {lastRunTime} - {engine.LastRunSummary}");
        ImGui.TextDisabled($"Before-AR gate: {plugin.BeforeArGate} - {plugin.BeforeArStatusText}");
        ImGui.TextDisabled($"AR suppression: {plugin.AutoRetainerIPC.LastSnapshot}");
        if (!string.IsNullOrWhiteSpace(engine.ActiveHandoffBlocker))
            ImGui.TextColored(new Vector4(1f, 0.65f, 0f, 1f), $"Handoff blocker: {engine.ActiveHandoffBlocker}");
        
        // Task count
        var pendingTasks = engine.GetPendingTaskCount();
        if (pendingTasks > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(1f, 1f, 0f, 1f), $"({pendingTasks} pending)");
        }
        
        ImGui.Spacing();

        var currentBlockers = AutomationCatalog.EngineTasks
            .Select(feature => (feature, eligibility: engine.GetTaskEligibility(feature.Id)))
            .Where(item => item.eligibility.Status is TaskEligibilityStatus.Blocked or TaskEligibilityStatus.Unsupported)
            .ToList();
        if (currentBlockers.Count > 0 && ImGui.CollapsingHeader($"Blocked prerequisites ({currentBlockers.Count})"))
        {
            foreach (var item in currentBlockers)
                ImGui.BulletText($"{item.feature.Label}: {item.eligibility.Reason}");
        }
        
        // Control buttons row
        // FULL STOP button - red only when plugin is in operation
        var highlightFullStop = engine.OwnsLiveWork ||
                                plugin.LootGoblinMapGatherManualRunCoordinator.IsActive ||
                                plugin.FishingService.IsActive ||
                                plugin.FishingRelogCoordinator.IsActive ||
                                plugin.ARPostProcessService.IsProcessing ||
                                plugin.AutoRetainerIPC.SuppressionOwnedByVermaxion;
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
        
        ImGui.BeginDisabled(engine.IsRunning || plugin.DadHandoffBlocksNewWork);
        if (ImGui.Button("Run All"))
            engine.ManualStart();
        ImGui.EndDisabled();
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

        // Task table with run buttons
        if (ImGui.BeginTable("TasksTable", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit))
        {
            ImGui.TableSetupColumn("Task", ImGuiTableColumnFlags.WidthFixed, 200);
            ImGui.TableSetupColumn("Enabled", ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 120);
            ImGui.TableSetupColumn("run", ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableSetupColumn("Maturity", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableHeadersRow();

            // --- Every AR PostProcess ---
            DrawTaskCategory("Run-start hook", AutomationCatalog.Get(AutomationCatalog.MiscCommands));
            DrawTaskRow("Misc Cmd", config.EnableMiscCmd,
                config.EnableMiscCmd ? "Every AR + manual run" : "Off",
                "run##MiscCmd", () => plugin.Engine.SendRunShutdownCommandBundle(), "OK");
            DrawTaskCategory("Ordered engine tasks", AutomationCatalog.Get(AutomationCatalog.FCBuffRefill));
            DrawTaskRow("FC Buff Refill", config.EnableFCBuffRefill, "Every AR run",
                "run##FCBuff", () => plugin.FCBuffService.RunTask(), "OK");
            DrawTaskRow("Vendor Stock", config.EnableVendorStock, GetVendorStockStatus(config),
                "run##Vendor", () => plugin.VendorStockService.RunTask(), "OK");
            var fishingButtonsDisabled = engine.IsRunning ||
                                         plugin.IsFishingRunActive ||
                                         plugin.FisherGearsetTestService.IsActive;
            DrawTaskCategory("Preemptive coordinator", AutomationCatalog.Get(AutomationCatalog.Fishing));
            DrawTaskRow("Fishing", config.EnableFishing, GetFishingStatus(config, plugin.FishingRunStatusText),
                "R##Fishing", plugin.RunFishingStartupManual, "OK",
                buttonDisabled: fishingButtonsDisabled,
                buttonTooltip: fishingButtonsDisabled ? "Fishing, relog, or engine work is active. Use FULL STOP to cancel." : null,
                secondaryButtonLabel: "T##FishingTest",
                secondaryOnClick: plugin.RunFishingStartupTest,
                secondaryButtonDisabled: fishingButtonsDisabled,
                secondaryButtonTooltip: "Run a full account-level Ocean Fishing test using the next real registration.",
                tertiaryButtonLabel: "F##FishingGearsetTest",
                tertiaryOnClick: plugin.RunFishingGearsetTest,
                tertiaryButtonDisabled: fishingButtonsDisabled,
                tertiaryButtonTooltip: "Equip and verify the current character's first saved Fisher gearset.");
            DrawTaskCategory("Ordered engine tasks (continued)", AutomationCatalog.Get(AutomationCatalog.RegisterRegistrables));
            DrawTaskRow("Register Registrables", config.EnableRegisterRegistrables, "Every AR run",
                "run##Register", () => plugin.RegisterRegistrablesService.Start(), "OK");
            DrawTaskRow("Refill Listings", config.EnableRefillFromListings, GetRefillFromListingsStatus(config),
                "run##Listings", () =>
                {
                    plugin.ConfigManager.SaveCurrentAccount();
                    var activeConfig = plugin.ConfigManager.GetActiveConfig();
                    plugin.RetainerListingRefillService.Start(activeConfig);
                }, "OK");
            DrawTaskCategory("Manual utility", null);
            DrawTaskRow("Retainer Bell", true, plugin.WorkshopBellService.StatusText,
                "run##WorkshopBell", () =>
                {
                    plugin.ConfigManager.SaveCurrentAccount();
                    var activeConfig = plugin.ConfigManager.GetActiveConfig();
                    plugin.WorkshopBellService.Start(activeConfig.RefillFromListingsRoute);
                }, "OK");
            DrawTaskCategory("Ordered engine tasks (continued)", AutomationCatalog.Get(AutomationCatalog.SeasonalGear));
            DrawTaskRow("Seasonal Gear", config.EnableSeasonalGearRoulette, "Every AR run",
                "run##Seasonal", () => plugin.SeasonalGearService.RunTask(), "OK");
            DrawTaskRow("Minion Roulette", config.EnableMinionRoulette, "Every AR run",
                "run##Minion", () => plugin.MinionRouletteService.RunTask(), "OK");
            DrawTaskRow("Gear Updater", config.EnableGearUpdater, "Every AR run",
                "run##Gear", () => plugin.GearUpdaterService.RunTask(), "OK");

            // --- Weekly Tasks ---
            DrawTaskRow("Verminion (5x)", config.EnableVerminionQueue,
                GetWeeklyTaskStatus(config.VerminionLastCompleted, config.VerminionNextReset, "Done this week", "Weekly"),
                "run##Verm", () => plugin.VerminionService.RunTask(), "OK");
            DrawTaskRow("Jumbo Cactpot", config.EnableJumboCactpot,
                GetJumboCactpotStatus(config),
                "run##Jumbo", () => plugin.CactpotService.RunJumboCactpot(), "OK");
            DrawTaskRow("Fashion Report", config.EnableFashionReport,
                GetFashionReportStatus(config),
                "run##Fashion", () => plugin.FashionReportService.Start(), "OK");

            // --- Daily Tasks ---
            DrawTaskRow("Mini Cactpot", config.EnableMiniCactpot,
                GetDailyTaskStatus(config.MiniCactpotLastCompleted, config.MiniCactpotNextReset, "Done today", "Daily"),
                "run##Mini", () => plugin.CactpotService.RunMiniCactpot(), "OK");
            DrawTaskRow("Chocobo Racing", config.EnableChocoboRacing,
                GetDailyTaskStatus(config.ChocoboRacingLastCompleted, config.ChocoboRacingNextReset, "Done today", "Daily"),
                "run##Choco", () => plugin.ChocoboRaceService.RunTask(), "OK");
            var lootGoblinDailyStatus = GetDailyTaskStatus(
                config.LootGoblinMapGatherLastCompleted,
                config.LootGoblinMapGatherNextReset,
                "Done today",
                "Daily");
            var lootGoblinStatus = LootGoblinMapGatherRowPolicy.GetStatus(
                lootGoblinDailyStatus,
                plugin.LootGoblinMapGatherService.State,
                plugin.LootGoblinMapGatherService.StatusText);
            var lootGoblinStatusTooltip = plugin.LootGoblinMapGatherService.State == LootGoblinMapGatherServiceState.Idle
                ? lootGoblinDailyStatus
                : $"{plugin.LootGoblinMapGatherService.State}: {plugin.LootGoblinMapGatherService.StatusText}";
            DrawTaskRow("LootGoblin Map Gather", config.EnableLootGoblinMapGather,
                lootGoblinStatus,
                "run##LootGoblinMapGather", () =>
                {
                    var response = plugin.LootGoblinMapGatherManualRunCoordinator.Start(engine.IsRunning);
                    var result = response.Accepted
                        ? response.Terminal && response.Success ? "completed" : "accepted"
                        : "rejected";
                    var detail = string.IsNullOrWhiteSpace(response.Message) ? response.State : response.Message;
                    Plugin.ChatGui.Print($"[Vermaxion] LootGoblin map gather {result}: {detail}");
                }, "OK",
                statusTooltip: lootGoblinStatusTooltip,
                buttonDisabled: engine.IsRunning,
                buttonTooltip: "Manual map gather is unavailable while VERMAXION engine is running.");
            DrawTaskRow("nag your mom", config.EnableNagYourMom,
                GetNagYourMomStatus(config, engine.NagYourMomStatusText),
                "run##Mom", () =>
                {
                    var route = GetFirstDueNagYourMomRoute(config);
                    var remainingRuns = Math.Max(1, GetRemainingNagYourMomRuns(config, route));
                    var result = plugin.MomIPCClient.StartRun(remainingRuns, config.NagYourMomJob, route == MomRunRoutes.CasualCc && config.NagYourMomStopAtSeriesRank25, route);
                    Plugin.ChatGui.Print($"[Vermaxion] mom {result.Status}: {result.Summary} route={result.Route} runs={result.CompletedRunCount}/{result.RequestedRunCount}");
                }, "OK");
            DrawTaskRow("nag your dad", config.EnableNagYourDad,
                GetNagYourDadStatus(config, engine.NagYourDadStatusText, plugin.DadIPCClient.LastSubmissionStatus),
                "run##Dad", () =>
                {
                    var activeConfig = plugin.ConfigManager.GetActiveConfig();
                    var result = plugin.DadIPCClient.StartSelection(
                        activeConfig.NagYourDadSelectionKind,
                        activeConfig.NagYourDadSelectionId,
                        activeConfig.NagYourDadSelectionDisplayName);
                    Plugin.ChatGui.Print($"[Vermaxion] {result.StatusText}");
                }, "OK");
            DrawTaskCategory("Configuration-only WIP", AutomationCatalog.Get(AutomationCatalog.EvercoldAdventurerActivity));
            DrawTaskRow("Adventurer Activity (Evercold)", config.EnableEvercoldAdventurerActivity,
                GetEvercoldAdventurerActivityStatus(config),
                "Stub##EvercoldActivity", () =>
                {
                    Plugin.Log.Information("[EvercoldActivity] WIP stub requested from main window.");
                    Plugin.ChatGui.Print("[Vermaxion] Adventurer Activity (Evercold) is WIP. Progress is config-only for now.");
                }, "WIP");

            // --- Utility Tasks ---
            DrawTaskCategory("Ordered engine tasks (continued)", AutomationCatalog.Get(AutomationCatalog.HighestCombatJob));
            DrawTaskRow("Highest Combat Job", config.EnableHighestCombatJob, "Every AR run",
                "run##Highest", () => plugin.HighestCombatJobService.RunTask(), "OK");
            DrawTaskRow("Current Job Equipment", config.EnableCurrentJobEquipment, "Every AR run",
                "run##Current", () => plugin.CurrentJobEquipmentService.RunTask(), "OK");

            ImGui.EndTable();

            ImGui.Spacing();

            // Test Functions
            ImGui.BeginDisabled(plugin.DadHandoffBlocksNewWork);
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

            ImGui.SameLine();
            if (ImGui.SmallButton("Test Chocobo Rank"))
            {
                Plugin.Log.Information("[UI] Testing racing chocobo rank from GoldSaucerInfo node 21");
                plugin.ChocoboRaceService.RequestGoldSaucerRankTest();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Opens /goldsaucer and reads GoldSaucerInfo node 21, the fallback used when RaceChocoboManager is not loaded.");
            ImGui.TextDisabled($"Chocobo rank test: {plugin.ChocoboRaceService.GoldSaucerRankTestStatus}");
            
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
            ImGui.EndDisabled();
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
        var momIpcStatus = plugin.MomIPCClient.GetReadiness();
        ImGui.TextDisabled($"AR PostProcess: {arStatus}  |  {now.DayOfWeek}");
        ImGui.TextDisabled($"mom IPC: {momIpcStatus.Summary}  |  nag your mom: {engine.NagYourMomStatusText}");
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

    private static void DrawTaskCategory(string label, AutomationFeatureDefinition? feature)
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.TextColored(new Vector4(0.45f, 0.75f, 1f, 1f), label);
        if (feature != null && ImGui.IsItemHovered())
            ImGui.SetTooltip($"{feature.CadenceLabel} · {feature.OwnershipLabel}");
    }

    private void DrawTaskRow(
        string task,
        bool enabled,
        string status,
        string buttonLabel,
        Action onClick,
        string maturity = "-",
        string? statusTooltip = null,
        bool buttonDisabled = false,
        string? buttonTooltip = null,
        string? secondaryButtonLabel = null,
        Action? secondaryOnClick = null,
        bool secondaryButtonDisabled = false,
        string? secondaryButtonTooltip = null,
        string? tertiaryButtonLabel = null,
        Action? tertiaryOnClick = null,
        bool tertiaryButtonDisabled = false,
        string? tertiaryButtonTooltip = null)
    {
        buttonDisabled |= plugin.DadHandoffBlocksNewWork;
        secondaryButtonDisabled |= plugin.DadHandoffBlocksNewWork;
        tertiaryButtonDisabled |= plugin.DadHandoffBlocksNewWork;
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text(task);
        ImGui.TableSetColumnIndex(1);
        ImGui.TextColored(enabled ? new Vector4(0, 1, 0, 1) : new Vector4(1, 0, 0, 1), enabled ? "On" : "Off");
        ImGui.TableSetColumnIndex(2);
        ImGui.TextDisabled(status);
        if (!string.IsNullOrWhiteSpace(statusTooltip) && ImGui.IsItemHovered())
            ImGui.SetTooltip(statusTooltip);
        ImGui.TableSetColumnIndex(3);
        ImGui.BeginDisabled(buttonDisabled);
        if (ImGui.SmallButton(buttonLabel))
            onClick();
        ImGui.EndDisabled();
        if (!string.IsNullOrWhiteSpace(buttonTooltip) && ImGui.IsItemHovered())
            ImGui.SetTooltip(buttonTooltip);
        if (!string.IsNullOrWhiteSpace(secondaryButtonLabel) && secondaryOnClick != null)
        {
            ImGui.SameLine();
            ImGui.BeginDisabled(secondaryButtonDisabled);
            if (ImGui.SmallButton(secondaryButtonLabel))
                secondaryOnClick();
            ImGui.EndDisabled();
            if (!string.IsNullOrWhiteSpace(secondaryButtonTooltip) && ImGui.IsItemHovered())
                ImGui.SetTooltip(secondaryButtonTooltip);
        }
        if (!string.IsNullOrWhiteSpace(tertiaryButtonLabel) && tertiaryOnClick != null)
        {
            ImGui.SameLine();
            ImGui.BeginDisabled(tertiaryButtonDisabled);
            if (ImGui.SmallButton(tertiaryButtonLabel))
                tertiaryOnClick();
            ImGui.EndDisabled();
            if (!string.IsNullOrWhiteSpace(tertiaryButtonTooltip) && ImGui.IsItemHovered())
                ImGui.SetTooltip(tertiaryButtonTooltip);
        }
        ImGui.TableSetColumnIndex(4);
        
        // Color code maturity
        Vector4 color;
        switch (maturity)
        {
            case "OK":
            case "[OK]":
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

        return ResetDetectionService.IsFashionReportAvailable(DateTime.UtcNow) ? "Ready now" : "Pending (Fri 09 UTC)";
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

    private static string GetFishingStatus(Models.CharacterConfig config, string serviceStatus)
    {
        if (!string.IsNullOrWhiteSpace(serviceStatus) && serviceStatus != "Idle")
            return serviceStatus;

        if (!config.EnableFishing)
            return "Off";

        return "AutoHook casts";
    }

    private static string GetRefillFromListingsStatus(Models.CharacterConfig config)
    {
        if (!config.EnableRefillFromListings)
            return "Off";

        return config.RefillFromListingsFrequency switch
        {
            Models.RefillFromListingsFrequency.EveryAR => $"Every AR / {FormatRefillSelection(config.RefillFromListingsSelectionMode)}",
            Models.RefillFromListingsFrequency.Daily => ResetDetectionService.TaskIsCompleted(config.RefillFromListingsLastCompleted, config.RefillFromListingsNextReset)
                ? "Done today"
                : $"Daily / {FormatRefillSelection(config.RefillFromListingsSelectionMode)}",
            Models.RefillFromListingsFrequency.Monthly => IsRefillFromListingsMonthlyComplete(config)
                ? "Done this month"
                : $"Monthly / {FormatRefillSelection(config.RefillFromListingsSelectionMode)}",
            _ => ResetDetectionService.TaskIsCompleted(config.RefillFromListingsLastCompleted, config.RefillFromListingsNextReset)
                ? "Done this week"
                : $"Weekly / {FormatRefillSelection(config.RefillFromListingsSelectionMode)}",
        };
    }

    private static bool IsRefillFromListingsMonthlyComplete(Models.CharacterConfig config)
    {
        if (config.RefillFromListingsLastCompleted == DateTime.MinValue)
            return false;

        var now = DateTime.UtcNow;
        var lastCompleted = config.RefillFromListingsLastCompleted.ToUniversalTime();
        if (lastCompleted.Year != now.Year || lastCompleted.Month != now.Month)
            return false;

        return config.RefillFromListingsNextReset == DateTime.MinValue || now < config.RefillFromListingsNextReset.ToUniversalTime();
    }

    private static string FormatRefillSelection(Models.RefillFromListingsSelectionMode mode)
    {
        return mode == Models.RefillFromListingsSelectionMode.Random ? "Random" : "All";
    }

    private static string GetNagYourMomStatus(Models.CharacterConfig config, string engineStatus)
    {
        if (!config.EnableNagYourMom)
            return "Off";

        if (string.IsNullOrWhiteSpace(config.NagYourMomJob))
            return "Set job";

        if (!IsAnyNagYourMomRouteDue(config))
            return "Route caps hit";

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

    private static bool IsAnyNagYourMomRouteDue(Models.CharacterConfig config)
        => IsNagYourMomRouteDue(config, MomRunRoutes.CasualCc)
           || IsNagYourMomRouteDue(config, MomRunRoutes.Frontline)
           || IsNagYourMomRouteDue(config, MomRunRoutes.RivalWings);

    private static bool IsNagYourMomRouteDue(Models.CharacterConfig config, string route)
        => IsNagYourMomRouteEnabled(config, route)
           && GetNagYourMomRouteCap(config, route) > 0
           && GetRemainingNagYourMomRuns(config, route) > 0;

    private static string GetFirstDueNagYourMomRoute(Models.CharacterConfig config)
    {
        if (IsNagYourMomRouteDue(config, MomRunRoutes.CasualCc))
            return MomRunRoutes.CasualCc;
        if (IsNagYourMomRouteDue(config, MomRunRoutes.Frontline))
            return MomRunRoutes.Frontline;
        return MomRunRoutes.RivalWings;
    }

    private static int GetRemainingNagYourMomRuns(Models.CharacterConfig config, string route)
        => Math.Max(0, GetNagYourMomRouteCap(config, route) - GetNagYourMomRouteAttempts(config, route));

    private static bool IsNagYourMomRouteEnabled(Models.CharacterConfig config, string route)
        => route switch
        {
            MomRunRoutes.Frontline => config.EnableNagYourMomFrontline,
            MomRunRoutes.RivalWings => config.EnableNagYourMomRivalWings,
            _ => config.EnableNagYourMomCasualCc,
        };

    private static int GetNagYourMomRouteCap(Models.CharacterConfig config, string route)
        => route switch
        {
            MomRunRoutes.Frontline => config.NagYourMomFrontlineRunsPerDay,
            MomRunRoutes.RivalWings => config.NagYourMomRivalWingsRunsPerDay,
            _ => config.NagYourMomRunsPerDay,
        };

    private static int GetNagYourMomRouteAttempts(Models.CharacterConfig config, string route)
        => route switch
        {
            MomRunRoutes.Frontline => config.NagYourMomFrontlineAttemptsToday,
            MomRunRoutes.RivalWings => config.NagYourMomRivalWingsAttemptsToday,
            _ => config.NagYourMomAttemptsToday,
        };

    private static string GetNagYourDadStatus(
        Models.CharacterConfig config,
        string engineStatus,
        string lastSubmissionStatus)
    {
        if (!config.EnableNagYourDad)
            return "Off";

        if (config.NagYourDadSelectionKind == DadSelectionKind.None ||
            string.IsNullOrWhiteSpace(config.NagYourDadSelectionId))
            return "Select DAD work";

        if (!string.IsNullOrWhiteSpace(engineStatus) && engineStatus != "Idle")
            return engineStatus;

        return string.IsNullOrWhiteSpace(lastSubmissionStatus)
            ? "Ready on AR"
            : lastSubmissionStatus;
    }

    private static string GetEvercoldAdventurerActivityStatus(Models.CharacterConfig config)
    {
        if (!config.EnableEvercoldAdventurerActivity)
            return "Off";

        if (config.EvercoldAdventurerActivityCompleted)
            return "Done";

        if (config.EvercoldAdventurerActivityTargetPoints <= 0)
            return "Set point cap";

        var current = Math.Clamp(config.EvercoldAdventurerActivityCurrentPoints, 0, config.EvercoldAdventurerActivityTargetPoints);
        if (current >= config.EvercoldAdventurerActivityTargetPoints)
            return "Done";

        return $"{current}/{config.EvercoldAdventurerActivityTargetPoints} pts";
    }

}
