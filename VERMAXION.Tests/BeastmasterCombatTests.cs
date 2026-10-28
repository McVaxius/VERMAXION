using System;
using System.Collections.Generic;
using System.Runtime.Loader;
using VERMAXION.Beastmaster;
using Xunit;

namespace VERMAXION.Tests
{
    public sealed class BeastmasterCombatTests
    {
        [Theory]
        [InlineData("Range", "MaxRange")]
        [InlineData("Destination", "None")]
        [InlineData("ForbiddenZoneCushion", "None")]
        [InlineData("Range", "Any", true)]
        public void CrewciblePresetRejectsUnsafeOrAmbiguousMovement(string track, string option, bool duplicate = false)
        {
            var provider = new FakeProvider();
            var preset = Newtonsoft.Json.Linq.JObject.Parse(provider.PresetJson);
            var movement = (Newtonsoft.Json.Linq.JArray)preset["Modules"]!["BossMod.Autorotation.MiscAI.NormalMovement"]!;
            var value = movement.Single(value => (string?)value["Track"] == track);
            value["Option"] = option;
            if (duplicate) movement.Add(value.DeepClone());
            provider.PresetJson = preset.ToString();
            Assert.False(BeastmasterCombat.CanStart(provider, out var reason));
            Assert.Contains("unrestricted dodge range", reason);
            Assert.Equal("Original movement", provider.Current.Preset);
        }

        [Fact]
        public void MissingPresetIsCreatedOnceWithoutOverwriteAndPassesExistingValidation()
        {
            var previousIpc = Plugin.PluginInterface.IpcCall;
            try
            {
                string? stored = null;
                var creates = 0;
                var reads = 0;
                Plugin.PluginInterface.IpcCall = (name, args) =>
                {
                    if (name == "BossMod.Presets.Get")
                    {
                        Assert.Equal(BeastmasterCombat.PresetName, Assert.Single(args));
                        reads++;
                        return stored;
                    }
                    Assert.Equal("BossMod.Presets.Create", name);
                    Assert.False(Assert.IsType<bool>(args[1]));
                    creates++;
                    stored = Assert.IsType<string>(args[0]);
                    return true;
                };

                var native = NativePresetProvider();
                var preset = native.ReadPreset();
                Assert.True(BeastmasterCombat.CanStart(new FakeProvider { PresetJson = preset }, out var error), error);
                Assert.Equal(preset, native.ReadPreset());
                Assert.Equal(1, creates);
                Assert.Equal(3, reads);
            }
            finally { Plugin.PluginInterface.IpcCall = previousIpc; }
        }

