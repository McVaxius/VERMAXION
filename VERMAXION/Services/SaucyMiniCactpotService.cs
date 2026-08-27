using System;
using System.Reflection;
using System.Runtime.Loader;
using Dalamud.Plugin.Services;
using ECommons.Reflection;

namespace VERMAXION.Services;

public sealed class SaucyMiniCactpotService
{
    private const string SaucyInternalName = "Saucy";
    private readonly IPluginLog log;
    private SaucyConfigurationAccessor? activeConfiguration;
    private SaucyMiniCactpotConfigurationSnapshot activeSnapshot;
    private bool runActive;

    public SaucyMiniCactpotService(IPluginLog log)
    {
        this.log = log;
    }

    public bool BeginMiniCactpotRun(bool requireSaucy, out string status)
    {
        EndMiniCactpotRun("new Mini Cactpot run");

        if (!TryGetSaucyConfig(out var config, out status))
        {
            status = $"Saucy unavailable: {status}";
            return !requireSaucy;
        }

        if (!SaucyConfigurationAccessor.TryCreate(config, out var configuration, out status))
        {
            status = $"Saucy unavailable: {status}";
            return !requireSaucy;
        }

        try
        {
            activeSnapshot = configuration.CaptureMiniCactpotState();
            activeConfiguration = configuration;
            runActive = true;

            var change = configuration.EnableMiniCactpot();
            LogSaveFailure(change.SaveError);
            if (change.StateChanged)
            {
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
            if (activeConfiguration != null)
            {
                var change = activeConfiguration.RestoreMiniCactpot(activeSnapshot);
                LogSaveFailure(change.SaveError);
                if (change.StateChanged)
                {
                    log.Information($"[Cactpot] Restored prior Saucy MiniCactpot state after {reason}.");
                }
            }
        }
        catch (Exception ex)
        {
            log.Warning($"[Cactpot] Failed to restore Saucy MiniCactpot state after {reason}: {ex.Message}");
        }
        finally
        {
            activeConfiguration = null;
            activeSnapshot = default;
            runActive = false;
        }
    }

    public static bool TryValidateConfiguration(out string status)
    {
        if (!TryGetSaucyConfig(out var config, out status))
            return false;

        return SaucyConfigurationAccessor.TryCreate(config, out _, out status);
    }

    private static bool TryGetSaucyConfig(
        out object config,
        out string status)
    {
        config = null!;

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

        status = "Saucy config available.";
        return true;
    }

    private void LogSaveFailure(Exception? exception)
    {
        if (exception != null)
            log.Warning($"[Cactpot] Saucy config save failed: {exception.Message}");
    }
}
