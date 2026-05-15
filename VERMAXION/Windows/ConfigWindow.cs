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

public class ConfigWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private string editAccountAlias = "";
    private readonly List<DadDutyOption> dadDutyOptions = new();
    private string dadDungeonSearch = "";
    private bool dadDutyOptionsLoaded;
    private string[] dadLanPartyPresetOptions = DadRunRequestOptions.LanPartyPresetStubs;
    private DateTime dadLanPartyPresetRefreshUtc = DateTime.MinValue;

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
    }

    private void DrawTaskOrderTab()
    {
        var config = plugin.Configuration;
        if (PostProcessTaskOrder.Normalize(config))
            config.Save();

        ImGui.Text("Global post-process order");
        ImGui.TextDisabled("Disabled or unavailable tasks remain listed. The engine skips them at runtime.");
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

            ImGui.PopID();
        }
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

        ImGui.TextDisabled("Chocobo Racing:");
        ImGui.BulletText("Chocoholic - Handles chocobo race automation");
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
        if (!string.IsNullOrEmpty(currentChar))
        {
            var isCurrentSelected = configManager.SelectedCharacterKey == currentChar;
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.4f, 1f, 0.4f, 1)); // Green text
            
            // Apply krangle to current character display
            var displayCurrentChar = plugin.Configuration.KrangleEnabled 
                ? KrangleService.KrangleName(CleanLuminaText(currentChar))
                : currentChar;
                
            if (ImGui.Selectable(displayCurrentChar, isCurrentSelected))
            {
                configManager.SelectedCharacterKey = currentChar;
            }
            ImGui.PopStyleColor();
            ImGui.Spacing();
        }

        // Other characters sorted alphabetically
        foreach (var charKey in configManager.GetSortedCharacterKeys())
        {
            if (charKey == currentChar) continue; // Skip current char, already shown
            var displayName = plugin.Configuration.KrangleEnabled
                ? KrangleService.KrangleName(CleanLuminaText(charKey))
                : charKey;

            var isSelected = configManager.SelectedCharacterKey == charKey;
            if (ImGui.Selectable(displayName, isSelected))
            {
                configManager.SelectedCharacterKey = charKey;
            }

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
            ImGui.TextDisabled(UIConstants.ConfigLabels.NewCharactersInheritThese);
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

            var henchman = cc.EnableHenchmanManagement;
            if (ImGui.Checkbox(UIConstants.ConfigLabels.HenchmanManagement, ref henchman))
            {
                cc.EnableHenchmanManagement = henchman;
                changed = true;
            }
            DrawDefaultOverrideButton(isDefault, configManager, "HenchmanManagement", UIConstants.ConfigLabels.HenchmanManagement,
                (source, target) => target.EnableHenchmanManagement = source.EnableHenchmanManagement);
            ImGui.SameLine();
            ImGui.TextDisabled("(?)");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(UIConstants.Tooltips.HenchmanManagement);

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
                ImGui.SetTooltip("Selects the highest level combat job (DOW/DOM only). Requires SimpleTweaks.");

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
                ImGui.SetTooltip("Updates equipment for current job only. No job cycling. Requires SimpleTweaks.");

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

            ImGui.Separator();
            ImGui.Text("Run Shutdown Bundle");
            ImGui.SameLine();
            if (ImGui.SmallButton("Send now##ShutdownBundleConfig"))
            {
                plugin.Engine.SendRunShutdownCommandBundle();
            }
            ImGui.TextDisabled("Always on. Sent once at the start of every AutoRetainer/manual VERMAXION run.");
            ImGui.TextWrapped("Commands: /rotation cancel, /vbmai off, /bmrai off, /wrath auto off, /vnavmesh stop, /visland stop, /ad stop, /sice stop, /ochillegal off, /fr off");
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
                    ImGui.SetTooltip("Uses RaceChocoboManager when the live racing chocobo profile is loaded. If the current racing chocobo rank is 50, Vermaxion skips the daily racing task instead of queueing.");

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
                ImGui.SetTooltip("Must start near a summoning bell. Withdraws current retainer market listings back into player inventory on the selected schedule.");
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

                ImGui.TextDisabled($"Attempts today: {cc.NagYourMomAttemptsToday}/{cc.NagYourMomRunsPerDay}");
                ImGui.TextDisabled($"Engine status: {plugin.Engine.NagYourMomStatusText}");
                ImGui.TextWrapped("AR-only task. VERMAXION evaluates this during the normal post-process pass, checks the local machine time window, and then asks mom for one full casual CC run.");
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
                ImGui.TextDisabled("Runs every AutoRetainer/manual VERMAXION run near a summoning bell; withdraws current market listings.");
                return;

            case RefillFromListingsFrequency.Monthly:
                if (IsRefillFromListingsMonthlyComplete(config))
                    ImGui.TextDisabled($"Completed until {FormatUtc(config.RefillFromListingsNextReset)}");
                else
                    ImGui.TextDisabled("Runs once per UTC calendar month near a summoning bell.");
                return;

            case RefillFromListingsFrequency.Daily:
                DrawDailyTaskHint(config.RefillFromListingsLastCompleted, config.RefillFromListingsNextReset, "Runs once per daily reset near a summoning bell.");
                return;

            case RefillFromListingsFrequency.Weekly:
            default:
                DrawWeeklyTaskHint(config.RefillFromListingsLastCompleted, config.RefillFromListingsNextReset, "Runs once per weekly reset near a summoning bell.");
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
            ImGui.TextDisabled($"Available Friday at {FormatUtc(ResetDetectionService.GetNextFashionReportAvailability(now))}. Runs only during the Friday UTC window.");
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
}
