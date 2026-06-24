using System;
using Dalamud.Plugin.Services;
using VERMAXION.Models;

namespace VERMAXION.Services;

public sealed class LootGoblinMapGatherManualRunCoordinator
{
    private readonly IPluginLog log;
    private readonly ConfigManager configManager;
    private readonly LootGoblinMapGatherService service;
    private readonly LootGoblinMapGatherManualRunTracker tracker = new();

    public LootGoblinMapGatherManualRunCoordinator(
        IPluginLog log,
        ConfigManager configManager,
        LootGoblinMapGatherService service)
    {
        this.log = log;
        this.configManager = configManager;
        this.service = service;
    }

    public bool IsActive => tracker.IsTracking && service.IsActive;

    public LootGoblinMapGatherResponse Start(bool engineIsRunning)
    {
        if (engineIsRunning)
            return LootGoblinMapGatherResponse.Rejected("VERMAXION engine is already running. Wait for it to finish or use Full Stop.");

        configManager.SaveCurrentAccount();
        var characterKey = configManager.CurrentCharacterKey;
        var response = service.Start(configManager.GetActiveConfig());
        tracker.Begin(characterKey, response);
        HandleTerminalResponse(response);
        return response;
    }

    public void Update()
    {
        if (!tracker.IsTracking)
            return;

        service.Update();
        HandleTerminalResponse(service.LastResponse);
    }

    public void Cancel()
    {
        service.Cancel();
        tracker.Cancel();
    }

    public void Reset()
    {
        tracker.Cancel();
        service.Reset();
    }

    private void HandleTerminalResponse(LootGoblinMapGatherResponse response)
    {
        if (!response.Terminal)
            return;

        if (!tracker.Complete(response, configManager.CurrentCharacterKey))
            return;

        var completedAt = DateTime.UtcNow;
        var config = configManager.GetActiveConfig();
        config.LootGoblinMapGatherLastCompleted = completedAt;
        config.LootGoblinMapGatherNextReset = ResetDetectionService.GetNextDailyReset(completedAt);
        configManager.SaveCurrentAccount();
        log.Information($"[LootGoblinMapGather] Persisted manual completion for {configManager.CurrentCharacterKey}");
        service.Reset();
    }
}
