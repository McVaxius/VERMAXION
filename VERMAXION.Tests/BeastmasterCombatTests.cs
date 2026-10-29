using System.Reflection;
using System.Runtime.Loader;
using System.Collections.Generic;
using VERMAXION.Beastmaster;
using Xunit;
using Newtonsoft.Json.Linq;

namespace VERMAXION.Tests
{
    [Collection("Beastmaster production host")]
    public sealed class BeastmasterCombatTests
    {
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void NativeLeaseCancelsRotationOnceAndKeepsMovementDuringAttackHold(bool bmrMovement)
        {
            var provider = new FakeProvider();
            var combat = new BeastmasterCombat(provider, bmrMovement);
            Assert.True(BeastmasterCombat.CanStart(provider, out var error), error);
            Assert.Null(provider.Current.Owner);
            Assert.Equal(0, provider.Cancellations);
            Assert.True(combat.TrySuspend(out error), error);
            var owner = provider.Current.Owner;
            Assert.NotNull(owner);
            Assert.False(provider.Current.Active);
            Assert.True(combat.TryActivate(out error, allowAttacks: false), error);
            Assert.Equal(owner, provider.Current.Owner);
            Assert.True(provider.Current.Active);
            Assert.False(provider.Current.AllowAttacks);
            Assert.Equal(bmrMovement, provider.Current.UseBmrMovement);
            Assert.True(combat.IsRunning);
            Assert.Equal(provider.Target, combat.ReadTarget());
            Assert.True(combat.TryActivate(out error), error);
            Assert.True(provider.Current.AllowAttacks);
            Assert.True(combat.TrySuspend(out error), error);
            Assert.False(provider.Current.Active);
            Assert.False(provider.Current.AllowAttacks);
            Assert.True(combat.OwnsState);
            Assert.True(combat.TryRelease(out error), error);
            Assert.False(combat.OwnsState);
            Assert.False(combat.IsRunning);
            Assert.Null(provider.Current.Owner);
            var calls = provider.Releases;
            Assert.True(combat.TryRelease(out error), error);
            Assert.Equal(calls, provider.Releases);
            Assert.Equal(1, provider.Cancellations);
        }

        [Fact]
        public void DamageItemAdviceRequiresTheActualOwnedPermissionAndClearsWithTheNativeWindow()
        {
            var provider = new FakeProvider();
            provider.Current = provider.Current with { AllowDamageItems = true, DamageItem = 139 };
            var combat = new BeastmasterCombat(provider);
            Assert.Equal(0u, combat.ReadTarget().DamageItem);
            Assert.True(combat.TryActivate(out var error), error);
            Assert.Equal(139u, combat.ReadTarget().DamageItem);
            provider.Current = provider.Current with { DamageItem = 0 };
            Assert.Equal(0u, combat.ReadTarget().DamageItem);
            provider.Current = provider.Current with { DamageItem = 139, AllowDamageItems = false };
            Assert.Equal(0u, combat.ReadTarget().DamageItem);
            provider.Current = provider.Current with { AllowDamageItems = true };
            Assert.True(combat.TrySuspend(out error), error);
            Assert.Equal(0u, combat.ReadTarget().DamageItem);
        }

        [Fact]
        public void DamageItemsRequireCurrentOwnedAttackPermissionAndRejectChangedProviders()
        {
            var provider = new FakeProvider();
            provider.Current = provider.Current with { AllowDamageItems = true };
            var combat = new BeastmasterCombat(provider);
            Assert.False(combat.AllowDamageItems); // An advertised flag is not an owned lease.
            Assert.True(combat.TryActivate(out var error), error);
            Assert.True(combat.AllowDamageItems);
            var permitted = provider.Current;
            provider.Current = permitted with { AllowDamageItems = false };
            Assert.False(combat.AllowDamageItems);
            provider.Current = permitted with { AllowAttacks = false };
            Assert.False(combat.AllowDamageItems);
            provider.Current = permitted with { Owner = "Another controller" };
            Assert.False(combat.AllowDamageItems);
            provider.Current = permitted with { Provider = new object() };
            Assert.False(combat.AllowDamageItems);
            provider.Current = permitted;
            provider.ReadUnavailable = true;
            Assert.False(combat.AllowDamageItems);
            provider.ReadUnavailable = false;
            Assert.True(combat.TrySuspend(out error), error);
            Assert.False(combat.AllowDamageItems);
            Assert.True(combat.TryRelease(out error), error);
            Assert.False(combat.AllowDamageItems);
        }

