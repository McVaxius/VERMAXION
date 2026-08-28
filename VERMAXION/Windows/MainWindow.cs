using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.Loader;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using ECommons.Reflection;
using VERMAXION.Models;
using VERMAXION.Services;

namespace VERMAXION.Windows;

public class MainWindow : Window, IDisposable
{
    private static readonly string[] TaskDependencyInternalNames =
    [
        "XADatabase",
        "AutoRetainer",
        "Lifestream",
        "AutoHook",
        "vnavmesh",
        "YesAlready",
        "ADS",
        "TeleporterPlugin",
        "Saucy",
        "TextAdvance",
        "XASlave",
        "QSTCompanion",
        "LootGoblin",
        "mom",
        "dad",
    ];

    private static readonly string[] RetainerBellSessionAddonNames =
    [
        "RetainerList",
        "RetainerCharacter",
        "RetainerSellList",
        "RetainerSell",
        "RetainerItemTransferList",
        "InventoryRetainerLarge",
        "InventoryRetainer",
        "RetainerGrid0",
        "RetainerGrid1",
        "RetainerGrid2",
        "RetainerGrid3",
        "RetainerGrid4",
        "RetainerCrystalGrid",
        "RetainerTaskAsk",
        "RetainerTaskResult",
    ];

    private readonly Plugin plugin;
    private readonly RetainerEquippingArProbeCache retainerEquippingReadinessCache =
        new(TimeSpan.FromSeconds(5));
    private List<TaskRowDescriptor>? taskRowsBeingBuilt;

