using System.Collections.Generic;
using System.Linq;
using VERMAXION.Models;
using Xunit;

namespace VERMAXION.Tests;

public sealed class FavoriteAndListingPacingTests
{
    [Fact]
    public void FavoriteResolutionIgnoresNullUnknownDuplicateAndRetiredIds()
    {
        List<string>? nullIds = null;
        var saved = new[]
        {
            AutomationCatalog.Fishing,
            "retired_automation",
            AutomationCatalog.Fishing,
            AutomationCatalog.MiniCactpot,
        };

        Assert.Empty(AutomationCatalog.ResolveFavorites(nullIds));
        Assert.Equal(
            new[] { AutomationCatalog.MiniCactpot, AutomationCatalog.Fishing },
            AutomationCatalog.ResolveFavorites(saved).Select(feature => feature.Id));
    }

    [Fact]
    public void FavoriteToggleAddsOnceRemovesAllDuplicatesAndIgnoresUnknownTargets()
    {
        var saved = new[]
        {
            "retired_automation",
            AutomationCatalog.Fishing,
            AutomationCatalog.Fishing,
        };

        var removed = AutomationCatalog.ToggleFavorite(saved, AutomationCatalog.Fishing);
        var added = AutomationCatalog.ToggleFavorite(removed, AutomationCatalog.MiniCactpot);
        var unchanged = AutomationCatalog.ToggleFavorite(added, "unknown_automation");

        Assert.Equal(new[] { "retired_automation" }, removed);
        Assert.Equal(new[] { "retired_automation", AutomationCatalog.MiniCactpot }, added);
        Assert.Equal(added, unchanged);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(250, 250)]
    [InlineData(2000, 2000)]
    [InlineData(2001, 2000)]
    public void ListingDelaysClampIndependently(int configured, int expected)
    {
        var action = RefillListingPacingSnapshot.Capture(configured, 777);
        var interItem = RefillListingPacingSnapshot.Capture(777, configured);

        Assert.Equal(expected, action.ActionDelayMs);
        Assert.Equal(777, action.InterItemDelayMs);
        Assert.Equal(777, interItem.ActionDelayMs);
        Assert.Equal(expected, interItem.InterItemDelayMs);
    }

    [Fact]
    public void OnlySuccessfulVerificationUsesInterItemDelay()
    {
        var pacing = RefillListingPacingSnapshot.Capture(123, 987);

        Assert.Equal(123, pacing.SelectDelay(RefillListingPacingEvent.MenuOrClick));
        Assert.Equal(987, pacing.SelectDelay(RefillListingPacingEvent.WithdrawalVerified));
        Assert.Equal(
            RefillListingPacingSnapshot.VerificationPollDelayMs,
            pacing.SelectDelay(RefillListingPacingEvent.WithdrawalNotVerified));
        Assert.NotEqual(987, pacing.SelectDelay(RefillListingPacingEvent.MenuOrClick));
        Assert.NotEqual(987, pacing.SelectDelay(RefillListingPacingEvent.WithdrawalNotVerified));
    }
}
