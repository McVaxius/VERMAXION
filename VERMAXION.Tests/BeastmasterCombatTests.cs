using System;
using System.Collections.Generic;
using System.Runtime.Loader;
using VERMAXION.Beastmaster;
using Xunit;

namespace VERMAXION.Tests
{
    public sealed class BeastmasterCombatTests
    {
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
            public string ReadPreset() => """
                {"Name":"passive - melee","Modules":{
                  "BossMod.Autorotation.MiscAI.StayCloseToTarget":[{"Track":"range","Option":"2.3"}],
                  "BossMod.Autorotation.MiscAI.StayWithinLeylines":[],
                  "BossMod.Autorotation.MiscAI.GoToPositional":[],
                  "BossMod.Autorotation.MiscAI.StayCloseToPartyRole":[],
                  "BossMod.Autorotation.MiscAI.NormalMovement":[{"Track":"Destination","Option":"Pathfind"}]
                }}
                """;
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

// Native provider boundaries are never invoked by this test. Existing test IPC fakes lack this action overload.
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
        public static bool TryGetDalamudPlugin(string name, out object plugin, out AssemblyLoadContext? context,
            bool suppressErrors, bool ignoreCache)
        { plugin = null!; context = null; return false; }
    }
}