        [Fact]
        public void PendingArenaPreparationRetainsItsLeaseWithoutCancellingItWithControl()
        {
            var provider = new FakeProvider();
            var combat = new BeastmasterCombat(provider);
            Assert.True(combat.TryPrepare(1, 2, out var ready, out _, out var error), error);
            Assert.False(ready);
            Assert.True(combat.IsRunning);
            var owner = provider.Current.Owner;
            Assert.True(combat.TryPrepare(1, 2, out ready, out _, out error), error);
            Assert.False(ready);
            Assert.Equal(owner, provider.Current.Owner);
            Assert.Equal(1, provider.Controls);
            Assert.Equal(2, provider.Preparations);
            provider.PreparationReady = true;
            Assert.True(combat.TryPrepare(1, 2, out ready, out _, out error), error);
            Assert.True(ready);
            Assert.Equal(1, provider.Controls);
            Assert.True(combat.TryRelease(out error), error);
            Assert.Equal(1, provider.Cancellations);
        }

        [Theory]
        [InlineData("")]
        [InlineData("duplicate roster")]
        [InlineData("short roster")]
        [InlineData("foreign trio")]
        [InlineData("duplicate trio")]
        [InlineData("borrow mismatch")]
        [InlineData("borrow outside trio")]
        [InlineData("missing first active")]
        [InlineData("required gear")]
        [InlineData("invalid required gear")]
        [InlineData("duplicate required gear")]
        [InlineData("malformed required gear")]
        [InlineData("private edition")]
        public void NativeSetupCatalogForcesExactCapacityAndTrioWithoutCallerSelection(string defect)
        {
            var ipc = Plugin.PluginInterface;
            var previousCall = ipc.IpcCall;
            try
            {
                uint[] roster = Enumerable.Range(1, 10).Select(value => (uint)value).ToArray();
                var payload = new JObject
                {
                    ["Roster"] = new JArray(roster), ["Trio"] = new JArray(1u, 2u, 3u),
                    ["PreBorrowFamiliar"] = 0u, ["BorrowAction"] = 0u, ["FirstActiveFamiliar"] = 1u, ["Error"] = "",
                };
                if (defect == "duplicate roster") payload["Roster"]![1] = 1u;
                if (defect == "short roster") payload["Roster"] = new JArray(roster.Take(9));
                if (defect == "foreign trio") payload["Trio"]![2] = 50u;
                if (defect == "duplicate trio") payload["Trio"]![2] = 1u;
                if (defect == "borrow mismatch") payload["BorrowAction"] = 46788u;
                if (defect == "borrow outside trio") { payload["PreBorrowFamiliar"] = 4u; payload["BorrowAction"] = 46788u; }
                if (defect == "missing first active") payload["FirstActiveFamiliar"] = 0u;
                if (defect == "required gear") payload["RequiredGear"] = new JArray(2u);
                if (defect == "invalid required gear") payload["RequiredGear"] = new JArray(0u);
                if (defect == "duplicate required gear") payload["RequiredGear"] = new JArray(2u, 2u);
                if (defect == "malformed required gear") payload["RequiredGear"] = new JArray("2");
                if (defect == "private edition") payload = new JObject { ["Error"] = "This board requires the private edition." };
                ipc.IpcCall = (name, args) =>
                {
                    Assert.Equal(BeastmasterCombat.IpcPrefix + "Setup.v1", name);
                    Assert.Equal(1u, args[0]);
                    return payload.ToString();
                };
                var valid = defect.Length == 0 || defect == "required gear";
                Assert.Equal(valid, BeastmasterCombat.TryReadSetup(1, 2, out var setup, out var error));
                if (!valid) { Assert.NotEmpty(error); return; }
                Assert.Equal(roster, setup.Roster);
                Assert.Equal(new uint[] { 1, 2, 3 }, setup.Trio);
                Assert.Equal(defect == "required gear" ? new uint[] { 2 } : [], setup.RequiredGear);
                var settings = new BeastmasterSettings { Board = 1, Degree = CrucibleDegree.Third,
                    TeamMode = CrucibleTeamMode.Custom, CustomTeam = [40, 41, 42], RecommendedTeamCap = 3 };
                var owned = Enumerable.Range(1, 50).Select(value => new BeastmasterCandidate((uint)value, 5, 0, 0));
                Assert.True(BeastmasterSelection.TryPrepareTeam(settings, owned, 3, out var selected, out error), error);
                Assert.Equal(roster, selected);
                Assert.Equal(new uint[] { 40, 41, 42 }, settings.CustomTeam);
            }
            finally { ipc.IpcCall = previousCall; }
        }

