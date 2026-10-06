using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Dalamud.Game.ClientState.Conditions;
using ECommons.Reflection;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VERMAXION.Models;

namespace VERMAXION.Services;

// Normal cleanup owns only the temporary objects registered by this instance.
internal sealed unsafe class FishCollectionAutoHook(Plugin plugin) : IDisposable
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private object? config, presets, originalPreset, world;
    private readonly List<object> owned = [];
    private readonly Dictionary<object, (bool All, bool Line, bool Mooch)> castSettings = new();
    private bool enabled, autoStart, autoOcean, collectables;
    private uint originalBait;
    private ulong character;
    private DateTimeOffset rodDownSince;
    private bool quitSent, baitRestoreSent;
    public bool OwnsState => config != null;
    public string SelectedName => presets == null ? "" : ReadNullable(presets, "SelectedPreset") is { } p ? (string)Read(p, "PresetName") : "";
    public uint[] RequiredBait { get; private set; } = [];
    public uint[] Cordials { get; private set; } = [];
    public bool ActionsAllowed { get; private set; }
    public string StartDiagnostics { get; private set; } = "";

    public static object Read(object owner, string name) => ReadNullable(owner, name)
        ?? throw new InvalidOperationException($"AutoHook member {name} is unavailable.");
    private static object? ReadNullable(object owner, string name)
    {
        for (var type = owner.GetType(); type != null; type = type.BaseType)
        {
            if (type.GetField(name, Members | BindingFlags.DeclaredOnly) is { } field) return field.GetValue(owner);
            if (type.GetProperty(name, Members | BindingFlags.DeclaredOnly) is { } property) return property.GetValue(owner);
        }
        return null;
    }
    private static void Write(object owner, string name, object value)
    {
        for (var type = owner.GetType(); type != null; type = type.BaseType)
        {
            if (type.GetField(name, Members | BindingFlags.DeclaredOnly) is { } field) { field.SetValue(owner, value); return; }
            if (type.GetProperty(name, Members | BindingFlags.DeclaredOnly) is { } property)
            {
                if (!property.CanWrite) break;
                property.SetValue(owner, value); return;
            }
        }
        throw new InvalidOperationException($"AutoHook member {name} is not writable.");
    }
    private static void SetAutoStart(bool value)
    {
        Plugin.PluginInterface.GetIpcSubscriber<bool, object>("AutoHook.SetAutoStartFishing").InvokeAction(value);
        if (Plugin.PluginInterface.GetIpcSubscriber<bool>("AutoHook.GetAutoStartFishing").InvokeFunc() != value)
            throw new InvalidOperationException("AutoHook autostart verification failed.");
    }
    private void SetEnabled(bool value)
    {
        if (!plugin.AutoHookIPC.TrySetPluginState(value, out var error)) throw new InvalidOperationException(error);
    }
    private void Save() => config!.GetType().GetMethod("Save", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null);

    public void Prepare(CollectionFish fish, FisherObservation observation)
    {
        if (OwnsState) throw new InvalidOperationException("Collection already owns AutoHook presets.");
        if (Plugin.Condition[ConditionFlag.Fishing]) throw new InvalidOperationException("Put away the rod before starting collection preparation.");
        foreach (var id in new uint[] { fish.HookAction, fish.CatchPath.Length > 1 ? 297u : 0,
                     fish.Snagging ? 4100u : 0, fish.ModestLure > 0 ? 37595u : 0, fish.AmbitiousLure > 0 ? 37594u : 0 })
            if (id > 0 && !FishCollectionObservation.ActionUnlocked(id)) throw LockedAction(id);
        if (!DalamudReflector.TryGetDalamudPlugin("AutoHook", out object hook, out AssemblyLoadContext? _, true, true) || hook == null)
            throw new InvalidOperationException("AutoHook is not loaded.");
        var assembly = hook.GetType().Assembly;
        var worldType = assembly.GetType("AutoHook.World.WorldState")
            ?? throw new InvalidOperationException("AutoHook casting state is unavailable.");
        var serviceAssembly = AssemblyLoadContext.GetLoadContext(assembly)!.LoadFromAssemblyName(new AssemblyName("clib"));
        var currentWorld = serviceAssembly.GetType("clib.Services.Svc")!.GetMethod("Get", BindingFlags.Public | BindingFlags.Static)!
            .MakeGenericMethod(worldType).Invoke(null, null)
            ?? throw new InvalidOperationException("AutoHook casting state is unavailable.");
        if ((bool)Read(Read(currentWorld, "Player"), "BlockCasting"))
            throw new InvalidOperationException("AutoHook is blocking casting before collection preparation.");
        var type = assembly.GetType("AutoHook.Presets.Config.Configuration")!;
        var configuration = type.GetProperty("C", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
            ?? throw new InvalidOperationException("AutoHook configuration is unavailable.");
        var collection = Read(configuration, "HookPresets");
        object? generated = null;
        if (!fish.Ocean)
        {
            var catalogType = assembly.GetType("AutoHook.Data.FishBaitCatalog")!;
            var clib = AssemblyLoadContext.GetLoadContext(assembly)!.LoadFromAssemblyName(new AssemblyName("clib"));
            var baitCatalog = clib.GetType("clib.Services.Svc")!.GetMethod("Get", BindingFlags.Public | BindingFlags.Static)!
                .MakeGenericMethod(catalogType).Invoke(null, null)!;
            var solver = Read(baitCatalog, "FishSolver");
            if (!(bool)Read(solver, "IsLoaded")) throw new InvalidOperationException("AutoHook fish solver is not loaded.");
            Func<uint, int> count = id => id == 6141
                ? Math.Max(10, FishCollectionObservation.Count(id, false))
                : FishCollectionObservation.Count(id % 1_000_000, id >= 1_000_000);
            var cordials = solver.GetType().GetMethod("ReadCordialInventory", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [count]);
            generated = BuildNativePreset(solver, assembly, fish, observation, cordials!);
            ValidateActions(JObject.FromObject(generated));
        }
        originalPreset = ReadNullable(collection, "SelectedPreset");
        enabled = Plugin.PluginInterface.GetIpcSubscriber<bool>("AutoHook.GetPluginState").InvokeFunc();
        autoStart = Plugin.PluginInterface.GetIpcSubscriber<bool>("AutoHook.GetAutoStartFishing").InvokeFunc();
        autoOcean = (bool)Read(configuration, "AutoOceanFish");
        collectables = (bool)Read(configuration, "AutoCollectablesEnabled");
        originalBait = PlayerState.Instance()->FishingBait;
        character = Plugin.PlayerState.ContentId;
        // Snapshot before the first write; partial failures still enter the same cleanup path.
        config = configuration;
        presets = collection;
        world = currentWorld;
        SetAutoStart(false);
        SetEnabled(false);
        Write(config, "AutoOceanFish", false);
        Write(config, "AutoCollectablesEnabled", false);
        var prefix = $"anon_VMX collection {Guid.NewGuid():N} ";
        if (fish.Ocean)
            ImportOcean(assembly, fish, prefix);
        else
        {
            Write(generated!, "PresetName", prefix + fish.Name);
            // AutoHook IntuitionTimerCD reads native TimeRemaining, including unknown-duration fish.
            // Preserve its thresholds and inversion semantics; no guessed countdown is inserted.
            Register(generated!);
        }
        RequiredBait = owned.SelectMany(Baits).Distinct().ToArray();
        Cordials = owned.Select(p => JObject.FromObject(p)["AutoCastsCfg"]?["CastCordial"])
            .Where(c => (bool?)c?["Enabled"] == true).Select(c => (uint)c!["Id"]!).Distinct().ToArray();
        foreach (var preset in owned)
        {
            var casts = Read(preset, "AutoCastsCfg");
            if (ReadNullable(casts, "CastFood") is { } food) Write(food, "Enabled", false);
            ValidateActions(JObject.FromObject(preset));
            castSettings[preset] = ((bool)Read(casts, "EnableAll"), (bool)Read(Read(casts, "CastLine"), "Enabled"),
                (bool)Read(Read(casts, "CastMooch"), "Enabled"));
        }
        AllowCasts(false);
        Select(owned.First());
        Save();
        Plugin.Log.Information($"[FishCollection] AutoHook strategy prepared: item={fish.ItemId}; selected={SelectedName}; ownedPresetIds={string.Join(",", owned.Select(p => Read(p, "UniqueId")))}; bait={string.Join(",", RequiredBait)}; blockCasting={(bool)Read(Read(world, "Player"), "BlockCasting")}");
    }

    private static object BuildNativePreset(object solver, Assembly assembly, CollectionFish fish,
        FisherObservation observation, object cordials)
    {
        var profile = solver.GetType().GetMethod("CreateProfile")!.Invoke(solver,
            [observation.Level, observation.MaximumGp, cordials])!;
        var skills = Read(profile, "Skills");
        foreach (var (name, id) in FishCollectionPolicy.SolverSkillActions)
            Write(skills, name, FishCollectionObservation.ActionUnlocked(id));
        var service = Read(solver, "_service");
        var solve = service.GetType().GetMethods().Single(m => m.Name == "Solve" && m.GetParameters().Length == 3);
        var plan = solve.Invoke(service, [(int)fish.ItemId, profile, (int)fish.SpotId])
            ?? throw new InvalidOperationException("AutoHook solver could not produce a strategy with the equipped Fisher's unlocked actions.");
        var preset = assembly.GetType("AutoHook.FishSolverIntegration.SolverPresetBuilder")!.GetMethod("Build")!
            .Invoke(null, [plan, fish.Name])
            ?? throw new InvalidOperationException("AutoHook could not build the native-unlock strategy.");
        // The builder enables these optional improvements independently of the solver's profile.
        // Normal Mooch and the profile's Patience strategy remain intact when they are unavailable.
        foreach (var fishConfig in ((IEnumerable)Read(preset, "ListOfFish")).Cast<object>())
            if (!(bool)Read(skills, "MoochII")) Write(Read(Read(fishConfig, "Mooch"), "Mooch2"), "Enabled", false);
        var casts = Read(preset, "AutoCastsCfg");
        if (!(bool)Read(skills, "MoochII")) Write(Read(Read(casts, "CastMooch"), "Mooch2"), "Enabled", false);
        if (!(bool)Read(skills, "MakeshiftBait")) Write(Read(casts, "CastMakeShiftBait"), "Enabled", false);
        if (!(bool)Read(skills, "PatienceII") && (bool)Read(skills, "PatienceI"))
            Write(Read(casts, "CastPatience"), "Id", 4102u);
        Plugin.Log.Information($"[FishCollection] Native solver actions: {string.Join(", ", FishCollectionPolicy.SolverSkillActions.Keys.Select(name => name + "=" + Read(skills, name)))}; archetype={Read(plan, "Archetype")}; hold={Read(plan, "HoldMode")}");
        return preset;
    }

    private void Register(object preset)
    {
        Write(preset, "UniqueId", Guid.NewGuid());
        owned.Add(preset); // Record before registration so even partial registration is cleaned up.
        presets!.GetType().GetMethod("RegisterPreset", Members)!.Invoke(presets, [preset, false]);
    }

    private void ImportOcean(Assembly assembly, CollectionFish fish, string prefix)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("VERMAXION.Resources.ocean-collection-presets.json")!;
        using var reader = new System.IO.StreamReader(stream);
        var all = (JArray)JObject.Parse(reader.ReadToEnd())["presets"]!;
        var targetNames = new Dictionary<uint, string>
        {
            [29788] = "SOTHIS", [29789] = "CORAL MANTA", [29790] = "STONESCALE", [29791] = "ELASMOSAURUS",
            [32074] = "HAFGUFA", [32094] = "SEAFARING TOAD", [32114] = "PLACODUS", [40540] = "TANIWHA",
            [40560] = "GLASS DRAGON", [40580] = "HELLS CLAW", [40600] = "JEWEL", [51228] = "AKUPARA", [51247] = "MANASVIN",
        };
        var target = all.OfType<JObject>().Single(p => ((string)p["PresetName"]!).EndsWith(targetNames[fish.ItemId], StringComparison.Ordinal));
        var baseName = ((string)target["PresetName"]!).Split("Spectral", StringSplitOptions.None)[0];
        var chosen = all.OfType<JObject>().Where(p => ((string)p["PresetName"]!).StartsWith(baseName, StringComparison.Ordinal)).ToArray();
        var nameMap = chosen.ToDictionary(p => (string)p["PresetName"]!, p => prefix + (string)p["PresetName"]!);
        var normalName = chosen.Select(p => (string)p["PresetName"]!).Single(n => n.EndsWith("Normal", StringComparison.Ordinal));
        var presetType = assembly.GetType("AutoHook.Presets.Config.CustomPresetConfig")!;
        foreach (var source in chosen.OrderBy(p => (string)p["PresetName"]! == normalName ? 0 : 1))
        {
            var clone = (JObject)source.DeepClone();
            clone["PresetName"] = nameMap[(string)source["PresetName"]!];
            foreach (var property in clone.Descendants().OfType<JProperty>().Where(p => p.Name == "PresetToSwap"))
            {
                var old = (string?)property.Value;
                if (old is null or "-") continue;
                // Post-spectral/POINTS references in the source folder must never select somebody
                // else's preset. Return to this attempt's owned normal strategy instead.
                property.Value = nameMap.GetValueOrDefault(old, nameMap[normalName]);
            }
            foreach (var property in clone.Descendants().OfType<JProperty>().Where(p => p.Name == "UniqueId" && p.Parent != clone &&
                         p.Parent?["Name"] == null)) property.Value = Guid.NewGuid().ToString();
            Register(JsonConvert.DeserializeObject(clone.ToString(), presetType)!);
        }
    }

    private static IEnumerable<uint> Baits(object preset)
    {
        var json = JObject.FromObject(preset);
        foreach (var hook in (JArray)json["ListOfBaits"]!)
            if ((int?)hook["BaitFish"]?["Id"] is > 0 and var id) yield return (uint)id;
        foreach (var obj in json.Descendants().OfType<JObject>())
        {
            if ((bool?)obj["ForceBaitSwap"] == true && (int?)obj["ForcedBaitId"] is > 0 and var forced) yield return (uint)forced;
            if ((bool?)obj["SwapBait"] == true && (int?)obj["BaitToSwap"]?["Id"] is > 0 and var bait) yield return (uint)bait;
        }
    }
    private static void ValidateActions(JObject json)
    {
        foreach (var obj in json.Descendants().OfType<JObject>())
            if (obj.Parent is not JProperty { Name: "CastCordial" or "CastFood" } && (bool?)obj["Enabled"] == true &&
                (uint?)obj["Id"] is > 0 and var id && !FishCollectionObservation.ActionUnlocked(id))
                throw LockedAction(id);
        foreach (var obj in json.Descendants().OfType<JObject>().Where(o => (bool?)o["HooksetEnabled"] == true))
            if ((uint?)obj["HooksetType"] is > 0 and var hook && !FishCollectionObservation.ActionUnlocked(hook))
                throw LockedAction(hook);
    }
    private static InvalidOperationException LockedAction(uint id)
    {
        var action = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>().GetRow(id);
        return new InvalidOperationException($"Strategy requires {action.Name.ExtractText()} (action {id}, level {action.ClassJobLevel}); its native level or quest unlock is missing.");
    }
    private void Select(object preset)
    {
        presets!.GetType().GetMethod("Select", Members)!.Invoke(presets, [preset, "IPC"]);
        if (!ReferenceEquals(Read(presets, "SelectedPreset"), preset) || (string)Read(presets, "SelectedGuid") != ((Guid)Read(preset, "UniqueId")).ToString())
            throw new InvalidOperationException("Exact collection preset selection was not verified.");
    }
    public bool StartFishing()
    {
        if (!owned.Any(p => ReferenceEquals(p, ReadNullable(presets!, "SelectedPreset"))))
            throw new InvalidOperationException("Collection preset ownership was lost.");
        if (!ActionsAllowed)
        {
            AllowCasts(true);
            SetEnabled(true);
            return false;
        }
        var playerState = Read(world!, "Player");
        var statuses = (IDictionary)Read(playerState, "ActionStatus");
        var castKey = ((ulong)(byte)ActionType.Action << 32) | 289u;
        uint? cachedStatus = statuses.Contains(castKey) ? Convert.ToUInt32(statuses[castKey]) : null;
        var blocked = (bool)Read(playerState, "BlockCasting");
        var cachedAvailable = (bool)(world!.GetType().GetMethod("IsCastAvailable", Members)?.Invoke(world, null)
            ?? throw new InvalidOperationException("AutoHook Cast availability is unavailable."));
        var action = ActionManager.Instance();
        uint? nativeStatus = action == null ? null : action->GetActionStatus(ActionType.Action, 289);
        var framework = EventFramework.Instance();
        var fishing = framework == null ? null : framework->EventHandlerModule.FishingEventHandler;
        var player = Plugin.ObjectTable.LocalPlayer;
        StartDiagnostics = $"cachedCastStatus={cachedStatus?.ToString() ?? "unavailable"}; cachedCastAvailable={cachedAvailable}; blockCasting={blocked}; nativeCastStatus={nativeStatus?.ToString() ?? "unavailable"}; canFish={(fishing == null ? "unavailable" : fishing->CanFish.ToString())}; mounted={Plugin.Condition[ConditionFlag.Mounted]}; bait={PlayerState.Instance()->FishingBait}; position={player?.Position}; rotation={player?.Rotation:F4}";
        if (blocked) throw new InvalidOperationException("AutoHook is blocking casting; " + StartDiagnostics);
        if (nativeStatus != 0) throw new InvalidOperationException("Native Cast is unavailable before AutoHook start; " + StartDiagnostics);
        // Use /ahstart's own gate, including its cached cooldown check.
        if (!cachedAvailable) return false;
        Plugin.Log.Information($"[FishCollection] AutoHook start readiness: {StartDiagnostics}");
        if (!CommandHelper.TrySendCommand("/ahstart")) throw new InvalidOperationException("AutoHook start was rejected.");
        rodDownSince = default; quitSent = false;
        return true;
    }
    public void StopFutureCasts()
    {
        if (!OwnsState) return;
        SetAutoStart(false);
        AllowCasts(false);
    }
    public void AllowCasts(bool allow)
    {
        ActionsAllowed = allow;
        foreach (var preset in owned)
        {
            var casts = Read(preset, "AutoCastsCfg");
            var saved = castSettings.GetValueOrDefault(preset);
            Write(casts, "EnableAll", allow && saved.All);
            Write(Read(casts, "CastLine"), "Enabled", allow && saved.Line);
            Write(Read(casts, "CastMooch"), "Enabled", allow && saved.Mooch);
        }
    }
    public bool Stow()
    {
        var framework = EventFramework.Instance();
        var fishing = framework == null ? null : framework->EventHandlerModule.FishingEventHandler;
        if (!Plugin.ClientState.IsLoggedIn || Plugin.PlayerState.ContentId != character) return true;
        if (fishing == null) throw new InvalidOperationException("Native rod state is unavailable; cleanup retains ownership.");
        if (fishing->State == FishingState.None && !Plugin.Condition[ConditionFlag.Fishing])
        {
            if (rodDownSince == default)
            {
                SetEnabled(false);
                rodDownSince = DateTimeOffset.UtcNow;
            }
            return DateTimeOffset.UtcNow - rodDownSince >= TimeSpan.FromMilliseconds(Math.Max(1500,
                Math.Max(Convert.ToDouble(Read(config!, "DelayBetweenCastsMin")), Convert.ToDouble(Read(config!, "DelayBetweenCastsMax"))) + 500));
        }
        rodDownSince = default;
        // AutoHook keeps hooking the eligible cast already in flight. Quit becomes usable only
        // after it settles; disabling the plugin earlier would discard that cast's hook handling.
        var manager = ActionManager.Instance();
        if ((fishing->State is FishingState.PoleReady || !Plugin.Condition[ConditionFlag.Fishing]) &&
            !quitSent && manager != null && manager->GetActionStatus(ActionType.Action, 299) == 0)
        {
            quitSent = manager->UseAction(ActionType.Action, 299);
            if (quitSent)
                Plugin.Log.Information($"[FishCollection] Rod stow requested after cast settlement; nativeRod={fishing->State}; nativeFishing={Plugin.Condition[ConditionFlag.Fishing]}");
        }
        return false;
    }
    public bool Restore(bool unloading = false)
    {
        if (!OwnsState) return true;
        var baitVerified = true;
        if (Plugin.ClientState.IsLoggedIn && Plugin.PlayerState.ContentId == character && originalBait > 0 &&
            FishCollectionObservation.Count(originalBait) > 0 && PlayerState.Instance()->FishingBait != originalBait)
        {
            var framework = EventFramework.Instance();
            if (framework == null || framework->EventHandlerModule.FishingEventHandler == null)
                throw new InvalidOperationException("Original bait restoration requires native fishing state.");
            if (!baitRestoreSent)
            {
                framework->EventHandlerModule.FishingEventHandler->ChangeBait((int)originalBait);
                baitRestoreSent = true;
                Plugin.Log.Information($"[FishCollection] Original bait restoration requested; item={originalBait}; waiting for native receipt");
            }
            baitVerified = false;
            if (!unloading) return false;
        }
        SetAutoStart(false);
        SetEnabled(false);
        var selected = ReadNullable(presets!, "SelectedPreset");
        if (selected == null || owned.Any(p => ReferenceEquals(p, selected)))
            presets!.GetType().GetMethod("Select", Members)!.Invoke(presets, [originalPreset, "IPC"]);
        foreach (var preset in owned.ToArray())
        {
            var id = (Guid)Read(preset, "UniqueId");
            presets!.GetType().GetMethod("RemovePreset", Members)!.Invoke(presets, [id]);
            if (((IEnumerable)Read(presets, "CustomPresets")).Cast<object>().Any(p => (Guid)Read(p, "UniqueId") == id))
                throw new InvalidOperationException("Owned preset removal is not settled.");
            owned.Remove(preset);
        }
        Write(config!, "AutoOceanFish", autoOcean);
        Write(config!, "AutoCollectablesEnabled", collectables);
        Save();
        SetAutoStart(autoStart);
        SetEnabled(enabled);
        config = presets = originalPreset = world = null;
        castSettings.Clear(); ActionsAllowed = false; Cordials = [];
        rodDownSince = default;
        quitSent = baitRestoreSent = false;
        RequiredBait = [];
        return baitVerified;
    }
    public void Dispose()
    {
        try
        {
            StopFutureCasts();
            if (OwnsState)
            {
                SetEnabled(false);
                if (!Restore(unloading: true))
                    Plugin.Log.Warning("Collection AutoHook state restored at unload; native bait receipt remains unverified");
            }
        }
        catch (Exception error) { Plugin.Log.Error(error, "Collection AutoHook cleanup at unload remains unverified"); }
    }
}
