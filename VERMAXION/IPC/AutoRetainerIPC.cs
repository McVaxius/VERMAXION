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
