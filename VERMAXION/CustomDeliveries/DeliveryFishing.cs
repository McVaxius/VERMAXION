using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Utility;
using ECommons.Reflection;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using VERMAXION.Services;

namespace VERMAXION.CustomDeliveries;

// Uses AutoHook's existing preset/bait capabilities; all game operations run on framework updates.
internal sealed class DeliveryFishing(Plugin plugin) : IDisposable
{
    private const uint CollectAction = 4101;
    private const uint CollectStatus = 805;
    private const uint CastAction = 289;
    private const uint QuitAction = 299;
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private DeliveryRoute? route;
    private TaskCompletionSource? completion;
    private CancellationToken cancellation;
    private object? configuration;
    private object? presets;
    private object? originalPreset;
    private object? activePreset;
    private string ownedPresetGuid = string.Empty;
    private Guid? temporaryPreset;
    private uint originalBait;
    private uint selectedBait;
    private bool originalEnabled;
    private bool originalAutoStart;
    private bool originalCollectables;
    private bool originalCollectStatus;
    private bool originalEnableAll;
    private bool originalCastLine;
    private bool originalCastCollect;
    private bool originalTurnCollectOff;
    private bool originalTurnCollectOffWithoutAnimCancel;
    private bool autoCastSnapshot;
    private bool ownsState;
    private bool cleanupPending;
    private DateTime cleanupStarted;
    private DateTime rodDownSince;
    private bool rodDownConfirmed;
    private bool stopping;
    private DateTime started;
    private DateTime initialCastAt;
    private DateTime collectRequestedAt;
    private bool quitRequested;
    private DateTime lastProgressAt;
    private int lastCount;
    private ulong contentId;

    public string StatusText { get; private set; } = "Idle";
    public bool IsCleanupPending => cleanupPending;

    public Task RunAsync(DeliveryRoute delivery, CancellationToken token)
    {
        if (ownsState || completion != null)
            throw new InvalidOperationException("Custom-delivery fishing already owns AutoHook state.");
        token.ThrowIfCancellationRequested();
        route = delivery;
        cancellation = token;
        completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        started = lastProgressAt = DateTime.UtcNow;
        initialCastAt = DateTime.MinValue;
        collectRequestedAt = DateTime.MinValue;
        quitRequested = false;
        lastCount = -1;
        contentId = Plugin.PlayerState.ContentId;
        stopping = false;
        rodDownConfirmed = false;
        rodDownSince = DateTime.MinValue;
        StatusText = "Checking AutoHook, bait, fishing gear and casting position";
        return completion.Task;
    }

