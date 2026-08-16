using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using ECommons.Reflection;
using VERMAXION.Models;

namespace VERMAXION.IPC;

public sealed class AutoRetainerIPC : IAutoRetainerSelectionAccessor
{
    private const string AutoRetainerInternalName = "AutoRetainer";
    private const string MultiModeTypeName = "AutoRetainer.Modules.Multi.MultiMode";

    private readonly IPluginLog log;
    private readonly ICallGateSubscriber<bool> getSuppressedSubscriber;
    private readonly ICallGateSubscriber<bool, object> setSuppressedSubscriber;
    private readonly ICallGateSubscriber<bool> isBusySubscriber;
    private readonly ICallGateSubscriber<bool> getMultiModeEnabledSubscriber;
    private readonly ICallGateSubscriber<bool, object> setMultiModeEnabledSubscriber;
    private bool suppressionOwnedByVermaxion;
    private DateTime lastReleaseAttemptAt = DateTime.MinValue;

    public bool SuppressionOwnedByVermaxion => suppressionOwnedByVermaxion;
    public SuppressionSnapshot LastSnapshot { get; private set; }

    public AutoRetainerIPC(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.log = log;
        getSuppressedSubscriber = pluginInterface.GetIpcSubscriber<bool>("AutoRetainer.GetSuppressed");
        setSuppressedSubscriber = pluginInterface.GetIpcSubscriber<bool, object>("AutoRetainer.SetSuppressed");
        isBusySubscriber = pluginInterface.GetIpcSubscriber<bool>("AutoRetainer.PluginState.IsBusy");
        getMultiModeEnabledSubscriber = pluginInterface.GetIpcSubscriber<bool>("AutoRetainer.GetMultiModeEnabled");
        setMultiModeEnabledSubscriber = pluginInterface.GetIpcSubscriber<bool, object>("AutoRetainer.SetMultiModeEnabled");
        LastSnapshot = new SuppressionSnapshot(false, false, false);
    }

    AutoRetainerSelectionReadResult IAutoRetainerSelectionAccessor.ReadCharacterSelection(
        ulong contentId)
        => ReadCharacterSelection(contentId);

    AutoRetainerSelectionWriteResult IAutoRetainerSelectionAccessor.WriteCharacterSelection(
        ulong contentId,
        bool enabled)
        => WriteCharacterSelection(contentId, enabled);

    internal AutoRetainerSelectionReadResult ReadCharacterSelection(ulong contentId)
    {
        try
        {
            if (!TryGetLoadedAutoRetainer(out var autoRetainerPlugin, out var error))
                return AutoRetainerSelectionReadResult.Failed(error);

            return AutoRetainerSelectionReflection.Read(autoRetainerPlugin, contentId);
        }
        catch (Exception ex)
        {
            return AutoRetainerSelectionReadResult.Failed(ex.Message);
        }
    }

    internal AutoRetainerSelectionWriteResult WriteCharacterSelection(
        ulong contentId,
        bool enabled)
    {
        try
        {
            if (!TryGetLoadedAutoRetainer(out var autoRetainerPlugin, out var error))
                return AutoRetainerSelectionWriteResult.Failed(error);

            return AutoRetainerSelectionReflection.Write(autoRetainerPlugin, contentId, enabled);
        }
        catch (Exception ex)
        {
            return AutoRetainerSelectionWriteResult.Failed(ex.Message);
        }
    }

    private static bool TryGetLoadedAutoRetainer(out object autoRetainerPlugin, out string error)
    {
        if (DalamudReflector.TryGetDalamudPlugin(
                AutoRetainerInternalName,
                out autoRetainerPlugin,
                out AssemblyLoadContext? _,
                true,
                true) &&
            autoRetainerPlugin != null)
        {
            error = string.Empty;
            return true;
        }

        autoRetainerPlugin = null!;
        error = "AutoRetainer was not loaded.";
        return false;
    }

