extern alias live;
extern alias host;

using System;
using System.Collections.Generic;
using System.Reflection;
using VERMAXION.Models;
using Xunit;
using AutomationCatalog = live::VERMAXION.AutomationCatalog;

namespace VERMAXION.Tests;

public sealed class TaskDependencyPolicyTests
{
    [Fact]
    public void ProductionDashboardRecognizesLoadedCombatProvidersWithoutPrivateInstances()
    {
        var pluginInterface = DispatchProxy.Create<host::Dalamud.Plugin.IDalamudPluginInterface, InventoryPluginInterface>();
        ((InventoryPluginInterface)(object)pluginInterface).Plugins = [
            InventoryPlugin.Create("RotationSolver", true),
            InventoryPlugin.Create("BossModReborn", true),
            InventoryPlugin.Create("vnavmesh", true),
            InventoryPlugin.Create("Lifestream", false),
        ];
        var interfaceProperty = typeof(live::VERMAXION.Plugin).GetProperty("PluginInterface", BindingFlags.Static | BindingFlags.NonPublic)!;
        var discover = typeof(live::VERMAXION.Windows.MainWindow).GetMethod("GetLoadedTaskDependencyNames", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = interfaceProperty.GetValue(null);
        try
        {
            interfaceProperty.SetValue(null, pluginInterface);
            var loaded = Assert.IsType<HashSet<string>>(discover.Invoke(null, null));
            Assert.Equal(3, loaded.Count);
            Assert.Contains("RotationSolver", loaded);
            Assert.Contains("BossModReborn", loaded);
            Assert.Contains("vnavmesh", loaded);
            Assert.DoesNotContain("Lifestream", loaded);
            Assert.DoesNotContain("rotationsolver", loaded);
        }
        finally
        {
            interfaceProperty.SetValue(null, previous);
        }
    }

    [Fact]
    public void CombatProviderPrerequisiteChecksReadyRoutesAndRejectsLoadedHostConflicts()
    {
        const string native = "BossMod.Presets.GetForceDisabled";
        const string companion = "BMRVariantLoader.Suite.IsReady.v1";
        var dependency = CreateCombatProvider(out var inventory);
        var blocker = dependency.GetType().GetMethod("GetBlocker")!;
        string? ReadBlocker(string taskId = live::VERMAXION.AutomationCatalog.AlliedSociety)
            => (string?)blocker.Invoke(dependency, [taskId]);
        void Load(params string[] names) => inventory.Plugins = names.Select(name => InventoryPlugin.Create(name, true)).ToArray();

        string[] restricted =
        [
            AutomationCatalog.AlliedSociety,
            AutomationCatalog.Beastmaster,
            "Run##BeastmasterWeekly",
        ];
        void ExpectBlocker(string expected)
        {
            foreach (var task in restricted) Assert.Contains(expected, ReadBlocker(task));
        }
        void ExpectAdmitted()
        {
            foreach (var task in restricted) Assert.Null(ReadBlocker(task));
        }

        ExpectBlocker("Enable God Mod Companion");
        Assert.Null(ReadBlocker(AutomationCatalog.ChocoboRacing));
        Assert.Null(ReadBlocker(AutomationCatalog.VerminionQueue));

        inventory.Functions[native] = () => false;
        Load("GodMod");
        ExpectBlocker("Enable God Mod Companion");
        Load("GodMod", "BMRVariantLoader");
        ExpectAdmitted();
        inventory.Functions[native] = () => throw new InvalidOperationException("Incompatible IPC");
        ExpectBlocker("GodMod is loaded");
        inventory.Functions[native] = () => true;
        Load("GodMod", "BossModReborn", "BMRVariantLoader");
        ExpectBlocker("Multiple BossMod hosts");
        Load("GodMod", "BossMod", "BMRVariantLoader");
        ExpectBlocker("Multiple BossMod hosts");
        Load("BossModReborn", "BossMod", "BMRVariantLoader");
        ExpectBlocker("Multiple BossMod hosts");

        Load("BossModReborn");
        ExpectBlocker("Enable God Mod Companion");
        Load("BMRVariantLoader");
        ExpectBlocker("Enable GodMod or BossModReborn");
        Load("BossModReborn", "BMRVariantLoader");
        inventory.Functions[companion] = () => true;
        ExpectAdmitted();
        inventory.Functions[companion] = () => false;
        ExpectBlocker("God Mod Companion is not ready");
        inventory.Functions.Remove(companion);
        ExpectBlocker("God Mod Companion is not ready");
        inventory.Plugins = [InventoryPlugin.Create("BMRVariantLoader", false), InventoryPlugin.Create("GodMod", true)];
        ExpectBlocker("Enable God Mod Companion");
    }

    [Fact]
    public void UnrelatedCatalogTasksDoNotProbeCombatProviders()
    {
        var dependency = CreateCombatProvider(out var inventory);
        var readBlocker = dependency.GetType().GetMethod("GetBlocker")!
            .CreateDelegate<Func<string?, string?>>(dependency);

        foreach (var feature in AutomationCatalog.Features)
        {
            if (feature.Id is AutomationCatalog.AlliedSociety or AutomationCatalog.Beastmaster or AutomationCatalog.BeastmasterWeekly)
                continue;

            Assert.Null(readBlocker(feature.Id));
        }

        Assert.Null(readBlocker(null));
        Assert.Equal(0, inventory.ProviderProbes);
    }

    [Fact]
    public void AlliedSocietyProviderLossCancelsBeforeGearsetConfirmationOrRotation()
    {
        var dependency = CreateCombatProvider(out var inventory);
        inventory.Plugins = [InventoryPlugin.Create("GodMod", true), InventoryPlugin.Create("BMRVariantLoader", true)];
        inventory.Functions["BossMod.Presets.GetForceDisabled"] = () => false;
        var readBlocker = dependency.GetType().GetMethod("GetBlocker")!
            .CreateDelegate<Func<string?, string?>>(dependency);

        var gearset = new live::VERMAXION.Models.GearsetSnapshot(5, 21, true, true, 100, "Test gearset", [1]);
        var equipRequests = 0;
        var confirmationRequests = 0;
        var rotationIdentityReads = 0;
        var equipment = Proxy<live::VERMAXION.Models.IEquipmentAutomationRuntime>((method, args) =>
        {
            switch (method.Name)
            {
                case "get_UtcNow": return new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);
                case "get_CharacterContentId": return 17UL;
                case "GetValidGearsets": return new[] { gearset };
                case "IsGearsetEquipped": return false;
                case "TryEquipGearset":
                    Assert.Equal(gearset.GearsetId, args![0]);
                    equipRequests++;
                    args[1] = string.Empty;
                    return true;
                case "TryConfirmGearsetChangePrompt":
                    confirmationRequests++;
                    return true;
                default: throw new InvalidOperationException(method.Name);
            }
        });
        var client = Proxy<host::Dalamud.Plugin.Services.IClientState>((method, _) => method.Name == "get_IsLoggedIn");
        var player = Proxy<host::Dalamud.Plugin.Services.IPlayerState>((method, _) => method.Name == "get_IsLoaded");
        var objects = Proxy<host::Dalamud.Plugin.Services.IObjectTable>((_, _) => { rotationIdentityReads++; return null; });
        var log = Proxy<host::Dalamud.Plugin.Services.IPluginLog>((_, _) => null);
        var service = new live::VERMAXION.Services.AlliedSocietyService(equipment, client, player, objects, log,
            new live::VERMAXION.IPC.QuestionableCompanionAlliedSocietyBridge());
        service.GetType().GetProperty("GetCombatProviderBlocker", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(service, (Func<string?>)(() => readBlocker(live::VERMAXION.AutomationCatalog.AlliedSociety)));

        service.Start(new live::VERMAXION.Models.CharacterConfig
        {
            AlliedSocietyGearsetSelection = live::VERMAXION.Models.AlliedSocietyGearsetSelection.SavedGearset,
            AlliedSocietyGearsetId = gearset.GearsetId,
        });
        service.Update();
        Assert.Equal(live::VERMAXION.Services.AlliedSocietyService.RunState.ConfirmingGearset, service.State);
        Assert.Equal(1, equipRequests);

        inventory.Plugins = [];
        service.Update();
        Assert.Equal(live::VERMAXION.Services.AlliedSocietyService.RunState.Cancelled, service.State);
        Assert.True(service.IsFailed);
        Assert.False(service.IsActive);
        Assert.Contains("Enable God Mod Companion", service.StatusText);

        inventory.Plugins = [InventoryPlugin.Create("GodMod", true)];
        service.Update();
        Assert.Equal(live::VERMAXION.Services.AlliedSocietyService.RunState.Cancelled, service.State);
        Assert.False(service.OwnsRotation);
        Assert.Equal(1, equipRequests);
        Assert.Equal(0, confirmationRequests);
        Assert.Equal(0, rotationIdentityReads);

        static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
        {
            var proxy = DispatchProxy.Create<T, BeastmasterRouteLifecycleTests.HostProxy>();
            ((BeastmasterRouteLifecycleTests.HostProxy)(object)proxy).Handler = handler;
            return proxy;
        }
    }

    internal static object CreateReadyCombatProvider(Func<bool>? isCompanionReady = null)
    {
        var dependency = CreateCombatProvider(out var inventory);
        inventory.Plugins = [InventoryPlugin.Create("BossModReborn", true), InventoryPlugin.Create("BMRVariantLoader", true)];
        inventory.Functions["BMRVariantLoader.Suite.IsReady.v1"] = isCompanionReady ?? (() => true);
        return dependency;
    }

    private static object CreateCombatProvider(out InventoryPluginInterface inventory)
    {
        var host = DispatchProxy.Create<host::Dalamud.Plugin.IDalamudPluginInterface, InventoryPluginInterface>();
        inventory = (InventoryPluginInterface)(object)host;
        var type = typeof(live::VERMAXION.Plugin).Assembly.GetType("VERMAXION.IPC.CombatProviderDependency", throwOnError: true)!;
        return Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null, args: [host], culture: null)!;
    }

