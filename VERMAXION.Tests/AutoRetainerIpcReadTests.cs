extern alias live;
extern alias host;

using System.Collections.Generic;
using System.Reflection;
using Xunit;
using Ipc = host::Dalamud.Plugin.IDalamudPluginInterface;
using Client = live::VERMAXION.IPC.AutoRetainerIPC;

namespace VERMAXION.Tests;

public sealed class AutoRetainerIpcReadTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public void TypedCharacterAndEquipmentReadsUseActualDalamudConversionWithoutCreatingDefaults()
    {
        var provider = new Provider();
        var client = provider.CreateClient();
        var selection = ReadSelection(client);
        Assert.True(Property<bool>(selection, "Success"));
        Assert.True(Property<bool>(selection, "Enabled"));
        var equipment = client.ReadEnabledRetainers(42);
        Assert.True(equipment.Success, equipment.Error);
        Assert.Equal(new[] { "Selected", "No stats" }, equipment.Retainers.Select(retainer => retainer.Name));
        Assert.Equal(650, equipment.Retainers[0].ItemLevel);
        Assert.Equal(4400, equipment.Retainers[0].Perception);
        Assert.Equal(-1, equipment.Retainers[1].ItemLevel);
        Assert.Equal(-1, equipment.Retainers[1].Perception);
        Assert.Single(provider.Additional!);
        Assert.Equal(new[] { "Selected", "No stats" }, provider.Selections![42].OrderBy(name => name == "Selected" ? 0 : 1));
        Assert.DoesNotContain("AutoRetainer.GetAdditionalRetainerData", provider.Calls);
    }

    [Fact]
    public void DisabledSelectionIsKnownButAMissingSelectionMemberIsUnavailable()
    {
        var provider = new Provider();
        var client = provider.CreateClient();
        provider.Character!.Enabled = false;
        var disabled = ReadSelection(client);
        Assert.True(Property<bool>(disabled, "Success"));
        Assert.False(Property<bool>(disabled, "Enabled"));
        provider.Character.Enabled = null;
        Assert.False(Property<bool>(ReadSelection(client), "Success"));
        provider.Character.CID = 43;
        Assert.False(Property<bool>(ReadSelection(client), "Success"));
        provider.Character = null;
        Assert.False(Property<bool>(ReadSelection(client), "Success"));
    }

    [Theory]
    [InlineData("selection")]
    [InlineData("retainers")]
    [InlineData("additional")]
    [InlineData("fields")]
    public void IncompleteEquipmentSnapshotsCannotBecomeKnownEmptyOrZeroData(string missing)
    {
        var provider = new Provider();
        if (missing == "selection") provider.Selections = null;
        if (missing == "retainers") provider.Character!.RetainerData = null;
        if (missing == "additional") provider.Additional = null;
        if (missing == "fields") provider.Character!.RetainerData![0].Job = null;
        var result = provider.CreateClient().ReadEnabledRetainers(42);
        Assert.False(result.Success);
        Assert.Empty(result.Retainers);
        Assert.NotEmpty(result.Error);
    }

    [Fact]
    public void EmptySelectionIsKnownWithoutRequiringAnUnneededCharacterRead()
    {
        var provider = new Provider { Character = null };
        provider.Selections![42].Clear();
        var result = provider.CreateClient().ReadEnabledRetainers(42);
        Assert.True(result.Success, result.Error);
        Assert.Empty(result.Retainers);
        Assert.DoesNotContain("AutoRetainer.GetOfflineCharacterData", provider.Calls);
    }

    [Fact]
    public void EarliestVentureIncludesUnselectedRetainersAndKeepsUnknownDistinctFromNoVentures()
    {
        var provider = new Provider();
        var client = provider.CreateClient();
        Assert.True(client.TryReadEarliestVenture(42, out var earliest, out var error), error);
        Assert.Equal(100, earliest); // The unselected retainer is earlier than the selected one.
        provider.Character!.RetainerData = [];
        Assert.True(client.TryReadEarliestVenture(42, out earliest, out error), error);
        Assert.Equal(long.MaxValue, earliest);
        provider.Character.RetainerData = null;
        Assert.False(client.TryReadEarliestVenture(42, out _, out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CollectOnlyUsesTypedReadAndRejectsAnUnavailableFunction(bool enabled)
    {
        var provider = new Provider();
        provider.Functions["AutoRetainer.GetConfig._dontReassign"] = (Func<bool>)(() => enabled);
        var client = provider.CreateClient();
        var result = client.ReadCollectOnly();
        Assert.True(result.Success, result.Error);
        Assert.Equal(enabled, result.Enabled);
        provider.Functions.Remove("AutoRetainer.GetConfig._dontReassign");
        Assert.False(client.ReadCollectOnly().Success);
        Assert.False(client.TrySetCollectOnly(!enabled, out var error));
        Assert.Contains("readback IPC is unavailable", error);
    }

    private static object ReadSelection(Client client)
        => typeof(Client).GetMethod("ReadCharacterSelection", PrivateInstance)!.Invoke(client, [42UL])!;

    private static T Property<T>(object value, string name)
        => (T)value.GetType().GetProperty(name)!.GetValue(value)!;

    private sealed class Provider
    {
        public NativeCharacter? Character = new()
        {
            CID = 42,
            Enabled = true,
            RetainerData =
            [
                new() { RetainerID = 1, Name = "Selected", Job = 18, Level = 100, HasVenture = true, VentureEndsAt = 200 },
                new() { RetainerID = 2, Name = "No stats", Job = 5, Level = 90, HasVenture = false, VentureEndsAt = 0 },
                new() { RetainerID = 3, Name = "Unselected", Job = 16, Level = 80, HasVenture = true, VentureEndsAt = 100 },
            ],
        };
        public Dictionary<ulong, HashSet<string>>? Selections = new() { [42] = ["Selected", "No stats"] };
        public Dictionary<string, NativeAdditional>? Additional = new() { ["#000000000000002A Selected"] = new() { Ilvl = 650, Perception = 4400 } };
        public readonly Dictionary<string, Delegate> Functions = new();
        public readonly List<string> Calls = [];

        public Provider()
        {
            Functions["AutoRetainer.GetOfflineCharacterData"] = (Func<ulong, NativeCharacter?>)(cid =>
            {
                Assert.Equal(42UL, cid);
                return Character;
            });
            Functions["AutoRetainer.PluginState.GetEnabledRetainers"] = (Func<Dictionary<ulong, HashSet<string>>?>)(() => Selections);
            Functions["AutoRetainer.GetConfig.AdditionalData"] = (Func<Dictionary<string, NativeAdditional>?>)(() => Additional);
            Functions["AutoRetainer.GetConfig._dontReassign"] = (Func<bool>)(() => false);
        }

        public Client CreateClient()
        {
            var ipc = (Ipc)Proxy(typeof(Ipc), (method, args) =>
            {
                Assert.Equal("GetIpcSubscriber", method.Name);
                var name = (string)args![0]!;
                return Proxy(method.ReturnType, (call, values) => call.Name switch
                {
                    "get_HasFunction" => Functions.ContainsKey(name),
                    "InvokeFunc" => Read(name, method.ReturnType.GenericTypeArguments[^1], values),
                    _ => throw new InvalidOperationException("A read probe attempted a non-read IPC operation"),
                });
            });
            return new Client(ipc, (host::Dalamud.Plugin.Services.IPluginLog)Proxy(typeof(host::Dalamud.Plugin.Services.IPluginLog), (_, _) => null));
        }

        private object? Read(string name, Type resultType, object?[]? args)
        {
            Calls.Add(name);
            var type = typeof(Ipc).Assembly.GetType("Dalamud.Plugin.Ipc.Internal.CallGateChannel", true)!;
            var channel = Activator.CreateInstance(type, [name])!;
            type.GetProperty("Func")!.SetValue(channel, Functions.GetValueOrDefault(name));
            return type.GetMethod("InvokeFunc", PrivateInstance)!.MakeGenericMethod(resultType).Invoke(channel, [args]);
        }
    }

    private static object Proxy(Type type, Func<MethodInfo, object?[]?, object?> handler)
    {
        var proxy = DispatchProxy.Create(type, typeof(QuestionableIntegrationTests.HostProxy));
        ((QuestionableIntegrationTests.HostProxy)proxy).Handler = handler;
        return proxy;
    }

    // Public fields reproduce the published AutoRetainerAPI transport without
    // constructing its game-bound DTOs or loading any provider assembly.
    public sealed class NativeCharacter
    {
        public ulong? CID;
        public bool? Enabled;
        public List<NativeRetainer>? RetainerData;
    }

    public sealed class NativeRetainer
    {
        public ulong? RetainerID;
        public string? Name;
        public uint? Job;
        public int? Level;
        public bool? HasVenture;
        public long? VentureEndsAt;
    }

    public sealed class NativeAdditional
    {
        public int Ilvl;
        public int Perception;
    }
}
