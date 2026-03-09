using Dalamud.Configuration;
using Dalamud.Plugin;
using System;

namespace Vermaxion;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    // Phase 1: Basic settings
    public int MaxAttempts { get; set; } = 5;
    public bool EnableAutoRetainer { get; set; } = true;
    public int QueueRetryDelay { get; set; } = 5000;
    public int FailureDelay { get; set; } = 3000;

    [NonSerialized]
    private IDalamudPluginInterface? _pluginInterface;

    public void Save()
    {
        _pluginInterface?.SavePluginConfig(this);
    }

    public void Initialize(IDalamudPluginInterface pluginInterface)
    {
        _pluginInterface = pluginInterface;
    }
}

public enum VerminionState
{
    Idle,           // Not running
    Queuing,        // Simulating queue
    InQueuePop,     // Simulating queue pop
    InDuty,         // Simulating in duty
    Failing,        // Simulating failure
    Exiting,        // Simulating exit
    Completed,      // All attempts done
    Error           // Error state
}
