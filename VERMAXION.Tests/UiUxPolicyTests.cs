using System.Collections.Generic;
using System.Linq;
using VERMAXION.Models;
using Xunit;

namespace VERMAXION.Tests;

public sealed class UiUxPolicyTests
{
    [Theory]
    [InlineData(TaskEligibilityStatus.Runnable, false, AutomationDashboardSection.DueNow)]
    [InlineData(TaskEligibilityStatus.Blocked, false, AutomationDashboardSection.Blocked)]
    [InlineData(TaskEligibilityStatus.Unsupported, false, AutomationDashboardSection.Blocked)]
    [InlineData(TaskEligibilityStatus.Disabled, false, AutomationDashboardSection.ScheduledLater)]
    [InlineData(TaskEligibilityStatus.NotDue, false, AutomationDashboardSection.ScheduledLater)]
    [InlineData(TaskEligibilityStatus.NotDue, true, AutomationDashboardSection.Complete)]
    public void DashboardClassificationIsExplicit(
        TaskEligibilityStatus status,
        bool completed,
        AutomationDashboardSection expected)
    {
        Assert.Equal(expected, AutomationDashboardPolicy.Classify(status, completed));
        Assert.False(string.IsNullOrWhiteSpace(AutomationDashboardPolicy.GetStateLabel(expected)));
    }

    [Fact]
    public void RecoveryNavigationOnlyAdvertisesConfigurableBlockers()
    {
        Assert.Equal(
            ConfigurationSection.EveryAr,
            AutomationDashboardPolicy.GetRecoverySection(
                AutomationCatalog.RegisterRegistrables,
                "no personal items configured"));
        Assert.Equal(
            ConfigurationSection.Daily,
            AutomationDashboardPolicy.GetRecoverySection(
                AutomationCatalog.NagYourMom,
                "Set a mom job."));
        Assert.Null(AutomationDashboardPolicy.GetRecoverySection(
            AutomationCatalog.GearUpdater,
            "stable current-character data unavailable"));
        Assert.Null(AutomationDashboardPolicy.GetRecoverySection(
            AutomationCatalog.NagYourMom,
            "mom IPC unavailable"));
        Assert.Equal(
            AutomationDashboardSection.ScheduledLater,
            AutomationDashboardPolicy.Classify(
                TaskEligibilityStatus.Blocked,
                completed: false,
                AutomationCatalog.FashionReport,
                "outside its Friday-to-reset availability window"));
        Assert.Equal(
            AutomationDashboardSection.Complete,
            AutomationDashboardPolicy.Classify(
                TaskEligibilityStatus.Blocked,
                completed: true,
                AutomationCatalog.FashionReport,
                "outside its Friday-to-reset availability window"));
    }

    [Fact]
    public void LaneMovesSwapOnlyWithinSelectedPhase()
    {
        var config = new Configuration();
        var before = PostProcessTaskOrder.GetLane(
            config.PostProcessTaskOrder,
            config.PostProcessTaskPlacement,
            PostProcessTaskPhase.BeforeAR);
        var after = PostProcessTaskOrder.GetLane(
            config.PostProcessTaskOrder,
            config.PostProcessTaskPlacement,
            PostProcessTaskPhase.AfterAR);
        Assert.True(before.Count >= 2);

        var moved = PostProcessTaskOrder.MoveWithinLane(
            config.PostProcessTaskOrder,
            config.PostProcessTaskPlacement,
            before[1],
            -1);

        Assert.Equal(before[1], PostProcessTaskOrder.GetLane(moved, config.PostProcessTaskPlacement, PostProcessTaskPhase.BeforeAR)[0]);
        Assert.Equal(before[0], PostProcessTaskOrder.GetLane(moved, config.PostProcessTaskPlacement, PostProcessTaskPhase.BeforeAR)[1]);
        Assert.Equal(after, PostProcessTaskOrder.GetLane(moved, config.PostProcessTaskPlacement, PostProcessTaskPhase.AfterAR));
    }

    [Fact]
    public void PhaseChangePreservesGlobalOrderAndNormalizesPlacement()
    {
        var config = new Configuration();
        var originalOrder = config.PostProcessTaskOrder.ToList();
        var taskId = originalOrder[0];
        var targetPhase = config.PostProcessTaskPlacement[taskId] == PostProcessTaskPhase.BeforeAR
            ? PostProcessTaskPhase.AfterAR
            : PostProcessTaskPhase.BeforeAR;

        var changed = PostProcessTaskOrder.ChangePhase(config.PostProcessTaskPlacement, taskId, targetPhase);

        Assert.Equal(originalOrder, config.PostProcessTaskOrder);
        Assert.Equal(targetPhase, changed[taskId]);
        Assert.Equal(PostProcessTaskOrder.DefaultOrder.Count, changed.Count);
        Assert.Equal(
            AutomationCatalog.EngineTasks.Select(feature => feature.Id).OrderBy(id => id),
            changed.Keys.OrderBy(id => id));
    }