        [Theory]
        [InlineData("{\"Name\":\"VERMAXION - Crewcible\",\"Modules\":{}}")]
        [InlineData("invalid user preset")]
        public void ExistingPresetIsPreservedAndStillSubjectToValidation(string existing)
        {
            var previousIpc = Plugin.PluginInterface.IpcCall;
            try
            {
                Plugin.PluginInterface.IpcCall = (name, args) =>
                {
                    Assert.Equal("BossMod.Presets.Get", name);
                    return existing;
                };
                Assert.Equal(existing, NativePresetProvider().ReadPreset());
                Assert.False(BeastmasterCombat.CanStart(new FakeProvider { PresetJson = existing }, out _));
            }
            finally { Plugin.PluginInterface.IpcCall = previousIpc; }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void CreationWithoutReadbackKeepsTheMissingPresetError(bool creationResult)
        {
            var previousIpc = Plugin.PluginInterface.IpcCall;
            try
            {
                var creates = 0;
                Plugin.PluginInterface.IpcCall = (name, args) =>
                {
                    if (name == "BossMod.Presets.Get") return null;
                    Assert.Equal("BossMod.Presets.Create", name);
                    creates++;
                    return creationResult;
                };
                var error = Assert.Throws<InvalidOperationException>(() => NativePresetProvider().ReadPreset());
                Assert.Equal("BossMod could not confirm the 'VERMAXION - Crewcible' preset after injection.", error.Message);
                Assert.Equal(1, creates);
            }
            finally { Plugin.PluginInterface.IpcCall = previousIpc; }
        }

        [Fact]
        public void ConcurrentPresetCreationUsesTheStoredCopyWithoutOverwrite()
        {
            var previousIpc = Plugin.PluginInterface.IpcCall;
            try
            {
                string? stored = null;
                const string concurrent = "another plugin's existing preset";
                Plugin.PluginInterface.IpcCall = (name, args) =>
                {
                    if (name == "BossMod.Presets.Get") return stored;
                    Assert.Equal("BossMod.Presets.Create", name);
                    Assert.False(Assert.IsType<bool>(args[1]));
                    stored = concurrent;
                    return false;
                };
                Assert.Equal(concurrent, NativePresetProvider().ReadPreset());
            }
            finally { Plugin.PluginInterface.IpcCall = previousIpc; }
        }

        [Fact]
        public void UnavailablePresetReadDoesNotAttemptCreation()
        {
            var previousIpc = Plugin.PluginInterface.IpcCall;
            try
            {
                Plugin.PluginInterface.IpcCall = (name, args) =>
                {
                    Assert.Equal("BossMod.Presets.Get", name);
                    throw new InvalidOperationException("IPC is unavailable");
                };
                var error = Assert.Throws<InvalidOperationException>(() => NativePresetProvider().ReadPreset());
                Assert.Equal("IPC is unavailable", error.Message);
            }
            finally { Plugin.PluginInterface.IpcCall = previousIpc; }
        }

        private static BeastmasterCombat.IProvider NativePresetProvider()
            => (BeastmasterCombat.IProvider)Activator.CreateInstance(typeof(BeastmasterCombat)
                .GetNestedType("NativeProvider", System.Reflection.BindingFlags.NonPublic)!, nonPublic: true)!;

        [Theory]
        [InlineData("RotationSolver", false, false, "RotationSolver is not loaded.")]
        [InlineData("BossModReborn", false, false, "BossModReborn is not loaded.")]
        [InlineData("RotationSolver", true, false, "RotationSolver is loaded, but its runtime state could not be read.")]
        [InlineData("BossModReborn", true, false, "BossModReborn is loaded, but its runtime state could not be read.")]
        [InlineData("RotationSolver", true, true, "RotationSolver is loaded, but its runtime state could not be read.")]
        [InlineData("BossModReborn", true, true, "BossModReborn is loaded, but its runtime state could not be read.")]
        public void NativeDiscoveryDistinguishesMissingProvidersFromUnreadableRuntime(
            string name, bool loaded, bool reflectorReturnsNull, string expected)
        {
            var previousInventory = Plugin.PluginInterface.InstalledPlugins;
            var previousInstances = ECommons.Reflection.DalamudReflector.PluginInstances;
            try
            {
                Plugin.PluginInterface.InstalledPlugins = [
                    new("RotationSolver", name != "RotationSolver" || loaded),
                    new("BossModReborn", name != "BossModReborn" || loaded),
                    new("BossMod", false),
                ];
                var instances = new Dictionary<string, object?>();
                if (name == "BossModReborn") instances["RotationSolver"] = new object();
                if (reflectorReturnsNull) instances[name] = null;
                ECommons.Reflection.DalamudReflector.PluginInstances = instances;

                Assert.False(BeastmasterCombat.CanStart(out var error));
                Assert.Equal(expected, error);
            }
            finally
            {
                Plugin.PluginInterface.InstalledPlugins = previousInventory;
                ECommons.Reflection.DalamudReflector.PluginInstances = previousInstances;
            }
        }

        [Fact]
        public void DeathResumeAndPartialFailurePreserveOriginalStateAndUnresolvedOwnership()
        {
            var provider = new FakeProvider();
            var combat = new BeastmasterCombat(provider);
            Assert.True(combat.TrySuspend(out var error), error);
            Assert.True(combat.OwnsState);
            Assert.False(combat.IsRunning);
            Assert.Equal(BeastmasterCombat.Mode.Off, provider.Current.Mode);
            Assert.True(provider.Current.ForceDisabled);

            Assert.True(combat.TryActivate(out error), error);
            Assert.True(combat.IsRunning);
            provider.Current = provider.Current with
            {
                Mode = BeastmasterCombat.Mode.Off, Preset = "", ForceDisabled = true,
            };
            Assert.True(combat.TrySuspend(out error), error);
            Assert.True(combat.TryActivate(out error), error);
            Assert.Equal(BeastmasterCombat.Mode.Henched, provider.Current.Mode);
            Assert.Equal(BeastmasterCombat.PresetName, provider.Current.Preset);
            Assert.True(combat.TryRelease(out error), error);
            Assert.False(combat.OwnsState);
            Assert.Equal(BeastmasterCombat.Mode.Auto, provider.Current.Mode);
            Assert.Equal("Original movement", provider.Current.Preset);

            // The second provider mutates, then throws: restore both, using the original snapshot.
            provider.FailAfterStartingRotation = true;
            Assert.False(combat.TryActivate(out error));
            Assert.Contains("synthetic", error);
            Assert.False(combat.OwnsState);
            Assert.Equal(BeastmasterCombat.Mode.Auto, provider.Current.Mode);
            Assert.Equal("Original movement", provider.Current.Preset);

            Assert.True(combat.TryActivate(out error), error);
            provider.FailRestoringMovement = true;
            Assert.False(combat.TryRelease(out error));
            Assert.True(combat.OwnsState);
            Assert.Equal(BeastmasterCombat.Mode.Auto, provider.Current.Mode);
            Assert.Equal(BeastmasterCombat.PresetName, provider.Current.Preset);
            provider.FailRestoringMovement = false;
            provider.Current = provider.Current with { Preset = "Another owner" };
            Assert.True(combat.TryRelease(out error), error);
            Assert.False(combat.OwnsState);
            Assert.Equal("Original movement", provider.Current.Preset);
            Assert.Empty(provider.AiTransitions);

            var original = provider.Current;
            foreach (var replaceRotation in new[] { true, false })
            {
                Assert.True(combat.TryActivate(out error), error);
                var owned = provider.Current;
                var replacement = replaceRotation
                    ? owned with { RotationProvider = new object() }
                    : owned with { MovementProvider = new object() };
                provider.Current = replacement;
                Assert.False(combat.TryRelease(out error));
                Assert.Contains("unloaded or replaced", error);
                Assert.True(combat.OwnsState);
                Assert.False(combat.IsRunning);
                Assert.Equal(replacement, provider.Current);
                Assert.Empty(provider.AiTransitions);

                provider.Current = owned;
                Assert.True(combat.TryRelease(out error), error);
                Assert.False(combat.OwnsState);
                Assert.Equal(original, provider.Current);
            }
        }

        [Fact]
        public void StartSuspendsExistingAiAndStopRestoresItAcrossPartialFailures()
        {
            var provider = new FakeProvider();
            provider.Current = provider.Current with { AiActive = true, AiMasterSlot = 3 };
            Assert.True(BeastmasterCombat.CanStart(provider, out var error), error);
            Assert.True(provider.Current.AiActive);
            Assert.Empty(provider.AiTransitions); // Availability/drawing never changes runtime state.

            var combat = new BeastmasterCombat(provider);
            Assert.True(combat.TrySuspend(out error), error);
            Assert.False(provider.Current.AiActive);
            Assert.True(provider.Current.ForceDisabled);
            Assert.True(combat.TrySuspend(out error), error);
            Assert.Single(provider.AiTransitions);
            Assert.True(combat.TryActivate(out error), error);
            Assert.Equal(BeastmasterCombat.Mode.Henched, provider.Current.Mode);
            Assert.Equal(BeastmasterCombat.PresetName, provider.Current.Preset);
            Assert.False(provider.Current.AiActive);
            Assert.True(combat.TryRelease(out error), error);
            Assert.True(provider.Current.AiActive);
            Assert.Equal(3, provider.Current.AiMasterSlot);
            Assert.Equal(BeastmasterCombat.Mode.Auto, provider.Current.Mode);
            Assert.Equal("Original movement", provider.Current.Preset);
            Assert.True(combat.TryRelease(out error), error);
            Assert.Equal(2, provider.AiTransitions.Count);

            // Native AI shutdown can clear its behavior/preset before reporting an error.
            provider.FailAfterStoppingAi = true;
            Assert.False(combat.TrySuspend(out error));
            Assert.False(combat.OwnsState);
            Assert.True(provider.Current.AiActive);
            Assert.Equal(3, provider.Current.AiMasterSlot);
            Assert.Equal("Original movement", provider.Current.Preset);

            Assert.True(combat.TrySuspend(out error), error);
            provider.FailRestoringAi = true;
            Assert.False(combat.TryRelease(out error));
            Assert.True(combat.OwnsState);
            Assert.False(provider.Current.AiActive);
            provider.FailRestoringAi = false;
            Assert.True(combat.TryRelease(out error), error);
            Assert.False(combat.OwnsState);
            Assert.True(provider.Current.AiActive);
            Assert.Equal(3, provider.Current.AiMasterSlot);
            // Restarting AI clears the manual preset again; restoration must follow it.
            Assert.Equal("Original movement", provider.Current.Preset);

            Assert.True(combat.TrySuspend(out error), error);
            provider.Current = provider.Current with { AiActive = true, AiMasterSlot = 6 };
            Assert.True(combat.TrySuspend(out error), error);
            Assert.True(combat.OwnsState);
            Assert.False(provider.Current.AiActive);
            Assert.True(provider.Current.ForceDisabled);
            Assert.True(combat.TryRelease(out error), error);
            Assert.Equal(3, provider.Current.AiMasterSlot);
            Assert.Equal("Original movement", provider.Current.Preset);

            Assert.True(combat.TrySuspend(out error), error);
            provider.Current = provider.Current with { Preset = "Another owner", ForceDisabled = false };
            Assert.True(combat.TryActivate(out error), error);
            Assert.True(combat.OwnsState);
            Assert.False(provider.Current.AiActive);
            Assert.Equal(BeastmasterCombat.PresetName, provider.Current.Preset);
            Assert.True(combat.TryRelease(out error), error);
            Assert.True(provider.Current.AiActive);
            Assert.Equal(3, provider.Current.AiMasterSlot);
            Assert.Equal("Original movement", provider.Current.Preset);
        }

        [Theory]
        [InlineData((int)BeastmasterCombat.Mode.Off)]
        [InlineData((int)BeastmasterCombat.Mode.Manual)]
        public void StartSurvivesAiShutdownAndReclaimsDriftWithoutReplacingTheOriginalSnapshot(int modeAfterAiStops)
        {
            var provider = new FakeProvider { ModeAfterStoppingAi = (BeastmasterCombat.Mode)modeAfterAiStops };
            provider.Current = provider.Current with { AiActive = true, AiMasterSlot = 3 };
            var original = provider.Current;
            var combat = new BeastmasterCombat(provider);

            Assert.True(BeastmasterCombat.CanStart(provider, out var error), error);
            Assert.Equal(original, provider.Current);
            Assert.True(combat.TrySuspend(out error), error);
            Assert.True(combat.OwnsState);
            Assert.False(combat.IsRunning);
            Assert.Equal(BeastmasterCombat.Mode.Off, provider.Current.Mode);
            Assert.False(provider.Current.AiActive);
            Assert.True(provider.Current.ForceDisabled);

            // Navigation must stay suspended even if another runtime enables its AI or mode.
            provider.Current = provider.Current with
            {
                Mode = BeastmasterCombat.Mode.AutoDuty, AiActive = true, AiMasterSlot = 6,
                Preset = "Changed during travel", ForceDisabled = false,
            };
            Assert.True(combat.TrySuspend(out error), error);
            Assert.False(combat.IsRunning);
            Assert.Equal(BeastmasterCombat.Mode.Off, provider.Current.Mode);
            Assert.False(provider.Current.AiActive);
            Assert.True(provider.Current.ForceDisabled);
            Assert.True(combat.TryActivate(out error), error);
            Assert.True(combat.IsRunning);
            Assert.Equal(BeastmasterCombat.Mode.Henched, provider.Current.Mode);
            Assert.Equal(BeastmasterCombat.PresetName, provider.Current.Preset);

            provider.Current = provider.Current with
            {
                Mode = BeastmasterCombat.Mode.PvP, AiActive = true, AiMasterSlot = 5,
                Preset = "Changed during combat", ForceDisabled = false,
            };
            Assert.True(combat.TryActivate(out error), error);
            Assert.True(combat.IsRunning);
            Assert.Equal(BeastmasterCombat.Mode.Henched, provider.Current.Mode);
            Assert.False(provider.Current.AiActive);
            Assert.Equal(BeastmasterCombat.PresetName, provider.Current.Preset);
            Assert.False(provider.Current.ForceDisabled);

            // Lifecycle auto-off is recoverable too; it must not become the saved original mode.
            provider.Current = provider.Current with { Mode = BeastmasterCombat.Mode.Off };
            Assert.True(combat.TryActivate(out error), error);
            Assert.Equal(BeastmasterCombat.Mode.Henched, provider.Current.Mode);
            Assert.True(combat.TryRelease(out error), error);
            Assert.False(combat.OwnsState);
            Assert.False(combat.IsRunning);
            Assert.Equal(original, provider.Current);
        }

        [Theory]
        [InlineData((int)BeastmasterCombat.Mode.AutoDuty)]
        [InlineData((int)BeastmasterCombat.Mode.Henched)]
        [InlineData((int)BeastmasterCombat.Mode.PvP)]
        public void StartReclaimsAutomationModesAndStopRestoresThem(int originalMode)
        {
            var provider = new FakeProvider { ModeAfterStoppingAi = BeastmasterCombat.Mode.Off };
            provider.Current = provider.Current with { Mode = (BeastmasterCombat.Mode)originalMode };
            var original = provider.Current;
            Assert.True(BeastmasterCombat.CanStart(provider, out var error), error);
            Assert.Equal(original, provider.Current);
            var combat = new BeastmasterCombat(provider);
            Assert.True(combat.TrySuspend(out error), error);
            Assert.Equal(BeastmasterCombat.Mode.Off, provider.Current.Mode);
            Assert.True(provider.Current.ForceDisabled);
            Assert.True(combat.TryActivate(out error), error);
            provider.Current = provider.Current with
            {
                Mode = BeastmasterCombat.Mode.Manual, AiActive = true, AiMasterSlot = 4,
                Preset = "Enabled after Start", ForceDisabled = false,
            };
            Assert.True(combat.TryActivate(out error), error);
            Assert.Equal(BeastmasterCombat.Mode.Henched, provider.Current.Mode);
            Assert.False(provider.Current.AiActive);
            Assert.Equal(BeastmasterCombat.PresetName, provider.Current.Preset);
            // Stop must restore the original mode after an AI shutdown changes it again.
            provider.Current = provider.Current with
            {
                AiActive = true, AiMasterSlot = 7, Preset = "Changed before Stop", ForceDisabled = false,
            };
            Assert.True(combat.TryRelease(out error), error);
            Assert.False(combat.OwnsState);
            Assert.Equal(original, provider.Current);
        }

        [Fact]
        public void StartClearsRuntimeOverridesAndRestoresOriginalValuesAfterDrift()
        {
            var provider = new FakeProvider();
            provider.Current = provider.Current with
            {
                ExternalOverrides = true, IpcOverrides = new object(), TargetingTypeOverride = 3,
            };
            var original = provider.Current;
            Assert.True(BeastmasterCombat.CanStart(provider, out var error), error);
            Assert.Equal(original, provider.Current);
            var combat = new BeastmasterCombat(provider);
            Assert.True(combat.TrySuspend(out error), error);
            Assert.Equal(BeastmasterCombat.Mode.Off, provider.Current.Mode);
            Assert.False(provider.Current.ExternalOverrides);
            Assert.Null(provider.Current.IpcOverrides);
            Assert.Null(provider.Current.TargetingTypeOverride);

            provider.Current = provider.Current with
            {
                ExternalOverrides = true, IpcOverrides = new object(), TargetingTypeOverride = 1,
            };
            Assert.True(combat.TryActivate(out error), error);
            Assert.Equal(BeastmasterCombat.Mode.Henched, provider.Current.Mode);
            Assert.False(provider.Current.ExternalOverrides);
            Assert.True(combat.TryRelease(out error), error);
            Assert.Equal(original, provider.Current);
            Assert.False(combat.OwnsState);
        }

        [Fact]
        public void WithholdingAttacksKeepsMovementAndRunOwnershipThenResumesAndRestores()
        {
            var provider = new FakeProvider();
            provider.Current = provider.Current with
            {
                AiActive = true, AiMasterSlot = 3, ExternalOverrides = true,
                IpcOverrides = new object(), TargetingTypeOverride = 2,
            };
            var original = provider.Current;
            var combat = new BeastmasterCombat(provider);

            Assert.True(combat.TryActivate(out var error), error);
            Assert.Equal(BeastmasterCombat.Mode.Henched, provider.Current.Mode);
            Assert.True(combat.TryActivate(out error, allowAttacks: false), error);
            Assert.True(combat.IsRunning);
            Assert.True(combat.OwnsState);
            Assert.Equal(BeastmasterCombat.Mode.Off, provider.Current.Mode);
            Assert.Equal(BeastmasterCombat.PresetName, provider.Current.Preset);
            Assert.False(provider.Current.ForceDisabled);
            Assert.False(provider.Current.AiActive);

            // A repeated hold retains movement and never replaces the pre-Start snapshot.
            Assert.True(combat.TryActivate(out error, allowAttacks: false), error);
            Assert.True(combat.IsRunning);
            Assert.True(combat.OwnsState);
            Assert.Equal(BeastmasterCombat.PresetName, provider.Current.Preset);
            Assert.False(provider.Current.ForceDisabled);
            Assert.True(combat.TryActivate(out error), error);
            Assert.Equal(BeastmasterCombat.Mode.Henched, provider.Current.Mode);
            Assert.True(combat.IsRunning);
            Assert.True(combat.TryRelease(out error), error);
            Assert.False(combat.IsRunning);
            Assert.False(combat.OwnsState);
            Assert.Equal(original, provider.Current);
        }

        [Fact]
        public void NativeTargetReadUsesBossModResolverAndFreshEncounterState()
        {
            var previousInventory = Plugin.PluginInterface.InstalledPlugins;
            var previousInstances = ECommons.Reflection.DalamudReflector.PluginInstances;
            var previousAi = BossMod.AI.AIManager.Instance;
            try
            {
                Plugin.PluginInterface.InstalledPlugins = [new("BossModReborn", true)];
                ECommons.Reflection.DalamudReflector.PluginInstances = new Dictionary<string, object?>
                {
                    ["BossModReborn"] = this,
                };
                var ai = new BossMod.AI.AIManager();
                BossMod.AI.AIManager.Instance = ai;
                var combat = new BeastmasterCombat();

                // Before an encounter starts, the route can still pull a present enemy.
                Assert.Equal(new BeastmasterCombat.Target(0, true), combat.ReadTarget());
                ai.Autorot.SelectedTarget = new BossMod.AI.TargetActorFixture(17);
                Assert.Equal(new BeastmasterCombat.Target(17, true), combat.ReadTarget());

                var module = new BossMod.AI.EncounterModuleFixture();
                ai.Autorot.Bossmods.ActiveModule = module;
                Assert.Equal(new BeastmasterCombat.Target(17, true), combat.ReadTarget());
                module.StateMachine.ActivePhase = new object();
                ai.Autorot.SelectedTarget = new BossMod.AI.TargetActorFixture(23);
                Assert.Equal(new BeastmasterCombat.Target(23, false), combat.ReadTarget());

                // A null decision during an active fight must not become a nearest-target fallback.
                ai.Autorot.SelectedTarget = null;
                Assert.Equal(new BeastmasterCombat.Target(0, false), combat.ReadTarget());
                module.StateMachine.ActivePhase = null;
                Assert.Equal(new BeastmasterCombat.Target(0, true), combat.ReadTarget());
                Assert.Equal(6, ai.Autorot.ResolverCalls.Count);
                Assert.All(ai.Autorot.ResolverCalls, call =>
                {
                    Assert.Equal(BossMod.AI.TargetStrategyFixture.EnemyWithHighestPriority, call.Strategy);
                    Assert.Equal(0, call.Selection);
                });
            }
            finally
            {
                BossMod.AI.AIManager.Instance = previousAi;
                Plugin.PluginInterface.InstalledPlugins = previousInventory;
                ECommons.Reflection.DalamudReflector.PluginInstances = previousInstances;
            }
        }

        [Fact]
        public void NativeTargetReadWithoutBossModPlayerSuppressesPullAndSkipsResolver()
        {
            var previousInventory = Plugin.PluginInterface.InstalledPlugins;
            var previousInstances = ECommons.Reflection.DalamudReflector.PluginInstances;
            var previousAi = BossMod.AI.AIManager.Instance;
            try
            {
                Plugin.PluginInterface.InstalledPlugins = [new("BossModReborn", true)];
                ECommons.Reflection.DalamudReflector.PluginInstances = new Dictionary<string, object?>
                {
                    ["BossModReborn"] = this,
                };
                var ai = new BossMod.AI.AIManager();
                ai.Autorot.Player = null;
                ai.Autorot.SelectedTarget = new BossMod.AI.TargetActorFixture(17);
                BossMod.AI.AIManager.Instance = ai;

                Assert.Equal(default(BeastmasterCombat.Target), new BeastmasterCombat().ReadTarget());
                Assert.Empty(ai.Autorot.ResolverCalls);
            }
            finally
            {
                BossMod.AI.AIManager.Instance = previousAi;
                Plugin.PluginInterface.InstalledPlugins = previousInventory;
                ECommons.Reflection.DalamudReflector.PluginInstances = previousInstances;
            }
        }

        private sealed class FakeProvider : BeastmasterCombat.IProvider
        {
            public BeastmasterCombat.State Current = new(new object(), new object(), BeastmasterCombat.Mode.Auto,
                BeastmasterCombat.RotationName, false, false, "Original movement", false);
            public bool FailAfterStartingRotation;
            public bool FailRestoringMovement;
            public bool FailAfterStoppingAi;
            public bool FailRestoringAi;
            public BeastmasterCombat.Mode? ModeAfterStoppingAi;
            public readonly List<(bool Enabled, int MasterSlot)> AiTransitions = [];
            public BeastmasterCombat.State Read() => Current;
            public BeastmasterCombat.Target ReadTarget() => default;
            public string PresetJson = """
                {"Name":"VERMAXION - Crewcible","Modules":{
                  "BossMod.Autorotation.MiscAI.StayCloseToTarget":[{"Track":"range","Option":"2.3"}],
                  "BossMod.Autorotation.MiscAI.NormalMovement":[{"Track":"Destination","Option":"Pathfind"},{"Track":"Range","Option":"Any"},{"Track":"ForbiddenZoneCushion","Option":"Medium"}]
                }}
                """;
            public string ReadPreset() => PresetJson;
            public void SetMode(BeastmasterCombat.Mode mode)
            {
                Current = Current with
                {
                    Mode = mode, ExternalOverrides = false, IpcOverrides = null, TargetingTypeOverride = null,
                };
                if (mode == BeastmasterCombat.Mode.Henched && FailAfterStartingRotation)
                {
                    FailAfterStartingRotation = false;
                    throw new InvalidOperationException("synthetic post-write failure");
                }
            }
            public void RestoreRotationOverrides(BeastmasterCombat.State saved)
            {
                Current = Current with
                {
                    ExternalOverrides = saved.ExternalOverrides, IpcOverrides = saved.IpcOverrides,
                    TargetingTypeOverride = saved.TargetingTypeOverride,
                };
            }
            public void SetPreset(string? name, bool forceDisabled)
            {
                if (name == "Original movement" && FailRestoringMovement)
                    throw new InvalidOperationException("synthetic restoration failure");
                Current = Current with { Preset = name, ForceDisabled = forceDisabled };
            }
            public void SetAiEnabled(bool enabled, int masterSlot)
            {
                if (enabled && FailRestoringAi)
                    throw new InvalidOperationException("synthetic AI restoration failure");
                AiTransitions.Add((enabled, masterSlot));
                Current = Current with
                {
                    AiActive = enabled, AiMasterSlot = enabled ? masterSlot : 0,
                    Preset = null, ForceDisabled = false,
                };
                if (!enabled && ModeAfterStoppingAi is { } mode)
                    Current = Current with { Mode = mode };
                if (!enabled && FailAfterStoppingAi)
                {
                    FailAfterStoppingAi = false;
                    throw new InvalidOperationException("synthetic partial AI suspension failure");
                }
            }
        }
    }
}

// Public member shapes needed by the native reflection bridge. Target decisions are
// supplied by each test; this fixture deliberately implements no BMR selection rules.
namespace BossMod.AI
{
    internal sealed class AIManager
    {
        public static AIManager? Instance;
        public readonly AutorotationFixture Autorot = new();
    }

