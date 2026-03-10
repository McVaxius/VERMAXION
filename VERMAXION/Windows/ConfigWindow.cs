using System;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
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
        if (ImGui.CollapsingHeader("Global Settings", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var krangleEnabled = config.KrangleEnabled;
            if (ImGui.Checkbox("Krangle Names", ref krangleEnabled))
            {
                config.KrangleEnabled = krangleEnabled;
                if (!krangleEnabled) KrangleService.ClearCache();
                config.Save();
            }
            ImGui.SameLine();
            ImGui.TextDisabled("(?)");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Replace character names with exercise words for screenshots");

            var dtrEnabled = config.DtrBarEnabled;
            if (ImGui.Checkbox("DTR Bar Entry", ref dtrEnabled))
            {
                config.DtrBarEnabled = dtrEnabled;
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
        ImGui.BulletText("Author: McVaxius");
    }

    private void DrawAccountSelector(ConfigManager configManager)
    {
        var accounts = configManager.Accounts;
        var currentId = configManager.CurrentAccountId;

        ImGui.Text("Account:");
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
                ImGui.Text("Account Alias:");
                ImGui.InputText("##EditAlias", ref editAccountAlias, 64);
                if (ImGui.Button("Save") && !string.IsNullOrWhiteSpace(editAccountAlias))
                {
                    configManager.UpdateAccountAlias(editAccountAlias);
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }
        }
    }

    private void DrawCharacterList(ConfigManager configManager)
    {
        ImGui.Text("Characters");
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
            if (ImGui.Selectable(currentChar, isCurrentSelected))
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
                ? KrangleService.KrangleName(charKey)
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
        var cc = configManager.GetActiveConfig();
        var isDefault = string.IsNullOrEmpty(charKey);

        var displayName = isDefault ? "Default Config" : charKey;
        if (plugin.Configuration.KrangleEnabled && !isDefault)
            displayName = KrangleService.KrangleName(charKey);

        ImGui.Text($"Settings: {displayName}");
        if (isDefault)
            ImGui.TextDisabled("New characters inherit these settings");
        ImGui.Separator();

        var changed = false;

        // Master enable
        var enabled = cc.Enabled;
        if (ImGui.Checkbox("Enabled##CharEnabled", ref enabled))
        {
            cc.Enabled = enabled;
            changed = true;
        }

        ImGui.Spacing();

        // --- Feature Toggles ---
        if (ImGui.CollapsingHeader("Every AR PostProcess", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var fcBuff = cc.EnableFCBuffRefill;
            if (ImGui.Checkbox("FC Buff Refill (Seal Sweetener)", ref fcBuff))
            {
                cc.EnableFCBuffRefill = fcBuff;
                changed = true;
            }
            if (fcBuff)
            {
                ImGui.Indent();
                var attempts = cc.FCBuffPurchaseAttempts;
                if (ImGui.SliderInt("Max Purchase Attempts", ref attempts, 1, 30))
                {
                    cc.FCBuffPurchaseAttempts = attempts;
                    changed = true;
                    // Save immediately on slider change
                    configManager.SaveCurrentAccount();
                }
                
                // FC Points threshold
                var minPoints = cc.FCBuffMinPoints;
                if (ImGui.InputInt("Min FC Points", ref minPoints))
                {
                    cc.FCBuffMinPoints = Math.Max(0, minPoints);
                    changed = true;
                    // Save immediately on input change
                    configManager.SaveCurrentAccount();
                }
                
                // Gil threshold
                var minGil = cc.FCBuffMinGil;
                if (ImGui.InputInt("Min Gil", ref minGil))
                {
                    cc.FCBuffMinGil = Math.Max(0, minGil);
                    changed = true;
                    // Save immediately on input change
                    configManager.SaveCurrentAccount();
                }
                
                ImGui.Unindent();
            }

            var henchman = cc.EnableHenchmanManagement;
            if (ImGui.Checkbox("Henchman Disable/Enable", ref henchman))
            {
                cc.EnableHenchmanManagement = henchman;
                changed = true;
            }
            ImGui.SameLine();
            ImGui.TextDisabled("(?)");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Stop Henchman before tasks, restart after");

            var minionRoulette = cc.EnableMinionRoulette;
            if (ImGui.Checkbox("Minion Roulette", ref minionRoulette))
            {
                cc.EnableMinionRoulette = minionRoulette;
                changed = true;
            }
            ImGui.SameLine();
            ImGui.TextDisabled("(?)");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Fire off /minion roulette once per AR postprocess");

            var seasonalGear = cc.EnableSeasonalGearRoulette;
            if (ImGui.Checkbox("Seasonal Gear Roulette", ref seasonalGear))
            {
                cc.EnableSeasonalGearRoulette = seasonalGear;
                changed = true;
            }
            ImGui.SameLine();
            ImGui.TextDisabled("(?)");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Randomly equip seasonal event gear for a fun ensemble each AR run");

            var gearUpdater = cc.EnableGearUpdater;
            if (ImGui.Checkbox("Gear Updater", ref gearUpdater))
            {
                cc.EnableGearUpdater = gearUpdater;
                changed = true;
            }
            ImGui.SameLine();
            ImGui.TextDisabled("(?)");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Cycle through all unlocked jobs: auto equip recommended gear and save gearset (2s intervals)");
        }

        if (ImGui.CollapsingHeader("Weekly Tasks", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var verminion = cc.EnableVerminionQueue;
            if (ImGui.Checkbox("Lord of Verminion (5 fails)", ref verminion))
            {
                cc.EnableVerminionQueue = verminion;
                changed = true;
            }
            if (cc.VerminionCompletedThisWeek)
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0, 1, 0, 1), "[Done]");
            }

            var jumbo = cc.EnableJumboCactpot;
            if (ImGui.Checkbox("Jumbo Cactpot (Saturday)", ref jumbo))
            {
                cc.EnableJumboCactpot = jumbo;
                changed = true;
            }
            if (cc.JumboCactpotCompletedThisWeek)
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0, 1, 0, 1), "[Done]");
            }
        }

        if (ImGui.CollapsingHeader("Daily Tasks", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var mini = cc.EnableMiniCactpot;
            if (ImGui.Checkbox("Mini Cactpot", ref mini))
            {
                cc.EnableMiniCactpot = mini;
                changed = true;
            }
            ImGui.SameLine();
            ImGui.TextDisabled("(?)");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("To enable: type /saucy, go to \"Other Games\" -> [x] Enable Auto Mini-Cactpot.\nVermaxion will teleport to Gold Saucer, walk to the Cactpot Board, and start the interaction.\nSaucy handles the actual mini-game solving.");
            if (cc.MiniCactpotCompletedToday)
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0, 1, 0, 1), "[Done]");
            }

            var chocobo = cc.EnableChocoboRacing;
            if (ImGui.Checkbox("Chocobo Racing (via Chocoholic)", ref chocobo))
            {
                cc.EnableChocoboRacing = chocobo;
                changed = true;
            }
            if (chocobo)
            {
                ImGui.Indent();
                var races = cc.ChocoboRacesPerDay;
                if (ImGui.SliderInt("Races Per Day", ref races, 1, 20))
                {
                    cc.ChocoboRacesPerDay = races;
                    changed = true;
                    // Save immediately on slider change
                    configManager.SaveCurrentAccount();
                }
                ImGui.Unindent();
            }
            if (cc.ChocoboRacingCompletedToday)
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0, 1, 0, 1), "[Done]");
            }
        }

        ImGui.Spacing();
        ImGui.Separator();

        // Reset buttons
        if (ImGui.Button("Reset Weekly Flags"))
        {
            cc.VerminionCompletedThisWeek = false;
            cc.JumboCactpotCompletedThisWeek = false;
            changed = true;
        }
        ImGui.SameLine();
        if (ImGui.Button("Reset Daily Flags"))
        {
            cc.MiniCactpotCompletedToday = false;
            cc.ChocoboRacingCompletedToday = false;
            changed = true;
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
            alias = KrangleService.KrangleName(alias);

        return string.IsNullOrWhiteSpace(alias) ? accountId : $"{alias} ({accountId})";
    }
}
