extern alias live;
extern alias host;

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using CofferObservation = live::VERMAXION.Services.CofferObservation;
using CofferOpeningService = live::VERMAXION.Services.CofferOpeningService;
using CofferOpeningReply = live::VERMAXION.Services.CofferOpeningReply;
using CofferOpeningIpcProvider = live::VERMAXION.IPC.CofferOpeningIpcProvider;
using IDalamudPluginInterface = host::Dalamud.Plugin.IDalamudPluginInterface;

namespace VERMAXION.Tests;

public sealed class CofferOpeningTests
{
    private sealed class Fixture : IDisposable
    {
        internal DateTime Now = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        internal CofferObservation Frame = new(123, true, true, 4, 3, 10, true);
        internal readonly CofferOpeningService Service;
        internal int Uses;
        internal bool AcceptUse = true, ThrowUse;
        internal Fixture() => Service = new(_ => Frame, _ =>
        {
            Uses++;
            if (ThrowUse) throw new InvalidOperationException("Injected use uncertainty");
            return AcceptUse;
        }, () => Now);
        internal void Tick(int milliseconds = 100) { Now = Now.AddMilliseconds(milliseconds); Service.Update(); }
        public void Dispose() => Service.Dispose();
    }

    [Fact]
    public void FixedSelectedStackWaitsForEveryReceiptAndNeverConsumesTheOtherStack()
    {
        using var f = new Fixture();
        var start = f.Service.Start(52450);
        Assert.True(start.Accepted);
        Assert.Equal(3, start.Status.Requested);
        f.Tick(); f.Tick();
        Assert.Equal(1, f.Uses);
        Assert.Equal(0, f.Service.Status.Opened);
        f.Frame = f.Frame with { Count = 3, FreeSlots = 9 };
        f.Tick(); // A receipt does not bypass the native action spacing.
        Assert.Equal(1, f.Uses);
        f.Tick(1000);
        Assert.Equal(2, f.Uses);
        f.Frame = f.Frame with { Count = 2, FreeSlots = 8 };
        f.Tick(1000);
        Assert.Equal(3, f.Uses);
        f.Frame = f.Frame with { Count = 1, FreeSlots = 7 };
        f.Tick(); f.Tick(2000);
        Assert.Equal("Completed", f.Service.Status.State);
        Assert.Equal(3, f.Service.Status.Opened);
        Assert.Equal(3, f.Uses);
        Assert.False(f.Service.Stop(start.OperationToken).Accepted);
    }

