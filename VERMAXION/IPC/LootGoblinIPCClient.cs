using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using VERMAXION.Models;

namespace VERMAXION.IPC;

public sealed class LootGoblinIPCClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly IPluginLog log;
    private readonly ICallGateSubscriber<bool> isReadySubscriber;
    private readonly ICallGateSubscriber<string> getGatherableMapsSubscriber;
    private readonly ICallGateSubscriber<string, string> startMapGatherSubscriber;
    private readonly ICallGateSubscriber<string, string> getMapGatherStatusSubscriber;
    private readonly ICallGateSubscriber<string, string> cancelMapGatherSubscriber;
    private IReadOnlyList<LootGoblinMapInfo> cachedMaps = Array.Empty<LootGoblinMapInfo>();
    private DateTime mapsCacheExpiresAtUtc = DateTime.MinValue;

    public LootGoblinIPCClient(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.log = log;
        isReadySubscriber = pluginInterface.GetIpcSubscriber<bool>("LootGoblin.IsReady");
        getGatherableMapsSubscriber = pluginInterface.GetIpcSubscriber<string>("LootGoblin.GetGatherableMapsJson");
        startMapGatherSubscriber = pluginInterface.GetIpcSubscriber<string, string>("LootGoblin.StartMapGatherJson");
        getMapGatherStatusSubscriber = pluginInterface.GetIpcSubscriber<string, string>("LootGoblin.GetMapGatherStatusJson");
        cancelMapGatherSubscriber = pluginInterface.GetIpcSubscriber<string, string>("LootGoblin.CancelMapGatherJson");
    }

    public bool IsReady()
    {
        try
        {
            return isReadySubscriber.InvokeFunc();
        }
        catch (Exception ex)
        {
            log.Debug($"[LootGoblin IPC] IsReady failed: {ex.Message}");
            return false;
        }
    }

    public IReadOnlyList<LootGoblinMapInfo> GetGatherableMaps(bool useCache = true)
    {
        var now = DateTime.UtcNow;
        if (useCache && now < mapsCacheExpiresAtUtc)
            return cachedMaps;

        try
        {
            var json = getGatherableMapsSubscriber.InvokeFunc();
            cachedMaps = JsonSerializer.Deserialize<List<LootGoblinMapInfo>>(json, JsonOptions)?
                .OrderBy(map => map.MinLevel)
                .ThenBy(map => map.MapName, StringComparer.OrdinalIgnoreCase)
                .ToList() ?? [];
            mapsCacheExpiresAtUtc = now.AddSeconds(5);
        }
        catch (Exception ex)
        {
            log.Debug($"[LootGoblin IPC] GetGatherableMapsJson failed: {ex.Message}");
            cachedMaps = Array.Empty<LootGoblinMapInfo>();
            mapsCacheExpiresAtUtc = now.AddSeconds(2);
        }

        return cachedMaps;
    }

    public LootGoblinMapGatherResponse StartMapGather(uint itemId, string mapName, bool runAfterGather)
    {
        var request = new LootGoblinMapGatherRequest
        {
            ItemId = itemId,
            MapName = mapName,
            RunAfterGather = runAfterGather,
        };

        try
        {
            var requestJson = JsonSerializer.Serialize(request, JsonOptions);
            var responseJson = startMapGatherSubscriber.InvokeFunc(requestJson);
            return DeserializeResponse(responseJson, LootGoblinMapGatherResponse.Unavailable("LootGoblin returned an unreadable start response."));
        }
        catch (Exception ex)
        {
            log.Warning($"[LootGoblin IPC] StartMapGatherJson failed: {ex.Message}");
            return LootGoblinMapGatherResponse.Unavailable($"LootGoblin IPC unavailable: {ex.Message}");
        }
    }

    public LootGoblinMapGatherResponse GetMapGatherStatus(string requestId)
    {
        try
        {
            var responseJson = getMapGatherStatusSubscriber.InvokeFunc(requestId);
            return DeserializeResponse(responseJson, LootGoblinMapGatherResponse.Unavailable("LootGoblin returned an unreadable status response."));
        }
        catch (Exception ex)
        {
            log.Warning($"[LootGoblin IPC] GetMapGatherStatusJson failed: {ex.Message}");
            return LootGoblinMapGatherResponse.Unavailable($"LootGoblin status IPC unavailable: {ex.Message}");
        }
    }

    public LootGoblinMapGatherResponse CancelMapGather(string requestId)
    {
        try
        {
            var responseJson = cancelMapGatherSubscriber.InvokeFunc(requestId);
            return DeserializeResponse(responseJson, LootGoblinMapGatherResponse.Unavailable("LootGoblin returned an unreadable cancel response."));
        }
        catch (Exception ex)
        {
            log.Warning($"[LootGoblin IPC] CancelMapGatherJson failed: {ex.Message}");
            return LootGoblinMapGatherResponse.Unavailable($"LootGoblin cancel IPC unavailable: {ex.Message}");
        }
    }

    private LootGoblinMapGatherResponse DeserializeResponse(string json, LootGoblinMapGatherResponse fallback)
    {
        try
        {
            return JsonSerializer.Deserialize<LootGoblinMapGatherResponse>(json, JsonOptions) ?? fallback;
        }
        catch (Exception ex)
        {
            log.Warning($"[LootGoblin IPC] Failed to deserialize response: {ex.Message}");
            return fallback;
        }
    }
}
