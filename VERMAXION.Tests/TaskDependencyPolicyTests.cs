extern alias live;
extern alias host;

using System;
using System.Collections.Generic;
using System.Reflection;
using VERMAXION.Models;
using Xunit;

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

        protected override object? Invoke(MethodInfo? method, object?[]? args)
            => method?.Name == "get_InstalledPlugins" ? Plugins
                : throw new InvalidOperationException("Unexpected plugin-interface access.");
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