    [Fact]
    public void WizardImpactIncludesFishingRowsAndApplyStaysWithinBoundary()
    {
        var current = CharacterConfig.CreateNew();
        current.EnableJumboCactpot = true;
        current.FishingStockItems[100] = new FishingStockSetting { Enabled = false, Target = 10, Min = 2 };
        var draft = current.Clone();
        draft.EnableFishing = true;
        draft.FishingStockItems[100].Enabled = true;
        draft.FishingStockItems[100].Target = 22;
        draft.FishingStockItems[200] = new FishingStockSetting { Enabled = true, Target = 5, Min = 1 };

        var impact = SetupWizardPolicy.GetImpact(SetupWizardKind.Fishing, current, draft);
        var target = current.Clone();
        SetupWizardPolicy.Apply(SetupWizardKind.Fishing, draft, target);

        Assert.Contains(impact, change => change.Key == nameof(CharacterConfig.EnableFishing));
        Assert.Contains(impact, change => change.Key == $"{nameof(CharacterConfig.FishingStockItems)}[100]");
        Assert.Contains(impact, change => change.Key == $"{nameof(CharacterConfig.FishingStockItems)}[200]");
        Assert.True(target.EnableFishing);
        Assert.Equal(22, target.FishingStockItems[100].Target);
        Assert.True(target.EnableJumboCactpot);
    }

    [Fact]
    public void WizardKindsCopyOnlyTheirExactFields()
    {
        var target = CharacterConfig.CreateNew();
        target.EnableFishing = true;
        target.EnableJumboCactpot = true;
        var draft = target.Clone();
        draft.EnableFCBuffRefill = true;
        draft.FCBuffPurchaseAttempts = 17;
        draft.FCBuffMinPoints = 123;
        draft.FCBuffMinGil = 456;
        draft.EnableFishing = false;
        draft.EnableJumboCactpot = false;

        SetupWizardPolicy.Apply(SetupWizardKind.FcBuff, draft, target);

        Assert.True(target.EnableFCBuffRefill);
        Assert.Equal(17, target.FCBuffPurchaseAttempts);
        Assert.Equal(123, target.FCBuffMinPoints);
        Assert.Equal(456, target.FCBuffMinGil);
        Assert.True(target.EnableFishing);
        Assert.True(target.EnableJumboCactpot);
    }

    [Fact]
    public void RegistrableSearchDeduplicatesAndMatchesIdOrName()
    {
        var names = new Dictionary<uint, string>
        {
            [10] = "Alpha Whistle",
            [20] = "Beta Roll",
        };

        Assert.Equal(new uint[] { 10, 20 }, RegistrableEditorPolicy.Normalize([10, 10, 0, 20]));
        Assert.Equal(new uint[] { 10 }, RegistrableEditorPolicy.SearchConfigured([10, 20], "alpha", names));
        Assert.Equal(new uint[] { 20 }, RegistrableEditorPolicy.SearchConfigured([10, 20], "20", names));
        Assert.Equal(new uint[] { 10, 20 }, RegistrableEditorPolicy.AddIfMissing([10, 10], 20));
    }

    [Fact]
    public void ImportPreviewValidatesCountsAndRequiresConfirmation()
    {
        var preview = RegistrableEditorPolicy.ParseImport(
            "[2, 2, 999, \"bad\", -1, 3]",
            new HashSet<uint> { 2, 3, 4 },
            new uint[] { 2, 4 });

        Assert.True(preview.IsValid);
        Assert.Equal(new uint[] { 2, 3 }, preview.AcceptedIds);
        Assert.Equal(2, preview.AcceptedCount);
        Assert.Equal(1, preview.DuplicateCount);
        Assert.Equal(1, preview.UnknownCount);
        Assert.Equal(2, preview.InvalidCount);
        Assert.Equal(1, preview.AddedCount);
        Assert.Equal(1, preview.RemovedCount);
        Assert.Equal(new uint[] { 2, 4 }, RegistrableEditorPolicy.ApplyImport([2, 4], preview, confirmed: false));
        Assert.Equal(new uint[] { 2, 3 }, RegistrableEditorPolicy.ApplyImport([2, 4], preview, confirmed: true));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("not json")]
    public void InvalidImportNeverMutates(string clipboard)
    {
        var preview = RegistrableEditorPolicy.ParseImport(clipboard, new HashSet<uint> { 1 }, new uint[] { 1 });

        Assert.False(preview.IsValid);
        Assert.Equal(new uint[] { 1 }, RegistrableEditorPolicy.ApplyImport([1], preview, confirmed: true));
    }
}