    public MainWindow(Plugin plugin)
        : base(
            "Vermaxion##Main",
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
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

        var oceanFishingWindowWatch = plugin.Configuration.OceanFishingWindowWatchEnabled;
        if (ImGui.Checkbox("Actively check for Ocean Fishing windows without AR pre/post process", ref oceanFishingWindowWatch))
        {
            plugin.Configuration.OceanFishingWindowWatchEnabled = oceanFishingWindowWatch;
            plugin.Configuration.Save();
        }
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("This will relog per your configured fishing settings and process Vermaxion Ocean Fishing as normal.");

        if (plugin.Configuration.KrangleEnabled && !string.IsNullOrEmpty(charKey))
            displayName = KrangleService.KrangleName(charKey);

        ImGui.Text($"Character: {displayName}");
        var account = plugin.ConfigManager.GetCurrentAccount();
        ImGui.SameLine();
        ImGui.TextDisabled($"Account: {(string.IsNullOrWhiteSpace(account?.AccountAlias) ? "Not selected" : account.AccountAlias)}");
        ImGui.SameLine();
        var enabled = plugin.Configuration.Enabled;
        if (ImGui.Checkbox("Enabled", ref enabled))
        {
            plugin.Configuration.Enabled = enabled;
            plugin.Configuration.Save();
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

        var pendingTasks = engine.GetPendingTaskCount();
        var readinessText = $"Engine readiness: {(engine.RegistryReady ? "Ready" : "Not ready")} · {engine.StatusText} (State: {engine.State})";
        if (pendingTasks > 0)
            readinessText += $" · {pendingTasks} pending";
        ImGui.TextColored(stateColor, readinessText);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(readinessText);
        if (!engine.RegistryReady)
        {
            ImGui.TextColored(new Vector4(1f, 0.15f, 0.15f, 1f), "CONFIGURED BUT NOT DISPATCHABLE");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(engine.RegistryDiagnostic);
            ImGui.SameLine();
            if (ImGui.SmallButton("Open Task Order"))
                plugin.ConfigWindow.OpenTaskOrder();
        }
        if (!string.IsNullOrWhiteSpace(engine.ActiveHandoffBlocker))
        {
            ImGui.TextColored(new Vector4(1f, 0.65f, 0f, 1f), $"Handoff blocker: {engine.ActiveHandoffBlocker}");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(engine.ActiveHandoffBlocker);
        }

        // Control buttons row
        // FULL STOP button - red only when plugin is in operation
        var highlightFullStop = engine.OwnsLiveWork ||
                                plugin.LootGoblinMapGatherManualRunCoordinator.IsActive ||
                                plugin.FishingService.IsActive ||
                                plugin.FishingRelogCoordinator.IsActive ||
                                plugin.GearUpdaterService.IsActive ||
                                plugin.HighestCombatJobService.IsActive ||
                                plugin.CurrentJobEquipmentService.IsActive ||
                                plugin.SeasonalGearService.IsActive ||
                                plugin.AlliedSocietyService.IsActive ||
                                plugin.AlliedSocietyService.OwnsRotation ||
                                plugin.AfterArParkService.IsActive ||
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

        void DrawTaskSurface(bool favoritesOnly)
        {
            taskRowsBeingBuilt = new List<TaskRowDescriptor>();
            var loadedPluginInternalNames = GetLoadedTaskDependencyNames();

        // Task table with run buttons
        var autoWidthTaskColumns = plugin.Configuration.AutoWidthMainTaskColumns;
        var taskTableFlags = ImGuiTableFlags.Borders |
                             ImGuiTableFlags.RowBg |
                             ImGuiTableFlags.SizingStretchProp;
        taskTableFlags |= autoWidthTaskColumns
            ? ImGuiTableFlags.NoSavedSettings
            : ImGuiTableFlags.Resizable;
        var taskTableId = autoWidthTaskColumns ? "TasksTableAutoWidth" : "TasksTable";
        if (ImGui.BeginTable(taskTableId, 6, taskTableFlags))
        {
            ImGui.TableSetupColumn("★", ImGuiTableColumnFlags.WidthFixed, autoWidthTaskColumns ? 0f : 28f);
            ImGui.TableSetupColumn("Task", ImGuiTableColumnFlags.WidthStretch, 1.8f);
            ImGui.TableSetupColumn("When", ImGuiTableColumnFlags.WidthFixed, autoWidthTaskColumns ? 0f : 94f);
            ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthFixed, autoWidthTaskColumns ? 0f : 98f);
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, autoWidthTaskColumns ? 0f : 142f);
            ImGui.TableSetupColumn("Dependencies", ImGuiTableColumnFlags.WidthFixed, autoWidthTaskColumns ? 0f : 112f);
            DrawTaskTableHeaders();

            // --- Every AR PostProcess ---
            DrawTaskCategory("Run-start hook", AutomationCatalog.Get(AutomationCatalog.MiscCommands));
            DrawTaskRow("Misc Cmd", config.EnableMiscCmd,
                config.EnableMiscCmd ? AutomationCatalog.Get(AutomationCatalog.MiscCommands).CadenceLabel : "Off",
                "run##MiscCmd", () => plugin.Engine.SendRunShutdownCommandBundle(), "OK");
            DrawTaskCategory("Ordered engine tasks", AutomationCatalog.Get(AutomationCatalog.FCBuffRefill));
            DrawTaskRow("FC Buff Refill", config.EnableFCBuffRefill, AutomationCatalog.Get(AutomationCatalog.FCBuffRefill).CadenceLabel,
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
            DrawTaskRow("Register Registrables", config.EnableRegisterRegistrables, AutomationCatalog.Get(AutomationCatalog.RegisterRegistrables).CadenceLabel,
                "run##Register", () => plugin.RegisterRegistrablesService.Start(), "OK");
            DrawTaskRow("Refill Listings", config.EnableRefillFromListings, GetRefillFromListingsStatus(config),
                "run##Listings", () =>
                {
                    plugin.ConfigManager.SaveCurrentAccount();
                    var activeConfig = plugin.ConfigManager.GetActiveConfig();
                    plugin.RetainerListingRefillService.Start(activeConfig);
                }, "OK");
            var retainerEquippingFeature = AutomationCatalog.Get(AutomationCatalog.RetainerEquipping);
            var retainerEquippingReadiness = GetRetainerEquippingReadiness(config, forceRefresh: false);
            var retainerEquippingExecuting =
                engine.State == VermaxionEngine.EngineState.RunningRetainerEquipping;
            var retainerEquippingStatus = retainerEquippingExecuting
                ? plugin.RetainerEquippingService.StatusText
                : retainerEquippingReadiness.StatusText;
            var retainerEquippingTooltip = retainerEquippingExecuting
                ? plugin.RetainerEquippingService.StatusText
                : retainerEquippingReadiness.DisabledReason;
            DrawTaskRow(
                retainerEquippingFeature.Label,
                config.EnableRetainerEquipping,
                retainerEquippingStatus,
                "run##RetainerEquipping",
                RunRetainerEquipping,
                retainerEquippingFeature.Maturity == AutomationMaturity.Wip ? "WIP" : "OK",
                statusTooltip: retainerEquippingTooltip,
                buttonDisabled: !retainerEquippingReadiness.CanRun,
                buttonTooltip: retainerEquippingReadiness.CanRun
                    ? "Run only Retainer Equipping. This explicit run ignores its scheduling checkbox."
                    : retainerEquippingReadiness.DisabledReason);
            DrawTaskCategory("Manual utility", null);
            DrawTaskRow("Retainer Bell", true, plugin.WorkshopBellService.StatusText,
                "run##WorkshopBell", () =>
                {
                    plugin.ConfigManager.SaveCurrentAccount();
                    var activeConfig = plugin.ConfigManager.GetActiveConfig();
                    plugin.WorkshopBellService.Start(activeConfig.RefillFromListingsRoute);
                }, "OK");
            var equipmentAutomationBusy = IsEquipmentAutomationBusy();
            DrawTaskRow("Bootstrap Gearsets", true, plugin.GearUpdaterService.StatusText,
                "run##BootstrapGearsets", plugin.GearUpdaterService.StartBootstrap, "OK",
                buttonDisabled: equipmentAutomationBusy,
                buttonTooltip: equipmentAutomationBusy
                    ? "An engine or equipment task is active."
                    : "Persist the current job, then bootstrap missing unlocked class/job gearsets from already-owned main hands.");
            DrawTaskCategory("Ordered engine tasks (continued)", AutomationCatalog.Get(AutomationCatalog.SeasonalGear));
            DrawTaskRow("Seasonal Gear", config.EnableSeasonalGearRoulette, AutomationCatalog.Get(AutomationCatalog.SeasonalGear).CadenceLabel,
                "run##Seasonal", () => plugin.SeasonalGearService.RunTask(), "OK");
            DrawTaskRow("Minion Roulette", config.EnableMinionRoulette, AutomationCatalog.Get(AutomationCatalog.MinionRoulette).CadenceLabel,
                "run##Minion", () => plugin.MinionRouletteService.RunTask(), "OK");
            DrawTaskRow("Gear Updater", config.EnableGearUpdater, AutomationCatalog.Get(AutomationCatalog.GearUpdater).CadenceLabel,
                "run##Gear", () => plugin.GearUpdaterService.RunTask(), "OK");
            DrawTaskRow("After-AR Park", config.EnableAfterArPark, GetAfterArParkStatus(config),
                "run##AfterArPark", () => plugin.AfterArParkService.Start(config), "OK",
                buttonDisabled: engine.IsRunning || plugin.AfterArParkService.IsActive ||
                                !AfterArParkService.TryResolveCommand(
                                    config.AfterArParkDestination,
                                    config.AfterArParkCustomCommand,
                                    out _,
                                    out _),
                buttonTooltip: "Issues the configured /li route once and waits for Lifestream/player settlement.");

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
            var alliedGearsetValid = IsAlliedSocietyGearsetValid(config);
            DrawTaskRow("Allied Society", config.EnableAlliedSociety,
                GetAlliedSocietyStatus(config),
                "run##AlliedSociety", () => plugin.AlliedSocietyService.Start(config), "OK",
                buttonDisabled: equipmentAutomationBusy || !alliedGearsetValid,
                buttonTooltip: alliedGearsetValid
                    ? "Runs Questionable Companion's Allied Society rotation for only the current Name@HomeWorld character."
                    : "Select a valid current or saved gearset before starting.");
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
                }, "OK",
                secondaryButtonLabel: "Test Series Rank##MomSeriesRank",
                secondaryOnClick: engine.TestNagYourMomSeriesRank,
                secondaryButtonTooltip: "Read the current PvP series rank once without starting mom or changing configuration.");
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
            DrawTaskRow("Highest Combat Job", config.EnableHighestCombatJob, AutomationCatalog.Get(AutomationCatalog.HighestCombatJob).CadenceLabel,
                "run##Highest", () => plugin.HighestCombatJobService.RunTask(), "OK");
            DrawTaskRow("Current Job Equipment", config.EnableCurrentJobEquipment, AutomationCatalog.Get(AutomationCatalog.CurrentJobEquipment).CadenceLabel,
                "run##Current", () => plugin.CurrentJobEquipmentService.RunTask(), "OK");

            DrawDashboardRows(taskRowsBeingBuilt, favoritesOnly, loadedPluginInternalNames);

            ImGui.EndTable();
        }

