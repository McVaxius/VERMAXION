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
    private readonly ICallGateSubscriber<string> getVersionSubscriber;

    public XADatabaseIPCClient(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.log = log;
        getAccountCharacterListJsonSubscriber = pluginInterface.GetIpcSubscriber<string>("XA.Database.GetAccountCharacterListJson");
        getVersionSubscriber = pluginInterface.GetIpcSubscriber<string>("XA.Database.GetVersion");
    }

    public XaFishingRosterSnapshot GetFishingRoster()
    {
        try
        {
            var json = getAccountCharacterListJsonSubscriber.InvokeFunc();
            var roster = XaFishingRosterParser.Parse(json);
            if (!roster.IsUsable)
            {
                var xadbVersion = ReadVersionForDiagnostics();
                log.Warning(
                    $"[XADB] Fisher roster rejected: status={roster.Status}, detail={roster.Detail}, " +
                    $"xadbVersion={xadbVersion}");
            }

            return roster;
        }
        catch (Exception ex)
        {
            var xadbVersion = ReadVersionForDiagnostics();
            log.Warning(
                $"[XADB] Failed to read Fisher levels from XA.Database.GetAccountCharacterListJson: {ex.Message}; " +
                $"xadbVersion={xadbVersion}");
            return XaFishingRosterSnapshot.Failure(
                XaFishingRosterReadStatus.IpcFailure,
                $"XA.Database.GetAccountCharacterListJson failed: {ex.Message}; XADB version: {xadbVersion}");
        }
    }

    private string ReadVersionForDiagnostics()
    {
        try
        {
            var version = getVersionSubscriber.InvokeFunc();
            return string.IsNullOrWhiteSpace(version) ? "unknown" : version.Trim();
        }
        catch (Exception ex)
        {
            return $"unavailable ({ex.Message})";
        }
    }
}
