using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using ECommons.Reflection;
using Newtonsoft.Json.Linq;

namespace VERMAXION.Beastmaster;

internal interface IBeastmasterCombat
{
    bool OwnsState { get; }
    bool IsRunning { get; }
    bool RequiresNativeMovement { get; }
    bool AllowDamageItems { get; }
    bool TryActivate(out string reason, bool allowAttacks = true, bool? useBmrMovement = null);
    bool TrySuspend(out string reason);
    bool TryRelease(out string reason);
    bool TryPrepare(uint board, uint battle, out bool ready, out string status, out string reason);
    BeastmasterCombat.Target ReadTarget();
}

// The pack owns native plans/actions. VMX owns one run-local lease.
internal sealed class BeastmasterCombat : IBeastmasterCombat
{
    internal const string IpcPrefix = "BMRVariantLoader.Crewcible.";
    internal const string RotationName = "BMRVariantModulePack.Autorotation.CrewcibleBST";
    internal readonly record struct Target(ulong InstanceId, bool CanPull, uint DamageItem = 0);
    internal readonly record struct Setup(uint[] Roster, uint[] Trio, uint PreBorrowFamiliar,
        uint BorrowAction, uint FirstActiveFamiliar, uint[] RequiredGear);
    internal readonly record struct State(object Provider, bool Available, bool Ready, string? Owner,
        bool Active, bool AllowAttacks, bool UseBmrMovement, string? Rotation, string? Error,
        bool MovementStopped = true, bool RequiresNativeMovement = false,
        bool AllowDamageItems = false, uint DamageItem = 0);
    internal interface IProvider
    {
        State Read();
        Target ReadTarget();
        string Control(string owner, bool active, bool allowAttacks, bool useBmrMovement);
        string Release(string owner);
        string Prepare(string owner, uint board, uint battle);
        bool CancelRotation();
    }

    private readonly IProvider provider;
    private readonly bool useBmrMovement;
    private readonly bool releaseOnFailure;
    private readonly string owner = "VERMAXION.Beastmaster/" + Guid.NewGuid().ToString("N");
    private State? original;
    private bool controllerClaimed;
    public bool OwnsState => original.HasValue;
    public bool IsRunning { get; private set; }
    public bool AllowDamageItems
    {
        get
        {
            if (!controllerClaimed || !IsRunning) return false;
            try
            {
                var current = ReadCurrent();
                return DamageItemsPermitted(current);
            }
            catch { return false; }
        }
    }
    private bool DamageItemsPermitted(State current) => controllerClaimed && IsRunning &&
        current.Owner == owner && current.Available && current.Ready && current.Active &&
        current.AllowAttacks && current.AllowDamageItems && current.Rotation == RotationName &&
        string.IsNullOrEmpty(current.Error);
    public bool RequiresNativeMovement
    {
        get
        {
            var current = ReadCurrent();
            if (controllerClaimed && current.Owner != owner)
                throw new InvalidOperationException("Crewcible ownership was revoked.");
            return current.RequiresNativeMovement;
        }
    }

    public BeastmasterCombat(bool useBmrMovement = true, bool releaseOnFailure = true)
        : this(new NativeProvider(), useBmrMovement, releaseOnFailure) { }
    internal BeastmasterCombat(IProvider provider, bool useBmrMovement = true, bool releaseOnFailure = true)
    { this.provider = provider; this.useBmrMovement = useBmrMovement; this.releaseOnFailure = releaseOnFailure; }

    public static bool CanStart(out string reason) => CanStart(new NativeProvider(), out reason);
    internal static bool CanStart(IProvider provider, out string reason)
    {
        try { Validate(provider.Read()); reason = string.Empty; return true; }
        catch (Exception ex) { reason = Describe(ex); return false; }
    }
    private static void Validate(State state)
    {
        if (!state.Available || !state.Ready || state.Rotation != RotationName || !string.IsNullOrEmpty(state.Error))
            throw new InvalidOperationException(string.IsNullOrEmpty(state.Error)
                ? "The native Crewcible rotation/controller is unavailable or not ready." : state.Error);
        if (state.Owner != null) throw new InvalidOperationException("The native Crewcible controller is already owned.");
    }

