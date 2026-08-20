using System;
using System.Collections.Generic;
using System.Linq;
using VERMAXION.Models;
using Xunit;

namespace VERMAXION.Tests;

public sealed class AutomationCatalogTests
{
    [Fact]
    public void EveryEnablePropertyHasExactlyOneCatalogOwner()
    {
        var enableProperties = typeof(CharacterConfig)
            .GetProperties()
            .Where(property => property.PropertyType == typeof(bool) &&
                               AutomationCatalog.IsFeatureEnableProperty(property.Name))
            .Select(property => property.Name)
            .OrderBy(name => name)
            .ToList();
        var catalogFlags = AutomationCatalog.Features
            .Select(feature => feature.FlagProperty)
            .OrderBy(name => name)
            .ToList();

        Assert.Equal(26, enableProperties.Count);
        Assert.Equal(enableProperties, catalogFlags);
        Assert.All(AutomationCatalog.Features, feature => Assert.True(Enum.IsDefined(feature.Owner)));
    }

    [Fact]
    public void EngineCatalogOrderAndRuntimeRegistrationsMatch()
    {
        var engineIds = AutomationCatalog.EngineTasks.Select(feature => feature.Id).ToList();
        var validation = AutomationCatalog.ValidateRuntimeRegistry(engineIds, PostProcessTaskOrder.DefaultOrder);

        Assert.Equal(20, engineIds.Count);
        Assert.True(validation.IsValid, validation.Message);
    }

    [Fact]
    public void RetainerEquippingIsDispatchableEngineTaskWhileMarkedWip()
    {
        var feature = AutomationCatalog.Get(AutomationCatalog.RetainerEquipping);
        var eligibility = AutomationCatalog.EngineTasks.ToDictionary(
            item => item.Id,
            item => item.Id == feature.Id
                ? TaskEligibility.Runnable("ready")
                : TaskEligibility.Disabled("not selected"));

        Assert.Equal(AutomationMaturity.Wip, feature.Maturity);
        Assert.Equal(AutomationOwner.EngineTask, feature.Owner);
        Assert.Contains(feature, AutomationCatalog.EngineTasks);
        Assert.Contains(feature.Id, PostProcessTaskOrder.DefaultOrder);
        Assert.Equal(
            [feature.Id],
            AutomationDispatchPlanner.BuildRunnableQueue(
                PostProcessTaskOrder.DefaultOrder,
                eligibility));
    }

