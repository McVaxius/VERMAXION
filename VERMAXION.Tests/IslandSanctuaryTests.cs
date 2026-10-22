extern alias live;
extern alias host;

using System;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Text.Json;
using VERMAXION.Models;
using Xunit;

namespace VERMAXION.Tests;

public sealed class IslandSanctuaryTests
{
    [Fact]
    public void NewAndLegacyProfilesAreOptInWeeklyWithAllSevenChoresSelected()
    {
        foreach (var profile in new[] { CharacterConfig.CreateNew(), JsonSerializer.Deserialize<CharacterConfig>("{}")! })
        {
            Assert.False(profile.EnableIslandSanctuary);
            Assert.Equal(IslandVisitCadence.Weekly, profile.IslandSanctuary.Cadence);
            Assert.All(Enum.GetValues<IslandChore>(), chore => Assert.True(profile.IslandSanctuary.IsSelected(chore)));
        }
    }

    [Theory]
    [InlineData(IslandVisitCadence.Weekly)]
    [InlineData(IslandVisitCadence.Daily)]
    [InlineData(IslandVisitCadence.Manual)]
    public void JsonRoundTripAndCloneRetainIndependentSavedPreferences(IslandVisitCadence cadence)
    {
        var profile = CharacterConfig.CreateNew();
        profile.EnableIslandSanctuary = true;
        profile.IslandSanctuary.Cadence = cadence;
        profile.IslandSanctuary.Pasture = false;
        profile.IslandSanctuary.Seeds = false;
        var restored = JsonSerializer.Deserialize<CharacterConfig>(JsonSerializer.Serialize(profile))!;
        Assert.True(restored.EnableIslandSanctuary);
        Assert.Equal(cadence, restored.IslandSanctuary.Cadence);
        Assert.False(restored.IslandSanctuary.Pasture);
        Assert.False(restored.IslandSanctuary.Seeds);
        var clone = restored.Clone();
        clone.IslandSanctuary.Income = false;
        clone.IslandSanctuary.Cadence = IslandVisitCadence.Manual;
        Assert.True(restored.IslandSanctuary.Income);
        Assert.Equal(cadence, restored.IslandSanctuary.Cadence);
        Assert.NotSame(clone.IslandSanctuary, restored.IslandSanctuary);
    }

    [Fact]
    public void ActualDefaultCopyCopiesPreferencesIndependentlyWithoutClearingTaskHistory()
    {
        var source = new live::VERMAXION.Models.CharacterConfig { EnableIslandSanctuary = true };
        source.IslandSanctuary.Cadence = live::VERMAXION.Models.IslandVisitCadence.Manual;
        source.IslandSanctuary.Granary = false;
        var target = new live::VERMAXION.Models.CharacterConfig { JumboCactpotLastCompleted = DateTime.UnixEpoch };
        typeof(live::VERMAXION.Services.ConfigManager).GetMethod("CopyDefaultSettings", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [source, target]);
        Assert.True(target.EnableIslandSanctuary);
        Assert.Equal(source.IslandSanctuary.Cadence, target.IslandSanctuary.Cadence);
        Assert.False(target.IslandSanctuary.Granary);
        Assert.Equal(DateTime.UnixEpoch, target.JumboCactpotLastCompleted);
        target.IslandSanctuary.Seeds = false;
        Assert.True(source.IslandSanctuary.Seeds);
        Assert.NotSame(source.IslandSanctuary, target.IslandSanctuary);
    }

    [Fact]
    public void RouteGroupsWorkshopAndKeepsRequiredArrivalAndConditionalSeeds()
    {
        var route = IslandRoutineStep.Route;
        Assert.Equal(new[] { 1, 2, 2, 3, 4, 5, 6, 7 }, route.Select(step => step.Stage));
        Assert.Null(route[0].Chore);
        Assert.Equal(new[] { IslandChore.Income, IslandChore.Agenda, IslandChore.Granary, IslandChore.Exports,
            IslandChore.Pasture, IslandChore.Crops, IslandChore.Seeds }, route.Skip(1).Select(step => step.Chore!.Value));
        Assert.Contains("same workshop stop", route[2].Detail);
        Assert.Contains("main building", route[4].Detail);
        Assert.Contains("only low seed types", route[^1].Detail);
        Assert.Equal(5, route.Skip(1).Count(step => step.Capability == "TBD"));
    }

