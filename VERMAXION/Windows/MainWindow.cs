using System;
using AethertekUI;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.Loader;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using ECommons.Reflection;
using VERMAXION.Models;
using VERMAXION.Services;
using VERMAXION.CustomDeliveries;

namespace VERMAXION.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly AethertekUI.Dalamud.MaterialWindowMotion motion = new();
    private static readonly string[] TaskDependencyInternalNames =
    [
        "XADatabase",
        "AutoRetainer",
        "Lifestream",
        "AutoHook",
        "vnavmesh",
        "YesAlready",
        "ADS",
        "Saucy",
        "TextAdvance",
        "XASlave",
        "QSTCompanion",
        "LootGoblin",
        "mom",
        "dad",
        "Artisan",
        "WigglyQuest",
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
    private static readonly string CurrentVersion=typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
    private string taskSearch = string.Empty;
    private string taskView = "Overview";
    private ulong customDeliveryAutoShowContentId;
    private readonly RetainerEquippingArProbeCache retainerEquippingReadinessCache =
        new(TimeSpan.FromSeconds(5));

    public MainWindow(Plugin plugin)
        : base(
            "Vermaxion##Main",
            ImGuiWindowFlags.None)
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(520, 620),
            MaximumSize = new Vector2(1600, 1200),
        };
        TitleBarButtons.Add(new()
        {
            Icon = FontAwesomeIcon.Cog, Priority = 0, IconOffset = new(2, 1),
            Click = button => { if (button == ImGuiMouseButton.Left) plugin.ToggleConfigUi(); },
            ShowTooltip = () => UiGui.SetTooltip("Settings"),
        });
        TitleBarButtons.Add(new()
        {
            Icon = FontAwesomeIcon.PowerOff, Priority = -10, IconOffset = new(2, 1),
            Click = button => { if (button == ImGuiMouseButton.Left) SetEnabled(!plugin.Configuration.Enabled); },
            ShowTooltip = () => MaterialText.SetTooltip(UiText.T("Enabled") + ": " + UiText.T(plugin.Configuration.Enabled ? "On" : "Off")),
        });
        TitleBarButtons.Add(new()
        {
            Icon = FontAwesomeIcon.Play, Priority = -20, IconOffset = new(2, 1),
            Click = button => { if (button == ImGuiMouseButton.Left) RunAll(); },
            ShowTooltip = () => MaterialText.SetTooltip(UiText.T("Run All") + "\n" + UiText.T(plugin.Engine.StatusText)
                + "\n" + UiText.T(plugin.Engine.ActiveHandoffBlocker)),
        });
        TitleBarButtons.Add(new()
        {
            Icon = FontAwesomeIcon.Stop, Priority = -30, IconOffset = new(2, 1),
            Click = button => { if (button == ImGuiMouseButton.Left) CancelRun(); },
            ShowTooltip = () => MaterialText.SetTooltip(UiText.T("Cancel") + "\n" + UiText.T(plugin.Engine.StatusText)),
        });
        TitleBarButtons.Add(new()
        {
            Icon = FontAwesomeIcon.Ban, Priority = -40, IconOffset = new(2, 1),
            Click = button => { if (button == ImGuiMouseButton.Left) plugin.FullStop(); },
            ShowTooltip = () => UiGui.SetTooltip("FULL STOP"),
        });
    }

    public void Dispose() { }

    internal void SetEnabled(bool enabled)
    {
        if (!enabled) plugin.PauseCurrentTargetCycleBestEffort("VERMAXION global automation disabled");
        plugin.Configuration.Enabled = enabled;
        plugin.Configuration.Save();
    }

    internal void RunAll()
    {
        if (!plugin.Engine.IsRunning && !plugin.DadHandoffBlocksNewWork)
            plugin.RunDashboardAction(() => plugin.Engine.ManualStart());
    }

    internal void CancelRun()
    {
        if (plugin.Engine.IsRunning) plugin.Engine.Cancel();
    }

    public override void PreDraw()
    {
        UIConstants.PushStyle(plugin.Configuration.CompactUi);
        motion.Prepare(this, reducedMotion: false, roundedCorners: true);
    }
    public override void PostDraw()
    {
        motion.Restore(this);
        UiGui.ImageTitle(this, UiText.T("Vermaxion") + " " + CurrentVersion, plugin.OriginalIcon);
        UIConstants.PopStyle();
    }

    internal void UpdateCustomDeliveryVisibility()
    {
        if (!Plugin.PlayerState.IsLoaded)
        {
            customDeliveryAutoShowContentId = 0;
            return;
        }
        var contentId = Plugin.PlayerState.ContentId;
        if (contentId == 0 || customDeliveryAutoShowContentId == contentId)
            return;
        var config = plugin.ConfigManager.GetActiveConfig();
        if (config.EnableCustomDeliveries && config.CustomDeliveriesSettings.AutoShowIfIncomplete &&
            plugin.CustomDeliveriesService.HasPendingWork(config.CustomDeliveriesSettings))
        {
            customDeliveryAutoShowContentId = contentId;
            IsOpen = true;
        }
    }

    public override void Draw()
    {
        motion.DrawChrome();
        var config = plugin.ConfigManager.GetActiveConfig();
        var engine = plugin.Engine;
        var charKey = plugin.ConfigManager.CurrentCharacterKey;
        var displayName = string.IsNullOrEmpty(charKey) ? UiText.T("(Default)") : charKey;

        var compact = plugin.Configuration.CompactUi;
        var scale = UIConstants.Scale;
        var headerStart = ImGui.GetCursorScreenPos();
        var markSize = (compact ? new Vector2(58, 49) : new Vector2(46, 39)) * scale;
        var icon = plugin.OriginalIcon;
        var imageMin = headerStart + new Vector2(2, 2) * scale;
        MaterialCanvas.DrawImage(ImGui.GetWindowDrawList(), icon.Handle, icon.Size, imageMin, imageMin + markSize);
        using (UiText.Font(compact ? UiFontRole.CompactTitle : UiFontRole.Title))
        {
            var callerScale = ImGuiP.GetCurrentWindow().FontWindowScale;
            try
            {
                ImGui.SetWindowFontScale(callerScale * (compact ? 1.56f : .86f));
                var titleOffset = 0f;
                unsafe
                {
                    var font = ImGui.GetFont(); var fontScale = ImGui.GetFontSize() / font.FontSize;
                    var top = float.MaxValue; var bottom = float.MinValue;
                    foreach (var character in "VERMAXION")
                    {
                        var glyph = ImGui.FindGlyphNoFallback(font, character);
                        if (glyph.Handle == null) continue;
                        top = MathF.Min(top, glyph.Handle->Y0 * fontScale); bottom = MathF.Max(bottom, glyph.Handle->Y1 * fontScale);
                    }
                    if (top <= bottom) titleOffset = (markSize.Y + 4 * scale - (bottom - top)) * .5f - top;
                }
                ImGui.SetCursorScreenPos(headerStart + new Vector2(markSize.X + (compact ? 14 : 18) * scale, titleOffset));
                UiGui.TextUnformatted("VERMAXION");
            }
            finally { ImGui.SetWindowFontScale(callerScale); }
        }
        if (plugin.Configuration.UiCompactVisibleOnMainWindow)
        {
            UIConstants.SameLineIfFits("C");
            if (UiGui.Checkbox("C", ref compact))
            { plugin.Configuration.CompactUi = compact; plugin.Configuration.Save(); }
            if (ImGui.IsItemHovered()) UiGui.SetTooltip("Compact mode");
        }
        if (plugin.Configuration.UiLanguageVisibleOnMainWindow)
        { UIConstants.SameLineIfFits("Language", 180 * scale); plugin.DrawLanguageSelector(); }
        UIConstants.SameLineIfFits("Transparency"); plugin.DrawTransparencyToggle();

        // Ko-fi donation button in upper right
        UIConstants.SameLineIfFits("\u2661 Ko-fi \u2661");
        if (UiGui.SmallButton("\u2661 Ko-fi \u2661"))
        {
            System.Diagnostics.Process.Start(new ProcessStartInfo
            {
                FileName = "https://ko-fi.com/mcvaxius",
                UseShellExecute = true
            });
        }
        if (ImGui.IsItemHovered())
        {
            UiGui.SetTooltip("Support development on Ko-fi");
        }
        ImGui.Separator();

        if (plugin.Configuration.KrangleEnabled && !string.IsNullOrEmpty(charKey))
            displayName = KrangleService.KrangleName(charKey);

        var account = plugin.ConfigManager.GetCurrentAccount();
        var wideHeader = ImGui.GetContentRegionAvail().X >= 740 * scale;
        var lineHeight = ImGui.GetTextLineHeight();
        var lineGap = ImGui.GetStyle().ItemSpacing.Y;
        var rowPadding = ImGui.GetStyle().CellPadding.Y * 2;
        var runtimeHeight = MathF.Ceiling(lineHeight * 3 + lineGap * 2 + rowPadding) + scale;
        var switchHeight = MathF.Ceiling(lineHeight + lineGap + 26 * scale + rowPadding) + scale;
        var statusHeight = wideHeader ? Math.Max((compact ? 64 : VermaxionPresentation.MainStatus) * scale, Math.Max(runtimeHeight, switchHeight)) : 0;
        var statusStart = ImGui.GetCursorScreenPos();
        var statusWidth = ImGui.GetContentRegionAvail().X;
        var statusDraw = ImGui.GetWindowDrawList();
        var headerId = ImGui.GetID("DashboardHeader");
        var scopeBottom = statusStart.Y;
        var headerFlags = ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.BordersInnerV;
        statusDraw.ChannelsSplit(2);
        statusDraw.ChannelsSetCurrent(1);
        try
        {
            if (ImGui.BeginTable("DashboardHeader", wideHeader ? 5 : 2, headerFlags))
            {
                headerId = ImGui.GetID("");
                ImGui.TableNextRow();
                DrawHeaderScope();
                if (wideHeader) DrawHeaderControls();
                var table = ImGuiP.GetCurrentTable();
                ImGuiP.TableEndRow(table);
                scopeBottom = table.RowPosY2;
                statusHeight = Math.Max(statusHeight, MathF.Ceiling(scopeBottom - statusStart.Y));
                ImGui.EndTable();
            }
            if (!wideHeader && ImGui.BeginTable("DashboardHeaderControls", 3, headerFlags))
            {
                ImGuiP.PushOverrideID(headerId);
                try
                {
                    ImGui.TableNextRow();
                    DrawHeaderControls();
                }
                finally { ImGui.PopID(); }
                var table = ImGuiP.GetCurrentTable();
                ImGuiP.TableEndRow(table);
                statusHeight = Math.Max(statusHeight, MathF.Ceiling(table.RowPosY2 - statusStart.Y));
                ImGui.EndTable();
            }
            statusDraw.ChannelsSetCurrent(0);
            VermaxionPresentation.Surface(statusStart, statusStart + new Vector2(statusWidth, statusHeight));
            if (!wideHeader)
                statusDraw.AddLine(new Vector2(statusStart.X + 8 * scale, scopeBottom + lineGap * .5f),
                    new Vector2(statusStart.X + statusWidth - 8 * scale, scopeBottom + lineGap * .5f), ImGui.GetColorU32(UIConstants.Border));
        }
        finally
        {
            statusDraw.ChannelsSetCurrent(1);
            statusDraw.ChannelsMerge();
        }
        ImGui.SetCursorScreenPos(new Vector2(statusStart.X, Math.Max(ImGui.GetCursorScreenPos().Y, statusStart.Y + statusHeight)));
        ImGui.Dummy(new Vector2(0, (compact ? 8 : 12) * scale));
        DrawControls();
        var watchFishingWindows=plugin.Configuration.OceanFishingWindowWatchEnabled;
        if(UIConstants.Checkbox("Watch Ocean Fishing windows",ref watchFishingWindows))
        {
            plugin.Configuration.OceanFishingWindowWatchEnabled=watchFishingWindows;
            plugin.Configuration.Save();
        }
        if(ImGui.IsItemHovered()) UiGui.SetTooltip("Actively check for Ocean Fishing windows without AR pre/post processing. Relogs and fishes using your configured fishing settings.");

        ImGui.BeginDisabled(account == null);
        var timerEnabled = account?.RetainerlessTimerEnabled ?? false;
        if (UIConstants.Checkbox("Run due tasks on a timer", ref timerEnabled) && account != null)
        {
            account.RetainerlessTimerEnabled = timerEnabled;
            plugin.ConfigManager.SaveCurrentAccount();
        }
        if (ImGui.IsItemHovered())
            UiGui.SetTooltip("For this runtime account and logged-in character only. Runs enabled, due tasks in both phases when idle. FULL STOP disables the timer.");
        var timerInterval = account?.RetainerlessTimerIntervalMinutes ?? 30;
        ImGui.SetNextItemWidth(120 * scale);
        if (UIConstants.InputInt("Timer interval (minutes)", ref timerInterval, 1, 10) && account != null)
        {
            account.RetainerlessTimerIntervalMinutes = timerInterval;
            plugin.ConfigManager.SaveCurrentAccount();
        }
        ImGui.EndDisabled();

        void DrawHeaderScope()
        {
            ImGui.TableNextColumn(); UiGui.TextDisabled("Account");
            MaterialText.Text(string.IsNullOrWhiteSpace(account?.AccountAlias) ? UiText.T("No account selected") : account.AccountAlias);
            ImGui.TableNextColumn(); UiGui.TextDisabled("Current Character"); MaterialText.Text(displayName);
        }

        void DrawHeaderControls()
        {
            ImGui.TableNextColumn(); UiGui.TextDisabled("Enabled");
            var enabled = plugin.Configuration.Enabled;
            if (UiGui.NativeSwitch("Enabled", ref enabled))
                SetEnabled(enabled);
            ImGui.TableNextColumn(); UiGui.TextDisabled("Krangle names");
            var krangleEnabled = plugin.Configuration.KrangleEnabled;
            if (UiGui.NativeSwitch(UIConstants.ConfigLabels.KrangleNames, ref krangleEnabled))
            {
                plugin.Configuration.KrangleEnabled = krangleEnabled;
                if (!krangleEnabled) KrangleService.ClearCache();
                plugin.Configuration.Save();
            }
            if (ImGui.IsItemHovered()) UiGui.SetTooltip(UIConstants.Tooltips.KrangleNames);
            ImGui.TableNextColumn(); UiGui.TextDisabled("Runtime");
            UiGui.TextColored(engine.RegistryReady ? UIConstants.Mint : UIConstants.Amber,
                engine.RegistryReady ? UiText.F("Ready / {0}", UiText.T(engine.State.ToString())) : "Blocked");
            UiGui.TextDisabled(UiText.F("{0} pending", engine.GetPendingTaskCount()));
            if (ImGui.IsItemHovered()) UiGui.SetTooltip(string.Join("\n", UiText.T(engine.StatusText), UiText.T(engine.RegistryDiagnostic), UiText.T(engine.ActiveHandoffBlocker)));
        }

        void DrawControls()
        {
            using var actionFont = UiText.Font(UiFontRole.Action);
            if (!engine.RegistryReady && UIConstants.Button("Open Task Order"))
                plugin.ConfigWindow.OpenTaskOrder();

            // Control buttons row
            // FULL STOP stays above task scrolling, with an explicit border in every state.
            var actionHeight = MaterialControlMetrics.Measure(MaterialTheme.Metrics, ImGui.GetTextLineHeight(), MaterialControlContext.Toolbar).Height;
            var available = ImGui.GetContentRegionAvail().X;
            var gap = ImGui.GetStyle().ItemSpacing.X;
            var previousWidth = Math.Max(140 * scale, MathF.Floor((available - 4 * gap) / 5));
            var labels = new[] { "FULL STOP", "Run All", "Settings", "Verminion", "Fish collection" };
            var minimums = labels.Select(label => Math.Max(140 * scale, UIConstants.ButtonWidth(label) + 28 * scale)).ToArray();
            var wideActions = minimums.Sum() + 4 * gap <= available;
            var extra = wideActions ? (available - minimums.Sum() - 4 * gap) / 5 : 0;
            var primaryExtra = wideActions ? extra : Math.Max(0, (available - minimums[0] - minimums[1] - gap) / 2);
            var navigationExtra = wideActions ? extra : Math.Max(0, (available - minimums.Skip(2).Sum() - 2 * gap) / 3);
            var widths = minimums.Select((minimum, index) => MathF.Floor(minimum + (index < 2 ? primaryExtra : navigationExtra))).ToArray();
            if (UIConstants.FullStopButton(new Vector2(widths[0], actionHeight), previousWidth)) plugin.FullStop();
            UIConstants.SameLineIfFits("Run All", widths[1]);

            ImGui.BeginDisabled(engine.IsRunning || plugin.DadHandoffBlocksNewWork);
            if (UIConstants.Button("Run All", new Vector2(widths[1], actionHeight), true, previousWidth))
                RunAll();
            ImGui.EndDisabled();
            if (engine.IsRunning)
            {
                UIConstants.SameLineIfFits("Cancel");
                if (UIConstants.Button("Cancel"))
                    CancelRun();
            }
            if (wideActions) UIConstants.SameLineIfFits("Settings", widths[2]);
            if (UIConstants.Button("Settings", new Vector2(widths[2], actionHeight), true, previousWidth))
                plugin.ToggleConfigUi();
            UIConstants.SameLineIfFits("Verminion", widths[3]);
            if (UIConstants.Button("Verminion", new Vector2(widths[3], actionHeight), true, previousWidth))
                plugin.VerminionWindow.IsOpen = true;
            UIConstants.SameLineIfFits("Fish collection", widths[4]);
            if (UIConstants.Button("Fish collection", new Vector2(widths[4], actionHeight), true, previousWidth))
                plugin.FishCollectionWindow.IsOpen = true;
            if (ImGui.IsItemHovered())
                UiGui.SetTooltip("Open big and fabled fish collection.");
        }

        void DrawTaskSurface(bool favoritesOnly, bool attentionOnly = false)
        {
            var taskRows = GetDashboardTaskRows();
            var search = taskSearch.Trim();
            var visibleRows = taskRows.Where(row =>
                    (!favoritesOnly || row.Feature != null && IsFavorite(row.Feature.Id)) &&
                    (!attentionOnly || row.Feature != null && row.Section is AutomationDashboardSection.DueNow or AutomationDashboardSection.Blocked) &&
                    (search.Length == 0 ||
                     UiText.T(row.Task).Contains(search, StringComparison.OrdinalIgnoreCase) ||
                     row.Task.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                     row.Feature?.Label.Contains(search, StringComparison.OrdinalIgnoreCase) == true ||
                     row.Dependencies.Any(name => name.Contains(search, StringComparison.OrdinalIgnoreCase))))
                .ToList();
            var loadedPluginInternalNames = GetLoadedTaskDependencyNames();

            DrawDashboardRows(visibleRows, favoritesOnly, loadedPluginInternalNames,
                favoritesOnly ? "Favorites" : attentionOnly ? "Overview" : "AllTasks");

        ImGui.Spacing();

        if (!favoritesOnly && !attentionOnly && UIConstants.CollapsingHeading("Advanced test controls"))
        {
            // Test Functions
            ImGui.BeginDisabled(plugin.DadHandoffBlocksNewWork);
            UiGui.Text("Test Functions");
            ImGui.Separator();

            var fishingTestDisabled = engine.IsRunning ||
                                      plugin.IsFishingRunActive ||
                                      plugin.FisherGearsetTestService.IsActive;
            ImGui.BeginDisabled(fishingTestDisabled);
            if (UIConstants.Button("Ocean Fishing account test"))
                plugin.RunDashboardAction(plugin.RunFishingStartupTest);
            UIConstants.SameLineIfFits("Current Fisher gearset test");
            if (UIConstants.Button("Current Fisher gearset test"))
                plugin.RunDashboardAction(plugin.RunFishingGearsetTest);
            ImGui.EndDisabled();

            var canGoToMainMenu = plugin.CanStartMainMenuTest(
                waitForOceanFishing: false,
                out var goToMainMenuBlockedReason);
            UIConstants.SameLineIfFits("Go to main menu");
            ImGui.BeginDisabled(!canGoToMainMenu);
            if (UIConstants.Button("Go to main menu"))
                plugin.GoToMainMenu();
            ImGui.EndDisabled();
            if (!canGoToMainMenu && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                UiGui.SetTooltip(goToMainMenuBlockedReason);

            var canWaitForOceanFishing = plugin.CanStartMainMenuTest(
                waitForOceanFishing: true,
                out var waitForOceanFishingBlockedReason);
            UIConstants.SameLineIfFits("Go to main menu and wait for Ocean Fishing");
            ImGui.BeginDisabled(!canWaitForOceanFishing);
            if (UIConstants.Button("Go to main menu and wait for Ocean Fishing"))
                plugin.GoToMainMenuAndWaitForOceanFishing();
            ImGui.EndDisabled();
            if (!canWaitForOceanFishing && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                UiGui.SetTooltip(waitForOceanFishingBlockedReason);

            if (UIConstants.Button("Check FC Buff Inventory"))
            {
                // Force config save before test
                plugin.ConfigManager.SaveCurrentAccount();
                Plugin.Log.Information("[UI] Forced config save before FC Buff Inventory test");
                plugin.RunDashboardAction(plugin.FCBuffInventoryService.Start);
            }

            UIConstants.SameLineIfFits("FC GC Test");
            if (UIConstants.Button("FC GC Test"))
            {
                // Force config save before test
                plugin.ConfigManager.SaveCurrentAccount();
                Plugin.Log.Information("[UI] Forced config save before FC GC test");
                plugin.FCBuffService.TestFreeCompanyGC();
            }

            UIConstants.SameLineIfFits("Test FC Points");
            if (UIConstants.Button("Test FC Points"))
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

            UIConstants.SameLineIfFits("Force Config Load");
            if (UIConstants.Button("Force Config Load"))
            {
                plugin.ConfigManager.LoadAllAccounts();
                // Get config AFTER loading to ensure we have the latest values
                var activeConfig = plugin.ConfigManager.GetActiveConfig();
                Plugin.Log.Information($"[UI] Forced config load: FCBuffMinPoints={activeConfig.FCBuffMinPoints}, FCBuffPurchaseAttempts={activeConfig.FCBuffPurchaseAttempts}");
            }

            UIConstants.SameLineIfFits("Test Chocobo Rank");
            if (UIConstants.Button("Test Chocobo Rank"))
            {
                Plugin.Log.Information("[UI] Testing racing chocobo rank from GoldSaucerInfo node 21");
                plugin.ChocoboRaceService.RequestGoldSaucerRankTest();
            }
            if (ImGui.IsItemHovered())
                UiGui.SetTooltip("Opens /goldsaucer and reads GoldSaucerInfo node 21, the fallback used when RaceChocoboManager is not loaded.");
            UiGui.TextDisabled(UiText.F($"Chocobo rank test: {plugin.ChocoboRaceService.GoldSaucerRankTestStatus}"));

            // BUTTON PRESSES
            ImGui.Spacing();
            UiGui.Text("Button Presses");
            ImGui.Separator();

            if (UIConstants.Button("[ESC]"))
            {
                Plugin.Log.Information("[UI] Testing ESC key press");
                GameHelpers.CloseCurrentAddon();
            }

            ImGui.SameLine();
            if (UIConstants.Button("[NUMPAD+]"))
            {
                Plugin.Log.Information("[UI] Testing NUMPAD+ key press");
                GameHelpers.SendNumpadPlus();
            }

            ImGui.SameLine();
            if (UIConstants.Button("[END]"))
            {
                Plugin.Log.Information("[UI] Testing END key press");
                GameHelpers.SendEnd();
            }

            ImGui.Spacing();
            var canTestJumboRoute = plugin.CanStartJumboRouteTest(out var jumboRouteBlockedReason);
            ImGui.BeginDisabled(!canTestJumboRoute);
            if (UIConstants.Button("Test Jumbo Broker Route"))
                plugin.RunJumboRouteTest(cashier: false);
            UIConstants.SameLineIfFits("Test Jumbo Cashier Route");
            if (UIConstants.Button("Test Jumbo Cashier Route"))
                plugin.RunJumboRouteTest(cashier: true);
            ImGui.EndDisabled();
            if (!canTestJumboRoute && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                UiGui.SetTooltip(jumboRouteBlockedReason);
            UiGui.TextDisabled("Route tests stop at the NPC without buying tickets or claiming payouts.");
            if (plugin.CactpotService.IsJumboRouteTest)
                UiGui.TextWrapped(UiText.F($"Jumbo route test: {UiText.T(plugin.CactpotService.StatusText)}"));
            ImGui.EndDisabled();
        }

        if (visibleRows.Count == 0)
            UiGui.TextWrapped(search.Length > 0
                ? "No tasks match this search in the selected view. Clear the search or open All Tasks."
                : favoritesOnly
                    ? "No favorite tasks yet. Open All Tasks and select the star beside any automation to add it here."
                    : attentionOnly
                        ? "No tasks need attention. Open All Tasks to see scheduled, disabled and completed tasks."
                        : "No tasks are available.");

        if (!attentionOnly && visibleRows.Any(row => row.Id == AutomationCatalog.CustomDeliveries) &&
            UIConstants.CollapsingHeading("Custom Deliveries: NPC ranks, bonuses and progress"))
            DrawCustomDeliveryNpcOverview(plugin, config.CustomDeliveriesSettings);

        DrawAdvancedDiagnostics();

        }

        ImGui.Separator();
        bool tabsOpen;
        using (MaterialText.PushLineHeight(new[] { "Overview", "All Tasks", "Favorites" }.Select(UiText.T).ToArray()))
            tabsOpen = ImGui.BeginTabBar("MainTaskTabs", ImGuiTabBarFlags.FittingPolicyScroll);
        if (tabsOpen)
        {
            DrawTaskTab("Overview", false, true);
            DrawTaskTab("All Tasks", false, false);
            DrawTaskTab("Favorites", true, false);
            ImGui.EndTabBar();
        }

        void DrawTaskTab(string label, bool favoritesOnly, bool attentionOnly)
        {
            if (UiGui.BeginTabItem(label))
            {
                taskView = label;
                UIConstants.SetNextItemWidth(-1f);
                UiGui.SearchInputTextWithHint("##TaskSearch", "Find a task or required plugin...", ref taskSearch, 100);
                if (taskSearch.Length > 0 && UIConstants.Button("Clear search")) taskSearch = string.Empty;
                var taskHeight = Math.Max(ImGui.GetTextLineHeightWithSpacing() * 6, ImGui.GetContentRegionAvail().Y);
                if (ImGui.BeginChild("TaskBody##" + label, new Vector2(0, taskHeight), false))
                {
                    if (attentionOnly)
                        UiGui.TextWrapped("Due now and blocked tasks. Browse All Tasks for scheduled tasks and manual utilities.");
                    DrawTaskSurface(favoritesOnly, attentionOnly);
                }
                ImGui.EndChild();
                ImGui.EndTabItem();
            }
        }

        void DrawAdvancedDiagnostics()
        {
            ImGui.Spacing();

            if (UIConstants.CollapsingHeading("Advanced diagnostics"))
            {
                var lastRunTime = engine.LastRunCompletedAtUtc?.ToLocalTime().ToString("g", UiText.Current.Culture) ?? UiText.T("Never");
                UiGui.TextWrapped(UiText.F("Last run: {0} at {1} - {2}", UiText.T(engine.LastRunOutcome.ToString()), lastRunTime, UiText.T(engine.LastRunSummary)));
                UiGui.TextWrapped(UiText.F("Before-AR gate: {0} - {1}", UiText.T(plugin.BeforeArGate.ToString()), UiText.T(plugin.BeforeArStatusText)));
                UiGui.TextWrapped(UiText.F($"AR suppression: {plugin.AutoRetainerIPC.LastSnapshot}"));

                var characterSelectRecovery = plugin.CharacterSelectStallRecovery;
                var characterSelectEligibility = characterSelectRecovery.GetEligibility(
                    plugin.Configuration.EnableCharacterSelectStallRecovery);
                UiGui.TextWrapped(
                    UiText.F($"Character-select recovery: {UiText.T(plugin.Configuration.EnableCharacterSelectStallRecovery ? "On" : "Off")} - {UiText.T(characterSelectRecovery.StatusText)}"));
                var characterSelectBlockedReason = characterSelectEligibility.CanAttempt
                    ? characterSelectRecovery.LastBlockedReason
                    : characterSelectEligibility.Reason;
                if (!string.IsNullOrWhiteSpace(characterSelectBlockedReason))
                    UiGui.TextWrapped(UiText.F("Character-select recovery blocker: {0}", UiText.T(characterSelectBlockedReason)));
                ImGui.BeginDisabled(!characterSelectEligibility.CanAttempt);
                if (UIConstants.Button("Load first character now"))
                    plugin.QueueCharacterSelectRecoveryAttempt();
                ImGui.EndDisabled();
                if (!characterSelectEligibility.CanAttempt && ImGui.IsItemHovered())
                    UiGui.SetTooltip(characterSelectEligibility.Reason);

                var now = DateTime.UtcNow;
                var nextDaily = ResetDetectionService.GetLastDailyReset(now).AddDays(1);
                var nextWeekly = ResetDetectionService.GetLastWeeklyReset(now).AddDays(7);
                var untilDaily = nextDaily - now;
                var untilWeekly = nextWeekly - now;
                var nextFriday = ResetDetectionService.GetNextFashionReportAvailability(now);
                var untilFriday = nextFriday - now;
                var nextJumboPayout = ResetDetectionService.GetNextJumboCactpotPayoutAvailability(now);
                var untilJumboPayout = nextJumboPayout - now;
                UiGui.TextWrapped(UiText.F($"Daily: {untilDaily.Hours}h {untilDaily.Minutes}m | Weekly: {untilWeekly.Days}d {untilWeekly.Hours}h {untilWeekly.Minutes}m | Fashion: {untilFriday.Days}d {untilFriday.Hours}h {untilFriday.Minutes}m | Jumbo payout: {untilJumboPayout.Days}d {untilJumboPayout.Hours}h {untilJumboPayout.Minutes}m"));

                var arStatus = UiText.T(plugin.ARPostProcessService.IsProcessing ? "Processing" : "Waiting");
                var momIpcStatus = plugin.MomIPCClient.GetReadiness();
                UiGui.TextWrapped(UiText.F($"AR PostProcess: {arStatus} | {now.ToString("dddd", UiText.Current.Culture)}"));
                UiGui.TextWrapped(UiText.F($"mom IPC: {UiText.T(momIpcStatus.Summary)} | nag your mom: {UiText.T(engine.NagYourMomStatusText)}"));
                UiGui.TextWrapped(UiText.F($"dad IPC: {UiText.T(plugin.DadIPCClient.IsReady() ? "Ready" : "Unavailable")} | nag your dad: {UiText.T(engine.NagYourDadStatusText)}"));
            }
        }
    }

    // Shared manual actions and live availability; safe to build without drawing either window.
    internal IReadOnlyList<TaskRowDescriptor> GetDashboardTaskRows(bool forceRefresh = false)
    {
        var config = plugin.ConfigManager.GetActiveConfig();
        var engine = plugin.Engine;
        var rows = new List<TaskRowDescriptor>();

        // --- Every AR PostProcess ---
        AddTaskRow("Misc Cmd", config.EnableMiscCmd,
            config.EnableMiscCmd ? AutomationCatalog.Get(AutomationCatalog.MiscCommands).CadenceLabel : "Off",
            "Run##MiscCmd", () => plugin.Engine.SendRunShutdownCommandBundle(), "OK");
        AddTaskRow("FC Buff Refill", config.EnableFCBuffRefill, AutomationCatalog.Get(AutomationCatalog.FCBuffRefill).CadenceLabel,
            "Run##FCBuff", () => plugin.FCBuffService.RunTask(), "OK");
        AddTaskRow("Vendor Stock", config.EnableVendorStock, GetVendorStockStatus(config),
            "Run##Vendor", () => plugin.VendorStockService.RunTask(), "OK");
        var fishingButtonsDisabled = engine.IsRunning ||
                                     plugin.IsFishingRunActive ||
                                     plugin.FisherGearsetTestService.IsActive;
        AddTaskRow("Fishing", config.EnableFishing, GetFishingStatus(config, plugin.FishingRunStatusText),
            "Run##Fishing", plugin.RunFishingStartupManual, "OK",
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
        var registrables = plugin.RegisterRegistrablesService;
        var registrableBlocker = registrables.GetManualStartBlockedReason();
        var registrableSource = config.RegisterUnregisteredItemsFromInventory ? "Inventory discovery" : "Personal list";
        var registrableStatus = !registrables.IsActive && registrableBlocker != null
            ? $"Blocked: {registrableBlocker}"
            : registrables.StatusText;
        AddTaskRow("Register Registrables", config.EnableRegisterRegistrables, $"{registrableSource}: {registrableStatus}",
            "Run##Register", registrables.StartManual,
            buttonDisabled: registrableBlocker != null,
            buttonTooltip: registrableBlocker ?? "Run the selected source once, regardless of scheduled enablement.");
        AddTaskRow("Refill Listings", config.EnableRefillFromListings, GetRefillFromListingsStatus(config),
            "Run##Listings", () =>
            {
                plugin.ConfigManager.SaveCurrentAccount();
                engine.ManualStartRefillListings();
            }, "OK");
        var retainerEquippingFeature = AutomationCatalog.Get(AutomationCatalog.RetainerEquipping);
        var retainerEquippingReadiness = GetRetainerEquippingReadiness(config, forceRefresh);
        var retainerEquippingExecuting =
            engine.State == VermaxionEngine.EngineState.RunningRetainerEquipping;
        var retainerEquippingStatus = retainerEquippingExecuting
            ? plugin.RetainerEquippingService.StatusText
            : retainerEquippingReadiness.StatusText;
        var retainerEquippingTooltip = retainerEquippingExecuting
            ? plugin.RetainerEquippingService.StatusText
            : retainerEquippingReadiness.DisabledReason;
        AddTaskRow(
            retainerEquippingFeature.Label,
            config.EnableRetainerEquipping,
            retainerEquippingStatus,
            "Run##RetainerEquipping",
            RunRetainerEquipping,
            retainerEquippingFeature.Maturity == AutomationMaturity.Wip ? "WIP" : "OK",
            statusTooltip: retainerEquippingTooltip,
            buttonDisabled: !retainerEquippingReadiness.CanRun,
            buttonTooltip: retainerEquippingReadiness.CanRun
                ? "Run only Retainer Equipping. This explicit run ignores its scheduling checkbox."
                : retainerEquippingReadiness.DisabledReason);
        AddTaskRow("Retainer Bell", true, plugin.WorkshopBellService.StatusText,
            "run##WorkshopBell", () =>
            {
                plugin.ConfigManager.SaveCurrentAccount();
                var activeConfig = plugin.ConfigManager.GetActiveConfig();
                plugin.WorkshopBellService.Start(activeConfig.RefillFromListingsRoute);
            }, "OK");
        var equipmentAutomationBusy = IsEquipmentAutomationBusy();
        AddTaskRow("Bootstrap Gearsets", true, plugin.GearUpdaterService.StatusText,
            "run##BootstrapGearsets", plugin.GearUpdaterService.StartBootstrap, "OK",
            buttonDisabled: equipmentAutomationBusy,
            buttonTooltip: equipmentAutomationBusy
                ? "An engine or equipment task is active."
                : "Persist the current job, then bootstrap missing unlocked class/job gearsets from already-owned main hands.");
        AddTaskRow("Seasonal Gear", config.EnableSeasonalGearRoulette, AutomationCatalog.Get(AutomationCatalog.SeasonalGear).CadenceLabel,
            "Run##Seasonal", () => plugin.SeasonalGearService.RunTask(), "OK");
        AddTaskRow("Minion Roulette", config.EnableMinionRoulette, AutomationCatalog.Get(AutomationCatalog.MinionRoulette).CadenceLabel,
            "Run##Minion", () => plugin.MinionRouletteService.RunTask(), "OK");
        AddTaskRow("Gear Updater", config.EnableGearUpdater, AutomationCatalog.Get(AutomationCatalog.GearUpdater).CadenceLabel,
            "Run##Gear", () => plugin.GearUpdaterService.RunTask(), "OK");
        var afterArParkCommandValid = AfterArParkService.TryResolveCommand(
            config.AfterArParkDestination, config.AfterArParkCustomCommand, out _, out var afterArParkCommandReason);
        AddTaskRow("After-AR Park", config.EnableAfterArPark, GetAfterArParkStatus(config),
            "Run##AfterArPark", () => plugin.AfterArParkService.Start(config), "OK",
            buttonDisabled: engine.IsRunning || plugin.AfterArParkService.IsActive || !afterArParkCommandValid,
            buttonTooltip: engine.IsRunning ? "The VERMAXION engine is running."
                : plugin.AfterArParkService.IsActive ? "After-AR Park is already active."
                : !afterArParkCommandValid ? afterArParkCommandReason
                : "Issues the configured /li route once and waits for Lifestream/player settlement.");

        // --- Weekly Tasks ---
        AddTaskRow("Verminion", config.EnableVerminionQueue,
            config.VerminionPaused ? $"Paused — use Resume | {VerminionService.ProgressSummary(config)}" : plugin.VerminionService.State != VerminionService.VerminionState.Idle
                ? plugin.VerminionService.StatusText
                : (VerminionService.WeeklyGoalReached(config) ? "Complete: " : string.Empty) + VerminionService.ProgressSummary(config),
            config.VerminionPaused ? "Resume##Verm" : "Run##Verm",
            () => { if (config.VerminionPaused) plugin.VerminionService.ResumeTask(); else plugin.VerminionService.RunTask(); }, "WIP",
            buttonDisabled: engine.IsRunning || plugin.VerminionService.IsActive,
            buttonTooltip: "Runs the selected weekly CPU goal; Resume continues the paused goal. Current strategy, progress and blockers appear here.",
            secondaryButtonLabel: "CPU campaign##Verminion", secondaryOnClick: () => plugin.VerminionService.RunChallenges(),
            secondaryButtonDisabled: engine.IsRunning || plugin.VerminionService.IsActive,
            secondaryButtonTooltip: "Permanent campaign: completes unfinished challenges in order. Cleared stages stay complete across weekly resets; the normal stage attempt limit applies.");
        AddTaskRow("Jumbo Cactpot", config.EnableJumboCactpot,
            GetJumboCactpotStatus(config),
            "Run##Jumbo", () => plugin.CactpotService.RunJumboCactpot(), "OK");
        AddTaskRow("Fashion Report", config.EnableFashionReport,
            GetFashionReportStatus(config),
            "Run##Fashion", () => plugin.FashionReportService.Start(), "OK");
        var deliveries = plugin.CustomDeliveriesService;
        var deliveryBlocker = deliveries.GetStartBlockedReason(config.CustomDeliveriesSettings);
        var deliveryBusy = IsEquipmentAutomationBusy() || plugin.FishingService.IsActive || plugin.FishingRelogCoordinator.IsActive;
        var deliveryStatus = deliveries.IsActive || deliveries.IsFailed
            ? deliveries.StatusText
            : deliveries.RemainingAllowances == 0
                ? "Complete: weekly allowances used"
                : $"Weekly: {deliveries.RemainingAllowances?.ToString() ?? "unknown"} allowances left";
        AddTaskRow("Custom Deliveries", config.EnableCustomDeliveries, deliveryStatus,
            "Run##CustomDeliveries", () => engine.ManualStartCustomDeliveries(), "OK",
            statusTooltip: deliveryStatus + "\nNPC rank, crafting/gathering/fishing bonuses and verified progress are shown below the task table.",
            buttonDisabled: deliveryBusy || deliveryBlocker != null || !deliveries.HasPendingWork(config.CustomDeliveriesSettings),
            buttonTooltip: deliveryBusy ? "An engine, equipment or fishing task is active."
                : deliveryBlocker != null ? deliveryBlocker
                : !deliveries.HasPendingWork(config.CustomDeliveriesSettings) ? "The selected policy has no remaining delivery route."
                : "Runs current-character custom deliveries with normal engine ownership and cancellation; spent allowances remain authoritative.");

        var stables = plugin.ChocoboStablesService;
        var stableBlocker = stables.GetStartBlockedReason(config.ChocoboStablesSettings);
        AddTaskRow("Chocobo Stables", config.EnableChocoboStables, stables.StatusText,
            "Run##Stables", () => engine.ManualStartChocoboStables(), "OK",
            statusTooltip: $"{stables.StatusText}\nCleanliness: {stables.Cleanliness}\nKrakka Root: {stables.FeedStock}; Magicked Stable Broom: {stables.BroomStock}; Thavnairian Onion: {stables.OnionStock}",
            buttonDisabled: deliveryBusy || stableBlocker != null,
            buttonTooltip: deliveryBusy ? "Another task is active." : stableBlocker ?? "Visit the selected stable, clean if needed and train the selected bird once.",
            secondaryButtonLabel: "scan##Stables", secondaryOnClick: () => engine.ManualStartChocoboStables(true),
            secondaryButtonDisabled: deliveryBusy || stables.GetStartBlockedReason(config.ChocoboStablesSettings, true) != null,
            secondaryButtonTooltip: "Visit and scan the selected stable without using feed or brooms.");

        // --- Daily Tasks ---
        AddTaskRow("Mini Cactpot", config.EnableMiniCactpot,
            GetDailyTaskStatus(config.MiniCactpotLastCompleted, config.MiniCactpotNextReset, "Done today", "Daily"),
            "Run##Mini", () => plugin.CactpotService.RunMiniCactpot(), "OK");
        AddTaskRow("Chocobo Racing", config.EnableChocoboRacing,
            GetDailyTaskStatus(config.ChocoboRacingLastCompleted, config.ChocoboRacingNextReset, "Done today", "Daily"),
            "Run##Choco", () => plugin.ChocoboRaceService.RunTask(), "OK");
        var alliedGearsetValid = IsAlliedSocietyGearsetValid(config);
        AddTaskRow("Allied Society", config.EnableAlliedSociety,
            GetAlliedSocietyStatus(config),
            "Run##AlliedSociety", () => plugin.AlliedSocietyService.Start(config), "OK",
            buttonDisabled: equipmentAutomationBusy || !alliedGearsetValid,
            buttonTooltip: !alliedGearsetValid ? "Select a valid current or saved gearset before starting."
                : equipmentAutomationBusy ? "An engine or equipment task is active."
                : "Runs Questionable Companion's Allied Society rotation for only the current Name@HomeWorld character.");
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
        AddTaskRow("LootGoblin Map Gather", config.EnableLootGoblinMapGather,
            lootGoblinStatus,
            "Run##LootGoblinMapGather", () =>
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
        AddTaskRow("nag your mom", config.EnableNagYourMom,
            GetNagYourMomStatus(config, engine.NagYourMomStatusText),
            "Run##Mom", () =>
            {
                var route = GetFirstDueNagYourMomRoute(config);
                var remainingRuns = Math.Max(1, GetRemainingNagYourMomRuns(config, route));
                if (!MomSchedule.TryGetQueueDeadlineUtc(config.NagYourMomWindowStartLocal, config.NagYourMomWindowEndLocal, DateTime.Now, out var deadlineUtc))
                {
                    Plugin.ChatGui.Print("[Vermaxion] mom start blocked: outside the configured local window.");
                    return;
                }
                var result = plugin.MomIPCClient.StartRun(remainingRuns, config.NagYourMomJob, route == MomRunRoutes.CasualCc && config.NagYourMomStopAtSeriesRank25, route,
                    queueDeadlineUtc: deadlineUtc);
                Plugin.ChatGui.Print($"[Vermaxion] mom {result.Status}: {result.Summary} route={result.Route} runs={result.CompletedRunCount}/{result.RequestedRunCount}");
            }, "OK",
            secondaryButtonLabel: "Test Series Rank##MomSeriesRank",
            secondaryOnClick: engine.TestNagYourMomSeriesRank,
            secondaryButtonTooltip: "Read the current PvP series rank once without starting mom or changing configuration.");
        AddTaskRow("nag your dad", config.EnableNagYourDad,
            GetNagYourDadStatus(config, engine.NagYourDadStatusText, plugin.DadIPCClient.LastSubmissionStatus),
            "Run##Dad", () =>
            {
                var activeConfig = plugin.ConfigManager.GetActiveConfig();
                var result = plugin.DadIPCClient.StartSelection(
                    activeConfig.NagYourDadSelectionKind,
                    activeConfig.NagYourDadSelectionId,
                    activeConfig.NagYourDadSelectionDisplayName);
                Plugin.ChatGui.Print($"[Vermaxion] {result.StatusText}");
            }, "OK");
        AddTaskRow("Adventurer Activity (Evercold)", config.EnableEvercoldAdventurerActivity,
            GetEvercoldAdventurerActivityStatus(config),
            "Stub##EvercoldActivity", () =>
            {
                Plugin.Log.Information("[EvercoldActivity] WIP stub requested from main window.");
                Plugin.ChatGui.Print("[Vermaxion] Adventurer Activity (Evercold) is WIP. Progress is config-only for now.");
            }, "WIP");

        // --- Utility Tasks ---
        var collectionBlocker = plugin.FishCollection.GetStartBlockedReason();
        AddTaskRow("Fish collection", true, plugin.FishCollection.Status,
            "Run##FishCollection", () => plugin.FishCollection.Start(), "OK",
            buttonDisabled: collectionBlocker != null,
            buttonTooltip: collectionBlocker);
        AddTaskRow("Highest Combat Job", config.EnableHighestCombatJob, AutomationCatalog.Get(AutomationCatalog.HighestCombatJob).CadenceLabel,
            "Run##Highest", () => plugin.HighestCombatJobService.RunTask(), "OK");
        AddTaskRow("Current Job Equipment", config.EnableCurrentJobEquipment, AutomationCatalog.Get(AutomationCatalog.CurrentJobEquipment).CadenceLabel,
            "Run##Current", () => plugin.CurrentJobEquipmentService.RunTask(), "OK");

        return rows;

        void AddTaskRow(
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
                GetTaskSettingsSection(feature?.Id),
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
            rows.Add(row);
        }
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
                return ["ADS", "Lifestream", "vnavmesh"];
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
                    "ADS",
                };
                return dependencies;
            }
            case "Refill Listings":
                return ["AutoRetainer", "Lifestream", "vnavmesh"];
            case "Retainer Equipping":
                return ["AutoRetainer"];
            case "Retainer Bell":
                return ["Lifestream", "vnavmesh"];
            case "After-AR Park":
            case "Verminion":
            case "Chocobo Racing":
                return ["Lifestream"];
            case "Jumbo Cactpot":
            case "Fashion Report":
                return ["Lifestream", "vnavmesh"];
            case "Chocobo Stables":
                return ["vnavmesh"];
            case "Mini Cactpot":
                return config.RequireSaucyForMiniCactpot
                    ? ["Lifestream", "vnavmesh", "Saucy"]
                    : ["Lifestream", "vnavmesh"];
            case "Allied Society":
                return ["QSTCompanion"];
            case "Custom Deliveries":
            {
                var dependencies = new List<string> { "Lifestream", "vnavmesh" };
                if (config.CustomDeliveriesSettings.AllowedTypes.HasFlag(DeliveryTypes.Crafting)) dependencies.Add("Artisan");
                if ((config.CustomDeliveriesSettings.AllowedTypes & (DeliveryTypes.Mining | DeliveryTypes.Botany)) != 0) dependencies.Add("WigglyQuest");
                if (config.CustomDeliveriesSettings.AllowedTypes.HasFlag(DeliveryTypes.Fishing)) dependencies.Add("AutoHook");
                return dependencies;
            }
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

    private void DrawDashboardRows(
        IReadOnlyList<TaskRowDescriptor> rows,
        bool favoritesOnly,
        IReadOnlySet<string> loadedPluginInternalNames,
        string view)
    {
        var scale = UIConstants.Scale;
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(ImGui.GetStyle().FramePadding.X, scale));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(ImGui.GetStyle().ItemSpacing.X, (UIConstants.Compact ? 2 : 4) * scale));
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(ImGui.GetStyle().CellPadding.X, (UIConstants.Compact ? 2 : 3) * scale));
        try
        {
            if (plugin.Configuration.AutoWidthMainTaskColumns && ImGui.BeginTable("TaskHeadings", 3,
                ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("Task", ImGuiTableColumnFlags.WidthStretch, 1f);
                ImGui.TableSetupColumn("State & dependencies", ImGuiTableColumnFlags.WidthStretch, 1f);
                ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 180f * scale);
                ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
                for (var column = 0; column < 3; column++)
                    if (ImGui.TableSetColumnIndex(column))
                        UIConstants.WrappedText(UIConstants.Text, ImGui.TableGetColumnName(column));
                ImGui.EndTable();
            }
            if (favoritesOnly)
                DrawGroup("Favorites", "Favorites", rows);
            else
            {
                if (view == "Overview" || !plugin.Configuration.AutoWidthMainTaskColumns)
                {
                    foreach (var section in Enum.GetValues<AutomationDashboardSection>())
                        DrawGroup(section.ToString(), AutomationDashboardPolicy.GetStateLabel(section),
                            rows.Where(row => row.Feature != null && row.Section == section).ToList());
                    DrawGroup("Manual", "Manual utilities", rows.Where(row => row.Feature == null).ToList());
                }
                else
                {
                    DrawGroup("Daily", "Daily", rows.Where(row => row.Feature?.Cadence == AutomationCadence.Daily).ToList());
                    DrawGroup("Weekly", "Weekly", rows.Where(row => row.Feature?.Cadence == AutomationCadence.Weekly || row.Id == AutomationCatalog.ChocoboStables).ToList());
                    DrawGroup("Utilities", "Utilities", rows.Where(row => row.Feature?.Cadence != AutomationCadence.Daily && row.Feature?.Cadence != AutomationCadence.Weekly && row.Id != AutomationCatalog.ChocoboStables).ToList());
                }
            }
        }
        finally { ImGui.PopStyleVar(3); }
        void DrawGroup(string id, string label, IReadOnlyList<TaskRowDescriptor> groupRows)
        {
            if (groupRows.Count == 0 && !favoritesOnly) return;
            ImGui.PushID(view + id);
            if (UIConstants.BeginPanel("TaskPanel"))
            {
                if (favoritesOnly) UIConstants.Heading(UiText.F($"Favorites ({groupRows.Count})"), true);
                if (favoritesOnly || UIConstants.CollapsingHeading($"{label} ({groupRows.Count})###TaskGroup", ImGuiTreeNodeFlags.DefaultOpen,
                    UiText.F("{0} ({1})", UiText.T(label), groupRows.Count)))
                {
                    var automatic = plugin.Configuration.AutoWidthMainTaskColumns;
                    var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp |
                        (automatic ? ImGuiTableFlags.NoSavedSettings : ImGuiTableFlags.Resizable);
                    if (ImGui.BeginTable(automatic ? "TaskRowsAutoV3" : "TaskRowsManualV3", 3, flags))
                    {
                        var favoriteWidth = Math.Max(MaterialText.Measure(UiText.T("Favorite")).X,
                            MathF.Ceiling(ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.X +
                                Math.Max(UIConstants.ButtonWidth("★"), UIConstants.ButtonWidth("☆"))) + UIConstants.Scale);
                        ImGui.TableSetupColumn("Favorite", automatic ? ImGuiTableColumnFlags.WidthStretch : ImGuiTableColumnFlags.WidthFixed, automatic ? 1f : favoriteWidth);
                        ImGui.TableSetupColumn("Task", ImGuiTableColumnFlags.WidthStretch, 1f);
                        ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 180f * UIConstants.Scale);
                        if (!automatic) UiGui.TableHeadersRow();
                        foreach (var row in groupRows) DrawDashboardRow(row, !favoritesOnly, loadedPluginInternalNames);
                        ImGui.EndTable();
                    }
                }
                UIConstants.EndPanel();
            }
            ImGui.PopID();
        }
    }

    private void DrawDashboardRow(
        TaskRowDescriptor row,
        bool showDiagnosticActions,
        IReadOnlySet<string> loadedPluginInternalNames)
    {
        var collection = row.Task == "Fish collection" ||
            row.Feature?.Id == AutomationCatalog.Fishing && plugin.FishCollection.IsActive;
        string StatusText(string text) => collection
            ? UiText.Collection(text, plugin.FishCollection.Supplies.Supplies.Select(supply => supply.Name), plugin.FishCollection.LastOutcome)
            : row.Feature?.Id == AutomationCatalog.VerminionQueue
                ? UiText.Verminion(text, VerminionService.ProgressSummary(plugin.ConfigManager.GetActiveConfig()))
                : UiText.T(text);
        var isFavorite = row.Feature != null && IsFavorite(row.Feature.Id);
        var dependencySummary = BuildTaskDependencySummary(row, loadedPluginInternalNames);
        var controlHeight = (UIConstants.Compact ? 24 : 28) * UIConstants.Scale;
        var automatic = plugin.Configuration.AutoWidthMainTaskColumns;
        ImGui.TableNextRow(ImGuiTableRowFlags.None, (plugin.Configuration.CompactUi ? 34 : VermaxionPresentation.MainRow) * UIConstants.Scale);
        ImGui.TableSetColumnIndex(0);
        if (row.Feature != null)
        {
            var enabled = row.Enabled;
            ImGui.BeginDisabled(plugin.ConfigManager.GetCurrentAccount() == null);
            if (ImGui.Checkbox("##TaskEnabled_" + row.Id, ref enabled))
            {
                typeof(CharacterConfig).GetProperty(row.Feature.FlagProperty)?.SetValue(plugin.ConfigManager.GetActiveConfig(), enabled);
                if (!enabled && row.Id == AutomationCatalog.ChocoboRacing)
                    plugin.PauseCurrentTargetCycleBestEffort("Dashboard disabled Chocobo Racing");
                plugin.ConfigManager.SaveCurrentAccount();
            }
            ImGui.EndDisabled();
            UIConstants.SameLineIfFits(isFavorite ? "★" : "☆");
            if (UIConstants.Button($"{(isFavorite ? "★" : "☆")}##Favorite_{row.Feature.Id}", new Vector2(0, controlHeight)))
            {
                plugin.Configuration.FavoriteAutomationIds = AutomationCatalog.ToggleFavorite(
                    plugin.Configuration.FavoriteAutomationIds,
                    row.Feature.Id);
                plugin.Configuration.Save();
            }
            if (ImGui.IsItemHovered())
                UiGui.SetTooltip(isFavorite ? "Remove from Favorites" : "Add to Favorites");
        }

        if (automatic && row.Feature != null)
            UIConstants.SameLineIfFits(row.Task, MaterialText.Measure(UiText.T(row.Task)).X);
        if (!automatic) ImGui.TableSetColumnIndex(1);
        var split = !automatic && ImGui.BeginTable("TaskDetails_" + row.Id, 2, ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.BordersInnerV);
        if (split) { ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); }
        ImGui.PushTextWrapPos(0f);
        UiGui.TextUnformatted(row.Task);
        ImGui.PopTextWrapPos();
        if (ImGui.IsItemHovered())
            MaterialText.SetTooltip(UiText.F($"{BuildTaskTooltip(row, StatusText)}\nDependencies: {UiText.T(dependencySummary.Tooltip)}"));

        if (automatic || split) ImGui.TableSetColumnIndex(1);
        var timing = row.Feature == null ? "On demand" : FormatWhen(row);
        var readiness = dependencySummary.Checks.Count == 0 ? "No dependencies required" : dependencySummary.Label;
        var status = string.IsNullOrWhiteSpace(row.Status) || row.Status == row.Feature?.CadenceLabel ? UiText.T(timing) : StatusText(row.Status);
        var when = UiText.T(timing);
        var stateText = status == when ? status : status + " · " + when;
        stateText += " · " + UiText.T(readiness);
        var wip = row.Maturity == "WIP";
        var wipLabel = UiText.T("WIP");
        var wipWidth = MathF.Ceiling(MaterialText.Measure(wipLabel).X + 12 * UIConstants.Scale);
        var statusSplit = wip && ImGui.GetContentRegionAvail().X >= wipWidth + MaterialText.Measure("0000000000").X +
            ImGui.GetStyle().CellPadding.X * 4 + ImGui.GetStyle().ItemSpacing.X &&
            ImGui.BeginTable("TaskStatus_" + row.Id, 2, ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.SizingStretchProp);
        if (statusSplit)
        {
            ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("Maturity", ImGuiTableColumnFlags.WidthFixed, wipWidth);
            ImGui.TableNextRow(); ImGui.TableNextColumn();
        }
        ImGui.BeginGroup();
        UIConstants.WrappedText(GetSectionColor(row.Section), stateText, translate: false);
        ImGui.EndGroup();
        if (ImGui.IsItemHovered())
            MaterialText.SetTooltip(UiText.F($"{BuildTaskTooltip(row, StatusText)}\nDependencies: {UiText.T(dependencySummary.Tooltip)}"));
        if (wip)
        {
            if (statusSplit) ImGui.TableNextColumn();
            else UIConstants.SameLineIfFits("WIP", wipWidth);
            MaterialStatus.Badge(wipLabel, new MaterialControlAppearance(UIConstants.Amber, new Vector4(0, 0, 0, 1), Vector4.Zero),
                new Vector2(wipWidth / UIConstants.Scale, controlHeight / UIConstants.Scale));
            if (statusSplit) ImGui.EndTable();
        }
        if (split) ImGui.EndTable();
        ImGui.TableSetColumnIndex(2);
        var available = ImGui.GetContentRegionAvail().X;
        var gap = ImGui.GetStyle().ItemSpacing.X;
        var runMeasure = UIConstants.ButtonWidth(row.ButtonLabel);
        var settingsMeasure = UIConstants.ButtonWidth("Settings");
        var runWidth = row.SettingsSection.HasValue
            ? Math.Max(1f, available - gap) * runMeasure / (runMeasure + settingsMeasure) : available;
        ImGui.BeginDisabled(row.ButtonDisabled);
        if (UIConstants.Button(row.ButtonLabel, new Vector2(runWidth, controlHeight)))
            plugin.RunDashboardAction(row.OnClick);
        ImGui.EndDisabled();
        if (!string.IsNullOrWhiteSpace(row.ButtonTooltip) && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            MaterialText.SetTooltip(StatusText(row.ButtonTooltip));

        if (row.SettingsSection.HasValue)
        {
            ImGui.SameLine();
            if (UIConstants.Button($"Settings##Task_{row.Feature?.Id}", new Vector2(Math.Max(1f, available - gap - runWidth), controlHeight)))
                plugin.ConfigWindow.OpenAutomationSettings(row.SettingsSection.Value, row.Feature?.Id);
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                UiGui.SetTooltip("Open this task's settings in the selected editing scope.");
        }

        if ((showDiagnosticActions || row.Id == AutomationCatalog.VerminionQueue) && !string.IsNullOrWhiteSpace(row.SecondaryButtonLabel) && row.SecondaryOnClick != null)
        {
            ImGui.BeginDisabled(row.SecondaryButtonDisabled);
            if (UIConstants.Button(row.SecondaryButtonLabel, new Vector2(ImGui.GetContentRegionAvail().X, controlHeight)))
                plugin.RunDashboardAction(row.SecondaryOnClick);
            ImGui.EndDisabled();
            if (!string.IsNullOrWhiteSpace(row.SecondaryButtonTooltip) && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                UiGui.SetTooltip(row.SecondaryButtonTooltip);
        }
        if (showDiagnosticActions && !string.IsNullOrWhiteSpace(row.TertiaryButtonLabel) && row.TertiaryOnClick != null)
        {
            ImGui.BeginDisabled(row.TertiaryButtonDisabled);
            if (UIConstants.Button(row.TertiaryButtonLabel, new Vector2(ImGui.GetContentRegionAvail().X, controlHeight)))
                plugin.RunDashboardAction(row.TertiaryOnClick);
            ImGui.EndDisabled();
            if (!string.IsNullOrWhiteSpace(row.TertiaryButtonTooltip) && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                UiGui.SetTooltip(row.TertiaryButtonTooltip);
        }
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
        var loaded = loadedPluginInternalNames.Contains(name);
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
            AutomationCatalog.VerminionQueue => VerminionService.WeeklyGoalReached(config)
                ? ResetDetectionService.GetNextWeeklyReset(DateTime.UtcNow) : DateTime.MinValue,
            AutomationCatalog.CustomDeliveries => plugin.CustomDeliveriesService.RemainingAllowances == 0
                ? ResetDetectionService.GetNextWeeklyReset(DateTime.UtcNow) : DateTime.MinValue,
            AutomationCatalog.MiniCactpot => config.MiniCactpotNextReset,
            AutomationCatalog.ChocoboRacing => config.ChocoboRacingNextReset,
            AutomationCatalog.ChocoboStables => config.ChocoboStablesNextTrainingUtc,
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

        var inWindow = MomSchedule.TryGetQueueDeadlineUtc(config.NagYourMomWindowStartLocal, config.NagYourMomWindowEndLocal, now, out _);
        if (inWindow)
            return DateTime.UtcNow;

        var nextStart = now.Date.Add(start);
        if (start <= end && now.TimeOfDay >= end)
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
            return "Due now";
        if (row.Section == AutomationDashboardSection.Blocked)
            return "Blocked";
        if (row.Eligibility.Status == TaskEligibilityStatus.Disabled)
            return "Off";
        if (row.Section == AutomationDashboardSection.Complete)
            return "Complete";
        if (row.NextEligibleAtUtc.HasValue)
            return row.NextEligibleAtUtc.Value.ToLocalTime().ToString("MMM dd HH:mm", UiText.Current.Culture);
        return "Blocked";
    }

    private static string BuildTaskTooltip(TaskRowDescriptor row, Func<string, string>? statusText = null)
    {
        statusText ??= UiText.T;
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
            ? row.NextEligibleAtUtc.Value.ToLocalTime().ToString("MMM dd yyyy HH:mm zzz", UiText.Current.Culture)
            : row.Section == AutomationDashboardSection.DueNow ? "Now" : "Not scheduled";
        var nextUtc = row.NextEligibleAtUtc.HasValue
            ? row.NextEligibleAtUtc.Value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'")
            : row.Section == AutomationDashboardSection.DueNow ? "Now" : "Not scheduled";
        var statusDetail = !string.IsNullOrWhiteSpace(row.StatusTooltip) &&
                           !string.Equals(row.StatusTooltip, row.Status, StringComparison.Ordinal)
            ? "\n" + UiText.F("Status detail: {0}", statusText(row.StatusTooltip))
            : string.Empty;

        return string.Join("\n",
            UiText.T(row.Task),
            UiText.F("Enabled: {0}", UiText.T(row.Enabled ? "On" : "Off")),
            UiText.F("Dashboard state: {0}", UiText.T(AutomationDashboardPolicy.GetStateLabel(row.Section))),
            UiText.F("Status: {0}{1}", statusText(row.Status), statusDetail),
            UiText.F("Eligibility detail: {0}", statusText(row.Eligibility.Reason)),
            UiText.F("Blocker: {0}", statusText(blocker)),
            UiText.F("Owner: {0}", UiText.T(owner)),
            UiText.F("Cadence: {0}", UiText.T(cadence)),
            UiText.F("Maturity: {0}", UiText.T(maturity)),
            UiText.F("Next eligible (local): {0}", UiText.T(nextLocal)),
            UiText.F("Next eligible (UTC): {0}", UiText.T(nextUtc)),
            UiText.F("Disabled action reason: {0}", statusText(disabledActionReason)));
    }

    private static Vector4 GetSectionColor(AutomationDashboardSection section)
        => section switch
        {
            AutomationDashboardSection.DueNow => UIConstants.Blue,
            AutomationDashboardSection.Blocked => UIConstants.Amber,
            AutomationDashboardSection.ScheduledLater => UIConstants.Metadata,
            _ => UIConstants.Text,
        };

    private bool IsFavorite(string automationId)
        => plugin.Configuration.FavoriteAutomationIds?.Contains(automationId, StringComparer.Ordinal) ?? false;

    private static AutomationFeatureDefinition? GetDisplayedFeature(string task)
    {
        var id = task switch
        {
            "Misc Cmd" => AutomationCatalog.MiscCommands,
            "Verminion" => AutomationCatalog.VerminionQueue,
            _ => null,
        };
        return id != null
            ? AutomationCatalog.Get(id)
            : AutomationCatalog.Features.FirstOrDefault(
                feature => string.Equals(feature.Label, task, StringComparison.Ordinal));
    }

    private static ConfigurationSection? GetTaskSettingsSection(string? automationId)
        => automationId switch
        {
            AutomationCatalog.MiscCommands or
            AutomationCatalog.FCBuffRefill or
            AutomationCatalog.MinionRoulette or
            AutomationCatalog.SeasonalGear or
            AutomationCatalog.GearUpdater or
            AutomationCatalog.HighestCombatJob or
            AutomationCatalog.CurrentJobEquipment or
            AutomationCatalog.AfterArPark or
            AutomationCatalog.VendorStock or
            AutomationCatalog.Fishing or
            AutomationCatalog.RetainerEquipping => ConfigurationSection.EveryAr,
            AutomationCatalog.VerminionQueue or
            AutomationCatalog.JumboCactpot or
            AutomationCatalog.FashionReport or
            AutomationCatalog.CustomDeliveries or
            AutomationCatalog.RegisterRegistrables => ConfigurationSection.Weekly,
            AutomationCatalog.MiniCactpot or
            AutomationCatalog.ChocoboRacing or
            AutomationCatalog.AlliedSociety or
            AutomationCatalog.LootGoblinMapGather => ConfigurationSection.Daily,
            AutomationCatalog.RefillListings or
            AutomationCatalog.NagYourMom or
            AutomationCatalog.NagYourDad => ConfigurationSection.VariableTime,
            AutomationCatalog.ChocoboStables => ConfigurationSection.Weekly,
            AutomationCatalog.EvercoldAdventurerActivity => ConfigurationSection.Wip,
            _ => null,
        };

    internal sealed record TaskRowDescriptor(
        AutomationFeatureDefinition? Feature,
        string Task,
        bool Enabled,
        string Status,
        TaskEligibility Eligibility,
        AutomationDashboardSection Section,
        DateTime? NextEligibleAtUtc,
        ConfigurationSection? SettingsSection,
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
        string? TertiaryButtonTooltip)
    {
        // Manual utilities already have unique, stable dashboard button IDs.
        public string Id => Feature?.Id ?? ButtonLabel;
        public bool IsConfigurationOnly => Feature?.Owner == AutomationOwner.ConfigOnlyWip;
        public string? DebugBlockedReason => IsConfigurationOnly
            ? "Configuration-only WIP; no runtime dispatch is available."
            : ButtonDisabled ? ButtonTooltip ?? "The dashboard manual action is unavailable." : null;
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

    internal static void DrawCustomDeliveryNpcOverview(Plugin plugin, CustomDeliveriesSettings settings)
    {
        var service = plugin.CustomDeliveriesService;
        UiGui.TextWrapped(UiText.F($"Character allowances: {service.RemainingAllowances?.ToString(UiText.Current.Culture) ?? UiText.T("unknown")} remaining this week."));
        if (service.Npcs.Count == 0 || !service.RemainingAllowances.HasValue)
        {
            UiGui.TextDisabled("NPC ranks, allowances and achievement progress are unknown until current-character data is available.");
            return;
        }
        if (!ImGui.BeginTable("CustomDeliveryNpcOverview", 6,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
            return;
        ImGui.TableSetupColumn("NPC");
        ImGui.TableSetupColumn("Rank / left");
        ImGui.TableSetupColumn("Progress");
        ImGui.TableSetupColumn("Craft");
        ImGui.TableSetupColumn("Gather");
        ImGui.TableSetupColumn("Fish");
        UiGui.TableHeadersRow();
        foreach (var npc in service.Npcs)
        {
            string RouteLabel(DeliveryTypes type, int bonusIndex)
            {
                var label = type switch
                {
                    DeliveryTypes.Crafting => "CRAFTER",
                    DeliveryTypes.Mining => "MIN",
                    DeliveryTypes.Botany => "BTN",
                    _ => "FSH",
                };
                var bonus = npc.IsBonusEffective[bonusIndex] ? " +bonus" : string.Empty;
                if (!settings.AllowedTypes.HasFlag(type))
                    return $"{label}{bonus}: disabled";
                var jobs = service.GetEligibleJobs(npc, type, settings);
                if (jobs.Count == 0)
                    return $"{label}{bonus}: ineligible";
                return $"{label}{bonus}: {string.Join(", ", jobs.Select(jobId => Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.ClassJob>().GetRow(jobId).Abbreviation.ExtractText()))}";
            }
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            UiGui.TextWrapped(npc.Name);
            ImGui.TableSetColumnIndex(1);
            UiGui.TextWrapped(npc.Unlocked ? UiText.F($"{npc.Rank} / {Math.Max(0, npc.MaxDeliveries - npc.UsedDeliveries)}") : "Locked");
            if (npc.Unlocked && npc.PendingRankQuestId != 0)
                UiGui.TextWrapped(UiText.F($"Quest: {npc.PendingRankQuestName}"));
            else if (npc.Unlocked && npc.SatisfactionMax > 0)
                UiGui.TextDisabled(UiText.F($"Satisfaction: {npc.SatisfactionCur}/{npc.SatisfactionMax}"));
            ImGui.TableSetColumnIndex(2);
            UiGui.TextWrapped(npc.AchievementCur.HasValue ? UiText.F($"{npc.AchievementCur.Value} / 150") : "Unknown / 150");
            ImGui.TableSetColumnIndex(3);
            UiGui.TextWrapped(RouteLabel(DeliveryTypes.Crafting, 0));
            ImGui.TableSetColumnIndex(4);
            UiGui.TextWrapped(RouteLabel(DeliveryTypes.Mining, 1));
            UiGui.TextWrapped(RouteLabel(DeliveryTypes.Botany, 1));
            ImGui.TableSetColumnIndex(5);
            UiGui.TextWrapped(RouteLabel(DeliveryTypes.Fishing, 2));
        }
        ImGui.EndTable();
        UiGui.TextDisabled("Ineligible: delivery type or job not selected, no matching gathering nodes or gearset, locked, or below rank/recipe requirements. Achievement progress is never guessed.");
        if (settings.ShowDebugUI)
            UiGui.TextWrapped(UiText.F($"Delivery status: {UiText.T(service.StatusText)}; observed turn-ins: {service.CompletedTurnins}"));
    }

    private bool IsEquipmentAutomationBusy()
        => plugin.Engine.IsRunning ||
           plugin.CustomDeliveriesService.IsActive ||
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

        if (engineStatus.Contains("Window closed", StringComparison.OrdinalIgnoreCase)
            || engineStatus.StartsWith("Scheduled mom work blocked:", StringComparison.Ordinal))
            return engineStatus;

        if (string.IsNullOrWhiteSpace(config.NagYourMomJob))
            return "Set job";

        if (!IsAnyNagYourMomRouteDue(config))
            return "Route caps hit";

        if (!TimeSpan.TryParse(config.NagYourMomWindowStartLocal, out var start) || !TimeSpan.TryParse(config.NagYourMomWindowEndLocal, out var end))
            return "Bad local window";

        var inWindow = MomSchedule.TryGetQueueDeadlineUtc(config.NagYourMomWindowStartLocal, config.NagYourMomWindowEndLocal, DateTime.Now, out _);

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
