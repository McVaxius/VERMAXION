using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Dalamud.Game.ClientState.Conditions;
using VERMAXION.Beastmaster;
using Xunit;

namespace VERMAXION.Tests;

[Collection("Beastmaster production host")]
public sealed class BeastmasterMinervaCombatTests
{
    [Fact]
    public void OlderSettingsKeepBossModAndCopyTheExplicitMinervaChoice()
    {
        Assert.Equal(BeastmasterCombatProvider.BossModReborn, new BeastmasterSettings().CombatProvider);
        Assert.Equal(BeastmasterCombatProvider.BossModReborn,
            System.Text.Json.JsonSerializer.Deserialize<BeastmasterSettings>("{}")!.CombatProvider);
        Assert.Equal(BeastmasterCombatProvider.BossModReborn,
            Newtonsoft.Json.JsonConvert.DeserializeObject<BeastmasterSettings>("{}")!.CombatProvider);
        var selected = new BeastmasterSettings { CombatProvider = BeastmasterCombatProvider.Minerva };
        Assert.Equal(BeastmasterCombatProvider.Minerva, selected.Clone().CombatProvider);
        selected.CombatProvider = (BeastmasterCombatProvider)99;
        Assert.Equal(BeastmasterCombatProvider.BossModReborn, selected.CombatProvider);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("Another controller", false)]
    [InlineData(BeastmasterMinervaCombat.OwnerName, false)]
    public void ReadinessNeverChangesStateAndRefusesAnExistingOwner(string? owner, bool ready)
    {
        var provider = new FakeProvider();
        provider.Current = provider.Current with { Owner = owner };
        var original = provider.Current;
        Assert.Equal(ready, BeastmasterMinervaCombat.CanStart(provider, out var error));
        if (!ready) Assert.Contains("owned", error);
        Assert.Equal(original, provider.Current);
        Assert.Empty(provider.Writes);
    }

