using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using ECommons.Reflection;
using Newtonsoft.Json.Linq;

namespace VERMAXION.Beastmaster;

// A run-local lease. No provider configuration or preset database is changed.
internal sealed class BeastmasterCombat
{
    internal const string PresetName = "passive - melee";
    internal const string RotationName = "RotationSolver.RebornRotations.Melee.BST_Reborn";
    internal enum Mode : byte { Off, Auto, TargetOnly, Manual, AutoDuty, Henched, PvP }
    internal readonly record struct Target(ulong InstanceId, bool CanPull);
    internal readonly record struct State(object RotationProvider, object MovementProvider, Mode Mode,
        string? Rotation, bool ExternalOverrides, bool AiActive, string? Preset, bool ForceDisabled)
    {
        public int AiMasterSlot { get; init; }
        public object? IpcOverrides { get; init; }
        public object? TargetingTypeOverride { get; init; }
    }

    internal interface IProvider
    {
        State Read();
        Target ReadTarget();
        string ReadPreset();
        void SetMode(Mode mode);
        void RestoreRotationOverrides(State saved);
        void SetPreset(string? name, bool forceDisabled);
        void SetAiEnabled(bool enabled, int masterSlot);
    }

    private readonly IProvider provider;
    private State? original;
    private bool rotationChanged, movementChanged, aiChanged;
    public bool OwnsState => original.HasValue;
    public bool IsRunning { get; private set; }

    public BeastmasterCombat() : this(new NativeProvider()) { }
    internal BeastmasterCombat(IProvider provider) => this.provider = provider;

    public static bool CanStart(out string reason)
        => CanStart(new NativeProvider(), out reason);

    internal static bool CanStart(IProvider provider, out string reason)
    {
        try { Validate(provider); reason = string.Empty; return true; }
        catch (Exception ex) { reason = Describe(ex); return false; }
    }

    public bool TryActivate(out string reason, bool allowAttacks = true) => TryApply(true, out reason, allowAttacks);
    public bool TrySuspend(out string reason) => TryApply(false, out reason);
    internal Target ReadTarget() => provider.ReadTarget();

    private bool TryApply(bool running, out string reason, bool allowAttacks = true)
    {
        try
        {
            if (original == null)
            {
                original = Validate(provider);
                rotationChanged = movementChanged = aiChanged = true;
            }

            var current = provider.Read();
            CheckProviders(current);
            if (running && current.Rotation != RotationName)
                throw new InvalidOperationException("RSR Reborn Beastmaster is no longer the current rotation.");

            // Start authorizes control until Stop. Runtime modes and preset selections
            // are state to reconcile, not ownership tokens that can abort the run.
            if (!running || !allowAttacks) SetRotation(Mode.Off);
            current = provider.Read();
            CheckProviders(current);
            if (current.AiActive)
            {
                // SwitchToIdle clears the manual preset, including on partial failure.
                aiChanged = movementChanged = true;
                provider.SetAiEnabled(false, current.AiMasterSlot);
                current = provider.Read();
                CheckProviders(current);
                if (current.AiActive)
                    throw new InvalidOperationException("BossMod AI suspension was not confirmed.");
            }

            // Establish movement before abilities on activation. Reassert Off after
            // the AI handoff too, since provider lifecycle events can change RSR.
            if (running)
            {
                SetMovement(PresetName, false);
                SetRotation(allowAttacks ? Mode.Henched : Mode.Off);
            }
            else
            {
                SetRotation(Mode.Off);
                SetMovement(string.Empty, true);
            }
            IsRunning = running;
            reason = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            IsRunning = false;
            reason = Describe(ex);
            // A failed acquisition/transition must also undo any earlier successful provider write.
            if (OwnsState && !TryRelease(out var cleanup)) reason += " Cleanup: " + cleanup;
            return false;
        }
    }

