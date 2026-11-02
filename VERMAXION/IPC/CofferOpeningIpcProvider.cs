using System;
using System.Text.Json;
using System.Threading.Tasks;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using VERMAXION.Services;

namespace VERMAXION.IPC;

public sealed class CofferOpeningIpcProvider : IDisposable
{
    public const string StartChannel = "VERMAXION.Coffers.Start";
    public const string StopChannel = "VERMAXION.Coffers.Stop";
    public const string StatusChannel = "VERMAXION.Coffers.GetStatus";
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly ICallGateProvider<uint, Task<string>> start;
    private readonly ICallGateProvider<string, Task<string>> stop;
    private readonly ICallGateProvider<string> status;
    private readonly CofferOpeningService service;
    private readonly Func<Func<CofferOpeningReply>, Task<CofferOpeningReply>> dispatch;
    private readonly Func<uint, CofferOpeningReply> startAction;
    private volatile bool disposed;

    public CofferOpeningIpcProvider(IDalamudPluginInterface host, CofferOpeningService service,
        Func<uint, CofferOpeningReply> startAction,
        Func<Func<CofferOpeningReply>, Task<CofferOpeningReply>> dispatch)
    {
        this.service = service; this.startAction = startAction; this.dispatch = dispatch;
        start = host.GetIpcProvider<uint, Task<string>>(StartChannel);
        stop = host.GetIpcProvider<string, Task<string>>(StopChannel);
        status = host.GetIpcProvider<string>(StatusChannel);
        start.RegisterFunc(itemId => Mutate(() => startAction(itemId)));
        stop.RegisterFunc(token => Mutate(() => service.Stop(token)));
        // Status is an immutable snapshot with no operation token or character identity.
        status.RegisterFunc(() => JsonSerializer.Serialize(service.Status, Json));
    }

    private async Task<string> Mutate(Func<CofferOpeningReply> action)
    {
        var result = await dispatch(() => disposed
            ? new(false, "", "Coffer utility is unloaded.", service.Status) : action()).ConfigureAwait(false);
        return JsonSerializer.Serialize(result, Json);
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        start.UnregisterFunc(); stop.UnregisterFunc(); status.UnregisterFunc();
    }
}
