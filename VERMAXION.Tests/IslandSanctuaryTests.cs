extern alias live;
extern alias host;

using System;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Text.Json;
using VERMAXION.Models;
using Xunit;
using IslandRun = live::VERMAXION.Services.IslandSanctuaryRun;
using ProviderStatus = live::VERMAXION.IPC.CalypsoIslandStatus;
using RuntimeConfig = live::VERMAXION.Models.CharacterConfig;
using RuntimeChore = live::VERMAXION.Models.IslandChore;
using RuntimeCadence = live::VERMAXION.Models.IslandVisitCadence;

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
        profile.IslandAgendaConfirmedCycle = "100.2";
        profile.IslandExportsConfirmedCycle = "99.6";
        profile.IslandSanctuary.Pasture = false;
        profile.IslandSanctuary.Seeds = false;
        var restored = JsonSerializer.Deserialize<CharacterConfig>(JsonSerializer.Serialize(profile))!;
        Assert.True(restored.EnableIslandSanctuary);
        Assert.Equal(cadence, restored.IslandSanctuary.Cadence);
        Assert.False(restored.IslandSanctuary.Pasture);
        Assert.False(restored.IslandSanctuary.Seeds);
        var clone = restored.Clone();
        Assert.Equal("100.2", clone.IslandAgendaConfirmedCycle);
        Assert.Equal("99.6", clone.IslandExportsConfirmedCycle);
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
        var target = new live::VERMAXION.Models.CharacterConfig { JumboCactpotLastCompleted = DateTime.UnixEpoch,
            IslandAgendaConfirmedCycle = "100.2", IslandExportsConfirmedCycle = "99.6" };
        source.IslandAgendaConfirmedCycle = "1.0"; source.IslandExportsConfirmedCycle = "1.0";
        typeof(live::VERMAXION.Services.ConfigManager).GetMethod("CopyDefaultSettings", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [source, target]);
        Assert.True(target.EnableIslandSanctuary);
        Assert.Equal(source.IslandSanctuary.Cadence, target.IslandSanctuary.Cadence);
        Assert.False(target.IslandSanctuary.Granary);
        Assert.Equal(DateTime.UnixEpoch, target.JumboCactpotLastCompleted);
        Assert.Equal("100.2", target.IslandAgendaConfirmedCycle);
        Assert.Equal("99.6", target.IslandExportsConfirmedCycle);
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
    public void SelectedIslandEntersExistingOrderedDispatchWithManualScope()
    {
        var feature = AutomationCatalog.Get(AutomationCatalog.IslandSanctuary);
        Assert.Equal(nameof(CharacterConfig.EnableIslandSanctuary), feature.FlagProperty);
        Assert.Equal(AutomationOwner.EngineTask, feature.Owner);
        Assert.Equal(AutomationCadence.Scheduled, feature.Cadence);
        Assert.Equal(AutomationMaturity.Wip, feature.Maturity);
        Assert.Contains(feature, AutomationCatalog.EngineTasks);
        Assert.Contains(feature.Id, PostProcessTaskOrder.DefaultOrder);
        Assert.Contains(feature.Id, PostProcessTaskOrder.Normalize([feature.Id]));
        Assert.Contains(feature.Id, PostProcessTaskOrder.Definitions.Select(task => task.Id));
        var eligibility = AutomationCatalog.Features.ToDictionary(item => item.Id,
            item => item.Id == feature.Id ? TaskEligibility.Runnable("fixture selected") : TaskEligibility.Disabled("fixture off"));
        Assert.Equal(new[] { feature.Id }, AutomationDispatchPlanner.BuildRunnableQueue(PostProcessTaskOrder.Normalize([feature.Id]), eligibility));
        Assert.True(AutomationRunScope.SingleTask(feature.Id).BypassSelectedScheduling);
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
            selected.IslandAgendaConfirmedCycle = "100.2";
            selected.IslandExportsConfirmedCycle = "99.6";
            manager.Accounts[viewed.AccountId].DefaultConfig.IslandAgendaConfirmedCycle = "1.0";
            manager.Accounts[viewed.AccountId].DefaultConfig.IslandExportsConfirmedCycle = "1.0";
            manager.SelectedAccountId = viewed.AccountId;
            manager.ResetCharacterToDefault("viewed-character");
            var reset = manager.Accounts[viewed.AccountId].Characters["viewed-character"];
            Assert.Equal("100.2", reset.IslandAgendaConfirmedCycle);
            Assert.Equal("99.6", reset.IslandExportsConfirmedCycle);
            Assert.Equal("playing-character", manager.CurrentCharacterKey);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData(RuntimeCadence.Weekly, "100.6", "101.0", true)]
    [InlineData(RuntimeCadence.Weekly, "100.0", "100.6", false)]
    [InlineData(RuntimeCadence.Daily, "100.0", "100.1", true)]
    [InlineData(RuntimeCadence.Daily, "100.1", "100.1", false)]
    public void IslandCadenceUsesProviderCycle(RuntimeCadence cadence, string last, string current, bool expected)
        => Assert.Equal(expected, IslandRun.IsDue(cadence, last, current));

    [Fact]
    public void SupportedChoresRequireIslandAndRespectManualScheduling()
    {
        var status = ReadyIsland();
        var config = new RuntimeConfig { EnableIslandSanctuary = true };
        var calls = 0;
        var run = new IslandRun(() => status, _ => { calls++; return true; }, () => { });
        run.Refresh(DateTime.UtcNow);
        config.IslandSanctuary.Cadence = RuntimeCadence.Manual;
        Assert.False(run.Eligibility(config, status.Character).IsRunnable);
        Assert.True(run.Eligibility(config, status.Character, manual: true).IsRunnable);
        status.InSanctuary = false;
        run.Start(config, status.Character, manual: true);
        Assert.True(run.IsFailed);
        Assert.Equal(0, calls);
        status.InSanctuary = true;
        config.IslandSanctuary.Agenda = config.IslandSanctuary.Exports = false;
        Assert.Equal(live::VERMAXION.Models.TaskEligibilityStatus.Unsupported,
            run.Eligibility(config, status.Character, manual: true).Status);
        Assert.Empty(run.ConfirmedChores);
    }

    [Fact]
    public void ActualReceiptsRunObserveThenAgendaThenExportsAndKeepSuggestPlannedOnly()
    {
        var status = ReadyIsland();
        var requested = new System.Collections.Generic.List<string>();
        var run = new IslandRun(() => status, id =>
        {
            requested.Add(id); status.Busy = true; status.Mode = "single"; status.CurrentTaskId = id;
            return true;
        }, () => throw new InvalidOperationException("Unexpected Stop"));
        var config = new RuntimeConfig { EnableIslandSanctuary = true };
        run.Start(config, status.Character, manual: false);
        run.Update(status.Character);
        Assert.Equal(new[] { "workshop.observe" }, requested);
        Assert.Empty(run.ConfirmedChores);
        run.Update(status.Character); // Busy is not completion.
        Assert.Empty(run.ConfirmedChores);
        CompleteIsland(status, "workshop.observe", "read today");
        run.Update(status.Character);
        Assert.Equal(new[] { "workshop.observe", "workshop.agenda" }, requested);
        Assert.Empty(run.ConfirmedChores);
        CompleteIsland(status, "workshop.agenda", "planned today (suggest only)");
        run.Update(status.Character);
        Assert.Contains(RuntimeChore.Agenda, run.ConfirmedChores);
        Assert.DoesNotContain(RuntimeChore.Exports, run.ConfirmedChores);
        CompleteIsland(status, "exports.sell", "nothing at its cap");
        run.Update(status.Character);
        Assert.Equal(new[] { "workshop.observe", "workshop.agenda", "exports.sell" }, requested);
        Assert.True(run.IsComplete);
        Assert.Equal(2, run.ConfirmedChores.Count);
        Assert.Contains("planned only", run.StatusText);
    }

    [Fact]
    public void ExportsOnlyCompletionDoesNotClaimAnUnrelatedSuggestedAgenda()
    {
        var status = ReadyIsland();
        CompleteIsland(status, "workshop.observe", "read today");
        CompleteIsland(status, "workshop.agenda", "planned today (suggest only)");
        var requested = new System.Collections.Generic.List<string>();
        var run = new IslandRun(() => status, id =>
        {
            requested.Add(id);
            status.Busy = true; status.Mode = "single"; status.CurrentTaskId = id;
            return true;
        }, () => throw new InvalidOperationException("Unexpected Stop"));
        var config = new RuntimeConfig();
        config.IslandSanctuary.Agenda = false;
        run.Start(config, status.Character, manual: true);
        run.Update(status.Character);
        CompleteIsland(status, "exports.sell", "sold today");
        run.Update(status.Character);
        Assert.True(run.IsComplete);
        Assert.False(run.IsFailed);
        Assert.Equal(new[] { "exports.sell" }, requested);
        Assert.Equal(new[] { RuntimeChore.Exports }, run.ConfirmedChores);
        Assert.Equal("Selected supported chores confirmed. Unavailable chores were not run or credited.", run.StatusText);
    }

    [Theory]
    [InlineData("Read workshop demand: done", true)]
    [InlineData("Read workshop demand: done (42 crafts read)", true)]
    [InlineData("Plan workshop agenda: done (planned 1000 cowries for the rest of the week)", true)]
    [InlineData("Sell full materials: done (sold 899)", true)]
    [InlineData("Read workshop demand: done ()", false)]
    [InlineData("Read workshop demand: done()", false)]
    [InlineData("read workshop demand: done (42 crafts read)", false)]
    [InlineData("Read workshop demand: done but failed", false)]
    [InlineData("Read workshop demand: done (42 crafts read", false)]
    [InlineData("Read workshop demand: done (42 crafts read) trailing", false)]
    [InlineData("Other task: done (42 crafts read)", false)]
    [InlineData("Read workshop demand: failed (error)", false)]
    public void PinnedProviderSuccessfulResultGrammarMatchesOnlyTheNamedTask(string result, bool expected)
    {
        var status = ReadyIsland();
        var id = result.StartsWith("Plan workshop agenda", StringComparison.Ordinal) ? "workshop.agenda"
            : result.StartsWith("Sell full materials", StringComparison.Ordinal) ? "exports.sell" : "workshop.observe";
        Assert.Equal(expected, status.Task(id)!.HasSuccessfulResult(result));
    }

    [Theory]
    [InlineData("workshop.agenda", "tomorrow is set", "tomorrow set to rest")]
    [InlineData("exports.sell", "sold today", "sold 899")]
    public void BusySingleEmptyTaskWindowsWaitWithoutStopOrPrematureCredit(string id, string reason, string detail)
    {
        var status = ReadyIsland();
        CompleteIsland(status, "workshop.observe", "read today");
        var config = new RuntimeConfig();
        config.IslandSanctuary.Agenda = id == "workshop.agenda";
        config.IslandSanctuary.Exports = id == "exports.sell";
        var calls = 0;
        var stops = 0;
        var run = new IslandRun(() => status, taskId =>
        {
            Assert.Equal(id, taskId);
            calls++;
            status.Busy = true;
            status.Mode = "single";
            status.CurrentTaskId = ""; // StartSingle has not reached PickNext yet.
            return true;
        }, () => stops++);
        run.Start(config, status.Character, manual: true);
        run.Update(status.Character);
        run.Update(status.Character);
        Assert.True(run.IsActive);
        Assert.False(run.IsFailed);
        Assert.Empty(run.ConfirmedChores);
        Assert.Equal(0, stops);
        status.CurrentTaskId = id;
        run.Update(status.Character);
        Assert.True(run.IsActive);
        CompleteIsland(status, id, reason, detail);
        status.Busy = true;
        status.Mode = "single";
        status.CurrentTaskId = ""; // Complete precedes the following PickNext/Finish tick.
        run.Update(status.Character);
        Assert.True(run.IsActive);
        Assert.False(run.IsFailed);
        Assert.Empty(run.ConfirmedChores);
        Assert.Equal(0, stops);
        status.Busy = false;
        status.Mode = "";
        run.Update(status.Character);
        Assert.True(run.IsComplete);
        Assert.False(run.IsFailed);
        Assert.Equal(1, calls);
        Assert.Equal(new[] { id == "workshop.agenda" ? RuntimeChore.Agenda : RuntimeChore.Exports }, run.ConfirmedChores);
        Assert.Equal(0, stops);
    }

    [Theory]
    [InlineData("loop", "workshop.observe")]
    [InlineData("single", "exports.sell")]
    public void KnownForeignBusyWorkIsReleasedWithoutStopOrCredit(string mode, string id)
    {
        var status = ReadyIsland();
        var stops = 0;
        var run = new IslandRun(() => status, _ => true, () => stops++);
        run.Start(new RuntimeConfig(), status.Character, manual: true);
        run.Update(status.Character);
        status.Busy = true;
        status.Mode = mode;
        status.CurrentTaskId = id;
        run.Update(status.Character);
        Assert.True(run.IsFailed);
        Assert.False(run.IsCleanupPending);
        Assert.Empty(run.ConfirmedChores);
        Assert.Equal(0, stops);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 3)]
    public void EachRequestRechecksProviderEnabledAndFailureCap(bool enabled, int failures)
    {
        var status = ReadyIsland();
        var calls = new System.Collections.Generic.List<string>();
        var run = new IslandRun(() => status, id => { calls.Add(id); return true; }, () => { });
        run.Start(new RuntimeConfig(), status.Character, manual: true);
        run.Update(status.Character);
        CompleteIsland(status, "workshop.observe", "read today");
        status.Task("workshop.agenda")!.Enabled = enabled;
        status.Task("workshop.agenda")!.Failures = failures;
        run.Update(status.Character);
        Assert.True(run.IsFailed);
        Assert.Equal(new[] { "workshop.observe" }, calls);
        Assert.Empty(run.ConfirmedChores);
    }

    [Theory]
    [InlineData("off", "Read workshop demand: done (42 crafts read)")]
    [InlineData("read today", "Read workshop demand: done but failed")]
    [InlineData("read today", "Stopped")]
    public void IdleOrTextWithoutMatchingStructuredSuccessfulResultNeverCredits(string reason, string result)
    {
        var status = ReadyIsland();
        var run = new IslandRun(() => status, _ => true, () => { });
        run.Start(new RuntimeConfig(), status.Character, manual: true);
        run.Update(status.Character);
        CompleteIsland(status, "workshop.observe", reason);
        status.LastResult = result;
        run.Update(status.Character);
        Assert.True(run.IsFailed);
        Assert.False(run.IsComplete);
        Assert.Empty(run.ConfirmedChores);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ChangedCharacterOrCycleRejectsReceiptWithoutStoppingForeignWork(bool character)
    {
        var status = ReadyIsland();
        var stops = 0;
        var run = new IslandRun(() => status, _ => true, () => stops++);
        var owner = status.Character;
        run.Start(new RuntimeConfig(), owner, manual: true);
        run.Update(owner);
        CompleteIsland(status, "workshop.observe", "read today");
        if (character) status.Character = "Other@Fixture"; else status.Cycle = "101.0";
        run.Update(owner);
        Assert.True(run.IsFailed);
        Assert.Equal(0, stops);
        Assert.Empty(run.ConfirmedChores);
    }

    [Theory]
    [InlineData("")]
    [InlineData("exports.sell")]
    public void CancelNeverStopsAnUnmatchedBusyTask(string currentTaskId)
    {
        var status = ReadyIsland();
        var stops = 0;
        var run = new IslandRun(() => status, _ => true, () => stops++);
        run.Start(new RuntimeConfig(), status.Character, manual: true);
        run.Update(status.Character);
        status.Busy = true; status.Mode = "single"; status.CurrentTaskId = currentTaskId;
        run.Cancel();
        Assert.Equal(0, stops);
        Assert.Equal(currentTaskId.Length == 0, run.IsCleanupPending);
        Assert.False(run.IsComplete);
    }

    [Fact]
    public void StopFailureRetainsOwnedCleanupWithoutRepeatingStopUntilFreshIdle()
    {
        var status = ReadyIsland();
        var stops = 0;
        var run = new IslandRun(() => status, id =>
        {
            status.Busy = true; status.Mode = "single"; status.CurrentTaskId = id; return true;
        }, () => { stops++; throw new InvalidOperationException("offline Stop failure"); });
        run.Start(new RuntimeConfig(), status.Character, manual: true);
        run.Update(status.Character);
        run.Cancel();
        Assert.True(run.IsCleanupPending);
        run.Reset(); run.Cancel();
        Assert.Equal(1, stops);
        Assert.True(run.IsCleanupPending);
        var now = DateTime.UtcNow;
        run.Refresh(now);
        Assert.True(run.IsCleanupPending);
        status.Busy = false;
        run.Refresh(now.AddSeconds(2));
        Assert.False(run.IsCleanupPending);
        Assert.False(run.IsComplete);
        Assert.Empty(run.ConfirmedChores);
    }

    [Theory]
    [InlineData(RuntimeChore.Agenda)]
    [InlineData(RuntimeChore.Exports)]
    public void AlreadyDoneChoreWithoutMatchingLastResultIsNotCredited(RuntimeChore chore)
    {
        var status = ReadyIsland();
        CompleteIsland(status, "workshop.observe", "read today");
        CompleteIsland(status, chore == RuntimeChore.Agenda ? "workshop.agenda" : "exports.sell",
            chore == RuntimeChore.Agenda ? "tomorrow is set" : "sold today");
        status.LastResult = "Stopped";
        var calls = 0;
        var run = new IslandRun(() => status, _ => { calls++; return true; }, () => { });
        var config = new RuntimeConfig();
        config.IslandSanctuary.Agenda = chore == RuntimeChore.Agenda;
        config.IslandSanctuary.Exports = chore == RuntimeChore.Exports;
        run.Start(config, status.Character, manual: true);
        run.Update(status.Character);
        Assert.True(run.IsFailed);
        Assert.False(run.IsComplete);
        Assert.Equal(0, calls);
        Assert.Empty(run.ConfirmedChores);
    }

    [Theory]
    [InlineData(RuntimeChore.Agenda, true)]
    [InlineData(RuntimeChore.Exports, true)]
    [InlineData(RuntimeChore.Agenda, false)]
    [InlineData(RuntimeChore.Exports, false)]
    public void AlreadyDoneChoreRequiresStableReceiptBetweenStartAndUpdate(RuntimeChore chore, bool changedFailures)
    {
        var status = ReadyIsland();
        CompleteIsland(status, "workshop.observe", "read today");
        var id = chore == RuntimeChore.Agenda ? "workshop.agenda" : "exports.sell";
        CompleteIsland(status, id, chore == RuntimeChore.Agenda ? "tomorrow is set" : "sold today");
        var calls = 0;
        var run = new IslandRun(() => status, _ => { calls++; return true; }, () => { });
        var config = new RuntimeConfig();
        config.IslandSanctuary.Agenda = chore == RuntimeChore.Agenda;
        config.IslandSanctuary.Exports = chore == RuntimeChore.Exports;
        run.Start(config, status.Character, manual: true);
        Assert.True(run.IsActive);
        if (changedFailures) status.Task(id)!.Failures++;
        else status.LastResult = "Stopped";
        run.Update(status.Character);
        Assert.True(run.IsFailed);
        Assert.False(run.IsComplete);
        Assert.Equal(0, calls);
        Assert.Empty(run.ConfirmedChores);
    }

    [Fact]
    public void ConfirmedAgendaRemainsSeparateWhenExportsFails()
    {
        var status = ReadyIsland();
        CompleteIsland(status, "workshop.observe", "read today");
        CompleteIsland(status, "workshop.agenda", "tomorrow is set");
        var run = new IslandRun(() => status, _ => true, () => { });
        run.Start(new RuntimeConfig(), status.Character, manual: true);
        run.Update(status.Character);
        Assert.Contains(RuntimeChore.Agenda, run.ConfirmedChores);
        CompleteIsland(status, "exports.sell", "sold today");
        status.LastResult = "Stopped";
        run.Update(status.Character);
        Assert.True(run.IsFailed);
        Assert.Equal(new[] { RuntimeChore.Agenda }, run.ConfirmedChores);
        Assert.DoesNotContain(RuntimeChore.Exports, run.ConfirmedChores);
        Assert.Equal("100.2", run.ConfirmedCycle);
    }

    [Theory]
    [InlineData("{\"Schema\":2,\"Character\":\"Player@Fixture\",\"Cycle\":\"100.2\",\"Tasks\":[]}")]
    [InlineData("{\"Schema\":1,\"Character\":\"Player@Fixture\",\"Cycle\":\"bad\",\"Tasks\":[]}")]
    [InlineData("{\"Schema\":1,\"Character\":\"Player@Fixture\",\"Cycle\":\"100.2\",\"Tasks\":[{\"Id\":\"exports.sell\",\"State\":\"ready\",\"Enabled\":true}]}")]
    [InlineData("{\"Schema\":1,\"Character\":\"Player@Fixture\",\"Cycle\":\"100.2\",\"Busy\":false,\"Tasks\":[{\"Id\":\"exports.sell\",\"Name\":\"Exports\",\"State\":\"ready\",\"Enabled\":true}]}")]
    public void MalformedProviderEnvelopeFailsClosed(string json)
        => Assert.Null(ProviderStatus.Parse(json));

    [Fact]
    public void LostProviderStatusRetainsUnconfirmedOwnedCleanup()
    {
        ProviderStatus? status = ReadyIsland();
        var snapshot = status;
        var stops = 0;
        var run = new IslandRun(() => status, id =>
        {
            snapshot.Busy = true; snapshot.Mode = "single"; snapshot.CurrentTaskId = id; return true;
        }, () => stops++);
        run.Start(new RuntimeConfig(), snapshot.Character, manual: true);
        run.Update(snapshot.Character);
        status = null;
        run.Update(snapshot.Character);
        Assert.True(run.IsFailed);
        Assert.True(run.IsCleanupPending);
        Assert.False(run.IsComplete);
        Assert.Empty(run.ConfirmedChores);
        Assert.Equal(0, stops); // Unknown ownership does not authorize a global Stop.
        snapshot.Busy = false; status = snapshot;
        run.Refresh(DateTime.UtcNow.AddSeconds(2));
        Assert.False(run.IsCleanupPending);
    }

    [Fact]
    public void CompleteSchemaOneEnvelopeParsesWithExplicitZeroFailureCounts()
    {
        var parsed = ProviderStatus.Parse(JsonSerializer.Serialize(ReadyIsland(),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        Assert.NotNull(parsed);
        Assert.Equal("100.2", parsed!.Cycle);
        Assert.Equal(0, parsed.Task("exports.sell")!.Failures);
        Assert.True(parsed.Task("exports.sell")!.Enabled);
    }

    private static ProviderStatus ReadyIsland() => new()
    {
        Schema = 1, Character = "Player@Fixture", Cycle = "100.2", InSanctuary = true,
        Tasks =
        [
            new() { Id = "workshop.observe", Name = "Read workshop demand", State = "ready", Enabled = true },
            new() { Id = "workshop.agenda", Name = "Plan workshop agenda", State = "blocked", Reason = "needs today's demand reading first", Enabled = true },
            new() { Id = "exports.sell", Name = "Sell full materials", State = "ready", Enabled = true },
        ],
    };

    private static void CompleteIsland(ProviderStatus status, string id, string reason, string detail = "fixture completion")
    {
        var task = status.Task(id)!;
        task.State = "done"; task.Reason = reason;
        status.Busy = false; status.Mode = ""; status.CurrentTaskId = "";
        status.LastResult = task.Name + ": done" + (string.IsNullOrEmpty(detail) ? "" : " (" + detail + ")");
        if (id == "workshop.observe") status.Task("workshop.agenda")!.State = "ready";
    }

    public class ConfigServices : DispatchProxy
    {
        public string Directory = "";
        protected override object? Invoke(MethodInfo? method, object?[]? args)
            => method!.Name == "GetPluginConfigDirectory" ? Directory : null;
    }
}