        [Theory]
        [InlineData("available")]
        [InlineData("unavailable")]
        [InlineData("error")]
        public void NativeCancellationIsBestEffortAndOnlyOccursOncePerLease(string commandState)
        {
            var ipc = Plugin.PluginInterface;
            var previousInventory = ipc.InstalledPlugins;
            var previousInstances = ECommons.Reflection.DalamudReflector.PluginInstances;
            var previousCall = ipc.IpcCall;
            var previousFunctions = ipc.IpcHasFunction;
            var previousActions = ipc.IpcHasAction;
            var previousCommand = Plugin.CommandManager.OnCommand;
            var commands = new List<string>();
            var ownersAtCancellation = new List<string?>();
            try
            {
                var state = new JObject
                {
                    ["Available"] = true, ["Ready"] = true, ["Owner"] = null,
                    ["Active"] = false, ["AllowAttacks"] = false, ["UseBmrMovement"] = false,
                    ["MovementStopped"] = true, ["RequiresNativeMovement"] = false,
                    ["Rotation"] = BeastmasterCombat.RotationName, ["Error"] = "",
                };
                ipc.InstalledPlugins = [new("BossModReborn", true), new("RotationSolver", true)];
                ECommons.Reflection.DalamudReflector.PluginInstances = new Dictionary<string, object?>
                    { ["BossModReborn"] = new object() };
                ipc.IpcHasFunction = name => name.StartsWith(BeastmasterCombat.IpcPrefix, StringComparison.Ordinal)
                    ? true : throw new InvalidOperationException("Unexpected provider inspection " + name);
                ipc.IpcHasAction = name => throw new InvalidOperationException("Unexpected action inspection " + name);
                ipc.IpcCall = (name, args) =>
                {
                    switch (name)
                    {
                        case BeastmasterCombat.IpcPrefix + "State.v1": return state.ToString();
                        case BeastmasterCombat.IpcPrefix + "Control.v1":
                            state["Owner"] = (string)args[0]!;
                            state["Active"] = (bool)args[1]!;
                            state["AllowAttacks"] = (bool)args[2]!;
                            state["UseBmrMovement"] = (bool)args[3]!;
                            state["MovementStopped"] = !(bool)args[1]! || !(bool)args[3]!;
                            return "";
                        case BeastmasterCombat.IpcPrefix + "Prepare.v1":
                            return "{\"Ready\":false,\"Status\":\"Preparing\",\"Error\":\"\"}";
                        case BeastmasterCombat.IpcPrefix + "Release.v1":
                            state["Owner"] = null;
                            state["Active"] = false;
                            state["AllowAttacks"] = false;
                            state["UseBmrMovement"] = false;
                            state["MovementStopped"] = true;
                            return "";
                        default: throw new InvalidOperationException("Unexpected provider dispatch " + name);
                    }
                };
                Plugin.CommandManager.OnCommand = command =>
                {
                    commands.Add(command);
                    ownersAtCancellation.Add(state.Value<string>("Owner"));
                    if (commandState == "error") throw new InvalidOperationException("Command unavailable");
                    return commandState == "available";
                };
                var provider = (BeastmasterCombat.IProvider)Activator.CreateInstance(typeof(BeastmasterCombat)
                    .GetNestedType("NativeProvider", BindingFlags.NonPublic)!, nonPublic: true)!;
                var combat = new BeastmasterCombat(provider);
                Assert.True(BeastmasterCombat.CanStart(provider, out var error), error);
                Assert.Empty(commands);
                Assert.True(combat.TryPrepare(1, 2, out _, out _, out error), error);
                Assert.True(combat.TryPrepare(1, 2, out _, out _, out error), error);
                Assert.True(combat.TryActivate(out error, allowAttacks: false), error);
                Assert.True(combat.TrySuspend(out error), error);
                Assert.True(combat.TryActivate(out error), error);
                Assert.True(combat.TryRelease(out error), error);
                Assert.True(combat.TryRelease(out error), error);
                Assert.Equal(new[] { "/rotation cancel" }, commands);
                Assert.False(combat.OwnsState);
                Assert.Null(state.Value<string>("Owner"));
                Assert.False(state.Value<bool>("Active"));
                Assert.False(state.Value<bool>("AllowAttacks"));
                Assert.True(combat.TryActivate(out error), error);
                Assert.True(combat.TryRelease(out error), error);
                Assert.Equal(new[] { "/rotation cancel", "/rotation cancel" }, commands);
                Assert.All(ownersAtCancellation, owner => Assert.Null(owner));
            }
            finally
            {
                ipc.InstalledPlugins = previousInventory;
                ECommons.Reflection.DalamudReflector.PluginInstances = previousInstances;
                ipc.IpcCall = previousCall;
                ipc.IpcHasFunction = previousFunctions;
                ipc.IpcHasAction = previousActions;
                Plugin.CommandManager.OnCommand = previousCommand;
            }
        }