    internal static bool TryReadSetup(uint board, uint battle, out Setup setup, out string reason)
    {
        setup = default;
        try
        {
            if (board is < 1 or > 5) throw new InvalidOperationException("Select one of the five Crucible boards.");
            var endpoint = Plugin.PluginInterface.GetIpcSubscriber<uint, uint, string>(IpcPrefix + "Setup.v1");
            if (!endpoint.HasFunction) throw new InvalidOperationException("The native Crewcible setup catalog is unavailable.");
            var payload = JObject.Parse(endpoint.InvokeFunc(board, battle));
            var error = payload.Value<string>("Error");
            if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException(error);
            uint[] ReadIds(string name) => payload[name] is JArray array && array.All(value => value.Type == JTokenType.Integer)
                ? array.Select(value => value.Value<uint>()).ToArray()
                : throw new InvalidOperationException("The native Crewcible setup is incompatible: " + name);
            uint ReadId(string name) => payload[name]?.Type == JTokenType.Integer ? payload.Value<uint>(name)
                : throw new InvalidOperationException("The native Crewcible setup is incompatible: " + name);
            var roster = ReadIds("Roster");
            var trio = ReadIds("Trio");
            var borrow = ReadId("PreBorrowFamiliar");
            var action = ReadId("BorrowAction");
            var first = ReadId("FirstActiveFamiliar");
            var requiredGear = payload["RequiredGear"] == null ? [] : ReadIds("RequiredGear");
            if (requiredGear.Any(id => id == 0) || requiredGear.Distinct().Count() != requiredGear.Length)
                throw new InvalidOperationException("The native encounter gear requirements are invalid.");
            if (roster.Length != BeastmasterSelection.TeamCapacity(1338u + board) ||
                roster.Any(id => id == 0) || roster.Distinct().Count() != roster.Length)
                throw new InvalidOperationException("The native clear roster must contain exactly the board capacity of different familiars.");
            if (battle != 0 && (trio.Length != 3 || trio.Any(id => !roster.Contains(id)) || trio.Distinct().Count() != 3))
                throw new InvalidOperationException("This native encounter has no exact registered fighting trio.");
            if (borrow != 0 && !trio.Contains(borrow) || (borrow == 0) != (action == 0) ||
                battle != 0 && first == 0 || first != 0 && !trio.Contains(first))
                throw new InvalidOperationException("The native encounter preparation does not match its registered setup.");
            setup = new(roster, trio, borrow, action, first, requiredGear);
            reason = string.Empty;
            return true;
        }
        catch (Exception ex) { reason = Describe(ex); return false; }
    }

    public bool TryActivate(out string reason, bool allowAttacks = true, bool? useBmrMovement = null)
        => TryApply(true, allowAttacks, useBmrMovement ?? this.useBmrMovement, out reason);
    public bool TrySuspend(out string reason) => TryApply(false, false, false, out reason);
    public bool TryPrepare(uint board, uint battle, out bool ready, out string status, out string reason)
    {
        ready = false;
        status = string.Empty;
        reason = string.Empty;
        if (!OwnsState && !TrySuspend(out reason)) return false;
        try
        {
            if (!controllerClaimed || ReadCurrent().Owner != owner)
                throw new InvalidOperationException("Crewcible ownership was revoked before arena preparation.");
            var response = JObject.Parse(provider.Prepare(owner, board, battle));
            if (response["Ready"]?.Type != JTokenType.Boolean || response["Status"]?.Type != JTokenType.String ||
                response["Error"]?.Type != JTokenType.String)
                throw new InvalidOperationException("The native arena preparation response is incompatible.");
            var error = response.Value<string>("Error");
            if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException(error);
            if (ReadCurrent().Owner != owner) throw new InvalidOperationException("Crewcible preparation ownership was not confirmed.");
            ready = response.Value<bool>("Ready");
            status = response.Value<string>("Status")!;
            IsRunning = true; // Pending prep owns the pipeline; ordinary suspension would cancel its request.
            return true;
        }
        catch (Exception ex)
        {
            IsRunning = false;
            reason = Describe(ex);
            if (releaseOnFailure && OwnsState && !TryRelease(out var cleanup)) reason += " Cleanup: " + cleanup;
            return false;
        }
    }
    public Target ReadTarget()
    {
        var current = ReadCurrent();
        if (controllerClaimed && current.Owner != owner) throw new InvalidOperationException("Crewcible ownership was revoked.");
        return provider.ReadTarget() with { DamageItem = DamageItemsPermitted(current) ? current.DamageItem : 0 };
    }
    private State ReadCurrent()
    {
        var current = provider.Read();
        if (original is { } saved && !ReferenceEquals(current.Provider, saved.Provider))
            throw new InvalidOperationException("A combat provider was unloaded or replaced; original ownership is unresolved.");
        return current;
    }
    private bool TryApply(bool active, bool allowAttacks, bool nativeMovement, out string reason)
    {
        try
        {
            if (original == null)
            {
                var initial = provider.Read();
                Validate(initial);
                original = initial;
                try { provider.CancelRotation(); }
                catch { }
            }
            var current = ReadCurrent();
            if (controllerClaimed && current.Owner != owner)
                throw new InvalidOperationException("Crewcible ownership was revoked.");
            controllerClaimed = true; // An IPC call may mutate state before its reply fails.
            var error = provider.Control(owner, active, active && allowAttacks, nativeMovement);
            if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException(error);
            current = ReadCurrent();
            if (current.Owner != owner || current.Active != active || current.AllowAttacks != (active && allowAttacks) ||
                current.UseBmrMovement != nativeMovement || (!active || !nativeMovement) && !current.MovementStopped)
                throw new InvalidOperationException("Native Crewcible control transition was not confirmed.");
            IsRunning = active;
            reason = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            IsRunning = false;
            reason = Describe(ex);
            if (releaseOnFailure && OwnsState && !TryRelease(out var cleanup)) reason += " Cleanup: " + cleanup;
            return false;
        }
    }
    public bool TryRelease(out string reason)
    {
        IsRunning = false;
        reason = string.Empty;
        if (original == null) return true;
        try
        {
            ReadCurrent();
            if (controllerClaimed)
            {
                // Release is owner-checked and idempotent. Never reacquire a revoked lease for cleanup.
                var error = provider.Release(owner);
                if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException(error);
                if (ReadCurrent().Owner == owner) throw new InvalidOperationException("Crewcible release was not confirmed.");
                controllerClaimed = false;
            }
            original = null;
            return true;
        }
        catch (Exception ex) { reason = Describe(ex); return false; }
    }