    public AutoRetainerEquipmentReadResult ReadEnabledRetainers(ulong contentId)
    {
        try
        {
            if (!TryGetLoadedAutoRetainer(out var plugin, out var error))
                return AutoRetainerEquipmentReadResult.Failed(error);
            if (!TryGetAutoRetainerConfig(plugin, out var config, out error))
                return AutoRetainerEquipmentReadResult.Failed(error);

            var selected = ReadDictionaryValue(config, "SelectedRetainers", contentId) as IEnumerable;
            var selectedNames = selected == null
                ? new HashSet<string>(StringComparer.Ordinal)
                : selected.Cast<object>()
                    .Select(value => value?.ToString() ?? string.Empty)
                    .Where(value => value.Length > 0)
                    .ToHashSet(StringComparer.Ordinal);
            if (selectedNames.Count == 0)
                return AutoRetainerEquipmentReadResult.Known([]);

            var offlineCharacters = ReadMember(config, "OfflineData") as IEnumerable;
            var character = offlineCharacters?.Cast<object>()
                .FirstOrDefault(value => ReadUInt64(value, "CID") == contentId);
            if (character == null)
                return AutoRetainerEquipmentReadResult.Failed(
                    "AutoRetainer offline data did not contain the current character.");

            var additionalData = ReadMember(config, "AdditionalData") as IDictionary;
            var retainerData = ReadMember(character, "RetainerData") as IEnumerable;
            if (retainerData == null)
                return AutoRetainerEquipmentReadResult.Failed(
                    "AutoRetainer retainer data was not readable.");

            var result = new List<AutoRetainerRetainerSnapshot>();
            foreach (var retainer in retainerData.Cast<object>())
            {
                var name = ReadString(retainer, "Name");
                if (!selectedNames.Contains(name))
                    continue;
                var key = $"#{contentId:X16} {name}";
                var additional = additionalData?.Contains(key) == true
                    ? additionalData[key]
                    : null;
                result.Add(new AutoRetainerRetainerSnapshot(
                    ReadUInt64(retainer, "RetainerID"),
                    name,
                    (uint)ReadInt64(retainer, "Job"),
                    (int)ReadInt64(retainer, "Level"),
                    ReadBool(retainer, "HasVenture"),
                    ReadInt64(retainer, "VentureEndsAt"),
                    additional == null ? -1 : (int)ReadInt64(additional, "Ilvl", -1),
                    additional == null ? -1 : (int)ReadInt64(additional, "Perception", -1)));
            }

            return AutoRetainerEquipmentReadResult.Known(result);
        }
        catch (Exception ex)
        {
            return AutoRetainerEquipmentReadResult.Failed(ex.Message);
        }
    }

    public AutoRetainerCollectOnlyReadResult ReadCollectOnly()
    {
        try
        {
            if (!TryGetLoadedAutoRetainer(out var plugin, out var error))
                return AutoRetainerCollectOnlyReadResult.Failed(error);
            if (!TryGetAutoRetainerConfig(plugin, out var config, out error))
                return AutoRetainerCollectOnlyReadResult.Failed(error);
            return AutoRetainerCollectOnlyReadResult.Known(ReadBool(config, "_dontReassign"));
        }
        catch (Exception ex)
        {
            return AutoRetainerCollectOnlyReadResult.Failed(ex.Message);
        }
    }

