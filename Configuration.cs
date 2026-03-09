using Dalamud.Configuration;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;
using Vermaxion.Models;

namespace Vermaxion;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    // Character-based configuration
    public Dictionary<string, CharacterConfig> CharacterConfigs { get; set; } = new();
    public string SelectedCharacter { get; set; } = "";

    // Global settings
    public GlobalConfig GlobalSettings { get; set; } = new();

    // Runtime state (not saved)
    [field: NonSerialized]
    public VerminionState CurrentState { get; set; } = VerminionState.Idle;

    public void Save()
    {
        PluginInterface!.SavePluginConfig(this);
    }

    public void Initialize(IDalamudPluginInterface pluginInterface)
    {
        PluginInterface = pluginInterface;
    }

    public IDalamudPluginInterface? PluginInterface { get; private set; }
}

[Serializable]
public class GlobalConfig
{
    public bool EnableDebugLogging { get; set; } = false;
    public bool EnableNotifications { get; set; } = true;
    public bool CheckForUpdates { get; set; } = true;
    public int UpdateCheckIntervalHours { get; set; } = 24;
    public bool MinimizeToTray { get; set; } = false;
    public bool StartMinimized { get; set; } = false;
    public string DefaultTheme { get; set; } = "Default";
}

public enum VerminionState
{
    Idle,           // Not running
    Queuing,        // In duty finder queue
    InQueuePop,     // Queue popped, waiting to accept
    InDuty,         // Currently in Varminion duty
    Failing,        // Intentionally failing the duty
    Exiting,        // Leaving the duty
    Completed,      // All attempts done
    Error           // Error state
}