    [Fact]
    public void SelectedIslandRemainsConfigOnlyAndCannotEnterOrderedDispatch()
    {
        var feature = AutomationCatalog.Get(AutomationCatalog.IslandSanctuary);
        Assert.Equal(nameof(CharacterConfig.EnableIslandSanctuary), feature.FlagProperty);
        Assert.Equal(AutomationOwner.ConfigOnlyWip, feature.Owner);
        Assert.Equal(AutomationCadence.ConfigOnly, feature.Cadence);
        Assert.Equal(AutomationMaturity.Wip, feature.Maturity);
        Assert.DoesNotContain(feature, AutomationCatalog.EngineTasks);
        Assert.DoesNotContain(feature.Id, PostProcessTaskOrder.DefaultOrder);
        Assert.DoesNotContain(feature.Id, PostProcessTaskOrder.Normalize([feature.Id]));
        Assert.DoesNotContain(feature.Id, PostProcessTaskOrder.Definitions.Select(task => task.Id));
        var eligibility = AutomationCatalog.Features.ToDictionary(item => item.Id,
            item => item.Id == feature.Id ? TaskEligibility.Runnable("fixture selected") : TaskEligibility.Disabled("fixture off"));
        Assert.Empty(AutomationDispatchPlanner.BuildRunnableQueue(PostProcessTaskOrder.Normalize([feature.Id]), eligibility));
    }

    [Fact]
    public void CapturedViewedAccountSaveKeepsRuntimeAccountAndOtherProfilesUnchanged()
    {
        var directory = Path.Combine(Path.GetTempPath(), "VERMAXION-IslandFixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var playing = new live::VERMAXION.Models.AccountConfig { AccountId = "playing-fixture", RegistrableInventoryDefaultV04011Applied = true };
            playing.Characters.Add("playing-character", new());
            var viewed = new live::VERMAXION.Models.AccountConfig { AccountId = "viewed-fixture", RegistrableInventoryDefaultV04011Applied = true };
            viewed.Characters.Add("viewed-character", new());
            foreach (var account in new[] { playing, viewed })
                File.WriteAllText(Path.Combine(directory, account.AccountId + "_Vermaxion.json"), JsonSerializer.Serialize(account));
            var pluginInterface = DispatchProxy.Create<host::Dalamud.Plugin.IDalamudPluginInterface, ConfigServices>();
            ((ConfigServices)(object)pluginInterface).Directory = directory;
            var log = DispatchProxy.Create<host::Dalamud.Plugin.Services.IPluginLog, ConfigServices>();
            var manager = new live::VERMAXION.Services.ConfigManager(pluginInterface, log) { CurrentAccountId = playing.AccountId };
            typeof(live::VERMAXION.Services.ConfigManager).GetProperty("CurrentCharacterKey")!.SetValue(manager, "playing-character");
            manager.SelectedAccountId = viewed.AccountId;
            manager.SelectedCharacterKey = "viewed-character";
            var selected = manager.GetSelectedConfig();
            selected.EnableIslandSanctuary = true;
            selected.IslandSanctuary.Cadence = live::VERMAXION.Models.IslandVisitCadence.Manual;
            selected.IslandSanctuary.Crops = false;
            var capturedAccountId = manager.SelectedAccountId;
            manager.SelectedAccountId = playing.AccountId;
            typeof(live::VERMAXION.Services.ConfigManager).GetMethod("SaveAccount", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(manager, [capturedAccountId]);

            Assert.Equal(playing.AccountId, manager.CurrentAccountId);
            Assert.Equal("playing-character", manager.CurrentCharacterKey);
            Assert.False(manager.GetActiveConfig().EnableIslandSanctuary);
            var saved = JsonSerializer.Deserialize<live::VERMAXION.Models.AccountConfig>(
                File.ReadAllText(Path.Combine(directory, viewed.AccountId + "_Vermaxion.json")))!;
            Assert.True(saved.Characters["viewed-character"].EnableIslandSanctuary);
            Assert.Equal(live::VERMAXION.Models.IslandVisitCadence.Manual, saved.Characters["viewed-character"].IslandSanctuary.Cadence);
            Assert.False(saved.Characters["viewed-character"].IslandSanctuary.Crops);
            Assert.False(saved.DefaultConfig.EnableIslandSanctuary);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    public class ConfigServices : DispatchProxy
    {
        public string Directory = "";
        protected override object? Invoke(MethodInfo? method, object?[]? args)
            => method!.Name == "GetPluginConfigDirectory" ? Directory : null;
    }
}
