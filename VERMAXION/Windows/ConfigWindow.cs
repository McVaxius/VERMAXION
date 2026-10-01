using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Dalamud.Game;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using Lumina.Excel.Sheets;
using VERMAXION.Models;
using VERMAXION.Services;
using VERMAXION.CustomDeliveries;

namespace VERMAXION.Windows;

public class ConfigWindow : Window, IDisposable
{
    private const string StylistRepositoryUrl = "https://raw.githubusercontent.com/NightmareXIV/MyDalamudPlugins/main/pluginmaster.json";
    private readonly Plugin plugin;
    private string editAccountAlias = "";
    private AutomationFeatureDefinition? characterFilter;
    private readonly List<DadDutyOption> dadDutyOptions = new();
    private string dadDungeonSearch = "";
    private bool dadDutyOptionsLoaded;
    private string[] dadLanPartyPresetOptions = DadRunRequestOptions.LanPartyPresetStubs;
    private DateTime dadLanPartyPresetRefreshUtc = DateTime.MinValue;
    private SetupWizardKind? activeWizard;
    private CharacterConfig? wizardDraft;
    private bool wizardPopupRequested;
    private bool pendingFishingCatalogRow;
    private bool focusFishingCatalogSearch;
    private string fishingCatalogSearch = string.Empty;
    private uint fishingCatalogRemoveItemId;
    private ConfigTab? requestedTab;
    private ConfigurationSection selectedConfigurationSection = ConfigurationSection.EveryAr;
    private bool scrollConfigurationToTop;
    private string taskSearch = string.Empty;
    private string characterSearch = string.Empty;
    private string selectedAutomationId = AutomationCatalog.MiscCommands;
    private bool confirmationPopupRequested;
    private string confirmationTitle = string.Empty;
    private string confirmationMessage = string.Empty;
    private System.Action? confirmedAction;
    private Func<bool>? confirmationContextValid;
    private Func<string>? confirmationWarning;
    private Func<bool>? wizardContextValid;
    private bool wizardFcBuffCadenceResetRequested;
    private string oceanFishingProviderSyncStatus = string.Empty;
    private bool? oceanFishingProviderSyncSucceeded;
    private ChokeAboTargetCycleCallResult? chokeAboTargetStatus;
    private ulong chokeAboTargetStatusContentId;
    private DateTime chokeAboTargetStatusNextRefreshUtc = DateTime.MinValue;
    private readonly List<ChocoboRaceAbility> chocoboAbilityOptions = new();
    private readonly List<Stain> chocoboColourOptions = new();
    private bool chocoboGoalOptionsLoaded;
    private string chocoboColourSearch = string.Empty;
    private string chocoboAbilitySearch = string.Empty;

    private enum ConfigTab
    {
        Settings,
        TaskOrder,
    }

    public void OpenWizard(SetupWizardKind kind)
    {
        var account = plugin.ConfigManager.GetCurrentAccount();
        wizardDraft = (account?.DefaultConfig ?? CharacterConfig.CreateNew()).Clone();
        activeWizard = kind;
        wizardContextValid = CaptureConfigurationContext();
        wizardFcBuffCadenceResetRequested = false;
        wizardPopupRequested = true;
    }

    public void OpenTaskOrder()
    {
        IsOpen = true;
        requestedTab = ConfigTab.TaskOrder;
    }

    public void OpenAutomationSettings(string automationId)
    {
        if (!AutomationCatalog.ById.ContainsKey(automationId))
            return;
        var settingsId = GetSettingsAutomationId(automationId);
        OpenAutomationSettings(GetSettingsSection(settingsId), settingsId);
    }

    public void OpenAutomationSettings(ConfigurationSection section)
        => OpenAutomationSettings(section, null);

    public void OpenAutomationSettings(ConfigurationSection section, string? automationId)
    {
        // Navigation retains the operator's account and character/default editing scope.
        IsOpen = true;
        requestedTab = ConfigTab.Settings;
        selectedAutomationId = automationId != null && AutomationCatalog.ById.ContainsKey(automationId)
            ? GetSettingsAutomationId(automationId)
            : AutomationCatalog.Features.First(feature => IsTaskNavigationEntry(feature) && GetSettingsSection(feature.Id) == section).Id;
        selectedConfigurationSection = GetSettingsSection(selectedAutomationId);
        taskSearch = string.Empty;
        scrollConfigurationToTop = true;
    }

    private static string GetSettingsAutomationId(string id) => id switch
    {
        AutomationCatalog.NagYourMomCasualCc or AutomationCatalog.NagYourMomFrontline or AutomationCatalog.NagYourMomRivalWings => AutomationCatalog.NagYourMom,
        _ => id,
    };

    private static bool IsTaskNavigationEntry(AutomationFeatureDefinition feature)
        => feature.Owner != AutomationOwner.ChildOption || feature.Id == AutomationCatalog.ReturnBeforeNag;

    private static ConfigurationSection GetSettingsSection(string id) => id switch
    {
        AutomationCatalog.VerminionQueue or AutomationCatalog.JumboCactpot or AutomationCatalog.FashionReport or
        AutomationCatalog.CustomDeliveries or AutomationCatalog.ChocoboStables or AutomationCatalog.RegisterRegistrables => ConfigurationSection.Weekly,
        AutomationCatalog.MiniCactpot or AutomationCatalog.ChocoboRacing or AutomationCatalog.AlliedSociety or
        AutomationCatalog.LootGoblinMapGather => ConfigurationSection.Daily,
        AutomationCatalog.RefillListings or AutomationCatalog.ReturnBeforeNag or AutomationCatalog.NagYourMom or
        AutomationCatalog.NagYourDad => ConfigurationSection.VariableTime,
        AutomationCatalog.EvercoldAdventurerActivity => ConfigurationSection.Wip,
        _ => ConfigurationSection.EveryAr,
    };

    private sealed class DadDutyOption
    {
        public uint Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string ContentType { get; init; } = string.Empty;
        public string DisplayName => $"{Name} ({Id})";
        public string SearchText => $"{Name} {Id} {ContentType}";
    }