    [Fact]
    public void ActiveCoffersPreventAutomaticFishingStartupBeforeClientWork()
    {
        using var f = new Fixture();
        Assert.True(f.Service.Start(52450).Accepted);
        var plugin = (live::VERMAXION.Plugin)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(live::VERMAXION.Plugin));
        typeof(live::VERMAXION.Plugin).GetProperty("CofferOpening")!.SetValue(plugin, f.Service);
        Assert.False(((live::VERMAXION.Services.IFishingStartupRuntime)plugin).CanInitiateStartup);
        Assert.Equal(0, f.Uses);
        Assert.True(f.Service.IsActive);
    }

    [Fact]
    public void ContextRequestCanSelectALaterLargerStackOfTheSameItem()
    {
        using var f = new Fixture();
        f.Frame = f.Frame with { FirstStackCount = 1 };
        var reply = f.Service.Start(52450, 3);
        Assert.True(reply.Accepted);
        Assert.Equal(3, reply.Status.Requested);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ZeroOrOneFreeSlotsNeverSubmitAnItem(int slots)
    {
        using var f = new Fixture();
        f.Frame = f.Frame with { FreeSlots = slots };
        Assert.False(f.Service.Start(52450).Accepted);
        f.Tick();
        Assert.Equal(0, f.Uses);
    }

    [Fact]
    public void LiveCapacityChangesAndStricterRequirementsStopBeforeAnotherUse()
    {
        using var f = new Fixture();
        Assert.True(f.Service.Start(52450).Accepted);
        f.Tick();
        f.Frame = f.Frame with { Count = 3, FreeSlots = 1 };
        f.Tick(); f.Tick(2000);
        Assert.Equal("Stopped", f.Service.Status.State);
        Assert.Equal(1, f.Service.Status.Opened);
        Assert.Equal(1, f.Uses);
        f.Frame = f.Frame with { FreeSlots = 4, RequiredFreeSlots = 5 };
        Assert.False(f.Service.Start(52450).Accepted);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("inventory")]
    [InlineData("owner")]
    [InlineData("conflict")]
    [InlineData("quantity")]
    public void InvalidAdmissionNeverUsesInventory(string reason)
    {
        using var f = new Fixture();
        f.Frame = reason switch
        {
            "unknown" => f.Frame with { Eligible = false },
            "inventory" => f.Frame with { Readable = false },
            "owner" => f.Frame with { CharacterId = 0 },
            "conflict" => f.Frame with { Blocker = "Another automation owns the character." },
            _ => f.Frame,
        };
        Assert.False(f.Service.Start(52450, reason == "quantity" ? 99 : 0).Accepted);
        f.Tick(); Assert.Equal(0, f.Uses);
    }

    [Fact]
    public void DuplicateForeignAndStaleRequestsCannotReplaceOrStopAnOwnedRun()
    {
        using var f = new Fixture();
        var first = f.Service.Start(52450);
        Assert.False(f.Service.Start(32161).Accepted);
        Assert.False(f.Service.Stop("foreign").Accepted);
        Assert.True(f.Service.IsActive);
        Assert.True(f.Service.Stop(first.OperationToken).Accepted);
        Assert.True(f.Service.Start(52450).Accepted);
        Assert.False(f.Service.Stop(first.OperationToken).Accepted);
        f.Service.Stop(); f.Tick();
        Assert.Equal(0, f.Uses);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("rejected")]
    [InlineData("exception")]
    [InlineData("changed")]
    public void UncertainOrRejectedUseNeverReplaysAutomatically(string reason)
    {
        using var f = new Fixture();
        f.AcceptUse = reason != "rejected";
        f.ThrowUse = reason == "exception";
        Assert.True(f.Service.Start(52450).Accepted);
        f.Tick();
        if (reason == "changed") f.Frame = f.Frame with { Count = 2 };
        f.Tick(7000); f.Tick(20000);
        Assert.Equal("Blocked", f.Service.Status.State);
        Assert.Equal(1, f.Uses);
        Assert.Equal(0, f.Service.Status.Opened);
    }

    [Theory]
    [InlineData("logout")]
    [InlineData("character")]
    [InlineData("conflict")]
    [InlineData("unreadable")]
    [InlineData("stop")]
    [InlineData("unload")]
    public void LifecycleLossCannotSubmitAnotherUse(string reason)
    {
        using var f = new Fixture();
        Assert.True(f.Service.Start(52450).Accepted);
        f.Tick();
        f.Frame = reason switch
        {
            "logout" => f.Frame with { CharacterId = 0 },
            "character" => f.Frame with { CharacterId = 456 },
            "conflict" => f.Frame with { Blocker = "Another automation owns the character." },
            "unreadable" => f.Frame with { Readable = false },
            _ => f.Frame,
        };
        if (reason == "stop") f.Service.Stop();
        if (reason == "unload") f.Service.Dispose();
        f.Tick(1000); f.Tick(10000);
        Assert.False(f.Service.IsActive);
        Assert.Equal(1, f.Uses);
        if (reason == "unload") Assert.False(f.Service.Start(52450).Accepted);
    }

    [Fact]
    public void NativeReadinessWaitsWithoutSubmittingAndThenStopsAtItsBound()
    {
        using var f = new Fixture();
        f.Frame = f.Frame with { CanUse = false };
        Assert.True(f.Service.Start(52450).Accepted);
        f.Tick(6999); Assert.True(f.Service.IsActive);
        f.Tick(101); Assert.False(f.Service.IsActive);
        Assert.Equal(0, f.Uses);
    }

    [Fact]
    public async Task IpcDispatchesMutationsKeepsTheStopTokenPrivateAndUnregisters()
    {
        using var f = new Fixture();
        var gates = new Dictionary<string, GateProxy>();
        var host = DispatchProxy.Create<IDalamudPluginInterface, GateProxy>();
        ((GateProxy)(object)host).Handler = (method, args) =>
        {
            var gate = DispatchProxy.Create(method.ReturnType, typeof(GateProxy));
            gates.Add((string)args![0]!, (GateProxy)gate);
            return gate;
        };
        var dispatches = 0;
        using var provider = new CofferOpeningIpcProvider(host, f.Service, id => f.Service.Start(id), action =>
        { dispatches++; return Task.FromResult(action()); });
        var start = (Func<uint, Task<string>>)gates[CofferOpeningIpcProvider.StartChannel].Callback!;
        var stop = (Func<string, Task<string>>)gates[CofferOpeningIpcProvider.StopChannel].Callback!;
        var read = (Func<string>)gates[CofferOpeningIpcProvider.StatusChannel].Callback!;
        var reply = JsonDocument.Parse(await start(52450));
        var token = reply.RootElement.GetProperty("operationToken").GetString()!;
        Assert.True(reply.RootElement.GetProperty("accepted").GetBoolean());
        var snapshot = read();
        Assert.DoesNotContain(token, snapshot);
        Assert.DoesNotContain("character", snapshot, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("account", snapshot, StringComparison.OrdinalIgnoreCase);
        Assert.False(JsonDocument.Parse(await stop("foreign")).RootElement.GetProperty("accepted").GetBoolean());
        Assert.True(JsonDocument.Parse(await stop(token)).RootElement.GetProperty("accepted").GetBoolean());
        Assert.Equal(3, dispatches);
        provider.Dispose();
        Assert.All(gates.Values, gate => Assert.Null(gate.Callback));
    }

    [Fact]
    public async Task UnloadRejectsAnIpcMutationAlreadyQueuedOnTheFramework()
    {
        using var f = new Fixture();
        var gates = new Dictionary<string, GateProxy>();
        var host = DispatchProxy.Create<IDalamudPluginInterface, GateProxy>();
        ((GateProxy)(object)host).Handler = (method, args) =>
        {
            var gate = DispatchProxy.Create(method.ReturnType, typeof(GateProxy));
            gates.Add((string)args![0]!, (GateProxy)gate);
            return gate;
        };
        var completion = new TaskCompletionSource<CofferOpeningReply>();
        Func<CofferOpeningReply>? queued = null;
        using var provider = new CofferOpeningIpcProvider(host, f.Service, id => f.Service.Start(id), action =>
        { queued = action; return completion.Task; });
        var start = (Func<uint, Task<string>>)gates[CofferOpeningIpcProvider.StartChannel].Callback!;
        var pending = start(52450);
        Assert.False(pending.IsCompleted);
        provider.Dispose();
        completion.SetResult(queued!());
        Assert.False(JsonDocument.Parse(await pending).RootElement.GetProperty("accepted").GetBoolean());
        Assert.False(f.Service.IsActive);
        f.Tick(); Assert.Equal(0, f.Uses);
    }

    public class GateProxy : DispatchProxy
    {
        internal Delegate? Callback;
        internal Func<MethodInfo, object?[]?, object?>? Handler;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == "RegisterFunc") { Callback = (Delegate)args![0]!; return null; }
            if (method.Name == "UnregisterFunc") { Callback = null; return null; }
            return Handler?.Invoke(method, args);
        }
    }
}
