extern alias live;
extern alias host;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Gui = host::FFXIVClientStructs.FFXIV.Component.GUI;
using NativeUi = host::FFXIVClientStructs.FFXIV.Client.UI;
using Newtonsoft.Json.Linq;
using Xunit;

namespace VERMAXION.Tests;

// This collection changes only synthetic, process-local host services and restores
// them afterwards. It must not overlap other tests that replace host services.
[CollectionDefinition("Beastmaster production host", DisableParallelization = true)]
public sealed class BeastmasterProductionHostCollection { }

[Collection("Beastmaster production host")]
public sealed class BeastmasterRouteLifecycleTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Assembly Production = typeof(live::VERMAXION.Plugin).Assembly;
    private static readonly Type ServiceType = Production.GetType("VERMAXION.Beastmaster.BeastmasterService", true)!;
    private static readonly Type RouteType = Production.GetType("VERMAXION.Beastmaster.BeastmasterRoute", true)!;

    [Fact]
    public void StationaryRunResumesAtThirtySecondsAndSpacesFurtherAttempts()
    {
        using var host = new HostFixture();
        var service = CreateStationaryRun(host);
        var now = DateTime.UtcNow;

        Assert.False(ShouldResume(now));
        Assert.False(ShouldResume(now.AddMilliseconds(29_999)));
        Assert.True(ShouldResume(now.AddSeconds(30)));
        Assert.False(ShouldResume(now.AddMilliseconds(59_999)));
        Assert.True(ShouldResume(now.AddSeconds(60)));

        bool ShouldResume(DateTime at) => (bool)Invoke(service, "ShouldResumeStationaryRun", at)!;
    }

    [Theory]
    [InlineData(1f, 0f, 0f)]
    [InlineData(0f, 1f, 0f)]
    [InlineData(0f, 0f, 1f)]
    public void MovementOnAnyAxisStartsANewThirtySecondInterval(float x, float y, float z)
    {
        using var host = new HostFixture();
        var service = CreateStationaryRun(host);
        var now = DateTime.UtcNow;
        Assert.False(ShouldResume(now));
        host.Position += new Vector3(x, y, z);
        Assert.False(ShouldResume(now.AddSeconds(29)));
        Assert.False(ShouldResume(now.AddMilliseconds(58_999)));
        Assert.True(ShouldResume(now.AddSeconds(59)));

        bool ShouldResume(DateTime at) => (bool)Invoke(service, "ShouldResumeStationaryRun", at)!;
    }

    [Theory]
    [InlineData("combat")]
    [InlineData("paused")]
    [InlineData("stopped")]
    [InlineData("outside Crucible")]
    [InlineData("different board")]
    [InlineData("outside duty")]
    [InlineData("dead")]
    [InlineData("zoning")]
    [InlineData("logged out")]
    [InlineData("unloaded character")]
    [InlineData("different character")]
    [InlineData("no owner")]
    [InlineData("finished")]
    [InlineData("result seen")]
    [InlineData("result observed")]
    public void IneligibleRunDiscardsStationaryTimeInsteadOfResuming(string reason)
    {
        using var host = new HostFixture();
        var service = CreateStationaryRun(host);
        var now = DateTime.UtcNow;
        Assert.False(ShouldResume(now));
        switch (reason)
        {
            case "combat": host.Conditions.Add("InCombat"); break;
            case "paused": Set(service, "IsPaused", true); break;
            case "stopped": Set(service, "operatorStopped", true); break;
            case "outside Crucible": host.Territory = 1; break;
            case "different board": host.Territory = 1343; break;
            case "outside duty": host.Conditions.Remove("BoundByDuty"); break;
            case "dead": host.PlayerDead = true; break;
            case "zoning": host.Conditions.Add("BetweenAreas"); break;
            case "logged out": host.LoggedIn = false; break;
            case "unloaded character": host.PlayerLoaded = false; break;
            case "different character": host.Character = 18; break;
            case "no owner": Set(service, "owner", 0UL); break;
            case "finished": Set(service, "State", Enum.Parse(ServiceType.GetNestedType("RunState", BindingFlags.NonPublic)!, "Complete")); break;
            case "result seen": Set(service, "resultSeen", true); break;
            case "result observed": Set(service, "ResultObserved", true); break;
        }
        Assert.False(ShouldResume(now.AddMinutes(1)));
        Assert.Equal(default, Read<DateTime>(service, "stationarySince"));

        host.Conditions.Clear(); host.Conditions.Add("BoundByDuty");
        host.Territory = 1342; host.PlayerDead = false; host.LoggedIn = host.PlayerLoaded = true; host.Character = 17;
        Set(service, "IsPaused", false); Set(service, "operatorStopped", false); Set(service, "owner", 17UL);
        Set(service, "State", Enum.Parse(ServiceType.GetNestedType("RunState", BindingFlags.NonPublic)!, "Running"));
        Set(service, "resultSeen", false); Set(service, "ResultObserved", false);
        Assert.False(ShouldResume(now.AddMinutes(1)));
        Assert.False(ShouldResume(now.AddMilliseconds(89_999)));
        Assert.True(ShouldResume(now.AddSeconds(90)));

        bool ShouldResume(DateTime at) => (bool)Invoke(service, "ShouldResumeStationaryRun", at)!;
    }

    [Theory]
    [InlineData("logged out")]
    [InlineData("unloaded character")]
    [InlineData("different character")]
    [InlineData("no owner")]
    public void UpdateIdentityWaitDiscardsAnAlreadyAgedStationaryInterval(string reason)
    {
        using var host = new HostFixture();
        var service = CreateStationaryRun(host);
        Set(service, "stationarySince", DateTime.UtcNow.AddMinutes(-1));
        if (reason == "logged out") host.LoggedIn = false;
        if (reason == "unloaded character") host.PlayerLoaded = false;
        if (reason == "different character") host.Character = 18;
        if (reason == "no owner") Set(service, "owner", 0UL);
        Invoke(service, "Update");
        Assert.Equal(default, Read<DateTime>(service, "stationarySince"));
        Assert.Empty(host.Warnings);
    }

    [Fact]
    public void StationaryUpdateInvokesExistingResumeAndRetainsRunAndPendingMenus()
    {
        using var host = new HostFixture();
        var service = CreateStationaryRun(host);
        var settings = Field(service, "runSettings");
        var menus = Field(service, "menus")!;
        Set(menus, "confirmationWindow", "XBMContentsTreasure");
        Set(menus, "confirmationAddress", (nint)123);
        var readRecovery = Field(service, "registrationReadRecovery")!;
        Invoke(readRecovery, "Ready", "SyntheticTreasure", (nint)1, DateTime.UtcNow);
        Set(service, "CurrentRun", 7);
        Set(service, "completionCommandPending", true);
        Set(service, "StopAfterNextOutcome", true);
        Set(service, "stationaryPosition", host.Position);
        Set(service, "stationarySince", DateTime.UtcNow.AddSeconds(-31));

        Invoke(service, "Update");

        Assert.Single(host.Warnings, message => message.Contains("Stationary for 30 seconds", StringComparison.Ordinal));
        // RestartFromCurrent clears this registration observation before its
        // native BST check; the fixture intentionally has no native job sheet.
        Assert.Null(Field(readRecovery, "window"));
        Assert.Equal(7, Read<int>(service, "CurrentRun"));
        Assert.Same(settings, Field(service, "runSettings"));
        Assert.True(Read<bool>(service, "completionCommandPending"));
        Assert.True(Read<bool>(service, "StopAfterNextOutcome"));
        Assert.False(Read<bool>(service, "resultSeen"));
        Assert.False(Read<bool>(service, "ResultObserved"));
        Assert.False(Read<bool>(service, "BoardRunCompleted"));
        Assert.Same(menus, Field(service, "menus"));
        Assert.Equal("XBMContentsTreasure", Field(menus, "confirmationWindow"));
        Assert.Equal((nint)123, Field(menus, "confirmationAddress"));
    }

    private static object CreateStationaryRun(HostFixture host)
    {
        host.Territory = 1342; host.Position = new(-695, 0, -16);
        host.Conditions.Add("BoundByDuty");
        var service = host.CreateService();
        Set(service, "runSettings", new live::VERMAXION.Beastmaster.BeastmasterSettings { Board = 4 });
        Set(service, "owner", 17UL); Set(service, "operatorStopped", false);
        Set(service, "State", Enum.Parse(ServiceType.GetNestedType("RunState", BindingFlags.NonPublic)!, "Running"));
        return service;
    }

    [Fact]
    public void CancelledEntranceMoveStaysPendingAndReissuesUntilActualArrival()
    {
        using var host = new HostFixture();
        var service = host.CreateService();
        var route = CreateRoute(service, 1339);
        var destination = RoutePosition(route, "MoveTo", 0);
        host.Position = destination + new Vector3(0, 0, 8);

        Invoke(route, "Update"); // The bundled route's initial WaitFor(IsReady).
        Assert.Equal(1, Read<int>(route, "Step"));
        Invoke(route, "Update");
        Assert.Single(host.MovementCommands);
        AgeMovement(route);
        // The command was accepted but the intro cancelled it before any movement.
        Invoke(route, "Update");
        Assert.Equal(1, Read<int>(route, "Step"));
        Assert.False(Read<bool>(route, "movementStarted"));
        Invoke(route, "Update");
        Assert.Equal(1, Read<int>(route, "Step"));
        Assert.Equal(2, host.MovementCommands.Count);

        host.Position = destination;
        AgeMovement(route);
        Invoke(route, "Update");
        Assert.Equal(2, Read<int>(route, "Step"));
        Assert.False(Read<bool>(service, "ownsNavigation"));
    }

    [Theory]
    [InlineData("OccupiedInCutSceneEvent")]
    [InlineData("BetweenAreas")]
    [InlineData("SufferingStatusAffliction2")]
    [InlineData("SufferingStatusAffliction63")]
    public void IntroOrLoadingRetainsTheMoveAndStopsAnInterruptedOwnedPath(string condition)
    {
        using var host = new HostFixture();
        var service = host.CreateService();
        var route = CreateRoute(service, 1339);
        var destination = RoutePosition(route, "MoveTo", 0);
        host.Position = destination + new Vector3(0, 0, 8);
        host.Conditions.Add(condition);

        Assert.False(Approach(route, destination));
        Assert.Empty(host.MovementCommands);
        host.Conditions.Clear();
        Assert.False(Approach(route, destination));
        Assert.Single(host.MovementCommands);
        host.Conditions.Add(condition);
        AgeMovement(route);
        Assert.False(Approach(route, destination));
        Assert.False(Read<bool>(route, "movementStarted"));
        Assert.Contains("/vnav stop", host.Commands);

        host.Conditions.Clear();
        Assert.False(Approach(route, destination));
        Assert.Equal(2, host.MovementCommands.Count);
    }

    [Fact]
    public void LaterPositionedTileWaitsForPathfindingAndForActualHorizontalAndVerticalArrival()
    {
        using var host = new HostFixture();
        var service = host.CreateService();
        var route = CreateRoute(service, 1340);
        var destination = RoutePosition(route, "Wait", 0);
        host.Position = destination + new Vector3(0, 0, 9);
        Assert.False(Approach(route, destination));
        AgeMovement(route);
        host.Pathfinding = true;
        Assert.False(Approach(route, destination));
        Assert.True(Read<bool>(route, "movementStarted"));
        Assert.DoesNotContain("/vnav stop", host.Commands);

        host.Pathfinding = false;
        Assert.False(Approach(route, destination));
        Assert.False(Approach(route, destination));
        host.Position = destination + new Vector3(0, 2, 0);
        AgeMovement(route);
        Assert.False(Approach(route, destination));
        Assert.False(Approach(route, destination));
        host.Position = destination;
        AgeMovement(route);
        Assert.True(Approach(route, destination));
    }

    [Fact]
    public void ArenaTransitionTakesOverAnUnfinishedWaypoint()
    {
        using var host = new HostFixture();
        var service = host.CreateService();
        var route = CreateRoute(service, 1339);
        var destination = RoutePosition(route, "MoveTo", 0);
        host.Position = destination + new Vector3(0, 0, 8);
        Assert.False(Approach(route, destination));
        // The arena is separated from every board waypoint. ObserveArena must
        // replace the approach before a proximity check can hold the old tile.
        host.Position = new Vector3(1000, 0, 1000);
        Invoke(route, "ObserveArena");
        Assert.True(Read<bool>(route, "recoveringPosition"));
        Assert.True(Read<bool>(route, "recoveringArena"));
        Assert.False(Read<bool>(route, "movementStarted"));
        Assert.True(Read<bool>(route, "AllowsCombat"));
    }

    [Fact]
    public void PauseStopsOwnedMovementWithoutAdvancingThePendingWaypoint()
    {
        using var host = new HostFixture();
        var service = host.CreateService();
        var route = CreateRoute(service, 1339);
        Set(service, "route", route);
        Set(service, "State", Enum.Parse(ServiceType.GetNestedType("RunState", BindingFlags.NonPublic)!, "Running"));
        var destination = RoutePosition(route, "MoveTo", 0);
        host.Position = destination + new Vector3(0, 0, 8);
        Assert.False(Approach(route, destination));

        Invoke(service, "Pause");
        Assert.True(Read<bool>(service, "IsPaused"));
        Assert.False(Read<bool>(service, "ownsNavigation"));
        Assert.False(Read<bool>(route, "movementStarted"));
        Assert.Equal(0, Read<int>(route, "Step"));
        Assert.Contains("/vnav stop", host.Commands);
    }

    [Fact]
    public void CompletedBatchRestartsItsPlanAndReusesMenusAndNavigationLikeAFreshInstance()
    {
        using var host = new HostFixture();
        var reused = host.CreateService();
        var menus = Field(reused, "menus")!;
        var navigation = Field(reused, "navigation");
        Set(reused, "runSettings", new live::VERMAXION.Beastmaster.BeastmasterSettings { Board = 2, RunCount = 40 });
        Set(reused, "CurrentRun", 40);
        Set(reused, "State", Enum.Parse(ServiceType.GetNestedType("RunState", BindingFlags.NonPublic)!, "Complete"));
        Set(reused, "resultSeen", true);
        Set(reused, "ResultObserved", true);
        Set(reused, "BoardRunCompleted", true);
        Set(reused, "resultRanksObserved", true);
        Set(reused, "resultRanksUnavailable", true);
        Set(reused, "resultReadStarted", DateTime.UtcNow.AddMinutes(-1));
        Set(reused, "completionCommandPending", true);
        Set(reused, "StopAfterNextOutcome", true);
        Set(menus, "resultsRecorded", true);
        Set(menus, "resultExitPending", true);
        Set(menus, "confirmationWindow", "XBMResult");

        var next = new live::VERMAXION.Beastmaster.BeastmasterSettings { Board = 1, RunCount = 30 };
        // Deliberately stop at the native player boundary after real fresh-plan
        // initialization. This does not claim offline duty registration succeeds.
        host.NativePlayerAvailable = false;
        Invoke(reused, "RestartFromCurrent", next, true, false);
        AssertNewPlan(reused);
        Assert.Same(menus, Field(reused, "menus"));
        Assert.Same(navigation, Field(reused, "navigation"));

        var fresh = host.CreateService(separateNavigation: true);
        Invoke(fresh, "RestartFromCurrent", next, true, false);
        AssertNewPlan(fresh);
        Assert.NotSame(navigation, Field(fresh, "navigation"));

        // BeginRoute resets the existing menus before reaching combat-provider
        // validation. The offline host deliberately has no combat plugins loaded.
        host.Territory = 1339;
        Invoke(reused, "BeginRoute", false, false);
        Assert.False(Read<bool>(menus, "resultsRecorded"));
        Assert.False(Read<bool>(menus, "resultExitPending"));
        Assert.Null(Field(menus, "confirmationWindow"));
        Assert.Same(menus, Field(reused, "menus"));
        Assert.Same(navigation, Field(reused, "navigation"));

        host.NativePlayerAvailable = true;
        foreach (var service in new[] { reused, fresh })
        {
            var route = CreateRoute(service, 1339);
            var first = RoutePosition(route, "MoveTo", 0);
            host.Position = first + new Vector3(0, 0, 8);
            Assert.False(Approach(route, first));
            AgeMovement(route);
            Assert.False(Approach(route, first));
            Assert.False(Approach(route, first));
            host.Position = first;
            AgeMovement(route);
            Assert.True(Approach(route, first));
        }
    }

    [Fact]
    public void MinervaReadinessSkipsTheBossModConflictPreflight()
    {
        using var host = new HostFixture();
        var conflictingBossMod = DispatchProxy.Create<host::Dalamud.Plugin.IExposedPlugin, HostProxy>();
        ((HostProxy)(object)conflictingBossMod).Handler = (method, _) => method.Name switch
        {
            "get_IsLoaded" => true,
            "get_InternalName" => "BossMod",
            _ => throw new InvalidOperationException("Unexpected plugin inventory read: " + method.Name),
        };
        host.InstalledPlugins = [conflictingBossMod];
        var readiness = ServiceType.GetMethod("CanStartCombat", Static)!;
        object?[] minerva = [new live::VERMAXION.Beastmaster.BeastmasterSettings
            { CombatProvider = live::VERMAXION.Beastmaster.BeastmasterCombatProvider.Minerva }, null];

        // The offline host has no RSR runtime. Minerva reaches that boundary
        // without applying BMR's earlier rejection of a loaded BossMod plugin.
        Assert.False((bool)readiness.Invoke(null, minerva)!);
        Assert.Equal("RotationSolver is not loaded.", minerva[1]);

        object?[] bmr = [new live::VERMAXION.Beastmaster.BeastmasterSettings(), null];
        Assert.False((bool)readiness.Invoke(null, bmr)!);
        Assert.Contains("Use BossModReborn alone", (string)bmr[1]!);
        Assert.Empty(host.Commands);
    }

    [Fact]
    public void SamePlanRestartRetainsCapturedMinervaWhenTheSavedProviderChanges()
    {
        using var host = new HostFixture();
        var service = host.CreateService();
        host.NativePlayerAvailable = false;
        var selected = new live::VERMAXION.Beastmaster.BeastmasterSettings
        {
            Board = 4, RunCount = 7,
            CombatProvider = live::VERMAXION.Beastmaster.BeastmasterCombatProvider.Minerva,
        };
        // Run the real fresh-plan initialization, stopping at the fixture's
        // existing native-player boundary before any offline duty registration.
        Assert.True((bool)Invoke(service, "RestartFromCurrent", selected, true, false)!);
        var captured = Field(service, "combat");
        var runSettings = Field(service, "runSettings");
        Assert.Equal("BeastmasterMinervaCombat", captured!.GetType().Name);
        Set(service, "State", Enum.Parse(ServiceType.GetNestedType("RunState", BindingFlags.NonPublic)!, "Running"));
        Set(service, "CurrentRun", 3);

        var changed = new live::VERMAXION.Beastmaster.BeastmasterSettings
        {
            Board = 1, RunCount = 1,
            CombatProvider = live::VERMAXION.Beastmaster.BeastmasterCombatProvider.BossModReborn,
        };
        Assert.True((bool)Invoke(service, "RestartFromCurrent", changed, true, false)!);

        Assert.Same(captured, Field(service, "combat"));
        Assert.Equal(live::VERMAXION.Beastmaster.BeastmasterCombatProvider.Minerva,
            Read<live::VERMAXION.Beastmaster.BeastmasterCombatProvider>(service, "combatProvider"));
        Assert.Same(runSettings, Field(service, "runSettings"));
        Assert.Equal(3, Read<int>(service, "CurrentRun"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FreshRestartCannotReplaceAnUnresolvedProviderOrItsRunOwnership(bool changedCharacter)
    {
        using var host = new HostFixture();
        var service = CreateStationaryRun(host);
        var captured = Field(service, "combat")!;
        var runSettings = Field(service, "runSettings");
        var originalType = captured.GetType().GetField("original", Instance)!.FieldType.GenericTypeArguments.Single();
        // Represent the existing lease after its provider was unloaded. The real
        // release path must fail against this fixture's empty plugin inventory.
        Set(captured, "original", Activator.CreateInstance(originalType));
        Set(captured, "rotationChanged", true);
        Assert.True(Read<bool>(captured, "OwnsState"));
        Set(service, "CurrentRun", 7);
        Set(service, "resultSeen", true);
        if (changedCharacter) host.Character = 18;
        else Set(service, "State", Enum.Parse(ServiceType.GetNestedType("RunState", BindingFlags.NonPublic)!, "Complete"));
        var priorState = Field(service, "State");
        var selected = new live::VERMAXION.Beastmaster.BeastmasterSettings
            { CombatProvider = live::VERMAXION.Beastmaster.BeastmasterCombatProvider.Minerva };

        Assert.False((bool)Invoke(service, "RestartFromCurrent", selected, true, false)!);

        Assert.Same(captured, Field(service, "combat"));
        Assert.True(Read<bool>(captured, "OwnsState"));
        Assert.Equal(live::VERMAXION.Beastmaster.BeastmasterCombatProvider.BossModReborn,
            Read<live::VERMAXION.Beastmaster.BeastmasterCombatProvider>(service, "combatProvider"));
        Assert.Same(runSettings, Field(service, "runSettings"));
        Assert.Equal(17UL, Read<ulong>(service, "owner"));
        Assert.Equal(priorState, Field(service, "State"));
        Assert.Equal(7, Read<int>(service, "CurrentRun"));
        Assert.True(Read<bool>(service, "resultSeen"));
        Assert.StartsWith("Combat restoration pending: ", Read<string>(service, "status"));
        Assert.Empty(host.Commands);
    }

    [Fact]
    public void SecondMasterFirstFightReturnResumesThroughItsBranchAndDispatchesTheNextTile()
    {
        using var host = new HostFixture { Territory = 1343, Position = new(-700, 0, -8) };
        var service = host.CreateService();
        // Resume on the board after the native first-fight return. Combat and
        // native tile menus are outside this offline fixture's boundary.
        var route = CreateRoute(service, 1343, resume: true);
        Invoke(route, "Update");
        Assert.Equal(6, Read<int>(route, "Step"));
        Set(route, "stepSince", DateTime.UtcNow.AddSeconds(-1));
        Invoke(route, "Update");
        Assert.Equal(7, Read<int>(route, "Step"));
        Invoke(route, "Update"); // The post-fight distance check has settled.
        Assert.Equal(8, Read<int>(route, "Step"));
        Invoke(route, "Update");
        Assert.Equal(26, Read<int>(route, "Step"));
        Invoke(route, "Update");
        Assert.Equal(26, Read<int>(route, "Step"));
        Assert.Equal("/vnav moveto -692.00 0.00 -9.00", Assert.Single(host.MovementCommands));

        host.Position = new(-692, 0, -9);
        AgeMovement(route);
        Invoke(route, "Update");
        Assert.True(Read<bool>(route, "positioned"));
        Assert.Equal(26, Read<int>(route, "Step"));
        Set(route, "stepSince", DateTime.UtcNow.AddSeconds(-2));
        Invoke(route, "Update");
        Assert.Equal(27, Read<int>(route, "Step"));
        Invoke(route, "Update");
        Set(route, "stepSince", DateTime.UtcNow.AddSeconds(-1));
        Invoke(route, "Update");
        Assert.Equal(28, Read<int>(route, "Step"));
        Invoke(route, "Update");
        Assert.Equal(29, Read<int>(route, "Step"));
        Invoke(route, "Update");
        Assert.Equal(30, Read<int>(route, "Step"));
        Invoke(route, "Update");
        Assert.Equal("/vnav moveto -691.00 0.00 -17.00", host.MovementCommands.Last());
        Assert.Equal(2, host.MovementCommands.Count);
    }

    [Theory]
    [InlineData(8, 26, 26, -692f, -9f)]
    [InlineData(21, 25, 50, -700f, -35f)]
    [InlineData(25, 50, 50, -700f, -35f)]
    [InlineData(29, 30, 30, -691f, -17f)]
    [InlineData(33, 37, 37, -691f, -26f)]
    [InlineData(40, 50, 50, -700f, -35f)]
    public void SecondMasterForwardBranchesReachTheirActualPositionedDestination(
        int source, int destination, int positionedStep, float x, float z)
    {
        using var host = new HostFixture { Territory = 1343, Position = new(-700, 0, -8) };
        var route = CreateRoute(host.CreateService(), 1343);
        Set(route, "Step", source);
        // Leave started=false: the two conditional jumps must pass their real,
        // unchanged distance predicates before the production jump can execute.
        Invoke(route, "Update");
        Assert.Equal(destination, Read<int>(route, "Step"));
        if (destination == 25)
        {
            Invoke(route, "Update");
            Assert.Equal(50, Read<int>(route, "Step"));
        }
        Invoke(route, "Update");
        Assert.Equal(positionedStep, Read<int>(route, "Step"));
        var command = Assert.Single(host.MovementCommands).Split(' ');
        Assert.Equal("/vnav", command[0]);
        Assert.Equal("moveto", command[1]);
        Assert.Equal(new Vector3(x, 0, z), new Vector3(
            float.Parse(command[2], CultureInfo.InvariantCulture),
            float.Parse(command[3], CultureInfo.InvariantCulture),
            float.Parse(command[4], CultureInfo.InvariantCulture)));
    }

    [Theory]
    [InlineData(21)]
    [InlineData(33)]
    public void SecondMasterConditionalForwardBranchesRetainTheirDistancePredicates(int source)
    {
        using var host = new HostFixture { Territory = 1343, Position = new(-700, 0, -8) };
        var route = CreateRoute(host.CreateService(), 1343);
        var action = (JObject)((JArray)Field(route, "actions")!)[source]!;
        var condition = Assert.IsType<JObject>(Assert.Single((JArray)action["Conditions"]!));
        var evaluate = RouteType.GetMethod("Condition", Static)!;
        Assert.True((bool)evaluate.Invoke(null, [condition])!);

        host.Position = new(120, 0, -420);
        Assert.False((bool)evaluate.Invoke(null, [condition])!);
        // Full Update at this position intentionally belongs to ObserveArena,
        // which precedes route conditions and needs a native combat provider.
    }

    [Fact]
    public void SecondMasterBackwardLoadingLoopStaysPendingThenAdvancesWhenTheConditionClears()
    {
        using var host = new HostFixture { Territory = 1343, Position = new(-700, 0, -8) };
        var route = CreateRoute(host.CreateService(), 1343);
        Set(route, "Step", 1);
        host.Conditions.Add("SufferingStatusAffliction2");
        Invoke(route, "Update");
        Assert.Equal(1, Read<int>(route, "Step"));
        host.Conditions.Clear();
        Invoke(route, "Update");
        Assert.Equal(2, Read<int>(route, "Step"));
        Assert.Empty(host.MovementCommands);
    }

    [Theory]
    [InlineData(8, 11)]
    [InlineData(12, 15)]
    public void FirstMasterConditionalForwardJumpsKeepTheirExistingDestinations(int source, int destination)
    {
        using var host = new HostFixture { Territory = 1342, Position = new(-700, 0, -16) };
        host.AddBoardObject(2015483, new(-695, 0, -16.5f));
        var route = CreateRoute(host.CreateService(), 1342);
        Set(route, "Step", source);
        Invoke(route, "Update");
        Assert.Equal(destination, Read<int>(route, "Step"));
        Assert.Empty(host.MovementCommands);
    }

    [Theory]
    [InlineData(1339u)]
    [InlineData(1340u)]
    [InlineData(1341u)]
    [InlineData(1342u)]
    [InlineData(1343u)]
    public void ExistingBackwardJumpsAndOtherBoardsForwardDestinationsStayUnchanged(uint territory)
    {
        using var host = new HostFixture { Territory = territory, Position = new(-700, 0, -8) };
        var route = CreateRoute(host.CreateService(), territory);
        var actions = (JArray)Field(route, "actions")!;
        var checkedJumps = 0;
        for (var source = 0; source < actions.Count; source++)
        {
            if (actions[source]!.Value<string>("Name") != "ModifyIndex") continue;
            var argument = actions[source]!["Arguments"]!.First!.Value<string>();
            if (territory == 1343 && argument!.StartsWith('+')) continue;
            Set(route, "Step", source);
            // This tests an already-admitted jump. Its native encounter/distance
            // condition is deliberately outside this preservation assertion.
            Set(route, "started", true);
            Invoke(route, "Update");
            var expected = (territory, source) switch
            {
                (1342, 8) => 11,
                (1342, 12) => 15,
                _ => source,
            };
            if (territory != 1342 || source is not (8 or 12)) Assert.Equal("-1", argument);
            Assert.Equal(expected, Read<int>(route, "Step"));
            checkedJumps++;
        }
        Assert.True(checkedJumps > 0);
        Assert.Empty(host.MovementCommands);
    }

    [Theory]
    [InlineData(1339u)]
    [InlineData(1340u)]
    [InlineData(1341u)]
    [InlineData(1342u)]
    [InlineData(1343u)]
    public void AbsoluteJumpRetainsItsExistingDestinationOnEveryBoard(uint territory)
    {
        using var host = new HostFixture { Territory = territory, Position = new(-700, 0, -8) };
        var route = CreateRoute(host.CreateService(), territory);
        var actions = (JArray)Field(route, "actions")!;
        var source = actions.Select((action, index) => (action, index))
            .First(entry => entry.action!.Value<string>("Name") == "ModifyIndex").index;
        // No bundled route currently uses an absolute argument. Change only this
        // test instance's argument to exercise the preserved production branch.
        actions[source]!["Arguments"]![0] = "4";
        Set(route, "Step", source);
        Set(route, "started", true);
        Invoke(route, "Update");
        Assert.Equal(5, Read<int>(route, "Step"));
        Assert.Empty(host.MovementCommands);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProductionSelectionRecoversExcludedTraineesWithoutInterruptedStartHistoryOrSavedChanges(bool existingTeam)
    {
        using var host = new HostFixture();
        var service = host.CreateService();
        var teamType = Production.GetType("VERMAXION.Beastmaster.CrucibleTeam", true)!;
        var catalog = (SortedDictionary<uint, live::VERMAXION.Beastmaster.CrucibleFamiliarSheetData>)
            teamType.GetProperty("FamiliarSheetDatas", Static)!.GetValue(null)!;
        (uint Number, int Rank, byte Kin, bool Owned, bool Blocked, bool Reserve)[] beasts =
        [
            (101, 20, 1, true, false, false),
            (102, 20, 1, true, false, true),
            (103, 20, 1, true, false, false),
            (104, 10, 1, true, true, true),
            (105, 12, 1, true, true, false),
            (106, 20, 1, true, true, false),
            (107, 3, 1, false, true, false),
            (108, 0, 1, true, true, false),
            (109, 5, 2, true, true, false),
        ];
        var data = new live::VERMAXION.Beastmaster.BeastmasterCharacterData();
        foreach (var beast in beasts)
        {
            catalog.Add(beast.Number, new()
            {
                Number = beast.Number, Name = $"Synthetic beast {beast.Number}", Element = "", Classification = beast.Kin,
            });
            data.Familiars.Add(beast.Number, new(beast.Number) { Rank = beast.Rank, Exp = "0" });
            if (beast.Owned) data.Owned.Add(beast.Number);
        }
        if (existingTeam) data.Team = [106, 102, 101];
        var savedList = new live::VERMAXION.Beastmaster.BeastTierList { Name = "Synthetic training order" };
        foreach (var number in new uint[] { 106, 104, 103, 109, 101, 105, 102, 107, 108 })
        {
            var beast = beasts.Single(entry => entry.Number == number);
            savedList.Add(new() { ID = number, PickBlock = beast.Blocked, BattleBlock = beast.Reserve });
        }
        savedList.Complete(catalog.Keys);
        var otherList = savedList.Clone("Synthetic inactive list");
        var saved = new live::VERMAXION.Beastmaster.BeastmasterSettings
        {
            Goal = live::VERMAXION.Beastmaster.BeastmasterRunGoal.TrainOwned,
            TeamMode = live::VERMAXION.Beastmaster.CrucibleTeamMode.Leveling,
            LevelingMode = live::VERMAXION.Beastmaster.CrucibleLevelingMode.Carry_Minus_2,
            Board = 2, TrainingTargetRank = 15, ClassificationLock = 1,
            BeastTierLists = [otherList, savedList], BeastTierListIndex = 1,
        };
        data.ManualSettings = saved;
        var run = saved.Clone();
        Set(service, "runSettings", run);
        Set(service, "runData", data);
        Set(service, "preparedOwned", data.Owned.ToHashSet());
        var originalOrder = savedList.Select(entry => entry.ID).ToArray();
        var originalFlags = savedList.Select(entry => (entry.ID, entry.PickBlock, entry.BattleBlock)).ToArray();
        Assert.Equal("Idle", Field(service, "State")!.ToString());
        Assert.Equal(0, Read<int>(service, "CurrentRun"));

        // Invoke the real production wrapper: no native team edits, failed Start,
        // recovery state, or prior attempt is needed to repair this planning error.
        var prepare = teamType.GetMethod("TryPrepareSelection", Static)!;
        object?[] first = [null, null];
        Assert.True((bool)prepare.Invoke(null, first)!);
        Assert.Equal(new uint[] { 103, 101, 102, 104, 105 }, (List<uint>)first[0]!);
        Assert.Equal(string.Empty, first[1]);
        Assert.Single(host.Warnings);
        var active = run.BeastTierLists[1];
        Assert.NotSame(savedList, active);
        Assert.Equal(originalOrder, active.Select(entry => entry.ID));
        Assert.Equal(originalFlags, savedList.Select(entry => (entry.ID, entry.PickBlock, entry.BattleBlock)));
        Assert.Equal(originalFlags, run.BeastTierLists[0].Select(entry => (entry.ID, entry.PickBlock, entry.BattleBlock)));
        Assert.Equal(originalFlags, otherList.Select(entry => (entry.ID, entry.PickBlock, entry.BattleBlock)));
        foreach (var before in originalFlags)
        {
            var after = active.Single(entry => entry.ID == before.ID);
            Assert.Equal(before.BattleBlock, after.BattleBlock);
            Assert.Equal(before.ID is 104 or 105 ? false : before.PickBlock, after.PickBlock);
        }
        Assert.Equal(existingTeam ? new uint[] { 106, 102, 101 } : Array.Empty<uint>(), data.Team);

        // A subsequent preparation consumes the recovered runtime eligibility;
        // the ordinary path succeeds without another recovery or preference edit.
        var recoveredFlags = active.Select(entry => (entry.ID, entry.PickBlock, entry.BattleBlock)).ToArray();
        object?[] second = [null, null];
        Assert.True((bool)prepare.Invoke(null, second)!);
        Assert.Equal((List<uint>)first[0]!, (List<uint>)second[0]!);
        Assert.Equal(string.Empty, second[1]);
        Assert.Single(host.Warnings);
        Assert.Same(active, run.BeastTierLists[1]);
        Assert.Equal(recoveredFlags, active.Select(entry => (entry.ID, entry.PickBlock, entry.BattleBlock)));
        Assert.Equal(originalFlags, savedList.Select(entry => (entry.ID, entry.PickBlock, entry.BattleBlock)));
    }

    private const string SyntheticLootReference = "Synthetic item capacity.\nSynthetic gear capacity.\nSynthetic token capacity.\nSynthetic finish.";
    private const string SyntheticGearNotice = "Synthetic gear capacity.\nSynthetic finish.";

    [Fact]
    public void LootCapacityConditionalSheetTextMatchesRenderedNoticeAndClosesTheOwnedLootVisit()
    {
        // Lumina's ExtractText omits the evaluated If branches. The previous
        // fixtures supplied their text directly and missed this production shape.
        var row = new host::Lumina.Text.SeStringBuilder();
        row.Append("Unable to take all.");
        row.AppendNewLine();
        row.BeginMacro(host::Lumina.Text.Payloads.MacroCode.If);
        row.AppendUIntExpression(1);
        row.AppendStringExpression("You will exceed the capacity of your beast gear inventory.");
        row.AppendStringExpression("");
        row.EndMacro();
        row.AppendNewLine();
        row.Append("Please select which rewards to take.");
        var expected = row.ToReadOnlySeString().ExtractText();
        Assert.DoesNotContain("beast gear", expected);
        Assert.Contains("Unable to take all.", expected);

        using var host = new HostFixture();
        host.EnableNativeUi();
        var menus = Field(host.CreateService(), "menus")!;
        var loot = host.AddLoot();
        host.AddOk("Additional information.\nUNABLE TO TAKE ALL.\nYou will exceed the capacity of your beast gear inventory.\nPlease select which rewards to take.\nAdditional details.");
        var now = DateTime.UtcNow;

        Assert.True(HandleLootRejection(menus, now, expected));
        Assert.Equal(1, host.OkClicks);
        Assert.Equal(0, host.TakeAllClicks);
        Assert.True(HandleLootRejection(menus, now.AddMilliseconds(1), expected));
        Assert.Equal(1, host.LootCloseCallbacks);
        Assert.Equal(0, host.TakeAllClicks);
        host.ShowAddon(loot, false);
        Assert.False(HandleLootRejection(menus, now.AddSeconds(6), expected));
        Assert.False(Read<bool>(menus, "LootRejectionPending"));
    }

    [Fact]
    public void LootCapacityStackAcknowledgesAllEighteenNoticesThenClosesWithoutRepeatingTakeAll()
    {
        using var host = new HostFixture();
        host.EnableNativeUi();
        var service = host.CreateService();
        var menus = Field(service, "menus")!;
        var loot = host.AddLoot();
        Assert.True(host.TakeAll(loot));
        Assert.Equal(1, host.TakeAllClicks);
        host.RecordLootSubmission(menus, loot);
        for (var index = 0; index < 18; index++) host.AddOk(SyntheticGearNotice);
        var now = DateTime.UtcNow;

        for (var index = 0; index < 18; index++)
        {
            Assert.True(HandleLootRejection(menus, now.AddMilliseconds(index), SyntheticLootReference));
            Assert.Equal(index + 1, host.OkClicks);
            Assert.Equal(1, host.TakeAllClicks);
            Assert.Equal(0, host.LootCloseCallbacks);
            Assert.True(Read<bool>(menus, "LootRejectionPending"));
        }
        Assert.Null(Field(menus, "confirmationWindow"));
        var unrelated = host.AddOk("Synthetic unrelated alert.");
        Assert.True(HandleLootRejection(menus, now.AddMilliseconds(30), SyntheticLootReference));
        Assert.Equal(18, host.OkClicks);
        Assert.Equal(0, host.LootCloseCallbacks);
        host.ShowAddon(unrelated, false);
        Assert.True(HandleLootRejection(menus, now.AddMilliseconds(50), SyntheticLootReference));
        Assert.Equal(1, host.LootCloseCallbacks);
        Assert.True(HandleLootRejection(menus, now.AddMilliseconds(100), SyntheticLootReference));
        Assert.Equal(1, host.LootCloseCallbacks);
        Assert.Equal(1, host.TakeAllClicks);

        // Even after the ordinary five-second submission protection expires,
        // a still-open rejected parent retries Close rather than Take All.
        var submissions = (System.Collections.IDictionary)Field(menus, "submittedWindows")!;
        var submissionType = submissions.GetType().GetGenericArguments()[1];
        submissions["XBMContentsBooty"] = Activator.CreateInstance(submissionType,
            loot, DateTime.UtcNow.AddSeconds(-6));
        Assert.True(HandleLootRejection(menus, now.AddSeconds(6), SyntheticLootReference));
        Assert.Equal(2, host.LootCloseCallbacks);
        Assert.Equal(1, host.TakeAllClicks);
        host.ShowAddon(loot, false);
        Invoke(menus, "ObserveWindowChanges");
        Assert.False(HandleLootRejection(menus, now.AddSeconds(12), SyntheticLootReference));
        Assert.False(Read<bool>(menus, "LootRejectionPending"));
        // The game can reuse an addon address on a later loot visit. A prior
        // rejection must not turn that fresh visit into another forced close.
        host.ShowAddon(loot, true);
        Assert.False(HandleLootRejection(menus, now.AddSeconds(13), SyntheticLootReference));
        Assert.True(host.TakeAll(loot));
        Assert.Equal(2, host.TakeAllClicks);
        Assert.Equal(2, host.LootCloseCallbacks);
    }

    [Fact]
    public void AlreadyOpenLootNoticeWaitsForNativeClosureAndDelayedAbandonmentBeforeRouteContinues()
    {
        using var host = new HostFixture { Territory = 1340 };
        host.EnableNativeUi();
        var service = host.CreateService();
        var menus = Field(service, "menus")!;
        var loot = host.AddLoot();
        var notice = host.AddOk(SyntheticGearNotice, hideOnClick: false);
        var now = DateTime.UtcNow;
        // No Take All submission history exists: this also covers Start/reload
        // while the native capacity popup is already open.
        Assert.True(HandleLootRejection(menus, now, SyntheticLootReference));
        Assert.True(HandleLootRejection(menus, now.AddMilliseconds(10), SyntheticLootReference));
        Assert.Equal(1, host.OkClicks);
        Assert.Equal(0, host.TakeAllClicks);
        Assert.Equal(0, host.LootCloseCallbacks);
        host.ShowAddon(notice, false);
        // The parent can also hide after OK, before Close has been submitted.
        host.ShowAddon(loot, false);
        Assert.True(HandleLootRejection(menus, now.AddMilliseconds(20), SyntheticLootReference));
        Assert.True(Read<bool>(menus, "LootRejectionPending"));
        Assert.Equal(0, host.LootCloseCallbacks);
        host.ShowAddon(loot, true);
        Assert.True(HandleLootRejection(menus, now.AddMilliseconds(25), SyntheticLootReference));
        Assert.Equal(1, host.LootCloseCallbacks);

        var route = CreateRoute(service, 1340);
        var actions = (JArray)Field(route, "actions")!;
        var move = actions.Select((action, index) => (action, index))
            .First(entry => entry.action!.Value<string>("Name") == "MoveTo").index;
        Set(route, "Step", move);
        var destination = RoutePosition(route, "MoveTo", 0);
        host.Position = destination + new Vector3(0, 0, 8);
        Set(service, "route", route);
        Set(service, "runSettings", new live::VERMAXION.Beastmaster.BeastmasterSettings { Board = 2 });
        Set(service, "owner", 17UL);
        Set(service, "operatorStopped", false);
        Set(service, "State", Enum.Parse(ServiceType.GetNestedType("RunState", BindingFlags.NonPublic)!, "Running"));
        Set(Field(service, "combat")!, "IsRunning", true);
        // Native close can hide the parent before its abandonment question is
        // ready. Pending ownership, rather than a visible-parent assumption,
        // keeps the route from walking away during that interval.
        host.ShowAddon(loot, false);
        Assert.True(HandleLootRejection(menus, now.AddMilliseconds(30), SyntheticLootReference));
        host.SuppressOrdinaryMenuTick();
        Invoke(service, "Update");
        Assert.Equal(move, Read<int>(route, "Step"));
        Assert.Empty(host.MovementCommands);
        host.AddYesNo();
        Assert.True(HandleLootRejection(menus, now.AddMilliseconds(40), SyntheticLootReference));
        Assert.Equal(1, host.YesClicks);
        Assert.Equal(0, host.TakeAllClicks);
        Assert.False(HandleLootRejection(menus, now.AddSeconds(6), SyntheticLootReference));
        Assert.False(Read<bool>(menus, "LootRejectionPending"));
        host.SuppressOrdinaryMenuTick();
        Invoke(service, "Update");
        Assert.Single(host.MovementCommands);
        Assert.Equal(move, Read<int>(route, "Step"));
    }

    [Theory]
    [InlineData("unrelated")]
    [InlineData("no parent")]
    [InlineData("outside Crucible")]
    [InlineData("not ready")]
    [InlineData("disabled OK")]
    public void LootCapacityHandlingLeavesUnrelatedOrUnownedNativeNoticesAlone(string scenario)
    {
        using var host = new HostFixture();
        host.EnableNativeUi();
        var menus = Field(host.CreateService(), "menus")!;
        if (scenario != "no parent") host.AddLoot();
        var notice = host.AddOk(scenario == "unrelated" ? "Synthetic unrelated alert." : SyntheticGearNotice);
        if (scenario == "outside Crucible") host.Territory = 148;
        if (scenario == "not ready") host.SetAddonReady(notice, false);
        if (scenario == "disabled OK") host.SetOkEnabled(notice, false);
        var handled = HandleLootRejection(menus, DateTime.UtcNow, SyntheticLootReference);
        Assert.Equal(scenario is not ("no parent" or "outside Crucible"), handled);
        Assert.False(Read<bool>(menus, "LootRejectionPending"));
        Assert.Equal(0, host.OkClicks);
        Assert.Equal(0, host.YesClicks);
        Assert.Equal(0, host.TakeAllClicks);
        Assert.Equal(0, host.LootCloseCallbacks);
    }

    [Theory]
    [InlineData("物品が満杯です。\n装備が満杯です。\n証が満杯です。\n報酬を選んでください。", "装備が満杯です。\n報酬を選んでください。", true)]
    [InlineData("Inventaire plein.\nÉquipement plein.\nJetons pleins.\nChoisissez les récompenses.", "  Équipement   plein.\r\nChoisissez les récompenses. ", true)]
    [InlineData(SyntheticLootReference, "Synthetic gear capacity.\nDifferent finish.", true)]
    [InlineData(SyntheticLootReference, "Synthetic token capacity.\nSynthetic gear capacity.\nSynthetic finish.", true)]
    [InlineData("Unable to take all.\nPlease select which rewards to take.", "Extra text: unable  to\ntake all. More details.", true)]
    [InlineData("Please select which rewards to take.", "Capacity warning.\nPlease select which rewards to take.\nAdditional details.", true)]
    [InlineData("Unable to take all.\nPlease select which rewards to take.", "An unrelated notice.", false)]
    [InlineData("", "An unrelated notice.", false)]
    public void LootRejectionMatchingFindsLocalizedPhrasesWithinRenderedNoticeText(string expected, string actual, bool matches)
    {
        var ui = Production.GetType("VERMAXION.Beastmaster.CrucibleUi", true)!;
        Assert.Equal(matches, (bool)ui.GetMethod("MatchesLootRejection", Static)!.Invoke(null, [actual, expected])!);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("hi")]
    [InlineData("id")]
    [InlineData("it")]
    [InlineData("ja")]
    [InlineData("ko")]
    [InlineData("pl")]
    [InlineData("pt-BR")]
    [InlineData("ru")]
    [InlineData("tr")]
    [InlineData("vi")]
    [InlineData("zh-Hans")]
    public void ProductionBeastmasterNumericStatusesPreserveLocalizedTextSemantics(string language)
    {
        var type = Production.GetType("VERMAXION.UiText", true)!;
        var constructor = type.GetConstructors(Instance).Single();
        var fontDelegateType = constructor.GetParameters()[1].ParameterType;
        var font = typeof(ChocoboStablesBatchTests).GetMethod("NoFont", Static)!
            .MakeGenericMethod(fontDelegateType.GenericTypeArguments[0]).CreateDelegate(fontDelegateType);
        using var text = (IDisposable)constructor.Invoke([language, font]);
        using var frame = (IDisposable)type.GetMethod("Enter", Instance)!.Invoke(text, null)!;
        var beastmaster = type.GetMethod("Beastmaster", Static)!;
        var translate = type.GetMethod("T", Static)!;
        var culture = CultureInfo.GetCultureInfo(language);
        var manager = new ResourceManager("VERMAXION.Localization.Strings_" + language.Replace('-', '_'), Production);
        var resources = manager.GetResourceSet(CultureInfo.InvariantCulture, true, false)!;
        (string Key, int Fields)[] templates =
        [
            ("Equipped durability {0}%; repair starts below {1}%.", 2),
            ("Repair complete; equipped durability {0}%.", 1),
            ("Repair improved durability to {0}%, but it is still below {1}%; continuing.", 2),
            ("Requires level {0} and item level {1}; current level {2}, item level {3}.", 4),
        ];
        object[][] captures =
        [
            ["47", "50", "90", "500"],
            ["0047", "0050", "0090", "0500"],
            ["\u0664\u0667", "\u096b\u0966", "\uff19\uff10", "\u06f5\u06f0\u06f0"],
            ["2147483648", "999999999999999999999999999999", "0", "100000000000000000000"],
        ];
        foreach (var (key, fields) in templates)
        {
            foreach (var values in captures)
            {
                var status = string.Format(CultureInfo.InvariantCulture, key, values);
                var expected = string.Format(culture, resources.GetString(key, false)!, values);
                Assert.Equal(expected, beastmaster.Invoke(null, [status]));
                // The old regex $ anchor admits one final LF and drops it on translation.
                Assert.Equal(expected, beastmaster.Invoke(null, [status + "\n"]));
                var paused = string.Format(culture, resources.GetString("Paused: {0}", false)!, expected);
                Assert.Equal(paused, beastmaster.Invoke(null, ["Paused: " + status]));
                Assert.Equal(paused, beastmaster.Invoke(null, ["Paused: " + status + "\n"]));
            }

            var ordinary = string.Format(CultureInfo.InvariantCulture, key, captures[0]);
            var localized = string.Format(culture, resources.GetString(key, false)!, captures[0]);
            Assert.Equal(ordinary + "\r\n", beastmaster.Invoke(null, [ordinary + "\r\n"]));
            Assert.Equal(localized + "\n\n", beastmaster.Invoke(null, [ordinary + "\n\n"]));
            foreach (var opaque in new[]
            {
                "Synthetic prefix: " + ordinary,
                char.ToLowerInvariant(ordinary[0]) + ordinary[1..],
                ordinary.Replace(" ", "  ", StringComparison.Ordinal),
                ordinary + "!",
                ordinary[..^1] + "!",
            })
                Assert.Equal(opaque, beastmaster.Invoke(null, [opaque]));

            // Invalid numeric fields retain the existing T(original) fallback. Generic
            // localization templates can still translate their opaque field contents.
            foreach (var invalid in new[] { "", "+47", "-47", "4.7", "\u00b2", "\u2167", "\U0001D7CE" })
                for (var field = 0; field < fields; field++)
                {
                    var values = (object[])captures[0].Clone();
                    values[field] = invalid;
                    var status = string.Format(CultureInfo.InvariantCulture, key, values) + "\n";
                    Assert.Equal(translate.Invoke(null, [status]), beastmaster.Invoke(null, [status]));
                }
        }

        const string provider = "Synthetic provider: route #007; opaque {0}\r\nSynthetic detail.";
        Assert.Equal(provider, beastmaster.Invoke(null, [provider]));
        Assert.Equal(string.Format(culture, resources.GetString("Paused: {0}", false)!, provider),
            beastmaster.Invoke(null, ["Paused: " + provider]));
        foreach (var prefix in new[] { "Stopped; combat restoration pending: ", "Combat restoration pending: ",
            "RSR runtime state is incompatible: ", "Recovering in five seconds: " })
            foreach (var detail in new[] { provider, "Repair complete; equipped durability 0047%.\n" })
            {
                var expected = string.Format(culture, resources.GetString(prefix + "{0}", false)!,
                    translate.Invoke(null, [detail]));
                Assert.Equal(expected, beastmaster.Invoke(null, [prefix + detail]));
            }
        manager.ReleaseAllResources();
    }

    [Fact]
    public void ProductionBeastmasterLongNumericNearMatchRemainsOpaqueWithoutThrowing()
    {
        var type = Production.GetType("VERMAXION.UiText", true)!;
        var constructor = type.GetConstructors(Instance).Single();
        var fontDelegateType = constructor.GetParameters()[1].ParameterType;
        var font = typeof(ChocoboStablesBatchTests).GetMethod("NoFont", Static)!
            .MakeGenericMethod(fontDelegateType.GenericTypeArguments[0]).CreateDelegate(fontDelegateType);
        using var text = (IDisposable)constructor.Invoke(["de", font]);
        using var frame = (IDisposable)type.GetMethod("Enter", Instance)!.Invoke(text, null)!;
        // This bounded synthetic response cannot complete the third numeric template.
        // The wrong delimiter also prevents generic resource-template localization.
        var status = "Repair improved durability to " + new string('9', 2 * 1024 * 1024) + "!";
        var actual = (string)type.GetMethod("Beastmaster", Static)!.Invoke(null, [status])!;
        Assert.True(string.Equals(status, actual, StringComparison.Ordinal),
            "An unmatched provider response must remain opaque.");
    }

    private static bool HandleLootRejection(object menus, DateTime now, string expected)
        => (bool)Invoke(menus, "HandleLootRejection", now, expected)!;

    private static void AssertNewPlan(object service)
    {
        var settings = (live::VERMAXION.Beastmaster.BeastmasterSettings)Field(service, "runSettings")!;
        Assert.Equal(1u, settings.Board);
        Assert.Equal(30, settings.RunCount);
        Assert.Equal(1, Read<int>(service, "CurrentRun"));
        Assert.False(Read<bool>(service, "resultSeen"));
        Assert.False(Read<bool>(service, "ResultObserved"));
        Assert.False(Read<bool>(service, "BoardRunCompleted"));
        Assert.False(Read<bool>(service, "resultRanksObserved"));
        Assert.False(Read<bool>(service, "resultRanksUnavailable"));
        Assert.Equal(default, Read<DateTime>(service, "resultReadStarted"));
        Assert.False(Read<bool>(service, "completionCommandPending"));
        Assert.False(Read<bool>(service, "StopAfterNextOutcome"));
    }

    private static object CreateRoute(object service, uint territory, bool resume = false)
        => Activator.CreateInstance(RouteType, Instance, null, [service, territory, resume], null)!;
    private static bool Approach(object route, Vector3 destination) => (bool)Invoke(route, "Approach", destination)!;
    private static void AgeMovement(object route) => Set(route, "movementSince", DateTime.UtcNow.AddSeconds(-1));
    private static Vector3 RoutePosition(object route, string name, int ordinal)
    {
        var position = ((JArray)Field(route, "actions")!).OfType<JObject>()
            .Where(action => action.Value<string>("Name") == name)
            .Select(action => action["Position"]!)
            .Where(position => position.Value<float>("X") != 0 || position.Value<float>("Z") != 0).ElementAt(ordinal);
        return new(position.Value<float>("X"), position.Value<float>("Y"), position.Value<float>("Z"));
    }
    private static object? Invoke(object target, string name, params object?[] arguments)
        => target.GetType().GetMethod(name, Instance)!.Invoke(target, arguments);
    private static object? Field(object target, string name)
        => target.GetType().GetField(name, Instance)?.GetValue(target) ?? target.GetType().GetProperty(name, Instance)?.GetValue(target);
    private static T Read<T>(object target, string name) => (T)Field(target, name)!;
    private static void Set(object target, string name, object? value)
    {
        if (target.GetType().GetField(name, Instance) is { } field) field.SetValue(target, value);
        else target.GetType().GetProperty(name, Instance)!.SetValue(target, value);
    }

    public class HostProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args);
    }

    private sealed unsafe class HostFixture : IDisposable
    {
        private readonly List<Action> restore = [];
        private readonly Type svc = Assembly.Load("ECommons").GetType("ECommons.DalamudServices.Svc", true)!;
        private readonly object player;
        private readonly object navigation;
        private readonly object pluginInterface;
        private readonly object log;
        private readonly List<host::Dalamud.Game.ClientState.Objects.Types.IGameObject> objects = [];
        private nint nativePlayerSlot;
        private nint nativeAtkModule;
        private readonly Dictionary<string, nint> addons = [];
        private readonly List<Delegate> nativeCallbacks = [];
        private readonly Dictionary<nint, Action<int>> addonEvents = [];
        private readonly List<(string Name, int Param)> receivedEvents = [];
        private readonly List<(string Name, int Param)> firedCallbacks = [];
        private const int TakeAllParam = 7009;
        public Vector3 Position;
        public uint Territory = 1339;
        public bool LoggedIn = true, PlayerLoaded = true, PlayerDead;
        public ulong Character = 17;
        public bool Pathfinding;
        public readonly HashSet<string> Conditions = [];
        public readonly List<string> Commands = [];
        public readonly List<string> Warnings = [];
        public host::Dalamud.Plugin.IExposedPlugin[] InstalledPlugins = [];
        public int OkClicks => receivedEvents.Count(entry => entry.Name == "SelectOk");
        public int YesClicks => receivedEvents.Count(entry => entry.Name == "SelectYesno");
        public int TakeAllClicks => receivedEvents.Count(entry => entry.Name == "XBMContentsBooty" && entry.Param == TakeAllParam);
        public int LootCloseCallbacks => firedCallbacks.Count(entry => entry.Name == "XBMContentsBooty" && entry.Param == 0);
        public List<string> MovementCommands => Commands.Where(command => command.StartsWith("/vnav moveto ", StringComparison.Ordinal)).ToList();

        public HostFixture()
        {
            try
            {
                player = Proxy(typeof(host::Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter), (method, _) => method.Name switch
                {
                    "get_Position" => Position, "get_IsDead" => PlayerDead, "get_IsCasting" => false,
                    "get_CurrentHp" => 100u, _ => Default(method.ReturnType),
                });
                var objects = Proxy(typeof(host::Dalamud.Plugin.Services.IObjectTable), (method, _) => method.Name switch
                {
                    "get_LocalPlayer" => player,
                    "GetEnumerator" => this.objects.GetEnumerator(),
                    _ => Default(method.ReturnType),
                });
                var conditions = Proxy(typeof(host::Dalamud.Plugin.Services.ICondition), (method, args) =>
                    method.Name == "get_Item" ? Conditions.Contains(args![0]!.ToString()!) : Default(method.ReturnType));
                log = Proxy(typeof(host::Dalamud.Plugin.Services.IPluginLog), (method, args) =>
                {
                    if (method.Name == "Warning" && args?.FirstOrDefault() is string message) Warnings.Add(message);
                    return Default(method.ReturnType);
                });
                pluginInterface = Proxy(typeof(host::Dalamud.Plugin.IDalamudPluginInterface), (method, _) =>
                    method.Name == "get_InstalledPlugins" ? InstalledPlugins : Default(method.ReturnType));
                Replace(typeof(live::VERMAXION.Plugin), "PluginInterface", pluginInterface);
                Replace(typeof(live::VERMAXION.Plugin), "ObjectTable", objects);
                Replace(typeof(live::VERMAXION.Plugin), "Condition", conditions);
                Replace(svc, "Objects", objects);
                Replace(svc, "Condition", conditions);
                Replace(svc, "Log", log);
                var gameGui = Proxy(typeof(host::Dalamud.Plugin.Services.IGameGui), (method, args) =>
                    method.Name == "GetAddonByName" && args?.FirstOrDefault() is string name
                        ? new host::Dalamud.Game.NativeWrapper.AtkUnitBasePtr(addons.GetValueOrDefault(name))
                        : Default(method.ReturnType));
                Replace(svc, "GameGui", gameGui);
                Replace(typeof(live::VERMAXION.Plugin), "GameGui", gameGui);
                Replace(typeof(live::VERMAXION.Plugin), "Log", log);
                Replace(svc, "ClientState", Proxy(typeof(host::Dalamud.Plugin.Services.IClientState), (method, _) => method.Name switch
                {
                    "get_TerritoryType" => Territory, "get_IsLoggedIn" => LoggedIn, _ => Default(method.ReturnType),
                }));
                Replace(svc, "PlayerState", Proxy(typeof(host::Dalamud.Plugin.Services.IPlayerState), (method, _) => method.Name switch
                {
                    "get_IsLoaded" => PlayerLoaded, "get_ContentId" => Character, _ => Default(method.ReturnType),
                }));

                navigation = CreateNavigation();

                var catalogType = Production.GetType("VERMAXION.Beastmaster.CrucibleTeam", true)!;
                var catalog = catalogType.GetField("<FamiliarSheetDatas>k__BackingField", Static)!;
                var oldCatalog = catalog.GetValue(null);
                catalog.SetValue(null, Activator.CreateInstance(catalog.FieldType));
                restore.Add(() => catalog.SetValue(null, oldCatalog));
                var runtime = Production.GetType("VERMAXION.Beastmaster.BeastmasterRuntime", true)!;
                var runtimeService = runtime.GetProperty("Service", Static)!;
                var oldService = runtimeService.GetValue(null);
                restore.Add(() => runtimeService.SetValue(null, oldService));

                CreateNativePlayerAvailability();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        // Implemented against the existing generated ClientStructs static-address
        // seam; the only synthetic native fact is whether a player slot exists.
        private void CreateNativePlayerAvailability()
        {
            var manager = typeof(host::FFXIVClientStructs.FFXIV.Client.Game.Object.GameObjectManager);
            var address = manager.GetNestedType("Addresses")!.GetField("Instance", Static)!.GetValue(null)!;
            var value = address.GetType().GetField("Value")!;
            var original = value.GetValue(address);
            var size = (int)manager.GetField("StructSize")!.GetRawConstantValue()!;
            var block = Marshal.AllocHGlobal(size);
            restore.Add(() =>
            {
                value.SetValue(address, original);
                Marshal.FreeHGlobal(block);
            });
            Marshal.Copy(new byte[size], 0, block, size);
            var objects = manager.GetField("Objects")!;
            var playerSlot = objects.FieldType.GetField("_indexSorted", Instance)!;
            nativePlayerSlot = block + objects.GetCustomAttribute<FieldOffsetAttribute>()!.Value
                + playerSlot.GetCustomAttribute<FieldOffsetAttribute>()!.Value;
            // Instance uses pInstance directly, with no pointer-to-pointer indirection.
            value.SetValue(address, block);
            NativePlayerAvailable = true;
        }
        public bool NativePlayerAvailable { set => Marshal.WriteIntPtr(nativePlayerSlot, value ? (nint)1 : 0); }

        // Existing generated native-address seams point only at allocations and
        // callbacks owned by this fixture. No game process or client is involved.
        public void EnableNativeUi()
        {
            var framework = Allocate<host::FFXIVClientStructs.FFXIV.Client.System.Framework.Framework>();
            var frameworkSlot = Allocate<nint>();
            *frameworkSlot = (nint)framework;
            ReplaceNativeAddress(typeof(host::FFXIVClientStructs.FFXIV.Client.System.Framework.Framework), "Instance", (nint)frameworkSlot);
            var ui = Allocate<NativeUi.UIModule>();
            var atk = Allocate<NativeUi.RaptureAtkModule>();
            var uiAddress = (nint)ui;
            nativeAtkModule = (nint)atk;
            ReplaceNativeAddress(typeof(host::FFXIVClientStructs.FFXIV.Client.System.Framework.Framework), "GetUIModule",
                CallbackPointer(new NativeGetter(_ => uiAddress)));
            var uiVtable = AllocatePointers(16);
            uiVtable[7] = CallbackPointer(new NativeGetter(_ => nativeAtkModule));
            *(nint*)ui = (nint)uiVtable;

            ReplaceNativeAddress(typeof(Gui.AtkUldManager), "SearchNodeById", CallbackPointer(new NativeFindNode((manager, id) =>
            {
                var uld = (Gui.AtkUldManager*)manager;
                for (var index = 0; index < uld->NodeListCount; index++)
                    if (uld->NodeList[index] != null && uld->NodeList[index]->NodeId == id) return (nint)uld->NodeList[index];
                return 0;
            })));
            ReplaceNativeAddress(typeof(Gui.AtkResNode), "IsVisible", CallbackPointer(new NativePredicate(node =>
                (byte)((((Gui.AtkResNode*)node)->NodeFlags & Gui.NodeFlags.Visible) != 0 ? 1 : 0))));
            ReplaceNativeAddress(typeof(Gui.AtkResNode), "GetAsAtkComponentNode", CallbackPointer(new NativeGetter(node => node)));
            ReplaceNativeAddress(typeof(Gui.AtkComponentBase), "GetComponentType", CallbackPointer(new NativeIntGetter(_ => (int)Gui.ComponentType.Button)));
            var fire = CallbackPointer(new NativeFireCallback(FireNativeCallback));
            ReplaceNativeAddress(typeof(Gui.AtkUnitBase), "FireCallback", fire);
            var callbackType = Assembly.Load("ECommons").GetType("ECommons.Automation.Callback", true)!;
            if (callbackType.GetField("FireCallback", Static) is { } callbackField)
            {
                var previous = callbackField.GetValue(null);
                callbackField.SetValue(null, Delegate.CreateDelegate(callbackField.FieldType, this,
                    GetType().GetMethod(nameof(FireNativeCallback), Instance)!));
                restore.Add(() => callbackField.SetValue(null, previous));
            }
            var throttlerType = Assembly.Load("ECommons").GetType("ECommons.Throttlers.EzThrottler", true)!;
            var throttler = throttlerType.GetField("Throttler", Static)!;
            var original = throttler.GetValue(null);
            throttler.SetValue(null, Activator.CreateInstance(throttler.FieldType));
            restore.Add(() => throttler.SetValue(null, original));
        }

        public nint AddLoot()
        {
            var loot = Allocate<Gui.AtkUnitBase>();
            InitializeAddon(loot, "XBMContentsBooty");
            var button = AddButton(loot, TakeAllParam, 46);
            loot->UldManager.NodeList = (Gui.AtkResNode**)AllocatePointers(1);
            loot->UldManager.NodeList[0] = (Gui.AtkResNode*)button->AtkComponentBase.OwnerNode;
            loot->UldManager.NodeListCount = 1;
            return (nint)loot;
        }

        public nint AddOk(string text, bool hideOnClick = true)
        {
            var notice = Allocate<NativeUi.AddonSelectOk>();
            var addon = (Gui.AtkUnitBase*)notice;
            InitializeAddon(addon, "SelectOk");
            notice->OkButton = AddButton(addon, 1, 3);
            notice->PromptText = Allocate<Gui.AtkTextNode>();
            var bytes = System.Text.Encoding.UTF8.GetBytes(text + "\0");
            var buffer = AllocateBytes(bytes.Length);
            Marshal.Copy(bytes, 0, buffer, bytes.Length);
            notice->PromptText->NodeText.StringPtr = (byte*)buffer;
            notice->PromptText->NodeText.BufSize = bytes.Length;
            notice->PromptText->NodeText.BufUsed = bytes.Length;
            notice->PromptText->NodeText.StringLength = bytes.Length - 1;
            var address = (nint)notice;
            addonEvents[address] = _ => { if (hideOnClick) ShowAddon(address, false); };
            return address;
        }

        public nint AddYesNo()
        {
            var question = Allocate<NativeUi.AddonSelectYesno>();
            var addon = (Gui.AtkUnitBase*)question;
            InitializeAddon(addon, "SelectYesno");
            question->YesButton = AddButton(addon, 1, 3);
            var address = (nint)question;
            addonEvents[address] = _ => ShowAddon(address, false);
            return address;
        }

        public void ShowAddon(nint address, bool visible) => ((Gui.AtkUnitBase*)address)->IsVisible = visible;
        public void SetAddonReady(nint address, bool ready)
        {
            var addon = (Gui.AtkUnitBase*)address;
            addon->Flags1A1 = ready ? (byte)(addon->Flags1A1 | 1) : (byte)(addon->Flags1A1 & ~1);
        }
        public void SetOkEnabled(nint address, bool enabled)
        {
            var node = ((NativeUi.AddonSelectOk*)address)->OkButton->AtkComponentBase.OwnerNode;
            if (enabled) node->AtkResNode.NodeFlags |= Gui.NodeFlags.Enabled;
            else node->AtkResNode.NodeFlags &= ~Gui.NodeFlags.Enabled;
        }
        public bool TakeAll(nint address)
        {
            var booty = Production.GetType("VERMAXION.Beastmaster.CrucibleUi+Screens+Booty", true)!;
            return (bool)booty.GetMethod("TakeAll", Static)!.Invoke(null,
                [Pointer.Box((void*)address, typeof(Gui.AtkUnitBase*))])!;
        }
        public void RecordLootSubmission(object menus, nint address) => Invoke(menus, "Submitted", "XBMContentsBooty",
            Pointer.Box((void*)address, typeof(Gui.AtkUnitBase*)));
        public void SuppressOrdinaryMenuTick()
        {
            // The real service guard runs while the ordinary menu poll is
            // throttled; the tested helper already supplied its localized text.
            var type = Assembly.Load("ECommons").GetType("ECommons.Throttlers.EzThrottler", true)!;
            type.GetMethod("Throttle", Static, null, [typeof(string), typeof(TimeSpan), typeof(bool)], null)!
                .Invoke(null, ["CrucibleMenus", TimeSpan.FromMinutes(1), true]);
        }

        private void InitializeAddon(Gui.AtkUnitBase* addon, string name)
        {
            addon->NameString = name;
            addon->IsVisible = true;
            addon->Flags1A1 |= 1;
            addon->UldManager.LoadedState = Gui.AtkLoadState.Loaded;
            var vtable = AllocatePointers(80);
            vtable[2] = CallbackPointer(new NativeReceiveEvent((self, _, param, _, _) =>
            {
                receivedEvents.Add((((Gui.AtkUnitBase*)self)->NameString, param));
                if (addonEvents.TryGetValue(self, out var handle)) handle(param);
            }));
            *(nint*)addon = (nint)vtable;
            addons[name] = (nint)addon;
            ref var list = ref ((NativeUi.RaptureAtkModule*)nativeAtkModule)->RaptureAtkUnitManager.AllLoadedUnitsList;
            list.Entries[list.Count] = addon;
            list.Count++;
        }
        private Gui.AtkComponentButton* AddButton(Gui.AtkUnitBase* addon, int param, uint nodeId)
        {
            var button = Allocate<Gui.AtkComponentButton>();
            var node = Allocate<Gui.AtkComponentNode>();
            var evt = Allocate<Gui.AtkEvent>();
            node->AtkResNode.NodeId = nodeId;
            node->AtkResNode.Type = (Gui.NodeType)1000;
            node->AtkResNode.NodeFlags = Gui.NodeFlags.Enabled | Gui.NodeFlags.Visible;
            node->AtkResNode.AtkEventManager.Event = evt;
            node->Component = (Gui.AtkComponentBase*)button;
            button->AtkComponentBase.AtkResNode = (Gui.AtkResNode*)node;
            button->AtkComponentBase.OwnerNode = node;
            evt->State.EventType = Gui.AtkEventType.ButtonClick;
            evt->Param = (uint)param;
            evt->Listener = (Gui.AtkEventListener*)addon;
            return button;
        }
        private byte FireNativeCallback(Gui.AtkUnitBase* addon, int count, Gui.AtkValue* values, byte _)
        {
            firedCallbacks.Add((addon->NameString, count > 0 ? values[0].Int : -1));
            return 1;
        }
        private nint CallbackPointer(Delegate callback)
        {
            nativeCallbacks.Add(callback);
            return Marshal.GetFunctionPointerForDelegate(callback);
        }
        private void ReplaceNativeAddress(Type type, string name, nint pointer)
        {
            var address = type.GetNestedType("Addresses")!.GetField(name, Static)!.GetValue(null)!;
            var value = address.GetType().GetField("Value")!;
            var previous = value.GetValue(address);
            value.SetValue(address, pointer);
            restore.Add(() => value.SetValue(address, previous));
        }
        private T* Allocate<T>() where T : unmanaged => (T*)AllocateBytes(sizeof(T));
        private nint* AllocatePointers(int count) => (nint*)AllocateBytes(count * nint.Size);
        private nint AllocateBytes(int size)
        {
            var block = Marshal.AllocHGlobal(size);
            new Span<byte>((void*)block, size).Clear();
            restore.Add(() => Marshal.FreeHGlobal(block));
            return block;
        }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint NativeGetter(nint self);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint NativeFindNode(nint self, uint id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate byte NativePredicate(nint self);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeIntGetter(nint self);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate byte NativeFireCallback(Gui.AtkUnitBase* self, int count, Gui.AtkValue* values, byte update);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void NativeReceiveEvent(nint self, Gui.AtkEventType type, int param, nint evt, nint data);

        public void AddBoardObject(uint baseId, Vector3 position) => objects.Add(
            (host::Dalamud.Game.ClientState.Objects.Types.IGameObject)Proxy(
                typeof(host::Dalamud.Game.ClientState.Objects.Types.IGameObject), (method, _) => method.Name switch
                {
                    "get_BaseId" => baseId, "get_Position" => position, _ => Default(method.ReturnType),
                }));

        public object CreateService(bool separateNavigation = false)
        {
            var settings = new live::VERMAXION.Beastmaster.BeastmasterSettings();
            var data = new live::VERMAXION.Beastmaster.BeastmasterCharacterData();
            return Activator.CreateInstance(ServiceType, Instance, null,
                [new Func<live::VERMAXION.Beastmaster.BeastmasterSettings>(() => settings),
                 new Func<live::VERMAXION.Beastmaster.BeastmasterCharacterData>(() => data),
                 new Action(() => { }), separateNavigation ? CreateNavigation() : navigation, null, null], null)!;
        }

        private object CreateNavigation()
        {
            var navType = typeof(live::VERMAXION.IPC.VNavmeshIPC);
            var instance = RuntimeHelpers.GetUninitializedObject(navType);
            Set(instance, "log", log);
            Set(instance, "groundRecovery", new live::VERMAXION.Models.GroundNavigationRecoveryTracker());
            Set(instance, "commandManager", Proxy(typeof(host::Dalamud.Plugin.Services.ICommandManager), (method, args) =>
            {
                if (method.Name != "ProcessCommand") return Default(method.ReturnType);
                Commands.Add((string)args![0]!);
                return true;
            }));
            foreach (var name in new[] { "pathIsRunningSubscriber", "pathfindInProgressSubscriber", "navIsReadySubscriber" })
            {
                var field = navType.GetField(name, Instance)!;
                field.SetValue(instance, Proxy(field.FieldType, (method, _) => method.Name == "InvokeFunc"
                    ? name == "navIsReadySubscriber" || name == "pathfindInProgressSubscriber" && Pathfinding
                    : Default(method.ReturnType)));
            }
            return instance;
        }

        private void Replace(Type type, string name, object value)
        {
            var property = type.GetProperty(name, Static)!;
            var previous = property.GetValue(null);
            property.SetValue(null, value);
            restore.Add(() => property.SetValue(null, previous));
        }
        private static object Proxy(Type type, Func<MethodInfo, object?[]?, object?> handler)
        {
            var proxy = DispatchProxy.Create(type, typeof(HostProxy));
            ((HostProxy)proxy).Handler = handler;
            return proxy;
        }
        private static object? Default(Type type) => type == typeof(void) || !type.IsValueType ? null : Activator.CreateInstance(type);
        public void Dispose()
        {
            for (var index = restore.Count - 1; index >= 0; index--) restore[index]();
        }
    }
}
