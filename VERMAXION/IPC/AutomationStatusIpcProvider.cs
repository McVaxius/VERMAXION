using System;
using System.Text.Json;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using VERMAXION.Models;

namespace VERMAXION.IPC;

public sealed class AutomationStatusIpcProvider : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ICallGateProvider<string> provider;
    private readonly Func<AutomationStatus> getStatus;

    public AutomationStatusIpcProvider(
        IDalamudPluginInterface pluginInterface,
        Func<AutomationStatus> getStatus)
    {
        this.getStatus = getStatus;
        provider = pluginInterface.GetIpcProvider<string>(AutomationStatusContract.Channel);
        provider.RegisterFunc(GetStatusJson);
    }

    private string GetStatusJson()
        => JsonSerializer.Serialize(getStatus(), JsonOptions);

    public void Dispose()
        => provider.UnregisterFunc();
}