    public bool TrySetCollectOnly(bool enabled, out string error)
    {
        error = string.Empty;
        try
        {
            if (!TryGetLoadedAutoRetainer(out var plugin, out error) ||
                !TryGetAutoRetainerConfig(plugin, out var config, out error))
            {
                return false;
            }

            var field = config.GetType().GetField(
                "_dontReassign",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null || field.FieldType != typeof(bool))
            {
                error = "AutoRetainer collect-only field was not writable.";
                return false;
            }

            field.SetValue(config, enabled);
            var verification = ReadCollectOnly();
            if (!verification.Success || verification.Enabled != enabled)
            {
                error = verification.Success
                    ? "AutoRetainer collect-only write did not verify."
                    : verification.Error;
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool TryGetAutoRetainerConfig(
        object plugin,
        out object config,
        out string error)
    {
        var field = plugin.GetType().GetField(
            "config",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        config = field?.GetValue(plugin)!;
        if (config != null)
        {
            error = string.Empty;
            return true;
        }
        error = "AutoRetainer configuration was not readable.";
        return false;
    }

    private static object? ReadDictionaryValue(object owner, string memberName, object key)
    {
        var dictionary = ReadMember(owner, memberName) as IDictionary;
        return dictionary?.Contains(key) == true ? dictionary[key] : null;
    }

    private static object? ReadMember(object owner, string name)
    {
        var type = owner.GetType();
        var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null)
            return field.GetValue(owner);
        return type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(owner);
    }

    private static string ReadString(object owner, string name) =>
        ReadMember(owner, name)?.ToString() ?? string.Empty;

    private static bool ReadBool(object owner, string name) =>
        ReadMember(owner, name) is bool value && value;

    private static ulong ReadUInt64(object owner, string name)
    {
        var value = ReadMember(owner, name);
        return value == null ? 0 : Convert.ToUInt64(value);
    }

    private static long ReadInt64(object owner, string name, long fallback = 0)
    {
        var value = ReadMember(owner, name);
        return value == null ? fallback : Convert.ToInt64(value);
    }

    public SuppressionReadResult ReadSuppression()
    {
        try
        {
            return SuppressionReadResult.Known(getSuppressedSubscriber.InvokeFunc());
        }
        catch (Exception ex)
        {
            log.Warning($"[AR] GetSuppressed failed: {ex.Message}");
            return SuppressionReadResult.Unknown(ex.Message);
        }
    }

    public SuppressionSnapshot GetSuppressionSnapshot()
    {
        var remote = ReadSuppression();
        LastSnapshot = new SuppressionSnapshot(remote.Success, remote.IsSuppressed, suppressionOwnedByVermaxion);
        return LastSnapshot;
    }

    public bool IsBusy()
        => ReadBusyState().Busy;

    public PluginBusyReadResult ReadBusyState()
    {
        try
        {
            return PluginBusyReadResult.Known(isBusySubscriber.InvokeFunc());
        }
        catch (Exception ex)
        {
            log.Warning($"[AR] PluginState.IsBusy failed: {ex.Message}");
            return PluginBusyReadResult.Failed(ex.Message);
        }
    }

    public AutoRetainerMultiModeReadResult ReadMultiModeEnabled()
    {
        try
        {
            return AutoRetainerMultiModeReadResult.Known(getMultiModeEnabledSubscriber.InvokeFunc());
        }
        catch (Exception ipcException)
        {
            log.Warning($"[AR] GetMultiModeEnabled IPC failed; trying local reflection fallback: {ipcException.Message}");
        }

        try
        {
            if (!DalamudReflector.TryGetDalamudPlugin(
                    AutoRetainerInternalName,
                    out var autoRetainerPlugin,
                    out AssemblyLoadContext? _,
                    true,
                    true) ||
                autoRetainerPlugin == null)
            {
                return AutoRetainerMultiModeReadResult.Failed("AutoRetainer was not loaded.");
            }

            var multiModeType = autoRetainerPlugin.GetType().Assembly.GetType(MultiModeTypeName);
            if (multiModeType == null)
                return AutoRetainerMultiModeReadResult.Failed($"{MultiModeTypeName} was not available.");

            var enabledField = multiModeType.GetField("Enabled", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (enabledField?.GetValue(null) is bool fieldValue)
                return AutoRetainerMultiModeReadResult.Known(fieldValue);

            var enabledProperty = multiModeType.GetProperty("Enabled", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (enabledProperty?.GetValue(null) is bool propertyValue)
                return AutoRetainerMultiModeReadResult.Known(propertyValue);

            return AutoRetainerMultiModeReadResult.Failed("MultiMode.Enabled field/property was not readable.");
        }
        catch (Exception ex)
        {
            log.Warning($"[AR] MultiMode.Enabled reflection failed: {ex.Message}");
            return AutoRetainerMultiModeReadResult.Failed(ex.Message);
        }
    }

    public bool TrySetMultiModeEnabled(bool enabled, out string error)
    {
        error = string.Empty;
        try
        {
            setMultiModeEnabledSubscriber.InvokeAction(enabled);
            var verification = ReadMultiModeEnabled();
            if (!verification.Success)
            {
                error = $"SetMultiModeEnabled({enabled}) was sent but verification failed: {verification.Error}";
                log.Warning($"[AR] {error}");
                return false;
            }

            if (verification.Enabled != enabled)
            {
                error = $"SetMultiModeEnabled({enabled}) did not change the verified state.";
                log.Warning($"[AR] {error}");
                return false;
            }

            log.Information($"[AR] MultiMode enabled={enabled} via IPC and verified.");
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            log.Warning($"[AR] SetMultiModeEnabled({enabled}) IPC failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Disables AutoRetainer's movement-stuck detector (BailoutManager.RunStuckDetection, AR 4.6.1.17) by
    /// holding its EzThrottler gate key. That detector has two field-verified defects harmful to a fishing
    /// character: (1) it treats any character stationary &gt;15s while MultiMode is active as "movement
    /// stuck", setting Enabled/WorkshopEnabled=false (silently benching it from retainers AND submarines)
    /// and firing Vnavmesh.Stop()+Lifestream.Abort(); fishing is inherently stationary, so every voyage
    /// feeds it victims and the exclusion is reported only to AR's AnomalyWindow, never the log; (2) its
    /// failure path dereferences Player.Position with no Player.Available guard, so with MultiMode active
    /// and nobody logged in (char select) it throws a NullReferenceException every framework tick and
    /// MultiMode never advances — a soft deadlock. No config flag gates the detector; RunStuckDetection
    /// early-returns unless EzThrottler.Check("NoMoveCheck") passes, so re-arming a very long throttle on
    /// that key is the narrowest off-switch. Re-asserted periodically because AR reloads reset its
    /// throttler. The throttle lives in AR's OWN ECommons instance, reached through AR's load context.
    /// </summary>
    public bool TrySuppressStuckDetection(out string error)
    {
        error = string.Empty;
        try
        {
            if (!TryGetLoadedAutoRetainer(out var plugin, out error))
                return false;

            var context = AssemblyLoadContext.GetLoadContext(plugin.GetType().Assembly);
            var ecommons = context?.Assemblies.FirstOrDefault(
                a => string.Equals(a.GetName().Name, "ECommons", StringComparison.Ordinal));
            var throttler = ecommons?.GetType("ECommons.Throttlers.EzThrottler");
            var method = throttler?.GetMethod(
                "Throttle",
                BindingFlags.Static | BindingFlags.Public,
                [typeof(string), typeof(int), typeof(bool)]);
            if (method == null)
            {
                error = "AutoRetainer's EzThrottler.Throttle(string,int,bool) was not reachable.";
                return false;
            }

            method.Invoke(null, ["NoMoveCheck", 2_000_000_000, true]);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// "Fake-ready" one enabled character so AutoRetainer MultiMode logs it in, even when no venture is
    /// naturally due. AR relogs to whichever enabled character becomes ready soonest (earliest retainer
    /// VentureEndsAt); if none is due during an ocean-fishing registration window, a fishing-enabled
    /// character can sit at the title/char-select screen and miss the boat entirely. Setting one retainer's
    /// VentureEndsAt to now+leadSeconds makes AR treat that character as ready and log it in — the same
    /// mechanism AR's own DebugArtisan "15s"/"1m" buttons use (r.VentureEndsAt = P.Time + n). It never
    /// touches MultiModeEnabled (VMX-owned) and self-corrects: AR overwrites VentureEndsAt from live game
    /// data the next time that character is logged in. Observed effect: title-wedged clients logged in
    /// within ~45s of the write.
    /// </summary>
    public bool TryFakeReadyEnabledCharacter(int leadSeconds, out string who, out string error)
    {
        who = string.Empty;
        error = string.Empty;
        try
        {
            if (!TryGetLoadedAutoRetainer(out var plugin, out error) ||
                !TryGetAutoRetainerConfig(plugin, out var config, out error))
            {
                return false;
            }

            // VentureEndsAt is standard unix seconds (matches DateTimeOffset.UtcNow), so no need to read
            // AutoRetainer's clock. Floor the lead so a mis-passed value can't set a past/near-now time.
            var target = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + Math.Max(10, leadSeconds);

            if (ReadMember(config, "OfflineData") is not IEnumerable offline)
            {
                error = "AutoRetainer OfflineData was not readable.";
                return false;
            }

            foreach (var character in offline.Cast<object>())
            {
                if (character == null || !ReadBool(character, "Enabled"))
                    continue;
                if (ReadMember(character, "RetainerData") is not IEnumerable retainers)
                    continue;
                // Prefer a retainer with a live venture — AR's readiness checks key off HasVenture, so
                // writing VentureEndsAt on a venture-less retainer may not be honored.
                var retainerList = retainers.Cast<object>().Where(r => r != null).ToList();
                var retainer = retainerList.FirstOrDefault(r => ReadBool(r!, "HasVenture")) ?? retainerList.FirstOrDefault();
                if (retainer == null)
                    continue;

                var type = retainer.GetType();
                var field = type.GetField("VentureEndsAt", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null && field.FieldType == typeof(long))
                {
                    field.SetValue(retainer, target);
                    who = ReadString(character, "Name");
                    return true;
                }
                var property = type.GetProperty("VentureEndsAt", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null && property.CanWrite && property.PropertyType == typeof(long))
                {
                    property.SetValue(retainer, target);
                    who = ReadString(character, "Name");
                    return true;
                }
            }

            error = "no enabled character with a writable retainer venture was found.";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Sets Enabled=true on the OfflineData entry matching the content id (the inn-park enable
    /// rollout: a char is AR-enabled only once it has been parked at a known-clean inn location). In-memory
    /// write; AutoRetainer persists it on its own save cycle.</summary>
    public bool TryEnableCharacter(ulong contentId, out string who, out string error)
    {
        who = string.Empty;
        error = string.Empty;
        try
        {
            if (!TryGetLoadedAutoRetainer(out var plugin, out error) ||
                !TryGetAutoRetainerConfig(plugin, out var config, out error))
            {
                return false;
            }
            if (ReadMember(config, "OfflineData") is not IEnumerable offline)
            {
                error = "AutoRetainer OfflineData was not readable.";
                return false;
            }
            var character = offline.Cast<object?>()
                .FirstOrDefault(value => value != null && ReadUInt64(value, "CID") == contentId);
            if (character == null)
            {
                error = $"no OfflineData entry for content id {contentId:X}.";
                return false;
            }
            who = ReadString(character, "Name");
            if (ReadBool(character, "Enabled"))
                return true; // already enabled — idempotent success
            var type = character.GetType();
            var field = type.GetField("Enabled", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null && field.FieldType == typeof(bool))
            {
                field.SetValue(character, true);
                return true;
            }
            var property = type.GetProperty("Enabled", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.CanWrite && property.PropertyType == typeof(bool))
            {
                property.SetValue(character, true);
                return true;
            }
            error = "Enabled member was not writable.";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Reads the earliest VentureEndsAt (unix seconds) across the retainers of the OfflineData
    /// entry matching the content id. Returns false when the char/entry/retainers are unreadable; a char
    /// with no ventures out yields earliest=long.MaxValue.</summary>
    public bool TryReadEarliestVenture(ulong contentId, out long earliestEndsAt, out string error)
    {
        earliestEndsAt = long.MaxValue;
        error = string.Empty;
        try
        {
            if (!TryGetLoadedAutoRetainer(out var plugin, out error) ||
                !TryGetAutoRetainerConfig(plugin, out var config, out error))
            {
                return false;
            }
            if (ReadMember(config, "OfflineData") is not IEnumerable offline)
            {
                error = "AutoRetainer OfflineData was not readable.";
                return false;
            }
            var character = offline.Cast<object?>()
                .FirstOrDefault(value => value != null && ReadUInt64(value, "CID") == contentId);
            if (character == null)
            {
                error = $"no OfflineData entry for content id {contentId:X}.";
                return false;
            }
            if (ReadMember(character, "RetainerData") is not IEnumerable retainers)
                return true; // no retainers — nothing ever due
            foreach (var retainer in retainers.Cast<object?>())
            {
                if (retainer == null || !ReadBool(retainer, "HasVenture"))
                    continue;
                var ends = ReadInt64(retainer, "VentureEndsAt");
                if (ends > 0 && ends < earliestEndsAt)
                    earliestEndsAt = ends;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public bool TryAcquireSuppression()
    {
        var remote = ReadSuppression();
        var action = SuppressionLeasePolicy.DecideAcquire(suppressionOwnedByVermaxion, remote);
        switch (action)
        {
            case SuppressionLeaseAction.None:
                UpdateLastSnapshot(remote);
                return true;
            case SuppressionLeaseAction.PreserveExternal:
                UpdateLastSnapshot(remote);
                log.Information("[AR] Suppression already active; VMX will not own release.");
                return false;
            case SuppressionLeaseAction.WaitForRemote:
                UpdateLastSnapshot(remote);
                log.Warning("[AR] Suppression state unknown; VMX will not acquire or claim ownership.");
                return false;
            case SuppressionLeaseAction.Acquire when suppressionOwnedByVermaxion:
                log.Warning("[AR] VMX ownership was stale because remote suppression was false; reacquiring.");
                break;
        }

        try
        {
            // Remote was confirmed unsuppressed before this write, so VMX owns any
            // suppression that appears even if the write call itself throws.
            suppressionOwnedByVermaxion = true;
            setSuppressedSubscriber.InvokeAction(true);
            var verification = ReadSuppression();
            suppressionOwnedByVermaxion = !verification.Success || verification.IsSuppressed;
            UpdateLastSnapshot(verification);
            if (!verification.Success)
            {
                log.Warning("[AR] SetSuppressed(true) sent but verification is unknown; retaining VMX ownership for recovery.");
                return false;
            }
            if (!verification.IsSuppressed)
            {
                log.Warning("[AR] SetSuppressed(true) did not produce remote suppression.");
                return false;
            }

            lastReleaseAttemptAt = DateTime.MinValue;
            log.Information("[AR] Suppressed AutoRetainer for before-AR tasks and verified remote state.");
            return true;
        }
        catch (Exception ex)
        {
            log.Warning($"[AR] SetSuppressed(true) failed: {ex.Message}");
            var verification = ReadSuppression();
            if (verification.Success && !verification.IsSuppressed)
                suppressionOwnedByVermaxion = false;
            UpdateLastSnapshot(verification);
            return false;
        }
    }

    public bool ReleaseSuppressionIfOwned(bool force = false)
    {
        var now = DateTime.UtcNow;
        if (!force &&
            lastReleaseAttemptAt != DateTime.MinValue &&
            now - lastReleaseAttemptAt < TimeSpan.FromSeconds(2))
        {
            return false;
        }

        lastReleaseAttemptAt = now;
        var remote = ReadSuppression();
        var action = SuppressionLeasePolicy.DecideRelease(suppressionOwnedByVermaxion, remote);
        if (!suppressionOwnedByVermaxion)
        {
            UpdateLastSnapshot(remote);
            lastReleaseAttemptAt = DateTime.MinValue;
            return true;
        }
        if (action == SuppressionLeaseAction.None || action == SuppressionLeaseAction.PreserveExternal)
        {
            UpdateLastSnapshot(remote);
            lastReleaseAttemptAt = DateTime.MinValue;
            return true;
        }
        if (action == SuppressionLeaseAction.ClearStaleOwnership)
        {
            suppressionOwnedByVermaxion = false;
            lastReleaseAttemptAt = DateTime.MinValue;
            UpdateLastSnapshot(remote);
            log.Information("[AR] Cleared stale VMX suppression ownership because remote suppression was already false.");
            return true;
        }
        if (action == SuppressionLeaseAction.WaitForRemote && !force)
        {
            UpdateLastSnapshot(remote);
            return false;
        }

        try
        {
            setSuppressedSubscriber.InvokeAction(false);
            var verification = ReadSuppression();
            if (verification.Success && !verification.IsSuppressed)
            {
                suppressionOwnedByVermaxion = false;
                lastReleaseAttemptAt = DateTime.MinValue;
                UpdateLastSnapshot(verification);
                log.Information("[AR] Released VMX AutoRetainer suppression and verified remote state.");
                return true;
            }

            UpdateLastSnapshot(verification);
            if (force)
            {
                suppressionOwnedByVermaxion = false;
                log.Warning("[AR] Full Stop cleared local VMX suppression ownership without confirmed remote release.");
            }
            else
            {
                log.Warning(verification.Success
                    ? "[AR] Remote suppression remained active after VMX release attempt; retaining ownership."
                    : "[AR] Suppression release verification is unknown; retaining ownership.");
            }

            return false;
        }
        catch (Exception ex)
        {
            log.Warning($"[AR] SetSuppressed(false) failed: {ex.Message}");
            if (force)
            {
                suppressionOwnedByVermaxion = false;
                log.Warning("[AR] Full Stop cleared local VMX suppression ownership after release failure.");
            }

            UpdateLastSnapshot(ReadSuppression());
            return false;
        }
    }

    private void UpdateLastSnapshot(SuppressionReadResult remote)
    {
        LastSnapshot = new SuppressionSnapshot(remote.Success, remote.IsSuppressed, suppressionOwnedByVermaxion);
    }
}