    internal enum TargetStrategyFixture { None, EnemyWithHighestPriority }

    internal sealed class AutorotationFixture
    {
        public object? Player { get; set; } = new object();
        public readonly BossModuleManagerFixture Bossmods = new();
        public TargetActorFixture? SelectedTarget { get; set; }
        public readonly List<(TargetStrategyFixture Strategy, int Selection)> ResolverCalls = [];

        public TargetActorFixture? ResolveTargetOverride(TargetStrategyFixture strategy, int selection)
        {
            ResolverCalls.Add((strategy, selection));
            return SelectedTarget;
        }
    }

    internal sealed class BossModuleManagerFixture
    {
        public EncounterModuleFixture? ActiveModule { get; set; }
    }

    internal sealed class EncounterModuleFixture
    {
        public readonly StateMachineFixture StateMachine = new();
    }

    internal sealed class StateMachineFixture
    {
        public object? ActivePhase { get; set; }
    }

    internal sealed class TargetActorFixture(ulong instanceId)
    {
        public readonly ulong InstanceID = instanceId;
    }
}

// No live providers are loaded. Existing test IPC fakes lack this action overload.
namespace VERMAXION.Beastmaster
{
    internal static class BeastmasterCombatTestIpcAction
    {
        public static Dalamud.Plugin.Ipc.ICallGateSubscriber<T1, T> GetIpcSubscriber<T1, T>(
            this VERMAXION.TestPluginInterface plugin, string name)
            => new(args => plugin.IpcCall!(name, args));

        public static void InvokeAction<T1, T>(this Dalamud.Plugin.Ipc.ICallGateSubscriber<T1, T> subscriber, T1 value)
            => subscriber.InvokeFunc(value);
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
