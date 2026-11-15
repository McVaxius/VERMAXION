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

    public AutoRetainerIpcReadTests() => SaveProbe.Reset();

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

    [Fact]
    public void SelectionWriteUsesActualHostArgumentConversionAndPreservesTheCompletePayload()
    {
        var provider = new Provider();
        provider.Character!.Enabled = false;
        SaveProbe.AfterSave = () => provider.Calls.Add("native-save");

        var result = WriteSelection(provider.CreateClient(), true);

        Assert.True(Property<bool>(result, "Success"));
        Assert.True(Property<bool>(result, "Enabled"));
        Assert.True(Property<bool>(result, "SaveInvoked"));
        Assert.Equal(1, provider.CharacterWrites);
        Assert.Equal(1, SaveProbe.Count);
        Assert.False(SaveProbe.WriteGatherables);
        Assert.True(SaveProbe.SaveConfig);
        AssertCompletePayload(provider.Character!);
        Assert.Equal(new[]
        {
            "AutoRetainer.GetOfflineCharacterData", "AutoRetainer.WriteOfflineCharacterData",
            "AutoRetainer.GetOfflineCharacterData", "native-save", "AutoRetainer.GetOfflineCharacterData",
        }, provider.Calls);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong-cid")]
    [InlineData("missing-enabled")]
    [InlineData("unavailable-write")]
    public void InvalidOrUnavailableSelectionWritesNeverSaveOrCreateACharacter(string failure)
    {
        var provider = new Provider();
        provider.Character!.Enabled = false;
        if (failure == "missing") provider.Character = null;
        if (failure == "wrong-cid") provider.Character!.CID = 43;
        if (failure == "missing-enabled") provider.Character!.Enabled = null;
        if (failure == "unavailable-write") provider.Actions.Clear();

        var result = WriteSelection(provider.CreateClient(), true);

        Assert.False(Property<bool>(result, "Success"));
        Assert.False(Property<bool>(result, "SaveInvoked"));
        Assert.Equal(0, provider.CharacterWrites);
        Assert.Equal(0, SaveProbe.Count);
        if (failure == "unavailable-write") Assert.False(provider.Character!.Enabled);
    }

    [Fact]
    public void UnverifiedWriteCannotReachTheNativeSave()
    {
        var provider = new Provider { IgnoreCharacterWrites = true };
        provider.Character!.Enabled = false;

        var result = WriteSelection(provider.CreateClient(), true);

        Assert.False(Property<bool>(result, "Success"));
        Assert.False(Property<bool>(result, "SaveInvoked"));
        Assert.False(provider.Character.Enabled);
        Assert.Equal(0, SaveProbe.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingSaveBindingPreservesTheObservedSelectionWithoutWriting(bool originalEnabled)
    {
        var provider = new Provider();
        provider.Character!.Enabled = originalEnabled;

        var result = WriteSelection(provider.CreateClient(), !originalEnabled, null);

        Assert.False(Property<bool>(result, "Success"));
        Assert.Equal(originalEnabled, Property<bool>(result, "Enabled"));
        Assert.False(Property<bool>(result, "SaveInvoked"));
        Assert.Equal(originalEnabled, provider.Character.Enabled);
        Assert.Equal(0, provider.CharacterWrites);
        Assert.Equal(0, SaveProbe.Count);
    }

    [Fact]
    public void SaveFailureReportsTheVerifiedMemoryStateAndAttemptedSave()
    {
        var provider = new Provider();
        provider.Character!.Enabled = false;
        SaveProbe.Throw = true;

        var result = WriteSelection(provider.CreateClient(), true);

        Assert.False(Property<bool>(result, "Success"));
        Assert.True(Property<bool>(result, "Enabled"));
        Assert.True(Property<bool>(result, "SaveInvoked"));
        Assert.True(provider.Character.Enabled);
        Assert.Contains("fixture save failed", Property<string>(result, "Error"));
        Assert.Equal(1, provider.CharacterWrites);
        Assert.Equal(1, SaveProbe.Count);
    }

    [Fact]
    public void SelectionChangedBySaveIsDetectedByTheExistingFinalReadback()
    {
        var provider = new Provider();
        SaveProbe.AfterSave = () => provider.Character!.Enabled = false;

        var result = WriteSelection(provider.CreateClient(), true);

        Assert.False(Property<bool>(result, "Success"));
        Assert.False(Property<bool>(result, "Enabled"));
        Assert.True(Property<bool>(result, "SaveInvoked"));
    }

    [Fact]
    public void InnEnableUsesIpcWithoutAddingASaveAndRemainsIdempotent()
    {
        var provider = new Provider();
        provider.Character!.Enabled = false;
        var client = provider.CreateClient();

        Assert.True(client.TryEnableCharacter(42, out var who, out var error), error);
        Assert.Equal("Fixture character", who);
        Assert.True(client.TryEnableCharacter(42, out _, out error), error);
        Assert.Equal(1, provider.CharacterWrites);
        Assert.Equal(0, SaveProbe.Count);
        AssertCompletePayload(provider.Character!);
    }

    [Fact]
    public void ReadinessKeepsTheExistingFirstVentureChoiceAndChangesOnlyItsTime()
    {
        var provider = new Provider();
        var before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        Assert.True(provider.CreateClient().TryFakeReadyEnabledCharacter(1, out var who, out var error), error);

        Assert.Equal("Fixture character", who);
        Assert.InRange(provider.Character!.RetainerData![0].VentureEndsAt!.Value,
            before + 10, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 10);
        Assert.Equal(100, provider.Character.RetainerData[2].VentureEndsAt);
        Assert.Equal(0, provider.Character.RetainerData[1].VentureEndsAt);
        AssertCompletePayload(provider.Character);
        Assert.Equal(1, provider.CharacterWrites);
        Assert.Equal(0, SaveProbe.Count);
    }

    [Fact]
    public void GilOptionsUseAdditionalDataIpcAndPreservePercentagesAndOtherFields()
    {
        var provider = new Provider();
        provider.RetainerSettings["Selected"] = new() { WithdrawGilPercent = 37, Deposit = true, Ilvl = 650 };
        provider.RetainerSettings["No stats"] = new() { WithdrawGil = true, WithdrawGilPercent = 52 };
        provider.RetainerSettings["Unselected"] = new() { WithdrawGilPercent = 91 };
        var method = typeof(Client).GetMethod("ConfigureRetainerGilWithdrawal", PrivateInstance, null,
            [typeof(ulong), typeof(MethodInfo)], null)!;

        method.Invoke(provider.CreateClient(), [42UL, SaveMethod]);

        Assert.Equal(2, provider.AdditionalWrites);
        Assert.All(provider.RetainerSettings.Values, value =>
        {
            Assert.True(value.WithdrawGil);
            Assert.False(value.Deposit);
        });
        Assert.Equal(37, provider.RetainerSettings["Selected"].WithdrawGilPercent);
        Assert.Equal(650, provider.RetainerSettings["Selected"].Ilvl);
        Assert.Equal(52, provider.RetainerSettings["No stats"].WithdrawGilPercent);
        Assert.Equal(91, provider.RetainerSettings["Unselected"].WithdrawGilPercent);
        Assert.Equal(1, SaveProbe.Count);
        Assert.False(SaveProbe.WriteGatherables);
        Assert.True(SaveProbe.SaveConfig);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MultiModeFallbackUsesTheExistingPublishedReadAndKeepsUnknownDistinct(bool enabled)
    {
        var provider = new Provider();
        provider.Functions["AutoRetainer.PluginState.GetMultiModeStatus"] = (Func<bool>)(() => enabled);
        var client = provider.CreateClient();

        var state = client.ReadMultiModeEnabled();

        Assert.True(state.Success, state.Error);
        Assert.Equal(enabled, state.Enabled);
        provider.Functions.Remove("AutoRetainer.PluginState.GetMultiModeStatus");
        Assert.False(client.ReadMultiModeEnabled().Success);
    }

    private static void AssertCompletePayload(NativeCharacter character)
    {
        Assert.Equal(42UL, character.CID);
        Assert.Equal("Fixture character", character.Name);
        Assert.Equal("Fixture world", character.World);
        Assert.True(character.WorkshopEnabled);
        Assert.Equal(ulong.MaxValue, character.FutureGoals["future"]);
        Assert.Equal(3, character.RetainerData!.Count);
        Assert.Equal(777u, character.RetainerData[0].VentureID);
        Assert.Equal(1234567, character.RetainerData[0].VentureBeginsAt);
    }

    private static MethodInfo SaveMethod => typeof(SaveProbe).GetMethod(nameof(SaveProbe.Save))!;
    private static object WriteSelection(Client client, bool enabled)
        => WriteSelection(client, enabled, SaveMethod);
    private static object WriteSelection(Client client, bool enabled, MethodInfo? save)
        => typeof(Client).GetMethod("WriteCharacterSelection", PrivateInstance, null,
            [typeof(ulong), typeof(bool), typeof(MethodInfo), typeof(string)], null)!
            .Invoke(client, [42UL, enabled, save, "fixture save binding unavailable"])!;

    private static class SaveProbe
    {
        internal static int Count;
        internal static bool WriteGatherables, SaveConfig, Throw;
        internal static Action? AfterSave;
        public static void Save(bool writeGatherables, bool saveConfig)
        {
            Count++;
            WriteGatherables = writeGatherables;
            SaveConfig = saveConfig;
            if (Throw) throw new InvalidOperationException("fixture save failed");
            AfterSave?.Invoke();
        }
        internal static void Reset()
        {
            Count = 0;
            WriteGatherables = SaveConfig = Throw = false;
            AfterSave = null;
        }
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
            Name = "Fixture character", World = "Fixture world", WorkshopEnabled = true,
            FutureGoals = new() { ["future"] = ulong.MaxValue },
            RetainerData =
            [
                new() { RetainerID = 1, Name = "Selected", Job = 18, Level = 100, HasVenture = true, VentureEndsAt = 200, VentureID = 777, VentureBeginsAt = 1234567 },
                new() { RetainerID = 2, Name = "No stats", Job = 5, Level = 90, HasVenture = false, VentureEndsAt = 0 },
                new() { RetainerID = 3, Name = "Unselected", Job = 16, Level = 80, HasVenture = true, VentureEndsAt = 100 },
            ],
        };
        public Dictionary<ulong, HashSet<string>>? Selections = new() { [42] = ["Selected", "No stats"] };
        public Dictionary<string, NativeAdditional>? Additional = new() { ["#000000000000002A Selected"] = new() { Ilvl = 650, Perception = 4400 } };
        public readonly Dictionary<string, Delegate> Functions = new();
        public readonly Dictionary<string, Delegate> Actions = new();
        public readonly Dictionary<string, NativeAdditional> RetainerSettings = new();
        public readonly List<string> Calls = [];
        public int CharacterWrites, AdditionalWrites;
        public bool IgnoreCharacterWrites;

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
            Functions["AutoRetainer.GetConfig.OfflineData"] = (Func<List<NativeCharacter>>)(() => Character == null ? [] : [Character]);
            Functions["AutoRetainer.GetAdditionalRetainerData"] = (Func<ulong, string, NativeAdditional>)((cid, name) =>
            {
                Assert.Equal(42UL, cid);
                return RetainerSettings.GetValueOrDefault(name) ?? new();
            });
            Actions["AutoRetainer.WriteOfflineCharacterData"] = (Action<NativeCharacter>)(character =>
            {
                Assert.Equal(42UL, character.CID);
                CharacterWrites++;
                if (!IgnoreCharacterWrites) Character = character;
            });
            Actions["AutoRetainer.WriteAdditionalRetainerData"] = (Action<ulong, string, NativeAdditional>)((cid, name, value) =>
            {
                Assert.Equal(42UL, cid);
                AdditionalWrites++;
                RetainerSettings[name] = value;
            });
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
                    "get_HasAction" => Actions.ContainsKey(name),
                    "InvokeFunc" => Read(name, method.ReturnType.GenericTypeArguments[^1], values),
                    "InvokeAction" => Write(name, values),
                    _ => throw new InvalidOperationException("Unexpected IPC operation"),
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

        private object? Write(string name, object?[]? args)
        {
            Calls.Add(name);
            var type = typeof(Ipc).Assembly.GetType("Dalamud.Plugin.Ipc.Internal.CallGateChannel", true)!;
            var channel = Activator.CreateInstance(type, [name])!;
            type.GetProperty("Action")!.SetValue(channel, Actions.GetValueOrDefault(name));
            type.GetMethod("InvokeAction", PrivateInstance)!.Invoke(channel, [args]);
            return null;
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
        public string Name = "";
        public string World = "";
        public bool WorkshopEnabled;
        public Dictionary<string, ulong> FutureGoals = new();
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
        public uint VentureID;
        public long VentureBeginsAt;
    }

    public sealed class NativeAdditional
    {
        public int Ilvl;
        public int Perception;
        public bool WithdrawGil;
        public bool Deposit;
        public int WithdrawGilPercent;
    }
}
