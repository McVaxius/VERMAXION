using System.Collections.Generic;
using System.Collections.ObjectModel;
using VERMAXION.Services;
using Xunit;

namespace VERMAXION.Tests;

public sealed class SaucyConfigurationAccessorTests
{
    [Fact]
    public void CurrentSaucyShapeEnablesAndRestoresModuleMembership()
    {
        var configuration = new CurrentSaucyConfiguration();

        Assert.True(SaucyConfigurationAccessor.TryCreate(configuration, out var accessor, out var status));
        Assert.Equal("Saucy config available.", status);

        var snapshot = accessor.CaptureMiniCactpotState();
        var enableChange = accessor.EnableMiniCactpot();

        Assert.True(enableChange.StateChanged);
        Assert.Null(enableChange.SaveError);
        Assert.Contains("MiniCactpot", configuration.EnabledModules);
        Assert.Equal(1, configuration.SaveCount);

        var restoreChange = accessor.RestoreMiniCactpot(snapshot);

        Assert.True(restoreChange.StateChanged);
        Assert.Null(restoreChange.SaveError);
        Assert.DoesNotContain("MiniCactpot", configuration.EnabledModules);
        Assert.Equal(2, configuration.SaveCount);
    }

    [Fact]
    public void LegacySaucyRestoresBooleanAndModuleMembership()
    {
        var configuration = new LegacySaucyConfiguration();
        Assert.True(SaucyConfigurationAccessor.TryCreate(configuration, out var accessor, out _));

        var snapshot = accessor.CaptureMiniCactpotState();
        accessor.EnableMiniCactpot();

        Assert.True(configuration.EnableAutoMiniCactpot);
        Assert.Contains("MiniCactpot", configuration.EnabledModules);

        accessor.RestoreMiniCactpot(snapshot);

        Assert.False(configuration.EnableAutoMiniCactpot);
        Assert.DoesNotContain("MiniCactpot", configuration.EnabledModules);
        Assert.Equal(2, configuration.SaveCount);
    }

    [Fact]
    public void AlreadyEnabledModuleIsNotChangedOrSaved()
    {
        var configuration = new CurrentSaucyConfiguration();
        configuration.EnabledModules.Add("MiniCactpot");
        Assert.True(SaucyConfigurationAccessor.TryCreate(configuration, out var accessor, out _));

        var snapshot = accessor.CaptureMiniCactpotState();
        var enableChange = accessor.EnableMiniCactpot();
        var restoreChange = accessor.RestoreMiniCactpot(snapshot);

        Assert.False(enableChange.StateChanged);
        Assert.False(restoreChange.StateChanged);
        Assert.Equal("MiniCactpot", Assert.Single(configuration.EnabledModules));
        Assert.Equal(0, configuration.SaveCount);
    }

    [Fact]
    public void MissingEnabledModulesFailsWithPreciseStatus()
    {
        var success = SaucyConfigurationAccessor.TryCreate(
            new MissingEnabledModulesConfiguration(),
            out _,
            out var status);

        Assert.False(success);
        Assert.Equal("Saucy config field EnabledModules was not available.", status);
    }

    [Fact]
    public void NonMutableEnabledModulesFailsWithPreciseStatus()
    {
        var success = SaucyConfigurationAccessor.TryCreate(
            new NonMutableEnabledModulesConfiguration(),
            out _,
            out var status);

        Assert.False(success);
        Assert.Equal("Saucy EnabledModules was not a mutable list.", status);
    }

    private sealed class CurrentSaucyConfiguration
    {
        public ObservableCollection<string> EnabledModules = [];

        public int SaveCount { get; private set; }

        public void Save() => SaveCount++;
    }

    private sealed class LegacySaucyConfiguration
    {
        public List<string> EnabledModules = [];
        public bool EnableAutoMiniCactpot = false;

        public int SaveCount { get; private set; }

        public void Save() => SaveCount++;
    }

    private sealed class MissingEnabledModulesConfiguration
    {
    }

    private sealed class NonMutableEnabledModulesConfiguration
    {
        public string[] EnabledModules = [];
    }
}