        ImGui.Spacing();

        if (!favoritesOnly && ImGui.CollapsingHeader("Advanced test controls"))
        {
            // Test Functions
            ImGui.BeginDisabled(plugin.DadHandoffBlocksNewWork);
            ImGui.Text("Test Functions");
            ImGui.Separator();

            var fishingTestDisabled = engine.IsRunning ||
                                      plugin.IsFishingRunActive ||
                                      plugin.FisherGearsetTestService.IsActive;
            ImGui.BeginDisabled(fishingTestDisabled);
            if (ImGui.SmallButton("Ocean Fishing account test"))
                plugin.RunFishingStartupTest();
            ImGui.SameLine();
            if (ImGui.SmallButton("Current Fisher gearset test"))
                plugin.RunFishingGearsetTest();
            ImGui.EndDisabled();
            
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

        if (favoritesOnly && (taskRowsBeingBuilt?.All(row => row.Feature == null || !IsFavorite(row.Feature.Id)) ?? true))
            ImGui.TextWrapped("No favorite tasks yet. Open All Tasks and select the star beside any automation to add it here.");
        taskRowsBeingBuilt = null;
        }

        if (ImGui.BeginChild("MainBody", new Vector2(0, 0), false))
        {
            if (ImGui.BeginTabBar("MainTaskTabs"))
            {
                if (ImGui.BeginTabItem("All Tasks"))
                {
                    DrawTaskSurface(false);
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Favorites"))
                {
                    DrawTaskSurface(true);
                    ImGui.EndTabItem();
                }
                ImGui.EndTabBar();
            }

            ImGui.Spacing();

            if (ImGui.CollapsingHeader("Advanced diagnostics"))
            {
                var lastRunTime = engine.LastRunCompletedAtUtc?.ToLocalTime().ToString("g") ?? "never";
                ImGui.TextWrapped($"Last run: {engine.LastRunOutcome} at {lastRunTime} - {engine.LastRunSummary}");
                ImGui.TextWrapped($"Before-AR gate: {plugin.BeforeArGate} - {plugin.BeforeArStatusText}");
                ImGui.TextWrapped($"AR suppression: {plugin.AutoRetainerIPC.LastSnapshot}");

                var characterSelectRecovery = plugin.CharacterSelectStallRecovery;
                var characterSelectEligibility = characterSelectRecovery.GetEligibility(
                    plugin.Configuration.EnableCharacterSelectStallRecovery);
                ImGui.TextWrapped(
                    $"Character-select recovery: {(plugin.Configuration.EnableCharacterSelectStallRecovery ? "On" : "Off")} - {characterSelectRecovery.StatusText}");
                var characterSelectBlockedReason = characterSelectEligibility.CanAttempt
                    ? characterSelectRecovery.LastBlockedReason
                    : characterSelectEligibility.Reason;
                if (!string.IsNullOrWhiteSpace(characterSelectBlockedReason))
                    ImGui.TextWrapped($"Character-select recovery blocker: {characterSelectBlockedReason}");
                ImGui.BeginDisabled(!characterSelectEligibility.CanAttempt);
                if (ImGui.SmallButton("Load first character now"))
                    plugin.QueueCharacterSelectRecoveryAttempt();
                ImGui.EndDisabled();
                if (!characterSelectEligibility.CanAttempt && ImGui.IsItemHovered())
                    ImGui.SetTooltip(characterSelectEligibility.Reason);

                var now = DateTime.UtcNow;
                var nextDaily = ResetDetectionService.GetLastDailyReset(now).AddDays(1);
                var nextWeekly = ResetDetectionService.GetLastWeeklyReset(now).AddDays(7);
                var untilDaily = nextDaily - now;
                var untilWeekly = nextWeekly - now;
                var nextFriday = ResetDetectionService.GetNextFashionReportAvailability(now);
                var untilFriday = nextFriday - now;
                var nextJumboPayout = ResetDetectionService.GetNextJumboCactpotPayoutAvailability(now);
                var untilJumboPayout = nextJumboPayout - now;
                ImGui.TextWrapped($"Daily: {untilDaily.Hours}h {untilDaily.Minutes}m | Weekly: {untilWeekly.Days}d {untilWeekly.Hours}h {untilWeekly.Minutes}m | Fashion: {untilFriday.Days}d {untilFriday.Hours}h {untilFriday.Minutes}m | Jumbo payout: {untilJumboPayout.Days}d {untilJumboPayout.Hours}h {untilJumboPayout.Minutes}m");

                var arStatus = plugin.ARPostProcessService.IsProcessing ? "Processing" : "Waiting";
                var momIpcStatus = plugin.MomIPCClient.GetReadiness();
                ImGui.TextWrapped($"AR PostProcess: {arStatus} | {now.DayOfWeek}");
                ImGui.TextWrapped($"mom IPC: {momIpcStatus.Summary} | nag your mom: {engine.NagYourMomStatusText}");
                ImGui.TextWrapped($"dad IPC: {(plugin.DadIPCClient.IsReady() ? "Ready" : "Unavailable")} | nag your dad: {engine.NagYourDadStatusText}");
            }
        }
        ImGui.EndChild();
    }

