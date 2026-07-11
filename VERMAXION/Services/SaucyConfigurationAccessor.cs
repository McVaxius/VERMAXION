using System;
using System.Collections;
using System.Reflection;

namespace VERMAXION.Services;

internal sealed class SaucyConfigurationAccessor
{
    private const string MiniCactpotModuleName = "MiniCactpot";

    private readonly object configuration;
    private readonly IList enabledModules;
    private readonly FieldInfo? legacyAutoMiniCactpotField;
    private readonly MethodInfo? saveMethod;

    private SaucyConfigurationAccessor(
        object configuration,
        IList enabledModules,
        FieldInfo? legacyAutoMiniCactpotField,
        MethodInfo? saveMethod)
    {
        this.configuration = configuration;
        this.enabledModules = enabledModules;
        this.legacyAutoMiniCactpotField = legacyAutoMiniCactpotField;
        this.saveMethod = saveMethod;
    }

    public static bool TryCreate(object configuration, out SaucyConfigurationAccessor accessor, out string status)
    {
        accessor = null!;

        var configurationType = configuration.GetType();
        var enabledModulesField = configurationType.GetField("EnabledModules", BindingFlags.Public | BindingFlags.Instance);
        if (enabledModulesField == null)
        {
            status = "Saucy config field EnabledModules was not available.";
            return false;
        }

        if (enabledModulesField.GetValue(configuration) is not IList enabledModules ||
            enabledModules.IsReadOnly ||
            enabledModules.IsFixedSize)
        {
            status = "Saucy EnabledModules was not a mutable list.";
            return false;
        }

        var legacyAutoMiniCactpotField = configurationType.GetField(
            "EnableAutoMiniCactpot",
            BindingFlags.Public | BindingFlags.Instance);
        if (legacyAutoMiniCactpotField != null && legacyAutoMiniCactpotField.FieldType != typeof(bool))
        {
            status = "Saucy legacy EnableAutoMiniCactpot field was not a Boolean.";
            return false;
        }

        accessor = new SaucyConfigurationAccessor(
            configuration,
            enabledModules,
            legacyAutoMiniCactpotField,
            configurationType.GetMethod("Save", BindingFlags.Public | BindingFlags.Instance));
        status = "Saucy config available.";
        return true;
    }

    public SaucyMiniCactpotConfigurationSnapshot CaptureMiniCactpotState()
    {
        var legacyEnabled = legacyAutoMiniCactpotField == null
            ? null
            : (bool?)legacyAutoMiniCactpotField.GetValue(configuration);

        return new SaucyMiniCactpotConfigurationSnapshot(
            enabledModules.Contains(MiniCactpotModuleName),
            legacyEnabled);
    }

    public SaucyConfigurationChange EnableMiniCactpot()
    {
        var changed = SetModuleMembership(enabled: true);
        if (legacyAutoMiniCactpotField != null && legacyAutoMiniCactpotField.GetValue(configuration) is not true)
        {
            legacyAutoMiniCactpotField.SetValue(configuration, true);
            changed = true;
        }

        return SaveIfChanged(changed);
    }

    public SaucyConfigurationChange RestoreMiniCactpot(SaucyMiniCactpotConfigurationSnapshot snapshot)
    {
        var changed = SetModuleMembership(snapshot.ModuleEnabled);
        if (legacyAutoMiniCactpotField != null &&
            snapshot.LegacyAutoMiniCactpotEnabled is { } legacyEnabled &&
            (bool)legacyAutoMiniCactpotField.GetValue(configuration)! != legacyEnabled)
        {
            legacyAutoMiniCactpotField.SetValue(configuration, legacyEnabled);
            changed = true;
        }

        return SaveIfChanged(changed);
    }

    private bool SetModuleMembership(bool enabled)
    {
        if (enabled)
        {
            if (enabledModules.Contains(MiniCactpotModuleName))
                return false;

            enabledModules.Add(MiniCactpotModuleName);
            return true;
        }

        var changed = false;
        while (enabledModules.Contains(MiniCactpotModuleName))
        {
            enabledModules.Remove(MiniCactpotModuleName);
            changed = true;
        }

        return changed;
    }

    private SaucyConfigurationChange SaveIfChanged(bool changed)
    {
        if (!changed || saveMethod == null)
            return new SaucyConfigurationChange(changed, null);

        try
        {
            saveMethod.Invoke(configuration, null);
            return new SaucyConfigurationChange(true, null);
        }
        catch (Exception ex)
        {
            return new SaucyConfigurationChange(true, ex);
        }
    }
}

internal readonly record struct SaucyMiniCactpotConfigurationSnapshot(
    bool ModuleEnabled,
    bool? LegacyAutoMiniCactpotEnabled);

internal readonly record struct SaucyConfigurationChange(bool StateChanged, Exception? SaveError);
