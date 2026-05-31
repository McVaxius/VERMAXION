using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using Dalamud.Plugin.Services;
using ECommons.Reflection;

namespace VERMAXION.Services;

public sealed class SaucyMiniCactpotService
{
    private const string SaucyInternalName = "Saucy";
    private const string MiniCactpotModuleName = "MiniCactpot";

    private readonly IPluginLog log;
    private object? activeConfig;
    private FieldInfo? activeAutoMiniField;
    private FieldInfo? activeEnabledModulesField;
    private bool runActive;
    private bool restoreNeeded;
    private bool priorAutoMiniCactpot;
    private bool priorModuleEnabled;

    public SaucyMiniCactpotService(IPluginLog log)
    {
        this.log = log;
    }

    public bool BeginMiniCactpotRun(bool requireSaucy, out string status)
    {
        EndMiniCactpotRun("new Mini Cactpot run");

        if (!TryGetSaucyConfig(out var config, out var autoMiniField, out var enabledModulesField, out status))
        {
            status = $"Saucy unavailable: {status}";
            return !requireSaucy;
        }

        if (!TryGetEnabledModules(config, enabledModulesField, out var enabledModules, out status))
        {
            status = $"Saucy unavailable: {status}";
            return !requireSaucy;
        }

        try
        {
            priorAutoMiniCactpot = autoMiniField.GetValue(config) is true;
            priorModuleEnabled = enabledModules.Contains(MiniCactpotModuleName);
            activeConfig = config;
            activeAutoMiniField = autoMiniField;
            activeEnabledModulesField = enabledModulesField;
            runActive = true;

            var changed = false;
            if (!priorAutoMiniCactpot)
            {
                autoMiniField.SetValue(config, true);
                changed = true;
            }

            if (!priorModuleEnabled)
            {
                enabledModules.Add(MiniCactpotModuleName);
                changed = true;
            }

            restoreNeeded = changed;
            if (changed)
            {
                TrySaveSaucyConfig(config);
                status = "Saucy MiniCactpot module temporarily enabled for VERMAXION Mini Cactpot.";
            }
            else
            {
                status = "Saucy MiniCactpot module already enabled.";
            }

            log.Information($"[Cactpot] {status}");
            return true;
        }
        catch (Exception ex)
        {
            EndMiniCactpotRun("failed Saucy preparation");
            status = $"Saucy MiniCactpot could not be enabled: {ex.Message}";
            log.Error($"[Cactpot] {status}");
            return !requireSaucy;
        }
    }

    public void EndMiniCactpotRun(string reason)
    {
        if (!runActive)
            return;

        try
        {
            if (restoreNeeded &&
                activeConfig != null &&
                activeAutoMiniField != null &&
                activeEnabledModulesField != null &&
                TryGetEnabledModules(activeConfig, activeEnabledModulesField, out var enabledModules, out _))
            {
                activeAutoMiniField.SetValue(activeConfig, priorAutoMiniCactpot);

                if (priorModuleEnabled)
                {
                    if (!enabledModules.Contains(MiniCactpotModuleName))
                        enabledModules.Add(MiniCactpotModuleName);
                }
                else
                {
                    while (enabledModules.Contains(MiniCactpotModuleName))
                        enabledModules.Remove(MiniCactpotModuleName);
                }

                TrySaveSaucyConfig(activeConfig);
                log.Information($"[Cactpot] Restored prior Saucy MiniCactpot state after {reason}.");
            }
        }
        catch (Exception ex)
        {
            log.Warning($"[Cactpot] Failed to restore Saucy MiniCactpot state after {reason}: {ex.Message}");
        }
        finally
        {
            activeConfig = null;
            activeAutoMiniField = null;
            activeEnabledModulesField = null;
            runActive = false;
            restoreNeeded = false;
            priorAutoMiniCactpot = false;
            priorModuleEnabled = false;
        }
    }

    private static bool TryGetSaucyConfig(
        out object config,
        out FieldInfo autoMiniField,
        out FieldInfo enabledModulesField,
        out string status)
    {
        config = null!;
        autoMiniField = null!;
        enabledModulesField = null!;

        if (!DalamudReflector.TryGetDalamudPlugin(SaucyInternalName, out object saucyPlugin, out AssemblyLoadContext? _, true, true) ||
            saucyPlugin == null)
        {
            status = "Saucy plugin is not loaded.";
            return false;
        }

        var saucyType = saucyPlugin.GetType();
        var configProperty = saucyType.GetProperty("C", BindingFlags.Public | BindingFlags.Static);
        config = configProperty?.GetValue(null) ?? null!;
        if (config == null)
        {
            status = "Saucy config was not available.";
            return false;
        }

        var configType = config.GetType();
        autoMiniField = configType.GetField("EnableAutoMiniCactpot", BindingFlags.Public | BindingFlags.Instance) ?? null!;
        enabledModulesField = configType.GetField("EnabledModules", BindingFlags.Public | BindingFlags.Instance) ?? null!;
        if (autoMiniField == null || enabledModulesField == null)
        {
            status = "Saucy config fields EnableAutoMiniCactpot or EnabledModules were not available.";
            return false;
        }

        status = "Saucy config available.";
        return true;
    }

    private static bool TryGetEnabledModules(object config, FieldInfo enabledModulesField, out IList enabledModules, out string status)
    {
        enabledModules = null!;
        status = string.Empty;

        if (enabledModulesField.GetValue(config) is not IList list)
        {
            status = "Saucy EnabledModules was not a mutable list.";
            return false;
        }

        enabledModules = list;
        return true;
    }

    private void TrySaveSaucyConfig(object config)
    {
        try
        {
            config.GetType().GetMethod("Save", BindingFlags.Public | BindingFlags.Instance)?.Invoke(config, null);
        }
        catch (Exception ex)
        {
            log.Warning($"[Cactpot] Saucy config save failed: {ex.Message}");
        }
    }
}