        [Fact]
        public void ControllerMutationBeforeReplyFailureIsReleased()
        {
            var provider = new FakeProvider { FailAfterControl = true };
            var combat = new BeastmasterCombat(provider);
            Assert.False(combat.TryActivate(out var error));
            Assert.Contains("post-write", error);
            Assert.False(combat.OwnsState);
            Assert.Null(provider.Current.Owner);
            Assert.False(provider.Current.Active);
            Assert.False(provider.Current.AllowAttacks);
            Assert.Equal(1, provider.Releases);
            Assert.Equal(1, provider.Cancellations);
        }

        [Fact]
        public void ComposedMovementKeepsTheActionLeaseUntilItsOwnerCompletesCleanup()
        {
            var provider = new FakeProvider { FailAfterControl = true };
            var combat = new BeastmasterCombat(provider, useBmrMovement: false, releaseOnFailure: false);
            Assert.False(combat.TryActivate(out _));
            Assert.True(combat.OwnsState);
            Assert.Equal(0, provider.Releases);
            Assert.True(combat.TrySuspend(out var error), error);
            Assert.True(combat.TryRelease(out error), error);
            Assert.Equal(1, provider.Cancellations);
        }

        [Fact]
        public void NativeMovementStopRequiresActualReadbackBeforeAnotherProviderCanBeEnabled()
        {
            var provider = new FakeProvider();
            var combat = new BeastmasterCombat(provider, releaseOnFailure: false);
            Assert.True(combat.TryActivate(out var error), error);
            provider.UnconfirmedMovementStop = true;
            Assert.False(combat.TrySuspend(out error));
            Assert.Contains("not confirmed", error);
            Assert.True(combat.OwnsState);
            Assert.False(provider.Current.Active);
            Assert.False(provider.Current.UseBmrMovement);
            Assert.False(provider.Current.MovementStopped);
            provider.UnconfirmedMovementStop = false;
            Assert.True(combat.TrySuspend(out error), error);
            Assert.True(provider.Current.MovementStopped);
            Assert.True(combat.TryRelease(out error), error);
        }

        [Fact]
        public void FailedReleaseRetainsTheNativeLeaseForCleanupRetry()
        {
            var provider = new FakeProvider();
            var combat = new BeastmasterCombat(provider);
            Assert.True(combat.TryActivate(out var error), error);
            var owner = provider.Current.Owner;
            provider.FailRelease = true;
            Assert.False(combat.TryRelease(out error));
            Assert.True(combat.OwnsState);
            Assert.Equal(owner, provider.Current.Owner);
            provider.FailRelease = false;
            Assert.True(combat.TryRelease(out error), error);
            Assert.False(combat.OwnsState);
            Assert.Null(provider.Current.Owner);
            Assert.False(provider.Current.Active);
            Assert.Equal(1, provider.Controls);
            Assert.Equal(2, provider.Releases);
            Assert.Equal(1, provider.Cancellations);
        }

