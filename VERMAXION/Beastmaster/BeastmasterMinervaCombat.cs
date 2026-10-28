using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Ipc;

namespace VERMAXION.Beastmaster;

// Minerva owns movement, while the existing Beastmaster rotation still owns abilities.
internal sealed class BeastmasterMinervaCombat : IBeastmasterCombat
{
    internal const string OwnerName = "VERMAXION.Beastmaster";
    internal readonly record struct State(BeastmasterCombat.RotationState Rotation, object MovementProvider,
        string? Owner, bool DodgeEnabled, bool FaceGazes, bool MovementStopped, string Preset, bool WithholdAttacks);

    internal interface IProvider
    {
        State Read();
        BeastmasterCombat.Target ReadTarget();
        void Claim();
        void SetMovement(bool enabled);
        void RestoreFacing(bool enabled);
        void Release();
        void SetMode(BeastmasterCombat.Mode mode);
        void RestoreRotationOverrides(BeastmasterCombat.RotationState saved);
    }

    private readonly IProvider provider;
    private State? original;
    private bool rotationChanged, movementChanged, releaseRequested;
    public bool OwnsState => original.HasValue;
    public bool IsRunning { get; private set; }

    public BeastmasterMinervaCombat() : this(new NativeProvider()) { }
    internal BeastmasterMinervaCombat(IProvider provider) => this.provider = provider;
    public static bool CanStart(out string reason) => CanStart(new NativeProvider(), out reason);
    internal static bool CanStart(IProvider provider, out string reason)
    {
        try { Validate(provider); reason = string.Empty; return true; }
        catch (Exception ex) { reason = BeastmasterCombat.Describe(ex); return false; }
    }

    private static State Validate(IProvider provider)
    {
        var current = provider.Read();
        if (current.Rotation.Rotation != BeastmasterCombat.RotationName)
            throw new InvalidOperationException("Load Rotation Solver Reborn and select its Reborn Beastmaster rotation.");
        if (current.Owner != null)
            throw new InvalidOperationException("Minerva is owned by another controller.");
        return current;
    }

    public bool TryActivate(out string reason, bool allowAttacks = true) => TryApply(true, allowAttacks, out reason);
    public bool TrySuspend(out string reason) => TryApply(false, false, out reason);
    public BeastmasterCombat.Target ReadTarget()
    {
        var current = ReadCurrent();
        if (original.HasValue && current.Owner != OwnerName)
            throw new InvalidOperationException("Minerva ownership was revoked.");
        return current.WithholdAttacks ? default : provider.ReadTarget();
    }

    private bool TryApply(bool running, bool allowAttacks, out string reason)
    {
        try
        {
            if (original == null) original = Validate(provider);
            var current = ReadCurrent();
            if (running && current.Rotation.Rotation != BeastmasterCombat.RotationName)
                throw new InvalidOperationException("RSR Reborn Beastmaster is no longer the current rotation.");
            // Do not turn abilities on until movement ownership is established.
            if (!movementChanged)
            {
                SetRotation(BeastmasterCombat.Mode.Off);
                movementChanged = true; // Claim may succeed before its reply or config save fails.
                provider.Claim();
            }
            current = ReadCurrent();
            RequireOwner(current);
            if (!running || !allowAttacks || current.WithholdAttacks)
                SetRotation(BeastmasterCombat.Mode.Off);
            provider.SetMovement(running);
            current = ReadCurrent();
            RequireOwner(current);
            if (current.DodgeEnabled != running || current.FaceGazes != running || !running && !current.MovementStopped)
                throw new InvalidOperationException("Minerva movement transition is unconfirmed.");
            SetRotation(running && allowAttacks && !current.WithholdAttacks
                ? BeastmasterCombat.Mode.Henched : BeastmasterCombat.Mode.Off);
            IsRunning = running;
            reason = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            IsRunning = false;
            reason = BeastmasterCombat.Describe(ex);
            if (OwnsState && !TryRelease(out var cleanup)) reason += " Cleanup: " + cleanup;
            return false;
        }
    }

    private State ReadCurrent()
    {
        var current = provider.Read();
        if (original is { } saved && (!ReferenceEquals(saved.Rotation.Provider, current.Rotation.Provider) ||
            !ReferenceEquals(saved.MovementProvider, current.MovementProvider)))
            throw new InvalidOperationException("A combat provider was unloaded or replaced; original ownership is unresolved.");
        return current;
    }

    private static void RequireOwner(State current)
    {
        if (current.Owner != OwnerName) throw new InvalidOperationException("Minerva ownership was revoked.");
    }

