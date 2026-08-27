using System;
using System.Reflection;
using System.Runtime.Loader;
using Dalamud.Plugin;
using ECommons.Reflection;

namespace VERMAXION.Services;

internal static class DependencyConfigurationInspector
{
    private const string XaSlaveInternalName = "XASlave";

    public static bool TryReadTextAdvanceEnabled(
        IDalamudPluginInterface pluginInterface,
        out bool enabled,
        out string status)
    {
        enabled = false;
        try
        {
            enabled = pluginInterface.GetIpcSubscriber<bool>("TextAdvance.IsEnabled").InvokeFunc();
            status = $"TextAdvance is {(enabled ? "enabled" : "disabled")}.";
            return true;
        }
        catch (Exception ex)
        {
            status = $"TextAdvance enabled state could not be read: {ex.Message}";
            return false;
        }
    }

    public static bool TryReadXaSlaveSkipDialogueEnabled(out bool enabled, out string status)
    {
        enabled = false;
        try
        {
            if (!DalamudReflector.TryGetDalamudPlugin(
                    XaSlaveInternalName,
                    out object xaSlavePlugin,
                    out AssemblyLoadContext? _,
                    true,
                    true) ||
                xaSlavePlugin == null)
            {
                status = "XA Slave is not loaded.";
                return false;
            }

            const BindingFlags instanceFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var pluginType = xaSlavePlugin.GetType();
            var configuration = pluginType.GetProperty("Configuration", instanceFlags)?.GetValue(xaSlavePlugin) ??
                                pluginType.GetField("Configuration", instanceFlags)?.GetValue(xaSlavePlugin);
            if (configuration == null)
            {
                status = "XA Slave Configuration is not available.";
                return false;
            }

            var configurationType = configuration.GetType();
            var property = configurationType.GetProperty("AutoSkipDialogueEnabled", instanceFlags);
            if (property != null && property.PropertyType == typeof(bool) && property.CanRead)
            {
                enabled = (bool)property.GetValue(configuration)!;
                status = $"XA Slave Skip Dialogue is {(enabled ? "enabled" : "disabled")}.";
                return true;
            }

            var field = configurationType.GetField("AutoSkipDialogueEnabled", instanceFlags);
            if (field != null && field.FieldType == typeof(bool))
            {
                enabled = (bool)field.GetValue(configuration)!;
                status = $"XA Slave Skip Dialogue is {(enabled ? "enabled" : "disabled")}.";
                return true;
            }

            status = "XA Slave AutoSkipDialogueEnabled Boolean setting is not readable.";
            return false;
        }
        catch (Exception ex)
        {
            status = $"XA Slave Skip Dialogue state could not be read: {ex.Message}";
            return false;
        }
    }
}
