using System;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using VERMAXION.Models;
using VERMAXION.Services;

namespace VERMAXION.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private string editAccountAlias = "";

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
            if (ImGui.BeginTabItem("About"))
            {
                DrawAboutTab();
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
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
                
                ImGui.Unindent();
            }

            var henchman = cc.EnableHenchmanManagement;
            if (ImGui.Checkbox(UIConstants.ConfigLabels.HenchmanManagement, ref henchman))
            {
                cc.EnableHenchmanManagement = henchman;
                changed = true;
            }
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
            ImGui.SameLine();
            ImGui.TextDisabled("(?)");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(UIConstants.Tooltips.MinionRoulette);
            
            // Minion Roulette state display
            if (cc.EnableMinionRoulette)
            {
                ImGui.Indent();
                ImGui.Text($"Attempts today: {cc.MinionRouletteAttemptsToday}");
                ImGui.Unindent();
            }

            var seasonalGear = cc.EnableSeasonalGearRoulette;
            if (ImGui.Checkbox(UIConstants.ConfigLabels.SeasonalGearRoulette, ref seasonalGear))
            {
                cc.EnableSeasonalGearRoulette = seasonalGear;
                changed = true;
            }
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

                var darkMatterTarget = cc.VendorStockGrade8DarkMatterTarget;
                ImGui.SetNextItemWidth(GetCompactNumericInputWidth());
                if (ImGui.InputInt("Grade 8 Dark Matter target", ref darkMatterTarget))
                {
                    cc.VendorStockGrade8DarkMatterTarget = Math.Max(0, darkMatterTarget);
                    changed = true;
                    configManager.SaveCurrentAccount();
                }

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
            ImGui.TextWrapped("Commands: /rotation cancel, /vbmai off, /bmrai off, /wrath auto off, /vnavmesh stop, /visland stop, /ad stop, /sice stop, /ochillegal off.");
        }

        if (ImGui.CollapsingHeader(UIConstants.ConfigLabels.WeeklyTasks, ImGuiTreeNodeFlags.DefaultOpen))
        {
            var verminion = cc.EnableVerminionQueue;
            if (ImGui.Checkbox(UIConstants.ConfigLabels.VerminionQueue, ref verminion))
            {
                cc.EnableVerminionQueue = verminion;
                changed = true;
            }
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
            if (IsJumboPurchasePendingPayout(cc.JumboCactpotLastCompleted, cc.JumboCactpotNextReset))
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

                if (cc.JumboCactpotNumberMode == JumboCactpotNumberMode.Fixed)
                {
                    var fixedNumber = cc.JumboCactpotFixedNumber;
                    ImGui.SetNextItemWidth(GetCompactNumericInputWidth() * 1.5f);
                    if (ImGui.InputInt("Fixed 4-digit number", ref fixedNumber))
                    {
                        cc.JumboCactpotFixedNumber = Math.Clamp(fixedNumber, 0, 9999);
                        changed = true;
                    }

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
            if (ResetDetectionService.TaskIsCompleted(cc.FashionReportLastCompleted, cc.FashionReportNextReset))
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(1, 1, 0, 1), "[Already Completed]");
            }
            else
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(1.0f, 0.75f, 0.2f, 1.0f), "[WIP]");
            }
            DrawFashionTaskHint(cc.FashionReportLastCompleted, cc.FashionReportNextReset);

            var register = cc.EnableRegisterRegistrables;
            if (ImGui.Checkbox("Register Registrables", ref register))
            {
                cc.EnableRegisterRegistrables = register;
                changed = true;
            }
            ImGui.SameLine();
            if (ImGui.Button("Configure##RegistrableConfig"))
            {
                plugin.RegistrableConfigWindow.IsOpen = true;
            }
        }

        if (ImGui.CollapsingHeader(UIConstants.ConfigLabels.DailyTasks, ImGuiTreeNodeFlags.DefaultOpen))
        {
            var mini = cc.EnableMiniCactpot;
            if (ImGui.Checkbox(UIConstants.ConfigLabels.MiniCactpot, ref mini))
            {
                cc.EnableMiniCactpot = mini;
                changed = true;
            }
            ImGui.SameLine();
            ImGui.TextDisabled("(?)");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("To enable: type /saucy, go to \"Other Games\" -> [x] Enable Auto Mini-Cactpot.\nVermaxion will teleport to Gold Saucer, walk to the Cactpot Board, and start the interaction.\nSaucy handles the actual mini-game solving.");
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

                var skipChocoboAtRank50 = cc.SkipChocoboRacingAtRank50;
                if (ImGui.Checkbox(UIConstants.ConfigLabels.SkipChocoboRacingIfLevel50, ref skipChocoboAtRank50))
                {
                    cc.SkipChocoboRacingAtRank50 = skipChocoboAtRank50;
                    changed = true;
                }
                ImGui.SameLine();
                ImGui.TextDisabled("(?)");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Uses RaceChocoboManager when the live racing chocobo profile is loaded. If the current racing chocobo rank is 50, Vermaxion skips the daily racing task instead of queueing.");

                ImGui.Unindent();
            }
            if (ResetDetectionService.TaskIsCompleted(cc.ChocoboRacingLastCompleted, cc.ChocoboRacingNextReset))
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(1, 1, 0, 1), "[Already Completed]");
            }
            DrawDailyTaskHint(cc.ChocoboRacingLastCompleted, cc.ChocoboRacingNextReset, "Runs once per daily reset.");
        }

        ImGui.Spacing();
        ImGui.Separator();

        // Reset buttons
        if (ImGui.Button("Reset Weekly Flags"))
        {
            // Reset legacy flags
            cc.VerminionCompletedThisWeek = false;
            cc.JumboCactpotCompletedThisWeek = false;
            cc.FashionReportCompletedThisWeek = false;
            cc.LastWeeklyReset = DateTime.MinValue;
            
            // Reset new DateTime system
            cc.VerminionLastCompleted = DateTime.MinValue;
            cc.VerminionNextReset = DateTime.MinValue;
            cc.JumboCactpotLastCompleted = DateTime.MinValue;
            cc.JumboCactpotNextReset = DateTime.MinValue;
            cc.FashionReportLastCompleted = DateTime.MinValue;
            cc.FashionReportNextReset = DateTime.MinValue;
            
            changed = true;
        }
        ImGui.SameLine();
        if (ImGui.Button("Reset Daily Flags"))
        {
            // Reset legacy flags
            cc.MiniCactpotCompletedToday = false;
            cc.ChocoboRacingCompletedToday = false;
            cc.LastDailyReset = DateTime.MinValue;
            cc.MiniCactpotTicketsToday = 0;
            
            // Reset new DateTime system
            cc.MiniCactpotLastCompleted = DateTime.MinValue;
            cc.MiniCactpotNextReset = DateTime.MinValue;
            cc.ChocoboRacingLastCompleted = DateTime.MinValue;
            cc.ChocoboRacingNextReset = DateTime.MinValue;
            
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

    private static float GetCompactNumericInputWidth()
        => Math.Max(72f, ImGui.CalcTextSize("00000").X + (ImGui.GetStyle().FramePadding.X * 2f) + 18f);

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
            ImGui.TextDisabled($"Ready now. Weekly reset at {FormatUtc(ResetDetectionService.GetNextWeeklyReset(now))}");
        }
        else
        {
            ImGui.TextDisabled($"Available Friday at {FormatUtc(ResetDetectionService.GetNextFridayAvailability(now))}");
        }
    }

    private static void DrawJumboTaskHint(DateTime lastCompleted, DateTime nextReset)
    {
        if (IsJumboPurchasePendingPayout(lastCompleted, nextReset))
        {
            ImGui.TextDisabled($"Ticket purchased. Payout available at {FormatUtc(nextReset)}");
            return;
        }

        if (ResetDetectionService.TaskIsCompleted(lastCompleted, nextReset))
        {
            ImGui.TextDisabled($"Completed until {FormatUtc(nextReset)}");
            return;
        }

        var now = DateTime.UtcNow;
        var saturdayReset = now.Date.AddHours(9);
        if (now.DayOfWeek == DayOfWeek.Saturday && now >= saturdayReset)
        {
            ImGui.TextDisabled("Ready now.");
        }
        else
        {
            ImGui.TextDisabled($"Available Saturday at {FormatUtc(ResetDetectionService.GetNextSaturdayAvailability(now))}");
        }
    }

    private static bool IsJumboPurchasePendingPayout(DateTime lastCompleted, DateTime nextReset)
    {
        if (!ResetDetectionService.TaskIsCompleted(lastCompleted, nextReset))
            return false;

        var now = DateTime.UtcNow;
        return nextReset > now &&
               nextReset <= ResetDetectionService.GetNextSaturdayAvailability(now) &&
               nextReset.DayOfWeek == DayOfWeek.Saturday;
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