    private void SetRotation(BeastmasterCombat.Mode mode)
    {
        var current = ReadCurrent();
        rotationChanged = true;
        if (current.Rotation.Mode == mode && !current.Rotation.ExternalOverrides) return;
        provider.SetMode(mode);
        current = ReadCurrent();
        if (current.Rotation.Mode != mode || current.Rotation.ExternalOverrides)
            throw new InvalidOperationException("RSR did not confirm the requested Beastmaster operating mode.");
    }

    public bool TryRelease(out string reason)
    {
        IsRunning = false;
        reason = string.Empty;
        if (original is not { } saved) return true;
        try
        {
            var current = ReadCurrent();
            if (movementChanged)
            {
                if (current.Owner == OwnerName)
                {
                    SetRotation(BeastmasterCombat.Mode.Off);
                    // Clear only Minerva's route before giving the user's prior settings back.
                    provider.SetMovement(false);
                    current = ReadCurrent();
                    RequireOwner(current);
                    if (current.DodgeEnabled || !current.MovementStopped)
                        throw new InvalidOperationException("Minerva movement transition is unconfirmed.");
                    provider.RestoreFacing(saved.FaceGazes);
                    current = ReadCurrent();
                    RequireOwner(current);
                    if (current.FaceGazes != saved.FaceGazes)
                        throw new InvalidOperationException("Minerva original state restoration is unconfirmed.");
                    releaseRequested = true;
                    provider.Release();
                    current = ReadCurrent();
                }
                if (releaseRequested && (current.Owner == OwnerName || current.Owner == null &&
                    (current.DodgeEnabled != saved.DodgeEnabled || current.FaceGazes != saved.FaceGazes || current.Preset != saved.Preset)))
                    throw new InvalidOperationException("Minerva original state restoration is unconfirmed.");
                // An observed revocation is not an unavailable read. Never overwrite a new owner or user choice.
                movementChanged = releaseRequested = false;
            }
        }
        catch (Exception ex) { reason = BeastmasterCombat.Describe(ex); }

        // A failed movement stop must not turn the original rotation back on while automation is unresolved.
        if (movementChanged) return false;
        if (rotationChanged)
        {
            try
            {
                ReadCurrent();
                provider.SetMode(saved.Rotation.Mode);
                provider.RestoreRotationOverrides(saved.Rotation);
                var rotation = ReadCurrent().Rotation;
                if (rotation.Mode != saved.Rotation.Mode || rotation.ExternalOverrides != saved.Rotation.ExternalOverrides ||
                    !Equals(rotation.IpcOverrides, saved.Rotation.IpcOverrides) ||
                    !Equals(rotation.TargetingTypeOverride, saved.Rotation.TargetingTypeOverride))
                    throw new InvalidOperationException("RSR original mode restoration is unconfirmed.");
                rotationChanged = false;
            }
            catch (Exception ex) { reason += (reason.Length == 0 ? "" : " ") + BeastmasterCombat.Describe(ex); }
        }
        if (!movementChanged && !rotationChanged) { original = null; return true; }
        return false;
    }