    [Fact]
    public void MissingDuplicateAndExtraRegistrationsFailVisibly()
    {
        var engineIds = AutomationCatalog.EngineTasks.Select(feature => feature.Id).ToList();
        var runtimeIds = engineIds.Skip(1).Append(engineIds[1]).Append("unexpected").ToList();

        var validation = AutomationCatalog.ValidateRuntimeRegistry(runtimeIds, PostProcessTaskOrder.DefaultOrder);

        Assert.False(validation.IsValid);
        Assert.Contains("Configured but not dispatchable", validation.Message);
        Assert.Contains("missing", validation.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("extra", validation.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Duplicate", validation.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MigrationRemovesFishingAndInsertsNewTasksAfterRegister()
    {
        var original = new[]
        {
            PostProcessTaskOrder.NagYourDad,
            PostProcessTaskOrder.LegacyFishing,
            PostProcessTaskOrder.RegisterRegistrables,
            PostProcessTaskOrder.FCBuffRefill,
        };

        var normalized = PostProcessTaskOrder.Normalize(original);
        var registerIndex = normalized.IndexOf(PostProcessTaskOrder.RegisterRegistrables);

        Assert.DoesNotContain(PostProcessTaskOrder.LegacyFishing, normalized);
        Assert.Equal(
            PostProcessTaskOrder.NewlyDispatchableIds,
            normalized.Skip(registerIndex + 1).Take(PostProcessTaskOrder.NewlyDispatchableIds.Count));
        Assert.True(normalized.IndexOf(PostProcessTaskOrder.NagYourDad) < registerIndex);
        Assert.True(registerIndex < normalized.IndexOf(PostProcessTaskOrder.FCBuffRefill));
    }

    [Fact]
    public void MigrationPreservesExistingPhasesAndIsIdempotent()
    {
        var config = new Configuration
        {
            PostProcessTaskOrder =
            [
                PostProcessTaskOrder.NagYourMom,
                PostProcessTaskOrder.RegisterRegistrables,
                PostProcessTaskOrder.LegacyFishing,
                PostProcessTaskOrder.RefillListings,
            ],
            PostProcessTaskPlacement = new Dictionary<string, PostProcessTaskPhase>
            {
                [PostProcessTaskOrder.NagYourMom] = PostProcessTaskPhase.BeforeAR,
                [PostProcessTaskOrder.RegisterRegistrables] = PostProcessTaskPhase.BeforeAR,
                [PostProcessTaskOrder.RefillListings] = PostProcessTaskPhase.AfterAR,
                [PostProcessTaskOrder.LegacyFishing] = PostProcessTaskPhase.AfterAR,
            },
        };

        Assert.True(PostProcessTaskOrder.Normalize(config));
        Assert.Equal(PostProcessTaskPhase.BeforeAR, config.PostProcessTaskPlacement[PostProcessTaskOrder.NagYourMom]);
        Assert.Equal(PostProcessTaskPhase.BeforeAR, config.PostProcessTaskPlacement[PostProcessTaskOrder.RegisterRegistrables]);
        Assert.Equal(PostProcessTaskPhase.AfterAR, config.PostProcessTaskPlacement[PostProcessTaskOrder.RefillListings]);
        Assert.DoesNotContain(PostProcessTaskOrder.LegacyFishing, config.PostProcessTaskPlacement.Keys);
        Assert.All(PostProcessTaskOrder.NewlyDispatchableIds,
            id => Assert.Equal(PostProcessTaskOrder.GetDefaultPhase(id), config.PostProcessTaskPlacement[id]));
        Assert.False(PostProcessTaskOrder.Normalize(config));
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public void MiscHookRunsOnlyForApplicableAfterArOrManualRuns(bool enabled, bool beforeAr, bool expected)
    {
        Assert.Equal(expected, AutomationRunHookPolicy.ShouldRunMiscHook(enabled, beforeAr));
    }

    [Fact]
    public void MiscHookCanBeTheOnlyApplicableWork()
    {
        Assert.True(AutomationRunHookPolicy.HasApplicableWork(false, true));
        Assert.False(AutomationRunHookPolicy.HasApplicableWork(false, false));
    }

    [Fact]
    public void SingleTaskScopeRunsOnlyRetainerEquippingAndBypassesOnlyItsSchedulingFlag()
    {
        var scope = AutomationRunScope.SingleTask(PostProcessTaskOrder.RetainerEquipping);
        var allRunnable = PostProcessTaskOrder.DefaultOrder.ToDictionary(
            id => id,
            id => TaskEligibility.Runnable(id));
        var scopedOrder = AutomationRunScopePolicy.FilterOrderedIds(
            PostProcessTaskOrder.DefaultOrder,
            scope);

        Assert.Equal([PostProcessTaskOrder.RetainerEquipping], scopedOrder);
        Assert.Equal(
            [PostProcessTaskOrder.RetainerEquipping],
            AutomationDispatchPlanner.BuildRunnableQueue(scopedOrder, allRunnable));
        Assert.True(AutomationRunScopePolicy.IsTaskSchedulingEnabled(
            scope,
            PostProcessTaskOrder.RetainerEquipping,
            configuredEnabled: false));
        Assert.False(AutomationRunScopePolicy.IsTaskSchedulingEnabled(
            scope,
            PostProcessTaskOrder.FCBuffRefill,
            configuredEnabled: false));
        Assert.False(AutomationRunScopePolicy.ShouldRunMiscHook(
            scope,
            enabled: true,
            beforeArRun: false));
    }

    [Fact]
    public void EveryEngineTaskIndependentlyReachesItsOrderedBinding()
    {
        var ids = AutomationCatalog.EngineTasks.Select(feature => feature.Id).ToList();
        foreach (var runnableId in ids)
        {
            var eligibility = ids.ToDictionary(
                id => id,
                id => id == runnableId
                    ? TaskEligibility.Runnable($"{id} ready")
                    : TaskEligibility.Disabled($"{id} disabled"));

            Assert.Equal([runnableId], AutomationDispatchPlanner.BuildRunnableQueue(PostProcessTaskOrder.DefaultOrder, eligibility));
        }
    }

    [Fact]
    public void DispatchPlannerPreservesExactEligibilityDispositionAndReason()
    {
        var eligibility = new Dictionary<string, TaskEligibility>
        {
            [PostProcessTaskOrder.FCBuffRefill] = TaskEligibility.Disabled("checkbox off"),
            [PostProcessTaskOrder.MiniCactpot] = TaskEligibility.NotDue("daily reset pending"),
            [PostProcessTaskOrder.RegisterRegistrables] = TaskEligibility.Blocked("personal item list empty"),
            [PostProcessTaskOrder.GearUpdater] = TaskEligibility.Unsupported("binding unavailable"),
            [PostProcessTaskOrder.MinionRoulette] = TaskEligibility.Runnable("ready"),
        };

        Assert.Equal([PostProcessTaskOrder.MinionRoulette],
            AutomationDispatchPlanner.BuildRunnableQueue(PostProcessTaskOrder.DefaultOrder, eligibility));
        Assert.Equal("checkbox off", eligibility[PostProcessTaskOrder.FCBuffRefill].Reason);
        Assert.Equal("daily reset pending", eligibility[PostProcessTaskOrder.MiniCactpot].Reason);
        Assert.Equal("personal item list empty", eligibility[PostProcessTaskOrder.RegisterRegistrables].Reason);
        Assert.Equal("binding unavailable", eligibility[PostProcessTaskOrder.GearUpdater].Reason);
    }
}