    private void SetRotation(Mode mode)
    {
        var current = provider.Read();
        CheckProviders(current);
        rotationChanged = true; // Restore the pre-Start mode even after an automatic Off.
        if (current.Mode == mode && !current.ExternalOverrides) return;
        provider.SetMode(mode);
        current = provider.Read();
        CheckProviders(current);
        if (current.Mode != mode || current.ExternalOverrides)
            throw new InvalidOperationException("RSR did not confirm the requested Beastmaster operating mode.");
    }

    private void SetMovement(string? name, bool forceDisabled)
    {
        var current = provider.Read();
        CheckProviders(current);
        if (current.AiActive) throw new InvalidOperationException("BossMod AI suspension was not confirmed.");
        movementChanged = true;
        if (MatchesMovement(current, name, forceDisabled)) return;
        provider.SetPreset(name, forceDisabled);
        current = provider.Read();
        CheckProviders(current);
        if (current.AiActive || !MatchesMovement(current, name, forceDisabled))
            throw new InvalidOperationException("BossMod did not confirm the requested Beastmaster movement preset.");
    }

    public bool TryRelease(out string reason)
    {
        IsRunning = false;
        reason = string.Empty;
        if (original is not { } saved) return true;
        if (!rotationChanged && !movementChanged && !aiChanged) { original = null; return true; }
        State current;
        try { current = provider.Read(); CheckProviders(current); }
        catch (Exception ex) { reason = Describe(ex); return false; }

        if (aiChanged)
        {
            try
            {
                current = provider.Read(); CheckProviders(current);
                if (current.AiActive != saved.AiActive || saved.AiActive && current.AiMasterSlot != saved.AiMasterSlot)
                {
                    // SwitchToFollow clears the manual preset too; restore it afterward.
                    rotationChanged = movementChanged = true;
                    provider.SetAiEnabled(saved.AiActive, saved.AiMasterSlot);
                }
                current = provider.Read(); CheckProviders(current);
                if (current.AiActive != saved.AiActive || saved.AiActive && current.AiMasterSlot != saved.AiMasterSlot)
                    throw new InvalidOperationException("BossMod AI original state restoration is unconfirmed.");
                aiChanged = false;
            }
            catch (Exception ex) { reason += (reason.Length == 0 ? "" : " ") + Describe(ex); }
        }
        if (movementChanged)
        {
            try
            {
                current = provider.Read(); CheckProviders(current);
                if (!MatchesMovement(current, saved.Preset, saved.ForceDisabled))
                    provider.SetPreset(saved.Preset, saved.ForceDisabled);
                current = provider.Read(); CheckProviders(current);
                if (current.AiActive && (!saved.AiActive || aiChanged || current.AiMasterSlot != saved.AiMasterSlot) ||
                    !MatchesMovement(current, saved.Preset, saved.ForceDisabled))
                    throw new InvalidOperationException("BossMod original preset restoration is unconfirmed.");
                movementChanged = false;
            }
            catch (Exception ex) { reason += (reason.Length == 0 ? "" : " ") + Describe(ex); }
        }
        // AI lifecycle operations can affect RSR too. Restore rotation last and
        // retain its claim if a later cleanup attempt must restore AI again.
        if (rotationChanged)
        {
            try
            {
                current = provider.Read(); CheckProviders(current);
                provider.SetMode(saved.Mode);
                provider.RestoreRotationOverrides(saved);
                current = provider.Read(); CheckProviders(current);
                if (current.Mode != saved.Mode || current.ExternalOverrides != saved.ExternalOverrides ||
                    !Equals(current.IpcOverrides, saved.IpcOverrides) ||
                    !Equals(current.TargetingTypeOverride, saved.TargetingTypeOverride))
                    throw new InvalidOperationException("RSR original mode restoration is unconfirmed.");
                rotationChanged = false;
            }
            catch (Exception ex) { reason += (reason.Length == 0 ? "" : " ") + Describe(ex); }
        }
        if (!rotationChanged && !movementChanged && !aiChanged) { original = null; return true; }
        return false;
    }