        [Fact]
        public void CompletedReleaseNeverReacquiresOrOverwritesAnotherNativeLease()
        {
            var provider = new FakeProvider();
            var combat = new BeastmasterCombat(provider);
            Assert.True(combat.TryActivate(out var error), error);
            Assert.True(combat.TryRelease(out error), error);
            Assert.False(combat.OwnsState);
            Assert.Null(provider.Current.Owner);
            Assert.False(provider.Current.Active);
            provider.Current = provider.Current with { Owner = "Another controller" };
            var writes = provider.Controls + provider.Releases + provider.Cancellations;
            Assert.True(combat.TryRelease(out error), error);
            Assert.Equal(writes, provider.Controls + provider.Releases + provider.Cancellations);
            Assert.Equal("Another controller", provider.Current.Owner);
            Assert.False(combat.OwnsState);
        }

        [Fact]
        public void ReplacedOrUnavailableProviderIsNeverMutatedDuringCleanup()
        {
            var provider = new FakeProvider();
            var combat = new BeastmasterCombat(provider);
            Assert.True(combat.TryActivate(out var error), error);
            var saved = provider.Current;
            provider.Current = saved with { Provider = new object() };
            var writes = provider.Controls + provider.Releases;
            Assert.False(combat.TryRelease(out error));
            Assert.Contains("unloaded or replaced", error);
            Assert.Equal(writes, provider.Controls + provider.Releases);
            provider.Current = saved;
            provider.ReadUnavailable = true;
            Assert.False(combat.TryRelease(out error));
            Assert.Equal(writes, provider.Controls + provider.Releases);
            provider.ReadUnavailable = false;
            Assert.True(combat.TryRelease(out error), error);
        }

        [Fact]
        public void RevokedLeaseDoesNotReacquireOrOverwriteAnotherOwner()
        {
            var provider = new FakeProvider();
            var combat = new BeastmasterCombat(provider);
            Assert.True(combat.TryActivate(out var error), error);
            provider.Current = provider.Current with { Owner = "Another controller" };
            var calls = provider.Controls;
            Assert.False(combat.TryActivate(out error));
            Assert.Contains("revoked", error);
            Assert.Equal(calls, provider.Controls);
            Assert.Equal("Another controller", provider.Current.Owner);
            Assert.True(combat.OwnsState);
        }

        [Theory]
        [InlineData("unavailable")]
        [InlineData("not ready")]
        [InlineData("wrong rotation")]
        [InlineData("owned")]
        public void PreflightIsReadOnlyAndRejectsUnsupportedControllerState(string failure)
        {
            var provider = new FakeProvider();
            provider.Current = failure switch
            {
                "unavailable" => provider.Current with { Available = false },
                "not ready" => provider.Current with { Ready = false },
                "wrong rotation" => provider.Current with { Rotation = "Other rotation" },
                _ => provider.Current with { Owner = "Other controller" },
            };
            var saved = provider.Current;
            Assert.False(BeastmasterCombat.CanStart(provider, out _));
            Assert.Equal(saved, provider.Current);
            Assert.Equal(0, provider.Controls + provider.Releases + provider.Cancellations);
            Assert.False(new BeastmasterCombat(provider).TryActivate(out _));
            Assert.Equal(0, provider.Controls + provider.Releases + provider.Cancellations);
        }

