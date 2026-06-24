using VERMAXION.Models;
using Xunit;

namespace VERMAXION.Tests;

public sealed class UiCloseFallbackPolicyTests
{
    [Fact]
    public void GuardedModeDoesNotPressFallbackEscapeWhenNoKnownAddonWasVisible()
    {
        Assert.False(UiCloseFallbackPolicy.ShouldPressFallbackEscape(
            UiCloseFallbackMode.OnlyWhenKnownAddonVisible,
            knownAddonWasVisible: false));
    }

    [Fact]
    public void GuardedModePressesFallbackEscapeWhenKnownAddonWasVisible()
    {
        Assert.True(UiCloseFallbackPolicy.ShouldPressFallbackEscape(
            UiCloseFallbackMode.OnlyWhenKnownAddonVisible,
            knownAddonWasVisible: true));
    }

    [Fact]
    public void AlwaysModePreservesExplicitCleanupFallbackEscape()
    {
        Assert.True(UiCloseFallbackPolicy.ShouldPressFallbackEscape(
            UiCloseFallbackMode.Always,
            knownAddonWasVisible: false));
    }
}
