using System;
using VERMAXION.Services;
using Xunit;

namespace VERMAXION.Tests;

public sealed class AutoHookConfigurationAccessorTests
{
    [Fact]
    public void Synchronize_WritesFieldAndPersistsChangedValue()
    {
        FieldConfiguration.SaveCount = 0;
        var configuration = new FieldConfiguration { AutoOceanFish = false };

        Assert.True(AutoHookConfigurationAccessor.TryCreate(configuration, out var accessor, out _));
        var result = accessor.Synchronize(true);

        Assert.True(result.Success);
        Assert.True(result.Changed);
        Assert.True(result.Value);
        Assert.True(configuration.AutoOceanFish);
        Assert.Equal(1, FieldConfiguration.SaveCount);
    }

    [Fact]
    public void Synchronize_WritesPropertyAndPersistsChangedValue()
    {
        PropertyConfiguration.SaveCount = 0;
        var configuration = new PropertyConfiguration { AutoOceanFish = true };

        Assert.True(AutoHookConfigurationAccessor.TryCreate(configuration, out var accessor, out _));
        var result = accessor.Synchronize(false);

        Assert.True(result.Success);
        Assert.True(result.Changed);
        Assert.False(result.Value);
        Assert.False(configuration.AutoOceanFish);
        Assert.Equal(1, PropertyConfiguration.SaveCount);
    }

    [Fact]
    public void Synchronize_DoesNotSaveUnchangedValue()
    {
        FieldConfiguration.SaveCount = 0;
        var configuration = new FieldConfiguration { AutoOceanFish = true };

        Assert.True(AutoHookConfigurationAccessor.TryCreate(configuration, out var accessor, out _));
        var result = accessor.Synchronize(true);

        Assert.True(result.Success);
        Assert.False(result.Changed);
        Assert.Equal(0, FieldConfiguration.SaveCount);
    }

    [Fact]
    public void TryCreate_ReportsMissingWritableBooleanSurface()
    {
        var created = AutoHookConfigurationAccessor.TryCreate(
            new ReadOnlyConfiguration(),
            out _,
            out var status);

        Assert.False(created);
        Assert.Contains("readable and writable Boolean", status, StringComparison.Ordinal);
    }

    [Fact]
    public void TryCreate_ReportsMissingStaticSaveMethod()
    {
        var created = AutoHookConfigurationAccessor.TryCreate(
            new NoSaveConfiguration(),
            out _,
            out var status);

        Assert.False(created);
        Assert.Contains("static Save()", status, StringComparison.Ordinal);
    }

    [Fact]
    public void Synchronize_ReportsSaveFailure()
    {
        var configuration = new ThrowingSaveConfiguration { AutoOceanFish = false };
        Assert.True(AutoHookConfigurationAccessor.TryCreate(configuration, out var accessor, out _));

        var result = accessor.Synchronize(true);

        Assert.False(result.Success);
        Assert.Contains("save failed", result.Status, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FieldConfiguration
    {
        public static int SaveCount;
        public bool AutoOceanFish;
        public static void Save() => SaveCount++;
    }

    private sealed class PropertyConfiguration
    {
        public static int SaveCount;
        public bool AutoOceanFish { get; set; }
        public static void Save() => SaveCount++;
    }

    private sealed class ReadOnlyConfiguration
    {
        public bool AutoOceanFish => true;
        public static void Save() { }
    }

    private sealed class NoSaveConfiguration
    {
        public bool AutoOceanFish = false;
    }

    private sealed class ThrowingSaveConfiguration
    {
        public bool AutoOceanFish;
        public static void Save() => throw new InvalidOperationException("save failed");
    }
}
