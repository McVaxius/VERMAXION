using System;
using System.Reflection;
using System.Runtime.Loader;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using ECommons.Reflection;
using VERMAXION.Models;

namespace VERMAXION.IPC;

public sealed class AutoRetainerIPC
{
    private const string AutoRetainerInternalName = "AutoRetainer";
    private const string MultiModeTypeName = "AutoRetainer.Modules.Multi.MultiMode";

    private readonly IPluginLog log;
    private readonly ICallGateSubscriber<bool> getSuppressedSubscriber;
    private readonly ICallGateSubscriber<bool, object> setSuppressedSubscriber;
    private readonly ICallGateSubscriber<bool> isBusySubscriber;
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
        LastSnapshot = new SuppressionSnapshot(false, false, false);
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
    {
        try
        {
            return isBusySubscriber.InvokeFunc();
        }
        catch (Exception ex)
        {
            log.Warning($"[AR] PluginState.IsBusy failed: {ex.Message}");
            return false;
        }
    }

    public AutoRetainerMultiModeReadResult ReadMultiModeEnabled()
    {
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