    [Fact]
    public void AnUnavailableReadCannotAcquireOrChangeProviders()
    {
        var provider = new FakeProvider { ReadUnavailable = true };
        var original = provider.Current;
        Assert.False(BeastmasterMinervaCombat.CanStart(provider, out var error));
        Assert.Contains("unavailable", error);
        var combat = new BeastmasterMinervaCombat(provider, provider.Abilities);
        Assert.False(combat.TrySuspend(out error));
        Assert.False(combat.OwnsState);
        Assert.Equal(original, provider.Current);
        Assert.Empty(provider.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryActivationForwardsTheFamiliarModeThroughHoldsAndMovementHandoffs(bool scripted)
    {
        var provider = new FakeProvider();
        var combat = new BeastmasterMinervaCombat(provider, provider.Abilities);
        foreach (var mode in new[] { scripted, !scripted, scripted })
        {
            provider.Current = provider.Current with { WithholdAttacks = true };
            Assert.True(combat.TryActivate(out var error, useScriptedFamiliars: mode), error);
            Assert.Equal(mode, provider.Abilities.UseScriptedFamiliars);
            Assert.False(provider.Abilities.AllowAttacks);
            provider.Current = provider.Current with { WithholdAttacks = false, NativeRoute = true };
            Assert.True(combat.TryActivate(out error, useScriptedFamiliars: mode), error);
            Assert.Equal(mode, provider.Abilities.UseScriptedFamiliars);
            Assert.True(provider.Abilities.AllowAttacks);
            Assert.True(provider.Abilities.NativeMovement);
            Assert.True(combat.TrySuspend(out error), error);
            Assert.Equal(mode, provider.Abilities.UseScriptedFamiliars);
            provider.Current = provider.Current with { NativeRoute = false };
        }
        Assert.Equal(1, provider.Claims);
        Assert.True(combat.TryRelease(out var released), released);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SuspensionRetainsOneClaimAndReleaseRestoresTheExactSnapshot(bool dodge, bool facing)
    {
        var provider = new FakeProvider();
        provider.Current = provider.Current with { DodgeEnabled = dodge, FaceGazes = facing };
        var original = provider.Current;
        var combat = new BeastmasterMinervaCombat(provider, provider.Abilities);
        Assert.True(combat.TrySuspend(out var error), error);
        Assert.True(combat.OwnsState);
        Assert.False(combat.IsRunning);
        Assert.Equal(BeastmasterMinervaCombat.OwnerName, provider.Current.Owner);
        Assert.False(provider.Current.DodgeEnabled);
        Assert.False(provider.Current.FaceGazes);
        Assert.True(provider.Current.MovementStopped);
        Assert.False(provider.Abilities.AllowAttacks);
        Assert.False(combat.AllowDamageItems);
        Assert.True(combat.TrySuspend(out error), error);
        Assert.Equal(1, provider.Claims);

        Assert.True(combat.TryActivate(out error), error);
        Assert.True(provider.Current.DodgeEnabled);
        Assert.True(provider.Current.FaceGazes);
        Assert.True(provider.Abilities.AllowAttacks);
        Assert.True(combat.AllowDamageItems);
        provider.Current = provider.Current with { Preset = "Changed during the run" };
        Assert.True(combat.TrySuspend(out error), error);
        Assert.True(combat.OwnsState);
        Assert.True(combat.TryRelease(out error), error);
        Assert.False(combat.OwnsState);
        Assert.False(combat.IsRunning);
        Assert.Equal(original, provider.Current);
        Assert.Equal(1, provider.Claims);
        var writes = provider.Writes.Count;
        Assert.True(combat.TryRelease(out error), error);
        Assert.Equal(writes, provider.Writes.Count);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void AttackHoldKeepsMovementAndFacingOwnedWithoutUsingAbilities(bool allowAttacks, bool restricted)
    {
        var provider = new FakeProvider();
        provider.Current = provider.Current with { WithholdAttacks = restricted };
        var combat = new BeastmasterMinervaCombat(provider, provider.Abilities);
        Assert.True(combat.TryActivate(out var error, allowAttacks), error);
        Assert.True(combat.IsRunning);
        Assert.True(combat.OwnsState);
        Assert.True(provider.Current.DodgeEnabled);
        Assert.True(provider.Current.FaceGazes);
        Assert.False(provider.Abilities.AllowAttacks);
        Assert.False(combat.AllowDamageItems);
        if (restricted)
        {
            Assert.Equal(default, combat.ReadTarget());
            Assert.Equal(0, provider.TargetReads);
        }

        provider.Current = provider.Current with { WithholdAttacks = false };
        Assert.True(combat.TryActivate(out error), error);
        Assert.True(provider.Abilities.AllowAttacks);
        Assert.True(combat.AllowDamageItems);
        Assert.Equal(provider.Target, combat.ReadTarget());
        Assert.Equal(1, provider.Claims);
        Assert.True(combat.TryRelease(out error), error);
    }

    [Fact]
    public void StagedRouteHandsMovementOverOnlyAfterThePreviousOwnerHasStopped()
    {
        var provider = new FakeProvider();
        var combat = new BeastmasterMinervaCombat(provider, provider.Abilities);
        Assert.True(combat.TryActivate(out var error), error);
        Assert.True(provider.Current.DodgeEnabled);
        Assert.False(provider.Abilities.NativeMovement);

        provider.OwnershipTransitions.Clear();
        provider.Current = provider.Current with { NativeRoute = true };
        Assert.True(combat.TryActivate(out error), error);
        Assert.Equal(new[] { "BMR:stopped", "Minerva:False", "BMR:movement" }, provider.OwnershipTransitions);
        Assert.False(provider.Current.DodgeEnabled);
        Assert.True(provider.Current.MovementStopped);
        Assert.True(provider.Abilities.NativeMovement);

        provider.OwnershipTransitions.Clear();
        Assert.True(combat.TryActivate(out error, allowAttacks: false), error);
        Assert.True(combat.TryActivate(out error, allowAttacks: false), error);
        Assert.Equal(new[] { "BMR:movement", "BMR:movement" }, provider.OwnershipTransitions);
        Assert.False(provider.Abilities.AllowAttacks);
        Assert.True(provider.Abilities.NativeMovement);

        provider.OwnershipTransitions.Clear();
        provider.Current = provider.Current with { NativeRoute = false };
        Assert.True(combat.TryActivate(out error), error);
        Assert.Equal(new[] { "BMR:stopped", "Minerva:True", "BMR:actions only" }, provider.OwnershipTransitions);
        Assert.True(provider.Current.DodgeEnabled);
        Assert.False(provider.Abilities.NativeMovement);
        Assert.True(combat.TryRelease(out error), error);
    }

    [Fact]
    public void RequiredNativeMovementWithoutWaypointsOrPriorAttackPermissionUsesTheConfirmedHandoff()
    {
        var provider = new FakeProvider();
        provider.Abilities.RequiresNativeMovement = true;
        var combat = new BeastmasterMinervaCombat(provider, provider.Abilities);
        Assert.False(provider.Current.NativeRoute);
        Assert.False(provider.Abilities.AllowAttacks);
        Assert.True(combat.TryActivate(out var error, allowAttacks: false), error);
        Assert.Equal(new[] { "BMR:stopped", "Minerva:False", "BMR:movement" }, provider.OwnershipTransitions);
        Assert.False(provider.Current.DodgeEnabled);
        Assert.True(provider.Current.MovementStopped);
        Assert.True(provider.Abilities.NativeMovement);
        Assert.False(provider.Abilities.AllowAttacks);

        provider.OwnershipTransitions.Clear();
        Assert.True(combat.TryActivate(out error, allowAttacks: false), error);
        Assert.Equal(new[] { "BMR:movement" }, provider.OwnershipTransitions);
        provider.Abilities.RequiresNativeMovement = false;
        provider.OwnershipTransitions.Clear();
        Assert.True(combat.TryActivate(out error, allowAttacks: false), error);
        Assert.Equal(new[] { "BMR:stopped", "Minerva:True", "BMR:actions only" }, provider.OwnershipTransitions);
        Assert.True(provider.Current.DodgeEnabled);
        Assert.False(provider.Abilities.NativeMovement);
        Assert.False(provider.Abilities.AllowAttacks);
        Assert.True(combat.TryRelease(out error), error);
    }

    [Fact]
    public void UnconfirmedStopsNeverEnableTheOtherMovementOwner()
    {
        var provider = new FakeProvider();
        var combat = new BeastmasterMinervaCombat(provider, provider.Abilities);
        Assert.True(combat.TryActivate(out var error), error);
        provider.Current = provider.Current with { NativeRoute = true };
        provider.MovementStopThrows = true;
        provider.OwnershipTransitions.Clear();
        Assert.False(combat.TryActivate(out error));
        Assert.DoesNotContain("BMR:movement", provider.OwnershipTransitions);
        Assert.False(provider.Abilities.NativeMovement);
        provider.MovementStopThrows = false;
        Assert.True(combat.TryRelease(out error), error);

        provider = new FakeProvider();
        combat = new BeastmasterMinervaCombat(provider, provider.Abilities);
        provider.Current = provider.Current with { NativeRoute = true };
        Assert.True(combat.TryActivate(out error), error);
        provider.Current = provider.Current with { NativeRoute = false };
        provider.Abilities.FailSuspension = true;
        provider.Abilities.FailRelease = true;
        provider.OwnershipTransitions.Clear();
        Assert.False(combat.TryActivate(out error));
        Assert.DoesNotContain("Minerva:True", provider.OwnershipTransitions);
        Assert.False(provider.Current.DodgeEnabled);
        Assert.Equal(BeastmasterMinervaCombat.OwnerName, provider.Current.Owner);
        Assert.True(combat.OwnsState);
        provider.Abilities.FailSuspension = false;
        provider.Abilities.FailRelease = false;
        Assert.True(combat.TryRelease(out error), error);
    }

    [Fact]
    public void APartiallySuccessfulClaimRetainsItsSnapshotWhenCleanupFails()
    {
        var provider = new FakeProvider { ClaimThrowsAfterOwnership = true, ReleaseThrowsBeforeMutation = true };
        var original = provider.Current;
        var combat = new BeastmasterMinervaCombat(provider, provider.Abilities);
        Assert.False(combat.TrySuspend(out var error));
        Assert.Contains("partial claim", error);
        Assert.Contains("release", error);
        Assert.True(combat.OwnsState);
        Assert.Equal(BeastmasterMinervaCombat.OwnerName, provider.Current.Owner);
        Assert.Equal(1, provider.Claims);
        provider.ReleaseThrowsBeforeMutation = false;
        Assert.True(combat.TryRelease(out error), error);
        Assert.False(combat.OwnsState);
        Assert.Equal(original, provider.Current);
        Assert.Equal(1, provider.Claims);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AReleaseErrorRetainsOwnershipUntilAnExplicitReadConfirmsRestoration(bool afterMutation)
    {
        var provider = new FakeProvider();
        var original = provider.Current;
        var combat = new BeastmasterMinervaCombat(provider, provider.Abilities);
        Assert.True(combat.TryActivate(out var error), error);
        provider.ReleaseThrowsBeforeMutation = !afterMutation;
        provider.ReleaseThrowsAfterMutation = afterMutation;
        Assert.False(combat.TryRelease(out error));
        Assert.True(combat.OwnsState);
        Assert.False(combat.IsRunning);
        provider.ReleaseThrowsBeforeMutation = provider.ReleaseThrowsAfterMutation = false;
        Assert.True(combat.TryRelease(out error), error);
        Assert.False(combat.OwnsState);
        Assert.Equal(original, provider.Current);
        Assert.Equal(afterMutation ? 1 : 2, provider.Releases);
        Assert.Equal(1, provider.Abilities.Acquisitions);
        Assert.Equal(1, provider.Abilities.Releases);
    }

    [Fact]
    public void AReleaseReplyFailureDoesNotHoldOrOverwriteTheNextControllersMovementState()
    {
        var provider = new FakeProvider();
        var original = provider.Current;
        var combat = new BeastmasterMinervaCombat(provider, provider.Abilities);
        Assert.True(combat.TryActivate(out var error), error);
        provider.ReleaseThrowsAfterMutation = true;
        Assert.False(combat.TryRelease(out error));
        Assert.True(combat.OwnsState);
        Assert.Null(provider.Current.Owner);

        provider.ReleaseThrowsAfterMutation = false;
        var nextOwner = provider.Current with
        {
            Owner = "Another controller", DodgeEnabled = false, FaceGazes = true, Preset = "New owner selection",
        };
        provider.Current = nextOwner;
        provider.Writes.Clear();
        Assert.True(combat.TryRelease(out error), error);
        Assert.False(combat.OwnsState);
        Assert.False(combat.IsRunning);
        Assert.Equal(nextOwner, provider.Current);
        Assert.DoesNotContain(provider.Writes, write => write is "Claim" or "Release" ||
            write.StartsWith("Movement:", StringComparison.Ordinal) || write.StartsWith("Facing:", StringComparison.Ordinal));
        Assert.Equal(1, provider.Releases);
    }

    [Fact]
    public void AnUnavailableOwnedProviderRetainsOwnershipWithoutAnyCleanupWrite()
    {
        var provider = new FakeProvider();
        var original = provider.Current;
        var combat = new BeastmasterMinervaCombat(provider, provider.Abilities);
        Assert.True(combat.TryActivate(out var error), error);
        var owned = provider.Current;
        provider.Writes.Clear();
        provider.ReadUnavailable = true;
        Assert.False(combat.TryRelease(out error));
        Assert.True(combat.OwnsState);
        Assert.Equal(owned, provider.Current);
        Assert.Empty(provider.Writes);
        provider.ReadUnavailable = false;
        Assert.True(combat.TryRelease(out error), error);
        Assert.Equal(original, provider.Current);
    }

    [Fact]
    public void AnUnconfirmedMovementStopKeepsAbilitiesOffAndRetainsTheOriginalSnapshot()
    {
        var provider = new FakeProvider();
        var original = provider.Current;
        var combat = new BeastmasterMinervaCombat(provider, provider.Abilities);
        Assert.True(combat.TryActivate(out var error), error);
        provider.MovementStopThrows = true;
        Assert.False(combat.TryRelease(out error));
        Assert.True(combat.OwnsState);
        Assert.False(combat.IsRunning);
        Assert.False(provider.Abilities.AllowAttacks);
        Assert.Equal(BeastmasterMinervaCombat.OwnerName, provider.Current.Owner);
        provider.MovementStopThrows = false;
        Assert.True(combat.TryRelease(out error), error);
        Assert.False(combat.OwnsState);
        Assert.Equal(original, provider.Current);
    }

    [Fact]
    public void UnconfirmedNativeReleaseKeepsMinervaStoppedAndLeased()
    {
        var provider = new FakeProvider();
        var original = provider.Current;
        var combat = new BeastmasterMinervaCombat(provider, provider.Abilities);
        Assert.True(combat.TryActivate(out var error), error);
        provider.Abilities.FailSuspension = true;
        provider.Abilities.FailRelease = true;
        Assert.False(combat.TryRelease(out error));
        Assert.Contains("native release", error);
        Assert.Equal(BeastmasterMinervaCombat.OwnerName, provider.Current.Owner);
        Assert.False(provider.Current.DodgeEnabled);
        Assert.True(provider.Current.MovementStopped);
        Assert.True(combat.OwnsState);
        Assert.True(provider.Abilities.OwnsState);
        provider.Abilities.FailSuspension = false;
        provider.Abilities.FailRelease = false;
        Assert.True(combat.TryRelease(out error), error);
        Assert.False(combat.OwnsState);
    }

    [Fact]
    public void RevokedNativeLeaseReachesIdempotentReleaseAfterMinervaStopsWithoutReacquiring()
    {
        var provider = new FakeProvider();
        var original = provider.Current;
        var combat = new BeastmasterMinervaCombat(provider, provider.Abilities);
        Assert.True(combat.TryActivate(out var error), error);
        Assert.Equal(1, provider.Abilities.Acquisitions);
        provider.Abilities.LeaseRevoked = true;
        provider.OwnershipTransitions.Clear();
        Assert.True(combat.TryRelease(out error), error);
        Assert.Equal(new[] { "Minerva:False", "BMR:released" }, provider.OwnershipTransitions);
        Assert.Equal(1, provider.Abilities.Acquisitions);
        Assert.Equal(1, provider.Abilities.Releases);
        Assert.False(combat.OwnsState);
        Assert.Equal(original, provider.Current);
    }

    [Fact]
    public void RefusedRevokedLeaseReleaseRetainsStoppedMinervaUntilNativeCleanupIsConfirmed()
    {
        var provider = new FakeProvider();
        var original = provider.Current;
        var combat = new BeastmasterMinervaCombat(provider, provider.Abilities);
        Assert.True(combat.TryActivate(out var error), error);
        provider.Abilities.LeaseRevoked = provider.Abilities.FailRelease = true;
        Assert.False(combat.TryRelease(out error));
        Assert.True(combat.OwnsState);
        Assert.Equal(BeastmasterMinervaCombat.OwnerName, provider.Current.Owner);
        Assert.False(provider.Current.DodgeEnabled);
        Assert.True(provider.Current.MovementStopped);
        Assert.Equal(0, provider.Releases);
        Assert.Equal(1, provider.Abilities.Acquisitions);
        provider.Abilities.FailRelease = false;
        Assert.True(combat.TryRelease(out error), error);
        Assert.Equal(1, provider.Abilities.Acquisitions);
        Assert.False(combat.OwnsState);
        Assert.Equal(original, provider.Current);
    }

    [Fact]
    public void ReplacedProvidersStayUntouchedUntilTheOriginalProviderReturns()
    {
        var provider = new FakeProvider();
        var original = provider.Current;
        var combat = new BeastmasterMinervaCombat(provider, provider.Abilities);
        Assert.True(combat.TryActivate(out var error), error);
        var owned = provider.Current;
        var replacement = owned with { MovementProvider = new object() };
        provider.Current = replacement;
        provider.Writes.Clear();
        Assert.False(combat.TryRelease(out error));
        Assert.Contains("unloaded or replaced", error);
        Assert.True(combat.OwnsState);
        Assert.False(combat.IsRunning);
        Assert.Equal(replacement, provider.Current);
        Assert.Empty(provider.Writes);
        provider.Current = owned;
        Assert.True(combat.TryRelease(out error), error);
        Assert.False(combat.OwnsState);
        Assert.Equal(original, provider.Current);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Another controller")]
    public void ObservedRevocationNeverReclaimsOrOverwritesTheNewMovementState(string? newOwner)
    {
        var provider = new FakeProvider();
        var original = provider.Current;
        var combat = new BeastmasterMinervaCombat(provider, provider.Abilities);
        Assert.True(combat.TryActivate(out var error), error);
        var revoked = provider.Current with
        {
            Owner = newOwner, DodgeEnabled = false, FaceGazes = false, Preset = "User selection",
        };
        provider.Current = revoked;
        provider.Writes.Clear();
        Assert.False(combat.TryActivate(out error));
        Assert.Contains("revoked", error);
        Assert.False(combat.OwnsState);
        Assert.False(combat.IsRunning);
        Assert.Equal(revoked, provider.Current);
        Assert.DoesNotContain(provider.Writes, write => write is "Claim" or "Release" ||
            write.StartsWith("Movement:", StringComparison.Ordinal) || write.StartsWith("Facing:", StringComparison.Ordinal));
        Assert.Equal(1, provider.Claims);
    }

    [Theory]
    [InlineData("Minerva.IsConnected")]
    [InlineData("Minerva.ReleasePreset")]
    [InlineData("Minerva.SetAutoDodge")]
    [InlineData("BMRVariantLoader.Crewcible.Movement.v1")]
    public void MissingNativeEndpointRefusesDispatchBeforeAnyIpcCall(string missing)
    {
        var ipc = Plugin.PluginInterface;
        var previousInventory = ipc.InstalledPlugins;
        var previousInstances = ECommons.Reflection.DalamudReflector.PluginInstances;
        var previousCall = ipc.IpcCall;
        var previousFunctions = ipc.IpcHasFunction;
        var previousActions = ipc.IpcHasAction;
        try
        {
            ipc.InstalledPlugins = [new("RotationSolver", true), new("Minerva", true)];
            ECommons.Reflection.DalamudReflector.PluginInstances = new Dictionary<string, object?>
            {
                ["RotationSolver"] = new object(), ["Minerva"] = new object(),
            };
            ipc.IpcHasFunction = name => name != missing;
            ipc.IpcHasAction = name => name != missing;
            var calls = 0;
            ipc.IpcCall = (_, _) => { calls++; throw new InvalidOperationException("Unexpected dispatch"); };
            var error = Assert.Throws<InvalidOperationException>(() => NativeProvider().Read());
            Assert.Equal("Minerva IPC is unavailable or incompatible.", error.Message);
            Assert.Equal(0, calls);
        }
        finally
        {
            ipc.InstalledPlugins = previousInventory;
            ECommons.Reflection.DalamudReflector.PluginInstances = previousInstances;
            ipc.IpcCall = previousCall;
            ipc.IpcHasFunction = previousFunctions;
            ipc.IpcHasAction = previousActions;
        }
    }

    [Theory]
    [InlineData("ordinary", 23UL)]
    [InlineData("negative", 17UL)]
    [InlineData("forbidden", 23UL)]
    [InlineData("deprioritized", 23UL)]
    [InlineData("only deprioritized", 17UL)]
    [InlineData("forced", 17UL)]
    [InlineData("priority", 17UL)]
    [InlineData("pull", 17UL)]
    [InlineData("engaged", 23UL)]
    [InlineData("player combat", 23UL)]
    public void NativeTargetsKeepOrdinaryAndLowerPriorityEnemiesWhileHonoringHints(string scenario, ulong expected)
    {
        var minerva = new TargetPlugin();
        var first = new TargetEnemy(17, 0, 8);
        var second = new TargetEnemy(23, 0, 3);
        minerva.ai.Hints.PotentialTargets.AddRange([first, second]);
        var hints = new Dictionary<string, object>();
        switch (scenario)
        {
            case "negative": first.Priority = -1; minerva.ai.Hints.PotentialTargets.Remove(second); break;
            case "forbidden": first.Priority = 10; hints["Minerva.Hints.ForbiddenTargets"] = new ulong[] { 17 }; break;
            case "deprioritized": first.Priority = 10; hints["Minerva.Hints.DeprioritizedTargets"] = new ulong[] { 17 }; break;
            case "only deprioritized":
                hints["Minerva.Hints.DeprioritizedTargets"] = new ulong[] { 17 };
                minerva.ai.Hints.PotentialTargets.Remove(second); break;
            case "forced": hints["Minerva.Hints.ForcedTarget"] = 17UL; break;
            case "priority": hints["Minerva.Hints.PriorityTargets"] = new ulong[] { 17 }; break;
            case "pull": hints["Minerva.Hints.PullTarget"] = 17UL; break;
            case "engaged":
                hints["Minerva.BossEngaged"] = true; first.Actor.InCombat = false; first.Priority = 10; break;
            case "player combat": first.Actor.InCombat = false; first.Priority = 10; break;
        }
        Assert.Equal(new BeastmasterCombat.Target(expected, false),
            ReadNativeTarget(minerva, hints, playerInCombat: scenario == "player combat").Target);
    }

    [Theory]
    [InlineData("dead")]
    [InlineData("destroyed")]
    [InlineData("untargetable")]
    [InlineData("ally")]
    [InlineData("out of range")]
    [InlineData("hard forbidden")]
    [InlineData("fully forbidden")]
    public void NativeTargetsRejectUnsafeActorsEvenWhenForced(string reason)
    {
        var minerva = new TargetPlugin();
        var rejected = new TargetEnemy(17, 10, 2);
        minerva.ai.Hints.PotentialTargets.AddRange([rejected, new TargetEnemy(23, 0, 3)]);
        switch (reason)
        {
            case "dead": rejected.Actor.IsDead = true; break;
            case "destroyed": rejected.Actor.IsDestroyed = true; break;
            case "untargetable": rejected.Actor.IsTargetable = false; break;
            case "ally": rejected.Actor.IsAlly = true; break;
            case "out of range": rejected.Actor.Position = new Vector3(50, 0, 0); break;
            case "hard forbidden": rejected.Priority = int.MinValue + 1; break;
            case "fully forbidden": rejected.Priority = int.MinValue; break;
        }
        var hints = new Dictionary<string, object> { ["Minerva.Hints.ForcedTarget"] = 17UL };
        Assert.Equal(new BeastmasterCombat.Target(23, false), ReadNativeTarget(minerva, hints).Target);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void NativeTargetsRejectAStaleSolutionOrUnavailablePlayer(bool solution, bool player)
    {
        var minerva = new TargetPlugin();
        minerva.ai.HasSolution = solution;
        minerva.ai.Hints.PotentialTargets.Add(new TargetEnemy(17, 0, 2));
        var result = ReadNativeTarget(minerva, playerAvailable: player);
        Assert.Equal(default, result.Target);
        if (!solution) Assert.Equal(0, result.Calls);
    }

    [Fact]
    public void NativeSuspensionConfirmsTheOwnedBackendBeforeDisablingFlagsOrStoppingTheController()
    {
        var minerva = new MovementPlugin();
        var backend = new MovementBackend();
        minerva.movement.Driving = backend;
        backend.OnStop = () =>
        {
            Assert.True(minerva.Config.AutoDodgeEnabled);
            Assert.True(minerva.Config.AutoFaceGazes);
            Assert.Equal(0, minerva.movement.Stops);
        };
        TestNativeMovement(minerva, native => native.SetMovement(false));
        Assert.Equal(1, backend.Stops);
        Assert.Equal(0, backend.Waypoints);
        Assert.False(backend.Steering);
        Assert.False(minerva.Config.AutoDodgeEnabled);
        Assert.False(minerva.Config.AutoFaceGazes);
        Assert.Equal(1, minerva.movement.Stops);
    }

    [Fact]
    public void NativeSuspensionDoesNotStopAnInitiallyForeignBackendRoute()
    {
        var minerva = new MovementPlugin();
        var foreign = new MovementBackend();
        minerva.movement.Driving = foreign;
        minerva.movement.Owns = false;
        minerva.movement.Mode = MovementMode.None;
        TestNativeMovement(minerva, native => native.SetMovement(false));
        Assert.Equal(0, foreign.Stops);
        Assert.Equal(2, foreign.Waypoints);
        Assert.True(foreign.Steering);
        Assert.False(minerva.Config.AutoDodgeEnabled);
        Assert.False(minerva.Config.AutoFaceGazes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeSuspensionRetainsAnUnconfirmedBackendAfterMinervaForgetsItsRoute(bool stopThrows)
    {
        var minerva = new MovementPlugin();
        var backend = new MovementBackend { StopThrows = stopThrows, StopClearsRoute = false };
        minerva.movement.Driving = backend;
        TestNativeMovement(minerva, native =>
        {
            Assert.Throws<InvalidOperationException>(() => native.SetMovement(false));
            Assert.Equal(1, backend.Stops);
            Assert.True(minerva.Config.AutoDodgeEnabled);
            Assert.True(minerva.Config.AutoFaceGazes);
            Assert.Equal(0, minerva.movement.Stops);

            minerva.movement.Mode = MovementMode.None;
            minerva.movement.Owns = false;
            minerva.movement.Driving = null;
            Assert.Throws<InvalidOperationException>(() => native.SetMovement(false));
            Assert.Equal(1, backend.Stops);
            Assert.True(minerva.Config.AutoDodgeEnabled);
            Assert.Equal(0, minerva.movement.Stops);

            backend.Waypoints = 0;
            Assert.Throws<InvalidOperationException>(() => native.SetMovement(false));
            Assert.Equal(1, backend.Stops);
            backend.Steering = false;
            native.SetMovement(false);
            Assert.Equal(1, backend.Stops);
            Assert.False(minerva.Config.AutoDodgeEnabled);
            Assert.False(minerva.Config.AutoFaceGazes);
            Assert.Equal(1, minerva.movement.Stops);
        });
    }

    [Fact]
    public void NativePendingStopDoesNotMaskANewOwnedBackendsFailure()
    {
        var minerva = new MovementPlugin();
        var original = new MovementBackend { StopClearsRoute = false };
        var replacement = new MovementBackend();
        minerva.movement.Driving = original;
        TestNativeMovement(minerva, native =>
        {
            Assert.Throws<InvalidOperationException>(() => native.SetMovement(false));
            minerva.movement.Driving = replacement;
            Assert.Throws<InvalidOperationException>(() => native.SetMovement(false));
            Assert.Equal(1, original.Stops);
            Assert.Equal(0, replacement.Stops);
            Assert.Equal(0, minerva.movement.Stops);
            Assert.True(minerva.Config.AutoDodgeEnabled);
            Assert.True(minerva.Config.AutoFaceGazes);

            original.Waypoints = 0;
            original.Steering = false;
            Assert.Throws<InvalidOperationException>(() => native.SetMovement(false));
            Assert.Equal(0, replacement.Stops);
            Assert.Equal(0, minerva.movement.Stops);
            Assert.True(minerva.Config.AutoDodgeEnabled);

            replacement.StopThrows = true;
            Assert.Throws<InvalidOperationException>(() => native.SetMovement(false));
            Assert.Equal(1, replacement.Stops);
            Assert.Equal(0, minerva.movement.Stops);
            Assert.True(minerva.Config.AutoDodgeEnabled);
            Assert.True(minerva.Config.AutoFaceGazes);

            replacement.StopThrows = false;
            native.SetMovement(false);
            Assert.Equal(1, original.Stops);
            Assert.Equal(2, replacement.Stops);
            Assert.Equal(1, minerva.movement.Stops);
            Assert.False(minerva.Config.AutoDodgeEnabled);
            Assert.False(minerva.Config.AutoFaceGazes);
        });
    }

    private static void TestNativeMovement(MovementPlugin minerva, Action<BeastmasterMinervaCombat.IProvider> verify)
    {
        var ipc = Plugin.PluginInterface;
        var previousInventory = ipc.InstalledPlugins;
        var previousInstances = ECommons.Reflection.DalamudReflector.PluginInstances;
        try
        {
            ipc.InstalledPlugins = [new("Minerva", true)];
            ECommons.Reflection.DalamudReflector.PluginInstances = new Dictionary<string, object?> { ["Minerva"] = minerva };
            verify(NativeProvider());
        }
        finally
        {
            ipc.InstalledPlugins = previousInventory;
            ECommons.Reflection.DalamudReflector.PluginInstances = previousInstances;
        }
    }

    private static (BeastmasterCombat.Target Target, int Calls) ReadNativeTarget(TargetPlugin minerva,
        Dictionary<string, object>? hints = null, bool playerAvailable = true, bool playerInCombat = false)
    {
        var ipc = Plugin.PluginInterface;
        var previousCall = ipc.IpcCall;
        var previousInventory = ipc.InstalledPlugins;
        var previousInstances = ECommons.Reflection.DalamudReflector.PluginInstances;
        var previousPlayer = Plugin.ObjectTable.LocalPlayer;
        var previousCombat = Plugin.Condition[ConditionFlag.InCombat];
        try
        {
            ipc.InstalledPlugins = [new("Minerva", true)];
            ECommons.Reflection.DalamudReflector.PluginInstances = new Dictionary<string, object?> { ["Minerva"] = minerva };
            Plugin.ObjectTable.LocalPlayer = playerAvailable ? new TestPlayer { Position = Vector3.Zero } : null;
            Plugin.Condition[ConditionFlag.InCombat] = playerInCombat;
            var values = new Dictionary<string, object>
            {
                ["Minerva.Hints.ForbiddenTargets"] = Array.Empty<ulong>(),
                ["Minerva.Hints.DeprioritizedTargets"] = Array.Empty<ulong>(),
                ["Minerva.Hints.ForcedTarget"] = 0UL,
                ["Minerva.Hints.PriorityTargets"] = Array.Empty<ulong>(),
                ["Minerva.Hints.PullTarget"] = 0UL,
                ["Minerva.BossEngaged"] = false,
            };
            foreach (var hint in hints ?? []) values[hint.Key] = hint.Value;
            var calls = 0;
            ipc.IpcCall = (name, args) => { Assert.Empty(args); calls++; return values[name]; };
            return (NativeProvider().ReadTarget(), calls);
        }
        finally
        {
            ipc.IpcCall = previousCall;
            ipc.InstalledPlugins = previousInventory;
            ECommons.Reflection.DalamudReflector.PluginInstances = previousInstances;
            Plugin.ObjectTable.LocalPlayer = previousPlayer;
            Plugin.Condition[ConditionFlag.InCombat] = previousCombat;
        }
    }

    private static BeastmasterMinervaCombat.IProvider NativeProvider()
        => (BeastmasterMinervaCombat.IProvider)Activator.CreateInstance(typeof(BeastmasterMinervaCombat)
            .GetNestedType("NativeProvider", BindingFlags.NonPublic)!, nonPublic: true)!;

    // These member shapes supply native facts only; target selection stays in production code.
    private sealed class TargetPlugin { public readonly TargetAi ai = new(); }
    private sealed class TargetAi
    {
        public bool HasSolution { get; set; } = true;
        public TargetHints Hints { get; } = new();
    }
    private sealed class TargetHints { public readonly List<TargetEnemy> PotentialTargets = []; }
    private sealed class TargetEnemy(ulong id, int priority, float distance)
    {
        public readonly TargetActor Actor = new(id) { Position = new Vector3(distance, 0, 0) };
        public int Priority = priority;
    }
    private sealed class TargetActor(ulong id)
    {
        public readonly ulong InstanceID = id;
        public bool IsDead = false, IsDestroyed = false, IsTargetable = true, IsAlly = false, InCombat = true;
        public Vector3 Position { get; set; }
    }

    private sealed class MovementPlugin
    {
        public MovementConfig Config { get; } = new();
        public MovementPresets Presets { get; } = new();
        public readonly MovementController movement = new();
    }
    private sealed class MovementConfig
    {
        public bool AutoDodgeEnabled { get; set; } = true;
        public bool AutoFaceGazes { get; set; } = true;
    }
    private sealed class MovementPresets { public string? Owner { get; set; } = BeastmasterMinervaCombat.OwnerName; }
    private enum MovementMode { None, Follower }
    private sealed class MovementController
    {
        private readonly MovementNavigation nav = new();
        public MovementMode Mode { get; set; } = MovementMode.Follower;
        public bool Owns { get; set; } = true;
        public int Stops;
        public MovementBackend? Driving { get => nav.Driving; set => nav.Driving = value; }
        private bool OwnsFollower() => Owns;
        public void Stop() { Stops++; Mode = MovementMode.None; }
    }
    private sealed class MovementNavigation
    {
        private MovementBackend? driving;
        public MovementBackend? Driving { get => driving; set => driving = value; }
    }
    private sealed class MovementBackend
    {
        public int Stops, Waypoints = 2;
        public bool Steering = true, StopThrows, StopClearsRoute = true;
        public Action? OnStop;
        public Dalamud.Plugin.Ipc.ICallGateSubscriber<object> Stop { get; }
        public Dalamud.Plugin.Ipc.ICallGateSubscriber<int> NumWaypoints { get; }
        public Dalamud.Plugin.Ipc.ICallGateSubscriber<bool>? IsSteering { get; }
        public MovementBackend()
        {
            Stop = new(_ =>
            {
                Stops++; OnStop?.Invoke();
                if (StopThrows) throw new InvalidOperationException("Synthetic backend stop failure");
                if (StopClearsRoute) { Waypoints = 0; Steering = false; }
                return null;
            });
            NumWaypoints = new(_ => Waypoints);
            IsSteering = new(_ => Steering);
        }
    }

    private sealed class FakeProvider : BeastmasterMinervaCombat.IProvider
    {
        public BeastmasterMinervaCombat.State Current = new(
            new object(), Owner: null, DodgeEnabled: true, FaceGazes: false, MovementStopped: true,
            Preset: "Original movement", WithholdAttacks: false);
        public readonly FakeAbilities Abilities;
        public readonly List<string> OwnershipTransitions = [];
        public FakeProvider() => Abilities = new(() => ReadTarget(), OwnershipTransitions);
        public readonly List<string> Writes = [];
        public BeastmasterCombat.Target Target = new(17, true);
        public int Claims, Releases, TargetReads;
        public bool ReadUnavailable, ClaimThrowsAfterOwnership, ReleaseThrowsBeforeMutation, ReleaseThrowsAfterMutation, MovementStopThrows;
        private BeastmasterMinervaCombat.State? beforeClaim;

        public BeastmasterMinervaCombat.State Read()
            => ReadUnavailable ? throw new InvalidOperationException("Provider unavailable") : Current;
        public BeastmasterCombat.Target ReadTarget() { TargetReads++; return Target; }
        public void Claim()
        {
            Writes.Add("Claim"); Claims++;
            beforeClaim = Current;
            Current = Current with { Owner = BeastmasterMinervaCombat.OwnerName, DodgeEnabled = true };
            if (ClaimThrowsAfterOwnership) throw new InvalidOperationException("Synthetic partial claim failure");
        }
        public void SetMovement(bool enabled)
        {
            OwnershipTransitions.Add("Minerva:" + enabled);
            Writes.Add("Movement:" + enabled);
            if (!enabled && MovementStopThrows) throw new InvalidOperationException("Synthetic movement stop failure");
            Current = Current with { DodgeEnabled = enabled, FaceGazes = enabled, MovementStopped = !enabled };
        }
        public void RestoreFacing(bool enabled)
        {
            Writes.Add("Facing:" + enabled);
            Current = Current with { FaceGazes = enabled };
        }
        public void Release()
        {
            Writes.Add("Release"); Releases++;
            if (ReleaseThrowsBeforeMutation) throw new InvalidOperationException("Synthetic release failure");
            var saved = beforeClaim!.Value;
            Current = Current with { Owner = null, DodgeEnabled = saved.DodgeEnabled, Preset = saved.Preset };
            if (ReleaseThrowsAfterMutation) throw new InvalidOperationException("Synthetic post-write release failure");
        }
    }

    private sealed class FakeAbilities(Func<BeastmasterCombat.Target> target, List<string> transitions) : IBeastmasterCombat
    {
        public bool OwnsState { get; private set; }
        public bool IsRunning { get; private set; }
        public bool RequiresNativeMovement { get; set; }
        public bool AllowDamageItems => OwnsState && IsRunning && AllowAttacks && !LeaseRevoked;
        public bool AllowAttacks { get; private set; }
        public bool NativeMovement { get; private set; }
        public bool UseScriptedFamiliars { get; private set; } = true;
        public bool FailSuspension, FailRelease, LeaseRevoked;
        public int Acquisitions, Releases;
        public bool TryActivate(out string reason, bool allowAttacks = true, bool? useBmrMovement = null, bool useScriptedFamiliars = true)
        {
            UseScriptedFamiliars = useScriptedFamiliars;
            NativeMovement = useBmrMovement == true;
            transitions.Add(NativeMovement ? "BMR:movement" : "BMR:actions only");
            if (!OwnsState) Acquisitions++;
            OwnsState = IsRunning = true; AllowAttacks = allowAttacks; reason = ""; return true;
        }
        public bool TrySuspend(out string reason)
        {
            reason = "";
            if (FailSuspension || LeaseRevoked) { reason = "Synthetic action suspension failure"; return false; }
            transitions.Add("BMR:stopped");
            NativeMovement = false;
            if (!OwnsState) Acquisitions++;
            OwnsState = true; IsRunning = AllowAttacks = false; return true;
        }
        public bool TryRelease(out string reason)
        {
            reason = "";
            if (!OwnsState) return true;
            Releases++;
            if (FailRelease) { reason = "Synthetic native release unconfirmed"; return false; }
            transitions.Add("BMR:released");
            OwnsState = IsRunning = AllowAttacks = NativeMovement = LeaseRevoked = false;
            return true;
        }
        public bool TryPrepare(uint board, uint battle, out bool ready, out string status, out string reason)
        { OwnsState = IsRunning = true; ready = true; status = "Prepared"; reason = ""; return true; }
        public BeastmasterCombat.Target ReadTarget() => target();
    }
}