    public unsafe void Update()
    {
        if (cleanupPending)
        {
            try { UpdateCleanup(); }
            catch (Exception error)
            {
                StatusText = $"Fishing cleanup is pending: {error.GetBaseException().Message}";
                if (cleanupStarted != DateTime.MaxValue)
                {
                    Plugin.Log.Error($"[CustomDeliveries][Fishing] {StatusText}");
                    Plugin.ChatGui.Print($"[Vermaxion] {StatusText}");
                    cleanupStarted = DateTime.MaxValue;
                }
            }
            return;
        }
        if (completion == null || route == null)
            return;
        try
        {
            cancellation.ThrowIfCancellationRequested();
            if (!Plugin.ClientState.IsLoggedIn || Plugin.PlayerState.ContentId != contentId)
                throw new InvalidOperationException("Character disconnected or changed during custom-delivery fishing.");
            var fish = route.Npc.FishData ?? throw new InvalidOperationException("Fishing location is unknown.");
            if (fish.IsSpearFish)
                throw new InvalidOperationException("This delivery requires a spearfishing preset and position; rod fishing cannot start here.");
            if (Plugin.ClientState.TerritoryType != fish.TerritoryTypeId)
                throw new InvalidOperationException("The character is outside the required fishing territory.");
            var player = Plugin.ObjectTable.LocalPlayer;
            var manager = ActionManager.Instance();
            var framework = EventFramework.Instance();
            if (player == null || manager == null || framework == null || framework->EventHandlerModule.FishingEventHandler == null)
                throw new InvalidOperationException("Fishing state is unavailable.");
            var fishing = framework->EventHandlerModule.FishingEventHandler;
            var count = Game.NumItemsInInventory(fish.FishItemId, checked((short)route.MinCollectibility));
            if (count != lastCount)
            {
                lastCount = count;
                lastProgressAt = DateTime.UtcNow;
                Plugin.Log.Information($"[CustomDeliveries][Fishing] item={fish.FishItemId}, qualifying={count}/{route.Count}, minimum={route.MinCollectibility}");
            }
            if (count >= route.Count)
            {
                if (!stopping)
                {
                    stopping = true;
                    started = DateTime.UtcNow;
                    if (ownsState)
                    {
                        SetAutoStart(false);
                        SetEnabled(false);
                        DisableOwnedCasts();
                    }
                }
                StatusText = $"Required collectibles reached: {count}/{route.Count}; stopping fishing";
                if (fishing->State == FishingState.None && !Plugin.Condition[ConditionFlag.Fishing])
                {
                    if (rodDownSince == DateTime.MinValue) rodDownSince = DateTime.UtcNow;
                    if (DateTime.UtcNow - rodDownSince >= QueuedCastDelay())
                    {
                        rodDownConfirmed = true;
                        Plugin.Log.Information("[CustomDeliveries][Fishing] Required inventory verified; rod put away before turn-in.");
                        route = null;
                        var finished = completion;
                        completion = null;
                        finished.TrySetResult();
                        return;
                    }
                }
                else rodDownSince = DateTime.MinValue;
                if (DateTime.UtcNow - started > TimeSpan.FromSeconds(30))
                    throw new InvalidOperationException("The required inventory was reached, but fishing did not stop within 30 seconds.");
                if (!quitRequested && manager->GetActionStatus(ActionType.Action, QuitAction) == 0)
                    quitRequested = manager->UseAction(ActionType.Action, QuitAction);
                return;
            }
            if (player.ClassJob.RowId != 18)
                throw new InvalidOperationException("Select an eligible Fisher gearset with a fishing rod.");
            var inventory = InventoryManager.Instance();
            var equipment = inventory == null ? null : inventory->GetInventoryContainer(InventoryType.EquippedItems);
            var rod = equipment == null ? null : equipment->GetInventorySlot(0);
            if (rod == null || rod->ItemId == 0 || rod->Condition == 0)
                throw new InvalidOperationException("A usable fishing rod must be equipped; repair missing or broken gear.");
            if (DateTime.UtcNow - lastProgressAt > TimeSpan.FromMinutes(10))
                throw new InvalidOperationException("No qualifying fish arrived for ten minutes; check bait, gathering gear, preset and position.");
            if (!ownsState)
            {
                if (Plugin.Condition[ConditionFlag.Fishing])
                    throw new InvalidOperationException("Another fishing session is already active; stop it before custom deliveries.");
                if (!fishing->CanFish)
                    throw new InvalidOperationException("Casting is unavailable at this position or facing; move to the fishing hole's shoreline with suitable gear.");
                BeginAutoHook();
            }
            if (GameHelpers.GetInventoryItemCount(selectedBait) == 0)
                throw new InvalidOperationException($"Required bait {GameHelpers.GetItemName(selectedBait)} is missing or exhausted.");
            var playerState = PlayerState.Instance();
            if (playerState == null) throw new InvalidOperationException("Player bait state is unavailable.");
            if (playerState->FishingBait != selectedBait)
            {
                if (DateTime.UtcNow - started > TimeSpan.FromSeconds(5))
                    throw new InvalidOperationException("Configured bait selection was not verified in the game state.");
                return;
            }
            StatusText = $"AutoHook fishing: {count}/{route.Count} qualifying collectibles";
            ConfirmRequestedCollectible(fish.FishItemId);
            if (initialCastAt == DateTime.MinValue)
            {
                if (!player.StatusList.Any(status => status.StatusId == CollectStatus))
                {
                    if (collectRequestedAt != DateTime.MinValue)
                    {
                        if (DateTime.UtcNow - collectRequestedAt > TimeSpan.FromSeconds(5))
                            throw new InvalidOperationException("Collect was dispatched but its status was not observed.");
                        return;
                    }
                    if (manager->GetActionStatus(ActionType.Action, CollectAction) != 0)
                        throw new InvalidOperationException("Collect is unavailable; unlock the action and equip suitable Fisher gear.");
                    if (!manager->UseAction(ActionType.Action, CollectAction))
                        throw new InvalidOperationException("Could not enable Collect for custom-delivery fishing.");
                    collectRequestedAt = DateTime.UtcNow;
                    return;
                }
                if (manager->GetActionStatus(ActionType.Action, CastAction) != 0)
                {
                    if (DateTime.UtcNow - (collectRequestedAt == DateTime.MinValue ? started : collectRequestedAt) > TimeSpan.FromSeconds(5))
                        throw new InvalidOperationException("Casting is unavailable after selecting bait and enabling Collect; check bait, gear and facing.");
                    return; // Bait and Collect animations must settle before the one initial cast.
                }
                if (!CommandHelper.TrySendCommand("/ahstart"))
                    throw new InvalidOperationException("AutoHook did not accept /ahstart.");
                initialCastAt = DateTime.UtcNow;
                Plugin.Log.Information("[CustomDeliveries][Fishing] AutoHook start dispatched after casting and Collect readiness.");
            }
            else if (!Plugin.Condition[ConditionFlag.Fishing] && DateTime.UtcNow - initialCastAt > TimeSpan.FromSeconds(15))
                throw new InvalidOperationException("AutoHook did not enter fishing; check the selected preset, bait and casting position.");
        }
        catch (Exception error)
        {
            var failed = completion;
            completion = null;
            route = null;
            var cleanup = BeginCleanup();
            StatusText = error.Message + cleanup;
            Plugin.Log.Warning($"[CustomDeliveries][Fishing] {StatusText}");
            if (error is OperationCanceledException)
                failed?.TrySetCanceled(cancellation);
            else
                failed?.TrySetException(new InvalidOperationException(StatusText, error));
        }
    }

