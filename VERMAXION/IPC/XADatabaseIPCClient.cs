using System;
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

    public XaFishingRosterSnapshot GetFishingRoster()
    {
        try
        {
            var json = getAccountCharacterListJsonSubscriber.InvokeFunc();
            var roster = XaFishingRosterParser.Parse(json);
            if (!roster.IsUsable)
            {
                log.Warning(
                    $"[XADB] Fisher roster rejected: status={roster.Status}, detail={roster.Detail}");
            }

            return roster;
        }
        catch (Exception ex)
        {
            log.Warning($"[XADB] Failed to read Fisher levels from XA.Database.GetAccountCharacterListJson: {ex.Message}");
            return XaFishingRosterSnapshot.Failure(
                XaFishingRosterReadStatus.IpcFailure,
                ex.Message);
        }
    }
}
