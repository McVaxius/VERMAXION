using System;
using System.Numerics;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Vermaxion.Models;
using Vermaxion.Services;

namespace Vermaxion.Windows;

public class MainWindow : Window
{
    private readonly GoldSaucerService _goldSaucerService;
    private readonly CharacterManager _characterManager;
    private readonly Configuration _config;
    private readonly IPluginLog _log;

    private CharacterConfig? _selectedCharacter;
    private string _newCharacterName = "";
    private bool _showAddCharacterDialog = false;

    public MainWindow(GoldSaucerService goldSaucerService, CharacterManager characterManager, Configuration config, IPluginLog log) 
        : base("Vermaxion##MainWindow", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.AlwaysAutoResize)
    {
        _goldSaucerService = goldSaucerService;
        _characterManager = characterManager;
        _config = config;
        _log = log;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(800, 600),
            MaximumSize = new Vector2(1200, 900)
        };

        _characterManager.CharacterChanged += OnCharacterChanged;
    }

    public void Dispose() { }

    public override void Draw()
    {
        ImGui.Text("Vermaxion - Gold Saucer Automation");
        ImGui.Separator();

        // Two-column layout
        DrawCharacterList();
        ImGui.SameLine();
        DrawCharacterConfiguration();

        ImGui.Separator();
        DrawGlobalSettings();
    }

    private void DrawCharacterList()
    {
        // Left panel - Character list
        ImGui.BeginChild("CharacterList", new Vector2(300, 400), true);

        ImGui.Text("Characters");
        ImGui.Separator();

        // Add character button
        if (ImGui.Button("Add Character"))
        {
            _showAddCharacterDialog = true;
        }

        ImGui.SameLine();
        if (ImGui.Button("Refresh"))
        {
            // Refresh current character detection
            var currentCharacter = _characterManager.GetOrCreateCurrentCharacter();
            if (currentCharacter != null)
            {
                _log.Information($"Detected current character: {currentCharacter.GetDisplayName()}");
            }
        }

        ImGui.Separator();

        // Character list
        var allCharacters = _characterManager.GetAllCharacters();
        foreach (var character in allCharacters)
        {
            var isSelected = _selectedCharacter?.CharacterId == character.CharacterId;
            var isCurrent = _characterManager.CurrentCharacter?.CharacterId == character.CharacterId;
            
            var displayName = character.GetDisplayName();
            if (isCurrent)
                displayName += " (Current)";

            if (ImGui.Selectable(displayName, isSelected))
            {
                SelectCharacter(character);
            }
        }

        ImGui.EndChild();
    }

    private void DrawCharacterConfiguration()
    {
        // Right panel - Character configuration
        ImGui.BeginChild("CharacterConfig", new Vector2(450, 400), true);

        if (_selectedCharacter == null)
        {
            ImGui.Text("Select a character to configure");
            ImGui.EndChild();
            return;
        }

        ImGui.Text($"Configuration for {_selectedCharacter.GetDisplayName()}");
        ImGui.Separator();

        // Mini Cactpot Settings
        if (ImGui.CollapsingHeader("Mini Cactpot"))
        {
            var enabled = _selectedCharacter.MiniCactpotEnabled;
            if (ImGui.Checkbox("Enable Mini Cactpot", ref enabled))
            {
                _selectedCharacter.MiniCactpotEnabled = enabled;
            }
            
            if (_selectedCharacter.MiniCactpotEnabled)
            {
                var strategyItems = new[] { "Random", "Optimal", "Custom" };
                var currentStrategyIndex = System.Math.Min(_selectedCharacter.MiniCactpotStrategy, strategyItems.Length - 1);
                var currentStrategy = strategyItems[currentStrategyIndex];
                if (ImGui.BeginCombo("Strategy", currentStrategy))
                {
                    for (int i = 0; i < strategyItems.Length; i++)
                    {
                        var isSelected = i == _selectedCharacter.MiniCactpotStrategy;
                        if (ImGui.Selectable(strategyItems[i], isSelected))
                        {
                            _selectedCharacter.MiniCactpotStrategy = i;
                        }
                    }
                    ImGui.EndCombo();
                }
                
                var autoSubmit = _selectedCharacter.MiniCactpotAutoSubmit;
                if (ImGui.Checkbox("Auto Submit", ref autoSubmit))
                {
                    _selectedCharacter.MiniCactpotAutoSubmit = autoSubmit;
                }
                
                // Status
                var status = _goldSaucerService.GetAutomationStatus(ActionType.MiniCactpot, _selectedCharacter);
                ImGui.Text($"Status: {status}");
            }
        }

        // Chocobo Race Settings
        if (ImGui.CollapsingHeader("Chocobo Races"))
        {
            var enabled = _selectedCharacter.ChocoboRacesEnabled;
            if (ImGui.Checkbox("Enable Chocobo Races", ref enabled))
            {
                _selectedCharacter.ChocoboRacesEnabled = enabled;
            }
            
            if (_selectedCharacter.ChocoboRacesEnabled)
            {
                var racesCount = _selectedCharacter.ChocoboRacesCount;
                if (ImGui.SliderInt("Races per Day", ref racesCount, 1, 10))
                {
                    _selectedCharacter.ChocoboRacesCount = racesCount;
                }
                
                var bettingStrategyItems = new[] { "Favorite", "Underdog", "Balanced" };
                var currentBettingStrategyIndex = System.Math.Min(_selectedCharacter.ChocoboRaceBettingStrategy, bettingStrategyItems.Length - 1);
                var currentBettingStrategy = bettingStrategyItems[currentBettingStrategyIndex];
                if (ImGui.BeginCombo("Betting Strategy", currentBettingStrategy))
                {
                    for (int i = 0; i < bettingStrategyItems.Length; i++)
                    {
                        var isSelected = i == _selectedCharacter.ChocoboRaceBettingStrategy;
                        if (ImGui.Selectable(bettingStrategyItems[i], isSelected))
                        {
                            _selectedCharacter.ChocoboRaceBettingStrategy = i;
                        }
                    }
                    ImGui.EndCombo();
                }
                
                var autoWatch = _selectedCharacter.ChocoboRaceAutoWatch;
                if (ImGui.Checkbox("Auto Watch Races", ref autoWatch))
                {
                    _selectedCharacter.ChocoboRaceAutoWatch = autoWatch;
                }
                
                // Status
                var status = _goldSaucerService.GetAutomationStatus(ActionType.ChocoboRaces, _selectedCharacter);
                ImGui.Text($"Status: {status}");
            }
        }

        // Jumbo Cactpot Settings
        if (ImGui.CollapsingHeader("Jumbo Cactpot"))
        {
            var enabled = _selectedCharacter.JumboCactpotEnabled;
            if (ImGui.Checkbox("Enable Jumbo Cactpot", ref enabled))
            {
                _selectedCharacter.JumboCactpotEnabled = enabled;
            }
            
            if (_selectedCharacter.JumboCactpotEnabled)
            {
                var autoSubmit = _selectedCharacter.JumboCactpotAutoSubmit;
                if (ImGui.Checkbox("Auto Submit", ref autoSubmit))
                {
                    _selectedCharacter.JumboCactpotAutoSubmit = autoSubmit;
                }
                
                var useCustomNumbers = _selectedCharacter.JumboCactpotUseCustomNumbers;
                if (ImGui.Checkbox("Use Custom Numbers", ref useCustomNumbers))
                {
                    _selectedCharacter.JumboCactpotUseCustomNumbers = useCustomNumbers;
                }
                
                if (_selectedCharacter.JumboCactpotUseCustomNumbers)
                {
                    var customNumbers = _selectedCharacter.JumboCactpotNumbers;
                    ImGui.InputInt("Number 1", ref customNumbers[0]);
                    ImGui.InputInt("Number 2", ref customNumbers[1]);
                    ImGui.InputInt("Number 3", ref customNumbers[2]);
                    _selectedCharacter.JumboCactpotNumbers = customNumbers;
                }
                
                // Status
                var status = _goldSaucerService.GetAutomationStatus(ActionType.JumboCactpot, _selectedCharacter);
                ImGui.Text($"Status: {status}");
            }
        }

        // Varminion Settings
        if (ImGui.CollapsingHeader("Varminion"))
        {
            var enabled = _selectedCharacter.VarminionEnabled;
            if (ImGui.Checkbox("Enable Varminion", ref enabled))
            {
                _selectedCharacter.VarminionEnabled = enabled;
            }
            
            if (_selectedCharacter.VarminionEnabled)
            {
                var attempts = _selectedCharacter.VarminionAttempts;
                if (ImGui.SliderInt("Failure Attempts", ref attempts, 1, 10))
                {
                    _selectedCharacter.VarminionAttempts = attempts;
                }
                
                var reEnableAutoRetainer = _selectedCharacter.VarminionReEnableAutoRetainer;
                if (ImGui.Checkbox("Re-enable AutoRetainer", ref reEnableAutoRetainer))
                {
                    _selectedCharacter.VarminionReEnableAutoRetainer = reEnableAutoRetainer;
                }
                
                var queueDelay = _selectedCharacter.VarminionQueueDelay;
                if (ImGui.SliderInt("Queue Delay (ms)", ref queueDelay, 1000, 10000))
                {
                    _selectedCharacter.VarminionQueueDelay = queueDelay;
                }
                
                var failureDelay = _selectedCharacter.VarminionFailureDelay;
                if (ImGui.SliderInt("Failure Delay (ms)", ref failureDelay, 1000, 10000))
                {
                    _selectedCharacter.VarminionFailureDelay = failureDelay;
                }
                
                // Status
                var status = _goldSaucerService.GetAutomationStatus(ActionType.Varminion, _selectedCharacter);
                ImGui.Text($"Status: {status}");
            }
        }

        // ARPostprocess Rules
        if (ImGui.CollapsingHeader("ARPostprocess Rules"))
        {
            DrawARPostprocessRules();
        }

        // Statistics
        if (ImGui.CollapsingHeader("Statistics"))
        {
            DrawStatistics();
        }

        ImGui.Separator();

        // Save button
        if (ImGui.Button("Save Configuration"))
        {
            _characterManager.UpdateCharacter(_selectedCharacter);
        }

        ImGui.EndChild();
    }

    private void DrawARPostprocessRules()
    {
        ImGui.Text("Trigger Rules:");
        
        for (int i = 0; i < _selectedCharacter!.ARPostprocessRules.Count; i++)
        {
            var rule = _selectedCharacter.ARPostprocessRules[i];
            var enabled = rule.Enabled;
            
            ImGui.PushID($"rule_{i}");
            
            if (ImGui.Checkbox($"##enabled_{i}", ref enabled))
            {
                rule.Enabled = enabled;
            }
            ImGui.SameLine();
            
            ImGui.Text($"{rule.RuleName} - {rule.TriggerType} → {rule.ActionType}");
            
            ImGui.PopID();
        }
        
        if (ImGui.Button("Add Rule"))
        {
            // TODO: Add rule dialog
        }
    }

    private void DrawStatistics()
    {
        var stats = _selectedCharacter!.Statistics;
        
        ImGui.Text("Mini Cactpot:");
        ImGui.Text($"  Attempts: {stats.MiniCactpotAttempts}");
        ImGui.Text($"  Wins: {stats.MiniCactpotWins}");
        ImGui.Text($"  Total Winnings: {stats.MiniCactpotTotalWinnings:N0} MGP");
        
        ImGui.Text("Chocobo Races:");
        ImGui.Text($"  Attempts: {stats.ChocoboRaceAttempts}");
        ImGui.Text($"  Wins: {stats.ChocoboRaceWins}");
        ImGui.Text($"  Total Winnings: {stats.ChocoboRaceTotalWinnings:N0} MGP");
        ImGui.Text($"  Total Bets: {stats.ChocoboRaceTotalBets:N0} MGP");
        
        ImGui.Text("Jumbo Cactpot:");
        ImGui.Text($"  Attempts: {stats.JumboCactpotAttempts}");
        ImGui.Text($"  Wins: {stats.JumboCactpotWins}");
        ImGui.Text($"  Total Winnings: {stats.JumboCactpotTotalWinnings:N0} MGP");
        
        ImGui.Text("Varminion:");
        ImGui.Text($"  Attempts: {stats.VarminionAttempts}");
        ImGui.Text($"  Completions: {stats.VarminionCompletions}");
    }

    private void DrawGlobalSettings()
    {
        ImGui.BeginChild("GlobalSettings", new Vector2(760, 100), true);
        
        ImGui.Text("Global Settings");
        ImGui.Separator();
        
        var enableDebugLogging = _config.GlobalSettings.EnableDebugLogging;
        if (ImGui.Checkbox("Enable Debug Logging", ref enableDebugLogging))
        {
            _config.GlobalSettings.EnableDebugLogging = enableDebugLogging;
        }
        
        var enableNotifications = _config.GlobalSettings.EnableNotifications;
        if (ImGui.Checkbox("Enable Notifications", ref enableNotifications))
        {
            _config.GlobalSettings.EnableNotifications = enableNotifications;
        }
        
        var checkForUpdates = _config.GlobalSettings.CheckForUpdates;
        if (ImGui.Checkbox("Check for Updates", ref checkForUpdates))
        {
            _config.GlobalSettings.CheckForUpdates = checkForUpdates;
        }
        
        var minimizeToTray = _config.GlobalSettings.MinimizeToTray;
        if (ImGui.Checkbox("Minimize to Tray", ref minimizeToTray))
        {
            _config.GlobalSettings.MinimizeToTray = minimizeToTray;
        }
        
        ImGui.SameLine();
        
        if (ImGui.Button("Save Global Settings"))
        {
            _config.Save();
        }
        
        ImGui.EndChild();
    }

    private void SelectCharacter(CharacterConfig character)
    {
        _selectedCharacter = character;
        _config.SelectedCharacter = character.CharacterId.ToString();
        _config.Save();
    }

    private void OnCharacterChanged(object? sender, CharacterChangedEventArgs e)
    {
        if (e.Character != null)
        {
            SelectCharacter(e.Character);
        }
    }

    public override void OnClose()
    {
        _characterManager.CharacterChanged -= OnCharacterChanged;
        base.OnClose();
    }
}