    public ConfigWindow(Plugin plugin)
        : base("Vermaxion Configuration##Config", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.plugin = plugin;
        Plugin.Log.Information("[ChocoboUX] Build marker chocobo-ux-20260930-02; separate offspring quantities and explicit permit counterpart objective.");
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(700, 500),
            MaximumSize = new Vector2(1200, 900),
        };
    }

    public void Dispose() { }

    public override void OnClose()
    {
        characterFilter = null;
        ClearConfirmation();
        CloseWizard();
    }

    public override void PreDraw() => UIConstants.PushStyle(plugin.Configuration.CompactUi);

    public override void PostDraw() => UIConstants.PopStyle();

    public override void Draw()
    {
        DrawAccountSelector(plugin.ConfigManager);
        DrawConfigurationScopeBanner(plugin.ConfigManager);
        ImGui.Separator();
        if (ImGui.BeginTabBar("ConfigTabs"))
        {
            var settingsFlags = requestedTab == ConfigTab.Settings
                ? ImGuiTabItemFlags.SetSelected
                : ImGuiTabItemFlags.None;
            if (ImGui.BeginTabItem("Characters", settingsFlags))
            {
                if (requestedTab == ConfigTab.Settings)
                    requestedTab = null;
                DrawSettingsTab();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Global"))
            {
                DrawGlobalSettingsTab();
                ImGui.EndTabItem();
            }
            var taskOrderFlags = requestedTab == ConfigTab.TaskOrder
                ? ImGuiTabItemFlags.SetSelected
                : ImGuiTabItemFlags.None;
            if (ImGui.BeginTabItem("Task Order", taskOrderFlags))
            {
                if (requestedTab == ConfigTab.TaskOrder)
                    requestedTab = null;
                if (ImGui.BeginChild("TaskOrderBody", new Vector2(0, 0), false, ImGuiWindowFlags.HorizontalScrollbar))
                    DrawTaskOrderTab();
                ImGui.EndChild();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Marketboard"))
            {
                DrawMarketboardSettings();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("About"))
            {
                if (ImGui.BeginChild("AboutBody", new Vector2(0, 0), false, ImGuiWindowFlags.HorizontalScrollbar))
                    DrawAboutTab();
                ImGui.EndChild();
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }

        DrawWizardPopup();
        DrawConfirmationPopup();
    }

    private void DrawTaskOrderTab()
    {
        var config = plugin.Configuration;
        if (PostProcessTaskOrder.Normalize(config))
            config.Save();

        if (!plugin.Engine.RegistryReady)
        {
            ImGui.TextColored(new Vector4(1f, 0.2f, 0.2f, 1f), "CONFIGURED BUT NOT DISPATCHABLE");
            ImGui.TextWrapped(plugin.Engine.RegistryDiagnostic);
            ImGui.Separator();
        }

        ImGui.Text("Global post-process order");
        ImGui.TextWrapped("Before AR runs while AutoRetainer is suppressed after login. After AR runs in the normal post-process slot. Up and Down stay within a lane; moving between lanes is always explicit.");
        ImGui.Spacing();

        if (ImGui.Button("Reset to default"))
        {
            RequestConfirmation(
                "Reset task order?",
                "Reset both global task-order lanes and every Before AR / After AR placement to the shipped defaults?",
                () =>
                {
                    PostProcessTaskOrder.ResetToDefault(config);
                    config.Save();
                });
        }

        ImGui.Separator();

        DrawTaskOrderLane(config, PostProcessTaskPhase.BeforeAR, "Before AR");
        ImGui.Spacing();
        DrawTaskOrderLane(config, PostProcessTaskPhase.AfterAR, "After AR");

        ImGui.Separator();
        DrawCatalogCategory(AutomationOwner.RunHook, "Run-start hook");
        DrawCatalogCategory(AutomationOwner.PreemptiveCoordinator, "Preemptive coordinator");
        ImGui.Text("Manual utility");
        ImGui.BulletText("Retainer Bell — manual utility; not part of ordered automation");
        DrawCatalogCategory(AutomationOwner.ConfigOnlyWip, "Configuration-only WIP");

        var blockers = AutomationCatalog.EngineTasks
            .Select(feature => (feature, eligibility: plugin.Engine.GetTaskEligibility(feature.Id)))
            .Where(item => item.eligibility.Status is TaskEligibilityStatus.Blocked or TaskEligibilityStatus.Unsupported)
            .ToList();
        if (blockers.Count > 0 && ImGui.CollapsingHeader("Current prerequisite blockers"))
        {
            foreach (var item in blockers)
                ImGui.BulletText($"{item.feature.Label}: {item.eligibility.Reason}");
        }
    }

    private void DrawTaskOrderLane(Configuration config, PostProcessTaskPhase phase, string label)
    {
        var lane = PostProcessTaskOrder.GetLane(
            config.PostProcessTaskOrder,
            config.PostProcessTaskPlacement,
            phase);
        ImGui.Text($"{label} lane ({lane.Count})");
        var tableFlags = ImGuiTableFlags.Borders |
                         ImGuiTableFlags.RowBg |
                         ImGuiTableFlags.Resizable |
                         ImGuiTableFlags.ScrollY |
                         ImGuiTableFlags.SizingStretchProp;
        if (!ImGui.BeginTable($"TaskOrder_{phase}", 4, tableFlags, new Vector2(0, 230f)))
            return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Move", ImGuiTableColumnFlags.WidthFixed, 72f);
        ImGui.TableSetupColumn("Task", ImGuiTableColumnFlags.WidthStretch, 1.2f);
        ImGui.TableSetupColumn("Cadence / owner / blocker", ImGuiTableColumnFlags.WidthStretch, 2f);
        ImGui.TableSetupColumn("Lane", ImGuiTableColumnFlags.WidthFixed, 104f);
        ImGui.TableHeadersRow();

        for (var laneIndex = 0; laneIndex < lane.Count; laneIndex++)
        {
            var taskId = lane[laneIndex];
            var definition = AutomationCatalog.Get(taskId);
            var eligibility = plugin.Engine.GetTaskEligibility(taskId);
            ImGui.PushID($"Lane_{phase}_{taskId}");
            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            ImGui.BeginDisabled(laneIndex == 0);
            if (ImGui.SmallButton("Up"))
            {
                config.PostProcessTaskOrder = PostProcessTaskOrder.MoveWithinLane(
                    config.PostProcessTaskOrder,
                    config.PostProcessTaskPlacement,
                    taskId,
                    -1);
                config.Save();
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.BeginDisabled(laneIndex == lane.Count - 1);
            if (ImGui.SmallButton("Dn"))
            {
                config.PostProcessTaskOrder = PostProcessTaskOrder.MoveWithinLane(
                    config.PostProcessTaskOrder,
                    config.PostProcessTaskPlacement,
                    taskId,
                    1);
                config.Save();
            }
            ImGui.EndDisabled();

            ImGui.TableSetColumnIndex(1);
            ImGui.TextWrapped($"{laneIndex + 1}. {definition.Label}");

            ImGui.TableSetColumnIndex(2);
            ImGui.TextWrapped($"{definition.CadenceLabel} · {definition.OwnershipLabel}");
            if (eligibility.Status is TaskEligibilityStatus.Blocked or TaskEligibilityStatus.Unsupported)
                ImGui.TextWrapped($"Blocked: {eligibility.Reason}");
            else
                ImGui.TextDisabled($"{eligibility.Status}: {eligibility.Reason}");

            ImGui.TableSetColumnIndex(3);
            var destination = phase == PostProcessTaskPhase.BeforeAR
                ? PostProcessTaskPhase.AfterAR
                : PostProcessTaskPhase.BeforeAR;
            if (ImGui.SmallButton($"Move to {(destination == PostProcessTaskPhase.BeforeAR ? "Before" : "After")}"))
            {
                config.PostProcessTaskPlacement = PostProcessTaskOrder.ChangePhase(
                    config.PostProcessTaskPlacement,
                    taskId,
                    destination);
                config.Save();
            }

            ImGui.PopID();
        }

        if (lane.Count == 0)
        {
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(1);
            ImGui.TextDisabled("No tasks are assigned to this lane.");
        }

        ImGui.EndTable();
    }

    private static void DrawCatalogCategory(AutomationOwner owner, string heading)
    {
        ImGui.Text(heading);
        foreach (var feature in AutomationCatalog.Features.Where(feature => feature.Owner == owner))
            ImGui.BulletText($"{feature.Label} — {feature.CadenceLabel}; {feature.OwnershipLabel}");
    }

    private void DrawGlobalSettingsTab()
    {
        var config = plugin.Configuration;

        ImGui.TextWrapped("Shared settings for all accounts and characters. Per-character task settings are in Characters.");
        ImGui.Separator();
        if (ImGui.BeginChild("GlobalSettingsBody", new Vector2(0, 0), false, ImGuiWindowFlags.HorizontalScrollbar))
        {
            if (ImGui.CollapsingHeader("Display & DTR", ImGuiTreeNodeFlags.DefaultOpen))
            {
            var compactUi = config.CompactUi;
            if (ImGui.Checkbox("Compact UI", ref compactUi))
            {
                config.CompactUi = compactUi;
                config.Save();
            }
            ImGui.TextWrapped("Use tighter spacing and heading gaps across all windows. All controls remain available.");
            ImGui.Spacing();
            var autoWidthMainTaskColumns = config.AutoWidthMainTaskColumns;
            if (ImGui.Checkbox(
                    UIConstants.ConfigLabels.AutoWidthMainTaskColumns,
                    ref autoWidthMainTaskColumns))
            {
                config.AutoWidthMainTaskColumns = autoWidthMainTaskColumns;
                config.Save();
            }
            DrawHelpMarker(UIConstants.Tooltips.AutoWidthMainTaskColumns);

            var krangleEnabled = config.KrangleEnabled;
            if (ImGui.Checkbox(UIConstants.ConfigLabels.KrangleNames, ref krangleEnabled))
            {
                config.KrangleEnabled = krangleEnabled;
                if (!krangleEnabled) KrangleService.ClearCache();
                config.Save();
            }
            ImGui.SameLine();
            ImGui.TextDisabled("(?)");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(UIConstants.Tooltips.KrangleNames);

            var dtrEnabled = config.DtrBarEnabled;
            if (ImGui.Checkbox(UIConstants.ConfigLabels.DtrBarEntry, ref dtrEnabled))
            {
                config.DtrBarEnabled = dtrEnabled;
                config.Save();
            }
            ImGui.SameLine();
            ImGui.TextDisabled("(?)");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Show/hide the DTR bar entry (server info bar).");

            ImGui.SameLine();

            var dtrMode = config.DtrBarMode;
            var dtrModes = new[] { "Text Only", "Icon+Text", "Icon Only" };
            ImGui.SetNextItemWidth(150);
            if (ImGui.Combo("DTR Mode", ref dtrMode, dtrModes, dtrModes.Length))
            {
                config.DtrBarMode = dtrMode;
                config.Save();
            }
            ImGui.SameLine();
            ImGui.TextDisabled("(?)");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("DTR bar display mode:\nText Only: 'VMX: On/Off'\nIcon+Text: '⚫ VMX'\nIcon Only: '⚫'");

            ImGui.Spacing();
            ImGui.Text("DTR Icons (max 3 characters)");
            ImGui.SameLine();
            ImGui.TextDisabled("(?)");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Customize the glyphs used for enabled/disabled icon modes.");
            ImGui.SameLine();
            if (ImGui.Button("Open Lodestone Glyphs"))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://na.finalfantasyxiv.com/lodestone/character/22423564/blog/4393835",
                    UseShellExecute = true
                });
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Opens Lodestone blog with available glyph codes");

            var enabledIcon = config.DtrIconEnabled;
            if (DrawIconInputs("Enabled", ref enabledIcon, "\uE03C"))
            {
                config.DtrIconEnabled = enabledIcon;
                config.Save();
            }

            var disabledIcon = config.DtrIconDisabled;
            if (DrawIconInputs("Disabled", ref disabledIcon, "\uE03D"))
            {
                config.DtrIconDisabled = disabledIcon;
                config.Save();
            }
            }

            if (ImGui.CollapsingHeader("Automation & Recovery"))
            {

            var autoRestoreRetainerChecking = config.AutoRestoreRetainerCheckingAfterWork;
            if (ImGui.Checkbox(
                    UIConstants.ConfigLabels.AutoRestoreRetainerCheckingAfterWork,
                    ref autoRestoreRetainerChecking))
            {
                config.AutoRestoreRetainerCheckingAfterWork = autoRestoreRetainerChecking;
                config.Save();
            }
            DrawHelpMarker(UIConstants.Tooltips.AutoRestoreRetainerCheckingAfterWork);
            ImGui.Indent();
            ImGui.TextWrapped("Works even when VERMAXION skips all tasks. Turn this off before intentionally disabling AutoRetainer checking for the current or previous character.");
            ImGui.Unindent();

            var enableCharacterSelectStallRecovery = config.EnableCharacterSelectStallRecovery;
            if (ImGui.Checkbox(
                    UIConstants.ConfigLabels.EnableCharacterSelectStallRecovery,
                    ref enableCharacterSelectStallRecovery))
            {
                config.EnableCharacterSelectStallRecovery = enableCharacterSelectStallRecovery;
                config.Save();
            }
            DrawHelpMarker(UIConstants.Tooltips.EnableCharacterSelectStallRecovery);

            var listingActionDelay = Math.Clamp(config.RefillListingsActionDelayMs, 0, 2000);
            ImGui.SetNextItemWidth(GetCompactNumericInputWidth() * 1.5f);
            if (ImGui.InputInt("Listing action delay (ms)", ref listingActionDelay, 50, 250))
            {
                config.RefillListingsActionDelayMs = Math.Clamp(listingActionDelay, 0, 2000);
                config.Save();
            }
            DrawHelpMarker("Delay after ordinary Refill Listings actions. Range: 0–2000 ms. Default: 250 ms. Setting 0 performs the next action without an added delay. Timeouts, navigation, close retries, and UI-settlement waits are unchanged.");

            var listingInterItemDelay = Math.Clamp(config.RefillListingsInterItemDelayMs, 0, 2000);
            ImGui.SetNextItemWidth(GetCompactNumericInputWidth() * 1.5f);
            if (ImGui.InputInt("Listing inter-item delay (ms)", ref listingInterItemDelay, 50, 250))
            {
                config.RefillListingsInterItemDelayMs = Math.Clamp(listingInterItemDelay, 0, 2000);
                config.Save();
            }
            DrawHelpMarker("Delay after a returned listing is verified and before the next listing is selected. Range: 0–2000 ms. Default: 250 ms. Menu clicks, verification polling, timeouts, navigation, closing, and UI-settlement waits are unchanged.");


            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Text($"Replayable setup wizards - {GetAccountDisplayName(plugin.ConfigManager, plugin.ConfigManager.CurrentAccountId)}");
            ImGui.BeginDisabled(plugin.ConfigManager.GetCurrentAccount() == null);
            if (ImGui.SmallButton("Default & Sync"))
                OpenWizard(SetupWizardKind.DefaultAndSync);
            ImGui.SameLine();
            if (ImGui.SmallButton("FC Buff"))
                OpenWizard(SetupWizardKind.FcBuff);
            ImGui.SameLine();
            if (ImGui.SmallButton("Fishing##Wizard"))
                OpenWizard(SetupWizardKind.Fishing);
            ImGui.SameLine();
            if (ImGui.SmallButton("Retainer Equipping"))
                OpenWizard(SetupWizardKind.RetainerEquipping);
            ImGui.EndDisabled();
            ImGui.TextWrapped("Wizards stage changes and edit only the current account's Default Config after Apply. Existing characters remain unchanged until an explicit row sync or Apply Default to ALL.");
            }

            if (ImGui.CollapsingHeader("Fishing"))
            {

            var oceanFishingWindowWatch = config.OceanFishingWindowWatchEnabled;
            if (ImGui.Checkbox("Watch Ocean Fishing windows", ref oceanFishingWindowWatch))
            {
                config.OceanFishingWindowWatchEnabled = oceanFishingWindowWatch;
                config.Save();
            }
            DrawHelpMarker("Actively check for Ocean Fishing windows without AR pre/post processing. Relogs and fishes using your configured fishing settings.");

            var fishingMode = config.FishingExecutionMode;
            if (ImGui.BeginCombo("Fishing mode", FormatFishingExecutionMode(fishingMode)))
            {
                foreach (var mode in Enum.GetValues<FishingExecutionMode>())
                {
                    var selected = mode == fishingMode;
                    if (ImGui.Selectable(FormatFishingExecutionMode(mode), selected))
                    {
                        config.FishingExecutionMode = mode;
                        config.Save();
                    }

                    if (selected)
                        ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
            DrawHelpMarker("Controls whether Fishing only runs on the current character or may relog through AutoRetainer to another enabled character on the current account.");

            var provider = config.OceanFishingProvider;
            ImGui.BeginDisabled(plugin.IsFishingRunActive);
            if (ImGui.BeginCombo("Ocean Fishing provider", FormatOceanFishingProvider(provider)))
            {
                foreach (var candidate in Enum.GetValues<OceanFishingProvider>())
                {
                    var selected = candidate == provider;
                    if (ImGui.Selectable(FormatOceanFishingProvider(candidate), selected))
                    {
                        config.OceanFishingProvider = candidate;
                        config.Save();
                        oceanFishingProviderSyncSucceeded =
                            plugin.AutoHookIPC.TrySynchronizeAutoOceanFish(candidate, out oceanFishingProviderSyncStatus);
                    }

                    if (selected)
                        ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
            ImGui.EndDisabled();
            DrawHelpMarker(
                "VerMAXION + AutoHook owns placement, bait, facing, casting, and recovery. AutoHook AutoOceanFish gives AutoHook all in-duty fishing while VERMAXION retains preparation, registration, results, cleanup, and return.");
            if (plugin.IsFishingRunActive)
                ImGui.TextDisabled("Provider is locked to the active Fishing run snapshot.");
            ImGui.BeginDisabled(plugin.IsFishingRunActive ||
                                !OceanFishingProviderPolicy.VermaxionOwnsInDutyFishing(provider));
            var positioningMode = config.OceanFishingPositioningMode;
            ImGui.TextUnformatted("Ocean Fishing positioning");
            if (ImGui.RadioButton("Fixed locations", positioningMode == OceanFishingPositioningMode.FixedLocations))
            {
                config.OceanFishingPositioningMode = OceanFishingPositioningMode.FixedLocations;
                config.Save();
            }
            ImGui.SameLine();
            if (ImGui.RadioButton("Continuous rail", positioningMode == OceanFishingPositioningMode.ContinuousRail))
            {
                config.OceanFishingPositioningMode = OceanFishingPositioningMode.ContinuousRail;
                config.Save();
            }
            ImGui.SameLine();
            if (ImGui.RadioButton("Spacing mode", positioningMode == OceanFishingPositioningMode.Spacing))
            {
                config.OceanFishingPositioningMode = OceanFishingPositioningMode.Spacing;
                config.Save();
            }
            ImGui.EndDisabled();
            DrawHelpMarker("Applies when VERMAXION owns positioning. Fixed locations randomly chooses one of 32 built-in spots; Continuous rail randomly chooses along the rail. Both allow shared spots and ignore nearby players. Spacing mode enables passenger assignment and player clearance. Locked during a Fishing run.");
            if (!string.IsNullOrWhiteSpace(oceanFishingProviderSyncStatus))
            {
                ImGui.TextColored(
                    oceanFishingProviderSyncSucceeded == true
                        ? new Vector4(0.25f, 1f, 0.35f, 1f)
                        : new Vector4(1f, 0.75f, 0.15f, 1f),
                    oceanFishingProviderSyncStatus);
            }

            var routePreference = OceanFishingRoutePolicy.Normalize(config.OceanFishingRoutePreference);
            if (ImGui.BeginCombo("Ocean Fishing route preference", routePreference.ToString()))
            {
                foreach (var preference in Enum.GetValues<OceanFishingRoutePreference>()
                             .Where(candidate => candidate != OceanFishingRoutePreference.Thavnair))
                {
                    var selected = preference == routePreference;
                    if (ImGui.Selectable(preference.ToString(), selected))
                    {
                        config.OceanFishingRoutePreference = preference;
                        config.Save();
                    }

                    if (selected)
                        ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
            DrawHelpMarker("Preferred Ocean Fishing route family for characters that do not have an explicit override.");

            var maxFisherLevel = config.FishingMaxFisherLevel;
            ImGui.SetNextItemWidth(GetCompactNumericInputWidth());
            if (ImGui.InputInt("Max Fisher level", ref maxFisherLevel))
            {
                config.FishingMaxFisherLevel = Math.Clamp(maxFisherLevel, 1, 100);
                config.Save();
            }
            DrawHelpMarker("Characters with Fisher at or above this level are skipped unless their active fishing window override applies.");

            var oceanFishingOffset = config.OceanFishingPreWindowOffsetMinutes;
            ImGui.SetNextItemWidth(GetCompactNumericInputWidth());
            if (ImGui.InputInt("Ocean Fishing pre-window offset", ref oceanFishingOffset))
            {
                config.OceanFishingPreWindowOffsetMinutes = Math.Clamp(
                    oceanFishingOffset,
                    FishingDefaults.MinOceanFishingPreWindowOffsetMinutes,
                    FishingDefaults.MaxOceanFishingPreWindowOffsetMinutes);
                config.Save();
            }
            DrawHelpMarker("Minutes relative to Ocean Fishing registration start. VERMAXION starts from this offset through the full 15-minute registration period; the closing boundary is excluded.");

            var logoutBetweenVoyages = config.LogoutBetweenScheduledOceanFishingVoyages;
            if (ImGui.Checkbox(
                    "Log out between scheduled Ocean Fishing voyages",
                    ref logoutBetweenVoyages))
            {
                config.LogoutBetweenScheduledOceanFishingVoyages = logoutBetweenVoyages;
                config.Save();
            }
            DrawHelpMarker(
                "After a successful automatically scheduled voyage finishes result handling and optional cleanup, override Return after Fishing, restore external state, then log out and wake at the snapshotted Ocean Fishing startup gate. Disabling this checkbox prevents future holds but does not cancel a hold already underway; use Main Window FULL STOP to cancel it. The game client and Dalamud must remain open at character select; VERMAXION cannot wake a closed client.");
            ImGui.TextWrapped(
                "Scheduled logout overrides every Return after Fishing destination. Manual and test voyages, and scheduled voyages while this setting is disabled, still use the configured return.");
            ImGui.TextDisabled($"Offline hold: {plugin.ScheduledOfflineHoldCoordinator.StatusText}");
            if (config.ScheduledOfflineHold is { } hold)
            {
                ImGui.TextWrapped(
                    $"Phase: {hold.Phase} · wake {hold.WakeAtUtc:u} · " +
                    $"registration {hold.NextRegistrationStartUtc:u}–{hold.NextRegistrationEndUtc:u}");
            }

            if (ImGui.SmallButton("Reset Fishing startup gate"))
                plugin.ResetFishingStartupGate();
            DrawHelpMarker("Clears the current Ocean Fishing startup-window attempt guard so an explicit Fishing run can retry.");

            DrawFishingStockCatalogEditor();
            }
        }
        ImGui.EndChild();
    }

    private void DrawSettingsTab()
    {
        var configManager = plugin.ConfigManager;
        var config = plugin.Configuration;

        var sectionLabels = new[]
        {
            UIConstants.ConfigLabels.EveryARPostProcess,
            UIConstants.ConfigLabels.WeeklyTasks,
            UIConstants.ConfigLabels.DailyTasks,
            UIConstants.ConfigLabels.VariableTimeTasks,
            UIConstants.ConfigLabels.WipTasks,
        };
        var leftWidth = Math.Clamp(config.LeftPanelWidth * UIConstants.Scale, 180f * UIConstants.Scale,
            Math.Max(180f * UIConstants.Scale, ImGui.GetContentRegionAvail().X * 0.35f));
        if (ImGui.BeginChild("SettingsNavigation", new Vector2(leftWidth, 0), true))
        {
            var characterHeight = Math.Max(100f * UIConstants.Scale, ImGui.GetContentRegionAvail().Y * 0.42f);
            if (ImGui.BeginChild("CharacterNavigation", new Vector2(0, characterHeight), false))
                DrawCharacterList(configManager);
            ImGui.EndChild();
            ImGui.Separator();
            UIConstants.Heading("Task settings", config.CompactUi);
            ImGui.SetNextItemWidth(-1f);
            ImGui.InputTextWithHint("##TaskSearch", "Search tasks...", ref taskSearch, 128);
            var sectionIndex = (int)selectedConfigurationSection;
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.Combo("##TaskGroup", ref sectionIndex, sectionLabels, sectionLabels.Length))
            {
                selectedConfigurationSection = (ConfigurationSection)sectionIndex;
                selectedAutomationId = AutomationCatalog.Features.First(feature =>
                    IsTaskNavigationEntry(feature) && GetSettingsSection(feature.Id) == selectedConfigurationSection).Id;
                scrollConfigurationToTop = true;
            }
            if (ImGui.BeginChild("TaskNavigation", new Vector2(0, 0), false))
            {
                var matches = AutomationCatalog.Features.Where(feature => IsTaskNavigationEntry(feature) &&
                    (string.IsNullOrWhiteSpace(taskSearch)
                        ? GetSettingsSection(feature.Id) == selectedConfigurationSection
                        : feature.Label.Contains(taskSearch.Trim(), StringComparison.OrdinalIgnoreCase) ||
                          feature.Id.Contains(taskSearch.Trim(), StringComparison.OrdinalIgnoreCase))).ToList();
                foreach (var feature in matches)
                {
                    var rowPosition = ImGui.GetCursorPos();
                    var rowWidth = Math.Max(1f, ImGui.GetContentRegionAvail().X);
                    var rowHeight = ImGui.CalcTextSize(feature.Label, false, rowWidth).Y;
                    if (ImGui.Selectable($"##{feature.Id}", selectedAutomationId == feature.Id,
                            ImGuiSelectableFlags.None, new Vector2(rowWidth, rowHeight)))
                    {
                        selectedAutomationId = feature.Id;
                        selectedConfigurationSection = GetSettingsSection(feature.Id);
                        scrollConfigurationToTop = true;
                    }
                    var nextRowPosition = ImGui.GetCursorPos();
                    ImGui.SetCursorPos(rowPosition);
                    ImGui.TextWrapped(feature.Label);
                    ImGui.SetCursorPos(nextRowPosition);
                }
                if (matches.Count == 0)
                    ImGui.TextWrapped("No tasks match this search.");
            }
            ImGui.EndChild();
        }
        ImGui.EndChild();
        ImGui.SameLine();
        if (ImGui.BeginChild("SelectedTaskPanel", new Vector2(0, 0), true))
        {
            UIConstants.Heading(AutomationCatalog.Get(selectedAutomationId).Label, config.CompactUi);
            ImGui.TextWrapped("Settings for the account and profile shown above.");
            ImGui.Separator();
            if (ImGui.BeginChild($"TaskSettings_{selectedAutomationId}", new Vector2(0, 0), false, ImGuiWindowFlags.HorizontalScrollbar))
            {
                if (scrollConfigurationToTop)
                {
                    ImGui.SetScrollY(0f);
                    scrollConfigurationToTop = false;
                }
                if (configManager.GetCurrentAccount() == null)
                    ImGui.TextWrapped("Select an account to edit its settings.");
                else
                    DrawCharacterSettings(configManager);
            }
            ImGui.EndChild();
        }
        ImGui.EndChild();
    }

    private void DrawAboutTab()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";
        ImGui.Text($"Vermaxion v{version}");
        ImGui.TextDisabled("Automates weekly and daily tasks triggered by AutoRetainer post-processing.");
        ImGui.Spacing();
        ImGui.Separator();

        ImGui.Text("Dependencies");
        ImGui.Spacing();

        ImGui.TextDisabled("Overall:");
        ImGui.BulletText("AutoRetainer - Triggers post-processing via IPC");
        ImGui.BulletText("YesAlready - Paused during operations to prevent interference");
        ImGui.BulletText("TextAdvance - Enables automatic text progression during dialogue");
        ImGui.BulletText("mom - Private CC runner/rank reader for nag your mom");
        ImGui.BulletText("dad - Private orchestrator for nag your dad");
        ImGui.Spacing();

        ImGui.TextDisabled("Mini Cactpot:");
        ImGui.BulletText("Saucy - Handles Mini Cactpot solving (/saucy -> Other Games -> Enable Auto Mini-Cactpot)");
        ImGui.BulletText("Lifestream - /li saucer (travel to the Gold Saucer main aetheryte)");
        ImGui.BulletText("vnavmesh - Navigation to Cactpot Board");
        ImGui.Spacing();

        ImGui.TextDisabled("Jumbo Cactpot:");
        ImGui.BulletText("Lifestream - /li Cactpot (navigate to Jumbo Cactpot area)");
        ImGui.BulletText("vnavmesh - Navigation to Broker/Cashier NPCs");
        ImGui.Spacing();

        ImGui.TextDisabled("Fishing:");
        ImGui.BulletText("XA Database - Fisher levels for current-account character selection");
        ImGui.BulletText("AutoRetainer - Character relog command handoff");
        ImGui.BulletText("Lifestream - Return/restock travel commands");
        ImGui.BulletText("AutoHook - Hook and reel behavior after Vermaxion casts");
        ImGui.BulletText("ADS - Repair when Fishing repair mode is enabled");
        ImGui.Spacing();

        ImGui.TextDisabled("Chocobo Racing:");
        ImGui.BulletText("VERMAXION - Handles observable one-race queue and completion loop");
        ImGui.BulletText("Choke-abo - Owns Target Pedigree planning, feeding, and breeding handoffs when that mode is selected");
        ImGui.Spacing();

        ImGui.TextDisabled("dad / Astrope:");
        ImGui.BulletText("dad - Receives one combined task payload and owns orchestration, readiness, claims, and routing");
        ImGui.BulletText("DadLanPartyModule - Internal Dad module lane for premade duty and Daily MSQ routing");
        ImGui.BulletText("DadAuraFarmerModule - Internal Dad module lane for commendation and Astrope routing");
        ImGui.Spacing();

        ImGui.TextDisabled("Lord of Verminion:");
        ImGui.BulletText("(Self-contained) - Duty queue via ContentsFinder");
        ImGui.Spacing();

        ImGui.TextDisabled("FC Buff Refill:");
        ImGui.BulletText("(Self-contained) - Checks Seal Sweetener status, purchases if needed");
        ImGui.Spacing();

        ImGui.TextDisabled("Gear Updater:");
        ImGui.BulletText("(Self-contained) - Cycles gearsets, auto-equips, saves");
        ImGui.Spacing();

        ImGui.TextDisabled("Minion Roulette:");
        ImGui.BulletText("(Self-contained) - /minion command");
        ImGui.Spacing();

        ImGui.TextDisabled("Seasonal Gear Roulette:");
        ImGui.BulletText("(Self-contained) - Random seasonal gear equip from predefined list");
        ImGui.Spacing();

        ImGui.Separator();
        ImGui.Text("Links");
        ImGui.BulletText("GitHub: https://github.com/McVaxius/VERMAXION");
        ImGui.BulletText("Author: DhogGPT");
    }

    private void DrawAccountSelector(ConfigManager configManager)
    {
        var accounts = configManager.Accounts;
        var currentId = configManager.CurrentAccountId;

        ImGui.Text(UIConstants.ConfigLabels.Account);
        ImGui.SameLine();

        ImGui.SetNextItemWidth(Math.Min(320f * UIConstants.Scale, Math.Max(100f, ImGui.GetContentRegionAvail().X - 100f * UIConstants.Scale)));
        if (ImGui.BeginCombo("##AccountCombo", GetAccountDisplayName(configManager, currentId)))
        {
            foreach (var kvp in accounts)
            {
                var isSelected = kvp.Key == currentId;
                if (ImGui.Selectable(GetAccountDisplayName(configManager, kvp.Key), isSelected))
                {
                    configManager.CurrentAccountId = kvp.Key;
                    configManager.SelectedCharacterKey = "";
                    plugin.Configuration.LastAccountId = kvp.Key;
                    plugin.Configuration.Save();
                }
                if (isSelected)
                    ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        // Rename button
        var account = configManager.GetCurrentAccount();
        if (account != null)
        {
            ImGui.SameLine();
            if (ImGui.Button("Rename##EditAccount"))
            {
                editAccountAlias = account.AccountAlias;
                ImGui.OpenPopup("EditAccountPopup");
            }

            if (ImGui.BeginPopup("EditAccountPopup"))
            {
                ImGui.Text(UIConstants.ConfigLabels.AccountAlias);
                ImGui.InputText("##EditAlias", ref editAccountAlias, 64);
                if (ImGui.Button(UIConstants.ConfigLabels.Save) && !string.IsNullOrWhiteSpace(editAccountAlias))
                {
                    configManager.UpdateAccountAlias(editAccountAlias);
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }
        }
    }

    private void DrawConfigurationScopeBanner(ConfigManager configManager)
    {
        var account = configManager.GetCurrentAccount();
        var charKey = configManager.SelectedCharacterKey;
        var isDefault = string.IsNullOrEmpty(charKey);
        var accountLabel = account == null
            ? "No account selected"
            : string.IsNullOrWhiteSpace(account.AccountAlias)
                ? "Unnamed account"
                : account.AccountAlias;
        var editingLabel = isDefault ? "Account default" : "Character";
        ImGui.Text($"Editing: {editingLabel}");
        ImGui.SameLine();
        ImGui.TextDisabled($"Account: {accountLabel}");
        ImGui.TextWrapped($"Selected: {(isDefault ? "Default Config" : charKey)} · Runtime character: {(string.IsNullOrWhiteSpace(configManager.CurrentCharacterKey) ? "Not logged in" : configManager.CurrentCharacterKey)}");

        if (account == null)
            return;

        if (isDefault)
        {
            var differing = account.Characters.Count(pair => !SettingsMatchDefault(account.DefaultConfig, pair.Value));
            ImGui.TextDisabled($"{differing} of {account.Characters.Count} characters differ from the account default settings.");
        }
        else
        {
            var selected = configManager.GetSelectedConfig();
            ImGui.TextDisabled(SettingsMatchDefault(account.DefaultConfig, selected)
                ? "Matches account default"
                : "Differs from account default");
        }
    }

    private static string CleanLuminaText(string text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains('\u0001'))
            return text;
        return text.Split('\u0001')[1] ?? text;
    }

    private void DrawCharacterList(ConfigManager configManager)
    {
        UIConstants.Heading(UIConstants.ConfigLabels.Characters, plugin.Configuration.CompactUi);
        DrawCharacterSortSelector();
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##CharacterSearch", "Search characters...", ref characterSearch, 128);

        ImGui.SetNextItemWidth(-1f);
        if (ImGui.BeginCombo("##CharacterFilter", characterFilter?.Label ?? "All characters"))
        {
            if (ImGui.Selectable("All characters", characterFilter == null))
                characterFilter = null;
            if (characterFilter == null)
                ImGui.SetItemDefaultFocus();

            foreach (var feature in AutomationCatalog.Features)
            {
                var selected = characterFilter == feature;
                if (ImGui.Selectable(feature.Label, selected))
                    characterFilter = feature;
                if (selected)
                    ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Show characters with the selected setting enabled.");
        ImGui.Separator();

        // Default config entry
        var isDefaultSelected = string.IsNullOrEmpty(configManager.SelectedCharacterKey);
        if (ImGui.Selectable("(Default Config)", isDefaultSelected))
        {
            configManager.SelectedCharacterKey = "";
        }

        // Current character (if exists and not default)
        var charName = Plugin.ObjectTable.LocalPlayer?.Name.ToString() ?? "";
        var worldName = Plugin.ObjectTable.LocalPlayer?.HomeWorld.Value.Name.ToString() ?? "";
        var currentChar = !string.IsNullOrEmpty(charName) && !string.IsNullOrEmpty(worldName)
            ? $"{charName}@{worldName}"
            : "";

        var filterProperty = characterFilter == null
            ? null
            : typeof(CharacterConfig).GetProperty(characterFilter.FlagProperty);
        foreach (var charKey in configManager.GetSortedCharacterKeys(plugin.Configuration.CharacterListSortMode))
        {
            if (!string.IsNullOrWhiteSpace(characterSearch) && !charKey.Contains(characterSearch.Trim(), StringComparison.OrdinalIgnoreCase))
                continue;
            if (characterFilter != null && filterProperty?.GetValue(configManager.GetConfigForKey(charKey)) is not true)
                continue;

            var displayName = plugin.Configuration.KrangleEnabled
                ? KrangleService.KrangleName(CleanLuminaText(charKey))
                : charKey;

            var isSelected = configManager.SelectedCharacterKey == charKey;
            var isCurrentCharacter = string.Equals(charKey, currentChar, StringComparison.Ordinal);
            if (isCurrentCharacter)
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.4f, 1f, 0.4f, 1));

            if (ImGui.Selectable(displayName, isSelected))
            {
                configManager.SelectedCharacterKey = charKey;
            }

            if (isCurrentCharacter)
                ImGui.PopStyleColor();

            // Right-click context menu
            if (ImGui.BeginPopupContextItem($"CharContext_{charKey}"))
            {
                if (ImGui.MenuItem("Reset to Default"))
                {
                    var accountLabel = configManager.GetCurrentAccount()?.AccountAlias ?? "current account";
                    RequestConfirmation(
                        "Reset character settings?",
                        $"Replace all synchronized settings for {displayName} in {accountLabel} with the current Account default? Character completion history is reset only where the existing reset operation already does so.",
                        () => RunConfigMutationWithTargetPause(
                            () => configManager.ResetCharacterToDefault(charKey),
                            "current character reset to account default"),
                        () => GetPvpEnableWarning(configManager.GetCurrentAccount()?.DefaultConfig,
                            new[] { configManager.GetConfigForKey(charKey) }, ConfigManager.CopyDefaultSettings));
                }
                if (ImGui.MenuItem("Delete"))
                {
                    var accountLabel = configManager.GetCurrentAccount()?.AccountAlias ?? "current account";
                    RequestConfirmation(
                        "Delete character configuration?",
                        $"Delete the saved configuration for {displayName} from {accountLabel}? The character can be recreated from the Account default when seen again.",
                        () => configManager.DeleteCharacter(charKey));
                }
                ImGui.EndPopup();
            }
        }
    }

    private void DrawCharacterSortSelector()
    {
        var sortMode = plugin.Configuration.CharacterListSortMode;
        ImGui.SetNextItemWidth(120f);
        if (ImGui.BeginCombo("##CharacterSortMode", FormatCharacterListSortMode(sortMode)))
        {
            foreach (var mode in Enum.GetValues<CharacterListSortMode>())
            {
                var selected = mode == sortMode;
                if (ImGui.Selectable(FormatCharacterListSortMode(mode), selected))
                {
                    plugin.Configuration.CharacterListSortMode = mode;
                    plugin.Configuration.Save();
                }

                if (selected)
                    ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Character list sort order");
    }

    private void DrawCharacterSettings(ConfigManager configManager)
    {
        var charKey = configManager.SelectedCharacterKey;
        var cc = configManager.GetSelectedConfig();
        var isDefault = string.IsNullOrEmpty(charKey);

        if (isDefault)
        {
            ImGui.TextDisabled(UIConstants.ConfigLabels.NewCharactersInheritThese);
            if (ImGui.SmallButton("Apply Default to ALL"))
            {
                var applied = RunConfigMutationWithTargetPause(
                    configManager.ApplyDefaultToAllCharacters,
                    "account default applied to all characters");
                Plugin.ChatGui.Print($"[Vermaxion] Default Config applied to {applied} characters.");
            }
            ImGui.SameLine();
            ImGui.TextDisabled("Explicitly replaces each existing character's synchronized settings.");
        }
        else if (!string.Equals(charKey, configManager.CurrentCharacterKey, StringComparison.Ordinal))
            ImGui.TextDisabled($"Runtime character is {configManager.CurrentCharacterKey}");
        ImGui.Spacing();

        var changed = false;

        // Master enable
        var enabled = cc.Enabled;
        if (ImGui.Checkbox($"{UIConstants.ConfigLabels.Enabled}##CharEnabled", ref enabled))
        {
            if (!enabled &&
                string.Equals(charKey, configManager.CurrentCharacterKey, StringComparison.Ordinal) &&
                cc.ChocoboAutomationMode == ChocoboAutomationMode.TargetPedigree)
            {
                plugin.PauseCurrentTargetCycleBestEffort("current-character automation disabled");
            }
            cc.Enabled = enabled;
            changed = true;
        }
        DrawDefaultOverrideButton(isDefault, configManager, "CharEnabled", UIConstants.ConfigLabels.Enabled,
            (source, target) => target.Enabled = source.Enabled);

        ImGui.Spacing();

        // --- Feature Toggles ---
        if (BeginConfigurationSection(UIConstants.ConfigLabels.EveryARPostProcess, ConfigurationSection.EveryAr))
        {
            if (selectedAutomationId == AutomationCatalog.MiscCommands)
            {
                var miscCmd = cc.EnableMiscCmd;
                if (ImGui.Checkbox(UIConstants.ConfigLabels.MiscCmd, ref miscCmd))
                {
                    cc.EnableMiscCmd = miscCmd;
                    changed = true;
                }
                DrawDefaultOverrideButton(isDefault, configManager, "MiscCmd", UIConstants.ConfigLabels.MiscCmd,
                    (source, target) => target.EnableMiscCmd = source.EnableMiscCmd);
                ImGui.SameLine();
                if (ImGui.SmallButton("Send now##MiscCmdConfig"))
                {
                    plugin.Engine.SendRunShutdownCommandBundle();
                }
                ImGui.SameLine();
                ImGui.TextDisabled("(?)");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(UIConstants.Tooltips.MiscCmd);
                ImGui.TextWrapped("Commands: /dduck stop (FULL STOP: stops Deep Duck automation), /rotation Cancel, /at enable, /vbmai off, /bmrai off, /wrath auto off, /vnavmesh stop, /visland stop, /ad stop, /sice stop, /ochillegal off, /fr off, /rotation Settings StartOnCountdown False");
                ImGui.TextWrapped("Misc Cmd sends once at the start of every enabled AutoRetainer/manual VERMAXION run.");
            }

            if (selectedAutomationId == AutomationCatalog.FCBuffRefill)
            {
                var fcBuff = cc.EnableFCBuffRefill;
                if (ImGui.Checkbox(UIConstants.ConfigLabels.FCBuffRefill, ref fcBuff))
                {
                    cc.EnableFCBuffRefill = fcBuff;
                    changed = true;
                }
                DrawDefaultOverrideButton(isDefault, configManager, "FCBuffRefill", UIConstants.ConfigLabels.FCBuffRefill,
                    (source, target) => target.EnableFCBuffRefill = source.EnableFCBuffRefill);
                if (fcBuff)
                {
                    ImGui.Indent();
                    var allowActivation = cc.AllowFCBuffActivation;
                    if (ImGui.Checkbox(UIConstants.ConfigLabels.AllowFCBuffActivation, ref allowActivation))
                    {
                        cc.AllowFCBuffActivation = allowActivation;
                        changed = true;
                    }
                    DrawDefaultOverrideButton(isDefault, configManager, "AllowFCBuffActivation", UIConstants.ConfigLabels.AllowFCBuffActivation,
                        (source, target) => target.AllowFCBuffActivation = source.AllowFCBuffActivation);
                    ImGui.TextWrapped("Purchasing and live stock reconciliation remain enabled when activation is off.");

                    var maintainStockTarget = cc.MaintainFCBuffStockTarget;
                    if (ImGui.Checkbox(UIConstants.ConfigLabels.MaintainFCBuffStockTarget, ref maintainStockTarget))
                    {
                        cc.MaintainFCBuffStockTarget = maintainStockTarget;
                        changed = true;
                    }
                    DrawDefaultOverrideButton(isDefault, configManager, "MaintainFCBuffStockTarget", UIConstants.ConfigLabels.MaintainFCBuffStockTarget,
                        (source, target) => target.MaintainFCBuffStockTarget = source.MaintainFCBuffStockTarget);
                    ImGui.TextWrapped("Off buys the configured quantity only at zero stock. On reads live stock and buys only the shortfall; a run that activates one also buys one replacement.");

                    ImGui.Text("Frequency:");
                    ImGui.SameLine();
                    var fcBuffFrequency = cc.FCBuffFrequency;
                    if (ImGui.RadioButton("AR##FCBuffEveryAR", fcBuffFrequency == FCBuffFrequency.EveryAR))
                    {
                        cc.FCBuffFrequency = FCBuffFrequency.EveryAR;
                        changed = true;
                    }
                    ImGui.SameLine();
                    if (ImGui.RadioButton("Daily##FCBuffDaily", fcBuffFrequency == FCBuffFrequency.Daily))
                    {
                        cc.FCBuffFrequency = FCBuffFrequency.Daily;
                        changed = true;
                    }
                    ImGui.SameLine();
                    if (ImGui.RadioButton("Weekly##FCBuffWeekly", fcBuffFrequency == FCBuffFrequency.Weekly))
                    {
                        cc.FCBuffFrequency = FCBuffFrequency.Weekly;
                        changed = true;
                    }
                    ImGui.SameLine();
                    if (ImGui.RadioButton("Monthly##FCBuffMonthly", fcBuffFrequency == FCBuffFrequency.Monthly))
                    {
                        cc.FCBuffFrequency = FCBuffFrequency.Monthly;
                        changed = true;
                    }
                    DrawDefaultOverrideButton(isDefault, configManager, "FCBuffFrequency", "FC Buff frequency",
                        (source, target) => target.FCBuffFrequency = source.FCBuffFrequency);
                    DrawFCBuffHint(cc);
                    DrawResetButton("FC Buff cadence", cc.ResetFCBuffState);

                    var attempts = cc.FCBuffPurchaseAttempts;
                    if (ImGui.SliderInt(
                            UIConstants.ConfigLabels.MaxPurchaseAttempts,
                            ref attempts,
                            1,
                            FCBuffRecoveryPolicy.MaxPurchaseAttempts))
                    {
                        cc.FCBuffPurchaseAttempts = attempts;
                        changed = true;
                        // Save immediately on slider change
                        configManager.SaveCurrentAccount();
                    }
                    DrawDefaultOverrideButton(isDefault, configManager, "FCBuffPurchaseAttempts", UIConstants.ConfigLabels.MaxPurchaseAttempts,
                        (source, target) => target.FCBuffPurchaseAttempts = source.FCBuffPurchaseAttempts);

                    // FC Points threshold
                    var minPoints = cc.FCBuffMinPoints;
                    ImGui.SetNextItemWidth(GetCompactNumericInputWidth());
                    if (ImGui.InputInt(UIConstants.ConfigLabels.MinFCPoints, ref minPoints))
                    {
                        cc.FCBuffMinPoints = Math.Max(0, minPoints);
                        changed = true;
                        // Save immediately on input change
                        configManager.SaveCurrentAccount();
                    }
                    DrawDefaultOverrideButton(isDefault, configManager, "FCBuffMinPoints", UIConstants.ConfigLabels.MinFCPoints,
                        (source, target) => target.FCBuffMinPoints = source.FCBuffMinPoints);

                    // Gil threshold
                    var minGil = cc.FCBuffMinGil;
                    ImGui.SetNextItemWidth(GetCompactNumericInputWidth());
                    if (ImGui.InputInt(UIConstants.ConfigLabels.MinGil, ref minGil))
                    {
                        cc.FCBuffMinGil = Math.Max(0, minGil);
                        changed = true;
                        // Save immediately on input change
                        configManager.SaveCurrentAccount();
                    }
                    DrawDefaultOverrideButton(isDefault, configManager, "FCBuffMinGil", UIConstants.ConfigLabels.MinGil,
                        (source, target) => target.FCBuffMinGil = source.FCBuffMinGil);

                    ImGui.Unindent();
                }
            }

            if (selectedAutomationId == AutomationCatalog.MinionRoulette)
            {
                var minionRoulette = cc.EnableMinionRoulette;
                if (ImGui.Checkbox(UIConstants.ConfigLabels.MinionRoulette, ref minionRoulette))
                {
                    cc.EnableMinionRoulette = minionRoulette;
                    changed = true;
                }
                DrawDefaultOverrideButton(isDefault, configManager, "MinionRoulette", UIConstants.ConfigLabels.MinionRoulette,
                    (source, target) => target.EnableMinionRoulette = source.EnableMinionRoulette);
                ImGui.SameLine();
                ImGui.TextDisabled("(?)");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(UIConstants.Tooltips.MinionRoulette);

                // Minion Roulette state display
                if (cc.EnableMinionRoulette)
                {
                    ImGui.Indent();
                    ImGui.Text($"Attempts today: {cc.MinionRouletteAttemptsToday}");
                    if (DrawResetButton("MinionRouletteDaily", cc.ResetMinionRouletteDailyState))
                        changed = true;
                    ImGui.Unindent();
                }
            }

            if (selectedAutomationId == AutomationCatalog.SeasonalGear)
            {
                var seasonalGear = cc.EnableSeasonalGearRoulette;
                if (ImGui.Checkbox(UIConstants.ConfigLabels.SeasonalGearRoulette, ref seasonalGear))
                {
                    cc.EnableSeasonalGearRoulette = seasonalGear;
                    changed = true;
                }
                DrawDefaultOverrideButton(isDefault, configManager, "SeasonalGearRoulette", UIConstants.ConfigLabels.SeasonalGearRoulette,
                    (source, target) => target.EnableSeasonalGearRoulette = source.EnableSeasonalGearRoulette);
                ImGui.SameLine();
                ImGui.TextDisabled("(?)");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(UIConstants.Tooltips.SeasonalGearRoulette);
            }

            if (selectedAutomationId == AutomationCatalog.GearUpdater)
            {
                var gearUpdater = cc.EnableGearUpdater;
                if (ImGui.Checkbox(UIConstants.ConfigLabels.GearUpdater, ref gearUpdater))
                {
                    cc.EnableGearUpdater = gearUpdater;
                    changed = true;
                }
                DrawDefaultOverrideButton(isDefault, configManager, "GearUpdater", UIConstants.ConfigLabels.GearUpdater,
                    (source, target) => target.EnableGearUpdater = source.EnableGearUpdater);
                ImGui.SameLine();
                ImGui.TextDisabled("(?)");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(UIConstants.Tooltips.GearUpdater);

                ImGui.Indent();
                var equipmentAutomationBusy = IsEquipmentAutomationBusy();
                ImGui.BeginDisabled(equipmentAutomationBusy);
                if (ImGui.SmallButton("Bootstrap missing gearsets"))
                    plugin.RunDashboardAction(plugin.GearUpdaterService.StartBootstrap);
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(equipmentAutomationBusy
                        ? "An engine or equipment task is active."
                        : "Persist the current job as an exact restoration anchor, then create exact gearsets for missing unlocked classes/jobs when a compatible main hand is already owned.");
                }
                ImGui.SameLine();
                if (ImGui.SmallButton("Copy Stylist repository URL"))
                {
                    ImGui.SetClipboardText(StylistRepositoryUrl);
                    Plugin.ChatGui.Print("[Vermaxion] Stylist repository URL copied.");
                }
                ImGui.TextDisabled("Stylist is optional. Gear Updater falls back to VERMAXION's native recommended-equipment path when its IPC is unavailable.");
                ImGui.Unindent();
            }

            if (selectedAutomationId == AutomationCatalog.HighestCombatJob)
            {
                var highestCombatJob = cc.EnableHighestCombatJob;
                if (ImGui.Checkbox("Highest Combat Job Selector", ref highestCombatJob))
                {
                    cc.EnableHighestCombatJob = highestCombatJob;
                    changed = true;
                }
                DrawDefaultOverrideButton(isDefault, configManager, "HighestCombatJob", "Highest Combat Job Selector",
                    (source, target) => target.EnableHighestCombatJob = source.EnableHighestCombatJob);
                ImGui.SameLine();
                ImGui.TextDisabled("(?)");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Selects the highest-level combat job (DOW/DOM only) through its saved gearset. Missing gearsets trigger the bounded native bootstrap first.");
            }

            if (selectedAutomationId == AutomationCatalog.CurrentJobEquipment)
            {
                var currentJobEquipment = cc.EnableCurrentJobEquipment;
                if (ImGui.Checkbox("Current Job Equipment Updater", ref currentJobEquipment))
                {
                    cc.EnableCurrentJobEquipment = currentJobEquipment;
                    changed = true;
                }
                DrawDefaultOverrideButton(isDefault, configManager, "CurrentJobEquipment", "Current Job Equipment Updater",
                    (source, target) => target.EnableCurrentJobEquipment = source.EnableCurrentJobEquipment);
                ImGui.SameLine();
                ImGui.TextDisabled("(?)");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Uses the native recommended-equipment module and exact native gearset persistence for the current saved gearset only.");
            }

            if (selectedAutomationId == AutomationCatalog.AfterArPark)
            {
                var afterArPark = cc.EnableAfterArPark;
                if (ImGui.Checkbox("After-AR Park", ref afterArPark))
                {
                    cc.EnableAfterArPark = afterArPark;
                    changed = true;
                }
                DrawDefaultOverrideButton(isDefault, configManager, "AfterArPark", "After-AR Park",
                    (source, target) => target.EnableAfterArPark = source.EnableAfterArPark);
                DrawHelpMarker("Issues one configured /li route, then waits for Lifestream idle and an available player to remain settled. It times out without retrying.");
                if (cc.EnableAfterArPark)
                {
                    ImGui.Indent();
                    var destination = cc.AfterArParkDestination;
                    if (ImGui.BeginCombo("Parking destination", FormatAfterArParkDestination(destination)))
                    {
                        foreach (var option in Enum.GetValues<AfterArParkDestination>())
                        {
                            var selected = option == destination;
                            if (ImGui.Selectable(FormatAfterArParkDestination(option), selected))
                            {
                                cc.AfterArParkDestination = option;
                                changed = true;
                            }
                            if (selected)
                                ImGui.SetItemDefaultFocus();
                        }
                        ImGui.EndCombo();
                    }
                    DrawDefaultOverrideButton(isDefault, configManager, "AfterArParkDestination", "After-AR parking destination",
                        (source, target) => target.AfterArParkDestination = source.AfterArParkDestination);

                    if (cc.AfterArParkDestination == AfterArParkDestination.Custom)
                    {
                        var customCommand = cc.AfterArParkCustomCommand;
                        if (ImGui.InputText("Custom /li command", ref customCommand, 128))
                        {
                            cc.AfterArParkCustomCommand = customCommand;
                            changed = true;
                        }
                        DrawDefaultOverrideButton(isDefault, configManager, "AfterArParkCustomCommand", "After-AR custom command",
                            (source, target) => target.AfterArParkCustomCommand = source.AfterArParkCustomCommand);
                    }

                    if (!AfterArParkService.TryResolveCommand(
                            cc.AfterArParkDestination,
                            cc.AfterArParkCustomCommand,
                            out var parkCommand,
                            out var parkError))
                    {
                        ImGui.TextColored(new Vector4(1f, 0.25f, 0.25f, 1f), parkError);
                    }
                    else
                    {
                        ImGui.TextDisabled($"One-shot route: {parkCommand}");
                    }
                    ImGui.Unindent();
                }
            }

            if (selectedAutomationId == AutomationCatalog.VendorStock)
            {
                var vendorStock = cc.EnableVendorStock;
                if (ImGui.Checkbox("Vendor Stock", ref vendorStock))
                {
                    cc.EnableVendorStock = vendorStock;
                    changed = true;
                }
                DrawDefaultOverrideButton(isDefault, configManager, "VendorStock", "Vendor Stock",
                    (source, target) => target.EnableVendorStock = source.EnableVendorStock);
                ImGui.SameLine();
                ImGui.TextDisabled("(?)");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Restocks configured consumables and Dark Matter after AR post-processing when inventory falls below the target amounts.");
                if (vendorStock)
                {
                    ImGui.Indent();

                    var gysahlTarget = cc.VendorStockGysahlGreensTarget;
                    ImGui.SetNextItemWidth(GetCompactNumericInputWidth());
                    if (ImGui.InputInt("Gysahl Greens target", ref gysahlTarget))
                    {
                        cc.VendorStockGysahlGreensTarget = Math.Max(0, gysahlTarget);
                        changed = true;
                        configManager.SaveCurrentAccount();
                    }
                    DrawDefaultOverrideButton(isDefault, configManager, "VendorStockGysahlGreensTarget", "Gysahl Greens target",
                        (source, target) => target.VendorStockGysahlGreensTarget = source.VendorStockGysahlGreensTarget);

                    var darkMatterTarget = cc.VendorStockGrade8DarkMatterTarget;
                    ImGui.SetNextItemWidth(GetCompactNumericInputWidth());
                    if (ImGui.InputInt("Grade 8 Dark Matter target", ref darkMatterTarget))
                    {
                        cc.VendorStockGrade8DarkMatterTarget = Math.Max(0, darkMatterTarget);
                        changed = true;
                        configManager.SaveCurrentAccount();
                    }
                    DrawDefaultOverrideButton(isDefault, configManager, "VendorStockGrade8DarkMatterTarget", "Grade 8 Dark Matter target",
                        (source, target) => target.VendorStockGrade8DarkMatterTarget = source.VendorStockGrade8DarkMatterTarget);

                    ImGui.TextDisabled("ADS selects the vendor route and buys only the missing stock. Requires ADS shop purchasing.");
                    ImGui.Unindent();
                }
            }

            if (selectedAutomationId == AutomationCatalog.Fishing)
            {
                var fishing = cc.EnableFishing;
                if (ImGui.Checkbox("Fishing", ref fishing))
                {
                    cc.EnableFishing = fishing;
                    changed = true;
                }
                DrawDefaultOverrideButton(isDefault, configManager, "Fishing", "Fishing",
                    (source, target) => target.EnableFishing = source.EnableFishing);
                DrawHelpMarker("Enables Fishing for this character. The global Ocean Fishing provider controls which plugin owns in-duty automation.");
                if (cc.EnableFishing)
                {
                    ImGui.Indent();

                    if (!isDefault)
                    {
                        var routeOverride = cc.OceanFishingRouteOverride;
                        var routeOverrideLabel = routeOverride.HasValue
                            ? OceanFishingRoutePolicy.Normalize(routeOverride.Value).ToString()
                            : "Use global";
                        if (ImGui.BeginCombo("Ocean Fishing route override", routeOverrideLabel))
                        {
                            var useGlobal = routeOverride == null;
                            if (ImGui.Selectable("Use global", useGlobal))
                            {
                                cc.OceanFishingRouteOverride = null;
                                changed = true;
                            }

                            if (useGlobal)
                                ImGui.SetItemDefaultFocus();

                            foreach (var preference in Enum.GetValues<OceanFishingRoutePreference>()
                                         .Where(candidate => candidate != OceanFishingRoutePreference.Thavnair))
                            {
                                var selected = routeOverride.HasValue &&
                                               OceanFishingRoutePolicy.Normalize(routeOverride.Value) == preference;
                                if (ImGui.Selectable(preference.ToString(), selected))
                                {
                                    cc.OceanFishingRouteOverride = preference;
                                    changed = true;
                                }

                                if (selected)
                                    ImGui.SetItemDefaultFocus();
                            }
                            ImGui.EndCombo();
                        }
                        DrawHelpMarker("Use the global route preference or keep an explicit route family for this character.");
                    }

                    var alwaysFish = cc.AlwaysFishOnThisCharacterIfWindowOpen;
                    if (ImGui.Checkbox("Always fish on this character if window open", ref alwaysFish))
                    {
                        cc.AlwaysFishOnThisCharacterIfWindowOpen = alwaysFish;
                        changed = true;
                    }
                    DrawHelpMarker("When a fishing window is already open, prefer this character even if another enabled character has a lower Fisher level.");

                    if (!isDefault && cc.AlwaysFishOnThisCharacterIfWindowOpen)
                    {
                        var account = configManager.GetCurrentAccount();
                        var duplicateAlwaysCount = account?.Characters.Count(pair =>
                            pair.Value.EnableFishing &&
                            pair.Value.AlwaysFishOnThisCharacterIfWindowOpen &&
                            !string.Equals(pair.Key, charKey, StringComparison.OrdinalIgnoreCase)) ?? 0;
                        if (duplicateAlwaysCount > 0)
                        {
                            ImGui.SameLine();
                            if (ImGui.SmallButton("Disable on other characters"))
                            {
                                var cleared = configManager.DisableAlwaysFishOnOtherCharacters(charKey);
                                Plugin.ChatGui.Print($"[Vermaxion] Disabled always-fish on {cleared} other character(s).");
                            }
                            if (ImGui.IsItemHovered())
                                ImGui.SetTooltip("Clears the active-window fishing override from every other character on this account.");
                        }
                    }

                    ImGui.Text("Fishing stock for this config");
                    foreach (var row in plugin.Configuration.FishingStockCatalog)
                    {
                        if (!cc.FishingStockItems.TryGetValue(row.ItemId, out var stock))
                        {
                            stock = new FishingStockSetting
                            {
                                Enabled = row.DefaultEnabled,
                                Target = row.DefaultTarget,
                                Min = row.DefaultMin,
                            };
                            cc.FishingStockItems[row.ItemId] = stock;
                        }

                        ImGui.PushID($"CharacterFishingStock_{row.ItemId}");
                        var enabledStock = stock.Enabled;
                        if (ImGui.Checkbox("##Enabled", ref enabledStock))
                        {
                            stock.Enabled = enabledStock;
                            changed = true;
                        }
                        ImGui.SameLine();
                        ImGui.Text(GetItemName(row.ItemId));
                        ImGui.SameLine(260f);
                        ImGui.SetNextItemWidth(GetCompactNumericInputWidth());
                        var stockTarget = stock.Target;
                        if (ImGui.InputInt("##Target", ref stockTarget))
                        {
                            stock.Target = Math.Max(0, stockTarget);
                            changed = true;
                        }
                        ImGui.SameLine();
                        ImGui.TextUnformatted("min");
                        ImGui.SameLine();
                        ImGui.SetNextItemWidth(GetCompactNumericInputWidth());
                        var stockMin = stock.Min;
                        if (ImGui.InputInt("##Min", ref stockMin))
                        {
                            stock.Min = Math.Max(0, stockMin);
                            changed = true;
                        }
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("Reorder point. 0 = buy whenever below target (default). Above 0 = only buy back up to target once inventory drops to this or lower.");
                        DrawDefaultOverrideButton(
                            isDefault,
                            configManager,
                            $"FishingStock_{row.ItemId}",
                            $"{GetItemName(row.ItemId)} fishing stock",
                            (source, target) =>
                            {
                                if (source.FishingStockItems.TryGetValue(row.ItemId, out var sourceStock))
                                    target.FishingStockItems[row.ItemId] = sourceStock.Clone();
                                else
                                    target.FishingStockItems.Remove(row.ItemId);
                            });
                        ImGui.PopID();
                    }
                    DrawHelpMarker("Enabled rows are processed in catalog order. ADS is asked for the exact missing quantity. Optional bait failures are reported; fishing only blocks when Versatile Lure reaches zero.");

                    var returnDestination = cc.FishingReturnDestination;
                    if (ImGui.BeginCombo("Return destination", FormatFishingReturnDestination(returnDestination)))
                    {
                        foreach (var destination in Enum.GetValues<FishingReturnDestination>())
                        {
                            var selected = destination == returnDestination;
                            if (ImGui.Selectable(FormatFishingReturnDestination(destination), selected))
                            {
                                cc.FishingReturnDestination = destination;
                                if (destination != FishingReturnDestination.Custom)
                                    cc.FishingReturnCommand = GetDefaultFishingReturnCommand(destination);
                                changed = true;
                            }

                            if (selected)
                                ImGui.SetItemDefaultFocus();
                        }
                        ImGui.EndCombo();
                    }
                    DrawDefaultOverrideButton(isDefault, configManager, "FishingReturnDestination", "Fishing return destination",
                        (source, target) => target.FishingReturnDestination = source.FishingReturnDestination);
                    DrawHelpMarker("Where this character should go after the fishing duty or window ends. An eligible scheduled logout overrides this return.");

                    var returnCommand = cc.FishingReturnCommand;
                    if (ImGui.InputText("Return slash command", ref returnCommand, 128))
                    {
                        cc.FishingReturnCommand = returnCommand;
                        changed = true;
                    }
                    DrawDefaultOverrideButton(isDefault, configManager, "FishingReturnCommand", "Fishing return command",
                        (source, target) => target.FishingReturnCommand = source.FishingReturnCommand);
                    DrawHelpMarker("Slash command sent for the selected return destination. Custom destinations require an explicit command.");

                    var repairMode = cc.FishingRepairMode;
                    if (ImGui.BeginCombo("Fishing repair mode", FormatFishingRepairMode(repairMode)))
                    {
                        foreach (var mode in Enum.GetValues<FishingRepairMode>())
                        {
                            var selected = mode == repairMode;
                            if (ImGui.Selectable(FormatFishingRepairMode(mode), selected))
                            {
                                cc.FishingRepairMode = mode;
                                changed = true;
                            }

                            if (selected)
                                ImGui.SetItemDefaultFocus();
                        }
                        ImGui.EndCombo();
                    }
                    DrawDefaultOverrideButton(isDefault, configManager, "FishingRepairMode", "Fishing repair mode",
                        (source, target) => target.FishingRepairMode = source.FishingRepairMode);
                    DrawHelpMarker("ADS repair mode for this character before fishing starts. Disabled skips gear repair.");

                    var repairThreshold = cc.FishingRepairThresholdPercent;
                    ImGui.SetNextItemWidth(GetCompactNumericInputWidth());
                    if (ImGui.InputInt("Fishing repair threshold %", ref repairThreshold))
                    {
                        cc.FishingRepairThresholdPercent = Math.Clamp(repairThreshold, 0, 100);
                        changed = true;
                    }
                    DrawDefaultOverrideButton(isDefault, configManager, "FishingRepairThresholdPercent", "Fishing repair threshold",
                        (source, target) => target.FishingRepairThresholdPercent = source.FishingRepairThresholdPercent);
                    DrawHelpMarker("Repairs when this character's lowest equipped gear condition is at or below this percent.");

                    var discardAfterVoyage = cc.FishingDiscardAfterVoyage;
                    if (ImGui.Checkbox("Discard configured fish after voyage", ref discardAfterVoyage))
                    {
                        cc.FishingDiscardAfterVoyage = discardAfterVoyage;
                        changed = true;
                    }
                    DrawDefaultOverrideButton(isDefault, configManager, "FishingDiscardAfterVoyage", "Fishing discard cleanup",
                        (source, target) => target.FishingDiscardAfterVoyage = source.FishingDiscardAfterVoyage);
                    DrawHelpMarker("After voyage results settle, waits for AutoRetainer to be readable and idle, then runs /ays discard.");

                    var sellAfterVoyage = cc.FishingSellAfterVoyage;
                    if (ImGui.Checkbox("Sell configured fish after voyage", ref sellAfterVoyage))
                    {
                        cc.FishingSellAfterVoyage = sellAfterVoyage;
                        changed = true;
                    }
                    DrawDefaultOverrideButton(isDefault, configManager, "FishingSellAfterVoyage", "Fishing sell cleanup",
                        (source, target) => target.FishingSellAfterVoyage = source.FishingSellAfterVoyage);
                    DrawHelpMarker("After discard cleanup, asks ADS to handle vendor travel and selling with AutoRetainer's configured sell list. Cleanup warnings do not prevent the configured return.");

                    var eatAnyFood = cc.FishingEatAnyFood;
                    if (ImGui.Checkbox("Eat any food in bags (pre-fishing lobby)", ref eatAnyFood))
                    {
                        cc.FishingEatAnyFood = eatAnyFood;
                        changed = true;
                    }
                    DrawDefaultOverrideButton(isDefault, configManager, "FishingEatAnyFood", "Eat any fishing food",
                        (source, target) => target.FishingEatAnyFood = source.FishingEatAnyFood);
                    DrawHelpMarker("Eats food in the pre-fishing lobby so Well-Fed covers the voyage (no fishing time lost; only eats while stationary, so it never fights rail placement). ON = scan the bags and eat whatever food is there, preferring GP food. Set a specific item id below to override the scan. Both off = no food.");

                    var fishingFoodItemId = (int)cc.FishingFoodItemId;
                    ImGui.SetNextItemWidth(GetCompactNumericInputWidth());
                    if (ImGui.InputInt("Specific food item id (0 = auto)", ref fishingFoodItemId))
                    {
                        cc.FishingFoodItemId = (uint)Math.Max(0, fishingFoodItemId);
                        changed = true;
                    }
                    DrawDefaultOverrideButton(isDefault, configManager, "FishingFoodItemId", "Fishing food item id",
                        (source, target) => target.FishingFoodItemId = source.FishingFoodItemId);
                    DrawHelpMarker("Optional override: eat exactly this item id (NQ+HQ both count; must be in inventory). 0 = let 'Eat any food' pick.");

                    ImGui.TextDisabled("Requires XADB, AutoRetainer, Lifestream, AutoHook, vnavmesh, YesAlready, and ADS. ADS handles vendor purchases, selling, and repair.");
                    ImGui.Unindent();
                }
            }

            if (selectedAutomationId == AutomationCatalog.RetainerEquipping)
            {
                var retainerEquipping = cc.EnableRetainerEquipping;
                if (ImGui.Checkbox("Retainer Equipping", ref retainerEquipping))
                {
                    cc.EnableRetainerEquipping = retainerEquipping;
                    changed = true;
                }
                DrawDefaultOverrideButton(isDefault, configManager, "RetainerEquipping", "Retainer Equipping",
                    (source, target) => target.EnableRetainerEquipping = source.EnableRetainerEquipping);
                DrawHelpMarker("Upgrades only AutoRetainer-enabled retainers. Combat uses AutoRetainer-compatible average item level; gatherers use total Perception only.");
                if (cc.EnableRetainerEquipping)
                {
                    ImGui.Indent();
                    var sourceMode = cc.RetainerGearSourceMode;
                    if (ImGui.BeginCombo("Gear source", FormatRetainerGearSourceMode(sourceMode)))
                    {
                        foreach (var mode in Enum.GetValues<RetainerGearSourceMode>())
                        {
                            var selected = mode == sourceMode;
                            if (ImGui.Selectable(FormatRetainerGearSourceMode(mode), selected))
                            {
                                cc.RetainerGearSourceMode = mode;
                                changed = true;
                            }
                            if (selected)
                                ImGui.SetItemDefaultFocus();
                        }
                        ImGui.EndCombo();
                    }
                    DrawDefaultOverrideButton(isDefault, configManager, "RetainerGearSourceMode", "Retainer gear source",
                        (source, target) => target.RetainerGearSourceMode = source.RetainerGearSourceMode);

                    var nonUniqueOnly = cc.RetainerGearNonUniqueOnly;
                    if (ImGui.Checkbox("Use non-unique items only", ref nonUniqueOnly))
                    {
                        cc.RetainerGearNonUniqueOnly = nonUniqueOnly;
                        changed = true;
                    }
                    DrawDefaultOverrideButton(isDefault, configManager, "RetainerGearNonUniqueOnly", "Retainer non-unique filter",
                        (source, target) => target.RetainerGearNonUniqueOnly = source.RetainerGearNonUniqueOnly);

                    var combatTarget = cc.RetainerCombatItemLevelTarget;
                    ImGui.SetNextItemWidth(GetCompactNumericInputWidth());
                    if (ImGui.InputInt("Combat item-level target", ref combatTarget))
                    {
                        cc.RetainerCombatItemLevelTarget = Math.Max(0, combatTarget);
                        changed = true;
                    }
                    DrawDefaultOverrideButton(isDefault, configManager, "RetainerCombatItemLevelTarget", "Retainer combat item-level target",
                        (source, target) => target.RetainerCombatItemLevelTarget = source.RetainerCombatItemLevelTarget);

                    var perceptionTarget = cc.RetainerGatheringPerceptionTarget;
                    ImGui.SetNextItemWidth(GetCompactNumericInputWidth());
                    if (ImGui.InputInt("Gathering Perception target", ref perceptionTarget))
                    {
                        cc.RetainerGatheringPerceptionTarget = Math.Max(0, perceptionTarget);
                        changed = true;
                    }
                    DrawDefaultOverrideButton(isDefault, configManager, "RetainerGatheringPerceptionTarget", "Retainer gathering Perception target",
                        (source, target) => target.RetainerGatheringPerceptionTarget = source.RetainerGatheringPerceptionTarget);
                    ImGui.TextWrapped("Player-equipped gear is never used. Ignore Gearset excludes saved-gearset items; All Gear intentionally bypasses that membership filter. Venture reassignment is temporarily suppressed and the prior AutoRetainer collect-only state is restored on every exit path.");
                    ImGui.Unindent();
                }
            }

        }

        if (BeginConfigurationSection(UIConstants.ConfigLabels.WeeklyTasks, ConfigurationSection.Weekly))
        {
            if (selectedAutomationId == AutomationCatalog.VerminionQueue)
            {
                var verminion = cc.EnableVerminionQueue;
                if (ImGui.Checkbox(UIConstants.ConfigLabels.VerminionQueue, ref verminion))
                {
                    cc.EnableVerminionQueue = verminion;
                    changed = true;
                }
                DrawDefaultOverrideButton(isDefault, configManager, "VerminionQueue", UIConstants.ConfigLabels.VerminionQueue,
                    (source, target) => target.CopyVerminionSettingsFrom(source));
                if (DrawResetButton("VerminionState", cc.ResetVerminionState))
                    changed = true;
                if (VerminionService.WeeklyGoalReached(cc))
                {
                    ImGui.SameLine();
                    ImGui.TextColored(new Vector4(1, 1, 0, 1), "[Already Completed]");
                }
                ImGui.TextDisabled(VerminionService.ProgressSummary(cc));
                ImGui.Indent();
                changed |= VerminionWindow.DrawSettings(cc);
                if (ImGui.Button("Open Verminion##Settings")) plugin.VerminionWindow.IsOpen = true;
                ImGui.TextWrapped("The standalone window runs on the current character. These settings belong to the selected configuration above.");
                if (ImGui.CollapsingHeader("Next strategy and required minions##VerminionSettings"))
                    VerminionWindow.DrawPlan(cc, VerminionService.PlannedStage(cc, cc.VerminionProgress.CampaignRequested),
                        !isDefault && charKey == configManager.CurrentCharacterKey && Plugin.PlayerState.IsLoaded);
                ImGui.Unindent();
            }

            if (selectedAutomationId == AutomationCatalog.JumboCactpot)
            {
                var jumbo = cc.EnableJumboCactpot;
                if (ImGui.Checkbox(UIConstants.ConfigLabels.JumboCactpot, ref jumbo))
                {
                    cc.EnableJumboCactpot = jumbo;
                    changed = true;
                }
                DrawDefaultOverrideButton(isDefault, configManager, "JumboCactpot", UIConstants.ConfigLabels.JumboCactpot,
                    (source, target) => target.EnableJumboCactpot = source.EnableJumboCactpot);
                if (DrawResetButton("JumboCactpotState", cc.ResetJumboCactpotState))
                    changed = true;
                if (ResetDetectionService.IsJumboPurchasePendingPayout(cc.JumboCactpotLastCompleted, cc.JumboCactpotNextReset))
                {
                    ImGui.SameLine();
                    ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1), "[Ticket Purchased]");
                }
                else if (ResetDetectionService.TaskIsCompleted(cc.JumboCactpotLastCompleted, cc.JumboCactpotNextReset))
                {
                    ImGui.SameLine();
                    ImGui.TextColored(new Vector4(1, 1, 0, 1), "[Already Completed]");
                }
                DrawJumboTaskHint(cc.JumboCactpotLastCompleted, cc.JumboCactpotNextReset);
                if (cc.EnableJumboCactpot)
                {
                    ImGui.Indent();

                    var numberMode = cc.JumboCactpotNumberMode;
                    if (ImGui.BeginCombo("Jumbo number mode", FormatJumboNumberMode(numberMode)))
                    {
                        foreach (var mode in Enum.GetValues<JumboCactpotNumberMode>())
                        {
                            var selected = mode == numberMode;
                            if (ImGui.Selectable(FormatJumboNumberMode(mode), selected))
                            {
                                cc.JumboCactpotNumberMode = mode;
                                changed = true;
                            }

                            if (selected)
                                ImGui.SetItemDefaultFocus();
                        }

                        ImGui.EndCombo();
                    }
                    DrawDefaultOverrideButton(isDefault, configManager, "JumboCactpotNumberMode", "Jumbo number mode",
                        (source, target) => target.JumboCactpotNumberMode = source.JumboCactpotNumberMode);

                    if (cc.JumboCactpotNumberMode == JumboCactpotNumberMode.Fixed)
                    {
                        var fixedNumber = cc.JumboCactpotFixedNumber;
                        ImGui.SetNextItemWidth(GetCompactNumericInputWidth() * 1.5f);
                        if (ImGui.InputInt("Fixed 4-digit number", ref fixedNumber))
                        {
                            cc.JumboCactpotFixedNumber = Math.Clamp(fixedNumber, 0, 9999);
                            changed = true;
                        }
                        DrawDefaultOverrideButton(isDefault, configManager, "JumboCactpotFixedNumber", "Fixed 4-digit number",
                            (source, target) => target.JumboCactpotFixedNumber = source.JumboCactpotFixedNumber);

                        ImGui.TextDisabled($"Current fixed number: {cc.JumboCactpotFixedNumber:0000}");
                    }
                    else
                    {
                        ImGui.TextDisabled("Uses a fresh random 4-digit number for each purchase.");
                    }

                    ImGui.Unindent();
                }
            }

            if (selectedAutomationId == AutomationCatalog.FashionReport)
            {
                var fashion = cc.EnableFashionReport;
                if (ImGui.Checkbox("Fashion Report", ref fashion))
                {
                    cc.EnableFashionReport = fashion;
                    changed = true;
                }
                DrawDefaultOverrideButton(isDefault, configManager, "FashionReport", "Fashion Report",
                    (source, target) => target.EnableFashionReport = source.EnableFashionReport);
                if (DrawResetButton("FashionReportState", cc.ResetFashionReportState))
                    changed = true;
                if (ResetDetectionService.TaskIsCompleted(cc.FashionReportLastCompleted, cc.FashionReportNextReset))
                {
                    ImGui.SameLine();
                    ImGui.TextColored(new Vector4(1, 1, 0, 1), "[Already Completed]");
                }
                else
                {
                    ImGui.SameLine();
                    ImGui.TextColored(new Vector4(0.2f, 1.0f, 0.2f, 1.0f), "[OK]");
                }
                DrawFashionTaskHint(cc.FashionReportLastCompleted, cc.FashionReportNextReset);
            }

            if (selectedAutomationId == AutomationCatalog.CustomDeliveries)
            {
                var deliveries = cc.EnableCustomDeliveries;
                if (ImGui.Checkbox("Custom Deliveries", ref deliveries))
                {
                    cc.EnableCustomDeliveries = deliveries;
                    changed = true;
                }
                DrawDefaultOverrideButton(isDefault, configManager, "CustomDeliveries", "Custom Deliveries",
                    (source, target) => target.CopyCustomDeliveriesSettingsFrom(source));
                if (DrawResetButton("CustomDeliveriesState", cc.ResetCustomDeliveriesState))
                    changed = true;
                ImGui.Indent();
                changed |= DrawCustomDeliveriesSettings(cc.CustomDeliveriesSettings);
                ImGui.TextWrapped("Weekly character and NPC allowances come from the game. Reset clears saved completion times; it does not restore spent allowances.");
                if (!isDefault && charKey == configManager.CurrentCharacterKey && Plugin.PlayerState.IsLoaded)
                {
                    if (ImGui.SmallButton("Fetch achievement progress now##CustomDeliveries"))
                        plugin.CustomDeliveriesService.RequestAchievements();
                    MainWindow.DrawCustomDeliveryNpcOverview(plugin, cc.CustomDeliveriesSettings);
                }
                else
                    ImGui.TextDisabled("NPC ranks, job eligibility and achievement progress are shown for the loaded character.");
                ImGui.Unindent();
            }

            if (selectedAutomationId == AutomationCatalog.ChocoboStables)
            {
                changed |= DrawChocoboStablesSettings(cc, !isDefault && charKey == configManager.CurrentCharacterKey);
            }

            if (selectedAutomationId == AutomationCatalog.RegisterRegistrables)
            {
                var register = cc.EnableRegisterRegistrables;
                if (ImGui.Checkbox("Register Registrables", ref register))
                {
                    cc.EnableRegisterRegistrables = register;
                    changed = true;
                }
                DrawDefaultOverrideButton(isDefault, configManager, "RegisterRegistrables", "Register Registrables",
                    (source, target) =>
                    {
                        target.EnableRegisterRegistrables = source.EnableRegisterRegistrables;
                        target.RegisterUnregisteredItemsFromInventory = source.RegisterUnregisteredItemsFromInventory;
                        target.PersonalRegistrableItems = new List<uint>(source.PersonalRegistrableItems);
                    });

                DrawHelpMarker("Controls scheduled runs. Manual Run and the saved debug reload task remain available when unchecked.");
                ImGui.Indent();
                if (ImGui.RadioButton("All unregistered registrables discovered in inventory", cc.RegisterUnregisteredItemsFromInventory))
                {
                    cc.RegisterUnregisteredItemsFromInventory = true;
                    changed = true;
                }
                ImGui.SameLine();
                ImGui.TextDisabled("(?)");
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(
                        "Uses one snapshot of Inventory 1-4 and ignores the personal list for that run.\n" +
                        "Only direct mounts, minions, fashion accessories, facewear, orchestrion rolls,\n" +
                        "emotes/hairstyles, bardings, and Triple Triad cards that are still locked are used.");
                }

                if (ImGui.RadioButton("Specific items from my list", !cc.RegisterUnregisteredItemsFromInventory))
                {
                    cc.RegisterUnregisteredItemsFromInventory = false;
                    changed = true;
                }
                ImGui.SameLine();
                if (ImGui.Button("Configure list##RegistrableConfig"))
                {
                    plugin.RegistrableConfigWindow.IsOpen = true;
                }
                ImGui.Unindent();
            }

        }

        if (BeginConfigurationSection(UIConstants.ConfigLabels.DailyTasks, ConfigurationSection.Daily))
        {
            if (selectedAutomationId == AutomationCatalog.MiniCactpot)
            {
                var mini = cc.EnableMiniCactpot;
                if (ImGui.Checkbox(UIConstants.ConfigLabels.MiniCactpot, ref mini))
                {
                    cc.EnableMiniCactpot = mini;
                    changed = true;
                }
                DrawDefaultOverrideButton(isDefault, configManager, "MiniCactpot", UIConstants.ConfigLabels.MiniCactpot,
                    (source, target) => target.EnableMiniCactpot = source.EnableMiniCactpot);
                ImGui.SameLine();
                ImGui.TextDisabled("(?)");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("To enable: type /saucy, go to \"Other Games\" -> [x] Enable Auto Mini-Cactpot.\nVermaxion will teleport to Gold Saucer, walk to the Cactpot Board, and start the interaction.\nSaucy handles the actual mini-game solving.");
                if (DrawResetButton("MiniCactpotState", cc.ResetMiniCactpotState))
                    changed = true;
                if (ResetDetectionService.TaskIsCompleted(cc.MiniCactpotLastCompleted, cc.MiniCactpotNextReset))
                {
                    ImGui.SameLine();
                    ImGui.TextColored(new Vector4(1, 1, 0, 1), "[Already Completed]");
                }
                DrawDailyTaskHint(cc.MiniCactpotLastCompleted, cc.MiniCactpotNextReset, "Runs once per daily reset. Returns with /li home before the next task.");

                // Mini Cactpot additional options
                if (cc.EnableMiniCactpot)
                {
                    ImGui.Indent();

                    var requireSaucy = cc.RequireSaucyForMiniCactpot;
                    if (ImGui.Checkbox("Require Saucy", ref requireSaucy))
                    {
                        cc.RequireSaucyForMiniCactpot = requireSaucy;
                        changed = true;
                    }
                    DrawDefaultOverrideButton(isDefault, configManager, "RequireSaucyForMiniCactpot", "Require Saucy",
                        (source, target) => target.RequireSaucyForMiniCactpot = source.RequireSaucyForMiniCactpot);
                    ImGui.SameLine();
                    ImGui.TextDisabled("(?)");
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("If enabled, Vermaxion will fail if Saucy is not available.\nIf disabled, Vermaxion will attempt to run without Saucy (may not work properly).");

                    ImGui.Text($"Tickets today: {cc.MiniCactpotTicketsToday}/3");
                    ImGui.Unindent();
                }
            }

            if (selectedAutomationId == AutomationCatalog.ChocoboRacing)
            {
                var chocobo = cc.EnableChocoboRacing;
                if (ImGui.Checkbox(UIConstants.ConfigLabels.ChocoboRacing, ref chocobo))
                {
                    if (!chocobo &&
                        string.Equals(charKey, configManager.CurrentCharacterKey, StringComparison.Ordinal) &&
                        cc.ChocoboAutomationMode == ChocoboAutomationMode.TargetPedigree)
                    {
                        plugin.PauseCurrentTargetCycleBestEffort("Chocobo Racing disabled");
                    }
                    cc.EnableChocoboRacing = chocobo;
                    changed = true;
                }
                DrawDefaultOverrideButton(isDefault, configManager, "ChocoboRacing", UIConstants.ConfigLabels.ChocoboRacing,
                    (source, target) => target.EnableChocoboRacing = source.EnableChocoboRacing);
                if (DrawResetButton("ChocoboRacingState", cc.ResetChocoboRacingState))
                    changed = true;
                if (ResetDetectionService.TaskIsCompleted(cc.ChocoboRacingLastCompleted, cc.ChocoboRacingNextReset))
                {
                    ImGui.SameLine();
                    ImGui.TextColored(new Vector4(1, 1, 0, 1), "[Already Completed]");
                }
                if (cc.ChocoboAutomationMode == ChocoboAutomationMode.AlwaysRace)
                    DrawDailyTaskHint(cc.ChocoboRacingLastCompleted, cc.ChocoboRacingNextReset, "Runs once per daily reset.");
                if (chocobo)
                {
                    ImGui.Indent();

                    var chocoboMode = cc.ChocoboAutomationMode;
                    var modePreview = Enum.IsDefined(chocoboMode)
                        ? chocoboMode == ChocoboAutomationMode.AlwaysRace ? "Always Race" : "Target Pedigree"
                        : $"Invalid ({(int)chocoboMode})";
                    if (ImGui.BeginCombo("Automation mode", modePreview))
                    {
                        foreach (var mode in Enum.GetValues<ChocoboAutomationMode>())
                        {
                            var selected = mode == chocoboMode;
                            var label = mode == ChocoboAutomationMode.AlwaysRace ? "Always Race" : "Target Pedigree";
                            if (ImGui.Selectable(label, selected))
                            {
                                if (chocoboMode == ChocoboAutomationMode.TargetPedigree &&
                                    mode == ChocoboAutomationMode.AlwaysRace &&
                                    string.Equals(charKey, configManager.CurrentCharacterKey, StringComparison.Ordinal))
                                {
                                    plugin.PauseCurrentTargetCycleBestEffort("Chocobo automation changed to Always Race");
                                }

                                cc.ChocoboAutomationMode = mode;
                                chocoboMode = mode;
                                changed = true;
                            }
                            if (selected)
                                ImGui.SetItemDefaultFocus();
                        }
                        ImGui.EndCombo();
                    }
                    DrawDefaultOverrideButton(isDefault, configManager, "ChocoboAutomationMode", "Chocobo automation mode",
                        (source, target) => target.ChocoboAutomationMode = source.ChocoboAutomationMode);
                    ImGui.TextDisabled("Target Pedigree uses a three-hour racing allowance, resetting at 09:00 UTC. Breeding can continue while the allowance is exhausted.");

                    if (cc.ChocoboAutomationMode == ChocoboAutomationMode.TargetPedigree)
                    {
                        DrawChocoboGoalSettings(cc, string.Equals(charKey, configManager.CurrentCharacterKey, StringComparison.Ordinal), ref changed);
                        var targetPedigree = cc.ChocoboTargetPedigree;
                        ImGui.BeginDisabled(cc.ChocoboBreedingGoal != ChocoboBreedingGoal.ReachPedigree);
                        ImGui.SetNextItemWidth(GetCompactNumericInputWidth());
                        if (ImGui.InputInt("Target pedigree", ref targetPedigree))
                        {
                            cc.ChocoboTargetPedigree = Math.Clamp(targetPedigree, 2, 9);
                            changed = true;
                        }
                        DrawDefaultOverrideButton(isDefault, configManager, "ChocoboTargetPedigree", "Chocobo target pedigree",
                            (source, target) => target.ChocoboTargetPedigree = source.ChocoboTargetPedigree);
                        ImGui.EndDisabled();

                        ImGui.TextDisabled("Intermediate chocobos retire at racing rank 40; retain the target pedigree to racing rank 50.");

                        var breedingMode = (int)cc.ChocoboBreedingMode;
                        if (ImGui.Combo("Breeding mode", ref breedingMode, "Owned parents\0NPC covering permits\0"))
                        { cc.ChocoboBreedingMode = (ChocoboBreedingMode)breedingMode; changed = true; }
                        ImGui.TextWrapped(cc.ChocoboBreedingMode == ChocoboBreedingMode.OwnedParents
                            ? "Use retained parents. Missing counterparts stop this mode; covering permits are purchased only in permit mode."
                            : "Use a retained parent and a matching-pedigree permit for the opposite sex. MGP purchases respect the reserve.");
                        if (cc.ChocoboBreedingMode == ChocoboBreedingMode.NpcPermits && cc.ChocoboBreedingGoal == ChocoboBreedingGoal.ReachPedigree)
                        {
                            var objective = cc.ChocoboProduceCounterpart ? 1 : 0;
                            if (ImGui.Combo("Permit objective", ref objective, "Advance pedigree\0Produce a missing counterpart\0"))
                            {
                                if (string.Equals(charKey, configManager.CurrentCharacterKey, StringComparison.Ordinal))
                                    plugin.ChocoboRaceService.PauseProgression();
                                cc.ChocoboProduceCounterpart = objective == 1;
                                cc.ChocoboProgressionPaused = true;
                                changed = true;
                            }
                            ImGui.TextWrapped(cc.ChocoboProduceCounterpart
                                ? "Use a parent one pedigree below the highest reached pedigree to produce another offspring at that pedigree. Keep duplicate sexes unregistered; raise the missing sex. Resume to apply."
                                : "Use the highest useful retained parent to advance by one pedigree.");
                        }
                        var feedPolicy = (int)cc.ChocoboFeedPolicy;
                        if (ImGui.Combo("When feeding cannot proceed", ref feedPolicy, "Fall back\0Skip\0Stop\0"))
                        { cc.ChocoboFeedPolicy = (ChocoboFeedPolicy)feedPolicy; changed = true; }
                        var gilReserve = (int)Math.Min(int.MaxValue, cc.ChocoboGilReserve);
                        if (ImGui.InputInt("Gil reserve", ref gilReserve))
                        { cc.ChocoboGilReserve = (uint)Math.Max(0, gilReserve); changed = true; }
                        var mgpReserve = (int)Math.Min(int.MaxValue, cc.ChocoboMgpReserve);
                        if (ImGui.InputInt("MGP reserve", ref mgpReserve))
                        { cc.ChocoboMgpReserve = (uint)Math.Max(0, mgpReserve); changed = true; }
                        ImGui.TextDisabled("Use stocked feed first. Fall back tries Grade 1 within reserves, then skips. Stop requires Resume.");

                        var preferredFeedGrade = cc.ChocoboPreferredFeedGrade;
                        ImGui.SetNextItemWidth(GetCompactNumericInputWidth());
                        if (ImGui.InputInt("Preferred feed grade", ref preferredFeedGrade))
                        {
                            cc.ChocoboPreferredFeedGrade = Math.Clamp(preferredFeedGrade, 1, 3);
                            changed = true;
                        }
                        DrawDefaultOverrideButton(isDefault, configManager, "ChocoboPreferredFeedGrade", "Chocobo preferred feed grade",
                            (source, target) => target.ChocoboPreferredFeedGrade = source.ChocoboPreferredFeedGrade);

                        if (!ChokeAboTargetCycleProtocol.TryValidateSettings(
                                cc.ChocoboTargetPedigree,
                                cc.ChocoboRetirementRank,
                                cc.ChocoboPreferredFeedGrade,
                                out var targetSettingsError))
                        {
                            ImGui.TextColored(new Vector4(1f, 0.35f, 0.25f, 1f), targetSettingsError);
                        }

                        if (string.Equals(charKey, configManager.CurrentCharacterKey, StringComparison.Ordinal))
                            DrawChokeAboTargetStatus();
                        else
                            ImGui.TextDisabled("Choke-abo status is shown only for the live current character.");
                    }

                    if (cc.ChocoboAutomationMode == ChocoboAutomationMode.AlwaysRace)
                    {
                    var races = cc.ChocoboRacesPerDay;
                    ImGui.Text($"{UIConstants.ConfigLabels.RacesPerDay}:");
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(GetCompactNumericInputWidth() * 2f);
                    if (ImGui.InputInt("##ChocoboRacesPerDay", ref races, 1, 5))
                    {
                        // Clamp between 1 and 69420
                        races = Math.Clamp(races, 1, 69420);
                        cc.ChocoboRacesPerDay = races;
                        changed = true;
                        // Save immediately on change
                        configManager.SaveCurrentAccount();
                    }
                    DrawDefaultOverrideButton(isDefault, configManager, "ChocoboRacesPerDay", UIConstants.ConfigLabels.RacesPerDay,
                        (source, target) => target.ChocoboRacesPerDay = source.ChocoboRacesPerDay);

                    var skipChocoboAtRank50 = cc.SkipChocoboRacingAtRank50;
                    if (ImGui.Checkbox(UIConstants.ConfigLabels.SkipChocoboRacingIfLevel50, ref skipChocoboAtRank50))
                    {
                        cc.SkipChocoboRacingAtRank50 = skipChocoboAtRank50;
                        changed = true;
                    }
                    DrawDefaultOverrideButton(isDefault, configManager, "SkipChocoboRacingAtRank50", UIConstants.ConfigLabels.SkipChocoboRacingIfLevel50,
                        (source, target) => target.SkipChocoboRacingAtRank50 = source.SkipChocoboRacingAtRank50);
                    ImGui.SameLine();
                    ImGui.TextDisabled("(?)");
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Checks rank before each race. Uses RaceChocoboManager when loaded, then opens /goldsaucer and reads GoldSaucerInfo node 21 as fallback. Rank 50 stops the daily racing task before another queue.");
                    }

                    ImGui.Unindent();
                }
            }

            if (selectedAutomationId == AutomationCatalog.AlliedSociety)
            {
                var alliedSociety = cc.EnableAlliedSociety;
                if (ImGui.Checkbox("Allied Society", ref alliedSociety))
                {
                    cc.EnableAlliedSociety = alliedSociety;
                    changed = true;
                }
                DrawDefaultOverrideButton(isDefault, configManager, "AlliedSociety", "Allied Society",
                    (source, target) => target.EnableAlliedSociety = source.EnableAlliedSociety);
                if (DrawResetButton("AlliedSocietyState", cc.ResetAlliedSocietyState))
                    changed = true;
                if (ResetDetectionService.TaskIsCompleted(cc.AlliedSocietyLastCompleted, cc.AlliedSocietyNextReset))
                {
                    ImGui.SameLine();
                    ImGui.TextColored(new Vector4(1, 1, 0, 1), "[Already Completed]");
                }
                DrawDailyTaskHint(cc.AlliedSocietyLastCompleted, cc.AlliedSocietyNextReset,
                    "Runs Questionable Companion's Allied Society rotation for this current character only.");
                if (cc.EnableAlliedSociety)
                {
                    ImGui.Indent();
                    var gearsetSelection = cc.AlliedSocietyGearsetSelection;
                    if (ImGui.RadioButton("Current Job##AlliedSociety", gearsetSelection == AlliedSocietyGearsetSelection.CurrentJob))
                    {
                        cc.AlliedSocietyGearsetSelection = AlliedSocietyGearsetSelection.CurrentJob;
                        changed = true;
                    }
                    ImGui.SameLine();
                    if (ImGui.RadioButton("Saved Gearset##AlliedSociety", gearsetSelection == AlliedSocietyGearsetSelection.SavedGearset))
                    {
                        cc.AlliedSocietyGearsetSelection = AlliedSocietyGearsetSelection.SavedGearset;
                        changed = true;
                    }
                    DrawDefaultOverrideButton(isDefault, configManager, "AlliedSocietyGearsetSelection", "Allied Society gearset mode",
                        (source, target) => target.AlliedSocietyGearsetSelection = source.AlliedSocietyGearsetSelection);

                    if (cc.AlliedSocietyGearsetSelection == AlliedSocietyGearsetSelection.SavedGearset)
                    {
                        var gearsets = plugin.EquipmentAutomationRuntime.GetValidGearsets();
                        var selectedGearset = gearsets.FirstOrDefault(gearset => gearset.GearsetId == cc.AlliedSocietyGearsetId);
                        var preview = selectedGearset == null
                            ? $"Invalid gearset {cc.AlliedSocietyGearsetId}"
                            : FormatGearset(selectedGearset);
                        if (ImGui.BeginCombo("Saved gearset", preview))
                        {
                            foreach (var gearset in gearsets.OrderBy(gearset => gearset.GearsetId))
                            {
                                var selected = gearset.GearsetId == cc.AlliedSocietyGearsetId;
                                if (ImGui.Selectable(FormatGearset(gearset), selected))
                                {
                                    cc.AlliedSocietyGearsetId = gearset.GearsetId;
                                    changed = true;
                                }
                                if (selected)
                                    ImGui.SetItemDefaultFocus();
                            }
                            if (gearsets.Count == 0)
                                ImGui.TextDisabled("No valid saved gearsets are available on the current character.");
                            ImGui.EndCombo();
                        }
                        DrawDefaultOverrideButton(isDefault, configManager, "AlliedSocietyGearsetId", "Allied Society saved gearset",
                            (source, target) => target.AlliedSocietyGearsetId = source.AlliedSocietyGearsetId);
                        if (selectedGearset == null)
                            ImGui.TextColored(new Vector4(1f, 0.25f, 0.25f, 1f), "A valid saved gearset must be selected before this task can start.");
                    }

                    ImGui.TextDisabled("Questionable Companion must be loaded with its AlliedSocietyRotationService public contract available.");
                    ImGui.Unindent();
                }
            }

            if (selectedAutomationId == AutomationCatalog.LootGoblinMapGather)
            {
                var lootGoblinMapGather = cc.EnableLootGoblinMapGather;
                if (ImGui.Checkbox(UIConstants.ConfigLabels.LootGoblinMapGather, ref lootGoblinMapGather))
                {
                    cc.EnableLootGoblinMapGather = lootGoblinMapGather;
                    changed = true;
                }
                DrawDefaultOverrideButton(isDefault, configManager, "LootGoblinMapGather", UIConstants.ConfigLabels.LootGoblinMapGather,
                    (source, target) => target.EnableLootGoblinMapGather = source.EnableLootGoblinMapGather);
                if (DrawResetButton("LootGoblinMapGatherState", cc.ResetLootGoblinMapGatherState))
                    changed = true;
                if (ResetDetectionService.TaskIsCompleted(cc.LootGoblinMapGatherLastCompleted, cc.LootGoblinMapGatherNextReset))
                {
                    ImGui.SameLine();
                    ImGui.TextColored(new Vector4(1, 1, 0, 1), "[Already Completed]");
                }
                DrawDailyTaskHint(cc.LootGoblinMapGatherLastCompleted, cc.LootGoblinMapGatherNextReset, "Runs once per daily reset through LootGoblin IPC.");
                if (cc.EnableLootGoblinMapGather)
                {
                    ImGui.Indent();
                    if (DrawLootGoblinMapDropdown(cc))
                        changed = true;
                    DrawDefaultOverrideButton(isDefault, configManager, "LootGoblinMapGatherItemId", "LootGoblin map",
                        (source, target) => target.LootGoblinMapGatherItemId = source.LootGoblinMapGatherItemId);

                    var runAfterGather = cc.LootGoblinMapGatherRunAfterGather;
                    if (ImGui.Checkbox("Run map after gather", ref runAfterGather))
                    {
                        cc.LootGoblinMapGatherRunAfterGather = runAfterGather;
                        changed = true;
                    }
                    DrawDefaultOverrideButton(isDefault, configManager, "LootGoblinMapGatherRunAfterGather", "Run map after gather",
                        (source, target) => target.LootGoblinMapGatherRunAfterGather = source.LootGoblinMapGatherRunAfterGather);

                    if (cc.LootGoblinMapGatherRunAfterGather && !IsSelectedLootGoblinMapSafe(cc))
                        ImGui.TextColored(new Vector4(1f, 0.25f, 0.25f, 1f), "Warning: run-after is safest only for solo outdoor maps without dungeons.");

                    ImGui.Unindent();
                }
            }

        }

        if (BeginConfigurationSection(UIConstants.ConfigLabels.VariableTimeTasks, ConfigurationSection.VariableTime))
        {
            if (selectedAutomationId == AutomationCatalog.RefillListings)
            {
                var refillListings = cc.EnableRefillFromListings;
                if (ImGui.Checkbox("Refill from listings", ref refillListings))
                {
                    cc.EnableRefillFromListings = refillListings;
                    changed = true;
                }
                DrawDefaultOverrideButton(isDefault, configManager, "RefillFromListings", "Refill from listings",
                    (source, target) => target.EnableRefillFromListings = source.EnableRefillFromListings);
                ImGui.SameLine();
                ImGui.TextDisabled("(?)");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Withdraws current retainer market listings back into player inventory on the selected schedule. If RetainerList is not already open, runs the selected Lifestream route before looking for a Summoning Bell.");
                if (cc.EnableRefillFromListings)
                {
                    ImGui.Indent();

                    var withdrawGil = cc.RefillFromListingsWithdrawGil;
                    if (ImGui.Checkbox("Enable AutoRetainer gil withdrawal for this character's retainers", ref withdrawGil))
                    {
                        cc.RefillFromListingsWithdrawGil = withdrawGil;
                        changed = true;
                    }
                    DrawDefaultOverrideButton(isDefault, configManager, "RefillFromListingsWithdrawGil", "Refill from listings gil withdrawal",
                        (source, target) => target.RefillFromListingsWithdrawGil = source.RefillFromListingsWithdrawGil);
                    DrawHelpMarker("Enables AutoRetainer's withdraw-gil setting for this character's retainers on login or plugin reload and when Refill Listings starts. Preserves each retainer's withdrawal percentage. Turning this off stops applying the setting; it does not undo AutoRetainer settings.");

                    ImGui.Text("Frequency:");
                    ImGui.SameLine();
                    var refillFrequency = cc.RefillFromListingsFrequency;
                    if (ImGui.RadioButton("AR##RefillListingsEveryAR", refillFrequency == RefillFromListingsFrequency.EveryAR))
                    {
                        cc.RefillFromListingsFrequency = RefillFromListingsFrequency.EveryAR;
                        changed = true;
                    }
                    ImGui.SameLine();
                    if (ImGui.RadioButton("Daily##RefillListingsDaily", refillFrequency == RefillFromListingsFrequency.Daily))
                    {
                        cc.RefillFromListingsFrequency = RefillFromListingsFrequency.Daily;
                        changed = true;
                    }
                    ImGui.SameLine();
                    if (ImGui.RadioButton("Weekly##RefillListingsWeekly", refillFrequency == RefillFromListingsFrequency.Weekly))
                    {
                        cc.RefillFromListingsFrequency = RefillFromListingsFrequency.Weekly;
                        changed = true;
                    }
                    ImGui.SameLine();
                    if (ImGui.RadioButton("Monthly##RefillListingsMonthly", refillFrequency == RefillFromListingsFrequency.Monthly))
                    {
                        cc.RefillFromListingsFrequency = RefillFromListingsFrequency.Monthly;
                        changed = true;
                    }
                    DrawDefaultOverrideButton(isDefault, configManager, "RefillFromListingsFrequency", "Refill from listings frequency",
                        (source, target) => target.RefillFromListingsFrequency = source.RefillFromListingsFrequency);

                    ImGui.Text("Selection:");
                    ImGui.SameLine();
                    var refillSelection = cc.RefillFromListingsSelectionMode;
                    if (ImGui.RadioButton("All##RefillListingsAll", refillSelection == RefillFromListingsSelectionMode.All))
                    {
                        cc.RefillFromListingsSelectionMode = RefillFromListingsSelectionMode.All;
                        changed = true;
                    }
                    ImGui.SameLine();
                    if (ImGui.RadioButton("Random##RefillListingsRandom", refillSelection == RefillFromListingsSelectionMode.Random))
                    {
                        cc.RefillFromListingsSelectionMode = RefillFromListingsSelectionMode.Random;
                        changed = true;
                    }
                    DrawDefaultOverrideButton(isDefault, configManager, "RefillFromListingsSelectionMode", "Refill from listings selection",
                        (source, target) => target.RefillFromListingsSelectionMode = source.RefillFromListingsSelectionMode);

                    ImGui.Text("Route:");
                    ImGui.SameLine();
                    var refillRoute = cc.RefillFromListingsRoute;
                    if (ImGui.RadioButton("Workshop (/li ws)##RefillListingsWorkshop", refillRoute == RefillFromListingsRoute.Workshop))
                    {
                        cc.RefillFromListingsRoute = RefillFromListingsRoute.Workshop;
                        changed = true;
                    }
                    ImGui.SameLine();
                    if (ImGui.RadioButton("Inn (/li inn)##RefillListingsInn", refillRoute == RefillFromListingsRoute.Inn))
                    {
                        cc.RefillFromListingsRoute = RefillFromListingsRoute.Inn;
                        changed = true;
                    }
                    ImGui.SameLine();
                    if (ImGui.RadioButton("Limsa (/li limsa)##RefillListingsLimsa", refillRoute == RefillFromListingsRoute.Limsa))
                    {
                        cc.RefillFromListingsRoute = RefillFromListingsRoute.Limsa;
                        changed = true;
                    }
                    DrawDefaultOverrideButton(isDefault, configManager, "RefillFromListingsRoute", "Refill from listings route",
                        (source, target) => target.RefillFromListingsRoute = source.RefillFromListingsRoute);

                    ImGui.SameLine();
                    ImGui.TextDisabled("(?)");
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("When RetainerList is closed, VERMAXION runs the selected /li route first, waits for it to settle, then finds and opens the route bell.");

                    var minFreeInventorySlots = Math.Clamp(cc.RefillFromListingsMinFreeInventorySlots, 10, 100);
                    if (minFreeInventorySlots != cc.RefillFromListingsMinFreeInventorySlots)
                    {
                        cc.RefillFromListingsMinFreeInventorySlots = minFreeInventorySlots;
                        changed = true;
                    }

                    ImGui.SetNextItemWidth(GetCompactNumericInputWidth() * 1.5f);
                    if (ImGui.InputInt("Minimum free inventory slots##RefillListingsMinFreeInventorySlots", ref minFreeInventorySlots, 1, 5))
                    {
                        cc.RefillFromListingsMinFreeInventorySlots = Math.Clamp(minFreeInventorySlots, 10, 100);
                        changed = true;
                    }
                    DrawDefaultOverrideButton(isDefault, configManager, "RefillFromListingsMinFreeInventorySlots", "Refill from listings minimum free inventory slots",
                        (source, target) => target.RefillFromListingsMinFreeInventorySlots = source.RefillFromListingsMinFreeInventorySlots);

                    DrawRefillFromListingsHint(cc);
                    if (DrawResetButton("RefillFromListingsState", cc.ResetRefillFromListingsState))
                        changed = true;

                    ImGui.Unindent();
                }
            }

            if (selectedAutomationId == AutomationCatalog.ReturnBeforeNag)
            {
                var returnBeforeNag = cc.EnableReturnBeforeNag;
                if (ImGui.Checkbox("Return before nag your mom / dad", ref returnBeforeNag))
                {
                    cc.EnableReturnBeforeNag = returnBeforeNag;
                    changed = true;
                }
                DrawDefaultOverrideButton(isDefault, configManager, "ReturnBeforeNag", "Return before nag your mom / dad",
                    (source, target) => target.EnableReturnBeforeNag = source.EnableReturnBeforeNag);
                if (cc.EnableReturnBeforeNag)
                {
                    ImGui.Indent();
                    var returnCommand = cc.ReturnBeforeNagCommand ?? string.Empty;
                    if (ImGui.InputText("Return command##BeforeNag", ref returnCommand, 256))
                    {
                        cc.ReturnBeforeNagCommand = returnCommand;
                        changed = true;
                    }
                    DrawDefaultOverrideButton(isDefault, configManager, "ReturnBeforeNagCommand", "Return before mom / dad command",
                        (source, target) => target.ReturnBeforeNagCommand = source.ReturnBeforeNagCommand);
                    if (!cc.TryGetReturnBeforeNagCommand(out _))
                        ImGui.TextDisabled("Enter one nonempty slash command on a single line; mom / dad will not start with invalid input.");
                    ImGui.TextDisabled("Shared by mom and dad. Waits for travel and two seconds without movement before each new request.");
                    ImGui.Unindent();
                }
            }

            if (selectedAutomationId == AutomationCatalog.NagYourMom)
            {
                var nagYourMom = cc.EnableNagYourMom;
                if (ImGui.Checkbox(UIConstants.ConfigLabels.NagYourMom, ref nagYourMom))
                {
                    cc.EnableNagYourMom = nagYourMom;
                    changed = true;
                }
                DrawDefaultOverrideButton(isDefault, configManager, "NagYourMom", UIConstants.ConfigLabels.NagYourMom,
                    (source, target) => target.EnableNagYourMom = source.EnableNagYourMom);
                ImGui.SameLine();
                ImGui.TextDisabled("(?)");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(UIConstants.Tooltips.NagYourMom);
                if (DrawResetButton("NagYourMomDailyState", cc.ResetNagYourMomDailyState))
                    changed = true;
                if (cc.EnableNagYourMom)
                {
                    ImGui.Indent();

                    var momCasualCc = cc.EnableNagYourMomCasualCc;
                    if (ImGui.Checkbox(UIConstants.ConfigLabels.NagYourMomCasualCc, ref momCasualCc))
                    {
                        cc.EnableNagYourMomCasualCc = momCasualCc;
                        changed = true;
                    }
                    DrawDefaultOverrideButton(isDefault, configManager, "NagYourMomCasualCc", UIConstants.ConfigLabels.NagYourMomCasualCc,
                        (source, target) => target.EnableNagYourMomCasualCc = source.EnableNagYourMomCasualCc);
                    if (cc.EnableNagYourMomCasualCc)
                    {
                        ImGui.Indent();
                        var momRunsPerDay = cc.NagYourMomRunsPerDay;
                        ImGui.SetNextItemWidth(GetCompactNumericInputWidth() * 1.5f);
                        if (ImGui.InputInt(UIConstants.ConfigLabels.NagYourMomRunsPerDay, ref momRunsPerDay))
                        {
                            cc.NagYourMomRunsPerDay = Math.Max(0, momRunsPerDay);
                            changed = true;
                            configManager.SaveCurrentAccount();
                        }
                        DrawDefaultOverrideButton(isDefault, configManager, "NagYourMomRunsPerDay", UIConstants.ConfigLabels.NagYourMomRunsPerDay,
                            (source, target) => target.NagYourMomRunsPerDay = source.NagYourMomRunsPerDay);
                        ImGui.TextDisabled($"CC attempts today: {cc.NagYourMomAttemptsToday}/{cc.NagYourMomRunsPerDay}");
                        ImGui.Unindent();
                    }

                    var momFrontline = cc.EnableNagYourMomFrontline;
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.2f, 0.2f, 1f));
                    var frontlineChanged = ImGui.Checkbox(UIConstants.ConfigLabels.NagYourMomFrontline, ref momFrontline);
                    ImGui.PopStyleColor();
                    if (frontlineChanged)
                    {
                        if (momFrontline)
                            RequestConfirmation("Enable Frontline?", string.Empty,
                                () =>
                                {
                                    cc.EnableNagYourMomFrontline = true;
                                    configManager.SaveCurrentAccount();
                                },
                                () => PvpEnableWarning(UIConstants.ConfigLabels.NagYourMomFrontline));
                        else
                        {
                            cc.EnableNagYourMomFrontline = false;
                            changed = true;
                        }
                    }
                    DrawDefaultOverrideButton(isDefault, configManager, "NagYourMomFrontline", UIConstants.ConfigLabels.NagYourMomFrontline,
                        (source, target) => target.EnableNagYourMomFrontline = source.EnableNagYourMomFrontline);
                    if (cc.EnableNagYourMomFrontline)
                    {
                        ImGui.Indent();
                        var frontlineRuns = cc.NagYourMomFrontlineRunsPerDay;
                        ImGui.SetNextItemWidth(GetCompactNumericInputWidth() * 1.5f);
                        if (ImGui.InputInt(UIConstants.ConfigLabels.NagYourMomFrontlineRunsPerDay, ref frontlineRuns))
                        {
                            cc.NagYourMomFrontlineRunsPerDay = Math.Max(0, frontlineRuns);
                            changed = true;
                            configManager.SaveCurrentAccount();
                        }
                        DrawDefaultOverrideButton(isDefault, configManager, "NagYourMomFrontlineRunsPerDay", UIConstants.ConfigLabels.NagYourMomFrontlineRunsPerDay,
                            (source, target) => target.NagYourMomFrontlineRunsPerDay = source.NagYourMomFrontlineRunsPerDay);
                        ImGui.TextDisabled($"Frontline attempts today: {cc.NagYourMomFrontlineAttemptsToday}/{cc.NagYourMomFrontlineRunsPerDay}");
                        ImGui.Unindent();
                    }

                    var momRivalWings = cc.EnableNagYourMomRivalWings;
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.2f, 0.2f, 1f));
                    var rivalWingsChanged = ImGui.Checkbox(UIConstants.ConfigLabels.NagYourMomRivalWings, ref momRivalWings);
                    ImGui.PopStyleColor();
                    if (rivalWingsChanged)
                    {
                        if (momRivalWings)
                            RequestConfirmation("Enable Rival Wings?", string.Empty,
                                () =>
                                {
                                    cc.EnableNagYourMomRivalWings = true;
                                    configManager.SaveCurrentAccount();
                                },
                                () => PvpEnableWarning(UIConstants.ConfigLabels.NagYourMomRivalWings));
                        else
                        {
                            cc.EnableNagYourMomRivalWings = false;
                            changed = true;
                        }
                    }
                    DrawDefaultOverrideButton(isDefault, configManager, "NagYourMomRivalWings", UIConstants.ConfigLabels.NagYourMomRivalWings,
                        (source, target) => target.EnableNagYourMomRivalWings = source.EnableNagYourMomRivalWings);
                    if (cc.EnableNagYourMomRivalWings)
                    {
                        ImGui.Indent();
                        var rivalWingsRuns = cc.NagYourMomRivalWingsRunsPerDay;
                        ImGui.SetNextItemWidth(GetCompactNumericInputWidth() * 1.5f);
                        if (ImGui.InputInt(UIConstants.ConfigLabels.NagYourMomRivalWingsRunsPerDay, ref rivalWingsRuns))
                        {
                            cc.NagYourMomRivalWingsRunsPerDay = Math.Max(0, rivalWingsRuns);
                            changed = true;
                            configManager.SaveCurrentAccount();
                        }
                        DrawDefaultOverrideButton(isDefault, configManager, "NagYourMomRivalWingsRunsPerDay", UIConstants.ConfigLabels.NagYourMomRivalWingsRunsPerDay,
                            (source, target) => target.NagYourMomRivalWingsRunsPerDay = source.NagYourMomRivalWingsRunsPerDay);
                        ImGui.TextDisabled($"Rival Wings attempts today: {cc.NagYourMomRivalWingsAttemptsToday}/{cc.NagYourMomRivalWingsRunsPerDay}");
                        ImGui.Unindent();
                    }

                    if (DrawJobCombo(UIConstants.ConfigLabels.NagYourMomJob, cc.NagYourMomJob, false, out var momJob))
                    {
                        cc.NagYourMomJob = momJob;
                        changed = true;
                    }
                    DrawDefaultOverrideButton(isDefault, configManager, "NagYourMomJob", UIConstants.ConfigLabels.NagYourMomJob,
                        (source, target) => target.NagYourMomJob = NormalizeJobAbbreviation(source.NagYourMomJob));

                    var localStart = cc.NagYourMomWindowStartLocal;
                    if (ImGui.InputText(UIConstants.ConfigLabels.NagYourMomWindowStartLocal, ref localStart, 16))
                    {
                        cc.NagYourMomWindowStartLocal = localStart.Trim();
                        changed = true;
                    }
                    DrawDefaultOverrideButton(isDefault, configManager, "NagYourMomWindowStartLocal", UIConstants.ConfigLabels.NagYourMomWindowStartLocal,
                        (source, target) => target.NagYourMomWindowStartLocal = source.NagYourMomWindowStartLocal);

                    var localEnd = cc.NagYourMomWindowEndLocal;
                    if (ImGui.InputText(UIConstants.ConfigLabels.NagYourMomWindowEndLocal, ref localEnd, 16))
                    {
                        cc.NagYourMomWindowEndLocal = localEnd.Trim();
                        changed = true;
                    }
                    DrawDefaultOverrideButton(isDefault, configManager, "NagYourMomWindowEndLocal", UIConstants.ConfigLabels.NagYourMomWindowEndLocal,
                        (source, target) => target.NagYourMomWindowEndLocal = source.NagYourMomWindowEndLocal);

                    var stopAt25 = cc.NagYourMomStopAtSeriesRank25;
                    if (ImGui.Checkbox(UIConstants.ConfigLabels.NagYourMomStopAtSeriesRank25, ref stopAt25))
                    {
                        cc.NagYourMomStopAtSeriesRank25 = stopAt25;
                        changed = true;
                    }
                    DrawDefaultOverrideButton(isDefault, configManager, "NagYourMomStopAtSeriesRank25", UIConstants.ConfigLabels.NagYourMomStopAtSeriesRank25,
                        (source, target) => target.NagYourMomStopAtSeriesRank25 = source.NagYourMomStopAtSeriesRank25);

                    ImGui.TextDisabled($"Engine status: {plugin.Engine.NagYourMomStatusText}");
                    ImGui.TextWrapped("AR-only task. VERMAXION evaluates this during the normal post-process pass, checks the local machine time window, then asks mom for due routes in order: CC, Frontline, Rival Wings.");
                    ImGui.Unindent();
                }
            }

            if (selectedAutomationId == AutomationCatalog.NagYourDad)
            {
                var nagYourDad = cc.EnableNagYourDad;
                if (ImGui.Checkbox(UIConstants.ConfigLabels.NagYourDad, ref nagYourDad))
                {
                    cc.EnableNagYourDad = nagYourDad;
                    changed = true;
                }
                DrawDefaultOverrideButton(isDefault, configManager, "NagYourDad", UIConstants.ConfigLabels.NagYourDad,
                    (source, target) => target.EnableNagYourDad = source.EnableNagYourDad);
                ImGui.SameLine();
                ImGui.TextDisabled("(?)");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(UIConstants.Tooltips.NagYourDad);
                if (cc.EnableNagYourDad)
                {
                    ImGui.Indent();
                    DrawDadSelectionSelector(cc, ref changed);
                    DrawDefaultOverrideButton(isDefault, configManager, "NagYourDadSelection", "DAD preset or schedule",
                        (source, target) =>
                        {
                            target.NagYourDadSelectionKind = source.NagYourDadSelectionKind;
                            target.NagYourDadSelectionId = source.NagYourDadSelectionId;
                            target.NagYourDadSelectionDisplayName = source.NagYourDadSelectionDisplayName;
                        });
                    var dadStatus = plugin.DadIPCClient.GetStatus();
                    ImGui.TextDisabled($"DAD IPC: {(plugin.DadIPCClient.IsReady() ? "Ready" : "Unavailable")} | launch: {dadStatus.Status}");
                    ImGui.TextWrapped($"DAD status: {dadStatus.Summary}");
                    ImGui.TextDisabled($"VERMAXION status: {plugin.Engine.NagYourDadStatusText}");
                    ImGui.TextWrapped("VERMAXION launches the selected saved DAD preset through the scheduler path, or the selected DAD schedule through its exact schedule run path.");
                    ImGui.Unindent();
                }
                if (ShouldDrawLegacyDadTaskBuilder())
                {
                    ImGui.Indent();

                    ImGui.TextWrapped("Dungeon count tells dad how many times to run the selected Duty Finder duty.");
                    var dadDungeonCount = cc.NagYourDadDungeonCount;
                    ImGui.SetNextItemWidth(GetCompactNumericInputWidth() * 1.5f);
                    if (ImGui.InputInt(UIConstants.ConfigLabels.NagYourDadDungeonCount, ref dadDungeonCount))
                    {
                        cc.NagYourDadDungeonCount = Math.Max(0, dadDungeonCount);
                        changed = true;
                        configManager.SaveCurrentAccount();
                    }
                    DrawDefaultOverrideButton(isDefault, configManager, "NagYourDadDungeonCount", UIConstants.ConfigLabels.NagYourDadDungeonCount,
                        (source, target) => target.NagYourDadDungeonCount = source.NagYourDadDungeonCount);

                    ImGui.TextWrapped("Dungeon frequency controls when dad should queue the selected duty from AR-triggered VERMAXION runs.");
                    var dadDungeonFrequencyIndex = DadRunRequestOptions.GetFrequencyIndex(cc.NagYourDadDungeonFrequency);
                    if (ImGui.Combo(UIConstants.ConfigLabels.NagYourDadDungeonFrequency, ref dadDungeonFrequencyIndex, DadRunRequestOptions.DungeonFrequencies, DadRunRequestOptions.DungeonFrequencies.Length))
                    {
                        cc.NagYourDadDungeonFrequency = DadRunRequestOptions.DungeonFrequencies[dadDungeonFrequencyIndex];
                        changed = true;
                    }
                    DrawDefaultOverrideButton(isDefault, configManager, "NagYourDadDungeonFrequency", UIConstants.ConfigLabels.NagYourDadDungeonFrequency,
                        (source, target) => target.NagYourDadDungeonFrequency = DadRunRequestOptions.NormalizeFrequency(source.NagYourDadDungeonFrequency));

                    ImGui.TextWrapped("Dungeon is the Duty Finder duty dad should run. Search by name or row id.");
                    DrawDadDutySelector(cc, ref changed);
                    DrawDefaultOverrideButton(isDefault, configManager, "NagYourDadDungeonName", UIConstants.ConfigLabels.NagYourDadDungeonName,
                        (source, target) =>
                        {
                            target.NagYourDadDungeonContentFinderConditionId = source.NagYourDadDungeonContentFinderConditionId;
                            target.NagYourDadDungeonName = source.NagYourDadDungeonName;
                        });

                    ImGui.TextWrapped("Dungeon job is the job hint dad should use. Leave blank for current job.");
                    if (DrawJobCombo(UIConstants.ConfigLabels.NagYourDadDungeonJob, cc.NagYourDadDungeonJob, true, out var dadDungeonJob))
                    {
                        cc.NagYourDadDungeonJob = dadDungeonJob;
                        changed = true;
                    }
                    DrawDefaultOverrideButton(isDefault, configManager, "NagYourDadDungeonJob", UIConstants.ConfigLabels.NagYourDadDungeonJob,
                        (source, target) => target.NagYourDadDungeonJob = NormalizeJobAbbreviation(source.NagYourDadDungeonJob));

                    ImGui.TextWrapped("dad will prefer Trust when available, then fall back to Duty Support when Trust is not possible.");
                    ImGui.TextDisabled("Execution preference: Trust, then Duty Support");

                    ImGui.TextWrapped("LAN Party queue mode tells dad to use DadLanPartyModule with the selected LAN Party-style preset for premade duty routing.");
                    var dadQueueViaLanParty = cc.NagYourDadQueueViaLanParty;
                    if (ImGui.Checkbox(UIConstants.ConfigLabels.NagYourDadQueueViaLanParty, ref dadQueueViaLanParty))
                    {
                        cc.NagYourDadQueueViaLanParty = dadQueueViaLanParty;
                        changed = true;
                    }
                    DrawDefaultOverrideButton(isDefault, configManager, "NagYourDadQueueViaLanParty", UIConstants.ConfigLabels.NagYourDadQueueViaLanParty,
                        (source, target) => target.NagYourDadQueueViaLanParty = source.NagYourDadQueueViaLanParty);
                    if (cc.NagYourDadQueueViaLanParty)
                    {
                        ImGui.Indent();
                        ImGui.TextWrapped("LAN Party preset is the Dad-provided preset consumed by DadLanPartyModule for this dungeon queue path.");
                        DrawDadLanPartyPresetSelector(cc, ref changed);
                        DrawDefaultOverrideButton(isDefault, configManager, "NagYourDadLanPartyPreset", UIConstants.ConfigLabels.NagYourDadLanPartyPreset,
                            (source, target) => target.NagYourDadLanPartyPreset = source.NagYourDadLanPartyPreset);
                        ImGui.Unindent();
                    }

                    ImGui.TextWrapped("Unsynced is a dad hint for duties that cannot use Trust or Duty Support.");
                    var dadDungeonUnsynced = cc.NagYourDadDungeonUnsynced;
                    if (ImGui.Checkbox(UIConstants.ConfigLabels.NagYourDadDungeonUnsynced, ref dadDungeonUnsynced))
                    {
                        cc.NagYourDadDungeonUnsynced = dadDungeonUnsynced;
                        changed = true;
                    }
                    DrawDefaultOverrideButton(isDefault, configManager, "NagYourDadDungeonUnsynced", UIConstants.ConfigLabels.NagYourDadDungeonUnsynced,
                        (source, target) => target.NagYourDadDungeonUnsynced = source.NagYourDadDungeonUnsynced);

                    ImGui.TextWrapped("Daily MSQ asks dad to run DadLanPartyModule against the configured LAN Party-style preset.");
                    var dadDailyMsq = cc.NagYourDadDailyMsq;
                    if (ImGui.Checkbox(UIConstants.ConfigLabels.NagYourDadDailyMsq, ref dadDailyMsq))
                    {
                        cc.NagYourDadDailyMsq = dadDailyMsq;
                        changed = true;
                    }
                    DrawDefaultOverrideButton(isDefault, configManager, "NagYourDadDailyMsq", UIConstants.ConfigLabels.NagYourDadDailyMsq,
                        (source, target) => target.NagYourDadDailyMsq = source.NagYourDadDailyMsq);
                    if (cc.NagYourDadDailyMsq)
                    {
                        ImGui.Indent();
                        if (cc.NagYourDadQueueViaLanParty)
                        {
                            ImGui.TextDisabled($"Uses LAN Party preset selected above: {cc.NagYourDadLanPartyPreset}");
                        }
                        else
                        {
                            ImGui.TextWrapped("LAN Party preset is the Dad-provided preset for DadLanPartyModule Daily MSQ routing.");
                            DrawDadLanPartyPresetSelector(cc, ref changed);
                            DrawDefaultOverrideButton(isDefault, configManager, "NagYourDadLanPartyPresetDailyMsq", UIConstants.ConfigLabels.NagYourDadLanPartyPreset,
                                (source, target) => target.NagYourDadLanPartyPreset = source.NagYourDadLanPartyPreset);
                        }
                        ImGui.Unindent();
                    }

                    ImGui.TextWrapped("Commendation attempts tells dad how many commendation-focused runs to attempt.");
                    var dadCommendationAttempts = cc.NagYourDadCommendationAttempts;
                    ImGui.SetNextItemWidth(GetCompactNumericInputWidth() * 1.5f);
                    if (ImGui.InputInt(UIConstants.ConfigLabels.NagYourDadCommendationAttempts, ref dadCommendationAttempts))
                    {
                        cc.NagYourDadCommendationAttempts = Math.Max(0, dadCommendationAttempts);
                        changed = true;
                        configManager.SaveCurrentAccount();
                    }
                    DrawDefaultOverrideButton(isDefault, configManager, "NagYourDadCommendationAttempts", UIConstants.ConfigLabels.NagYourDadCommendationAttempts,
                        (source, target) => target.NagYourDadCommendationAttempts = source.NagYourDadCommendationAttempts);

                    ImGui.TextWrapped("Astrope attempts tells dad how many Astrope commendation attempts to schedule inside the local time window.");
                    var dadAstropeAttempts = cc.NagYourDadAstropeAttempts;
                    ImGui.SetNextItemWidth(GetCompactNumericInputWidth() * 1.5f);
                    if (ImGui.InputInt(UIConstants.ConfigLabels.NagYourDadAstropeAttempts, ref dadAstropeAttempts))
                    {
                        cc.NagYourDadAstropeAttempts = Math.Max(0, dadAstropeAttempts);
                        changed = true;
                        configManager.SaveCurrentAccount();
                    }
                    DrawDefaultOverrideButton(isDefault, configManager, "NagYourDadAstropeAttempts", UIConstants.ConfigLabels.NagYourDadAstropeAttempts,
                        (source, target) => target.NagYourDadAstropeAttempts = source.NagYourDadAstropeAttempts);

                    ImGui.TextWrapped("Astrope local start is the first local machine time dad may run Astrope attempts.");
                    var dadWindowStart = cc.NagYourDadWindowStartLocal;
                    if (ImGui.InputText(UIConstants.ConfigLabels.NagYourDadWindowStartLocal, ref dadWindowStart, 16))
                    {
                        cc.NagYourDadWindowStartLocal = dadWindowStart.Trim();
                        changed = true;
                    }
                    DrawDefaultOverrideButton(isDefault, configManager, "NagYourDadWindowStartLocal", UIConstants.ConfigLabels.NagYourDadWindowStartLocal,
                        (source, target) => target.NagYourDadWindowStartLocal = source.NagYourDadWindowStartLocal);

                    ImGui.TextWrapped("Astrope local end is the last local machine time dad may run Astrope attempts.");
                    var dadWindowEnd = cc.NagYourDadWindowEndLocal;
                    if (ImGui.InputText(UIConstants.ConfigLabels.NagYourDadWindowEndLocal, ref dadWindowEnd, 16))
                    {
                        cc.NagYourDadWindowEndLocal = dadWindowEnd.Trim();
                        changed = true;
                    }
                    DrawDefaultOverrideButton(isDefault, configManager, "NagYourDadWindowEndLocal", UIConstants.ConfigLabels.NagYourDadWindowEndLocal,
                        (source, target) => target.NagYourDadWindowEndLocal = source.NagYourDadWindowEndLocal);

                    ImGui.TextDisabled($"Engine status: {plugin.Engine.NagYourDadStatusText}");
                    ImGui.TextWrapped("AR-only task. VERMAXION builds one combined dad payload from the configured dungeon, MSQ, commendation, and Astrope asks. Dad then owns cross-account orchestration. If dad is unavailable or rejects the payload, VERMAXION moves on and retries on the next AR pass.");
                    ImGui.Unindent();
                }
            }

        }

        if (BeginConfigurationSection(UIConstants.ConfigLabels.WipTasks, ConfigurationSection.Wip))
        {
            if (selectedAutomationId == AutomationCatalog.EvercoldAdventurerActivity)
            {
                var evercoldActivity = cc.EnableEvercoldAdventurerActivity;
                if (ImGui.Checkbox("Adventurer Activity (Evercold) [WIP]", ref evercoldActivity))
                {
                    cc.EnableEvercoldAdventurerActivity = evercoldActivity;
                    changed = true;
                }
                DrawDefaultOverrideButton(isDefault, configManager, "EvercoldAdventurerActivity", "Adventurer Activity (Evercold)",
                    (source, target) => target.EnableEvercoldAdventurerActivity = source.EnableEvercoldAdventurerActivity);
                if (cc.EnableEvercoldAdventurerActivity)
                {
                    ImGui.Indent();

                    var currentPoints = cc.EvercoldAdventurerActivityCurrentPoints;
                    ImGui.SetNextItemWidth(GetCompactNumericInputWidth() * 2f);
                    if (ImGui.InputInt("Current points", ref currentPoints))
                    {
                        cc.EvercoldAdventurerActivityCurrentPoints = Math.Max(0, currentPoints);
                        if (cc.EvercoldAdventurerActivityTargetPoints > 0)
                            cc.EvercoldAdventurerActivityCurrentPoints = Math.Min(cc.EvercoldAdventurerActivityCurrentPoints, cc.EvercoldAdventurerActivityTargetPoints);
                        changed = true;
                    }

                    var targetPoints = cc.EvercoldAdventurerActivityTargetPoints;
                    ImGui.SetNextItemWidth(GetCompactNumericInputWidth() * 2f);
                    if (ImGui.InputInt("Point cap", ref targetPoints))
                    {
                        cc.EvercoldAdventurerActivityTargetPoints = Math.Max(0, targetPoints);
                        if (cc.EvercoldAdventurerActivityTargetPoints > 0)
                            cc.EvercoldAdventurerActivityCurrentPoints = Math.Min(cc.EvercoldAdventurerActivityCurrentPoints, cc.EvercoldAdventurerActivityTargetPoints);
                        changed = true;
                    }
                    DrawDefaultOverrideButton(isDefault, configManager, "EvercoldAdventurerActivityTargetPoints", "Evercold point cap",
                        (source, target) => target.EvercoldAdventurerActivityTargetPoints = source.EvercoldAdventurerActivityTargetPoints);

                    var evercoldDone = cc.EvercoldAdventurerActivityCompleted;
                    if (ImGui.Checkbox("Done##EvercoldActivityDone", ref evercoldDone))
                    {
                        cc.EvercoldAdventurerActivityCompleted = evercoldDone;
                        changed = true;
                    }
                    if (DrawResetButton("EvercoldAdventurerActivityState", cc.ResetEvercoldAdventurerActivityState))
                        changed = true;

                    ImGui.TextDisabled("Config-only WIP entry. Automation will stop at the point cap when real Evercold logic is added.");
                    ImGui.Unindent();
                }
            }

        }

        ImGui.Spacing();
        ImGui.Separator();

        if (ImGui.CollapsingHeader("Profile actions & saved task state"))
        {
            // Reset buttons
            if (ImGui.Button("Reset Weekly Section"))
            {
                RequestTaskStateReset("weekly task state", charKey, cc.ResetWeeklySectionState);
            }
            ImGui.SameLine();
            if (ImGui.Button("Reset Daily Section"))
            {
                RequestTaskStateReset("daily task state", charKey, cc.ResetDailySectionState);
            }
            if (ImGui.Button("Reset All Character Task State"))
            {
                RequestTaskStateReset("all saved task state", charKey, cc.ResetAllTaskState);
            }

            ImGui.Spacing();

            // Apply Default to All button (only visible when editing default config)
            if (isDefault)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.5f, 0.8f, 1));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.6f, 0.9f, 1));
                var account = configManager.GetCurrentAccount();
                var differing = account?.Characters.Values.Count(character =>
                    !SettingsMatchDefault(account.DefaultConfig, character)) ?? 0;
                if (ImGui.Button($"Apply Default Settings to ALL Characters ({differing})", new Vector2(-1, 30)))
                {
                    var count = RunConfigMutationWithTargetPause(
                        configManager.ApplyDefaultToAllCharacters,
                        "all default settings applied to all characters");
                    Plugin.Log.Information($"[Config] Applied default settings to {count} characters");
                    Plugin.ChatGui.Print($"[Vermaxion] Default settings applied to {count} characters.");
                }
                ImGui.PopStyleColor(2);
                ImGui.TextDisabled("Copies all toggles and values from Default to every character. Preserves completion flags.");
            }
        }

        if (changed)
            configManager.SaveCurrentAccount();
    }

    private void DrawMarketboardSettings()
    {
        var manager = plugin.ConfigManager;
        if (manager.GetCurrentAccount() == null)
        { ImGui.TextWrapped("Select an account to edit marketboard settings."); return; }
        var settings = manager.GetSelectedConfig().ChocoboStablesSettings;
        ImGui.TextWrapped("Purchasing settings for the account and profile shown above.");
        ImGui.Separator();
        var enabled = settings.BuyOnionFromMarketboard;
        var changed = false;
        if (ImGui.Checkbox("Buy a Thavnairian Onion when needed (Emptor)", ref enabled))
        { settings.BuyOnionFromMarketboard = enabled; changed = true; }
        var unitLimit = settings.OnionMaxUnitPrice;
        if (ImGui.InputInt("Maximum onion unit price (gil)", ref unitLimit))
        { settings.OnionMaxUnitPrice = Math.Max(0, unitLimit); changed = true; }
        var budget = settings.OnionGilBudget;
        if (ImGui.InputInt("Total gil limit per onion purchase (including tax)", ref budget))
        { settings.OnionGilBudget = Math.Max(0, budget); changed = true; }
        ImGui.TextWrapped("Buys one onion on the current world only when your own rank 10-19 stabled chocobo is capped and no onion is in inventory. Both limits must be positive. Requires Emptor API 5; using the onion remains manual. When buying is disabled, the existing free quest-reward acquisition remains available.");
        if (changed) manager.SaveCurrentAccount();
    }

    private bool DrawChocoboStablesSettings(Models.CharacterConfig cc, bool currentCharacter)
    {
        var changed = false;
        var enabled = cc.EnableChocoboStables;
        if (ImGui.Checkbox("Chocobo Stables", ref enabled)) { cc.EnableChocoboStables = enabled; changed = true; }
        ImGui.Indent();
        var settings = cc.ChocoboStablesSettings;
        static string DestinationLabel(Models.StableDestination value) => value switch
        {
            Models.StableDestination.SharedEstate1 => "Shared Estate 1",
            Models.StableDestination.SharedEstate2 => "Shared Estate 2",
            Models.StableDestination.SharedEstate3 => "Shared Estate 3",
            Models.StableDestination.PersonalEstate => "Personal Estate",
            Models.StableDestination.Apartment => "Apartment",
            _ => "FC Estate",
        };
        if (ImGui.BeginCombo("Stable destination", DestinationLabel(settings.Destination)))
        {
            foreach (var value in Enum.GetValues<Models.StableDestination>())
                if (ImGui.Selectable(DestinationLabel(value), value == settings.Destination))
                {
                    settings.Destination = value; settings.OtherOwner = settings.OtherChocobo = string.Empty;
                    cc.ChocoboStablesNextTrainingUtc = DateTime.MinValue; changed = true;
                }
            ImGui.EndCombo();
        }
        if (currentCharacter)
        {
            var fc = ChocoboStablesService.HasFreeCompany();
            ImGui.TextDisabled(fc == true ? "FC membership: member" : fc == false ? "FC membership: no FC" : "FC membership: unavailable");
        }
        if (ImGui.BeginCombo("Chocobo to train", settings.Target == Models.StableTarget.OwnChocobo ? "Own chocobo" : "Specific other chocobo"))
        {
            foreach (var target in Enum.GetValues<Models.StableTarget>())
                if (ImGui.Selectable(target == Models.StableTarget.OwnChocobo ? "Own chocobo" : "Specific other chocobo", settings.Target == target))
                { settings.Target = target; cc.ChocoboStablesNextTrainingUtc = DateTime.MinValue; changed = true; }
            ImGui.EndCombo();
        }
        var clean = settings.CleanStable;
        if (ImGui.Checkbox("Clean stable when needed", ref clean)) { settings.CleanStable = clean; changed = true; }
        if (currentCharacter)
        {
            var service = plugin.ChocoboStablesService;
            var blocker = service.GetStartBlockedReason(settings, true);
            ImGui.BeginDisabled(IsEquipmentAutomationBusy() || plugin.Engine.IsRunning || blocker != null);
            if (ImGui.SmallButton("Scan selected stable##StableRoster")) plugin.Engine.ManualStartChocoboStables(true);
            ImGui.EndDisabled();
            if (blocker != null) ImGui.TextWrapped(blocker);
            var roster = service.GetRoster(settings.Destination);
            if (settings.Target == Models.StableTarget.SpecificOther)
            {
                var preview = string.IsNullOrEmpty(settings.OtherChocobo) ? "Scan first, then select a chocobo" : $"{settings.OtherChocobo} ({settings.OtherOwner})";
                if (ImGui.BeginCombo("Scanned other chocobo", preview))
                {
                    var index = 0;
                    foreach (var bird in roster.Where(b => b.Owner != Plugin.ObjectTable.LocalPlayer?.Name.TextValue))
                    {
                        if (ImGui.Selectable($"{bird.Name} ({bird.Owner}) - {bird.Progression}; {bird.Training}##StableBird{index++}", bird.Matches(settings.OtherOwner, settings.OtherChocobo)))
                        {
                            settings.OtherOwner = bird.Owner; settings.OtherChocobo = bird.Name;
                            cc.ChocoboStablesNextTrainingUtc = DateTime.MinValue; changed = true;
                        }
                    }
                    ImGui.EndCombo();
                }
            }
            ImGui.TextWrapped(service.StatusText);
            ImGui.TextDisabled($"Cleanliness: {service.Cleanliness}; Krakka Root: {service.FeedStock}; Brooms: {service.BroomStock}; Onions: {service.OnionStock}");
            foreach (var bird in roster)
                ImGui.TextWrapped($"{bird.Name} ({bird.Owner}): {bird.Progression}; training {bird.Training}");
        }
        else ImGui.TextDisabled("Load this character to scan its selected estate and select another chocobo.");
        ImGui.TextWrapped("Enabled stable feeding also starts automatically when the logged-in character is idle and training is due. Uses one Krakka Root (8165) per training. Optional cleaning uses an inventory Magicked Stable Broom (8168); without one, the visit continues without cleaning or purchasing supplies. If your own rank 10-19 chocobo is capped and no Thavnairian Onion is in inventory, the Marketboard tab can enable a bounded Emptor purchase. Otherwise, Wiggly Quest can acquire an unfinished free-onion reward and its sidequest prerequisites. Stops after one onion is available; using it remains manual. Quest acquisition requires the appropriate job, level, MSQ progress and Wiggly routes.");
        ImGui.Unindent();
        return changed;
    }

    private static bool DrawCustomDeliveriesSettings(CustomDeliveriesSettings settings)
    {
        var changed = false;
        string PolicyLabel(DeliveryNpcPolicy policy) => policy == DeliveryNpcPolicy.BonusesOnly ? "Bonuses only" : "Closest to 150";
        if (ImGui.BeginCombo("NPC policy##CustomDeliveries", PolicyLabel(settings.NpcPolicy)))
        {
            foreach (var policy in Enum.GetValues<DeliveryNpcPolicy>())
                if (ImGui.Selectable(PolicyLabel(policy), settings.NpcPolicy == policy))
                {
                    settings.NpcPolicy = policy;
                    changed = true;
                }
            ImGui.EndCombo();
        }
        ImGui.TextWrapped(settings.NpcPolicy == DeliveryNpcPolicy.ClosestTo150
            ? "Uses verified achievement progress below 150, preferring the closest NPC and stopping at 150. Unknown progress stays excluded."
            : "Uses only delivery types that offer a bonus. Non-bonus routes are excluded.");

        ImGui.Text("Allowed delivery types");
        foreach (var type in new[] { DeliveryTypes.Crafting, DeliveryTypes.Mining, DeliveryTypes.Botany, DeliveryTypes.Fishing })
        {
            var allowed = settings.AllowedTypes.HasFlag(type);
            if (ImGui.Checkbox($"{type}##DeliveryType", ref allowed))
            {
                settings.AllowedTypes = allowed ? settings.AllowedTypes | type : settings.AllowedTypes & ~type;
                changed = true;
            }
            if (type != DeliveryTypes.Fishing) ImGui.SameLine();
        }
        ImGui.TextDisabled("Equal routes prefer crafting, mining, botany, then fishing.");

        ImGui.Text("Eligible crafting jobs");
        for (uint jobId = 8; jobId <= 15; jobId++)
        {
            var job = Plugin.DataManager.GetExcelSheet<ClassJob>().GetRow(jobId);
            var selected = settings.EligibleCraftJobs.Contains(jobId);
            if (ImGui.Checkbox($"{job.Abbreviation.ExtractText()}##DeliveryJob{jobId}", ref selected))
            {
                if (selected) settings.EligibleCraftJobs.Add(jobId); else settings.EligibleCraftJobs.Remove(jobId);
                changed = true;
            }
            if (jobId != 11 && jobId != 15) ImGui.SameLine();
        }
        if (ImGui.BeginCombo("Crafting job selection##CustomDeliveries", settings.CraftJobType.ToString()))
        {
            foreach (var choice in Enum.GetValues<DeliveryJobChoice>())
                if (ImGui.Selectable(choice.ToString(), settings.CraftJobType == choice))
                {
                    settings.CraftJobType = choice;
                    changed = true;
                }
            ImGui.EndCombo();
        }
        if (settings.CraftJobType == DeliveryJobChoice.Specific &&
            ImGui.BeginCombo("Preferred crafting job##CustomDeliveries", Plugin.DataManager.GetExcelSheet<ClassJob>().GetRow(settings.SelectedCraftJob).Abbreviation.ExtractText()))
        {
            for (uint jobId = 8; jobId <= 15; jobId++)
            {
                var job = Plugin.DataManager.GetExcelSheet<ClassJob>().GetRow(jobId);
                if (ImGui.Selectable(job.Name.ExtractText(), settings.SelectedCraftJob == jobId))
                {
                    settings.SelectedCraftJob = jobId;
                    changed = true;
                }
            }
            ImGui.EndCombo();
        }

        ImGui.Text("Eligible gathering jobs");
        foreach (uint jobId in new uint[] { 16, 17 })
        {
            var selected = settings.EligibleGatherJobs.Contains(jobId);
            if (ImGui.Checkbox($"{Plugin.DataManager.GetExcelSheet<ClassJob>().GetRow(jobId).Name.ExtractText()}##DeliveryJob{jobId}", ref selected))
            {
                if (selected) settings.EligibleGatherJobs.Add(jobId); else settings.EligibleGatherJobs.Remove(jobId);
                changed = true;
            }
            if (jobId == 16) ImGui.SameLine();
        }
        if (ImGui.BeginCombo("Preferred gathering job##CustomDeliveries", Plugin.DataManager.GetExcelSheet<ClassJob>().GetRow(settings.SelectedGatherJob).Name.ExtractText()))
        {
            foreach (uint jobId in new uint[] { 16, 17 })
                if (ImGui.Selectable(Plugin.DataManager.GetExcelSheet<ClassJob>().GetRow(jobId).Name.ExtractText(), settings.SelectedGatherJob == jobId))
                {
                    settings.SelectedGatherJob = jobId;
                    changed = true;
                }
            ImGui.EndCombo();
        }

        var baitId = (int)Math.Min(settings.FishingBaitId, int.MaxValue);
        ImGui.SetNextItemWidth(GetCompactNumericInputWidth() * 2f);
        if (ImGui.InputInt("Fishing bait item ID##CustomDeliveries", ref baitId))
        {
            settings.FishingBaitId = (uint)Math.Max(0, baitId);
            changed = true;
        }
        changed |= ImGui.InputText("AutoHook preset##CustomDeliveries", ref settings.FishingPresetName, 128);
        ImGui.TextWrapped("Default bait: Versatile Lure (29717). Entering 0 resets to 29717. A blank preset creates an AutoHook preset for the requested collectible; a named preset uses your existing setup. Fishing requires an eligible fisher gearset, bait and a valid fishing position.");
        changed |= ImGui.Checkbox("Fetch achievement progress automatically##CustomDeliveries", ref settings.AutoFetchAchievements);
        changed |= ImGui.Checkbox("Show overview when deliveries are incomplete##CustomDeliveries", ref settings.AutoShowIfIncomplete);
        changed |= ImGui.Checkbox("Show delivery debug details##CustomDeliveries", ref settings.ShowDebugUI);
        return changed;
    }

    private void DrawDadSelectionSelector(CharacterConfig cc, ref bool changed)
    {
        var catalog = plugin.DadIPCClient.GetSelectionCatalog();
        var all = catalog.Presets.Concat(catalog.Schedules).ToList();
        var selected = all.FirstOrDefault(item =>
            item.Kind == cc.NagYourDadSelectionKind &&
            string.Equals(item.Id, cc.NagYourDadSelectionId, StringComparison.OrdinalIgnoreCase));
        var fallback = string.IsNullOrWhiteSpace(cc.NagYourDadSelectionDisplayName)
            ? string.IsNullOrWhiteSpace(cc.NagYourDadSelectionId)
                ? "Select DAD preset or schedule"
                : cc.NagYourDadSelectionId
            : cc.NagYourDadSelectionDisplayName;
        var preview = selected == null
            ? cc.NagYourDadSelectionKind == DadSelectionKind.None ? fallback : $"{cc.NagYourDadSelectionKind}: {fallback}"
            : $"{selected.Kind}: {selected.DisplayName}";

        ImGui.SetNextItemWidth(460f);
        if (ImGui.BeginCombo("DAD Preset or Schedule", preview))
        {
            if (ImGui.Selectable("None", cc.NagYourDadSelectionKind == DadSelectionKind.None))
            {
                cc.NagYourDadSelectionKind = DadSelectionKind.None;
                cc.NagYourDadSelectionId = string.Empty;
                cc.NagYourDadSelectionDisplayName = string.Empty;
                changed = true;
            }

            DrawDadSelectionGroup("Presets", catalog.Presets, cc, ref changed);
            DrawDadSelectionGroup("Schedules", catalog.Schedules, cc, ref changed);
            ImGui.EndCombo();
        }

        if (!catalog.Available)
            ImGui.TextDisabled(catalog.Summary);
        else if (selected == null && cc.NagYourDadSelectionKind != DadSelectionKind.None)
            ImGui.TextDisabled($"Saved selection is not currently present in DAD; retaining fallback '{fallback}' without guessing a migration.");
        else
            ImGui.TextDisabled(catalog.Summary);
    }

    private static void DrawDadSelectionGroup(
        string label,
        IReadOnlyList<DadSelectionCatalogItem> items,
        CharacterConfig cc,
        ref bool changed)
    {
        ImGui.Separator();
        ImGui.TextDisabled(label);
        foreach (var item in items)
        {
            var isSelected = cc.NagYourDadSelectionKind == item.Kind &&
                             string.Equals(cc.NagYourDadSelectionId, item.Id, StringComparison.OrdinalIgnoreCase);
            if (ImGui.Selectable($"{item.DisplayName}##dad-selection-{item.Kind}-{item.Id}", isSelected))
            {
                cc.NagYourDadSelectionKind = item.Kind;
                cc.NagYourDadSelectionId = item.Id;
                cc.NagYourDadSelectionDisplayName = item.DisplayName;
                changed = true;
            }
            if (isSelected)
                ImGui.SetItemDefaultFocus();
        }
        if (items.Count == 0)
            ImGui.TextDisabled($"No DAD {label.ToLowerInvariant()} available.");
    }

    private static bool ShouldDrawLegacyDadTaskBuilder() => false;

    private void DrawDadDutySelector(CharacterConfig cc, ref bool changed)
    {
        LoadDadDutyOptions();

        var selected = dadDutyOptions.FirstOrDefault(option => option.Id == cc.NagYourDadDungeonContentFinderConditionId);
        var selectedLabel = selected?.DisplayName ?? "Select Duty Finder duty";

        ImGui.SetNextItemWidth(420f);
        if (!ImGui.BeginCombo(UIConstants.ConfigLabels.NagYourDadDungeonName, selectedLabel))
            return;

        ImGui.Text("Search:");
        ImGui.SetNextItemWidth(390f);
        ImGui.InputText("##DadDutySearch", ref dadDungeonSearch, 80);
        ImGui.Separator();

        if (dadDutyOptions.Count == 0)
        {
            ImGui.TextDisabled("Duty Finder list unavailable from Lumina.");
            ImGui.EndCombo();
            return;
        }

        var filter = dadDungeonSearch.Trim();
        var shown = 0;
        foreach (var option in dadDutyOptions)
        {
            if (!string.IsNullOrWhiteSpace(filter) &&
                option.SearchText.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            var isSelected = option.Id == cc.NagYourDadDungeonContentFinderConditionId;
            if (ImGui.Selectable(option.DisplayName, isSelected))
            {
                cc.NagYourDadDungeonContentFinderConditionId = option.Id;
                cc.NagYourDadDungeonName = option.Name;
                changed = true;
            }

            if (isSelected)
                ImGui.SetItemDefaultFocus();

            shown++;
            if (shown >= 40)
                break;
        }

        if (shown == 0)
            ImGui.TextDisabled("No matching Duty Finder duties.");

        ImGui.EndCombo();
    }

    private void DrawDadLanPartyPresetSelector(CharacterConfig cc, ref bool changed)
    {
        var presets = GetDadLanPartyPresetOptions(cc.NagYourDadLanPartyPreset);
        var presetIndex = DadRunRequestOptions.GetLanPartyPresetIndex(cc.NagYourDadLanPartyPreset, presets);
        if (ImGui.Combo(UIConstants.ConfigLabels.NagYourDadLanPartyPreset, ref presetIndex, presets, presets.Length))
        {
            cc.NagYourDadLanPartyPreset = presets[presetIndex];
            changed = true;
        }
    }

    private string[] GetDadLanPartyPresetOptions(string selectedPreset)
    {
        if (DateTime.UtcNow - dadLanPartyPresetRefreshUtc > TimeSpan.FromSeconds(10))
        {
            dadLanPartyPresetRefreshUtc = DateTime.UtcNow;
            var dadPresets = plugin.DadIPCClient.GetLanPartyPresets();
            if (dadPresets.Length > 0)
                dadLanPartyPresetOptions = dadPresets;
        }

        if (string.IsNullOrWhiteSpace(selectedPreset) ||
            dadLanPartyPresetOptions.Any(option => string.Equals(option, selectedPreset, StringComparison.OrdinalIgnoreCase)))
        {
            return dadLanPartyPresetOptions;
        }

        return dadLanPartyPresetOptions.Concat([selectedPreset]).ToArray();
    }

    private void LoadDadDutyOptions()
    {
        if (dadDutyOptionsLoaded)
            return;

        dadDutyOptionsLoaded = true;
        dadDutyOptions.Clear();

        try
        {
            var sheet = Plugin.DataManager.GetExcelSheet<ContentFinderCondition>(ClientLanguage.English);
            if (sheet is null)
                return;

            foreach (var row in sheet)
            {
                if (row.RowId == 0 || row.ContentType.ValueNullable is null || row.TerritoryType.ValueNullable is null)
                    continue;

                var name = CleanLuminaText(row.Name.ToString()).Trim();
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var contentType = CleanLuminaText(row.ContentType.Value.Name.ToString()).Trim();
                dadDutyOptions.Add(new DadDutyOption
                {
                    Id = row.RowId,
                    Name = name,
                    ContentType = contentType,
                });
            }

            dadDutyOptions.Sort((left, right) =>
            {
                var nameComparison = string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
                return nameComparison != 0 ? nameComparison : left.Id.CompareTo(right.Id);
            });
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning($"[Config] Failed to load dad Duty Finder list: {ex.Message}");
        }
    }

    private string GetAccountDisplayName(ConfigManager configManager, string accountId)
    {
        if (string.IsNullOrWhiteSpace(accountId))
            return "No account selected";
        if (!configManager.Accounts.TryGetValue(accountId, out var acc))
            return accountId;

        var alias = acc.AccountAlias;
        if (plugin.Configuration.KrangleEnabled && !string.IsNullOrEmpty(alias))
            alias = KrangleService.KrangleName(CleanLuminaText(alias));

        return string.IsNullOrWhiteSpace(alias) ? accountId : $"{alias} ({accountId})";
    }

    private static bool DrawIconInputs(string label, ref string icon, string defaultIcon)
    {
        var changed = false;

        var tempIcon = icon;
        ImGui.SetNextItemWidth(80);
        if (ImGui.InputText($"##{label}Icon", ref tempIcon, 10))
        {
            icon = tempIcon;
            changed = true;
        }
        ImGui.SameLine();
        if (ImGui.Button($"Reset##{label}Reset"))
        {
            icon = defaultIcon;
            changed = true;
        }
        ImGui.SameLine();
        ImGui.TextDisabled($"({defaultIcon})");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Default icon. Enter Unicode like \\uE03C or paste glyphs directly");

        // Add code field next to symbol field
        ImGui.SameLine();
        ImGui.Text("Code:");
        ImGui.SameLine();
        var iconCode = GetUnicodeCode(icon);
        ImGui.SetNextItemWidth(60);
        if (ImGui.InputText($"##{label}Code", ref iconCode, 10))
        {
            // Convert code back to Unicode character
            if (iconCode.StartsWith("\\u") && iconCode.Length >= 6)
            {
                try
                {
                    var code = Convert.ToInt32(iconCode.Substring(2), 16);
                    icon = char.ConvertFromUtf32(code);
                    changed = true;
                }
                catch
                {
                    // Invalid code, keep original
                }
            }
        }

        return changed;
    }

    private bool BeginConfigurationSection(string label, ConfigurationSection section)
    {
        if (selectedConfigurationSection != section)
            return false;

        return true;
    }

    private static void DrawHelpMarker(string tooltip)
    {
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(tooltip);
    }

    private static string GetUnicodeCode(string icon)
    {
        if (string.IsNullOrEmpty(icon) || icon.Length != 1)
            return "\\uE03C";

        var code = (int)icon[0];
        return $"\\u{code:X4}";
    }

    private static void DrawWeeklyTaskHint(DateTime lastCompleted, DateTime nextReset, string pendingText)
    {
        if (ResetDetectionService.TaskIsCompleted(lastCompleted, nextReset))
        {
            ImGui.TextDisabled($"Completed until {FormatUtc(nextReset)}");
        }
        else
        {
            ImGui.TextDisabled(pendingText);
        }
    }

    private bool DrawLootGoblinMapDropdown(CharacterConfig config)
    {
        var maps = plugin.LootGoblinIPCClient.GetGatherableMaps();
        var selected = maps.FirstOrDefault(map => map.ItemId == config.LootGoblinMapGatherItemId);
        var preview = selected?.DisplayName ?? $"Map {config.LootGoblinMapGatherItemId}";
        var changed = false;

        ImGui.Text("Map:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(320f);
        if (ImGui.BeginCombo("##LootGoblinMapGatherItemId", preview))
        {
            foreach (var map in maps)
            {
                var isSelected = map.ItemId == config.LootGoblinMapGatherItemId;
                if (ImGui.Selectable(map.DisplayName, isSelected))
                {
                    config.LootGoblinMapGatherItemId = map.ItemId;
                    changed = true;
                }

                if (isSelected)
                    ImGui.SetItemDefaultFocus();
            }

            if (maps.Count == 0)
                ImGui.TextDisabled("LootGoblin IPC unavailable or no gatherable maps exported.");

            ImGui.EndCombo();
        }

        return changed;
    }

    private bool IsSelectedLootGoblinMapSafe(CharacterConfig config)
    {
        var selected = plugin.LootGoblinIPCClient
            .GetGatherableMaps()
            .FirstOrDefault(map => map.ItemId == config.LootGoblinMapGatherItemId);

        return selected != null && LootGoblinMapSafetyPolicy.IsSoloOutdoorSafe(selected);
    }

    private static void DrawDailyTaskHint(DateTime lastCompleted, DateTime nextReset, string pendingText)
    {
        if (ResetDetectionService.TaskIsCompleted(lastCompleted, nextReset))
        {
            ImGui.TextDisabled($"Completed until {FormatUtc(nextReset)}");
        }
        else
        {
            ImGui.TextDisabled(pendingText);
        }
    }

    private static void DrawRefillFromListingsHint(CharacterConfig config)
    {
        switch (config.RefillFromListingsFrequency)
        {
            case RefillFromListingsFrequency.EveryAR:
                ImGui.TextDisabled("Runs every AutoRetainer/manual VERMAXION run through the selected Lifestream bell route.");
                return;

            case RefillFromListingsFrequency.Monthly:
                if (IsRefillFromListingsMonthlyComplete(config))
                    ImGui.TextDisabled($"Completed until {FormatUtc(config.RefillFromListingsNextReset)}");
                else
                    ImGui.TextDisabled("Runs once per UTC calendar month through the selected Lifestream bell route.");
                return;

            case RefillFromListingsFrequency.Daily:
                DrawDailyTaskHint(config.RefillFromListingsLastCompleted, config.RefillFromListingsNextReset, "Runs once per daily reset through the selected Lifestream bell route.");
                return;

            case RefillFromListingsFrequency.Weekly:
            default:
                DrawWeeklyTaskHint(config.RefillFromListingsLastCompleted, config.RefillFromListingsNextReset, "Runs once per weekly reset through the selected Lifestream bell route.");
                return;
        }
    }

    private static void DrawFCBuffHint(CharacterConfig config)
    {
        switch (config.FCBuffFrequency)
        {
            case FCBuffFrequency.EveryAR:
                ImGui.TextDisabled("Runs every automatic AR post-process. Run now always bypasses cadence.");
                return;
            case FCBuffFrequency.Daily:
                DrawDailyTaskHint(config.FCBuffLastCompleted, config.FCBuffNextReset, "Runs once per daily reset.");
                return;
            case FCBuffFrequency.Weekly:
                DrawWeeklyTaskHint(config.FCBuffLastCompleted, config.FCBuffNextReset, "Runs once per Tuesday weekly reset.");
                return;
            case FCBuffFrequency.Monthly:
            default:
                if (ResetDetectionService.TaskIsCompleted(config.FCBuffLastCompleted, config.FCBuffNextReset))
                    ImGui.TextDisabled($"Completed until {FormatUtc(config.FCBuffNextReset)}");
                else
                    ImGui.TextDisabled("Runs once per UTC calendar month.");
                return;
        }
    }

    private static bool IsRefillFromListingsMonthlyComplete(CharacterConfig config)
    {
        if (config.RefillFromListingsLastCompleted == DateTime.MinValue)
            return false;

        var now = DateTime.UtcNow;
        var lastCompleted = config.RefillFromListingsLastCompleted.ToUniversalTime();
        if (lastCompleted.Year != now.Year || lastCompleted.Month != now.Month)
            return false;

        return config.RefillFromListingsNextReset == DateTime.MinValue || now < config.RefillFromListingsNextReset.ToUniversalTime();
    }

    private static float GetCompactNumericInputWidth()
        => Math.Max(72f, ImGui.CalcTextSize("00000").X + (ImGui.GetStyle().FramePadding.X * 2f) + 18f);

    private void DrawChokeAboTargetStatus()
    {
        var contentId = Plugin.PlayerState.ContentId;
        var now = DateTime.UtcNow;
        var config = plugin.ConfigManager.GetActiveConfig();
        var allowance = ChocoboDailyAllowance.Remaining(config, now);
        ImGui.Separator();
        ImGui.Text($"Daily racing allowance: {TimeSpan.FromSeconds(allowance):hh\\:mm\\:ss} of 03:00:00 remaining");
        ImGui.ProgressBar((float)(allowance / ChocoboDailyAllowance.LimitSeconds), new Vector2(-1, 0), "Queue and racing time only");
        ImGui.TextDisabled($"Resets {ChocoboDailyAllowance.ResetAt(now).AddDays(1).ToLocalTime():ddd, MMM d HH:mm} local (09:00 UTC). Covering waits do not consume racing time.");
        ImGui.Text(config.ChocoboProgressionPaused ? "Progression paused" : "Progression enabled");
        ImGui.BeginDisabled(config.ChocoboBreedingGoal != ChocoboBreedingGoal.ReachPedigree && !plugin.ChokeAboIpcClient.IsWorkflowAvailable);
        if (ImGui.Button("Resume##ChocoboProgression"))
            plugin.RunDashboardAction(() => plugin.ChocoboRaceService.ResumeProgression());
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Pause##ChocoboProgression")) plugin.ChocoboRaceService.PauseProgression();
        ImGui.SameLine();
        if (ImGui.Button("Stop##ChocoboProgression")) plugin.ChocoboRaceService.PauseProgression();
        if (config.ChocoboBreedingGoal != ChocoboBreedingGoal.ReachPedigree)
        {
            ImGui.TextWrapped("Matching offspring stay unregistered. A matching G9 racer retires at rank 40 to become breeding stock.");
            ImGui.BeginDisabled(!plugin.ChokeAboIpcClient.IsWorkflowAvailable ||
                !chokeAboTargetStatus.HasValue || chokeAboTargetStatus.Value.Status?.ProductionComplete != true);
            if (ImGui.Button("Start new batch##ChocoboProduction"))
                plugin.RunDashboardAction(() => plugin.ChocoboRaceService.ResumeProgression(startNewBatch: true));
            ImGui.EndDisabled();
        }
        if (contentId != chokeAboTargetStatusContentId || now >= chokeAboTargetStatusNextRefreshUtc)
        {
            chokeAboTargetStatusContentId = contentId;
            chokeAboTargetStatus = plugin.ChokeAboIpcClient.GetTargetCycleStatus(contentId);
            chokeAboTargetStatusNextRefreshUtc = now.AddSeconds(1);
        }

        if (!chokeAboTargetStatus.HasValue)
        {
            ImGui.TextColored(new Vector4(1f, 0.55f, 0.2f, 1f), "Breeding status unavailable. Enable Choke-abo to continue.");
            return;
        }

        var result = chokeAboTargetStatus.Value;
        if (!result.Succeeded || result.Status == null)
        {
            ImGui.TextColored(
                new Vector4(1f, 0.55f, 0.2f, 1f),
                $"Breeding status unavailable: {result.Error}");
            return;
        }

        var status = result.Status;
        if (status.OffspringGoal != ChocoboBreedingGoal.ReachPedigree)
        {
            ImGui.TextUnformatted(status.OffspringGoal == ChocoboBreedingGoal.AbilityOffspring ? "Offspring printer" : "Colour seeker");
            ImGui.Text($"Matching G9 offspring: {status.MatchingOffspringProduced:N0} / {status.MatchingOffspringRequested:N0}");
            ImGui.ProgressBar(status.MatchingOffspringRequested > 0
                ? Math.Clamp((float)status.MatchingOffspringProduced / status.MatchingOffspringRequested, 0, 1) : 0,
                new Vector2(-1, 0), "Matching offspring retained unregistered");
            if (status.ProductionComplete)
                ImGui.TextColored(new Vector4(0.4f, 1f, 0.6f, 1f), "Offspring quantity reached. Use Start new batch to repeat this goal.");
        }
        ImGui.TextUnformatted(status.RacingRank > 0 ? $"Registered pedigree: G{status.Pedigree}    Racing rank: {status.RacingRank}/50"
            : status.RacerDataAvailable ? "No registered racing chocobo." : "Registered racer data is unavailable.");
        if (status.RacingRank > 0 && status.RacerDataAvailable)
        {
            DrawChocoboAbility("Inherited ability", status.InheritedAbilityId);
            DrawChocoboAbility("Learned ability", status.LearnedAbilityId);
            if (Plugin.DataManager.GetExcelSheet<Stain>().TryGetRow(status.ColourId, out var colour))
            {
                DrawChocoboColour(colour, "CurrentRacer");
                ImGui.SameLine();
                ImGui.TextUnformatted($"Colour: {colour.Name.ExtractText()}");
            }
        }
        else if (status.RacingRank > 0)
            ImGui.TextDisabled("Racer ability and colour data are unavailable.");
        ImGui.TextWrapped($"Next action: {status.Reason}");
        if (status.ProgressionComplete)
            ImGui.TextColored(new Vector4(0.4f, 1f, 0.6f, 1f), "Pedigree and racing-rank goal reached. Racer retained.");
        if (status.NextCoveringEligibilityUtc.HasValue)
        {
            var ready = status.NextCoveringEligibilityUtc.Value;
            var remaining = ready - DateTimeOffset.UtcNow;
            ImGui.TextWrapped(remaining > TimeSpan.Zero
                ? $"Collection ready in {remaining:hh\\:mm\\:ss} - {ready.ToLocalTime():ddd, MMM d HH:mm} local"
                : "Covering is ready for collection.");
        }
    }

    private void DrawChocoboGoalSettings(CharacterConfig config, bool isCurrentCharacter, ref bool changed)
    {
        var goal = (int)config.ChocoboBreedingGoal;
        ImGui.SetNextItemWidth(280);
        if (ImGui.Combo("Breeding goal", ref goal, "Reach pedigree and racing rank 50\0Offspring printer - inherited ability\0Colour seeker\0"))
        {
            if (isCurrentCharacter)
                plugin.ChocoboRaceService.PauseProgression();
            config.ChocoboBreedingGoal = (ChocoboBreedingGoal)goal;
            config.ChocoboProgressionPaused = true;
            if (config.ChocoboBreedingGoal != ChocoboBreedingGoal.ReachPedigree)
            {
                config.ChocoboTargetPedigree = 9;
                config.ChocoboProduceCounterpart = false;
            }
            changed = true;
        }
        if (config.ChocoboBreedingGoal == ChocoboBreedingGoal.ReachPedigree)
            return;
        if (!Enum.IsDefined(config.ChocoboBreedingGoal))
        {
            ImGui.TextColored(new Vector4(1f, 0.55f, 0.2f, 1f), "Choose a valid breeding goal before continuing.");
            return;
        }

        if (!chocoboGoalOptionsLoaded)
        {
            chocoboAbilityOptions.AddRange(Plugin.DataManager.GetExcelSheet<ChocoboRaceAbility>()
                .Where(ability => ability.RowId > 0 && ability.RowId <= byte.MaxValue && !string.IsNullOrWhiteSpace(ability.Name.ExtractText()))
                .OrderBy(ability => ability.Name.ExtractText()));
            chocoboColourOptions.AddRange(Plugin.DataManager.GetExcelSheet<Stain>()
                .Where(colour => colour.RowId > 0 && colour.RowId <= byte.MaxValue && !colour.IsMetallic && !string.IsNullOrWhiteSpace(colour.Name.ExtractText()))
                .OrderBy(colour => colour.Shade).ThenBy(colour => colour.SubOrder));
            chocoboGoalOptionsLoaded = true;
        }
        ImGui.TextWrapped("Produce pedigree-9 offspring. Only confirmed matches count toward the requested quantity; a matching parent must be available.");
        if (config.ChocoboBreedingGoal == ChocoboBreedingGoal.AbilityOffspring)
        {
            var preview = chocoboAbilityOptions.Where(ability => ability.RowId == config.ChocoboDesiredInheritedAbilityId)
                .Select(ability => ability.Name.ExtractText()).FirstOrDefault() ?? "Choose an inherited ability";
            ImGui.SetNextItemWidth(280);
            if (ImGui.BeginCombo("Desired inherited ability", preview))
            {
                ImGui.SetNextItemWidth(280);
                ImGui.InputTextWithHint("##ChocoboAbilitySearch", "Find an inherited ability", ref chocoboAbilitySearch, 80);
                foreach (var ability in chocoboAbilityOptions)
                {
                    if (!ability.Name.ExtractText().Contains(chocoboAbilitySearch, StringComparison.OrdinalIgnoreCase))
                        continue;
                    ImGui.PushID((int)ability.RowId);
                    DrawChocoboGameIcon(ability.Icon);
                    ImGui.SameLine();
                    if (ImGui.Selectable(ability.Name.ExtractText(), ability.RowId == config.ChocoboDesiredInheritedAbilityId))
                    {
                        config.ChocoboDesiredInheritedAbilityId = ability.RowId;
                        changed = true;
                    }
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip(ability.Description.ExtractText());
                    ImGui.PopID();
                }
                ImGui.EndCombo();
            }
            var quantity = config.ChocoboDesiredAbilityOffspringCount;
            ImGui.SetNextItemWidth(120);
            if (ImGui.InputInt("Matching offspring to produce##Ability", ref quantity))
            {
                config.ChocoboDesiredAbilityOffspringCount = Math.Max(1, quantity);
                changed = true;
            }
            ImGui.TextDisabled("The inherited ability is the breeding target. A learned ability alone does not count.");
            if (config.ChocoboDesiredInheritedAbilityId != 0)
                ImGui.TextWrapped($"Printer target: {config.ChocoboDesiredAbilityOffspringCount:N0} pedigree-9 offspring with {preview} as their inherited ability.");
            else
                ImGui.TextColored(new Vector4(1f, 0.7f, 0.3f, 1f), "Choose the inherited ability to print.");
        }
        else
        {
            var quantity = config.ChocoboDesiredColourOffspringCount;
            ImGui.SetNextItemWidth(120);
            if (ImGui.InputInt("Matching offspring to produce##Colour", ref quantity))
            {
                config.ChocoboDesiredColourOffspringCount = Math.Max(1, quantity);
                changed = true;
            }
            ImGui.TextWrapped("Accept any selected colour. The quantity is a total across the selected colours.");
            ImGui.SetNextItemWidth(280);
            ImGui.InputTextWithHint("##ChocoboColourSearch", "Find a colour", ref chocoboColourSearch, 80);
            if (ImGui.BeginChild("AcceptableChocoboColours", new Vector2(0, 190), true))
            {
                foreach (var colour in chocoboColourOptions)
                {
                    var name = colour.Name.ExtractText();
                    if (!name.Contains(chocoboColourSearch, StringComparison.OrdinalIgnoreCase))
                        continue;
                    ImGui.PushID((int)colour.RowId);
                    var selected = config.ChocoboAcceptableColourIds.Contains(colour.RowId);
                    if (ImGui.Checkbox("##Accept", ref selected))
                    {
                        if (selected) config.ChocoboAcceptableColourIds.Add(colour.RowId);
                        else config.ChocoboAcceptableColourIds.Remove(colour.RowId);
                        changed = true;
                    }
                    ImGui.SameLine();
                    DrawChocoboColour(colour, "Choice");
                    ImGui.SameLine();
                    ImGui.TextUnformatted(name);
                    ImGui.PopID();
                }
            }
            ImGui.EndChild();
            ImGui.Text($"Acceptable colours selected: {config.ChocoboAcceptableColourIds.Count}");
            var selectedNames = config.ChocoboAcceptableColourIds.Select(id =>
                chocoboColourOptions.Where(colour => colour.RowId == id).Select(colour => colour.Name.ExtractText())
                    .FirstOrDefault() ?? $"Unavailable colour ({id})");
            ImGui.TextWrapped($"Colour target: {config.ChocoboDesiredColourOffspringCount:N0} pedigree-9 offspring in any of the selected colours.");
            if (config.ChocoboAcceptableColourIds.Count > 0)
            {
                ImGui.TextWrapped(string.Join(", ", selectedNames));
                if (ImGui.SmallButton("Clear acceptable colours"))
                {
                    config.ChocoboAcceptableColourIds.Clear();
                    changed = true;
                }
            }
            else
                ImGui.TextColored(new Vector4(1f, 0.7f, 0.3f, 1f), "Select at least one acceptable colour.");
        }
        if (!plugin.ChokeAboIpcClient.IsWorkflowAvailable)
            ImGui.TextColored(new Vector4(1f, 0.7f, 0.3f, 1f), "Enable the current Choke-abo build to start or resume offspring production.");
        ImGui.TextWrapped("Each covering takes 24 hours. Matching offspring are retained unregistered; owned mode never buys a covering permit.");
        ImGui.TextWrapped("These goals are saved separately. Existing pedigree progression remains available; Pause and Stop preserve a pending covering.");
    }

    private static void DrawChocoboAbility(string label, uint abilityId)
    {
        if (abilityId != 0 && Plugin.DataManager.GetExcelSheet<ChocoboRaceAbility>().TryGetRow(abilityId, out var ability))
        {
            DrawChocoboGameIcon(ability.Icon);
            ImGui.SameLine();
            ImGui.TextUnformatted($"{label}: {ability.Name.ExtractText()}");
        }
        else
            ImGui.TextDisabled($"{label}: none");
    }

    private static void DrawChocoboGameIcon(uint iconId)
    {
        var texture = Plugin.TextureProvider.GetFromGameIcon(iconId).GetWrapOrDefault();
        if (texture != null)
            ImGui.Image(texture.Handle, new Vector2(24, 24));
    }

    private static void DrawChocoboColour(Stain colour, string id)
        => ImGui.ColorButton($"##ChocoboColour{id}", new Vector4(((colour.Color >> 16) & 255) / 255f, ((colour.Color >> 8) & 255) / 255f, (colour.Color & 255) / 255f, 1),
            ImGuiColorEditFlags.NoTooltip, new Vector2(22, 22));

    private T RunConfigMutationWithTargetPause<T>(Func<T> mutation, string reason)
    {
        var targetWasActive = IsCurrentTargetAutomationEnabled();
        var result = mutation();
        if (targetWasActive && !IsCurrentTargetAutomationEnabled())
            plugin.PauseCurrentTargetCycleBestEffort(reason, targetModeWasActive: true);
        return result;
    }

    private void RunConfigMutationWithTargetPause(System.Action mutation, string reason)
        => RunConfigMutationWithTargetPause(
            () =>
            {
                mutation();
                return true;
            },
            reason);

    private bool IsCurrentTargetAutomationEnabled()
    {
        var active = plugin.ConfigManager.GetActiveConfig();
        return active?.Enabled == true &&
               active.EnableChocoboRacing &&
               active.ChocoboAutomationMode == ChocoboAutomationMode.TargetPedigree;
    }

    private bool DrawResetButton(string id, System.Action reset)
    {
        ImGui.SameLine();
        if (!ImGui.SmallButton($"Reset##{id}"))
            return false;

        var scope = string.IsNullOrWhiteSpace(plugin.ConfigManager.SelectedCharacterKey)
            ? "the current Account default"
            : plugin.ConfigManager.SelectedCharacterKey;
        RequestConfirmation(
            "Reset saved task state?",
            $"Reset {id} state for {scope}? This affects only the named configuration scope.",
            () =>
            {
                reset();
                plugin.ConfigManager.SaveCurrentAccount();
            });
        return false;
    }

    private void RequestTaskStateReset(string label, string charKey, System.Action reset)
    {
        var scope = string.IsNullOrWhiteSpace(charKey) ? "the current Account default" : charKey;
        RequestConfirmation(
            "Reset saved task state?",
            $"Reset {label} for {scope}? This affects only the named configuration scope.",
            () =>
            {
                reset();
                plugin.ConfigManager.SaveCurrentAccount();
            });
    }

    private void DrawDefaultOverrideButton(
        bool isDefault,
        ConfigManager configManager,
        string id,
        string label,
        Action<CharacterConfig, CharacterConfig> copy)
    {
        var account = configManager.GetCurrentAccount();
        if (account == null)
            return;

        if (!isDefault)
        {
            var selected = configManager.GetSelectedConfig();
            var matches = SettingMatchesDefault(account.DefaultConfig, selected, copy);
            ImGui.SameLine();
            ImGui.TextDisabled(matches ? "Matches account default" : "Differs from account default");
            if (!matches)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton($"Use default##{id}"))
                {
                    void ApplyDefault() => RunConfigMutationWithTargetPause(
                        () =>
                        {
                            copy(account.DefaultConfig, selected);
                            configManager.SaveCurrentAccount();
                        },
                        $"account default changed {label}");
                    if (GetPvpEnableWarning(account.DefaultConfig, new[] { selected }, copy).Length > 0)
                        RequestConfirmation($"Use default for {label}?", string.Empty, ApplyDefault,
                            () => GetPvpEnableWarning(account.DefaultConfig, new[] { selected }, copy));
                    else
                        ApplyDefault();
                }
            }
            return;
        }

        var differing = account.Characters.Values.Count(character =>
            !SettingMatchesDefault(account.DefaultConfig, character, copy));
        ImGui.SameLine();
        ImGui.TextDisabled($"{differing} characters differ");
        ImGui.SameLine();
        ImGui.BeginDisabled(differing == 0);
        if (ImGui.SmallButton($"Apply to all##{id}"))
        {
            var count = RunConfigMutationWithTargetPause(
                () => configManager.ApplyDefaultSettingToAllCharacters(label, copy),
                $"account default {label} applied to all characters");
            Plugin.Log.Information($"[Config] Applied default {label} to {count} characters");
            Plugin.ChatGui.Print($"[Vermaxion] Default {label} applied to {count} characters.");
        }
        ImGui.EndDisabled();
    }

    private static bool SettingMatchesDefault(
        CharacterConfig defaultConfig,
        CharacterConfig target,
        Action<CharacterConfig, CharacterConfig> copy)
    {
        var projected = target.Clone();
        copy(defaultConfig, projected);
        return AccountConfigPersistence.AreEquivalent(target, projected);
    }

    private static bool SettingsMatchDefault(CharacterConfig defaultConfig, CharacterConfig target)
    {
        var projected = target.Clone();
        ConfigManager.CopyDefaultSettings(defaultConfig, projected);
        return AccountConfigPersistence.AreEquivalent(target, projected);
    }

    private static bool DrawJobCombo(string label, string value, bool includeCurrentJobOption, out string selectedJob)
    {
        selectedJob = NormalizeJobAbbreviation(value);
        var preview = selectedJob;
        if (string.IsNullOrWhiteSpace(preview))
            preview = includeCurrentJobOption ? "Current job" : "Select job";

        var changed = false;
        if (!ImGui.BeginCombo(label, preview))
            return false;

        if (includeCurrentJobOption)
        {
            var selected = string.IsNullOrWhiteSpace(selectedJob);
            if (ImGui.Selectable("Current job", selected))
            {
                selectedJob = string.Empty;
                changed = true;
            }

            if (selected)
                ImGui.SetItemDefaultFocus();

            ImGui.Separator();
        }

        foreach (var job in DadRunRequestOptions.JobHintExamples)
        {
            var normalizedJob = NormalizeJobAbbreviation(job);
            var selected = string.Equals(selectedJob, normalizedJob, StringComparison.Ordinal);
            if (ImGui.Selectable(normalizedJob, selected))
            {
                selectedJob = normalizedJob;
                changed = true;
            }

            if (selected)
                ImGui.SetItemDefaultFocus();
        }

        ImGui.EndCombo();
        return changed;
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

    private static string FormatAfterArParkDestination(AfterArParkDestination destination)
        => destination switch
        {
            AfterArParkDestination.Home => "Home (/li home)",
            AfterArParkDestination.Limsa => "Limsa (/li limsa)",
            AfterArParkDestination.FreeCompany => "Free Company (/li fc)",
            AfterArParkDestination.Inn => "Inn (/li inn)",
            AfterArParkDestination.Workshop => "Workshop (/li ws)",
            AfterArParkDestination.Custom => "Custom /li command",
            _ => "Invalid",
        };

    private static string FormatGearset(GearsetSnapshot gearset)
        => $"{gearset.GearsetId + 1}: {gearset.Name} (Lv. {gearset.Level})";

    private static string NormalizeJobAbbreviation(string value)
        => value?.Trim().ToUpperInvariant() ?? string.Empty;

    private static void DrawFashionTaskHint(DateTime lastCompleted, DateTime nextReset)
    {
        if (ResetDetectionService.TaskIsCompleted(lastCompleted, nextReset))
        {
            ImGui.TextDisabled($"Completed until {FormatUtc(nextReset)}");
            return;
        }

        var now = DateTime.UtcNow;
        if (ResetDetectionService.IsFashionReportAvailable(now))
        {
            ImGui.TextDisabled($"Ready now. Window closes at {FormatUtc(ResetDetectionService.GetCurrentFashionReportWindowEnd(now))}.");
        }
        else
        {
            ImGui.TextDisabled($"Available Friday at {FormatUtc(ResetDetectionService.GetNextFashionReportAvailability(now))}. Runs through weekly reset.");
        }
    }

    private static void DrawJumboTaskHint(DateTime lastCompleted, DateTime nextReset)
    {
        var dataCenterName = ResetDetectionService.GetCurrentCharacterJumboDataCenterName();
        if (ResetDetectionService.IsJumboPurchasePendingPayout(lastCompleted, nextReset))
        {
            ImGui.TextDisabled($"Ticket purchased. Payout opens for {dataCenterName} at {FormatUtc(nextReset)}.");
            return;
        }

        if (ResetDetectionService.TaskIsCompleted(lastCompleted, nextReset))
        {
            ImGui.TextDisabled($"Completed until {FormatUtc(nextReset)}");
            return;
        }

        var now = DateTime.UtcNow;
        if (ResetDetectionService.IsJumboCactpotPayoutAvailable(now))
        {
            ImGui.TextDisabled($"Ready to turn in now for {dataCenterName}. Weekly reset at {FormatUtc(ResetDetectionService.GetNextWeeklyReset(now))}.");
        }
        else
        {
            ImGui.TextDisabled($"Ready to purchase now. Payout opens for {dataCenterName} at {FormatUtc(ResetDetectionService.GetNextJumboCactpotPayoutAvailability(now))}.");
        }
    }

    private static string FormatUtc(DateTime timestamp)
    {
        return timestamp == DateTime.MinValue
            ? "unknown"
            : timestamp.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'");
    }

    private static string FormatJumboNumberMode(JumboCactpotNumberMode mode)
    {
        return mode switch
        {
            JumboCactpotNumberMode.Fixed => "Fixed",
            _ => "Random",
        };
    }

    private static string FormatFishingExecutionMode(FishingExecutionMode mode)
        => mode switch
        {
            FishingExecutionMode.AutoRetainerRelogCurrentAccount => "AR postprocess / current account relog",
            _ => "Current character only",
        };

    private static string FormatCharacterListSortMode(CharacterListSortMode mode)
        => mode switch
        {
            CharacterListSortMode.Name => "Name",
            CharacterListSortMode.Server => "Server",
            CharacterListSortMode.CreationDate => "Creation date",
            _ => mode.ToString(),
        };

    private static string FormatFishingReturnDestination(FishingReturnDestination destination)
        => destination switch
        {
            FishingReturnDestination.None => "None",
            FishingReturnDestination.Limsa => "Limsa",
            FishingReturnDestination.FreeCompany => "Free Company",
            FishingReturnDestination.Inn => "Inn",
            FishingReturnDestination.Custom => "Custom",
            _ => "Home",
        };

    private static string GetDefaultFishingReturnCommand(FishingReturnDestination destination)
        => destination switch
        {
            FishingReturnDestination.None => string.Empty,
            FishingReturnDestination.Limsa => "/li limsa",
            FishingReturnDestination.FreeCompany => "/li fc",
            FishingReturnDestination.Inn => "/li inn",
            FishingReturnDestination.Custom => string.Empty,
            _ => "/li home",
        };

    private static string FormatFishingRepairMode(FishingRepairMode mode)
        => mode switch
        {
            FishingRepairMode.Self => "ADS self repair",
            FishingRepairMode.NpcNoInn => "ADS NPC no-inn",
            FishingRepairMode.NpcNoTeleportNoInn => "ADS NPC no-teleport/no-inn",
            _ => "Disabled",
        };

    private void DrawFishingStockCatalogEditor()
    {
        var configuration = plugin.Configuration;
        var configManager = plugin.ConfigManager;
        var changed = false;

        ImGui.Spacing();
        ImGui.Text("Ordered fishing-stock catalog");
        ImGui.TextWrapped($"Sync target account: {GetAccountDisplayName(configManager, configManager.CurrentAccountId)}");
        ImGui.TextWrapped("Catalog defaults are global. Sync actions apply them to the selected account.");

        for (var index = 0; index < configuration.FishingStockCatalog.Count; index++)
        {
            var row = configuration.FishingStockCatalog[index];
            ImGui.PushID($"FishingCatalog_{row.ItemId}");

            if (ImGui.SmallButton("-"))
            {
                fishingCatalogRemoveItemId = row.ItemId;
                ImGui.OpenPopup("Remove fishing-stock item?");
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("+"))
            {
                pendingFishingCatalogRow = true;
                fishingCatalogSearch = string.Empty;
                focusFishingCatalogSearch = true;
            }
            ImGui.SameLine();
            ImGui.TextWrapped(GetItemName(row.ItemId));

            var target = row.DefaultTarget;
            ImGui.SetNextItemWidth(GetCompactNumericInputWidth());
            if (ImGui.InputInt("Default target", ref target))
            {
                row.DefaultTarget = Math.Max(0, target);
                changed = true;
            }
            var defaultMin = row.DefaultMin;
            ImGui.SetNextItemWidth(GetCompactNumericInputWidth());
            if (ImGui.InputInt("Default reorder point", ref defaultMin))
            {
                row.DefaultMin = Math.Max(0, defaultMin);
                changed = true;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Default reorder point (0 = buy whenever below target).");
            var enabled = row.DefaultEnabled;
            if (ImGui.Checkbox("Enabled by default", ref enabled))
            {
                row.DefaultEnabled = enabled;
                changed = true;
            }
            var currentAccount = configManager.GetCurrentAccount();
            var differingRecords = currentAccount == null
                ? 0
                : new[] { currentAccount.DefaultConfig }
                    .Concat(currentAccount.Characters.Values)
                    .Count(record => !FishingStockRowMatches(record, row));
            ImGui.BeginDisabled(currentAccount == null);
            if (ImGui.SmallButton($"Sync row ({differingRecords})"))
            {
                var count = configManager.SyncFishingStockRowToCurrentAccount(row);
                Plugin.ChatGui.Print($"[Vermaxion] {GetItemName(row.ItemId)} defaults synchronized to {count} current-account records.");
            }

            ImGui.EndDisabled();
            ImGui.TextWrapped($"Affected account: {GetAccountDisplayName(configManager, configManager.CurrentAccountId)}");
            ImGui.Separator();

            if (ImGui.BeginPopupModal("Remove fishing-stock item?", ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.TextWrapped($"Remove {GetItemName(fishingCatalogRemoveItemId)} from the global catalog?");
                ImGui.TextWrapped("This also purges its account-default and character values. Re-adding it starts clean.");
                if (ImGui.Button("Remove"))
                {
                    configManager.RemoveFishingStockCatalogEntry(configuration, fishingCatalogRemoveItemId);
                    fishingCatalogRemoveItemId = 0;
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel"))
                {
                    fishingCatalogRemoveItemId = 0;
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }

            ImGui.PopID();
        }

        if (pendingFishingCatalogRow)
        {
            ImGui.PushID("PendingFishingCatalogRow");
            ImGui.BeginDisabled();
            ImGui.SmallButton("-");
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.SmallButton("+"))
            {
                fishingCatalogSearch = string.Empty;
                focusFishingCatalogSearch = true;
            }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(Math.Max(80f, ImGui.GetContentRegionAvail().X));
            if (focusFishingCatalogSearch)
            {
                ImGui.SetKeyboardFocusHere();
                focusFishingCatalogSearch = false;
            }
            ImGui.InputTextWithHint("##ItemSearch", "Search for an item...", ref fishingCatalogSearch, 128);
            ImGui.TextDisabled("New row defaults: target 99, disabled.");

            if (!string.IsNullOrWhiteSpace(fishingCatalogSearch))
            {
                var query = fishingCatalogSearch.Trim();
                var matches = Plugin.DataManager.GetExcelSheet<Item>()
                    .Where(item => item.RowId != 0 &&
                                   !string.IsNullOrWhiteSpace(item.Name.ToString()) &&
                                   item.Name.ToString().Contains(query, StringComparison.OrdinalIgnoreCase))
                    .Take(20)
                    .ToList();

                if (ImGui.BeginChild("FishingCatalogMatches", new Vector2(0, 120f * UIConstants.Scale), true))
                {
                    foreach (var item in matches)
                    {
                        if (!ImGui.Selectable($"{item.Name}##{item.RowId}"))
                            continue;

                        if (configManager.AddFishingStockCatalogEntry(configuration, item.RowId, 99, false))
                        {
                            pendingFishingCatalogRow = false;
                            fishingCatalogSearch = string.Empty;
                        }
                        else
                        {
                            Plugin.ChatGui.PrintError("[Vermaxion] That item is already in the fishing-stock catalog.");
                        }
                    }
                }
                ImGui.EndChild();
            }

            if (ImGui.SmallButton("Cancel blank row"))
            {
                pendingFishingCatalogRow = false;
                fishingCatalogSearch = string.Empty;
            }
            ImGui.PopID();
        }
        else if (ImGui.SmallButton("+ Add fishing-stock item"))
        {
            pendingFishingCatalogRow = true;
            fishingCatalogSearch = string.Empty;
            focusFishingCatalogSearch = true;
        }

        var account = configManager.GetCurrentAccount();
        var allDifferingRecords = account == null
            ? 0
            : new[] { account.DefaultConfig }
                .Concat(account.Characters.Values)
                .Count(record => configuration.FishingStockCatalog.Any(row => !FishingStockRowMatches(record, row)));
        ImGui.BeginDisabled(account == null);
        if (ImGui.SmallButton($"Sync ALL catalog defaults ({allDifferingRecords})"))
        {
            var count = configManager.SyncAllFishingStockRowsToCurrentAccount(configuration.FishingStockCatalog);
            Plugin.ChatGui.Print($"[Vermaxion] All fishing-stock defaults synchronized to {count} current-account records.");
        }
        ImGui.EndDisabled();
        ImGui.TextWrapped($"Affected account: {GetAccountDisplayName(configManager, configManager.CurrentAccountId)}");
        ImGui.TextWrapped("Changing a global default does not alter existing account or character values until a row or all-catalog sync is explicitly used.");

        if (changed)
            configuration.Save();
    }

    private static bool FishingStockRowMatches(CharacterConfig config, FishingStockCatalogEntry row)
        => config.FishingStockItems.TryGetValue(row.ItemId, out var stock) &&
           stock.Enabled == row.DefaultEnabled &&
           stock.Target == row.DefaultTarget &&
           stock.Min == row.DefaultMin;

    private string GetItemName(uint itemId)
    {
        var sheet = Plugin.DataManager.GetExcelSheet<Item>();
        return sheet.GetRowOrDefault(itemId)?.Name.ToString() is { Length: > 0 } name
            ? name
            : $"Item {itemId}";
    }

    private void DrawWizardPopup()
    {
        if (activeWizard != null && wizardContextValid?.Invoke() != true)
            CloseWizard();

        if (wizardPopupRequested)
        {
            ImGui.OpenPopup("Setup Wizard");
            wizardPopupRequested = false;
        }

        var open = true;
        if (!ImGui.BeginPopupModal("Setup Wizard", ref open, ImGuiWindowFlags.AlwaysAutoResize))
        {
            CloseWizard();
            return;
        }

        if (!open || activeWizard == null || wizardDraft == null)
        {
            ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
            CloseWizard();
            return;
        }

        ImGui.Text($"{FormatWizardKind(activeWizard.Value)} wizard");
        ImGui.TextWrapped($"Account: {GetAccountDisplayName(plugin.ConfigManager, plugin.ConfigManager.CurrentAccountId)} - Default Config");
        ImGui.Separator();
        ImGui.TextWrapped("Changes are staged here. Apply writes only to this account's Default Config and never starts automation.");

        switch (activeWizard.Value)
        {
            case SetupWizardKind.DefaultAndSync:
            {
                var enabled = wizardDraft.Enabled;
                if (ImGui.Checkbox("Enable inherited character automation", ref enabled))
                    wizardDraft.Enabled = enabled;
                ImGui.TextWrapped("New characters inherit Default Config. Existing characters remain unchanged until you use a row-level sync or Apply Default to ALL in the Default Config view.");
                break;
            }
            case SetupWizardKind.FcBuff:
            {
                var enabled = wizardDraft.EnableFCBuffRefill;
                if (ImGui.Checkbox("Enable FC Buff", ref enabled))
                    wizardDraft.EnableFCBuffRefill = enabled;
                var allowActivation = wizardDraft.AllowFCBuffActivation;
                if (ImGui.Checkbox(UIConstants.ConfigLabels.AllowFCBuffActivation, ref allowActivation))
                    wizardDraft.AllowFCBuffActivation = allowActivation;
                var maintainStockTarget = wizardDraft.MaintainFCBuffStockTarget;
                if (ImGui.Checkbox(UIConstants.ConfigLabels.MaintainFCBuffStockTarget, ref maintainStockTarget))
                    wizardDraft.MaintainFCBuffStockTarget = maintainStockTarget;
                var frequency = wizardDraft.FCBuffFrequency;
                if (ImGui.BeginCombo("Frequency", frequency.ToString()))
                {
                    foreach (var option in Enum.GetValues<FCBuffFrequency>())
                    {
                        var selected = option == frequency;
                        if (ImGui.Selectable(option.ToString(), selected))
                            wizardDraft.FCBuffFrequency = option;
                        if (selected)
                            ImGui.SetItemDefaultFocus();
                    }
                    ImGui.EndCombo();
                }
                if (ImGui.Button("Reset saved cadence state on Apply"))
                    wizardFcBuffCadenceResetRequested = true;
                if (wizardFcBuffCadenceResetRequested)
                    ImGui.TextDisabled("Cadence completion state will be reset to due when this wizard is applied.");
                var quantity = wizardDraft.FCBuffPurchaseAttempts;
                if (ImGui.InputInt(UIConstants.ConfigLabels.MaxPurchaseAttempts, ref quantity))
                    wizardDraft.FCBuffPurchaseAttempts = Math.Clamp(
                        quantity,
                        1,
                        FCBuffRecoveryPolicy.MaxPurchaseAttempts);
                var points = wizardDraft.FCBuffMinPoints;
                if (ImGui.InputInt("Minimum FC points", ref points))
                    wizardDraft.FCBuffMinPoints = Math.Max(0, points);
                var gil = wizardDraft.FCBuffMinGil;
                if (ImGui.InputInt("Minimum gil", ref gil))
                    wizardDraft.FCBuffMinGil = Math.Max(0, gil);
                ImGui.TextWrapped("Requires Free Company action access. Target mode buys only the live shortfall and replaces an action this run activates; without it, positive stock still suppresses purchasing. Stock is decremented only after confirmed VERMAXION activation.");
                break;
            }
            case SetupWizardKind.Fishing:
            {
                var enabled = wizardDraft.EnableFishing;
                if (ImGui.Checkbox("Enable Fishing", ref enabled))
                    wizardDraft.EnableFishing = enabled;
                foreach (var row in plugin.Configuration.FishingStockCatalog)
                {
                    if (!wizardDraft.FishingStockItems.TryGetValue(row.ItemId, out var stock))
                    {
                        stock = new FishingStockSetting
                        {
                            Enabled = row.DefaultEnabled,
                            Target = row.DefaultTarget,
                            Min = row.DefaultMin,
                        };
                        wizardDraft.FishingStockItems[row.ItemId] = stock;
                    }
                    ImGui.PushID($"WizardFishing_{row.ItemId}");
                    var stockEnabled = stock.Enabled;
                    if (ImGui.Checkbox(GetItemName(row.ItemId), ref stockEnabled))
                        stock.Enabled = stockEnabled;
                    ImGui.SameLine(300f);
                    var target = stock.Target;
                    ImGui.SetNextItemWidth(72f);
                    if (ImGui.InputInt("target", ref target))
                        stock.Target = Math.Max(0, target);
                    ImGui.SameLine();
                    var wizardMin = stock.Min;
                    ImGui.SetNextItemWidth(72f);
                    if (ImGui.InputInt("min", ref wizardMin))
                        stock.Min = Math.Max(0, wizardMin);
                    ImGui.PopID();
                }
                ImGui.TextWrapped("Requires ADS and the listed fishing dependencies. Optional bait purchase failures are reported; Versatile Lure blocks only when none remains.");
                break;
            }
            case SetupWizardKind.RetainerEquipping:
            {
                var enabled = wizardDraft.EnableRetainerEquipping;
                if (ImGui.Checkbox("Enable Retainer Equipping", ref enabled))
                    wizardDraft.EnableRetainerEquipping = enabled;
                var sourceMode = wizardDraft.RetainerGearSourceMode;
                if (ImGui.BeginCombo("Gear source", FormatRetainerGearSourceMode(sourceMode)))
                {
                    foreach (var mode in Enum.GetValues<RetainerGearSourceMode>())
                    {
                        var selected = mode == sourceMode;
                        if (ImGui.Selectable(FormatRetainerGearSourceMode(mode), selected))
                            wizardDraft.RetainerGearSourceMode = mode;
                        if (selected)
                            ImGui.SetItemDefaultFocus();
                    }
                    ImGui.EndCombo();
                }
                var nonUnique = wizardDraft.RetainerGearNonUniqueOnly;
                if (ImGui.Checkbox("Use non-unique items only", ref nonUnique))
                    wizardDraft.RetainerGearNonUniqueOnly = nonUnique;
                var combatTarget = wizardDraft.RetainerCombatItemLevelTarget;
                if (ImGui.InputInt("Combat item-level target", ref combatTarget))
                    wizardDraft.RetainerCombatItemLevelTarget = Math.Max(0, combatTarget);
                var perceptionTarget = wizardDraft.RetainerGatheringPerceptionTarget;
                if (ImGui.InputInt("Gathering Perception target", ref perceptionTarget))
                    wizardDraft.RetainerGatheringPerceptionTarget = Math.Max(0, perceptionTarget);
                ImGui.TextWrapped("Only AutoRetainer-enabled retainers are touched. Player-equipped items are excluded. Venture reassignment suppression is temporary and restored to its prior state.");
                break;
            }
        }

        ImGui.Separator();
        var account = plugin.ConfigManager.GetCurrentAccount();
        var impact = account == null
            ? new List<SetupWizardFieldChange>()
            : SetupWizardPolicy.GetImpact(activeWizard.Value, account.DefaultConfig, wizardDraft).ToList();
        if (activeWizard == SetupWizardKind.FcBuff && wizardFcBuffCadenceResetRequested)
        {
            impact.Add(new SetupWizardFieldChange(
                "FCBuffCadenceState",
                "Cadence completion state",
                "Saved",
                "Due"));
        }
        ImGui.Text("Changes to Account default");
        if (impact.Count == 0)
        {
            ImGui.TextDisabled("No fields will change.");
        }
        else if (ImGui.BeginTable(
                     "WizardImpact",
                     3,
                     ImGuiTableFlags.Borders |
                     ImGuiTableFlags.RowBg |
                     ImGuiTableFlags.ScrollY |
                     ImGuiTableFlags.SizingStretchProp,
                     new Vector2(0, Math.Min(180f, 28f + impact.Count * 24f))))
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Field", ImGuiTableColumnFlags.WidthStretch, 1.4f);
            ImGui.TableSetupColumn("Current", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Staged", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();
            foreach (var change in impact)
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.TextWrapped(change.Label);
                ImGui.TableSetColumnIndex(1);
                ImGui.TextWrapped(change.Before);
                ImGui.TableSetColumnIndex(2);
                ImGui.TextWrapped(change.After);
            }
            ImGui.EndTable();
        }

        ImGui.Separator();
        ImGui.BeginDisabled(impact.Count == 0);
        if (ImGui.Button("Apply to account default"))
        {
            if (ApplyWizard(applyToAllCharacters: false))
            {
                ImGui.CloseCurrentPopup();
                CloseWizard();
            }
        }
        ImGui.SameLine();
        if (ImGui.Button("Apply default to all characters"))
        {
            if (ApplyWizard(applyToAllCharacters: true))
            {
                ImGui.CloseCurrentPopup();
                CloseWizard();
            }
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
        {
            ImGui.CloseCurrentPopup();
            CloseWizard();
        }

        ImGui.EndPopup();
    }

    private bool ApplyWizard(bool applyToAllCharacters)
    {
        if (activeWizard == null || wizardDraft == null || wizardContextValid?.Invoke() != true)
            return false;

        var account = plugin.ConfigManager.GetCurrentAccount();
        if (account == null)
        {
            Plugin.ChatGui.PrintError("[Vermaxion] Select an account before applying a setup wizard.");
            return false;
        }

        SetupWizardPolicy.Apply(activeWizard.Value, wizardDraft, account.DefaultConfig);
        if (activeWizard == SetupWizardKind.FcBuff && wizardFcBuffCadenceResetRequested)
            account.DefaultConfig.ResetFCBuffState();

        plugin.ConfigManager.SaveCurrentAccount();
        var appliedCharacterCount = applyToAllCharacters
            ? RunConfigMutationWithTargetPause(
                plugin.ConfigManager.ApplyDefaultToAllCharacters,
                "setup wizard default applied to all characters")
            : 0;
        if (applyToAllCharacters && activeWizard == SetupWizardKind.FcBuff && wizardFcBuffCadenceResetRequested)
        {
            foreach (var character in account.Characters.Values)
                character.ResetFCBuffState();
            plugin.ConfigManager.SaveCurrentAccount();
        }
        plugin.Configuration.SetupWizardCompleted = true;
        plugin.Configuration.SetupWizardStateMigrated = true;
        plugin.Configuration.Save();
        Plugin.ChatGui.Print(applyToAllCharacters
            ? $"[Vermaxion] Setup wizard applied to this account's Default Config and {appliedCharacterCount} characters."
            : "[Vermaxion] Setup wizard applied to this account's Default Config. Existing characters were not changed.");
        return true;
    }

    private void CloseWizard()
    {
        activeWizard = null;
        wizardDraft = null;
        wizardPopupRequested = false;
        wizardFcBuffCadenceResetRequested = false;
        wizardContextValid = null;
    }

    private static string PvpEnableWarning(string mode)
        => $"Other players may be watching for automated play in {mode}. Enable it anyway?";

    private static string GetPvpEnableWarning(CharacterConfig? source, IEnumerable<CharacterConfig>? targets,
        Action<CharacterConfig, CharacterConfig> copy)
    {
        if (source == null || targets == null) return string.Empty;
        var frontline = false;
        var rivalWings = false;
        foreach (var target in targets)
        {
            var projected = target.Clone();
            copy(source, projected);
            frontline |= !target.EnableNagYourMomFrontline && projected.EnableNagYourMomFrontline;
            rivalWings |= !target.EnableNagYourMomRivalWings && projected.EnableNagYourMomRivalWings;
        }
        var warnings = new List<string>();
        if (frontline) warnings.Add(PvpEnableWarning(UIConstants.ConfigLabels.NagYourMomFrontline));
        if (rivalWings) warnings.Add(PvpEnableWarning(UIConstants.ConfigLabels.NagYourMomRivalWings));
        return string.Join("\n\n", warnings);
    }

    private Func<bool> CaptureConfigurationContext()
    {
        var manager = plugin.ConfigManager;
        var accountId = manager.CurrentAccountId;
        var characterKey = manager.SelectedCharacterKey;
        var account = manager.GetCurrentAccount();
        var profile = account == null ? null : manager.GetSelectedConfig();
        return () => manager.CurrentAccountId == accountId
            && manager.SelectedCharacterKey == characterKey
            && ReferenceEquals(manager.GetCurrentAccount(), account)
            && (account == null || ReferenceEquals(manager.GetSelectedConfig(), profile));
    }

    private void RequestConfirmation(string title, string message, System.Action action, Func<string>? warning = null)
    {
        confirmationTitle = title;
        confirmationMessage = message;
        confirmedAction = action;
        confirmationContextValid = CaptureConfigurationContext();
        confirmationWarning = warning;
        confirmationPopupRequested = true;
    }

    private void ClearConfirmation()
    {
        confirmedAction = null;
        confirmationContextValid = null;
        confirmationWarning = null;
        confirmationPopupRequested = false;
    }

    private void DrawConfirmationPopup()
    {
        if (confirmedAction != null && confirmationContextValid?.Invoke() != true)
            ClearConfirmation();

        if (confirmationPopupRequested)
        {
            ImGui.OpenPopup("Confirm configuration action");
            confirmationPopupRequested = false;
        }

        ImGui.SetNextWindowSize(new Vector2(ImGui.GetFontSize() * 34f, 0), ImGuiCond.Always);
        var open = true;
        if (!ImGui.BeginPopupModal(
                "Confirm configuration action",
                ref open,
                ImGuiWindowFlags.AlwaysAutoResize))
        {
            ClearConfirmation();
            return;
        }

        if (!open || confirmedAction == null || ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            ClearConfirmation();
            ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
            return;
        }

        ImGui.Text(confirmationTitle);
        ImGui.Separator();
        if (confirmationMessage.Length > 0) ImGui.TextWrapped(confirmationMessage);
        var warning = confirmationWarning?.Invoke() ?? string.Empty;
        if (warning.Length > 0)
        {
            ImGui.Spacing();
            ImGui.TextWrapped(warning);
        }
        ImGui.Spacing();
        if (ImGui.Button(warning.Length > 0 ? "Yes" : "Confirm"))
        {
            var action = confirmedAction;
            ClearConfirmation();
            action?.Invoke();
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button(warning.Length > 0 ? "No" : "Cancel"))
        {
            ClearConfirmation();
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    private static string FormatOceanFishingProvider(OceanFishingProvider provider)
        => provider switch
        {
            OceanFishingProvider.AutoHookAutoOceanFish => "AutoHook AutoOceanFish",
            _ => "VerMAXION + AutoHook",
        };

    private static string FormatWizardKind(SetupWizardKind kind)
        => kind switch
        {
            SetupWizardKind.FcBuff => "FC Buff",
            SetupWizardKind.Fishing => "Fishing",
            SetupWizardKind.RetainerEquipping => "Retainer Equipping",
            _ => "Default & Sync",
        };

    private static string FormatRetainerGearSourceMode(RetainerGearSourceMode mode)
        => mode switch
        {
            RetainerGearSourceMode.IgnoreArmory => "Ignore Armoury Chest",
            RetainerGearSourceMode.IgnoreGearset => "Ignore saved gearsets",
            RetainerGearSourceMode.AllGear => "All inventory and Armoury gear",
            _ => mode.ToString(),
        };
}