    private void RunRetainerEquipping()
    {
        var config = plugin.ConfigManager.GetActiveConfig();
        var readiness = GetRetainerEquippingReadiness(config, forceRefresh: true);
        if (!readiness.CanRun)
        {
            Plugin.ChatGui.Print($"[Vermaxion] Retainer Equipping cannot run: {readiness.DisabledReason}");
            return;
        }

        if (!plugin.Engine.ManualStartRetainerEquipping())
        {
            Plugin.ChatGui.Print(
                "[Vermaxion] Retainer Equipping could not start because the engine start boundary changed.");
        }
    }

    private RetainerEquippingReadinessResult GetRetainerEquippingReadiness(
        CharacterConfig config,
        bool forceRefresh)
    {
        var contentId = Plugin.PlayerState.ContentId;
        var loggedIn = Plugin.ClientState.IsLoggedIn &&
                       Plugin.ObjectTable.LocalPlayer != null &&
                       contentId != 0;
        var bellSessionActive = Plugin.Condition[ConditionFlag.OccupiedSummoningBell] ||
                                plugin.WorkshopBellService.IsActive ||
                                RetainerBellSessionAddonNames.Any(GameHelpers.IsAddonVisible);
        var unprobed = RetainerEquippingArProbe.BusyReadFailed("not probed");
        var snapshot = new RetainerEquippingReadinessSnapshot(
            loggedIn,
            plugin.DadHandoffBlocksNewWork,
            plugin.Engine.IsRunning,
            bellSessionActive,
            config.RetainerCombatItemLevelTarget,
            config.RetainerGatheringPerceptionTarget,
            unprobed);
        var immediate = RetainerEquippingReadinessPolicy.Evaluate(snapshot);
        if (immediate.DisabledReason is RetainerEquippingReadinessPolicy.LoggedOutReason or
            RetainerEquippingReadinessPolicy.DadOwnershipReason or
            RetainerEquippingReadinessPolicy.EngineActiveReason or
            RetainerEquippingReadinessPolicy.BellSessionReason or
            RetainerEquippingReadinessPolicy.ZeroTargetsReason)
        {
            return immediate;
        }

        var cacheKey =
            $"{contentId:X16}:{config.RetainerCombatItemLevelTarget}:{config.RetainerGatheringPerceptionTarget}";
        var probe = retainerEquippingReadinessCache.GetOrRefresh(
            cacheKey,
            DateTime.UtcNow,
            forceRefresh,
            () => ReadRetainerEquippingArProbe(contentId));
        return RetainerEquippingReadinessPolicy.Evaluate(snapshot with { AutoRetainer = probe });
    }

    private RetainerEquippingArProbe ReadRetainerEquippingArProbe(ulong contentId)
    {
        var busy = plugin.AutoRetainerIPC.ReadBusyState();
        if (!busy.Success)
            return RetainerEquippingArProbe.BusyReadFailed(busy.Error);
        if (busy.Busy)
            return RetainerEquippingArProbe.Busy();

        var retainers = plugin.AutoRetainerIPC.ReadEnabledRetainers(contentId);
        return retainers.Success
            ? RetainerEquippingArProbe.Idle(retainers.Retainers)
            : RetainerEquippingArProbe.RetainerReadFailed(retainers.Error);
    }

    private void DrawTaskCategory(string label, AutomationFeatureDefinition? feature)
    {
        // Category calls remain beside their row definitions; the dashboard renders explicit state sections.
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
        var feature = GetDisplayedFeature(task);
        var eligibility = GetDashboardEligibility(feature, enabled, buttonDisabled, buttonTooltip, status);
        var dadHandoffBlocker = plugin.DadHandoffBlocksNewWork
            ? "A granted or pending DAD handoff reservation blocks new VERMAXION work."
            : null;
        var row = new TaskRowDescriptor(
            feature,
            task,
            enabled,
            status,
            eligibility,
            AutomationDashboardPolicy.Classify(
                eligibility.Status,
                IsCompletedStatus(status),
                feature?.Id,
                eligibility.Reason),
            GetNextEligibleAt(feature?.Id, plugin.ConfigManager.GetActiveConfig()),
            feature == null
                ? null
                : AutomationDashboardPolicy.GetRecoverySection(feature.Id, eligibility.Reason),
            GetTaskDependencies(task, plugin.ConfigManager.GetActiveConfig()),
            buttonLabel,
            onClick,
            maturity,
            statusTooltip,
            buttonDisabled || plugin.DadHandoffBlocksNewWork,
            dadHandoffBlocker ?? buttonTooltip,
            secondaryButtonLabel,
            secondaryOnClick,
            secondaryButtonDisabled || plugin.DadHandoffBlocksNewWork,
            dadHandoffBlocker ?? secondaryButtonTooltip,
            tertiaryButtonLabel,
            tertiaryOnClick,
            tertiaryButtonDisabled || plugin.DadHandoffBlocksNewWork,
            dadHandoffBlocker ?? tertiaryButtonTooltip);
        taskRowsBeingBuilt?.Add(row);
    }

    private static HashSet<string> GetLoadedTaskDependencyNames()
    {
        var loaded = new HashSet<string>(StringComparer.Ordinal);
        foreach (var internalName in TaskDependencyInternalNames)
        {
            try
            {
                if (DalamudReflector.TryGetDalamudPlugin(
                        internalName,
                        out object pluginInstance,
                        out AssemblyLoadContext? _,
                        true,
                        true) &&
                    pluginInstance != null)
                {
                    loaded.Add(internalName);
                }
            }
            catch
            {
                // Dependency status is informational; reflection failures count as not loaded.
            }
        }

        return loaded;
    }

