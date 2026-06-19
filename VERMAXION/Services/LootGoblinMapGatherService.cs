using System;
using Dalamud.Plugin.Services;
using VERMAXION.IPC;
using VERMAXION.Models;

namespace VERMAXION.Services;

public sealed class LootGoblinMapGatherService
{
    private readonly IPluginLog log;
    private readonly LootGoblinIPCClient ipcClient;
    private string activeRequestId = string.Empty;

    public LootGoblinMapGatherService(IPluginLog log, LootGoblinIPCClient ipcClient)
    {
        this.log = log;
        this.ipcClient = ipcClient;
    }

    public bool IsActive => State is LootGoblinMapGatherServiceState.Starting or LootGoblinMapGatherServiceState.Running;
    public bool IsComplete => State == LootGoblinMapGatherServiceState.Complete;
    public bool IsFailed => State == LootGoblinMapGatherServiceState.Failed;
    public LootGoblinMapGatherServiceState State { get; private set; } = LootGoblinMapGatherServiceState.Idle;
    public string StatusText { get; private set; } = "Idle";
    public LootGoblinMapGatherResponse LastResponse { get; private set; } = new();

    public LootGoblinMapGatherResponse Start(CharacterConfig config)
    {
        if (IsActive)
            return LastResponse;

        State = LootGoblinMapGatherServiceState.Starting;
        StatusText = "Starting LootGoblin map gather...";
        var maps = ipcClient.GetGatherableMaps();
        var selectedMap = FindMap(config.LootGoblinMapGatherItemId, maps);
        var mapName = selectedMap?.MapName ?? string.Empty;

        var response = ipcClient.StartMapGather(
            config.LootGoblinMapGatherItemId,
            mapName,
            config.LootGoblinMapGatherRunAfterGather);
        LastResponse = response;
        StatusText = string.IsNullOrWhiteSpace(response.Message) ? response.State : response.Message;

        if (!response.Accepted)
        {
            State = LootGoblinMapGatherServiceState.Failed;
            log.Warning($"[LootGoblinMapGather] Start rejected: {StatusText}");
            return response;
        }

        if (response.Terminal)
        {
            State = response.Success ? LootGoblinMapGatherServiceState.Complete : LootGoblinMapGatherServiceState.Failed;
            log.Information($"[LootGoblinMapGather] Start returned terminal status: state={response.State}, success={response.Success}, message={response.Message}");
            return response;
        }

        activeRequestId = response.RequestId;
        State = LootGoblinMapGatherServiceState.Running;
        log.Information($"[LootGoblinMapGather] Accepted request {activeRequestId}: map={response.MapName} ({response.ItemId}), runAfterGather={response.RunAfterGather}");
        return response;
    }

    public void Update()
    {
        if (!IsActive || string.IsNullOrWhiteSpace(activeRequestId))
            return;

        var response = ipcClient.GetMapGatherStatus(activeRequestId);
        LastResponse = response;
        StatusText = string.IsNullOrWhiteSpace(response.Message) ? response.State : response.Message;

        if (!response.Terminal)
            return;

        State = response.Success ? LootGoblinMapGatherServiceState.Complete : LootGoblinMapGatherServiceState.Failed;
        activeRequestId = string.Empty;

        if (response.Success)
            log.Information($"[LootGoblinMapGather] Completed: {StatusText}");
        else
            log.Warning($"[LootGoblinMapGather] Failed: {StatusText}");
    }

    public void Cancel()
    {
        if (string.IsNullOrWhiteSpace(activeRequestId))
        {
            Reset();
            return;
        }

        LastResponse = ipcClient.CancelMapGather(activeRequestId);
        StatusText = string.IsNullOrWhiteSpace(LastResponse.Message) ? "Cancelled" : LastResponse.Message;
        State = LootGoblinMapGatherServiceState.Cancelled;
        activeRequestId = string.Empty;
        log.Information($"[LootGoblinMapGather] Cancel requested: {StatusText}");
    }

    public void Reset()
    {
        State = LootGoblinMapGatherServiceState.Idle;
        StatusText = "Idle";
        activeRequestId = string.Empty;
        LastResponse = new LootGoblinMapGatherResponse();
    }

    private static LootGoblinMapInfo? FindMap(uint itemId, System.Collections.Generic.IEnumerable<LootGoblinMapInfo> maps)
    {
        foreach (var map in maps)
        {
            if (map.ItemId == itemId)
                return map;
        }

        return null;
    }
}