    private sealed class NativeProvider : IProvider
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private object? stoppingBackend;
        public State Read()
        {
            var rsr = BeastmasterCombat.ReadPlugin("RotationSolver", out var context);
            var minerva = BeastmasterCombat.ReadPlugin("Minerva", out _);
            RequireFunctions();
            if (!Plugin.PluginInterface.GetIpcSubscriber<bool>("Minerva.IsConnected").InvokeFunc())
                throw new InvalidOperationException("Minerva IPC is unavailable or incompatible.");
            ValidateTargets(minerva);
            var config = Property(minerva, "Config");
            var presets = Property(minerva, "Presets");
            var movement = Movement(minerva);
            var enabled = config.GetType().GetProperty("AutoDodgeEnabled", Instance);
            var facing = config.GetType().GetProperty("AutoFaceGazes", Instance);
            if (enabled?.PropertyType != typeof(bool) || !enabled.CanRead || !enabled.CanWrite ||
                facing?.PropertyType != typeof(bool) || !facing.CanRead || !facing.CanWrite ||
                movement.GetType().GetMethod("Stop", Instance, null, Type.EmptyTypes, null)?.ReturnType != typeof(void) ||
                movement.GetType().GetMethod("OwnsFollower", Instance, null, Type.EmptyTypes, null)?.ReturnType != typeof(bool) ||
                movement.GetType().GetField("nav", Instance) == null)
                throw new InvalidOperationException("Minerva runtime controls are incompatible.");
            var nav = Field(movement, "nav");
            var backend = nav.GetType().GetField("driving", Instance)?.FieldType;
            if (backend?.GetProperty("Stop", Instance)?.PropertyType != typeof(ICallGateSubscriber<object>) ||
                backend.GetProperty("NumWaypoints", Instance)?.PropertyType != typeof(ICallGateSubscriber<int>) ||
                backend.GetProperty("IsSteering", Instance)?.PropertyType != typeof(ICallGateSubscriber<bool>))
                throw new InvalidOperationException("Minerva runtime controls are incompatible.");
            var dodge = (bool)enabled.GetValue(config)!;
            if (Plugin.PluginInterface.GetIpcSubscriber<bool>("Minerva.IsAutoDodgeEnabled").InvokeFunc() != dodge)
                throw new InvalidOperationException("Minerva runtime controls are incompatible.");
            var ownerProperty = presets.GetType().GetProperty("Owner", Instance);
            if (ownerProperty?.PropertyType != typeof(string) || !ownerProperty.CanRead)
                throw new InvalidOperationException("Minerva runtime controls are incompatible.");
            var mode = Property(movement, "Mode");
            if (!mode.GetType().IsEnum || !Enum.GetNames(mode.GetType()).Contains("None"))
                throw new InvalidOperationException("Minerva runtime controls are incompatible.");
            return new(BeastmasterCombat.ReadRotation(rsr, context), minerva, (string?)ownerProperty.GetValue(presets),
                dodge, (bool)facing.GetValue(config)!, mode.ToString() == "None" && stoppingBackend == null,
                Plugin.PluginInterface.GetIpcSubscriber<string>("Minerva.ActivePreset").InvokeFunc(),
                Plugin.PluginInterface.GetIpcSubscriber<bool>("Minerva.MustNotAct").InvokeFunc() ||
                Plugin.PluginInterface.GetIpcSubscriber<bool>("Minerva.MustNotMove").InvokeFunc() ||
                Plugin.PluginInterface.GetIpcSubscriber<bool>("Minerva.MustNotTurn").InvokeFunc());
        }

        private static object Property(object value, string name)
            => value.GetType().GetProperty(name, Instance)?.GetValue(value)
                ?? throw new InvalidOperationException("Minerva runtime controls are incompatible.");
        private static object Movement(object minerva)
            => minerva.GetType().GetField("movement", Instance)?.GetValue(minerva)
                ?? throw new InvalidOperationException("Minerva runtime controls are incompatible.");

        private static void ValidateTargets(object minerva)
        {
            var ai = Field(minerva, "ai");
            var solution = ai.GetType().GetProperty("HasSolution", Instance);
            var hints = Property(ai, "Hints");
            var targets = hints.GetType().GetField("PotentialTargets", Instance);
            var list = targets?.FieldType;
            if (solution?.PropertyType != typeof(bool) || !solution.CanRead ||
                list == null || !list.IsGenericType || list.GetGenericTypeDefinition() != typeof(List<>) || targets!.GetValue(hints) == null)
                throw new InvalidOperationException("Minerva runtime controls are incompatible.");
            var enemy = list.GetGenericArguments()[0];
            var actor = enemy.GetField("Actor", Instance)?.FieldType;
            var position = actor?.GetProperty("Position", Instance);
            if (enemy.GetField("Priority", Instance)?.FieldType != typeof(int) ||
                actor?.GetField("InstanceID", Instance)?.FieldType != typeof(ulong) || position?.CanRead != true ||
                position.PropertyType.GetField("X", Instance)?.FieldType != typeof(float) ||
                position.PropertyType.GetField("Z", Instance)?.FieldType != typeof(float) ||
                new[] { "IsDead", "IsDestroyed", "IsTargetable", "IsAlly", "InCombat" }
                    .Any(name => actor.GetField(name, Instance)?.FieldType != typeof(bool)))
                throw new InvalidOperationException("Minerva runtime controls are incompatible.");
        }

