using System;
using System.Collections.Generic;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using VERMAXION.Models;

namespace VERMAXION.IPC;

public sealed class XADatabaseIPCClient
{
    private readonly IPluginLog log;
    private readonly ICallGateSubscriber<string> getAccountCharacterListJsonSubscriber;

    public XADatabaseIPCClient(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.log = log;
        getAccountCharacterListJsonSubscriber = pluginInterface.GetIpcSubscriber<string>("XA.Database.GetAccountCharacterListJson");
    }

    public IReadOnlyDictionary<string, int> GetFisherLevelsByCharacterKey()
    {
        try
        {
            var json = getAccountCharacterListJsonSubscriber.InvokeFunc();
            return XaFishingRosterParser.ParseFisherLevels(json);
        }
        catch (Exception ex)
        {
            log.Warning($"[XADB] Failed to read Fisher levels from XA.Database.GetAccountCharacterListJson: {ex.Message}");
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
