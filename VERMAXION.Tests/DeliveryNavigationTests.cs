using System;
using System.Numerics;
using System.Reflection;
using Dalamud.Plugin.Services;
using VERMAXION.IPC;
using Xunit;

namespace VERMAXION.Tests
{
    public sealed class DeliveryNavigationTests
    {
        [Theory]
        [InlineData(null, 1)]
        [InlineData(true, 1)]
        [InlineData(false, 0)]
        public void JumboCanSuppressOnlyTheRecoveryJumpWhileRetainingStallReissue(bool? allowJump, int expectedJumps)
        {
            Plugin.ObjectTable.LocalPlayer = new TestPlayer { Position = Vector3.Zero };
            VERMAXION.Services.GameHelpers.JumpCount = 0;
            var commands = new ICommandManager();
            try
            {
                using var navigation = new VNavmeshIPC(new IPluginLog(), commands);
                var destination = new Vector3(111.02f, 13, -24.21f);
                Assert.True(Dispatch());
                var tracker = typeof(VNavmeshIPC).GetField("groundRecovery", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(navigation)!;
                tracker.GetType().GetField("lastProgressAt", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(tracker, DateTime.UtcNow.AddSeconds(-13));
                Assert.True(Dispatch());
                Assert.Equal(expectedJumps, VERMAXION.Services.GameHelpers.JumpCount);
                Assert.Equal(2, commands.Commands.Count);
                Assert.All(commands.Commands, command => Assert.Equal("/vnav moveto 111.02 13.00 -24.21", command));

                bool Dispatch() => allowJump is { } selected
                    ? navigation.PathfindAndMoveTo(destination, allowRecoveryJump: selected)
                    : navigation.PathfindAndMoveTo(destination);
            }
            finally
            {
                Plugin.ObjectTable.LocalPlayer = null;
                VERMAXION.Services.GameHelpers.JumpCount = 0;
            }
        }

        [Fact]
        public void BelowSeaLevelDeliveryUsesNearestPointAndRejectsMissingInvalidOrWrongFloorResults()
        {
            var destination = new Vector3(-64, -130, 24);
            Vector3? candidate = destination + new Vector3(0.5f, 0.25f, 0);
            var throwQuery = false;
            var queries = 0;
            Plugin.PluginInterface.IpcCall = (name, args) =>
            {
                // The global reachability filter excludes this valid underwater town.
                if (name == "vnavmesh.Query.Mesh.NearestPointReachable") return null;
                Assert.Equal("vnavmesh.Query.Mesh.NearestPoint", name);
                Assert.Equal(destination, Assert.IsType<Vector3>(args[0]));
                Assert.Equal(3f, Assert.IsType<float>(args[1]));
                Assert.Equal(2f, Assert.IsType<float>(args[2]));
                queries++;
                if (throwQuery) throw new InvalidOperationException("Native query unavailable");
                return candidate;
            };
            try
            {
                using var navigation = new VNavmeshIPC(new IPluginLog(), new ICommandManager());
                Assert.False(navigation.TryFindReachablePointNear(destination, 3, out _));
                Assert.True(navigation.TryFindDeliveryPointNear(destination, 3, out var point));
                Assert.Equal(candidate.Value, point);

                foreach (var rejected in new Vector3?[]
                {
                    null,
                    new(float.NaN, destination.Y, destination.Z),
                    new(destination.X, float.PositiveInfinity, destination.Z),
                    new(destination.X, destination.Y, float.NegativeInfinity),
                    destination + new Vector3(3.1f, 0, 0),
                    destination - new Vector3(0, 13, 0),
                    destination + new Vector3(0, 2.5f, 0),
                })
                {
                    candidate = rejected;
                    Assert.False(navigation.TryFindDeliveryPointNear(destination, 3, out point));
                    Assert.Equal(default, point);
                }
                throwQuery = true;
                Assert.False(navigation.TryFindDeliveryPointNear(destination, 3, out _));
                var beforeInvalidInputs = queries;
                Assert.False(navigation.TryFindDeliveryPointNear(new(float.NaN, 0, 0), 3, out _));
                foreach (var tolerance in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
                    Assert.False(navigation.TryFindDeliveryPointNear(destination, tolerance, out _));
                Assert.Equal(beforeInvalidInputs, queries);
            }
            finally { Plugin.PluginInterface.IpcCall = null; }
        }
    }
}

namespace VERMAXION.Services
{
    // The focused navigation test calls no game actions.
    public static class GameHelpers
    {
        public static int JumpCount;
        public static bool IsPlayerAvailable() => Plugin.ObjectTable.LocalPlayer != null;
        public static void SendJump() => JumpCount++;
    }
}
