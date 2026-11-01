extern alias live;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Xunit;

namespace VERMAXION.Tests;

[Collection("Beastmaster production host")]
public sealed class BeastmasterActivePathRecoveryTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [Theory]
    [InlineData(0f, 0f, 0f)]
    [InlineData(0.01f, 0f, -0.01f)]
    [InlineData(0f, 1f, 0f)]
    public void ActivePathWithoutHorizontalProgressSchedulesSameStepRecovery(float x, float y, float z)
    {
        using var fixture = new RouteFixture();
        fixture.Start();
        fixture.ExpireProgress();
        fixture.Position += new Vector3(x, y, z);
        Assert.False(fixture.Tick());
        Assert.True(Read<bool>(fixture.Route, "movementRecoveryPending"));
        Assert.Single(fixture.Moves);
        Assert.Single(fixture.Warnings, warning => warning.Contains("no horizontal progress", StringComparison.Ordinal));
        Assert.Equal(0, Read<int>(fixture.Route, "Step"));
        Assert.False(Read<bool>(fixture.Service, "BoardRunCompleted"));
        Assert.False(Read<bool>(fixture.Service, "ResultObserved"));
    }

    [Fact]
    public void MeaningfulHorizontalMovementRestartsTheProgressWindow()
    {
        using var fixture = new RouteFixture();
        fixture.Start();
        fixture.ExpireProgress();
        fixture.Position += new Vector3(0.75f, 0, 0);
        Assert.False(fixture.Tick());
        Assert.False(fixture.Tick());
        Assert.False(Read<bool>(fixture.Route, "movementRecoveryPending"));
        Assert.Empty(fixture.Warnings);
        Assert.Single(fixture.Moves);
    }

    [Fact]
    public void AnActivePathIsNotReissuedBeforeTheStallDeadline()
    {
        using var fixture = new RouteFixture();
        fixture.Start();
        Assert.False(fixture.Tick());
        Assert.False(Read<bool>(fixture.Route, "movementRecoveryPending"));
        Assert.Empty(fixture.Warnings);
        Assert.Single(fixture.Moves);
    }

    [Fact]
    public void RecoveryWaitsForConfirmedStopAndReissuesExactlyTheSameDestination()
    {
        using var fixture = new RouteFixture();
        fixture.Start();
        Set(fixture.Host, "StopAccepted", false);
        fixture.ExpireProgress();
        Assert.False(fixture.Tick()); // Detect, without replacing the active path.
        Assert.False(fixture.Tick()); // Request a stop, which the host rejects.
        Assert.True(Read<bool>(fixture.Route, "movementRecoveryPending"));
        Assert.Single(fixture.Moves);
        Assert.Single(fixture.Commands, command => command == "/vnav stop");

        Assert.False(fixture.Tick()); // The 500ms stop-retry interval prevents spam.
        Assert.Single(fixture.Commands, command => command == "/vnav stop");
        Set(fixture.Host, "PathRunning", false);
        fixture.AllowStopRetry();
        Assert.False(fixture.Tick()); // Readable idle state releases the recovery.
        Assert.False(Read<bool>(fixture.Route, "movementRecoveryPending"));
        Assert.False(fixture.Tick()); // Only now submit the replacement.
        Assert.Equal(new[] { RouteFixture.MoveCommand, RouteFixture.MoveCommand }, fixture.Moves);
        Assert.Equal(0, Read<int>(fixture.Route, "Step"));
        Assert.DoesNotContain(fixture.Commands, command => command.Contains("jump", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("unreadable")]
    public void PendingOrUnreadableNavigationCannotReceiveAReplacementPath(string reason)
    {
        using var fixture = new RouteFixture();
        fixture.Start();
        Set(fixture.Host, "StopAccepted", false);
        if (reason == "pending")
        {
            Set(fixture.Host, "PathRunning", false);
            Set(fixture.Host, "Pathfinding", true);
        }
        else Set(fixture.Host, "NavigationUnavailable", true);
        fixture.ExpireProgress();
        Assert.False(fixture.Tick());
        Assert.False(fixture.Tick());
        Assert.True(Read<bool>(fixture.Route, "movementRecoveryPending"));
        Assert.Single(fixture.Moves);

        Set(fixture.Host, "Pathfinding", false);
        Set(fixture.Host, "NavigationUnavailable", false);
        Set(fixture.Host, "PathRunning", true); // A previously pending query can start late.
        fixture.AllowStopRetry();
        Assert.False(fixture.Tick());
        Assert.True(Read<bool>(fixture.Route, "movementRecoveryPending"));
        Assert.Single(fixture.Moves);

        Set(fixture.Host, "PathRunning", false);
        fixture.AllowStopRetry();
        Assert.False(fixture.Tick());
        Assert.False(fixture.Tick());
        Assert.Equal(new[] { RouteFixture.MoveCommand, RouteFixture.MoveCommand }, fixture.Moves);
    }

    [Fact]
    public void RecoveryDoesNotStopMovementTheServiceDoesNotOwn()
    {
        using var fixture = new RouteFixture();
        fixture.Start();
        Set(fixture.Service, "ownsNavigation", false);
        fixture.ExpireProgress();
        Assert.False(fixture.Tick());
        Assert.False(fixture.Tick());
        Assert.Single(fixture.Commands);
        Assert.Single(fixture.Moves);
        Assert.True(Read<bool>(fixture.Route, "movementRecoveryPending"));
    }

    [Theory]
    [InlineData("InCombat")]
    [InlineData("BetweenAreas")]
    [InlineData("SufferingStatusAffliction2")]
    [InlineData("SufferingStatusAffliction63")]
    public void ReadinessGuardsDiscardStallTimeWithoutRepathing(string condition)
    {
        using var fixture = new RouteFixture();
        fixture.Start();
        fixture.ExpireProgress();
        Read<HashSet<string>>(fixture.Host, "Conditions").Add(condition);
        Assert.False(fixture.Tick());
        Assert.False(Read<bool>(fixture.Route, "movementRecoveryPending"));
        Assert.False(Read<bool>(fixture.Route, "movementStarted"));
        Assert.Equal(DateTime.MinValue, Read<DateTime>(Field(fixture.Route, "movementProgress")!, "lastProgressAt"));
        Assert.Empty(fixture.Warnings);
        Assert.Single(fixture.Moves);
    }

    [Theory]
    [InlineData("Pause")]
    [InlineData("ResetAfterDeath")]
    public void PauseOrDeathDiscardsAnAlreadyPendingRecovery(string method)
    {
        using var fixture = new RouteFixture();
        fixture.Start();
        fixture.ExpireProgress();
        Assert.False(fixture.Tick());
        Assert.True(Read<bool>(fixture.Route, "movementRecoveryPending"));
        Call(fixture.Route, method);
        Assert.False(Read<bool>(fixture.Route, "movementRecoveryPending"));
        Assert.False(Read<bool>(fixture.Route, "movementStarted"));
        Assert.Equal(DateTime.MinValue, Read<DateTime>(Field(fixture.Route, "movementProgress")!, "lastProgressAt"));
        Assert.Single(fixture.Moves);
    }

    [Fact]
    public void ArrivalStillRequiresBothDistanceAndSettledNavigation()
    {
        using var fixture = new RouteFixture();
        fixture.Start();
        fixture.Position = RouteFixture.Destination;
        Assert.False(fixture.Tick());
        Set(fixture.Host, "PathRunning", false);
        Assert.True(fixture.Tick());
        Assert.Single(fixture.Moves);
        Assert.Empty(fixture.Warnings);
    }

    // Reuse the existing native-host fixture rather than duplicating its unsafe
    // service setup or changing production visibility for a focused regression.
    private sealed class RouteFixture : IDisposable
    {
        internal static readonly Vector3 Destination = new(-700, 0, -59);
        internal const string MoveCommand = "/vnav moveto -700.00 0.00 -59.00";
        internal readonly object Host;
        internal readonly object Service;
        internal readonly object Route;

        internal RouteFixture()
        {
            var hostType = typeof(BeastmasterRouteLifecycleTests).GetNestedType("HostFixture", BindingFlags.NonPublic)!;
            Host = Activator.CreateInstance(hostType, nonPublic: true)!;
            try
            {
                Set(Host, "Territory", 1340u);
                Position = new(-700, 0, -50);
                Service = Call(Host, "CreateService", false)!;
                var routeType = typeof(live::VERMAXION.Plugin).Assembly.GetType("VERMAXION.Beastmaster.BeastmasterRoute", true)!;
                Route = Activator.CreateInstance(routeType, Instance, null, [Service, 1340u, false], null)!;
                Set(Service, "route", Route);
            }
            catch { Dispose(); throw; }
        }

        internal Vector3 Position { get => Read<Vector3>(Host, "Position"); set => Set(Host, "Position", value); }
        internal List<string> Commands => Read<List<string>>(Host, "Commands");
        internal List<string> Warnings => Read<List<string>>(Host, "Warnings");
        internal string[] Moves => Commands.Where(command => command.StartsWith("/vnav moveto ", StringComparison.Ordinal)).ToArray();
        internal bool Tick() => (bool)Call(Route, "Approach", Destination)!;
        internal void Start()
        {
            Assert.False(Tick());
            Assert.Equal(MoveCommand, Assert.Single(Moves));
            Set(Host, "PathRunning", true);
            Set(Route, "movementSince", DateTime.UtcNow.AddSeconds(-1));
        }
        internal void ExpireProgress()
            => Set(Field(Route, "movementProgress")!, "lastProgressAt", DateTime.UtcNow.AddSeconds(-13));
        internal void AllowStopRetry() => Set(Route, "nextMovementRecoveryStop", DateTime.MinValue);
        public void Dispose() => ((IDisposable)Host).Dispose();
    }

    private static object? Call(object target, string name, params object?[] arguments)
        => target.GetType().GetMethod(name, Instance)!.Invoke(target, arguments);
    private static object? Field(object target, string name)
        => target.GetType().GetField(name, Instance)?.GetValue(target) ?? target.GetType().GetProperty(name, Instance)?.GetValue(target);
    private static T Read<T>(object target, string name) => (T)Field(target, name)!;
    private static void Set(object target, string name, object? value)
    {
        if (target.GetType().GetField(name, Instance) is { } field) field.SetValue(target, value);
        else target.GetType().GetProperty(name, Instance)!.SetValue(target, value);
    }
}
