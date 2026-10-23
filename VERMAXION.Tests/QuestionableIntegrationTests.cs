extern alias live;
extern alias host;

using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;
using Ipc = host::Dalamud.Plugin.IDalamudPluginInterface;

namespace VERMAXION.Tests;

public sealed class QuestionableIntegrationTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Type Gathering = typeof(live::VERMAXION.CustomDeliveries.DeliveryRoute).Assembly
        .GetType("VERMAXION.CustomDeliveries.WigglyGathering", true)!;

    [Fact]
    public void GatheringConvertsBothDtosThroughDalamudAndVerifiesInventoryTotals()
    {
        var fixture = new Provider();
        var gathering = fixture.CreateGathering();
        Call(gathering, "Start");
        Assert.Equal(4173u, fixture.Request!.ItemId);
        Assert.Equal(16, fixture.Request.ClassJob);
        Assert.Equal(500, fixture.Request.Collectability);
        Assert.Equal(2, fixture.Request.Quantity); // One at target quality + one still needed at any qualifying quality.
        Assert.True((bool)Call(gathering, "Update")!);
        fixture.Status!.State = "Gathering";
        Assert.True((bool)Call(gathering, "Update")!);
        fixture.Status.State = "Complete";
        fixture.Status.Have = 2;
        Assert.False((bool)Call(gathering, "Update")!);
        Call(gathering, "Stop");
        Assert.Equal(0, fixture.Cancels);
        Assert.Throws<TargetInvocationException>(() => Call(gathering, "Start"));
        Assert.Equal(1, fixture.Starts);
    }

    [Fact]
    public void GatheringCancellationUsesOnlyItsAcknowledgedRequestAndRequiresTerminalReadback()
    {
        var fixture = new Provider();
        var gathering = fixture.CreateGathering();
        Call(gathering, "Start");
        fixture.CancelAccepted = false;
        Assert.Throws<TargetInvocationException>(() => Call(gathering, "Stop"));
        Assert.Equal("owned-gather", fixture.CancelId);
        Assert.Equal(1, fixture.Cancels);
        // A later explicit Stop may reconcile the same request; no polling dispatched another cancel.
        fixture.Status!.State = "Cancelled";
        Call(gathering, "Stop");
        Call(gathering, "Stop");
        Assert.Equal(1, fixture.Cancels);
    }

    [Fact]
    public void GatheringWithAnUncertainStartNeverRedispatchesOrCancelsAnUnacknowledgedOperation()
    {
        var fixture = new Provider { MissingStartResult = true };
        var gathering = fixture.CreateGathering();
        Assert.Throws<TargetInvocationException>(() => Call(gathering, "Start"));
        Assert.Throws<TargetInvocationException>(() => Call(gathering, "Start"));
        Call(gathering, "Stop");
        Assert.Equal(1, fixture.Starts);
        Assert.Equal(0, fixture.Cancels);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GatheringCannotUpdateOrCancelAcrossProviderOrCharacterChanges(bool reload)
    {
        var fixture = new Provider();
        var gathering = fixture.CreateGathering();
        Call(gathering, "Start");
        if (reload) fixture.Plugin = new();
        else fixture.Character = 2;
        Assert.Throws<TargetInvocationException>(() => Call(gathering, "Update"));
        Call(gathering, "Stop");
        Assert.Equal(0, fixture.Cancels);
    }

    [Fact]
    public void GatheringCannotCancelAReplacementEvenWhenItsItemAndQuantityMatch()
    {
        var fixture = new Provider();
        var gathering = fixture.CreateGathering();
        Call(gathering, "Start");
        fixture.Status!.RequestId = "replacement-gather";
        Assert.Throws<TargetInvocationException>(() => Call(gathering, "Update"));
        Call(gathering, "Stop");
        Assert.Equal(0, fixture.Cancels);
    }

    [Theory]
    [InlineData("Failed")]
    [InlineData("Cancelled")]
    [InlineData("Complete")]
    [InlineData("Unsupported")]
    public void GatheringNeverTreatsAnErrorOrInsufficientStockAsCompletion(string state)
    {
        var fixture = new Provider();
        var gathering = fixture.CreateGathering();
        Call(gathering, "Start");
        fixture.Status!.State = state;
        fixture.Status.Have = 0;
        Assert.Throws<TargetInvocationException>(() => Call(gathering, "Update"));
    }

    [Theory]
    [InlineData("unregistered")]
    [InlineData("busy")]
    [InlineData("unknown")]
    public void GatheringRefusesUnavailableOrBusyProvidersBeforeDispatch(string mode)
    {
        var fixture = new Provider();
        if (mode == "unregistered") fixture.Functions.Remove("WigglyQuest.Gathering.Start");
        if (mode == "busy") fixture.Busy = true;
        if (mode == "unknown") fixture.Status = null;
        Assert.Throws<TargetInvocationException>(() => fixture.CreateGathering());
        Assert.Equal(0, fixture.Starts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TemporaryPriorityPreservesExistingEntriesAndRemovesOnlyItsOwnInsertion(bool existing)
    {
        var fixture = new Provider();
        if (existing) fixture.Priority.Add("126");
        var service = CreateStables();
        Call(service, "PrepareOnionPriority", fixture.Ipc);
        Assert.Equal(!existing, OwnsPriority(service));
        Assert.Equal(existing ? 0 : 1, fixture.Inserts);
        Call(service, "ReleaseOnionPriority", fixture.Ipc);
        Call(service, "ReleaseOnionPriority", fixture.Ipc);
        Assert.Equal(existing ? new[] { "keep", "126" } : new[] { "keep" }, fixture.Priority);
        Assert.Equal(existing ? 0 : 1, fixture.Removes);
        Assert.False(OwnsPriority(service));
    }

    [Theory]
    [InlineData("saved")]
    [InlineData("unregistered")]
    [InlineData("unknown")]
    public void TemporaryPriorityRefusesUnreadableOrPersistedStateWithoutMutation(string mode)
    {
        var fixture = new Provider { PersistPriority = mode == "saved" };
        if (mode == "unregistered") fixture.Functions.Remove("WigglyQuest.Priority.GetPersistBetweenSessions");
        if (mode == "unknown") fixture.Functions["WigglyQuest.Priority.Get"] = (Func<List<string>?>)(() => null);
        var service = CreateStables();
        Assert.Throws<TargetInvocationException>(() => Call(service, "PrepareOnionPriority", fixture.Ipc));
        Assert.False(OwnsPriority(service));
        Assert.Equal(0, fixture.Inserts);
        Assert.Equal(new[] { "keep" }, fixture.Priority);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TemporaryPriorityCapturesOwnershipBeforeARejectedOrThrowingInsertion(bool throws)
    {
        var fixture = new Provider();
        fixture.Functions["WigglyQuest.InsertQuestPriority"] = (Func<int, string, bool>)((_, quest) =>
        {
            fixture.Priority.Add(quest);
            if (throws) throw new InvalidOperationException("Fixture acknowledgement lost after insertion");
            return false;
        });
        var service = CreateStables();
        Assert.Throws<TargetInvocationException>(() => Call(service, "PrepareOnionPriority", fixture.Ipc));
        Assert.True(OwnsPriority(service));
        Call(service, "ReleaseOnionPriority", fixture.Ipc);
        Assert.Equal(new[] { "keep" }, fixture.Priority);
        Assert.False(OwnsPriority(service));
    }

    [Fact]
    public void PriorityRemovalFailureRetainsOwnershipWithoutReinsertionOrAutomaticReplay()
    {
        var fixture = new Provider();
        var service = CreateStables();
        Call(service, "PrepareOnionPriority", fixture.Ipc);
        fixture.Functions["WigglyQuest.Priority.Remove"] = (Func<string, (bool, string?)>)(quest =>
        {
            fixture.Removes++;
            fixture.Priority.Remove(quest);
            return (false, "Fixture save failed after removal");
        });
        Assert.Throws<TargetInvocationException>(() => Call(service, "ReleaseOnionPriority", fixture.Ipc));
        Assert.True(OwnsPriority(service));
        Assert.Equal(new[] { "keep" }, fixture.Priority);
        Call(service, "ReleaseOnionPriority", fixture.Ipc); // Reconcile absent entry, without another mutation.
        Assert.False(OwnsPriority(service));
        Assert.Equal(1, fixture.Removes);
    }

    private static object CreateStables()
    {
        var type = typeof(live::VERMAXION.Services.ChocoboStablesService);
        var service = RuntimeHelpers.GetUninitializedObject(type);
        type.GetField("onionQuestId", PrivateInstance)!.SetValue(service, (ushort)126);
        return service;
    }

    private static bool OwnsPriority(object service)
        => (bool)service.GetType().GetField("onionQuestPriorityInserted", PrivateInstance)!.GetValue(service)!;

    private static object? Call(object target, string method, params object[] args)
        => target.GetType().GetMethod(method, PrivateInstance)!.Invoke(target, args);

    private sealed class Provider
    {
        public object Plugin = new();
        public ulong Character = 1;
        public bool Busy, MissingStartResult, PersistPriority;
        public bool CancelAccepted = true;
        public int Starts, Cancels, Inserts, Removes;
        public string? CancelId;
        public ProviderGatherRequest? Request;
        public ProviderGatherStatus? Status = new() { State = "Idle" };
        public readonly List<string> Priority = ["keep"];
        public readonly Dictionary<string, Delegate> Functions = new();
        public readonly Ipc Ipc;

        public Provider()
        {
            Functions["WigglyQuest.IsRunning"] = (Func<bool>)(() => Busy);
            Functions["WigglyQuest.Gathering.CanGather"] = (Func<uint, byte, bool>)((item, job) => item == 4173 && job == 16);
            Functions["WigglyQuest.Gathering.Start"] = (Func<ProviderGatherRequest, ProviderGatherStart?>)(request =>
            {
                Starts++;
                Request = request;
                Status = new() { RequestId = "owned-gather", State = "Travelling", ItemId = request.ItemId, Need = request.Quantity };
                return MissingStartResult ? null : new() { Accepted = true, RequestId = Status.RequestId, TerritoryId = 400 };
            });
            Functions["WigglyQuest.Gathering.GetStatus"] = (Func<ProviderGatherStatus?>)(() => Status);
            Functions["WigglyQuest.Gathering.Cancel"] = (Func<string, (bool, string?)>)(id =>
            {
                Cancels++;
                CancelId = id;
                if (CancelAccepted) Status!.State = "Cancelled";
                return (CancelAccepted, CancelAccepted ? null : "Fixture cancellation refused");
            });
            Functions["WigglyQuest.Priority.Get"] = (Func<List<string>>)(() => [.. Priority]);
            Functions["WigglyQuest.Priority.GetPersistBetweenSessions"] = (Func<bool>)(() => PersistPriority);
            Functions["WigglyQuest.InsertQuestPriority"] = (Func<int, string, bool>)((index, quest) =>
            {
                Inserts++;
                Priority.Insert(index, quest);
                return true;
            });
            Functions["WigglyQuest.Priority.Remove"] = (Func<string, (bool, string?)>)(quest =>
            {
                Removes++;
                return (Priority.Remove(quest), null);
            });
            Ipc = (Ipc)Proxy(typeof(Ipc), (method, args) =>
            {
                Assert.Equal("GetIpcSubscriber", method.Name);
                var name = (string)args![0]!;
                return Proxy(method.ReturnType, (call, values) => call.Name switch
                {
                    "get_HasFunction" => Functions.ContainsKey(name),
                    "InvokeFunc" => InvokeChannel(name, method.ReturnType.GenericTypeArguments[^1], values),
                    _ => throw new InvalidOperationException("Unexpected IPC call " + call.Name),
                });
            });
        }

        public object CreateGathering()
        {
            var route = new live::VERMAXION.CustomDeliveries.DeliveryRoute(null!,
                live::VERMAXION.CustomDeliveries.DeliveryTypes.Mining, 16, 1, 4173, 3, 400, 500);
            return Gathering.GetConstructors(PrivateInstance).Single(constructor => constructor.GetParameters().Length == 5)
                .Invoke([Ipc, route, (Func<object>)(() => Plugin), (Func<ulong>)(() => Character),
                    (Func<ushort, int>)(quality => quality == 400 ? 2 : 1)]);
        }

        private object? InvokeChannel(string name, Type resultType, object?[]? args)
        {
            var type = typeof(Ipc).Assembly.GetType("Dalamud.Plugin.Ipc.Internal.CallGateChannel", true)!;
            var channel = Activator.CreateInstance(type, [name])!;
            type.GetProperty("Func")!.SetValue(channel, Functions.GetValueOrDefault(name));
            return type.GetMethod("InvokeFunc", PrivateInstance)!.MakeGenericMethod(resultType).Invoke(channel, [args]);
        }
    }

    private static object Proxy(Type type, Func<MethodInfo, object?[]?, object?> handler)
    {
        var proxy = DispatchProxy.Create(type, typeof(HostProxy));
        ((HostProxy)proxy).Handler = handler;
        return proxy;
    }

    public class HostProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args);
    }

    public sealed class ProviderGatherRequest
    {
        public uint ItemId { get; set; }
        public byte ClassJob { get; set; }
        public int Quantity { get; set; }
        public ushort Collectability { get; set; }
    }

    public sealed class ProviderGatherStart
    {
        public bool Accepted { get; set; }
        public string? RequestId { get; set; }
        public string? Reason { get; set; }
        public ushort? TerritoryId { get; set; }
    }

    public sealed class ProviderGatherStatus
    {
        public string? RequestId { get; set; }
        public string? State { get; set; }
        public uint ItemId { get; set; }
        public int Have { get; set; }
        public int Need { get; set; }
        public string? FailureReason { get; set; }
    }
}