    private void CheckProviders(State current)
    {
        if (original is not { } saved || !ReferenceEquals(current.RotationProvider, saved.RotationProvider) ||
            !ReferenceEquals(current.MovementProvider, saved.MovementProvider))
            throw new InvalidOperationException("A combat provider was unloaded or replaced; original ownership is unresolved.");
    }

    private static bool MatchesMovement(State state, string? preset, bool forceDisabled)
        => state.ForceDisabled == forceDisabled && string.Equals(state.Preset, preset, StringComparison.Ordinal);

    private static State Validate(IProvider provider)
    {
        var state = provider.Read();
        if (state.Rotation != RotationName)
            throw new InvalidOperationException("Load Rotation Solver Reborn and select its Reborn Beastmaster rotation.");
        var preset = JObject.Parse(provider.ReadPreset());
        var modules = preset["Modules"] as JObject;
        string[] allowed = ["StayCloseToTarget", "StayWithinLeylines", "GoToPositional", "StayCloseToPartyRole", "NormalMovement"];
        const string prefix = "BossMod.Autorotation.MiscAI.";
        if ((string?)preset["Name"] != PresetName || modules == null || modules.Count != allowed.Length ||
            modules.Properties().Any(p => !allowed.Contains(p.Name.StartsWith(prefix, StringComparison.Ordinal) ? p.Name[prefix.Length..] : "")))
            throw new InvalidOperationException("BossMod requires the existing movement-only 'passive - melee' preset; no DPS rotation may be included.");
        var range = modules[prefix + "StayCloseToTarget"]?.FirstOrDefault(t => (string?)t["Track"] == "range");
        var destination = modules[prefix + "NormalMovement"]?.FirstOrDefault(t => (string?)t["Track"] == "Destination");
        if ((string?)range?["Option"] != "2.3" || (string?)destination?["Option"] != "Pathfind")
            throw new InvalidOperationException("BossMod 'passive - melee' requires range 2.3 and Pathfind movement.");
        return state;
    }

    private static string Describe(Exception ex)
        => (ex is TargetInvocationException { InnerException: { } inner } ? inner : ex).Message;