    private static IReadOnlyList<string> GetTaskDependencies(string task, CharacterConfig config)
    {
        switch (task)
        {
            case "FC Buff Refill":
            case "Vendor Stock":
                return ["Lifestream", "vnavmesh"];
            case "Fishing":
            {
                var dependencies = new List<string>
                {
                    "XADatabase",
                    "AutoRetainer",
                    "Lifestream",
                    "AutoHook",
                    "vnavmesh",
                    "YesAlready",
                };
                if (config.FishingRepairMode != FishingRepairMode.Disabled ||
                    config.FishingStockItems.Values.Any(stock => stock.Enabled && stock.Target > 0))
                {
                    dependencies.Add("ADS");
                }
                return dependencies;
            }
            case "Refill Listings":
                return ["AutoRetainer", "Lifestream", "vnavmesh"];
            case "Retainer Equipping":
                return ["AutoRetainer"];
            case "Retainer Bell":
                return ["Lifestream", "vnavmesh"];
            case "After-AR Park":
            case "Verminion (5x)":
            case "Chocobo Racing":
                return ["Lifestream"];
            case "Jumbo Cactpot":
            case "Fashion Report":
                return ["Lifestream", "vnavmesh"];
            case "Mini Cactpot":
                return config.RequireSaucyForMiniCactpot
                    ? ["Teleporter", "Lifestream", "vnavmesh", "Saucy"]
                    : ["Teleporter", "Lifestream", "vnavmesh"];
            case "Allied Society":
                return ["QSTCompanion"];
            case "LootGoblin Map Gather":
                return ["LootGoblin"];
            case "nag your mom":
                return ["mom"];
            case "nag your dad":
                return ["dad"];
            default:
                return [];
        }
    }