        [Theory]
        [InlineData("")]
        [InlineData("State.v1")]
        [InlineData("Control.v1")]
        [InlineData("Release.v1")]
        [InlineData("Target.v1")]
        [InlineData("Prepare.v1")]
        public void NativeDiscoveryAndTargetRequireCompleteIpc(string missing)
        {
            var ipc = Plugin.PluginInterface;
            var previousInventory = ipc.InstalledPlugins;
            var previousInstances = ECommons.Reflection.DalamudReflector.PluginInstances;
            var previousCall = ipc.IpcCall;
            var previousFunctions = ipc.IpcHasFunction;
            try
            {
                ipc.InstalledPlugins = [new("BossModReborn", true)];
                ipc.IpcHasFunction = name => name != BeastmasterCombat.IpcPrefix + missing;
                ECommons.Reflection.DalamudReflector.PluginInstances = new Dictionary<string, object?> { ["BossModReborn"] = new object() };
                ipc.IpcCall = (name, _) => name switch
                {
                    BeastmasterCombat.IpcPrefix + "State.v1" => "{\"Available\":true,\"Ready\":true,\"Owner\":null,\"Active\":false,\"AllowAttacks\":false,\"UseBmrMovement\":false,\"MovementStopped\":true,\"RequiresNativeMovement\":true,\"Rotation\":\"" + BeastmasterCombat.RotationName + "\",\"Error\":\"\"}",
                    BeastmasterCombat.IpcPrefix + "Target.v1" => (17UL, true),
                    _ => throw new InvalidOperationException("Unexpected write " + name),
                };
                var provider = (BeastmasterCombat.IProvider)Activator.CreateInstance(typeof(BeastmasterCombat)
                    .GetNestedType("NativeProvider", BindingFlags.NonPublic)!, nonPublic: true)!;
                if (missing.Length > 0)
                {
                    ipc.IpcCall = (_, _) => throw new InvalidOperationException("Unexpected dispatch before endpoint validation");
                    Assert.False(BeastmasterCombat.CanStart(provider, out var missingError));
                    Assert.Contains("IPC is unavailable", missingError);
                    return;
                }
                Assert.True(BeastmasterCombat.CanStart(provider, out var error), error);
                var state = provider.Read();
                Assert.False(state.Active);
                Assert.False(state.AllowAttacks);
                Assert.False(state.UseBmrMovement);
                Assert.True(state.RequiresNativeMovement);
                Assert.False(state.AllowDamageItems); // Older providers omit the optional permission.
                Assert.True(new BeastmasterCombat(provider).RequiresNativeMovement);
                Assert.Equal(new BeastmasterCombat.Target(17, true), provider.ReadTarget());
            }
            finally
            {
                ipc.InstalledPlugins = previousInventory;
                ECommons.Reflection.DalamudReflector.PluginInstances = previousInstances;
                ipc.IpcCall = previousCall;
                ipc.IpcHasFunction = previousFunctions;
            }
        }

        private sealed class FakeProvider : BeastmasterCombat.IProvider
        {
            public BeastmasterCombat.State Current = new(new object(), true, true, null, false, false, true,
                BeastmasterCombat.RotationName, "");
            public BeastmasterCombat.Target Target = new(17, true);
            public int Controls, Releases, Cancellations;
            public int Preparations;
            public bool PreparationReady;
            public bool FailAfterControl, FailRelease, ReadUnavailable, UnconfirmedMovementStop;
            public BeastmasterCombat.State Read() => ReadUnavailable ? throw new InvalidOperationException("Provider unavailable") : Current;
            public BeastmasterCombat.Target ReadTarget() => Target;
            public string Control(string owner, bool active, bool allowAttacks, bool useBmrMovement)
            {
                Controls++;
                if (Current.Owner != null && Current.Owner != owner) return "Controller already owned";
                Current = Current with { Owner = owner, Active = active, AllowAttacks = allowAttacks, UseBmrMovement = useBmrMovement,
                    MovementStopped = (!active || !useBmrMovement) && !UnconfirmedMovementStop };
                if (FailAfterControl) { FailAfterControl = false; throw new InvalidOperationException("Synthetic post-write failure"); }
                return "";
            }
            public string Release(string owner)
            {
                Releases++;
                if (Current.Owner != null && Current.Owner != owner) return "Controller already owned";
                if (FailRelease) return "Synthetic release failure";
                Current = Current with { Owner = null, Active = false, AllowAttacks = false };
                return "";
            }
            public string Prepare(string owner, uint board, uint battle)
            {
                Assert.Equal(Current.Owner, owner);
                Preparations++;
                return new JObject { ["Ready"] = PreparationReady, ["Status"] = "Preparing", ["Error"] = "" }.ToString();
            }
            public bool CancelRotation()
            {
                Cancellations++;
                return false;
            }
        }
    }
}

namespace ECommons.Reflection
{
    internal static class DalamudReflector
    {
        public static IReadOnlyDictionary<string, object?> PluginInstances { get; set; }
            = new Dictionary<string, object?>();

        public static bool TryGetDalamudPlugin(string name, out object plugin, out AssemblyLoadContext? context,
            bool suppressErrors, bool ignoreCache)
        {
            context = null;
            var found = PluginInstances.TryGetValue(name, out var instance);
            plugin = instance!;
            return found;
        }
    }
}