    internal static string Describe(Exception ex)
        => (ex is TargetInvocationException { InnerException: { } inner } ? inner : ex).Message;

    internal static object ReadPlugin(string internalName, out AssemblyLoadContext? context)
    {
        if (!Plugin.PluginInterface.InstalledPlugins.Any(candidate => candidate.IsLoaded && candidate.InternalName == internalName))
            throw new InvalidOperationException($"{internalName} is not loaded.");
        if (!DalamudReflector.TryGetDalamudPlugin(internalName, out object instance, out context, true, true) || instance == null)
            throw new InvalidOperationException($"{internalName} is loaded, but its runtime state could not be read.");
        return instance;
    }

    private sealed class NativeProvider : IProvider
    {
        public State Read()
        {
            var ipc = Plugin.PluginInterface;
            if (ipc.InstalledPlugins.Any(candidate => candidate.IsLoaded && candidate.InternalName == "BossMod"))
                throw new InvalidOperationException("Use BossModReborn alone for Beastmaster; both BossMod providers share IPC names.");
            var bmr = ReadPlugin("BossModReborn", out _);
            if (!ipc.GetIpcSubscriber<string>(IpcPrefix + "State.v1").HasFunction ||
                !ipc.GetIpcSubscriber<string, bool, bool, bool, string>(IpcPrefix + "Control.v1").HasFunction ||
                !ipc.GetIpcSubscriber<string, string>(IpcPrefix + "Release.v1").HasFunction ||
                !ipc.GetIpcSubscriber<string, uint, uint, string>(IpcPrefix + "Prepare.v1").HasFunction ||
                !ipc.GetIpcSubscriber<(ulong, bool)>(IpcPrefix + "Target.v1").HasFunction)
                throw new InvalidOperationException("The native Crewcible controller IPC is unavailable or incompatible.");
            var status = JObject.Parse(ipc.GetIpcSubscriber<string>(IpcPrefix + "State.v1").InvokeFunc());
            if (new[] { "Available", "Ready", "Active", "AllowAttacks", "UseBmrMovement", "MovementStopped", "RequiresNativeMovement" }
                    .Any(name => status[name]?.Type != JTokenType.Boolean) ||
                status["Owner"]?.Type is not (JTokenType.String or JTokenType.Null) ||
                status["Rotation"]?.Type != JTokenType.String)
                throw new InvalidOperationException("The native Crewcible controller state is incompatible.");
            return new(bmr, status.Value<bool?>("Available") == true, status.Value<bool?>("Ready") == true,
                status.Value<string>("Owner"), status.Value<bool?>("Active") == true,
                status.Value<bool?>("AllowAttacks") == true, status.Value<bool?>("UseBmrMovement") == true,
                status.Value<string>("Rotation"), status.Value<string>("Error"),
                status.Value<bool>("MovementStopped"), status.Value<bool>("RequiresNativeMovement"),
                status["AllowDamageItems"]?.Type == JTokenType.Boolean && status.Value<bool>("AllowDamageItems"),
                status["DamageItem"]?.Type == JTokenType.Integer ? status.Value<uint>("DamageItem") : 0);
        }
        public Target ReadTarget()
        {
            var target = Plugin.PluginInterface.GetIpcSubscriber<(ulong, bool)>(IpcPrefix + "Target.v1").InvokeFunc();
            return new(target.Item1, target.Item2);
        }
        public string Control(string owner, bool active, bool allowAttacks, bool useBmrMovement)
            => Plugin.PluginInterface.GetIpcSubscriber<string, bool, bool, bool, string>(IpcPrefix + "Control.v1")
                .InvokeFunc(owner, active, allowAttacks, useBmrMovement);
        public string Release(string owner)
            => Plugin.PluginInterface.GetIpcSubscriber<string, string>(IpcPrefix + "Release.v1").InvokeFunc(owner);
        public string Prepare(string owner, uint board, uint battle)
            => Plugin.PluginInterface.GetIpcSubscriber<string, uint, uint, string>(IpcPrefix + "Prepare.v1").InvokeFunc(owner, board, battle);
        public bool CancelRotation() => Plugin.CommandManager.ProcessCommand("/rotation cancel");
    }
}
