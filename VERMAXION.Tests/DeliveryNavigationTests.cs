using System;
using System.Numerics;
using Dalamud.Plugin.Services;
using VERMAXION.IPC;
using Xunit;

namespace VERMAXION.Tests
{
    public sealed class DeliveryNavigationTests
    {
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
        public static bool IsPlayerAvailable() => Plugin.ObjectTable.LocalPlayer != null;
        public static void SendJump() { }
    }
}
