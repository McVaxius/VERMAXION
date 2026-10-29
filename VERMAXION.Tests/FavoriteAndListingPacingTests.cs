extern alias live;
extern alias host;

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;
using VERMAXION.Models;
using Xunit;

namespace VERMAXION.Tests;

[Collection("Beastmaster production host")]
public sealed class FavoriteAndListingPacingTests
{
    [Theory]
    [InlineData(ChocoboAutomationMode.TargetPedigree, true, false, true)]
    [InlineData(ChocoboAutomationMode.AlwaysRace, true, false, false)]
    [InlineData(ChocoboAutomationMode.TargetPedigree, false, true, false)]
    [InlineData(ChocoboAutomationMode.TargetPedigree, false, false, false)]
    public void ChocoboRaceGilAdmissionPreservesOnlyProvenNativeContinuation(
        ChocoboAutomationMode mode, bool nativeRace, bool savedPending, bool runnable)
    {
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        var profile = new live::VERMAXION.Models.CharacterConfig
        {
            Enabled = true, EnableChocoboRacing = true, ChocoboRacePending = savedPending,
            ChocoboAutomationMode = (live::VERMAXION.Models.ChocoboAutomationMode)mode,
        };
        var manager = (live::VERMAXION.Services.ConfigManager)RuntimeHelpers.GetUninitializedObject(typeof(live::VERMAXION.Services.ConfigManager));
        manager.CurrentAccountId = "gil-admission";
        typeof(live::VERMAXION.Services.ConfigManager).GetField("accounts", fields)!.SetValue(manager,
            new Dictionary<string, live::VERMAXION.Models.AccountConfig>
            { [manager.CurrentAccountId] = new() { AccountId = manager.CurrentAccountId, DefaultConfig = profile } });
        var client = DispatchProxy.Create<host::Dalamud.Plugin.Services.IClientState, BeastmasterRouteLifecycleTests.HostProxy>();
        ((BeastmasterRouteLifecycleTests.HostProxy)(object)client).Handler = (method, _) =>
            method.Name == "get_TerritoryType" ? (nativeRace ? 389u : 144u) : null;
        var condition = DispatchProxy.Create<host::Dalamud.Plugin.Services.ICondition, BeastmasterRouteLifecycleTests.HostProxy>();
        ((BeastmasterRouteLifecycleTests.HostProxy)(object)condition).Handler = (method, args) =>
            method.Name == "get_Item" && nativeRace &&
            (host::Dalamud.Game.ClientState.Conditions.ConditionFlag)args![0]! == host::Dalamud.Game.ClientState.Conditions.ConditionFlag.BoundByDuty;
        var race = (live::VERMAXION.Services.ChocoboRaceService)RuntimeHelpers.GetUninitializedObject(typeof(live::VERMAXION.Services.ChocoboRaceService));
        typeof(live::VERMAXION.Services.ChocoboRaceService).GetField("configManager", fields)!.SetValue(race, manager);
        typeof(live::VERMAXION.Services.ChocoboRaceService).GetField("condition", fields)!.SetValue(race, condition);
        var engine = (live::VERMAXION.Services.VermaxionEngine)RuntimeHelpers.GetUninitializedObject(typeof(live::VERMAXION.Services.VermaxionEngine));
        typeof(live::VERMAXION.Services.VermaxionEngine).GetField("configuration", fields)!.SetValue(engine,
            new live::VERMAXION.Configuration { MinimumTeleportGil = 10000 });
        typeof(live::VERMAXION.Services.VermaxionEngine).GetField("clientState", fields)!.SetValue(engine, client);
        typeof(live::VERMAXION.Services.VermaxionEngine).GetField("chocoboRaceService", fields)!.SetValue(engine, race);
        var clientProperty = typeof(live::VERMAXION.Plugin).GetProperty("ClientState", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previousClient = clientProperty.GetValue(null);
        try
        {
            clientProperty.SetValue(null, client);
            Assert.Null(live::VERMAXION.Services.GameHelpers.ReadGilBalance()); // No native inventory in this offline host.
            var eligibility = (live::VERMAXION.Models.TaskEligibility)typeof(live::VERMAXION.Services.VermaxionEngine)
                .GetMethod("ApplyTeleportGilFloor", fields)!.Invoke(engine,
                    [AutomationCatalog.ChocoboRacing, profile, live::VERMAXION.Models.TaskEligibility.Runnable()])!;
            Assert.Equal(runnable, eligibility.IsRunnable);
        }
        finally { clientProperty.SetValue(null, previousClient); }
    }

    [Fact]
    public void ProductionTeleportGilReasonsLocalizeInEveryLanguage()
    {
        const string unknown = "Gil balance is unavailable; teleport-dependent work is skipped.";
        const string key = "Teleport-dependent work needs at least {0} gil; current balance is {1}.";
        var assembly = typeof(live::VERMAXION.Plugin).Assembly;
        var textType = assembly.GetType("VERMAXION.UiText", true)!;
        var constructor = textType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Single();
        var fontType = constructor.GetParameters()[1].ParameterType;
        var font = typeof(FavoriteAndListingPacingTests).GetMethod(nameof(NoFont), BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(fontType.GenericTypeArguments[0]).CreateDelegate(fontType);
        foreach (var language in new[] { "en", "de", "es", "fr", "hi", "id", "it", "ja", "ko", "pl", "pt-BR", "ru", "tr", "vi", "zh-Hans" })
        {
            using var text = (IDisposable)constructor.Invoke([language, font]);
            using var frame = (IDisposable)textType.GetMethod("Enter", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(text, null)!;
            var manager = new ResourceManager("VERMAXION.Localization.Strings_" + language.Replace('-', '_'), assembly);
            var resources = manager.GetResourceSet(CultureInfo.InvariantCulture, true, false)!;
            var translate = textType.GetMethod("T", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
            Assert.False(string.IsNullOrWhiteSpace(resources.GetString("Minimum gil for teleport tasks", false)));
            Assert.False(string.IsNullOrWhiteSpace(resources.GetString("Skip work that needs teleport travel below this balance. Local tasks and free travel remain available. Default: 10,000 gil. Set 0 to disable. Purchase reserves still apply.", false)));
            Assert.False(string.IsNullOrWhiteSpace(resources.GetString(unknown, false)));
            Assert.False(string.IsNullOrWhiteSpace(resources.GetString(key, false)));
            Assert.Equal(resources.GetString(unknown, false), translate.Invoke(null, [unknown]));
            Assert.Equal(string.Format(CultureInfo.GetCultureInfo(language), resources.GetString(key, false)!, "10,000", "9,999"),
                translate.Invoke(null, ["Teleport-dependent work needs at least 10,000 gil; current balance is 9,999."]));
            manager.ReleaseAllResources();
        }
    }

    private static IDisposable NoFont<T>(T _) => new EmptyFont();
    private sealed class EmptyFont : IDisposable { public void Dispose() { } }

    [Fact]
    public void TeleportGilFloorDefaultsClampsAndRoundTripsWithoutChangingPurchaseReserves()
    {
        var config = new Configuration();
        Assert.Equal(10000, config.MinimumTeleportGil);
        config.MinimumTeleportGil = -1;
        Assert.Equal(0, config.MinimumTeleportGil);
        config.MinimumTeleportGil = 25000;
        var restored = Newtonsoft.Json.JsonConvert.DeserializeObject<Configuration>(
            Newtonsoft.Json.JsonConvert.SerializeObject(config))!;
        Assert.Equal(25000, restored.MinimumTeleportGil);
        Assert.Equal(10000, Newtonsoft.Json.JsonConvert.DeserializeObject<Configuration>("{}")!.MinimumTeleportGil);
        var character = new CharacterConfig { VerminionGilReserve = 5000, ChocoboGilReserve = 75000, FCBuffMinGil = 20000 };
        var clone = character.Clone();
        Assert.Equal(5000u, clone.VerminionGilReserve);
        Assert.Equal(75000u, clone.ChocoboGilReserve);
        Assert.Equal(20000, clone.FCBuffMinGil);
    }

    [Theory]
    [InlineData(9999L, 10000, true, true)]
    [InlineData(10000L, 10000, true, false)]
    [InlineData(10001L, 10000, true, false)]
    [InlineData(0L, 0, true, false)]
    [InlineData(0L, 10000, false, false)]
    [InlineData(null, 10000, true, true)]
    [InlineData(null, 10000, false, false)]
    [InlineData(null, 0, true, false)]
    public void TeleportGilFloorChecksTravelOnlyAndIncludesTheExactFloor(long? gil, int floor, bool needsTeleport, bool blocked)
        => Assert.Equal(blocked, TeleportGilPolicy.GetBlockedReason(gil, floor, needsTeleport) != null);

    [Theory]
    [InlineData(TaskEligibilityStatus.Disabled)]
    [InlineData(TaskEligibilityStatus.NotDue)]
    [InlineData(TaskEligibilityStatus.Blocked)]
    [InlineData(TaskEligibilityStatus.Unsupported)]
    public void TeleportGilFloorPreservesExistingEligibilityReasons(TaskEligibilityStatus status)
    {
        var previous = new TaskEligibility(status, "existing reason");
        Assert.Equal(previous, TeleportGilPolicy.Apply(previous, 0, 10000, true));
    }

    [Theory]
    [InlineData(AutomationCatalog.MiniCactpot, 144u, false)]
    [InlineData(AutomationCatalog.JumboCactpot, 144u, false)]
    [InlineData(AutomationCatalog.FashionReport, 129u, true)]
    [InlineData(AutomationCatalog.VerminionQueue, 388u, false)]
    [InlineData(AutomationCatalog.VerminionQueue, 144u, false)]
    [InlineData(AutomationCatalog.VerminionQueue, 129u, true)]
    [InlineData(AutomationCatalog.Beastmaster, 148u, false)]
    [InlineData(AutomationCatalog.BeastmasterWeekly, 129u, true)]
    [InlineData(AutomationCatalog.RegisterRegistrables, 129u, false)]
    [InlineData(AutomationCatalog.CurrentJobEquipment, 129u, false)]
    [InlineData(AutomationCatalog.RetainerEquipping, 129u, false)]
    [InlineData(AutomationCatalog.MinionRoulette, 129u, false)]
    public void TeleportTaskAdmissionPreservesKnownLocalRoutes(string task, uint territory, bool needsTeleport)
        => Assert.Equal(needsTeleport, TeleportGilPolicy.TaskNeedsTeleport(task, territory));

    [Theory]
    [InlineData("/li saucer", 144u, false)]
    [InlineData("/li Cactpot", 144u, false)]
    [InlineData("/li limsa", 129u, false)]
    [InlineData("saucer", 129u, true)]
    [InlineData("/li home", 129u, true)]
    [InlineData("/li stop", 129u, false)]
    [InlineData("abort", 129u, false)]
    [InlineData("/return", 129u, false)]
    [InlineData("/echo ready", 129u, false)]
    [InlineData("/myhome", 129u, true)]
    [InlineData("/tp Limsa Lominsa", 148u, true)]
    public void TravelGatePreservesFreeRoutesAndNonTravelCommands(string command, uint territory, bool needsTeleport)
        => Assert.Equal(needsTeleport, TeleportGilPolicy.CommandNeedsTeleport(command, territory));

    [Fact]
    public void DispatchRevalidationSkipsTravelAfterBalanceFallsAndKeepsLocalWork()
    {
        var order = new[] { AutomationCatalog.VerminionQueue, AutomationCatalog.CurrentJobEquipment };
        Dictionary<string, TaskEligibility> ReadEligibility(long gil) => order.ToDictionary(id => id,
            id => TeleportGilPolicy.Apply(TaskEligibility.Runnable(), gil, 10000,
                TeleportGilPolicy.TaskNeedsTeleport(id, 129)));
        Assert.Equal(order, AutomationDispatchPlanner.BuildRunnableQueue(order, ReadEligibility(10000)));
        Assert.Equal(new[] { AutomationCatalog.CurrentJobEquipment },
            AutomationDispatchPlanner.BuildRunnableQueue(order, ReadEligibility(9999)));
    }

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