    private static void DrawTaskTableHeaders()
    {
        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
        ImGui.TableSetColumnIndex(0);
        ImGui.TableHeader("★");
        ImGui.TableSetColumnIndex(1);
        ImGui.TableHeader("Task");
        ImGui.TableSetColumnIndex(2);
        ImGui.TableHeader("When");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "Next dashboard timing: Now, Blocked, Off, Done, or the next eligible local date and time.");
        }
        ImGui.TableSetColumnIndex(3);
        ImGui.TableHeader("Type");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "Owner / cadence\n" +
                "Owners: ENG ordered engine task; HOOK run-start hook; COORD preemptive coordinator; " +
                "WIP config-only WIP; ROUTE child option; MAN manual utility.\n" +
                "Cadence: RUN every applicable run; DAY daily; WK weekly; SCH scheduled; " +
                "WIN coordinator window; CFG config only. WIP is appended for work-in-progress features.");
        }
        ImGui.TableSetColumnIndex(4);
        ImGui.TableHeader("Actions");
        ImGui.TableSetColumnIndex(5);
        ImGui.TableHeader("Dependencies");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Informational only. Reports Ready, Missing, or Needs setup; task eligibility is unchanged except Fishing validates its provider before run acquisition.");
    }

    private void DrawDashboardRows(
        IReadOnlyList<TaskRowDescriptor> rows,
        bool favoritesOnly,
        IReadOnlySet<string> loadedPluginInternalNames)
    {
        var catalogRows = rows.Where(row => row.Feature != null).ToList();
        if (favoritesOnly)
        {
            foreach (var row in catalogRows.Where(row => IsFavorite(row.Feature!.Id)))
                DrawDashboardRow(row, showDiagnosticActions: false, loadedPluginInternalNames: loadedPluginInternalNames);
            return;
        }

        foreach (var section in Enum.GetValues<AutomationDashboardSection>())
        {
            var sectionRows = catalogRows.Where(row => row.Section == section).ToList();
            if (sectionRows.Count == 0)
                continue;

            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(1);
            ImGui.TextColored(
                GetSectionColor(section),
                $"{AutomationDashboardPolicy.GetStateLabel(section)} ({sectionRows.Count})");
            foreach (var row in sectionRows)
                DrawDashboardRow(row, showDiagnosticActions: true, loadedPluginInternalNames: loadedPluginInternalNames);
        }

        var manualRows = rows.Where(row => row.Feature == null).ToList();
        if (manualRows.Count > 0)
        {
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(1);
            ImGui.TextColored(new Vector4(0.45f, 0.75f, 1f, 1f), $"Manual utilities ({manualRows.Count})");
            foreach (var row in manualRows)
                DrawDashboardRow(row, showDiagnosticActions: true, loadedPluginInternalNames: loadedPluginInternalNames);
        }
    }

    private void DrawDashboardRow(
        TaskRowDescriptor row,
        bool showDiagnosticActions,
        IReadOnlySet<string> loadedPluginInternalNames)
    {
        var isFavorite = row.Feature != null && IsFavorite(row.Feature.Id);
        var dependencySummary = BuildTaskDependencySummary(row, loadedPluginInternalNames);
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        if (row.Feature != null)
        {
            if (ImGui.SmallButton($"{(isFavorite ? "★" : "☆")}##Favorite_{row.Feature.Id}"))
            {
                plugin.Configuration.FavoriteAutomationIds = AutomationCatalog.ToggleFavorite(
                    plugin.Configuration.FavoriteAutomationIds,
                    row.Feature.Id);
                plugin.Configuration.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(isFavorite ? "Remove from Favorites" : "Add to Favorites");
        }

        ImGui.TableSetColumnIndex(1);
        if (dependencySummary.State == TaskDependencyState.Ready)
            ImGui.TextUnformatted(row.Task);
        else
            ImGui.TextColored(new Vector4(1f, 0.75f, 0.15f, 1f), row.Task);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"{BuildTaskTooltip(row)}\nDependencies: {dependencySummary.Tooltip}");

        ImGui.TableSetColumnIndex(2);
        ImGui.TextColored(GetSectionColor(row.Section), FormatWhen(row));

        ImGui.TableSetColumnIndex(3);
        ImGui.TextUnformatted(FormatType(row));

        ImGui.TableSetColumnIndex(4);
        ImGui.BeginDisabled(row.ButtonDisabled);
        if (ImGui.SmallButton(row.ButtonLabel))
            row.OnClick();
        ImGui.EndDisabled();
        if (!string.IsNullOrWhiteSpace(row.ButtonTooltip) && ImGui.IsItemHovered())
            ImGui.SetTooltip(row.ButtonTooltip);

        if (row.Section == AutomationDashboardSection.Blocked && row.RecoverySection.HasValue)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton($"Settings##Recovery_{row.Feature?.Id}"))
                plugin.ConfigWindow.OpenAutomationSettings(row.RecoverySection.Value);
        }

        if (showDiagnosticActions && !string.IsNullOrWhiteSpace(row.SecondaryButtonLabel) && row.SecondaryOnClick != null)
        {
            ImGui.SameLine();
            ImGui.BeginDisabled(row.SecondaryButtonDisabled);
            if (ImGui.SmallButton(row.SecondaryButtonLabel))
                row.SecondaryOnClick();
            ImGui.EndDisabled();
            if (!string.IsNullOrWhiteSpace(row.SecondaryButtonTooltip) && ImGui.IsItemHovered())
                ImGui.SetTooltip(row.SecondaryButtonTooltip);
        }
        if (showDiagnosticActions && !string.IsNullOrWhiteSpace(row.TertiaryButtonLabel) && row.TertiaryOnClick != null)
        {
            ImGui.SameLine();
            ImGui.BeginDisabled(row.TertiaryButtonDisabled);
            if (ImGui.SmallButton(row.TertiaryButtonLabel))
                row.TertiaryOnClick();
            ImGui.EndDisabled();
            if (!string.IsNullOrWhiteSpace(row.TertiaryButtonTooltip) && ImGui.IsItemHovered())
                ImGui.SetTooltip(row.TertiaryButtonTooltip);
        }

        ImGui.TableSetColumnIndex(5);
        DrawTaskDependencies(dependencySummary);
    }

    private TaskDependencySummary BuildTaskDependencySummary(
        TaskRowDescriptor row,
        IReadOnlySet<string> loadedPluginInternalNames)
    {
        var checks = row.Dependencies
            .Select(name => BuildDependencyCheck(row.Task, name, loadedPluginInternalNames))
            .ToList();

        if (row.Task is "Mini Cactpot" or "Jumbo Cactpot" or "Fashion Report")
        {
            checks.Add(TaskDependencyPolicy.Alternative(
                "Dialogue automation (TextAdvance or XA Slave Skip Dialogue)",
                BuildTextAdvanceCheck(loadedPluginInternalNames),
                BuildXaSlaveSkipDialogueCheck(loadedPluginInternalNames)));
        }

        return TaskDependencyPolicy.Aggregate(checks);
    }

    private TaskDependencyCheck BuildDependencyCheck(
        string task,
        string name,
        IReadOnlySet<string> loadedPluginInternalNames)
    {
        var internalName = name == "Teleporter" ? "TeleporterPlugin" : name;
        var loaded = loadedPluginInternalNames.Contains(internalName);
        if (task == "Fishing" && name == "AutoHook")
        {
            if (!loaded)
                return TaskDependencyCheck.Loaded(name, false);

            var read = plugin.AutoHookIPC.ReadAutoOceanFish();
            var provider = plugin.Configuration.OceanFishingProvider;
            return TaskDependencyPolicy.FishingProviderAlignment(
                provider,
                autoHookLoaded: true,
                read.Success,
                read.Enabled,
                read.Status);
        }

        if (task == "Mini Cactpot" && name == "Saucy")
        {
            var status = "Saucy is not loaded.";
            var configured = loaded && SaucyMiniCactpotService.TryValidateConfiguration(out status);
            return TaskDependencyCheck.Configured(name, loaded, configured, status);
        }

        return TaskDependencyCheck.Loaded(name, loaded);
    }

    private static TaskDependencyCheck BuildTextAdvanceCheck(
        IReadOnlySet<string> loadedPluginInternalNames)
    {
        var loaded = loadedPluginInternalNames.Contains("TextAdvance");
        var enabled = false;
        var status = "TextAdvance is not loaded.";
        var readable = loaded && DependencyConfigurationInspector.TryReadTextAdvanceEnabled(
            Plugin.PluginInterface,
            out enabled,
            out status);
        return TaskDependencyCheck.Configured("TextAdvance", loaded, readable && enabled, status);
    }

    private static TaskDependencyCheck BuildXaSlaveSkipDialogueCheck(
        IReadOnlySet<string> loadedPluginInternalNames)
    {
        var loaded = loadedPluginInternalNames.Contains("XASlave");
        var enabled = false;
        var status = "XA Slave is not loaded.";
        var readable = loaded && DependencyConfigurationInspector.TryReadXaSlaveSkipDialogueEnabled(
            out enabled,
            out status);
        return TaskDependencyCheck.Configured("XA Slave Skip Dialogue", loaded, readable && enabled, status);
    }

    private static void DrawTaskDependencies(TaskDependencySummary summary)
    {
        if (summary.Checks.Count == 0)
        {
            ImGui.TextColored(new Vector4(0.55f, 0.55f, 0.55f, 1f), "-");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(summary.Tooltip);
            return;
        }

        ImGui.TextColored(
            summary.State == TaskDependencyState.Ready
                ? new Vector4(0.25f, 1f, 0.35f, 1f)
                : new Vector4(1f, 0.75f, 0.15f, 1f),
            summary.Label);

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(summary.Tooltip);
    }

    private TaskEligibility GetDashboardEligibility(
        AutomationFeatureDefinition? feature,
        bool enabled,
        bool buttonDisabled,
        string? buttonTooltip,
        string status)
    {
        if (feature == null)
            return buttonDisabled
                ? TaskEligibility.Blocked(buttonTooltip ?? "Manual utility is unavailable right now.")
                : TaskEligibility.Runnable(status);
        if (feature.Owner == AutomationOwner.EngineTask)
            return plugin.Engine.GetTaskEligibility(feature.Id);
        if (!enabled)
            return TaskEligibility.Disabled($"{feature.Label} is disabled for this character.");
        if (feature.Owner == AutomationOwner.ConfigOnlyWip)
            return TaskEligibility.Unsupported("Configuration-only WIP; no runtime dispatch is available.");
        if (feature.Owner == AutomationOwner.PreemptiveCoordinator &&
            string.Equals(status, "AutoHook casts", StringComparison.Ordinal))
        {
            return TaskEligibility.NotDue("Runs in its configured coordinator window; the manual action remains available.");
        }
        return buttonDisabled
            ? TaskEligibility.Blocked(buttonTooltip ?? $"{feature.Label} cannot run right now.")
            : TaskEligibility.Runnable(status);
    }

    private static bool IsCompletedStatus(string status)
        => status.StartsWith("Done", StringComparison.OrdinalIgnoreCase) ||
           status.StartsWith("Complete", StringComparison.OrdinalIgnoreCase) ||
           status.StartsWith("Ticket purchased", StringComparison.OrdinalIgnoreCase) ||
           status.StartsWith("Route caps hit", StringComparison.OrdinalIgnoreCase);

    private DateTime? GetNextEligibleAt(string? automationId, CharacterConfig config)
    {
        var next = automationId switch
        {
            AutomationCatalog.VerminionQueue => config.VerminionNextReset,
            AutomationCatalog.MiniCactpot => config.MiniCactpotNextReset,
            AutomationCatalog.ChocoboRacing => config.ChocoboRacingNextReset,
            AutomationCatalog.LootGoblinMapGather => config.LootGoblinMapGatherNextReset,
            AutomationCatalog.AlliedSociety => config.AlliedSocietyNextReset,
            AutomationCatalog.FashionReport => ResetDetectionService.TaskIsCompleted(
                config.FashionReportLastCompleted,
                config.FashionReportNextReset)
                ? config.FashionReportNextReset
                : ResetDetectionService.GetNextFashionReportAvailability(DateTime.UtcNow),
            AutomationCatalog.JumboCactpot => config.JumboCactpotPayoutAvailableAt > DateTime.UtcNow
                ? config.JumboCactpotPayoutAvailableAt
                : config.JumboCactpotNextReset,
            AutomationCatalog.RefillListings => config.RefillFromListingsNextReset,
            AutomationCatalog.NagYourMom => GetNextNagYourMomEligibilityUtc(config),
            AutomationCatalog.Fishing => GetNextFishingEligibilityUtc(),
            _ => DateTime.MinValue,
        };
        return next == DateTime.MinValue ? null : next.ToUniversalTime();
    }

    private static DateTime GetNextNagYourMomEligibilityUtc(CharacterConfig config)
    {
        var now = DateTime.Now;
        if (!IsAnyNagYourMomRouteDue(config))
            return now.Date.AddDays(1).ToUniversalTime();
        if (!TimeSpan.TryParse(config.NagYourMomWindowStartLocal, out var start) ||
            !TimeSpan.TryParse(config.NagYourMomWindowEndLocal, out var end))
        {
            return DateTime.MinValue;
        }

        var inWindow = start <= end
            ? now.TimeOfDay >= start && now.TimeOfDay <= end
            : now.TimeOfDay >= start || now.TimeOfDay <= end;
        if (inWindow)
            return DateTime.UtcNow;

        var nextStart = now.Date.Add(start);
        if (start <= end && now.TimeOfDay > end)
            nextStart = nextStart.AddDays(1);
        return nextStart.ToUniversalTime();
    }

    private DateTime GetNextFishingEligibilityUtc()
    {
        var now = DateTimeOffset.UtcNow;
        var registration = OceanFishingSchedulePolicy.GetCurrentOrNextRegistrationWindow(now);
        var window = OceanFishingSchedulePolicy.BuildStartupWindow(
            registration.StartUtc,
            plugin.Configuration.OceanFishingPreWindowOffsetMinutes);
        var nextStart = window.StartUtc <= now
            ? OceanFishingSchedulePolicy.BuildStartupWindow(
                registration.StartUtc.AddHours(FishingDefaults.OceanFishingRegistrationIntervalHours),
                plugin.Configuration.OceanFishingPreWindowOffsetMinutes).StartUtc
            : window.StartUtc;
        return nextStart.UtcDateTime;
    }

    private static string FormatWhen(TaskRowDescriptor row)
    {
        if (row.Section == AutomationDashboardSection.DueNow)
            return "Now";
        if (row.Section == AutomationDashboardSection.Blocked)
            return "Blocked";
        if (row.Eligibility.Status == TaskEligibilityStatus.Disabled)
            return "Off";
        if (row.Section == AutomationDashboardSection.Complete)
            return "Done";
        if (row.NextEligibleAtUtc.HasValue)
            return row.NextEligibleAtUtc.Value.ToLocalTime().ToString("MMM dd HH:mm");
        return "Blocked";
    }

    private static string FormatType(TaskRowDescriptor row)
    {
        if (row.Feature == null)
            return "MAN";

        var owner = row.Feature.Owner switch
        {
            AutomationOwner.EngineTask => "ENG",
            AutomationOwner.RunHook => "HOOK",
            AutomationOwner.PreemptiveCoordinator => "COORD",
            AutomationOwner.ConfigOnlyWip => "WIP",
            AutomationOwner.ChildOption => "ROUTE",
            _ => row.Feature.Owner.ToString().ToUpperInvariant(),
        };
        var cadence = row.Feature.Cadence switch
        {
            AutomationCadence.EveryRun => "RUN",
            AutomationCadence.Daily => "DAY",
            AutomationCadence.Weekly => "WK",
            AutomationCadence.Scheduled => "SCH",
            AutomationCadence.CoordinatorWindow => "WIN",
            AutomationCadence.ConfigOnly => "CFG",
            _ => row.Feature.Cadence.ToString().ToUpperInvariant(),
        };
        return $"{owner}/{cadence}{(row.Maturity == "WIP" ? " WIP" : string.Empty)}";
    }

    private static string BuildTaskTooltip(TaskRowDescriptor row)
    {
        var owner = row.Feature?.OwnershipLabel ?? "Manual utility";
        var cadence = row.Feature?.CadenceLabel ?? "Manual / on demand";
        var maturity = row.Feature?.Maturity == AutomationMaturity.Wip
            ? "WIP"
            : row.Feature == null ? "Not applicable" : "Stable";
        var blocker = row.Section == AutomationDashboardSection.Blocked
            ? row.Eligibility.Reason
            : "None";
        var disabledActionReason = row.ButtonDisabled
            ? row.ButtonTooltip ?? row.Eligibility.Reason
            : "None";
        var nextLocal = row.NextEligibleAtUtc.HasValue
            ? row.NextEligibleAtUtc.Value.ToLocalTime().ToString("MMM dd yyyy HH:mm zzz")
            : row.Section == AutomationDashboardSection.DueNow ? "Now" : "Not scheduled";
        var nextUtc = row.NextEligibleAtUtc.HasValue
            ? row.NextEligibleAtUtc.Value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'")
            : row.Section == AutomationDashboardSection.DueNow ? "Now" : "Not scheduled";
        var statusDetail = !string.IsNullOrWhiteSpace(row.StatusTooltip) &&
                           !string.Equals(row.StatusTooltip, row.Status, StringComparison.Ordinal)
            ? $"\nStatus detail: {row.StatusTooltip}"
            : string.Empty;

        return $"{row.Task}\n" +
               $"Enabled: {(row.Enabled ? "On" : "Off")}\n" +
               $"Dashboard state: {AutomationDashboardPolicy.GetStateLabel(row.Section)}\n" +
               $"Status: {row.Status}{statusDetail}\n" +
               $"Eligibility detail: {row.Eligibility.Reason}\n" +
               $"Blocker: {blocker}\n" +
               $"Owner: {owner}\n" +
               $"Cadence: {cadence}\n" +
               $"Maturity: {maturity}\n" +
               $"Next eligible (local): {nextLocal}\n" +
               $"Next eligible (UTC): {nextUtc}\n" +
               $"Disabled action reason: {disabledActionReason}";
    }

    private static Vector4 GetSectionColor(AutomationDashboardSection section)
        => section switch
        {
            AutomationDashboardSection.DueNow => new Vector4(0.25f, 1f, 0.35f, 1f),
            AutomationDashboardSection.Blocked => new Vector4(1f, 0.35f, 0.25f, 1f),
            AutomationDashboardSection.ScheduledLater => new Vector4(0.45f, 0.75f, 1f, 1f),
            AutomationDashboardSection.Complete => new Vector4(0.55f, 0.85f, 0.55f, 1f),
            _ => Vector4.One,
        };

    private bool IsFavorite(string automationId)
        => plugin.Configuration.FavoriteAutomationIds?.Contains(automationId, StringComparer.Ordinal) ?? false;

    private static AutomationFeatureDefinition? GetDisplayedFeature(string task)
    {
        var id = task switch
        {
            "Misc Cmd" => AutomationCatalog.MiscCommands,
            "Verminion (5x)" => AutomationCatalog.VerminionQueue,
            _ => null,
        };
        return id != null
            ? AutomationCatalog.Get(id)
            : AutomationCatalog.Features.FirstOrDefault(
                feature => string.Equals(feature.Label, task, StringComparison.Ordinal));
    }

    private sealed record TaskRowDescriptor(
        AutomationFeatureDefinition? Feature,
        string Task,
        bool Enabled,
        string Status,
        TaskEligibility Eligibility,
        AutomationDashboardSection Section,
        DateTime? NextEligibleAtUtc,
        ConfigurationSection? RecoverySection,
        IReadOnlyList<string> Dependencies,
        string ButtonLabel,
        Action OnClick,
        string Maturity,
        string? StatusTooltip,
        bool ButtonDisabled,
        string? ButtonTooltip,
        string? SecondaryButtonLabel,
        Action? SecondaryOnClick,
        bool SecondaryButtonDisabled,
        string? SecondaryButtonTooltip,
        string? TertiaryButtonLabel,
        Action? TertiaryOnClick,
        bool TertiaryButtonDisabled,
        string? TertiaryButtonTooltip);

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

    private string GetAlliedSocietyStatus(Models.CharacterConfig config)
    {
        if (plugin.AlliedSocietyService.State != AlliedSocietyService.RunState.Idle)
            return plugin.AlliedSocietyService.StatusText;
        if (!config.EnableAlliedSociety)
            return "Off";
        if (!IsAlliedSocietyGearsetValid(config))
            return "Invalid gearset";
        return GetDailyTaskStatus(
            config.AlliedSocietyLastCompleted,
            config.AlliedSocietyNextReset,
            "Done today",
            "Daily");
    }

    private string GetAfterArParkStatus(Models.CharacterConfig config)
    {
        if (plugin.AfterArParkService.IsActive ||
            plugin.AfterArParkService.IsComplete ||
            plugin.AfterArParkService.IsFailed)
        {
            return plugin.AfterArParkService.StatusText;
        }
        if (!config.EnableAfterArPark)
            return "Off";
        return AfterArParkService.TryResolveCommand(
            config.AfterArParkDestination,
            config.AfterArParkCustomCommand,
            out var command,
            out _)
            ? command
            : "Invalid command";
    }

    private bool IsAlliedSocietyGearsetValid(Models.CharacterConfig config)
    {
        var gearsets = plugin.EquipmentAutomationRuntime.GetValidGearsets();
        return config.AlliedSocietyGearsetSelection switch
        {
            AlliedSocietyGearsetSelection.CurrentJob => EquipmentAutomationPolicy.SelectCurrentGearset(
                gearsets,
                plugin.EquipmentAutomationRuntime.CurrentGearsetId,
                plugin.EquipmentAutomationRuntime.CurrentJobId) != null,
            AlliedSocietyGearsetSelection.SavedGearset => gearsets.Any(
                gearset => gearset.GearsetId == config.AlliedSocietyGearsetId),
            _ => false,
        };
    }

    private bool IsEquipmentAutomationBusy()
        => plugin.Engine.IsRunning ||
           plugin.GearUpdaterService.IsActive ||
           plugin.HighestCombatJobService.IsActive ||
           plugin.CurrentJobEquipmentService.IsActive ||
           plugin.SeasonalGearService.IsActive ||
           plugin.AlliedSocietyService.IsActive ||
           plugin.AlliedSocietyService.OwnsRotation;

    private static string GetNagYourMomStatus(Models.CharacterConfig config, string engineStatus)
    {
        if (engineStatus.StartsWith("Series rank test:", StringComparison.Ordinal) ||
            engineStatus.StartsWith("Series rank test failed:", StringComparison.Ordinal))
        {
            return engineStatus;
        }

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