    private unsafe void BeginAutoHook()
    {
        if (!DalamudReflector.TryGetDalamudPlugin("AutoHook", out object hook, out AssemblyLoadContext? _, true, true) || hook == null)
            throw new InvalidOperationException("AutoHook must be loaded for custom-delivery fishing.");
        var type = hook.GetType().Assembly.GetType("AutoHook.Presets.Config.Configuration")
            ?? throw new InvalidOperationException("Current AutoHook configuration is unavailable.");
        configuration = type.GetProperty("C", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
            ?? throw new InvalidOperationException("AutoHook configuration C is unavailable.");
        presets = Read(configuration, "HookPresets");
        originalPreset = presets.GetType().GetProperty("SelectedPreset", Members)?.GetValue(presets);
        originalEnabled = Plugin.PluginInterface.GetIpcSubscriber<bool>("AutoHook.GetPluginState").InvokeFunc();
        originalAutoStart = Plugin.PluginInterface.GetIpcSubscriber<bool>("AutoHook.GetAutoStartFishing").InvokeFunc();
        originalCollectables = (bool)Read(configuration, "AutoCollectablesEnabled");
        var playerState = PlayerState.Instance();
        if (playerState == null) throw new InvalidOperationException("Player bait state is unavailable.");
        originalBait = playerState->FishingBait;
        originalCollectStatus = Plugin.ObjectTable.LocalPlayer!.StatusList.Any(status => status.StatusId == CollectStatus);
        var settings = plugin.ConfigManager.GetActiveConfig().CustomDeliveriesSettings;
        selectedBait = settings.FishingBaitId;
        if (GameHelpers.GetInventoryItemCount(selectedBait) == 0)
            throw new InvalidOperationException($"Missing custom-delivery bait {GameHelpers.GetItemName(selectedBait)} ({selectedBait}); keep it in inventory before fishing.");
        // Snapshot is complete before the first mutation, including partially failed IPC writes.
        ownsState = true;
        SetAutoStart(false);
        SetEnabled(false);
        if (!Plugin.PluginInterface.GetIpcSubscriber<uint, bool>("AutoHook.SwapBaitById").InvokeFunc(selectedBait))
            throw new InvalidOperationException($"AutoHook could not equip bait {GameHelpers.GetItemName(selectedBait)}.");
        if (string.IsNullOrWhiteSpace(settings.FishingPresetName))
        {
            if (!Plugin.PluginInterface.GetIpcSubscriber<uint, uint, bool, bool>("AutoHook.CreateAndSelectAutoAnonymousPreset")
                    .InvokeFunc(route!.Npc.FishData!.FishItemId, selectedBait, false))
                throw new InvalidOperationException("AutoHook could not create the requested fish/bait preset.");
            activePreset = Read(presets, "SelectedPreset");
            temporaryPreset = (Guid)Read(activePreset, "UniqueId");
        }
        else
        {
            Plugin.PluginInterface.GetIpcSubscriber<string, object>("AutoHook.SetPreset").InvokeAction(settings.FishingPresetName);
            activePreset = Read(presets, "SelectedPreset");
            if ((string)Read(activePreset, "PresetName") != settings.FishingPresetName)
                throw new InvalidOperationException($"AutoHook preset '{settings.FishingPresetName}' was not found.");
        }
        ownedPresetGuid = (string)Read(presets, "SelectedGuid");
        var casts = Read(activePreset, "AutoCastsCfg");
        originalEnableAll = (bool)Read(casts, "EnableAll");
        originalCastLine = (bool)Read(Read(casts, "CastLine"), "Enabled");
        originalCastCollect = (bool)Read(Read(casts, "CastCollect"), "Enabled");
        originalTurnCollectOff = (bool)Read(casts, "TurnCollectOff");
        originalTurnCollectOffWithoutAnimCancel = (bool)Read(casts, "TurnCollectOffWithoutAnimCancel");
        autoCastSnapshot = true;
        Write(casts, "EnableAll", true);
        Write(Read(casts, "CastLine"), "Enabled", true);
        Write(Read(casts, "CastCollect"), "Enabled", true);
        Write(casts, "TurnCollectOff", false);
        Write(casts, "TurnCollectOffWithoutAnimCancel", false);
        Write(configuration, "AutoCollectablesEnabled", true);
        Save();
        SetEnabled(true);
        // Initial start is explicit; the preset owns subsequent casts.
        Plugin.Log.Information($"[CustomDeliveries][Fishing] AutoHook preset selected; item={route!.Npc.FishData!.FishItemId}, bait={selectedBait}, target={route.Count}");
    }

    // AutoHook's general collectible handler does not cover all SatisfactionSupply items.
    private static unsafe void ConfirmRequestedCollectible(uint itemId)
    {
        var addon = (AddonSelectYesno*)Plugin.GameGui.GetAddonByName("SelectYesno").Address;
        if (addon == null || !addon->AtkUnitBase.IsReady || !addon->AtkUnitBase.IsVisible || addon->AtkUnitBase.AtkValuesCount <= 14)
            return;
        if (ItemUtil.GetBaseId(addon->AtkUnitBase.AtkValues[14].UInt).ItemId != itemId)
            return;
        var framework = EventFramework.Instance();
        if (framework == null || framework->EventHandlerModule.FishingEventHandler == null ||
            framework->EventHandlerModule.FishingEventHandler->State != FishingState.ConfirmingCollectable)
            return;
        var evt = new AtkEvent { Listener = &addon->AtkUnitBase.AtkEventListener, Target = &AtkStage.Instance()->AtkEventTarget };
        var data = new AtkEventData();
        addon->ReceiveEvent(AtkEventType.ButtonClick, 0, &evt, &data);
    }

    public void Cancel()
    {
        var pending = completion;
        completion = null;
        route = null;
        var cleanup = BeginCleanup();
        StatusText = "Fishing stopped" + cleanup;
        if (cleanup.Length > 0)
        {
            pending?.TrySetException(new InvalidOperationException(StatusText));
            throw new InvalidOperationException(StatusText);
        }
        pending?.TrySetCanceled();
    }

    private unsafe string BeginCleanup()
    {
        if (!ownsState || cleanupPending)
            return string.Empty;
        string stopFailure = string.Empty;
        try { SetAutoStart(false); }
        catch (Exception error) { stopFailure += $"; could not stop AutoHook autostart: {error.GetBaseException().Message}"; }
        try { SetEnabled(false); }
        catch (Exception error) { stopFailure += $"; could not disable AutoHook: {error.GetBaseException().Message}"; }
        try { DisableOwnedCasts(); }
        catch (Exception error) { stopFailure += $"; could not stop owned preset casts: {error.GetBaseException().Message}"; }
        if (Plugin.ClientState.IsLoggedIn && Plugin.PlayerState.ContentId == contentId && !rodDownConfirmed)
        {
            cleanupPending = true;
            cleanupStarted = DateTime.UtcNow;
            rodDownSince = DateTime.MinValue;
            quitRequested = false;
            return stopFailure;
        }
        return stopFailure + Restore();
    }

    private void DisableOwnedCasts()
    {
        if (!autoCastSnapshot || activePreset == null) return;
        var casts = Read(activePreset, "AutoCastsCfg");
        Write(casts, "EnableAll", false);
        Write(Read(casts, "CastLine"), "Enabled", false);
    }

    private unsafe void UpdateCleanup()
    {
        var disconnected = !Plugin.ClientState.IsLoggedIn || Plugin.PlayerState.ContentId != contentId;
        var framework = EventFramework.Instance();
        var fishing = framework == null ? null : framework->EventHandlerModule.FishingEventHandler;
        if (!disconnected && fishing == null)
            throw new InvalidOperationException("Native rod state is unavailable; AutoHook remains disabled.");
        var rodDown = !disconnected && fishing->State == FishingState.None && !Plugin.Condition[ConditionFlag.Fishing];
        if (rodDown)
        {
            if (rodDownSince == DateTime.MinValue) rodDownSince = DateTime.UtcNow;
            // Let AutoHook's already queued actions drain before restoring its previous active state.
            if (DateTime.UtcNow - rodDownSince < QueuedCastDelay())
                return;
        }
        else if (!disconnected)
        {
            if (rodDownSince != DateTime.MinValue) quitRequested = false;
            rodDownSince = DateTime.MinValue;
        }
        if (disconnected || (rodDown && rodDownSince != DateTime.MinValue))
        {
            cleanupPending = false;
            var failure = Restore();
            StatusText = failure.Length == 0 ? "Fishing stopped; AutoHook state restored" : failure;
            if (failure.Length > 0) Plugin.ChatGui.Print($"[Vermaxion] Custom-delivery cleanup failed{failure}");
            return;
        }
        var manager = ActionManager.Instance();
        if (!quitRequested && manager != null && manager->GetActionStatus(ActionType.Action, QuitAction) == 0)
            quitRequested = manager->UseAction(ActionType.Action, QuitAction);
        StatusText = "Stopping fishing before restoring AutoHook state";
        if (DateTime.UtcNow - cleanupStarted > TimeSpan.FromSeconds(30))
        {
            // Preserve the disabled state and ownership; never restart a cast after failed cancellation.
            StatusText = "Fishing did not stop; AutoHook restoration is pending. Put the rod away to finish cleanup.";
            if (cleanupStarted != DateTime.MaxValue)
            {
                Plugin.Log.Error($"[CustomDeliveries][Fishing] {StatusText}");
                Plugin.ChatGui.Print($"[Vermaxion] {StatusText}");
                cleanupStarted = DateTime.MaxValue;
            }
        }
    }

    private TimeSpan QueuedCastDelay()
        => TimeSpan.FromMilliseconds(configuration == null ? 1000 : Math.Max(1000,
            Math.Max(Convert.ToDouble(Read(configuration, "DelayBetweenCastsMin")),
                Convert.ToDouble(Read(configuration, "DelayBetweenCastsMax"))) + 500));

    private unsafe string Restore()
    {
        if (!ownsState)
            return string.Empty;
        string failures = string.Empty;
        void RestorePart(Action action)
        {
            try { action(); }
            catch (Exception error) { failures += $"; AutoHook cleanup: {error.GetBaseException().Message}"; }
        }
        RestorePart(() => SetAutoStart(false));
        RestorePart(() => SetEnabled(false));
        if (autoCastSnapshot && activePreset != null)
            RestorePart(() =>
            {
                var casts = Read(activePreset, "AutoCastsCfg");
                Write(casts, "EnableAll", originalEnableAll);
                Write(Read(casts, "CastLine"), "Enabled", originalCastLine);
                Write(Read(casts, "CastCollect"), "Enabled", originalCastCollect);
                Write(casts, "TurnCollectOff", originalTurnCollectOff);
                Write(casts, "TurnCollectOffWithoutAnimCancel", originalTurnCollectOffWithoutAnimCancel);
            });
        RestorePart(() => Write(configuration!, "AutoCollectablesEnabled", originalCollectables));
        if (presets != null)
        {
            RestorePart(() =>
            {
                if ((string)Read(presets, "SelectedGuid") == ownedPresetGuid || ownedPresetGuid.Length == 0)
                    presets.GetType().GetMethod("Select", Members)!.Invoke(presets, [originalPreset, "IPC"]);
            });
            if (temporaryPreset.HasValue)
                RestorePart(() => presets.GetType().GetMethod("RemovePreset", Members)!.Invoke(presets, [temporaryPreset.Value]));
        }
        if (Plugin.ClientState.IsLoggedIn && Plugin.PlayerState.ContentId == contentId)
        {
            if (originalBait != 0 && GameHelpers.GetInventoryItemCount(originalBait) > 0)
                RestorePart(() =>
                {
                    // AutoHook's disabled WorldState can still cache the pre-swap bait. Use its native
                    // bait capability directly and verify the real game state when restoring.
                    var framework = EventFramework.Instance();
                    if (framework == null || framework->EventHandlerModule.FishingEventHandler == null)
                        throw new InvalidOperationException("Cannot restore original bait: fishing state is unavailable.");
                    framework->EventHandlerModule.FishingEventHandler->ChangeBait((int)originalBait);
                    var playerState = PlayerState.Instance();
                    if (playerState == null || playerState->FishingBait != originalBait)
                        throw new InvalidOperationException("Original bait restoration was not verified.");
                });
            if (!originalCollectStatus && Plugin.ObjectTable.LocalPlayer?.StatusList.Any(status => status.StatusId == CollectStatus) == true)
                RestorePart(() =>
                {
                    var manager = ActionManager.Instance();
                    if (manager == null || !manager->UseAction(ActionType.Action, CollectAction))
                        throw new InvalidOperationException("Could not restore the original Collect state.");
                });
        }
        RestorePart(Save);
        RestorePart(() => SetAutoStart(originalAutoStart));
        RestorePart(() => SetEnabled(originalEnabled));
        ownsState = autoCastSnapshot = false;
        configuration = presets = originalPreset = activePreset = null;
        temporaryPreset = null;
        ownedPresetGuid = string.Empty;
        Plugin.Log.Information("[CustomDeliveries][Fishing] Task-owned AutoHook state restoration attempted" + failures);
        return failures;
    }

    private static object Read(object owner, string name)
        => owner.GetType().GetField(name, Members)?.GetValue(owner) ??
           owner.GetType().GetProperty(name, Members)?.GetValue(owner) ??
           throw new InvalidOperationException($"AutoHook member {name} is unavailable.");

    private static void Write(object owner, string name, object value)
    {
        if (owner.GetType().GetField(name, Members) is { } field)
            field.SetValue(owner, value);
        else if (owner.GetType().GetProperty(name, Members) is { CanWrite: true } property)
            property.SetValue(owner, value);
        else
            throw new InvalidOperationException($"AutoHook member {name} is not writable.");
    }

    private void Save()
        => configuration!.GetType().GetMethod("Save", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
            null, Type.EmptyTypes, null)!.Invoke(null, null);

    private static void SetAutoStart(bool enabled)
    {
        Plugin.PluginInterface.GetIpcSubscriber<bool, object>("AutoHook.SetAutoStartFishing").InvokeAction(enabled);
        if (Plugin.PluginInterface.GetIpcSubscriber<bool>("AutoHook.GetAutoStartFishing").InvokeFunc() != enabled)
            throw new InvalidOperationException("AutoHook autostart state change was not verified.");
    }

    private static void SetEnabled(bool enabled)
    {
        Plugin.PluginInterface.GetIpcSubscriber<bool, object>("AutoHook.SetPluginState").InvokeAction(enabled);
        if (Plugin.PluginInterface.GetIpcSubscriber<bool>("AutoHook.GetPluginState").InvokeFunc() != enabled)
            throw new InvalidOperationException("AutoHook enabled state change was not verified.");
    }

    public void Dispose()
    {
        try { Cancel(); }
        catch (Exception error) { Plugin.Log.Error(error, "Custom-delivery fishing cancellation at disposal failed"); }
        try
        {
            // Framework updates stop at unload; restore configuration even if native Quit is unsettled.
            if (cleanupPending)
            {
                Plugin.Log.Warning("[CustomDeliveries][Fishing] Unload before rod-down acknowledgement; native cancellation is unverified.");
                cleanupPending = false;
                var failure = Restore();
                if (failure.Length > 0) Plugin.Log.Error(failure);
            }
        }
        catch (Exception error) { Plugin.Log.Error(error, "Custom-delivery fishing disposal failed"); }
    }
}