    [Fact]
    public void Aggregate_ReportsReadyWhenAllChecksAreReady()
    {
        var summary = TaskDependencyPolicy.Aggregate([
            TaskDependencyCheck.Loaded("Lifestream", true),
            TaskDependencyCheck.Loaded("vnavmesh", true),
        ]);

        Assert.Equal(TaskDependencyState.Ready, summary.State);
        Assert.Equal("Ready", summary.Label);
        Assert.Contains("Missing: None", summary.Tooltip, StringComparison.Ordinal);
    }

    [Fact]
    public void Aggregate_ReportsMissingAndPreservesSetupDetail()
    {
        var summary = TaskDependencyPolicy.Aggregate([
            TaskDependencyCheck.Loaded("Lifestream", false),
            TaskDependencyCheck.Configured("Saucy", true, false, "Saucy config is inaccessible."),
        ]);

        Assert.Equal(TaskDependencyState.Missing, summary.State);
        Assert.Equal(1, summary.MissingCount);
        Assert.Equal(1, summary.NeedsSetupCount);
        Assert.Contains("Saucy config is inaccessible", summary.Tooltip, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void DialogueAlternative_AcceptsEitherEnabledProvider(bool textAdvanceReady, bool xaSlaveReady)
    {
        var check = TaskDependencyPolicy.Alternative(
            "Dialogue automation",
            TaskDependencyCheck.Configured("TextAdvance", true, textAdvanceReady, "TextAdvance state"),
            TaskDependencyCheck.Configured("XA Slave Skip Dialogue", true, xaSlaveReady, "XA Slave state"));

        Assert.Equal(TaskDependencyState.Ready, check.State);
    }

    [Fact]
    public void DialogueAlternative_ReportsMissingWhenNeitherPluginIsLoaded()
    {
        var check = TaskDependencyPolicy.Alternative(
            "Dialogue automation",
            TaskDependencyCheck.Loaded("TextAdvance", false),
            TaskDependencyCheck.Loaded("XA Slave", false));

        Assert.Equal(TaskDependencyState.Missing, check.State);
    }

    [Fact]
    public void DialogueAlternative_ReportsNeedsSetupWhenLoadedProviderIsDisabled()
    {
        var check = TaskDependencyPolicy.Alternative(
            "Dialogue automation",
            TaskDependencyCheck.Configured("TextAdvance", true, false, "TextAdvance is disabled."),
            TaskDependencyCheck.Loaded("XA Slave", false));

        Assert.Equal(TaskDependencyState.NeedsSetup, check.State);
        Assert.Contains("disabled", check.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(OceanFishingProvider.VermaxionAutoHook, false, TaskDependencyState.Ready)]
    [InlineData(OceanFishingProvider.VermaxionAutoHook, true, TaskDependencyState.NeedsSetup)]
    [InlineData(OceanFishingProvider.AutoHookAutoOceanFish, true, TaskDependencyState.Ready)]
    [InlineData(OceanFishingProvider.AutoHookAutoOceanFish, false, TaskDependencyState.NeedsSetup)]
    public void FishingProviderAlignment_ReportsDrift(
        OceanFishingProvider provider,
        bool autoOceanFishEnabled,
        TaskDependencyState expectedState)
    {
        var check = TaskDependencyPolicy.FishingProviderAlignment(
            provider,
            autoHookLoaded: true,
            settingReadable: true,
            autoOceanFishEnabled,
            "read");

        Assert.Equal(expectedState, check.State);
    }

    [Fact]
    public void FishingProviderAlignment_DistinguishesMissingAndUnreadable()
    {
        var missing = TaskDependencyPolicy.FishingProviderAlignment(
            OceanFishingProvider.VermaxionAutoHook,
            autoHookLoaded: false,
            settingReadable: false,
            null,
            "missing");
        var unreadable = TaskDependencyPolicy.FishingProviderAlignment(
            OceanFishingProvider.VermaxionAutoHook,
            autoHookLoaded: true,
            settingReadable: false,
            null,
            "unwritable surface");

        Assert.Equal(TaskDependencyState.Missing, missing.State);
        Assert.Equal(TaskDependencyState.NeedsSetup, unreadable.State);
    }

    [Theory]
    [InlineData(false, TaskDependencyState.Missing)]
    [InlineData(true, TaskDependencyState.Ready)]
    public void SaucyConfigurationReadiness_DistinguishesMissingAndAccessible(
        bool accessible,
        TaskDependencyState expected)
    {
        var check = TaskDependencyCheck.Configured("Saucy", accessible, accessible, "Saucy config status");

        Assert.Equal(expected, check.State);
    }

    public class InventoryPluginInterface : DispatchProxy
    {
        public host::Dalamud.Plugin.IExposedPlugin[] Plugins { get; set; } = [];
        public Dictionary<string, Func<bool>> Functions { get; } = [];
        internal int ProviderProbes { get; private set; }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name == "get_InstalledPlugins")
            {
                ProviderProbes++;
                return Plugins;
            }
            if (method?.Name == "GetIpcSubscriber" && args is [string channel])
            {
                var ipc = DispatchProxy.Create<host::Dalamud.Plugin.Ipc.ICallGateSubscriber<bool>, BooleanIpc>();
                var subscriber = (BooleanIpc)(object)ipc;
                subscriber.HasFunction = () =>
                {
                    ProviderProbes++;
                    return Functions.ContainsKey(channel);
                };
                subscriber.Read = () =>
                {
                    ProviderProbes++;
                    return Functions[channel]();
                };
                return ipc;
            }
            throw new InvalidOperationException("Unexpected plugin-interface access.");
        }
    }

    public class BooleanIpc : DispatchProxy
    {
        public Func<bool> HasFunction { get; set; } = null!;
        public Func<bool> Read { get; set; } = null!;

        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
        {
            "get_HasFunction" => HasFunction(),
            "InvokeFunc" => Read(),
            _ => throw new InvalidOperationException("Unexpected combat IPC access."),
        };
    }

    public class InventoryPlugin : DispatchProxy
    {
        public string InternalName { get; set; } = "";
        public bool IsLoaded { get; set; }

        public static host::Dalamud.Plugin.IExposedPlugin Create(string name, bool loaded)
        {
            var plugin = DispatchProxy.Create<host::Dalamud.Plugin.IExposedPlugin, InventoryPlugin>();
            var inventory = (InventoryPlugin)(object)plugin;
            inventory.InternalName = name;
            inventory.IsLoaded = loaded;
            return plugin;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
        {
            "get_InternalName" => InternalName,
            "get_IsLoaded" => IsLoaded,
            _ => throw new InvalidOperationException("Unexpected installed-plugin access."),
        };
    }
}