    private sealed class NativeProvider : IProvider
    {
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public State Read()
        {
            if (Plugin.PluginInterface.InstalledPlugins.Any(candidate => candidate.IsLoaded && candidate.InternalName == "BossMod"))
                throw new InvalidOperationException("Use BossModReborn alone for Beastmaster; both BossMod providers share IPC names.");
            var rsr = ReadPlugin("RotationSolver", out var context);
            var bmr = ReadPlugin("BossModReborn", out _);
            var loadContext = context ?? AssemblyLoadContext.GetLoadContext(rsr.GetType().Assembly);
            var basic = loadContext?.Assemblies.FirstOrDefault(a => a.GetName().Name == "RotationSolver.Basic")
                ?? throw new InvalidOperationException("RSR runtime state is unavailable.");
            var data = basic.GetType("RotationSolver.Basic.DataCenter", true)!;
            bool Flag(string name) => ReadStatic(data, name) is bool value ? value
                : throw new InvalidOperationException("RSR runtime state is incompatible: " + name);
            var state = Flag("State");
            var manual = Flag("IsManual");
            var targetOnly = Flag("IsTargetOnly");
            var autoDuty = Flag("IsAutoDuty");
            var henched = Flag("IsHenched");
            var pvp = Flag("IsPvPStateEnabled");
            var mode = !state ? Mode.Off : pvp ? Mode.PvP : henched ? Mode.Henched : autoDuty ? Mode.AutoDuty
                : targetOnly ? Mode.TargetOnly : manual ? Mode.Manual : Mode.Auto;
            if (!state && (manual || targetOnly || autoDuty || henched || pvp) ||
                state && (mode == Mode.Henched ? !manual || targetOnly || autoDuty || pvp
                    : mode == Mode.Manual ? targetOnly || autoDuty || henched || pvp
                    : manual || (targetOnly ? 1 : 0) + (autoDuty ? 1 : 0) + (pvp ? 1 : 0) > 1))
                throw new InvalidOperationException("RSR operating-mode flags are inconsistent.");
            var rotation = ReadStatic(data, "CurrentRotation");
            var overrides = ReadStatic(data, "IpcOverrides");
            var targeting = ReadStatic(data, "TargetingTypeOverride");
            var aiType = bmr.GetType().Assembly.GetType("BossMod.AI.AIManager", true)!;
            var ai = aiType.GetField("Instance", Static)?.GetValue(null)
                ?? throw new InvalidOperationException("BossMod AI runtime state is unavailable.");
            var behaviour = aiType.GetField("Beh", Instance)
                ?? throw new InvalidOperationException("BossMod AI runtime state is incompatible.");
            var masterSlot = aiType.GetField("MasterSlot", Instance)?.GetValue(ai) is int slot ? slot
                : throw new InvalidOperationException("BossMod AI follow target is unavailable.");
            if (aiType.GetMethod("SwitchToIdle", Instance, null, Type.EmptyTypes, null) == null ||
                aiType.GetMethod("SwitchToFollow", Instance, null, [typeof(int)], null) == null)
                throw new InvalidOperationException("BossMod AI lifecycle controls are unavailable.");
            var preset = Plugin.PluginInterface.GetIpcSubscriber<string?>("BossMod.Presets.GetActive").InvokeFunc();
            var force = Plugin.PluginInterface.GetIpcSubscriber<bool>("BossMod.Presets.GetForceDisabled").InvokeFunc();
            if (force && preset != string.Empty)
                throw new InvalidOperationException("BossMod forced-disabled preset state is inconsistent.");
            return new State(rsr, bmr, mode, rotation?.GetType().FullName, overrides != null || targeting != null,
                behaviour.GetValue(ai) != null, preset, force)
                { AiMasterSlot = masterSlot, IpcOverrides = overrides, TargetingTypeOverride = targeting };
        }

        public string ReadPreset()
            => Plugin.PluginInterface.GetIpcSubscriber<string, string?>("BossMod.Presets.Get").InvokeFunc(PresetName)
                ?? throw new InvalidOperationException("BossMod is missing the existing 'passive - melee' preset.");

        public Target ReadTarget()
        {
            var bmr = ReadPlugin("BossModReborn", out _);
            var aiType = bmr.GetType().Assembly.GetType("BossMod.AI.AIManager", true)!;
            var ai = aiType.GetField("Instance", Static)?.GetValue(null)
                ?? throw new InvalidOperationException("BossMod AI runtime state is unavailable.");
            var autorot = aiType.GetField("Autorot", Instance)?.GetValue(ai)
                ?? throw new InvalidOperationException("BossMod AI runtime state is incompatible.");
            var autorotType = autorot.GetType();
            var player = autorotType.GetProperty("Player", Instance)
                ?? throw new InvalidOperationException("BossMod AI runtime state is incompatible.");
            if (player.GetValue(autorot) == null) return default;

            var bossmods = autorotType.GetField("Bossmods", Instance)?.GetValue(autorot)
                ?? throw new InvalidOperationException("BossMod AI runtime state is incompatible.");
            var activeModule = bossmods.GetType().GetProperty("ActiveModule", Instance)
                ?? throw new InvalidOperationException("BossMod AI runtime state is incompatible.");
            var module = activeModule.GetValue(bossmods);
            object? activePhase = null;
            if (module != null)
            {
                var stateMachine = module.GetType().GetField("StateMachine", Instance)?.GetValue(module)
                    ?? throw new InvalidOperationException("BossMod AI runtime state is incompatible.");
                var phase = stateMachine.GetType().GetProperty("ActivePhase", Instance)
                    ?? throw new InvalidOperationException("BossMod AI runtime state is incompatible.");
                activePhase = phase.GetValue(stateMachine);
            }

            // Delegate the decision to BMR itself, including its encounter priorities
            // and forbidden targets. No encounter-specific rules belong in VMX.
            var resolve = autorotType.GetMethod("ResolveTargetOverride", Instance)
                ?? throw new InvalidOperationException("BossMod AI runtime state is incompatible.");
            var strategy = Enum.Parse(resolve.GetParameters()[0].ParameterType, "EnemyWithHighestPriority");
            var actor = resolve.Invoke(autorot, [strategy, 0]); // StrategyEnemySelection.Closest
            var targetId = actor == null ? 0UL : actor.GetType().GetField("InstanceID", Instance)?.GetValue(actor) is ulong id ? id
                : throw new InvalidOperationException("BossMod AI runtime state is incompatible.");
            return new Target(targetId, activePhase == null);
        }