        private static void RequireFunctions()
        {
            var ipc = Plugin.PluginInterface;
            if (!ipc.GetIpcSubscriber<bool>("Minerva.IsConnected").HasFunction ||
                !ipc.GetIpcSubscriber<string, bool, bool>("Minerva.SetAutoDodge").HasFunction ||
                !ipc.GetIpcSubscriber<string, bool>("Minerva.ReleasePreset").HasFunction ||
                !ipc.GetIpcSubscriber<bool>("Minerva.IsAutoDodgeEnabled").HasFunction ||
                !ipc.GetIpcSubscriber<string>("Minerva.ActivePreset").HasFunction ||
                !ipc.GetIpcSubscriber<bool>("Minerva.BossEngaged").HasFunction ||
                !ipc.GetIpcSubscriber<bool>("Minerva.MustNotAct").HasFunction ||
                !ipc.GetIpcSubscriber<bool>("Minerva.MustNotMove").HasFunction ||
                !ipc.GetIpcSubscriber<bool>("Minerva.MustNotTurn").HasFunction ||
                !ipc.GetIpcSubscriber<ulong>("Minerva.Hints.ForcedTarget").HasFunction ||
                !ipc.GetIpcSubscriber<ulong>("Minerva.Hints.PullTarget").HasFunction ||
                !ipc.GetIpcSubscriber<ulong[]>("Minerva.Hints.PriorityTargets").HasFunction ||
                !ipc.GetIpcSubscriber<ulong[]>("Minerva.Hints.ForbiddenTargets").HasFunction ||
                !ipc.GetIpcSubscriber<ulong[]>("Minerva.Hints.DeprioritizedTargets").HasFunction ||
                !ipc.GetIpcSubscriber<byte, object>("RotationSolverReborn.ChangeOperatingMode").HasAction)
                throw new InvalidOperationException("Minerva IPC is unavailable or incompatible.");
        }

        public BeastmasterCombat.Target ReadTarget()
        {
            var ipc = Plugin.PluginInterface;
            var minerva = BeastmasterCombat.ReadPlugin("Minerva", out _);
            var ai = minerva.GetType().GetField("ai", Instance)?.GetValue(minerva)
                ?? throw new InvalidOperationException("Minerva runtime controls are incompatible.");
            if (Property(ai, "HasSolution") is not true) return default;
            var hints = Property(ai, "Hints");
            var targets = hints.GetType().GetField("PotentialTargets", Instance)?.GetValue(hints) as IEnumerable
                ?? throw new InvalidOperationException("Minerva runtime controls are incompatible.");
            var forbidden = ipc.GetIpcSubscriber<ulong[]>("Minerva.Hints.ForbiddenTargets").InvokeFunc();
            var deprioritized = ipc.GetIpcSubscriber<ulong[]>("Minerva.Hints.DeprioritizedTargets").InvokeFunc();
            var forced = ipc.GetIpcSubscriber<ulong>("Minerva.Hints.ForcedTarget").InvokeFunc();
            var priority = ipc.GetIpcSubscriber<ulong[]>("Minerva.Hints.PriorityTargets").InvokeFunc();
            var pull = ipc.GetIpcSubscriber<ulong>("Minerva.Hints.PullTarget").InvokeFunc();
            var inCombat = ipc.GetIpcSubscriber<bool>("Minerva.BossEngaged").InvokeFunc() || Plugin.Condition[ConditionFlag.InCombat];
            var player = Plugin.ObjectTable.LocalPlayer;
            if (player == null) return default;
            var candidates = new List<(ulong Id, int Priority, float Distance)>();
            foreach (var enemy in targets)
            {
                if (enemy == null) continue;
                var actor = Field(enemy, "Actor");
                var id = (ulong)Field(actor, "InstanceID");
                var rank = (int)Field(enemy, "Priority");
                if (id == 0 || rank <= int.MinValue + 1 || forbidden.Contains(id) ||
                    (bool)Field(actor, "IsDead") || (bool)Field(actor, "IsDestroyed") ||
                    !(bool)Field(actor, "IsTargetable") || (bool)Field(actor, "IsAlly")) continue;
                // An explicit target/pull can open a fight. Otherwise keep passive mobs out of an ongoing encounter.
                if (inCombat && !(bool)Field(actor, "InCombat") && id != forced && !priority.Contains(id)) continue;
                var position = Property(actor, "Position");
                var x = (float)Field(position, "X") - player.Position.X;
                var z = (float)Field(position, "Z") - player.Position.Z;
                var distance = x * x + z * z;
                if (distance < 50 * 50) candidates.Add((id, deprioritized.Contains(id) ? Math.Min(rank, -1) : rank, distance));
            }
            bool Eligible(ulong id) => candidates.Any(candidate => candidate.Id == id);
            var target = Eligible(forced) ? forced : priority.FirstOrDefault(Eligible);
            if (target == 0 && !inCombat && Eligible(pull)) target = pull;
            // PriorityTargets contains only raised opinions. Ordinary priority-zero enemies remain attackable;
            // negative priorities are legal once no better enemy remains. Never let the route bypass forbidden hints.
            if (target == 0) target = candidates.OrderByDescending(candidate => candidate.Priority)
                .ThenBy(candidate => candidate.Distance).Select(candidate => candidate.Id).FirstOrDefault();
            return new(target, false);
        }

        private static object Field(object value, string name)
            => value.GetType().GetField(name, Instance)?.GetValue(value)
                ?? throw new InvalidOperationException("Minerva runtime controls are incompatible.");

