using System;
using System.Reflection;

namespace VERMAXION.Services;

internal sealed class AutoHookConfigurationAccessor
{
    private const string AutoOceanFishMemberName = "AutoOceanFish";

    private readonly object configuration;
    private readonly PropertyInfo? autoOceanFishProperty;
    private readonly FieldInfo? autoOceanFishField;
    private readonly MethodInfo saveMethod;

    private AutoHookConfigurationAccessor(
        object configuration,
        PropertyInfo? autoOceanFishProperty,
        FieldInfo? autoOceanFishField,
        MethodInfo saveMethod)
    {
        this.configuration = configuration;
        this.autoOceanFishProperty = autoOceanFishProperty;
        this.autoOceanFishField = autoOceanFishField;
        this.saveMethod = saveMethod;
    }

    public static bool TryCreate(
        object configuration,
        out AutoHookConfigurationAccessor accessor,
        out string status)
    {
        accessor = null!;
        var configurationType = configuration.GetType();
        const BindingFlags instanceFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        var property = configurationType.GetProperty(AutoOceanFishMemberName, instanceFlags);
        if (property != null &&
            (property.PropertyType != typeof(bool) || !property.CanRead || !property.CanWrite))
        {
            status = "AutoHook config property AutoOceanFish is not a readable and writable Boolean.";
            return false;
        }

        var field = property == null
            ? configurationType.GetField(AutoOceanFishMemberName, instanceFlags)
            : null;
        if (property == null &&
            (field == null || field.FieldType != typeof(bool) || field.IsInitOnly || field.IsLiteral))
        {
            status = "AutoHook config field AutoOceanFish is not a writable Boolean.";
            return false;
        }

        var save = configurationType.GetMethod(
            "Save",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);
        if (save == null)
        {
            status = "AutoHook config static Save() method is not available.";
            return false;
        }

        accessor = new AutoHookConfigurationAccessor(configuration, property, field, save);
        status = "AutoHook AutoOceanFish configuration is available.";
        return true;
    }

    public AutoHookConfigurationSyncResult Read()
    {
        try
        {
            return new AutoHookConfigurationSyncResult(
                Success: true,
                Changed: false,
                Value: ReadValue(),
                Status: "AutoHook AutoOceanFish setting read successfully.");
        }
        catch (Exception ex)
        {
            return Failure("Could not read AutoHook AutoOceanFish", ex);
        }
    }

    public AutoHookConfigurationSyncResult Synchronize(bool expected)
    {
        bool current;
        try
        {
            current = ReadValue();
            if (current == expected)
            {
                return new AutoHookConfigurationSyncResult(
                    Success: true,
                    Changed: false,
                    Value: current,
                    Status: $"AutoHook AutoOceanFish is already {(expected ? "enabled" : "disabled")}.");
            }

            WriteValue(expected);
            saveMethod.Invoke(null, null);
            return new AutoHookConfigurationSyncResult(
                Success: true,
                Changed: true,
                Value: expected,
                Status: $"AutoHook AutoOceanFish was {(expected ? "enabled" : "disabled")} and saved.");
        }
        catch (Exception ex)
        {
            return Failure($"Could not set and save AutoHook AutoOceanFish={(expected ? "on" : "off")}", ex);
        }
    }

    private bool ReadValue()
        => autoOceanFishProperty != null
            ? (bool)autoOceanFishProperty.GetValue(configuration)!
            : (bool)autoOceanFishField!.GetValue(configuration)!;

    private void WriteValue(bool value)
    {
        if (autoOceanFishProperty != null)
            autoOceanFishProperty.SetValue(configuration, value);
        else
            autoOceanFishField!.SetValue(configuration, value);
    }

    private static AutoHookConfigurationSyncResult Failure(string prefix, Exception exception)
    {
        var detail = exception is TargetInvocationException { InnerException: not null } invocation
            ? invocation.InnerException!.Message
            : exception.Message;
        return new AutoHookConfigurationSyncResult(false, false, null, $"{prefix}: {detail}");
    }
}

internal readonly record struct AutoHookConfigurationSyncResult(
    bool Success,
    bool Changed,
    bool? Value,
    string Status);