        public void SetMode(Mode mode)
            => Plugin.PluginInterface.GetIpcSubscriber<byte, object>("RotationSolverReborn.ChangeOperatingMode").InvokeAction((byte)mode);

        public void RestoreRotationOverrides(State saved)
        {
            var context = AssemblyLoadContext.GetLoadContext(saved.RotationProvider.GetType().Assembly);
            var data = context?.Assemblies.FirstOrDefault(a => a.GetName().Name == "RotationSolver.Basic")
                ?.GetType("RotationSolver.Basic.DataCenter", true)
                ?? throw new InvalidOperationException("RSR runtime state is unavailable.");
            // These are public, run-time overrides. Changing the mode clears them;
            // restore the opaque original values afterward without changing config.
            data.GetProperty("IpcOverrides", Static)!.SetValue(null, saved.IpcOverrides);
            data.GetProperty("TargetingTypeOverride", Static)!.SetValue(null, saved.TargetingTypeOverride);
        }

        public void SetPreset(string? name, bool forceDisabled)
        {
            // GetForceDisabled is equality with RMM.ForceDisable, not a separate enable flag.
            if (forceDisabled) Plugin.PluginInterface.GetIpcSubscriber<bool>("BossMod.Presets.SetForceDisabled").InvokeFunc();
            else if (name == null) Plugin.PluginInterface.GetIpcSubscriber<bool>("BossMod.Presets.ClearActive").InvokeFunc();
            else Plugin.PluginInterface.GetIpcSubscriber<string, bool>("BossMod.Presets.SetActive").InvokeFunc(name);
        }

        public void SetAiEnabled(bool enabled, int masterSlot)
        {
            var bmr = ReadPlugin("BossModReborn", out _);
            var aiType = bmr.GetType().Assembly.GetType("BossMod.AI.AIManager", true)!;
            var ai = aiType.GetField("Instance", Static)?.GetValue(null)
                ?? throw new InvalidOperationException("BossMod AI runtime state is unavailable.");
            var method = aiType.GetMethod(enabled ? "SwitchToFollow" : "SwitchToIdle", Instance, null,
                enabled ? [typeof(int)] : Type.EmptyTypes, null)
                ?? throw new InvalidOperationException("BossMod AI lifecycle controls are unavailable.");
            // These existing BMR operations change runtime behavior, not saved AI preferences.
            method.Invoke(ai, enabled ? [masterSlot] : null);
        }

        private static object ReadPlugin(string internalName, out AssemblyLoadContext? context)
        {
            if (!Plugin.PluginInterface.InstalledPlugins.Any(candidate => candidate.IsLoaded && candidate.InternalName == internalName))
                throw new InvalidOperationException($"{internalName} is not loaded.");
            if (!DalamudReflector.TryGetDalamudPlugin(internalName, out object instance, out context, true, true) || instance == null)
                throw new InvalidOperationException($"{internalName} is loaded, but its runtime state could not be read.");
            return instance;
        }

        private static object? ReadStatic(Type type, string name)
            => (type.GetProperty(name, Static) ?? throw new InvalidOperationException("RSR runtime state is incompatible: " + name)).GetValue(null);
    }
}