        public void Claim()
        {
            stoppingBackend = null; // A fresh lease follows confirmed release or observed revocation of the previous one.
            if (!Plugin.PluginInterface.GetIpcSubscriber<string, bool, bool>("Minerva.SetAutoDodge").InvokeFunc(OwnerName, true))
                throw new InvalidOperationException("Minerva is owned by another controller.");
        }

        public void SetMovement(bool enabled)
        {
            var minerva = BeastmasterCombat.ReadPlugin("Minerva", out _);
            var presets = Property(minerva, "Presets");
            if (presets.GetType().GetProperty("Owner", Instance)?.GetValue(presets) as string != OwnerName)
                throw new InvalidOperationException("Minerva ownership was revoked.");
            var movement = Movement(minerva);
            if (!enabled) StopOwnedFollower(movement);
            var config = Property(minerva, "Config");
            config.GetType().GetProperty("AutoDodgeEnabled", Instance)!.SetValue(config, enabled);
            config.GetType().GetProperty("AutoFaceGazes", Instance)!.SetValue(config, enabled);
            // Release restores prior enabled=true too, so it cannot serve as a pause.
            if (!enabled) movement.GetType().GetMethod("Stop", Instance, null, Type.EmptyTypes, null)!.Invoke(movement, null);
        }

        private void StopOwnedFollower(object movement)
        {
            var owns = movement.GetType().GetMethod("OwnsFollower", Instance, null, Type.EmptyTypes, null)
                ?? throw new InvalidOperationException("Minerva runtime controls are incompatible.");
            var nav = Field(movement, "nav");
            var driving = nav.GetType().GetField("driving", Instance)?.GetValue(nav);
            var owned = owns.Invoke(movement, null) is true;
            if (stoppingBackend == null)
            {
                if (!owned) return;
                stoppingBackend = driving ?? throw new InvalidOperationException("Minerva movement transition is unconfirmed.");
            }
            var backend = stoppingBackend;
            var stop = Property(backend, "Stop") as ICallGateSubscriber<object>;
            var waypoints = Property(backend, "NumWaypoints") as ICallGateSubscriber<int>;
            var steering = backend.GetType().GetProperty("IsSteering", Instance)?.GetValue(backend) as ICallGateSubscriber<bool>;
            if (stop?.HasAction != true || waypoints?.HasFunction != true || steering != null && !steering.HasFunction)
                throw new InvalidOperationException("Minerva IPC is unavailable or incompatible.");
            bool Stopped() => waypoints.InvokeFunc() == 0 && (steering == null || !steering.InvokeFunc());
            if (!Stopped())
            {
                // A later Minerva frame can erase its own route identity even after Stop fails.
                // Retain the existing backend claim, but never stop a route it no longer recognizes as owned.
                if (!owned || !ReferenceEquals(driving, backend))
                    throw new InvalidOperationException("Minerva movement transition is unconfirmed.");
                stop.InvokeAction();
                if (!Stopped()) throw new InvalidOperationException("Minerva movement transition is unconfirmed.");
            }
            if (owned && !ReferenceEquals(driving, backend))
            {
                // Minerva may have issued another owned route while the old backend was unresolved.
                // Carry that current backend into the existing cleanup cadence too.
                stoppingBackend = driving ?? throw new InvalidOperationException("Minerva movement transition is unconfirmed.");
                throw new InvalidOperationException("Minerva movement transition is unconfirmed.");
            }
            stoppingBackend = null;
        }

        public void RestoreFacing(bool enabled)
        {
            var minerva = BeastmasterCombat.ReadPlugin("Minerva", out _);
            var presets = Property(minerva, "Presets");
            if (presets.GetType().GetProperty("Owner", Instance)?.GetValue(presets) as string != OwnerName)
                throw new InvalidOperationException("Minerva ownership was revoked.");
            var config = Property(minerva, "Config");
            config.GetType().GetProperty("AutoFaceGazes", Instance)!.SetValue(config, enabled);
        }

        public void Release()
        {
            if (!Plugin.PluginInterface.GetIpcSubscriber<string, bool>("Minerva.ReleasePreset").InvokeFunc(OwnerName))
                throw new InvalidOperationException("Minerva original state restoration is unconfirmed.");
        }
        public void SetMode(BeastmasterCombat.Mode mode) => BeastmasterCombat.SetRotationMode(mode);
        public void RestoreRotationOverrides(BeastmasterCombat.RotationState saved)
            => BeastmasterCombat.RestoreRotationOverrides(saved.Provider, saved.IpcOverrides, saved.TargetingTypeOverride);
    }
}
