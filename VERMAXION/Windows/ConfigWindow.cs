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

namespace VERMAXION.Windows;

public enum SetupWizardKind
{
    DefaultAndSync,
    FcBuff,
    Fishing,
    RetainerEquipping,
}

public class ConfigWindow : Window, IDisposable
{
    private const string StylistRepositoryUrl = "https://raw.githubusercontent.com/NightmareXIV/MyDalamudPlugins/main/pluginmaster.json";
    private readonly Plugin plugin;
    private string editAccountAlias = "";
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

    public void OpenWizard(SetupWizardKind kind)
    {
        var account = plugin.ConfigManager.GetCurrentAccount();
        wizardDraft = (account?.DefaultConfig ?? CharacterConfig.CreateNew()).Clone();
        activeWizard = kind;
        wizardPopupRequested = true;
    }

    private sealed class DadDutyOption
    {
        public uint Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string ContentType { get; init; } = string.Empty;
        public string DisplayName => $"{Name} ({Id})";
        public string SearchText => $"{Name} {Id} {ContentType}";
    }

    public ConfigWindow(Plugin plugin)
        : base("Vermaxion Configuration##Config", ImGuiWindowFlags.None)
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(700, 500),
            MaximumSize = new Vector2(1200, 900),
        };
    }

    public void Dispose() { }

    public override void Draw()
    {
        if (ImGui.BeginTabBar("ConfigTabs"))
        {
            if (ImGui.BeginTabItem("Settings"))
            {
                DrawSettingsTab();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Task Order"))
            {
                DrawTaskOrderTab();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("About"))
            {
                DrawAboutTab();
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }

        DrawWizardPopup();
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
        ImGui.TextDisabled("Before AR runs while AutoRetainer is suppressed after login. After AR runs in the normal postprocess slot.");
        ImGui.Spacing();

        if (ImGui.Button("Reset to default"))
        {
            PostProcessTaskOrder.ResetToDefault(config);
            config.Save();
        }

        ImGui.Separator();

        var order = config.PostProcessTaskOrder;
        for (var index = 0; index < order.Count; index++)
        {
            ImGui.PushID($"TaskOrder_{order[index]}_{index}");

            var canMoveUp = index > 0;
            if (!canMoveUp)
                ImGui.BeginDisabled();
            if (ImGui.SmallButton("Up"))
            {
                (order[index - 1], order[index]) = (order[index], order[index - 1]);
                config.Save();
            }
            if (!canMoveUp)
                ImGui.EndDisabled();

            ImGui.SameLine();

            var canMoveDown = index < order.Count - 1;
            if (!canMoveDown)
                ImGui.BeginDisabled();
            if (ImGui.SmallButton("Down"))
            {
                (order[index + 1], order[index]) = (order[index], order[index + 1]);
                config.Save();
            }
            if (!canMoveDown)
                ImGui.EndDisabled();

            ImGui.SameLine();
            ImGui.Text($"{index + 1}. {PostProcessTaskOrder.GetLabel(order[index])}");
            var definition = AutomationCatalog.Get(order[index]);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"{definition.CadenceLabel} · {definition.OwnershipLabel}");

            ImGui.SameLine(360f);
            var phase = config.PostProcessTaskPlacement.TryGetValue(order[index], out var configuredPhase)
                ? configuredPhase
                : PostProcessTaskOrder.GetDefaultPhase(order[index]);
            var beforeAr = phase == PostProcessTaskPhase.BeforeAR;
            if (ImGui.RadioButton("Before AR", beforeAr))
            {
                config.PostProcessTaskPlacement[order[index]] = PostProcessTaskPhase.BeforeAR;
                config.Save();
            }
            ImGui.SameLine();
            if (ImGui.RadioButton("After AR", !beforeAr))
            {
                config.PostProcessTaskPlacement[order[index]] = PostProcessTaskPhase.AfterAR;
                config.Save();
            }

            ImGui.PopID();
        }

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

    private static void DrawCatalogCategory(AutomationOwner owner, string heading)
    {
        ImGui.Text(heading);
        foreach (var feature in AutomationCatalog.Features.Where(feature => feature.Owner == owner))
            ImGui.BulletText($"{feature.Label} — {feature.CadenceLabel}; {feature.OwnershipLabel}");
    }

    private void DrawSettingsTab()
    {
        var configManager = plugin.ConfigManager;
        var config = plugin.Configuration;

        // --- Global Settings ---
        if (ImGui.CollapsingHeader(UIConstants.ConfigLabels.GlobalSettings, ImGuiTreeNodeFlags.DefaultOpen))
        {
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

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Text("Replayable setup wizards");
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
            ImGui.TextWrapped("Wizards stage changes and edit only the current account's Default Config after Apply. Existing characters remain unchanged until an explicit row sync or Apply Default to ALL.");

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Text("Fishing");

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

            if (ImGui.SmallButton("Reset Fishing startup gate"))
                plugin.ResetFishingStartupGate();
            DrawHelpMarker("Clears the current Ocean Fishing startup-window attempt guard so an explicit Fishing run can retry.");

            DrawFishingStockCatalogEditor();
        }

        ImGui.Separator();

        // --- Account Selector ---
        DrawAccountSelector(configManager);

        ImGui.Separator();

        // --- Left Panel: Character List / Right Panel: Character Settings ---
        var leftWidth = config.LeftPanelWidth;

        if (ImGui.BeginChild("LeftPanel", new Vector2(leftWidth, 0), true))
        {
            DrawCharacterList(configManager);
        }
        ImGui.EndChild();

        ImGui.SameLine();

        if (ImGui.BeginChild("RightPanel", new Vector2(0, 0), true))
        {
            DrawCharacterSettings(configManager);
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
        ImGui.BulletText("Teleporter - /tp gold (teleport to Gold Saucer)");
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

    private static string CleanLuminaText(string text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains('\u0001'))
            return text;
        return text.Split('\u0001')[1] ?? text;
    }

    private void DrawCharacterList(ConfigManager configManager)
    {
        ImGui.Text(UIConstants.ConfigLabels.Characters);
        ImGui.SameLine();
        DrawCharacterSortSelector();
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

        foreach (var charKey in configManager.GetSortedCharacterKeys(plugin.Configuration.CharacterListSortMode))
        {
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
                    configManager.ResetCharacterToDefault(charKey);
                if (ImGui.MenuItem("Delete"))
                    configManager.DeleteCharacter(charKey);
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

        var displayName = isDefault ? "Default Config" : charKey;
        if (plugin.Configuration.KrangleEnabled && !isDefault)
            displayName = KrangleService.KrangleName(CleanLuminaText(charKey));

        ImGui.Text($"{UIConstants.ConfigLabels.Settings}: {displayName}");
        if (isDefault)
        {
            ImGui.TextDisabled(UIConstants.ConfigLabels.NewCharactersInheritThese);
            if (ImGui.SmallButton("Apply Default to ALL"))
            {
                var count = configManager.ApplyDefaultToAllCharacters();
                Plugin.ChatGui.Print($"[Vermaxion] Default Config applied to {count} characters.");
            }
            ImGui.SameLine();
            ImGui.TextDisabled("Explicitly replaces each existing character's synchronized settings.");
        }
        else if (!string.Equals(charKey, configManager.CurrentCharacterKey, StringComparison.Ordinal))
            ImGui.TextDisabled($"Runtime character is {configManager.CurrentCharacterKey}");
        ImGui.Separator();

        var changed = false;

        // Master enable
        var enabled = cc.Enabled;
        if (ImGui.Checkbox($"{UIConstants.ConfigLabels.Enabled}##CharEnabled", ref enabled))
        {
            cc.Enabled = enabled;
            changed = true;
        }
        DrawDefaultOverrideButton(isDefault, configManager, "CharEnabled", UIConstants.ConfigLabels.Enabled,
            (source, target) => target.Enabled = source.Enabled);

        ImGui.Spacing();

        // --- Feature Toggles ---
        if (ImGui.CollapsingHeader(UIConstants.ConfigLabels.EveryARPostProcess, ImGuiTreeNodeFlags.DefaultOpen))
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
            ImGui.TextWrapped("Commands: /rotation Cancel, /at enable, /vbmai off, /bmrai off, /wrath auto off, /vnavmesh stop, /visland stop, /ad stop, /sice stop, /ochillegal off, /fr off, /rotation Settings StartOnCountdown False");

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
                var attempts = cc.FCBuffPurchaseAttempts;
                if (ImGui.SliderInt(UIConstants.ConfigLabels.MaxPurchaseAttempts, ref attempts, 1, 30))
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
                plugin.GearUpdaterService.StartBootstrap();
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

                ImGui.TextDisabled("Gridania: Maisenta for Gysahl Greens. Khetto's Amphitheatre: Alaric for Grade 8 Dark Matter.");
                ImGui.Unindent();
            }

            var fishing = cc.EnableFishing;
            if (ImGui.Checkbox("Fishing", ref fishing))
            {
                cc.EnableFishing = fishing;
                changed = true;
            }
            DrawDefaultOverrideButton(isDefault, configManager, "Fishing", "Fishing",
                (source, target) => target.EnableFishing = source.EnableFishing);
            DrawHelpMarker("Enables Fishing for this character. Vermaxion chooses among enabled current-account characters, casts, and leaves hook/reel behavior to AutoHook.");
            if (cc.EnableFishing)
            {
                ImGui.Indent();

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
                    if (isDefault)
                    {
                        ImGui.SameLine();
                        if (ImGui.SmallButton("Apply row to ALL"))
                        {
                            var defaultStock = cc.FishingStockItems[row.ItemId].Clone();
                            var count = configManager.ApplyDefaultSettingToAllCharacters(
                                $"{GetItemName(row.ItemId)} fishing stock",
                                (_, target) => target.FishingStockItems[row.ItemId] = defaultStock.Clone());
                            Plugin.ChatGui.Print($"[Vermaxion] Fishing-stock row applied to {count} characters.");
                        }
                    }
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
                DrawHelpMarker("Where this character should go after the fishing duty or window ends.");

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
                DrawHelpMarker("After discard cleanup, moves near Limsa's Merchant & Mender and runs /ays itemsell. Cleanup warnings do not prevent the configured return.");

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

                ImGui.TextDisabled("Requires XADB, AutoRetainer, Lifestream, AutoHook, and vnavmesh. ADS is the only repair provider and is required when repair is enabled.");
                ImGui.Unindent();
            }

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

            ImGui.Separator();
            ImGui.TextDisabled("Misc Cmd sends once at the start of every enabled AutoRetainer/manual VERMAXION run.");
        }

        if (ImGui.CollapsingHeader(UIConstants.ConfigLabels.WeeklyTasks, ImGuiTreeNodeFlags.DefaultOpen))
        {
            var verminion = cc.EnableVerminionQueue;
            if (ImGui.Checkbox(UIConstants.ConfigLabels.VerminionQueue, ref verminion))
            {
                cc.EnableVerminionQueue = verminion;
                changed = true;
            }
            DrawDefaultOverrideButton(isDefault, configManager, "VerminionQueue", UIConstants.ConfigLabels.VerminionQueue,
                (source, target) => target.EnableVerminionQueue = source.EnableVerminionQueue);
            if (DrawResetButton("VerminionState", cc.ResetVerminionState))
                changed = true;
            if (ResetDetectionService.TaskIsCompleted(cc.VerminionLastCompleted, cc.VerminionNextReset))
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(1, 1, 0, 1), "[Already Completed]");
            }
            DrawWeeklyTaskHint(cc.VerminionLastCompleted, cc.VerminionNextReset, "Available during the current weekly window.");

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

            var register = cc.EnableRegisterRegistrables;
            if (ImGui.Checkbox("Register Registrables", ref register))
            {
                cc.EnableRegisterRegistrables = register;
                changed = true;
            }
            DrawDefaultOverrideButton(isDefault, configManager, "RegisterRegistrables", "Register Registrables",
                (source, target) => target.EnableRegisterRegistrables = source.EnableRegisterRegistrables);

            var registerFromInventory = cc.RegisterUnregisteredItemsFromInventory;
            if (ImGui.Checkbox("Register unregistered items from inventory", ref registerFromInventory))
            {
                cc.RegisterUnregisteredItemsFromInventory = registerFromInventory;
                changed = true;
            }
            DrawDefaultOverrideButton(
                isDefault,
                configManager,
                "RegisterUnregisteredItemsFromInventory",
                "Register unregistered items from inventory",
                (source, target) => target.RegisterUnregisteredItemsFromInventory =
                    source.RegisterUnregisteredItemsFromInventory);
            ImGui.SameLine();
            ImGui.TextDisabled("(?)");
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(
                    "Uses one snapshot of Inventory 1-4 and ignores the personal list for that run.\n" +
                    "Only direct mounts, minions, fashion accessories, facewear, orchestrion rolls,\n" +
                    "emotes/hairstyles, bardings, and Triple Triad cards that are still locked are used.");
            }

            ImGui.SameLine();
            if (ImGui.Button("Configure##RegistrableConfig"))
            {
                plugin.RegistrableConfigWindow.IsOpen = true;
            }
            DrawDefaultOverrideButton(isDefault, configManager, "PersonalRegistrableItems", "Registrable personal item list",
                (source, target) => target.PersonalRegistrableItems = new List<uint>(source.PersonalRegistrableItems));
        }

        if (ImGui.CollapsingHeader(UIConstants.ConfigLabels.DailyTasks, ImGuiTreeNodeFlags.DefaultOpen))
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

            var chocobo = cc.EnableChocoboRacing;
            if (ImGui.Checkbox(UIConstants.ConfigLabels.ChocoboRacing, ref chocobo))
            {
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
            DrawDailyTaskHint(cc.ChocoboRacingLastCompleted, cc.ChocoboRacingNextReset, "Runs once per daily reset.");
            if (chocobo)
            {
                ImGui.Indent();
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

                ImGui.Unindent();
            }

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

        if (ImGui.CollapsingHeader(UIConstants.ConfigLabels.VariableTimeTasks, ImGuiTreeNodeFlags.DefaultOpen))
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
                if (ImGui.Checkbox(UIConstants.ConfigLabels.NagYourMomFrontline, ref momFrontline))
                {
                    cc.EnableNagYourMomFrontline = momFrontline;
                    changed = true;
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
                if (ImGui.Checkbox(UIConstants.ConfigLabels.NagYourMomRivalWings, ref momRivalWings))
                {
                    cc.EnableNagYourMomRivalWings = momRivalWings;
                    changed = true;
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

        if (ImGui.CollapsingHeader(UIConstants.ConfigLabels.WipTasks, ImGuiTreeNodeFlags.DefaultOpen))
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

        ImGui.Spacing();
        ImGui.Separator();

        // Reset buttons
        if (ImGui.Button("Reset Weekly Section"))
        {
            cc.ResetWeeklySectionState();
            changed = true;
        }
        ImGui.SameLine();
        if (ImGui.Button("Reset Daily Section"))
        {
            cc.ResetDailySectionState();
            changed = true;
        }
        if (ImGui.Button("Reset All Character Task State"))
        {
            cc.ResetAllTaskState();
            changed = true;
        }

        ImGui.Spacing();

        // Apply Default to All button (only visible when editing default config)
        if (isDefault)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.5f, 0.8f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.6f, 0.9f, 1));
            if (ImGui.Button("Apply Default Settings to ALL Characters", new Vector2(-1, 30)))
            {
                var count = configManager.ApplyDefaultToAllCharacters();
                Plugin.Log.Information($"[Config] Applied default settings to {count} characters");
                Plugin.ChatGui.Print($"[Vermaxion] Default settings applied to {count} characters.");
            }
            ImGui.PopStyleColor(2);
            ImGui.TextDisabled("Copies all toggles and values from Default to every character. Preserves completion flags.");
        }

        if (changed)
            configManager.SaveCurrentAccount();
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

    private static bool DrawResetButton(string id, System.Action reset)
    {
        ImGui.SameLine();
        if (!ImGui.SmallButton($"Reset##{id}"))
            return false;

        reset();
        return true;
    }

    private static void DrawDefaultOverrideButton(
        bool isDefault,
        ConfigManager configManager,
        string id,
        string label,
        Action<CharacterConfig, CharacterConfig> copy)
    {
        if (!isDefault)
            return;

        ImGui.SameLine();
        if (!ImGui.SmallButton($"Override all##{id}"))
            return;

        var count = configManager.ApplyDefaultSettingToAllCharacters(label, copy);
        Plugin.Log.Information($"[Config] Applied default {label} to {count} characters");
        Plugin.ChatGui.Print($"[Vermaxion] Default {label} applied to {count} characters.");
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
        ImGui.TextDisabled("[-] [+] [item search] [default target] [default enabled]");

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
            ImGui.TextUnformatted(GetItemName(row.ItemId));
            ImGui.SameLine(370f);

            var target = row.DefaultTarget;
            ImGui.SetNextItemWidth(72f);
            if (ImGui.InputInt("##DefaultTarget", ref target))
            {
                row.DefaultTarget = Math.Max(0, target);
                changed = true;
            }
            ImGui.SameLine();
            var defaultMin = row.DefaultMin;
            ImGui.SetNextItemWidth(72f);
            if (ImGui.InputInt("##DefaultMin", ref defaultMin))
            {
                row.DefaultMin = Math.Max(0, defaultMin);
                changed = true;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Default reorder point (0 = buy whenever below target).");
            ImGui.SameLine();
            var enabled = row.DefaultEnabled;
            if (ImGui.Checkbox("##DefaultEnabled", ref enabled))
            {
                row.DefaultEnabled = enabled;
                changed = true;
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("Sync row to current account"))
            {
                var count = configManager.SyncFishingStockRowToCurrentAccount(row);
                Plugin.ChatGui.Print($"[Vermaxion] {GetItemName(row.ItemId)} defaults synchronized to {count} current-account records.");
            }

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
            ImGui.SetNextItemWidth(330f);
            if (focusFishingCatalogSearch)
            {
                ImGui.SetKeyboardFocusHere();
                focusFishingCatalogSearch = false;
            }
            ImGui.InputTextWithHint("##ItemSearch", "Search for an item...", ref fishingCatalogSearch, 128);
            ImGui.SameLine(370f);
            ImGui.TextDisabled("99");
            ImGui.SameLine();
            ImGui.TextDisabled("disabled");

            if (!string.IsNullOrWhiteSpace(fishingCatalogSearch))
            {
                var query = fishingCatalogSearch.Trim();
                var matches = Plugin.DataManager.GetExcelSheet<Item>()
                    .Where(item => item.RowId != 0 &&
                                   !string.IsNullOrWhiteSpace(item.Name.ToString()) &&
                                   item.Name.ToString().Contains(query, StringComparison.OrdinalIgnoreCase))
                    .Take(20)
                    .ToList();

                if (ImGui.BeginChild("FishingCatalogMatches", new Vector2(350f, 120f), true))
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

        if (ImGui.SmallButton("Sync ALL catalog defaults to current account"))
        {
            var count = configManager.SyncAllFishingStockRowsToCurrentAccount(configuration.FishingStockCatalog);
            Plugin.ChatGui.Print($"[Vermaxion] All fishing-stock defaults synchronized to {count} current-account records.");
        }
        ImGui.TextWrapped("Changing a global default does not alter existing account or character values until a row or all-catalog sync is explicitly used.");

        if (changed)
            configuration.Save();
    }

    private string GetItemName(uint itemId)
    {
        var sheet = Plugin.DataManager.GetExcelSheet<Item>();
        return sheet.GetRowOrDefault(itemId)?.Name.ToString() is { Length: > 0 } name
            ? name
            : $"Item {itemId}";
    }

    private void DrawWizardPopup()
    {
        if (wizardPopupRequested)
        {
            ImGui.OpenPopup("Setup Wizard");
            wizardPopupRequested = false;
        }

        if (activeWizard == null || wizardDraft == null)
            return;

        var open = true;
        if (!ImGui.BeginPopupModal("Setup Wizard", ref open, ImGuiWindowFlags.AlwaysAutoResize))
        {
            if (!open)
                CloseWizard();
            return;
        }

        ImGui.Text($"{FormatWizardKind(activeWizard.Value)} wizard");
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
                var quantity = wizardDraft.FCBuffPurchaseAttempts;
                if (ImGui.InputInt("Purchase quantity", ref quantity))
                    wizardDraft.FCBuffPurchaseAttempts = Math.Max(1, quantity);
                var points = wizardDraft.FCBuffMinPoints;
                if (ImGui.InputInt("Minimum FC points", ref points))
                    wizardDraft.FCBuffMinPoints = Math.Max(0, points);
                var gil = wizardDraft.FCBuffMinGil;
                if (ImGui.InputInt("Minimum gil", ref gil))
                    wizardDraft.FCBuffMinGil = Math.Max(0, gil);
                ImGui.TextWrapped("Requires Free Company action access. Stock is cached by Free Company ID and only decremented after confirmed activation.");
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
        if (ImGui.Button("Apply"))
        {
            if (ApplyWizard())
            {
                ImGui.CloseCurrentPopup();
                CloseWizard();
            }
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
        {
            ImGui.CloseCurrentPopup();
            CloseWizard();
        }

        ImGui.EndPopup();
    }

    private bool ApplyWizard()
    {
        if (activeWizard == null || wizardDraft == null)
            return false;

        var account = plugin.ConfigManager.GetCurrentAccount();
        if (account == null)
        {
            Plugin.ChatGui.PrintError("[Vermaxion] Select an account before applying a setup wizard.");
            return false;
        }

        var target = account.DefaultConfig;
        switch (activeWizard.Value)
        {
            case SetupWizardKind.DefaultAndSync:
                target.Enabled = wizardDraft.Enabled;
                break;
            case SetupWizardKind.FcBuff:
                target.EnableFCBuffRefill = wizardDraft.EnableFCBuffRefill;
                target.FCBuffPurchaseAttempts = wizardDraft.FCBuffPurchaseAttempts;
                target.FCBuffMinPoints = wizardDraft.FCBuffMinPoints;
                target.FCBuffMinGil = wizardDraft.FCBuffMinGil;
                break;
            case SetupWizardKind.Fishing:
                target.EnableFishing = wizardDraft.EnableFishing;
                target.FishingStockItems = wizardDraft.FishingStockItems.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.Clone());
                break;
            case SetupWizardKind.RetainerEquipping:
                target.EnableRetainerEquipping = wizardDraft.EnableRetainerEquipping;
                target.RetainerGearSourceMode = wizardDraft.RetainerGearSourceMode;
                target.RetainerGearNonUniqueOnly = wizardDraft.RetainerGearNonUniqueOnly;
                target.RetainerCombatItemLevelTarget = wizardDraft.RetainerCombatItemLevelTarget;
                target.RetainerGatheringPerceptionTarget = wizardDraft.RetainerGatheringPerceptionTarget;
                break;
        }

        plugin.ConfigManager.SaveCurrentAccount();
        plugin.Configuration.SetupWizardCompleted = true;
        plugin.Configuration.SetupWizardStateMigrated = true;
        plugin.Configuration.Save();
        Plugin.ChatGui.Print("[Vermaxion] Setup wizard applied to this account's Default Config. Existing characters were not changed.");
        return true;
    }

    private void CloseWizard()
    {
        activeWizard = null;
        wizardDraft = null;
        wizardPopupRequested = false;
    }

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
