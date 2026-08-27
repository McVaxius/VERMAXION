using System;
using System.Reflection;
using System.Runtime.Loader;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using ECommons.Reflection;
using VERMAXION.Models;
using VERMAXION.Services;

namespace VERMAXION.IPC;

public sealed class AutoHookIPC
{
    private const string AutoHookInternalName = "AutoHook";
    private readonly IPluginLog log;
    private readonly ICallGateSubscriber<bool> getPluginStateSubscriber;
    private readonly ICallGateSubscriber<bool, object> setPluginStateSubscriber;

    public AutoHookIPC(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.log = log;
        getPluginStateSubscriber = pluginInterface.GetIpcSubscriber<bool>("AutoHook.GetPluginState");
        setPluginStateSubscriber = pluginInterface.GetIpcSubscriber<bool, object>("AutoHook.SetPluginState");
    }

    public PluginStateReadResult ReadPluginState()
    {
        try
        {
            return PluginStateReadResult.Known(getPluginStateSubscriber.InvokeFunc());
        }
        catch (Exception ex)
        {
            log.Warning($"[AutoHook] GetPluginState failed: {ex.Message}");
            return PluginStateReadResult.Failed(ex.Message);
        }
    }

    public bool TrySetPluginState(bool enabled, out string error)
    {
        error = string.Empty;
        try
        {
            setPluginStateSubscriber.InvokeAction(enabled);
            var verification = ReadPluginState();
            if (!verification.Success)
            {
                error = $"SetPluginState({enabled}) was sent but verification failed: {verification.Error}";
                return false;
            }

            if (verification.Enabled != enabled)
            {
                error = $"SetPluginState({enabled}) did not change the verified state.";
                return false;
            }

            log.Information($"[AutoHook] Enabled={enabled} via IPC and verified.");
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            log.Warning($"[AutoHook] SetPluginState({enabled}) failed: {ex.Message}");
            return false;
        }
    }

    public AutoHookAutoOceanFishReadResult ReadAutoOceanFish()
    {
        if (!TryGetConfigurationAccessor(out var accessor, out var error))
            return new AutoHookAutoOceanFishReadResult(false, null, error);

        var result = accessor.Read();
        return new AutoHookAutoOceanFishReadResult(result.Success, result.Value, result.Status);
    }

    public bool TrySynchronizeAutoOceanFish(OceanFishingProvider provider, out string status)
    {
        if (!TryGetConfigurationAccessor(out var accessor, out status))
            return false;

        var expected = OceanFishingProviderPolicy.ExpectedAutoOceanFish(provider);
        var result = accessor.Synchronize(expected);
        status = result.Status;
        if (result.Success)
            log.Information($"[AutoHook] {status}");
        else
            log.Warning($"[AutoHook] {status}");
        return result.Success;
    }

    private static bool TryGetConfigurationAccessor(
        out AutoHookConfigurationAccessor accessor,
        out string status)
    {
        accessor = null!;
        try
        {
            if (!DalamudReflector.TryGetDalamudPlugin(
                    AutoHookInternalName,
                    out object autoHookPlugin,
                    out AssemblyLoadContext? _,
                    true,
                    true) ||
                autoHookPlugin == null)
            {
                status = "AutoHook plugin is not loaded.";
                return false;
            }

            const BindingFlags staticFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            var serviceType = autoHookPlugin.GetType().Assembly.GetType("AutoHook.Service");
            var configuration = serviceType?.GetProperty("Configuration", staticFlags)?.GetValue(null) ??
                                serviceType?.GetField("Configuration", staticFlags)?.GetValue(null);
            if (configuration == null)
            {
                status = "AutoHook Service.Configuration is not available.";
                return false;
            }

            return AutoHookConfigurationAccessor.TryCreate(configuration, out accessor, out status);
        }
        catch (Exception ex)
        {
            status = $"Could not inspect AutoHook AutoOceanFish configuration: {ex.Message}";
            return false;
        }
    }
}

public readonly record struct AutoHookAutoOceanFishReadResult(bool Success, bool? Enabled, string Status);
